using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose;

/// <summary>
/// Interface for FirehoseClient — enables mock-based testing of the rescue pipeline.
/// Includes only methods actually called by RescueOrchestrator, PartitionRestorer,
/// PartitionDiagnostics, MagiskRemover, and QFuseAuditor.
/// </summary>
public interface IFirehoseClient
{
    // ─── Chip Info (read-only) ───────────────────────────────
    SaharaChipInfo? ChipInfo { get; }

    // ─── Read Operations (non-destructive) ────────────────────
    Task<byte[]> ReadPartitionAsync(string partitionName, int lun = 0, int sectorSize = 512, CancellationToken ct = default);
    Task<List<GptPartitionEntry>> PrintGptAsync(int lun = 0, CancellationToken ct = default);
    Task<byte[]> PeekAsync(uint address, uint size, CancellationToken ct = default);
    Task<string> GetStorageInfoAsync(CancellationToken ct = default);

    // ─── Write/Erase Operations (DESTRUCTIVE) ─────────────────
    Task<bool> WritePartitionAsync(string partitionName, byte[] data, int lun = 0, int sectorSize = 512, CancellationToken ct = default);

    /// <summary>
    /// Write data to specific sectors within a partition (sparse/offset write).
    /// Uses the firehose <program> command with start_sector set to the
    /// partition's absolute start_sector + the provided startSector offset.
    /// CONVENTION: startSector is a SECTOR NUMBER (not byte offset), matching
    /// the firehose protocol's start_sector parameter.
    /// data.Length should be a multiple of sectorSize.
    /// </summary>
    Task<bool> WritePartitionBlocksAsync(string partitionName, byte[] data, long startSector, int lun = 0, int sectorSize = 512, CancellationToken ct = default);

    Task<bool> ErasePartitionAsync(string partitionName, int lun = 0, CancellationToken ct = default);

    // ─── Device Control (DESTRUCTIVE) ─────────────────────────
    Task ResetAsync(string mode = "system", CancellationToken ct = default);
}