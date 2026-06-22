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
/// </summary>
public class MockFirehoseClient : IFirehoseClient
{
    // ─── Call Recording ──────────────────────────────────────
    public List<CallRecord> CallLog { get; } = new();

    public class CallRecord
    {
        public string MethodName { get; set; } = "";
        public object?[] Arguments { get; set; } = Array.Empty<object?>();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public override string ToString() => $"{MethodName}({string.Join(", ", Arguments)})";
    }

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
        _readPartitionResponses.Clear();
        _gptResponses.Clear();
        _peekResponses.Clear();
        _storageInfoResponse = "ufs";
        _writeResult = true;
        _writeBlocksResult = true;
        _eraseResult = true;
        _chipInfo = null;
    }

    // ─── Configurable Responses ──────────────────────────────

    private readonly Dictionary<string, byte[]> _readPartitionResponses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<GptPartitionEntry>> _gptResponses = new();
    private readonly Dictionary<uint, byte[]> _peekResponses = new();
    private string _storageInfoResponse = "ufs";
    private bool _writeResult = true;
    private bool _writeBlocksResult = true;
    private bool _eraseResult = true;
    private SaharaChipInfo? _chipInfo;

    public void SetReadPartitionResponse(string partitionName, byte[] data) =>
        _readPartitionResponses[partitionName] = data;

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

    public SaharaChipInfo? ChipInfo => _chipInfo;

    public Task<byte[]> ReadPartitionAsync(string partitionName, int lun = 0, int sectorSize = 512, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(ReadPartitionAsync), Arguments = new object?[] { partitionName, lun, sectorSize } });

        if (_readPartitionResponses.TryGetValue(partitionName, out var data))
            return Task.FromResult(data);

        // Return empty data for unknown partitions
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<List<GptPartitionEntry>> PrintGptAsync(int lun = 0, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(PrintGptAsync), Arguments = new object?[] { lun } });

        if (_gptResponses.TryGetValue(lun, out var entries))
            return Task.FromResult(entries);

        // Return empty GPT by default
        return Task.FromResult(new List<GptPartitionEntry>());
    }

    public Task<byte[]> PeekAsync(uint address, uint size, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(PeekAsync), Arguments = new object?[] { address, size } });

        if (_peekResponses.TryGetValue(address, out var data))
            return Task.FromResult(data);

        // Return zeros for unknown addresses
        return Task.FromResult(new byte[size]);
    }

    public Task<string> GetStorageInfoAsync(CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(GetStorageInfoAsync), Arguments = Array.Empty<object?>() });
        return Task.FromResult(_storageInfoResponse);
    }

    public Task<bool> WritePartitionAsync(string partitionName, byte[] data, int lun = 0, int sectorSize = 512, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(WritePartitionAsync), Arguments = new object?[] { partitionName, data.Length, lun, sectorSize } });
        return Task.FromResult(_writeResult);
    }

    public Task<bool> WritePartitionBlocksAsync(string partitionName, byte[] data, long startSector, int lun = 0, int sectorSize = 512, CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(WritePartitionBlocksAsync), Arguments = new object?[] { partitionName, data.Length, startSector, lun, sectorSize } });

        // Simulate the write: update partition data at the specified offset
        if (_readPartitionResponses.TryGetValue(partitionName, out var existing))
        {
            var updated = new byte[existing.Length];
            Array.Copy(existing, updated, existing.Length);
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
        return Task.FromResult(_eraseResult);
    }

    public Task ResetAsync(string mode = "system", CancellationToken ct = default)
    {
        CallLog.Add(new CallRecord { MethodName = nameof(ResetAsync), Arguments = new object?[] { mode } });
        return Task.CompletedTask;
    }
}