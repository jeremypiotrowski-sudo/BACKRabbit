using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose.Rescue;

public class RescueOrchestrator
{
    private readonly IFirehoseClient _client;
    private readonly string _backupDir;
    private readonly bool _dryRun;
    private readonly bool _force;
    private readonly bool _skipDownloadModeCheck;
    private readonly bool _wipeData;

    public RescueOrchestrator(IFirehoseClient client, string backupDir, bool dryRun = false, bool force = false, bool skipDownloadModeCheck = false, bool wipeData = false)
    {
        _client = client;
        _backupDir = backupDir;
        _dryRun = dryRun;
        _force = force;
        _skipDownloadModeCheck = skipDownloadModeCheck;
        _wipeData = wipeData;
    }

    public async Task<RescueReport> RunFullRescueAsync(CancellationToken ct = default)
    {
        var report = new RescueReport { IsDryRun = _dryRun };

        if (_dryRun)
        {
            Console.WriteLine("\n🔥 FIREHOSE DRY-RUN MODE ENABLED — NO FLASHING WILL OCCUR");
            Console.WriteLine("   All diagnosis, detection, and verification will run normally.");
            Console.WriteLine("   Write/erase operations will be logged but skipped.");
        }

        // [0/7] Ensure device is in Download Mode
        Console.WriteLine("\n[0/7] Ensuring device is in Download Mode (button-guided if needed)...");
        if (_skipDownloadModeCheck)
        {
            Console.WriteLine("  [SKIPPED] --skip-dl-mode-check specified — assuming device is already in Download Mode");
        }
        else
        {
            var inDownloadMode = await RebootDownloadManager.TryRebootToDownloadModeAsync(
                adbClient: null,
                skipInteractive: false,
                ct: ct);

            if (!inDownloadMode)
            {
                Console.WriteLine("❌ Cannot proceed without Download Mode. Aborting rescue.");
                report.Verdict = OverallVerdict.Aborted;
                return report;
            }
        }

        // [1/7] Verify backup directory
        Console.WriteLine("\n[1/7] Verifying firmware backup...");
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

        // [2/7] Diagnose — full-GPT audit + critical partition analysis
        Console.WriteLine("\n[2/7] Diagnosing partitions (full-GPT audit)...");
        var diagnostics = new PartitionDiagnostics(_client, report, _backupDir);

        // Run full-GPT audit first (compares ALL partitions against stock)
        var gptAudit = await diagnostics.RunFullGptAuditAsync(ct);
        report.FullGptAudit = gptAudit;
        Console.WriteLine($"  Full-GPT audit: {gptAudit.TotalPartitions} partitions, {gptAudit.MatchCount} match, {gptAudit.MismatchCount} mismatch, {gptAudit.NoStockComparisonCount} no stock comparison");

        // Then run the existing critical partition analysis (boot/vbmeta/devinfo/etc.)
        await diagnostics.RunAsync(ct);
        Console.WriteLine($"  Done. {report.Partitions.Count} critical partitions analyzed. Verdict: {report.Verdict}");

        // [3/7] Fuse audit
        Console.WriteLine("\n[3/7] Auditing QFuses...");
        var fuseAuditor = new QFuseAuditor(_client);
        report.FuseAudit = await fuseAuditor.AuditAsync(ct);
        Console.WriteLine($"  Done. {report.FuseAudit.TotalBlown}/{report.FuseAudit.TotalAvailable} fuses blown.");

        // [4/7] Restore tampered partitions — SPARSE REPAIR first, then full restore fallback
        Console.WriteLine("\n[4/7] Restoring tampered partitions (sparse repair)...");
        var tampered = report.Partitions
            .Where(p => p.Status == "Tampered")
            .Select(p => p.PartitionName)
            .ToList();

        // Phase 4a: Sparse repair — write only differing sectors from the full-GPT audit
        if (report.FullGptAudit != null && report.FullGptAudit.MismatchCount > 0)
        {
            Console.WriteLine($"  [SPARSE] Full-GPT audit found {report.FullGptAudit.MismatchCount} mismatched partition(s)");
            if (_dryRun)
            {
                var sparseRestorer = new PartitionRestorer(_client, _backupDir, report, _force, dryRun: true);
                report.SparseRepair = await sparseRestorer.SparseRepairAsync(report.FullGptAudit, ct);
                Console.WriteLine($"  [DRY-RUN] Sparse repair planned: {report.SparseRepair.TotalSectorsWritten} sectors across {report.SparseRepair.TotalPartitionsRepaired} partition(s)");
            }
            else
            {
                var sparseRestorer = new PartitionRestorer(_client, _backupDir, report, _force, dryRun: false);
                report.SparseRepair = await sparseRestorer.SparseRepairAsync(report.FullGptAudit, ct);
                Console.WriteLine($"  [SPARSE] Done. {report.SparseRepair.TotalPartitionsRepaired} repaired, {report.SparseRepair.TotalSectorsWritten} sectors written.");
            }
        }
        else
        {
            Console.WriteLine("  No sparse repair needed (no mismatches in full-GPT audit).");
        }

        // Phase 4b: Full restore fallback for partitions still tampered after sparse repair
        if (tampered.Count > 0)
        {
            if (_dryRun)
            {
                Console.WriteLine($"  [DRY-RUN] Would full-restore {tampered.Count} tampered partition(s) as fallback: {string.Join(", ", tampered)}");
                foreach (var name in tampered)
                {
                    var backupPath = Path.Combine(_backupDir, $"{name}.img");
                    if (File.Exists(backupPath))
                    {
                        var backupSize = new FileInfo(backupPath).Length;
                        Console.WriteLine($"    [DRY-RUN] Would flash {name} from {backupPath} ({backupSize:N0} bytes)");
                        report.RestoreActions.Add(new RestoreAction
                        {
                            PartitionName = name,
                            Action = "DryRunSkipped",
                            SourceFile = backupPath,
                            Verified = false
                        });
                    }
                    else
                    {
                        Console.WriteLine($"    [DRY-RUN] {name}: no backup file - would skip");
                    }
                }
            }
            else
            {
                var restorer = new PartitionRestorer(_client, _backupDir, report, _force);
                await restorer.RestoreAsync(tampered, ct);
                Console.WriteLine($"  Done. {report.RestoreActions.Count} full-restore actions taken.");
            }
        }
        else
        {
            Console.WriteLine("  No full-restore fallback needed.");
        }

        // [4c/7] Optional userdata wipe (--wipe-data)
        Console.WriteLine("\n[4c/7] Checking --wipe-data flag...");
        var wipeRestorer = new PartitionRestorer(_client, _backupDir, report, _force, _dryRun, _wipeData);
        if (!_wipeData)
        {
            Console.WriteLine("  userdata NOT erased (--wipe-data not set).");
            Console.WriteLine("  Partitions with NO_STOCK_COMPARISON remain on device.");
            report.WipeData = new WipeDataResult { Status = WipeDataStatus.WipeNotAuthorized };
        }
        else
        {
            // Dry-run path: log the planned wipe, make ZERO calls
            if (_dryRun)
            {
                Console.WriteLine("  [DRY-RUN] --wipe-data set. Would erase userdata partition at block level.");
                Console.WriteLine("  [DRY-RUN] Would verify post-erase readback shows all zeros.");
                report.WipeData = await wipeRestorer.WipeUserDataAsync(forceConfirmation: "DRY-RUN-AUTO", ct: ct);
            }
            else
            {
                // Second typed confirmation gate inside orchestrator (CLI already gated this once)
                var confirmationString = _client.ChipInfo?.SerialNumber ?? "CONFIRM-WIPE-DATA";
                Console.WriteLine($"  ☢️  --wipe-data confirmed. About to erase userdata partition at block level.");
                Console.WriteLine($"     Type the confirmation token to proceed: {confirmationString}");
                Console.Write($"     > ");
                var typedConfirmation = Console.ReadLine()?.Trim() ?? "";
                report.WipeData = await wipeRestorer.WipeUserDataAsync(forceConfirmation: typedConfirmation, expectedToken: confirmationString, ct: ct);
            }

            // Verification failure ABORTS rescue (skips Phase 5/6/7)
            if (report.WipeData.Status == WipeDataStatus.VerificationFailed)
            {
                Console.WriteLine("  ❌ --wipe-data VERIFICATION FAILED. ABORTING rescue.");
                Console.WriteLine($"     First non-zero sector: {report.WipeData.FirstNonZeroSector}");
                report.Verdict = OverallVerdict.PermanentDamage;
                // Skip remaining phases, generate report and exit
                goto GenerateReport;
            }
        }

        // [5/7] Remove Magisk — SKIP if --wipe-data was used AND userdata verified empty
        Console.WriteLine("\n[5/7] Removing Magisk...");
        bool wipeDataVerifiedEmpty = report.WipeData?.Status == WipeDataStatus.WipedAndVerified;
        if (_wipeData && wipeDataVerifiedEmpty)
        {
            Console.WriteLine("  [SKIPPED] userdata wiped and verified — /data/adb does not exist.");
            Console.WriteLine("             Magisk removal is irrelevant: no persistent Magisk artifacts can survive an empty userdata.");
            // Add a placeholder MagiskRemovalResult so the report shows the skip
            report.MagiskRemovals.Add(new MagiskRemovalResult
            {
                BootPartition = "(skipped: userdata wiped)",
                MagiskFound = false,
                MagiskRemoved = false,
                VbmetaRestored = false,
            });
        }
        else
        {
            var magiskTampered = report.Partitions
                .Where(p => p.Anomalies.Any(a => a.Contains("Magisk")))
                .Select(p => p.PartitionName)
                .ToList();

            if (magiskTampered.Count > 0)
            {
                if (_dryRun)
                {
                    Console.WriteLine($"  [DRY-RUN] Magisk detected in {magiskTampered.Count} partition(s): {string.Join(", ", magiskTampered)}");
                    Console.WriteLine($"  [DRY-RUN] Would parse boot image → detect artifacts → clean ramdisk → repack → flash → verify");
                    foreach (var name in magiskTampered)
                    {
                        report.MagiskRemovals.Add(new MagiskRemovalResult
                        {
                            BootPartition = name,
                            MagiskFound = true,
                            MagiskRemoved = false,
                            VbmetaRestored = false
                        });
                    }
                    Console.WriteLine($"  [DRY-RUN] DRY-RUN PLAN: Flash {string.Join("+", magiskTampered)}, remove Magisk, verify");
                }
                else
                {
                    var remover = new MagiskRemover(_client, _backupDir, report);
                    await remover.RemoveAllAsync(ct);
                    Console.WriteLine($"  Done. {report.MagiskRemovals.Count} boot partitions processed.");
                }
            }
            else
            {
                Console.WriteLine("  No Magisk detected in boot partitions.");
            }
        }  // closes the new else block (--wipe-data skip wrapper)

        // [6/7] Final verification — re-read partitions from device, compare to pre-rescue
        Console.WriteLine("\n[6/7] Final verification...");
        if (_dryRun)
        {
            Console.WriteLine("  [DRY-RUN] Would re-run FullGptAudit to confirm all partitions match stock.");
            Console.WriteLine("  [DRY-RUN] Skipping post-rescue verification (no writes were performed).");
            // In dry-run, verdict stays as diagnosed — no changes were made
            report.PostRescueGptAudit = null; // explicit: no post-rescue readback in dry-run
        }
        else
        {
            // Re-run FullGptAudit to verify mismatched partitions are now matching
            var postAuditDiagnostics = new PartitionDiagnostics(_client, report, _backupDir);
            var postAudit = await postAuditDiagnostics.RunFullGptAuditAsync(ct);
            report.PostRescueGptAudit = postAudit;
            Console.WriteLine($"  Post-rescue GPT audit: {postAudit.TotalPartitions} partitions, {postAudit.MatchCount} match, {postAudit.MismatchCount} mismatch");

            // Also update the legacy per-partition diagnosis for the existing critical-partition flow
            var verifyReport = new RescueReport();
            var verifyDiagnostics = new PartitionDiagnostics(_client, verifyReport, _backupDir);
            await verifyDiagnostics.RunAsync(ct);

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

            // Cross-check: any pre-rescue MISMATCH partition that is still MISMATCH post-rescue?
            if (report.FullGptAudit != null)
            {
                var preMismatch = report.FullGptAudit.Entries
                    .Where(e => e.Status == GptAuditStatus.Mismatch)
                    .Select(e => e.PartitionName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var postMismatch = postAudit.Entries
                    .Where(e => e.Status == GptAuditStatus.Mismatch)
                    .Select(e => e.PartitionName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var stillMismatched = preMismatch.Intersect(postMismatch, StringComparer.OrdinalIgnoreCase).ToList();
                if (stillMismatched.Count > 0)
                {
                    Console.WriteLine($"  ⚠️  {stillMismatched.Count} partition(s) still mismatched post-rescue: {string.Join(", ", stillMismatched)}");
                }
                else if (preMismatch.Count > 0)
                {
                    Console.WriteLine($"  ✅ All {preMismatch.Count} previously-mismatched partition(s) now match stock.");
                }
            }

            // Recalculate verdict
            report.Verdict = DeterminePostRescueVerdict(report);
            Console.WriteLine($"  Post-rescue verdict: {report.Verdict}");
        }

        // [7/7] Generate report
        GenerateReport:
        Console.WriteLine("\n[7/7] Generating rescue report...");
        var reportJson = report.ToJson();
        var reportHash = ComputeSha256(System.Text.Encoding.UTF8.GetBytes(reportJson));

        if (!string.IsNullOrEmpty(_backupDir) && Directory.Exists(_backupDir))
        {
            var reportPath = Path.Combine(_backupDir, "rescue-report.json");
            await File.WriteAllTextAsync(reportPath, reportJson, ct);
            Console.WriteLine($"  Report saved to: {reportPath}");
            Console.WriteLine($"  Report SHA256: {reportHash}");
        }
        else
        {
            // Save to temp directory even without backup dir — audit trail is always valuable
            var fallbackDir = Path.Combine(Path.GetTempPath(), "BACKRabbit", "reports");
            Directory.CreateDirectory(fallbackDir);
            var reportPath = Path.Combine(fallbackDir,
                $"rescue-report_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(reportPath, reportJson, ct);
            Console.WriteLine($"  Report saved to: {reportPath}");
            Console.WriteLine($"  Report SHA256: {reportHash}");
        }

        // Print summary
        report.PrintSummary();

        // Reset device
        if (_dryRun)
        {
            Console.WriteLine("\n[DRY-RUN] Would reset device to system");
            Console.WriteLine("🔥 DRY-RUN COMPLETE — No partitions were modified.");
        }
        else
        {
            Console.WriteLine("\nResetting device to system...");
            try
            {
                await _client.ResetAsync("system", ct);
            }
            catch
            {
                Console.WriteLine("  Reset command sent (device may already be rebooting)");
            }
        }

        return report;
    }

    private static OverallVerdict DeterminePostRescueVerdict(RescueReport report)
    {
        // CRITICAL: WipeData verification failure → PermanentDamage (regardless of other factors)
        if (report.WipeData?.Status == WipeDataStatus.VerificationFailed)
            return OverallVerdict.PermanentDamage;

        // Evidence: any pre-rescue MISMATCH partition that is still MISMATCH post-rescue → rescue failed
        if (report.FullGptAudit != null && report.PostRescueGptAudit != null)
        {
            var preMismatch = report.FullGptAudit.Entries
                .Where(e => e.Status == GptAuditStatus.Mismatch)
                .Select(e => e.PartitionName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var postMismatch = report.PostRescueGptAudit.Entries
                .Where(e => e.Status == GptAuditStatus.Mismatch)
                .Select(e => e.PartitionName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var stillMismatched = preMismatch.Intersect(postMismatch, StringComparer.OrdinalIgnoreCase).Count();
            if (stillMismatched > 0)
                return OverallVerdict.PermanentDamage;
        }

        // Sparse repair failures → not fully recovered
        bool sparseRepairFailed = report.SparseRepair != null
            && report.SparseRepair.TotalPartitionsFailed > 0;

        bool anyStillTampered = report.Partitions.Any(p => p.Status == "Tampered");
        bool anyPermanent = report.FuseAudit?.TotalBlown > 0;

        if (!anyStillTampered && !anyPermanent && !sparseRepairFailed)
            return OverallVerdict.FullyRecovered;
        if (anyStillTampered)
            return OverallVerdict.PermanentDamage;
        if (anyPermanent || sparseRepairFailed)
            return OverallVerdict.PartiallyRecovered;
        return OverallVerdict.PartiallyRecovered;
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
