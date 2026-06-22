using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BACKRabbit.Protocol.Firehose;

namespace BACKRabbit.Protocol.Firehose.Tests;

/// <summary>
/// Mock FirehoseClient for unit testing the rescue pipeline.
/// Records all calls, tracks destructive operations, and returns configurable responses.
/// Unified mock — supports both CallLog-based tracking (used by FullGptAudit, Integration,
/// SparseWrite, StockOnlyWrite, WipeData tests) and simple list-based tracking
/// (used by RescueOrchestratorTests).
/// </summary>
public class MockFirehoseClient : IFirehoseClient
{
    // ─── Call Recording (structured) ──────────────────────────
    public List<CallRecord> CallLog { get; } = new();

    public class CallRecord
    {
        public string MethodName { get; set; } = "";
        public object?[] Arguments { get; set; } = Array.Empty<object?>();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public override string ToString() => $"{MethodName}({string.Join(", ", Arguments)})";
    }

    // ─── Simple Call Tracking (for RescueOrchestratorTests) ────
    public List<string> WriteCalls { get; } = new();
    public List<(string Partition, long StartSector, int DataLength)> WriteBlocksCalls { get; } = new();
    public int WriteBlocksCallCount => WriteBlocksCalls.Count;
    public List<string> EraseCalls { get; } = new();
    public List<string> ResetCalls { get; } = new();
    public List<string> ReadCalls { get; } = new();

    // ─── Destructive Call Tracking ───────────────────────────
    public int DestructiveCallCount =>
        CallLog.Count(c => c.MethodName is nameof(WritePartitionAsync)
            or nameof(WritePartitionBlocksAsync)
            or nameof(ErasePartitionAsync) or nameof(ResetAsync));

    public bool WasDestructiveMethodCalled(string methodName) =>
        CallLog.Any(c => c.MethodName == methodName);

    public bool WasCalled(string methodName) =>
        CallLog.Any(c => c.MethodName == methodName);

    public int CallCount(string methodName) =>
        CallLog.Count(c => c.MethodName == methodName);

    public void Reset()
    {
        CallLog.Clear();
        WriteCalls.Clear();
        WriteBlocksCalls.Clear();
        EraseCalls.Clear();
        ResetCalls.Clear();
        ReadCalls.Clear();
        _readPartitionResponses.Clear();
        ReadPartitionOverrides.Clear();
        PartitionData.Clear();
        _gptResponses.Clear();
        _peekResponses.Clear();
        _storageInfoResponse = "ufs";
        _writeResult = true;
        _writeBlocksResult = true;
        _eraseResult = true;
        _chipInfo = null;
        GptEntries.Clear();
        FuseData.Clear();
        ChipInfoOverride = null;
    }

    // ─── Configurable Responses ──────────────────────────────

    private readonly Dictionary<string, byte[]> _readPartitionResponses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<GptPartitionEntry>> _gptResponses = new();
    private readonly Dictionary<uint, byte[]> _peekResponses = new();
    private string _storageInfoResponse = "ufs";
    private bool _writeResult = true;
    private bool _writeBlocksResult = true;
    private bool _eraseResult = true;
    private bool _eraseSimulatesWipe = false;
    private int _userdataSectorCount = 2048;  // default 1MB if --wipe-data simulated
    private SaharaChipInfo? _chipInfo;

    // ─── RescueOrchestratorTests-style configurable properties ─
    public Dictionary<string, byte[]> PartitionData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> ReadPartitionOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GptPartitionEntry> GptEntries { get; set; } = new();
    public string StorageType { get; set; } = "ufs";
    public Dictionary<uint, byte[]> FuseData { get; set; } = new();
    public SaharaChipInfo? ChipInfoOverride { get; set; }

    // ─── IFirehoseClient.ChipInfo ────────────────────────────
    public SaharaChipInfo? ChipInfo => ChipInfoOverride ?? _chipInfo;

    /// <summary>
    /// When true, calling ErasePartitionAsync("userdata") will:
    /// 1. Record the erase call
    /// 2. Replace subsequent ReadPartitionAsync("userdata") returns with all-zero data
    /// (simulating a successful block-level wipe).
    /// </summary>
    public void SetEraseSimulatesWipe(bool enabled, int userdataSectorCount = 2048) =>
        (_eraseSimulatesWipe, _userdataSectorCount) = (enabled, userdataSectorCount);

    /// <summary>
    /// Configure the readback data for userdata partition (overrides the wipe simulation).
    /// Useful for simulating a failed wipe (non-zero data after erase).
    /// </summary>
    public void SetUserdataReadback(byte[] data) =>
        _readPartitionResponses["userdata"] = data;

    public void SetReadPartitionResponse(string partitionName, byte[] data) =>
        _readPartitionResponses[partitionName] = data;

    /// <summary>
    /// Manually set readback data for a partition (e.g. userdata) to simulate non-zero data after erase.
    /// Alias for SetReadPartitionResponse — used by RescueOrchestratorTests.
    /// </summary>
    public void SetReadPartitionOverride(string partitionName, byte[] data) =>
        ReadPartitionOverrides[partitionName] = data;

    public void SetGptResponse(int lun, List<GptPartitionEntry> entries) =>
        _gptResponses[lun] = entries;

    public void SetPeekResponse(uint address, byte[] data) =>
        _peekResponses[address] = data;

    public void SetStorageInfoResponse(string info) =>
        _storageInfoResponse = info;

    public void SetWriteResult(bool result) =>
        _writeResult = result;

    public void SetWriteBlocksResult(bool result) =>
        _writeBlocksResult = result;

    public void SetEraseResult(bool result) =>
        _eraseResult = result;

    public void SetChipInfo(SaharaChipInfo chipInfo) =>
        _chipInfo = chipInfo;

    // ─── IFirehoseClient Implementation ──────────────────────

    public Task<byte[]> ReadPartitionAsync(string partitionName, int lun = 0, int sectorSize = 512, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(ReadPartitionAsync), Arguments = new object?[] { partitionName, lun, sectorSize } });
        ReadCalls.Add(partitionName);

        // 1. ReadPartitionOverrides wins (used for simulating non-zero data after wipe)
        if (ReadPartitionOverrides.TryGetValue(partitionName, out var overrideData))
            return Task.FromResult(overrideData);

        // 2. _readPartitionResponses (set via SetReadPartitionResponse)
        if (_readPartitionResponses.TryGetValue(partitionName, out var data))
            return Task.FromResult(data);

        // 3. PartitionData (set directly by RescueOrchestratorTests)
        if (PartitionData.TryGetValue(partitionName, out var pd))
            return Task.FromResult(pd);

        // Return empty data for unknown partitions
        return Task.FromResult(Array.Empty<byte>());
    }

    /// <summary>
    /// Helper to get the current userdata readback (used after --wipe-data simulation).
    /// Returns all-zero bytes of userdataSectorCount * 512 size if _eraseSimulatesWipe is enabled,
    /// or whatever was set via SetUserdataReadback.
    /// </summary>
    public byte[] GetUserdataReadback()
    {
        if (_readPartitionResponses.TryGetValue("userdata", out var configured))
            return configured;
        if (ReadPartitionOverrides.TryGetValue("userdata", out var overrideData))
            return overrideData;
        if (PartitionData.TryGetValue("userdata", out var pd))
            return pd;
        if (_eraseSimulatesWipe)
            return new byte[_userdataSectorCount * 512];
        return Array.Empty<byte>();
    }

    public Task<List<GptPartitionEntry>> PrintGptAsync(int lun = 0, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(PrintGptAsync), Arguments = new object?[] { lun } });

        // GptEntries takes priority (set directly by RescueOrchestratorTests)
        if (GptEntries.Count > 0)
            return Task.FromResult(GptEntries);

        if (_gptResponses.TryGetValue(lun, out var entries))
            return Task.FromResult(entries);

        // Return empty GPT by default
        return Task.FromResult(new List<GptPartitionEntry>());
    }

    public Task<byte[]> PeekAsync(uint address, uint size, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(PeekAsync), Arguments = new object?[] { address, size } });

        // FuseData takes priority (set directly by RescueOrchestratorTests)
        if (FuseData.TryGetValue(address, out var fuseData))
            return Task.FromResult(fuseData.Take((int)size).ToArray());

        if (_peekResponses.TryGetValue(address, out var data))
            return Task.FromResult(data);

        // Return zeros for unknown addresses
        return Task.FromResult(new byte[size]);
    }

    public Task<string> GetStorageInfoAsync(CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(GetStorageInfoAsync), Arguments = Array.Empty<object?>() });

        // StorageType takes priority (set directly by RescueOrchestratorTests)
        if (!string.IsNullOrEmpty(StorageType) && StorageType != "ufs")
            return Task.FromResult(StorageType);

        return Task.FromResult(_storageInfoResponse);
    }

    public Task<bool> WritePartitionAsync(string partitionName, byte[] data, int lun = 0, int sectorSize = 512, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(WritePartitionAsync), Arguments = new object?[] { partitionName, data.Length, lun, sectorSize } });
        WriteCalls.Add(partitionName);

        // Store in PartitionData for RescueOrchestratorTests
        PartitionData[partitionName] = data;

        return Task.FromResult(_writeResult);
    }

    public Task<bool> WritePartitionBlocksAsync(string partitionName, byte[] data, long startSector, int lun = 0, int sectorSize = 512, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(WritePartitionBlocksAsync), Arguments = new object?[] { partitionName, data.Length, startSector, lun, sectorSize } });
        WriteBlocksCalls.Add((partitionName, startSector, data.Length));

        // Simulate the write: update partition data at the specified offset
        if (PartitionData.TryGetValue(partitionName, out var existing))
        {
            var updated = new byte[existing.Length];
            Array.Copy(existing, updated, existing.Length);
            int byteOffset = (int)(startSector * sectorSize);
            if (byteOffset + data.Length <= updated.Length)
            {
                Array.Copy(data, 0, updated, byteOffset, data.Length);
                PartitionData[partitionName] = updated;
            }
        }
        else if (_readPartitionResponses.TryGetValue(partitionName, out var existing2))
        {
            var updated = new byte[existing2.Length];
            Array.Copy(existing2, updated, existing2.Length);
            int byteOffset = (int)(startSector * sectorSize);
            if (byteOffset + data.Length <= updated.Length)
            {
                Array.Copy(data, 0, updated, byteOffset, data.Length);
                _readPartitionResponses[partitionName] = updated;
            }
        }

        return Task.FromResult(_writeBlocksResult);
    }

    public Task<bool> ErasePartitionAsync(string partitionName, int lun = 0, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(ErasePartitionAsync), Arguments = new object?[] { partitionName, lun } });
        EraseCalls.Add(partitionName);

        // If --wipe-data simulation is enabled and we just erased userdata,
        // populate the readback response with all-zero bytes (simulating successful wipe).
        if (_eraseSimulatesWipe && partitionName.Equals("userdata", StringComparison.OrdinalIgnoreCase))
        {
            _readPartitionResponses["userdata"] = new byte[_userdataSectorCount * 512];
            PartitionData["userdata"] = new byte[_userdataSectorCount * 512];
        }

        return Task.FromResult(_eraseResult);
    }

    public Task ResetAsync(string mode = "system", CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(ResetAsync), Arguments = new object?[] { mode } });
        ResetCalls.Add(mode);
        return Task.CompletedTask;
    }
}