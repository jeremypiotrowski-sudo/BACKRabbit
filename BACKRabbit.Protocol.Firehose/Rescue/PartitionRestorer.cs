using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose.Rescue;

public class PartitionRestorer
{
    private readonly IFirehoseClient _client;
    private readonly string _backupDir;
    private readonly RescueReport _report;
    private readonly bool _force;

    // Partitions that should NEVER be restored (device-unique or dangerous)
    private static readonly HashSet<string> _neverRestore = new(StringComparer.OrdinalIgnoreCase)
    {
        "sec",       // QFuse data — read-only, touching it is dangerous
        "ddr",       // DDR training data — device-specific
        "limits",    // Hardware limits — device-specific
        "apdp",      // AP debug policy — device-specific
        "msadp",     // Modem debug policy — device-specific
    };

    public PartitionRestorer(IFirehoseClient client, string backupDir, RescueReport report, bool force = false)
    {
        _client = client;
        _backupDir = backupDir;
        _report = report;
        _force = force;
    }

    public async Task<List<RestoreAction>> RestoreAsync(
        List<string> partitionNames, CancellationToken ct = default)
    {
        var actions = new List<RestoreAction>();

        foreach (var name in partitionNames)
        {
            var action = new RestoreAction
            {
                PartitionName = name,
                Action = "Skipped",
            };

            // Safety: never restore dangerous partitions (unless --force)
            if (_neverRestore.Contains(name) && !_force)
            {
                action.Action = "Skipped";
                action.SourceFile = "BLOCKED — device-unique partition, cannot restore";
                actions.Add(action);
                _report.RestoreActions.Add(action);
                Console.WriteLine($"  {name}: SKIPPED (device-unique, dangerous to restore)");
                continue;
            }

            if (_neverRestore.Contains(name) && _force)
            {
                Console.WriteLine($"  {name}: FORCE OVERRIDE — restoring despite blocklist");
                Console.Write($"  Type the device model name to confirm (e.g., SM-F966U1): ");
                var confirmation = Console.ReadLine()?.Trim() ?? "";
                if (string.IsNullOrEmpty(confirmation) || confirmation.Length < 3)
                {
                    action.Action = "Skipped";
                    action.SourceFile = "BLOCKED — force confirmation failed (empty input)";
                    actions.Add(action);
                    _report.RestoreActions.Add(action);
                    Console.WriteLine($"  {name}: SKIPPED (force confirmation failed)");
                    continue;
                }
                Console.WriteLine($"  {name}: Confirmation accepted — proceeding with force override");
            }

            var backupPath = Path.Combine(_backupDir, $"{name}.img");
            if (!File.Exists(backupPath))
            {
                action.Action = "Skipped";
                action.SourceFile = "MISSING — no backup file found";
                actions.Add(action);
                _report.RestoreActions.Add(action);
                Console.WriteLine($"  {name}: SKIPPED (no backup file)");
                continue;
            }

            try
            {
                // 1. Save forensic evidence (pre-restore state)
                var forensicDir = Path.Combine(_backupDir, "forensic", "pre-restore");
                Directory.CreateDirectory(forensicDir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var forensicPath = Path.Combine(forensicDir, $"{name}_{timestamp}.img");

                try
                {
                    var currentData = await _client.ReadPartitionAsync(name, 0, ct: ct);
                    await File.WriteAllBytesAsync(forensicPath, currentData, ct);
                    action.PreRestoreHash = ComputeSha256(currentData);
                }
                catch (FirehoseException)
                {
                    // Partition not readable — still try to restore
                    action.PreRestoreHash = "unreadable";
                }

                // 2. Load known-good backup
                var knownGood = await File.ReadAllBytesAsync(backupPath, ct);
                var knownGoodHash = ComputeSha256(knownGood);

                // 3. Size check — warn if backup is significantly different size
                try
                {
                    var gptEntries = await _client.PrintGptAsync(0, ct);
                    var gptEntry = gptEntries.Find(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (gptEntry != null)
                    {
                        long expectedSize = (long)(gptEntry.Sectors * 512);
                        long deviation = Math.Abs(knownGood.Length - expectedSize);
                        if (deviation > expectedSize * 0.10) // 10% tolerance
                        {
                            Console.WriteLine($"  WARNING: {name} backup size ({knownGood.Length}) differs from partition size ({expectedSize}) by {deviation} bytes");
                        }
                    }
                }
                catch { /* non-critical */ }

                // 4. Erase
                Console.WriteLine($"  {name}: erasing...");
                await _client.ErasePartitionAsync(name, 0, ct);
                action.Action = "Erased";

                // 5. Write
                Console.WriteLine($"  {name}: writing {knownGood.Length:N0} bytes...");
                await _client.WritePartitionAsync(name, knownGood, 0, ct: ct);
                action.Action = "Flashed";
                action.SourceFile = backupPath;

                // 6. Verify
                Console.WriteLine($"  {name}: verifying...");
                var writtenData = await _client.ReadPartitionAsync(name, 0, ct: ct);
                var writtenHash = ComputeSha256(writtenData);
                action.PostRestoreHash = writtenHash;
                action.Verified = (writtenHash == knownGoodHash);

                if (action.Verified)
                    Console.WriteLine($"  {name}: OK (hash matches)");
                else
                    Console.WriteLine($"  {name}: WARNING — hash mismatch after write!");
            }
            catch (Exception ex)
            {
                action.Action = "Failed";
                action.SourceFile = $"Error: {ex.Message}";
                Console.WriteLine($"  {name}: FAILED — {ex.Message}");
            }

            actions.Add(action);
            _report.RestoreActions.Add(action);
        }

        return actions;
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
