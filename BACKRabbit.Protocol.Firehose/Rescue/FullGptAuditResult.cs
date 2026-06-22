using System.Collections.Generic;

namespace BACKRabbit.Protocol.Firehose.Rescue;

/// <summary>
/// Result of a full-GPT audit comparing every partition on the device
/// against the stock firmware backup directory.
/// </summary>
public class FullGptAuditResult
{
    public List<GptPartitionAuditEntry> Entries { get; set; } = new();
    public int TotalPartitions => Entries.Count;
    public int MatchCount => Entries.FindAll(e => e.Status == GptAuditStatus.Match).Count;
    public int MismatchCount => Entries.FindAll(e => e.Status == GptAuditStatus.Mismatch).Count;
    public int NoStockComparisonCount => Entries.FindAll(e => e.Status == GptAuditStatus.NoStockComparison).Count;
    public int ErrorCount => Entries.FindAll(e => e.Status == GptAuditStatus.Error).Count;
}

public enum GptAuditStatus
{
    Match,
    Mismatch,
    NoStockComparison,
    Error
}

public class GptPartitionAuditEntry
{
    public string PartitionName { get; set; } = "";
    public GptAuditStatus Status { get; set; }
    public long PartitionSize { get; set; }
    public string? DeviceSha256 { get; set; }
    public string? StockSha256 { get; set; }
    public int DifferingSectorCount { get; set; }
    public List<long> DifferingSectorAddresses { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public override string ToString() =>
        $"{PartitionName}: {Status}" + (DifferingSectorCount > 0 ? $" ({DifferingSectorCount} sectors differ)" : "");
}