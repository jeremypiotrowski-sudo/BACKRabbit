using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose.Rescue;

public class RescueOrchestrator
{
    private readonly FirehoseClient _client;
    private readonly string _backupDir;

    public RescueOrchestrator(FirehoseClient client, string backupDir)
    {
        _client = client;
        _backupDir = backupDir;
    }

    public async Task<RescueReport> RunFullRescueAsync(CancellationToken ct = default)
    {
        var report = new RescueReport();

        // [0/7] Verify backup directory
        Console.WriteLine("\n[0/7] Verifying firmware backup...");
        var requiredPartitions = new[] { "boot.img", "boot_a.img", "init_boot.img", "vbmeta.img", "vbmeta_a.img" };
        var missingCritical = new List<string>();
        if (!string.IsNullOrEmpty(_backupDir) && Directory.Exists(_backupDir))
        {
            foreach (var required in requiredPartitions)
            {
                if (!File.Exists(Path.Combine(_backupDir, required)))
                    missingCritical.Add(required);
            }
            if (missingCritical.Count > 0)
                Console.WriteLine($"  Warning: Missing critical partitions: {string.Join(", ", missingCritical)}");
            else
                Console.WriteLine($"  Backup directory OK: {_backupDir}");
        }
        else
        {
            Console.WriteLine("  No backup directory — diagnosis-only mode.");
        }

        // [1/7] Diagnose
        Console.WriteLine("\n[1/7] Diagnosing partitions...");
        var diagnostics = new PartitionDiagnostics(_client, report, _backupDir);
        await diagnostics.RunAsync(ct);
        Console.WriteLine($"  Done. {report.Partitions.Count} partitions analyzed. Verdict: {report.Verdict}");

        // [2/7] Fuse audit
        Console.WriteLine("\n[2/7] Auditing QFuses...");
        var fuseAuditor = new QFuseAuditor(_client);
        report.FuseAudit = await fuseAuditor.AuditAsync(ct);
        Console.WriteLine($"  Done. {report.FuseAudit.TotalBlown}/{report.FuseAudit.TotalAvailable} fuses blown.");

        // [3/7] Restore tampered partitions
        Console.WriteLine("\n[3/7] Restoring tampered partitions...");
        var tampered = report.Partitions
            .Where(p => p.Status == "Tampered")
            .Select(p => p.PartitionName)
            .ToList();

        if (tampered.Count > 0)
        {
            var restorer = new PartitionRestorer(_client, _backupDir, report);
            await restorer.RestoreAsync(tampered, ct);
            Console.WriteLine($"  Done. {report.RestoreActions.Count} actions taken.");
        }
        else
        {
            Console.WriteLine("  No tampered partitions to restore.");
        }

        // [4/7] Remove Magisk
        Console.WriteLine("\n[4/7] Removing Magisk...");
        var magiskTampered = report.Partitions
            .Where(p => p.Anomalies.Any(a => a.Contains("Magisk")))
            .Select(p => p.PartitionName)
            .ToList();

        if (magiskTampered.Count > 0)
        {
            var remover = new MagiskRemover(_client, _backupDir, report);
            await remover.RemoveAllAsync(ct);
            Console.WriteLine($"  Done. {report.MagiskRemovals.Count} boot partitions processed.");
        }
        else
        {
            Console.WriteLine("  No Magisk detected in boot partitions.");
        }

        // [5/7] Final verification
        Console.WriteLine("\n[5/7] Final verification...");
        var verifyReport = new RescueReport();
        var verifyDiagnostics = new PartitionDiagnostics(_client, verifyReport, _backupDir);
        await verifyDiagnostics.RunAsync(ct);

        // Update original report with post-rescue status
        foreach (var vp in verifyReport.Partitions)
        {
            var orig = report.Partitions.Find(p => p.PartitionName == vp.PartitionName);
            if (orig != null && orig.Status == "Tampered" && vp.Status == "Normal")
            {
                orig.Status = "Normal (restored)";
                orig.Anomalies.Clear();
                orig.Anomalies.Add("Restored successfully");
            }
        }

        // Recalculate verdict
        report.Verdict = DeterminePostRescueVerdict(report);
        Console.WriteLine($"  Post-rescue verdict: {report.Verdict}");

        // [6/7] Generate report
        Console.WriteLine("\n[6/7] Generating rescue report...");
        var reportPath = Path.Combine(_backupDir, "rescue-report.json");
        await File.WriteAllTextAsync(reportPath, report.ToJson(), ct);
        Console.WriteLine($"  Report saved to: {reportPath}");

        // Print summary
        report.PrintSummary();

        // Reset device
        Console.WriteLine("\nResetting device to system...");
        try
        {
            await _client.ResetAsync("system", ct);
        }
        catch
        {
            Console.WriteLine("  Reset command sent (device may already be rebooting)");
        }

        return report;
    }

    private static OverallVerdict DeterminePostRescueVerdict(RescueReport report)
    {
        bool anyStillTampered = report.Partitions.Any(p => p.Status == "Tampered");
        bool anyPermanent = report.FuseAudit?.TotalBlown > 0;
        bool allRestored = report.RestoreActions.Count > 0
            && report.RestoreActions.All(a => a.Verified || a.Action == "Skipped");

        if (!anyStillTampered && !anyPermanent) return OverallVerdict.FullyRecovered;
        if (anyStillTampered && !anyPermanent) return OverallVerdict.PartiallyRecovered;
        if (anyPermanent && !anyStillTampered) return OverallVerdict.PartiallyRecovered;
        if (anyPermanent && anyStillTampered) return OverallVerdict.PermanentDamage;
        return OverallVerdict.PartiallyRecovered;
    }
}
