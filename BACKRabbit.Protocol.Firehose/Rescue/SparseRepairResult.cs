using System.Collections.Generic;

namespace BACKRabbit.Protocol.Firehose.Rescue;

/// <summary>
/// Result of a sparse repair operation — block-level normalization of
/// mismatched partitions by writing only the sectors that differ from stock.
/// </summary>
public class SparseRepairResult
{
    public List<SparsePartitionResult> Partitions { get; set; } = new();
    public bool DryRun { get; set; }
    public int TotalSectorsWritten => DryRun ? 0 : Partitions.FindAll(p => p.Status == SparseRepairStatus.Repaired).Sum(p => p.SectorsWritten);
    public int TotalPartitionsRepaired => Partitions.FindAll(p => p.Status == SparseRepairStatus.Repaired).Count;
    public int TotalPartitionsFailed => Partitions.FindAll(p => p.Status == SparseRepairStatus.VerificationFailed).Count;
    public int TotalPartitionsSkipped => Partitions.FindAll(p => p.Status == SparseRepairStatus.Skipped || p.Status == SparseRepairStatus.Blocklisted || p.Status == SparseRepairStatus.NoStockImage).Count;
}

public enum SparseRepairStatus
{
    Repaired,
    Skipped,
    VerificationFailed,
    Blocklisted,
    NoStockImage
}

public class SparsePartitionResult
{
    public string PartitionName { get; set; } = "";
    public SparseRepairStatus Status { get; set; }
    public int SectorsWritten { get; set; }
    public int SectorsVerified { get; set; }
    public List<long> WrittenSectorAddresses { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public override string ToString() =>
        $"{PartitionName}: {Status}" + (SectorsWritten > 0 ? $" ({SectorsWritten} sectors written)" : "");
}