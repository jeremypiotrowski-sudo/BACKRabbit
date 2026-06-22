using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BACKRabbit.Protocol.Firehose.Rescue;

public enum OverallVerdict
{
    Clean,
    Tampered,
    PartiallyRecovered,
    FullyRecovered,
    PermanentDamage,
    Aborted
}

public class RescueReport
{
    public bool IsDryRun { get; set; }
    public DeviceInfo Device { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<PartitionDiagnosis> Partitions { get; set; } = new();
    public QFuseAuditResult? FuseAudit { get; set; }
    public List<RestoreAction> RestoreActions { get; set; } = new();
    public List<MagiskRemovalResult> MagiskRemovals { get; set; } = new();
    public FullGptAuditResult? FullGptAudit { get; set; }
    public SparseRepairResult? SparseRepair { get; set; }
    public WipeDataResult? WipeData { get; set; }
    public OverallVerdict Verdict { get; set; }
    public List<string> Recommendations { get; set; } = new();

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    public void PrintSummary()
    {
        Console.WriteLine($"\n=== BACKRabbit Rescue Report ===");
        if (IsDryRun)
            Console.WriteLine($"🔥 DRY-RUN MODE — No partitions were modified");
        Console.WriteLine($"Device: {Device.MsmId:X8}  SoC: {Device.SocModel}  Storage: {Device.StorageType}");
        Console.WriteLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"Verdict: {Verdict}");
        Console.WriteLine($"\n--- Partitions ({Partitions.Count}) ---");
        Console.WriteLine($"{"Name",-20} {"Status",-12} {"Anomalies",-30}");
        Console.WriteLine(new string('-', 62));
        foreach (var p in Partitions)
            Console.WriteLine($"{p.PartitionName,-20} {p.Status,-12} {string.Join(", ", p.Anomalies),-30}");
        if (FuseAudit != null)
        {
            Console.WriteLine($"\n--- QFuse Audit ---");
            Console.WriteLine($"Blown: {FuseAudit.TotalBlown}/{FuseAudit.TotalAvailable}");
            foreach (var f in FuseAudit.Fuses)
                if (f.IsBlown) Console.WriteLine($"  {f.FuseName}: {f.Implication}");
        }
        if (RestoreActions.Count > 0)
        {
            Console.WriteLine($"\n--- Restore Actions ({RestoreActions.Count}) ---");
            foreach (var r in RestoreActions)
                Console.WriteLine($"  {r.PartitionName}: {r.Action} (verified={r.Verified})");
        }
        if (MagiskRemovals.Count > 0)
        {
            Console.WriteLine($"\n--- Magisk Removal ---");
            foreach (var m in MagiskRemovals)
                Console.WriteLine($"  {m.BootPartition}: found={m.MagiskFound} removed={m.MagiskRemoved} vbmeta={m.VbmetaRestored}");
        }
        if (Recommendations.Count > 0)
        {
            Console.WriteLine($"\n--- Recommendations ---");
            foreach (var r in Recommendations) Console.WriteLine($"  * {r}");
        }
    }
}

public class DeviceInfo
{
    public uint MsmId { get; set; }
    public string SocModel { get; set; } = "unknown";
    public string StorageType { get; set; } = "unknown";
    public bool IsFused { get; set; }
    public string? SerialNumber { get; set; }
}

public class PartitionDiagnosis
{
    public string PartitionName { get; set; } = "";
    public int Lun { get; set; }
    public ulong StartSector { get; set; }
    public ulong Sectors { get; set; }
    public string Status { get; set; } = "Unknown"; // Normal, Tampered, Missing, Unknown
    public string? ExpectedHash { get; set; }
    public string? ActualHash { get; set; }
    public List<string> Anomalies { get; set; } = new();
    [JsonIgnore] public byte[]? RawData { get; set; }
}

public class QFuseAuditResult
{
    public List<QFuseStatus> Fuses { get; set; } = new();
    public int TotalBlown { get; set; }
    public int TotalAvailable { get; set; }
    public List<string> PermanentDamageWarnings { get; set; } = new();
}

public class QFuseStatus
{
    public string FuseName { get; set; } = "";
    public uint Address { get; set; }
    public int BitNumber { get; set; }
    public bool IsBlown { get; set; }
    public string Description { get; set; } = "";
    public string Implication { get; set; } = "";
}

public class RestoreAction
{
    public string PartitionName { get; set; } = "";
    public string Action { get; set; } = ""; // Erased, Flashed, Skipped
    public string? SourceFile { get; set; }
    public bool Verified { get; set; }
    public string? PreRestoreHash { get; set; }
    public string? PostRestoreHash { get; set; }
}

public class MagiskRemovalResult
{
    public string BootPartition { get; set; } = "";
    public bool MagiskFound { get; set; }
    public bool MagiskRemoved { get; set; }
    public string? OriginalHash { get; set; }
    public string? CleanHash { get; set; }
    public bool VbmetaRestored { get; set; }
}
