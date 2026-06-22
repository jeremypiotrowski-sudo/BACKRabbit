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
    private readonly bool _dryRun;
    private readonly bool _wipeData;

    // Partitions that should NEVER be restored (device-unique or dangerous)
    private static readonly HashSet<string> _neverRestore = new(StringComparer.OrdinalIgnoreCase)
    {
        "sec",       // QFuse data — read-only, touching it is dangerous
        "ddr",       // DDR training data — device-specific
        "limits",    // Hardware limits — device-specific
        "apdp",      // AP debug policy — device-specific
        "msadp",     // Modem debug policy — device-specific
    };

    public PartitionRestorer(IFirehoseClient client, string backupDir, RescueReport report, bool force = false, bool dryRun = false, bool wipeData = false)
    {
        _client = client;
        _backupDir = backupDir;
        _report = report;
        _force = force;
        _dryRun = dryRun;
        _wipeData = wipeData;
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

    /// <summary>
    /// Sparse repair: for each MISMATCH partition in the audit, write ONLY the
    /// differing sectors from the stock image. Verify every written sector after write.
    /// In dry-run mode: log planned writes but make ZERO WritePartitionBlocksAsync calls.
    /// CONVENTION: DifferingSectorAddresses stores byte offsets; we convert to sector
    /// numbers by dividing by 512 before passing to WritePartitionBlocksAsync.
    /// </summary>
    public async Task<SparseRepairResult> SparseRepairAsync(
        FullGptAuditResult audit, CancellationToken ct = default)
    {
        var result = new SparseRepairResult { DryRun = _dryRun };
        const int sectorSize = 512;

        foreach (var entry in audit.Entries)
        {
            if (entry.Status != GptAuditStatus.Mismatch)
                continue;

            var partResult = new SparsePartitionResult
            {
                PartitionName = entry.PartitionName,
            };

            // a. Check blocklist
            if (_neverRestore.Contains(entry.PartitionName) && !_force)
            {
                partResult.Status = SparseRepairStatus.Blocklisted;
                partResult.ErrorMessage = "BLOCKLISTED - SKIPPED (device-unique partition, use --force to override)";
                Console.WriteLine($"  [SPARSE] {entry.PartitionName}: BLOCKLISTED - SKIPPED");
                result.Partitions.Add(partResult);
                continue;
            }

            // b. Read stock image
            var stockPath = Path.Combine(_backupDir, $"{entry.PartitionName}.img");
            if (!File.Exists(stockPath))
            {
                partResult.Status = SparseRepairStatus.NoStockImage;
                partResult.ErrorMessage = "NO STOCK IMAGE - SKIPPED";
                Console.WriteLine($"  [SPARSE] {entry.PartitionName}: NO STOCK IMAGE - SKIPPED");
                result.Partitions.Add(partResult);
                continue;
            }

            var stockData = await File.ReadAllBytesAsync(stockPath, ct);

            if (_dryRun)
            {
                // Log planned writes but make ZERO calls
                partResult.Status = SparseRepairStatus.Repaired; // would be repaired
                partResult.SectorsWritten = entry.DifferingSectorCount;
                partResult.WrittenSectorAddresses = new List<long>(entry.DifferingSectorAddresses);
                Console.WriteLine($"  [DRY-RUN] [SPARSE] {entry.PartitionName}: Would write {entry.DifferingSectorCount} sectors");

                foreach (var addr in entry.DifferingSectorAddresses)
                {
                    long sectorNumber = addr / sectorSize;
                    Console.WriteLine($"    [DRY-RUN] Would write sector {sectorNumber} (offset {addr})");
                }

                result.Partitions.Add(partResult);
                continue;
            }

            // c. Write each differing sector
            Console.WriteLine($"  [SPARSE] {entry.PartitionName}: Writing {entry.DifferingSectorCount} differing sectors...");
            int writtenCount = 0;

            foreach (var addr in entry.DifferingSectorAddresses)
            {
                long sectorNumber = addr / sectorSize;
                int byteOffset = (int)addr;

                // Extract 512-byte block from stock image
                if (byteOffset + sectorSize > stockData.Length)
                {
                    Console.WriteLine($"    WARNING: Sector at offset {addr} beyond stock image size - skipping");
                    continue;
                }

                var blockData = new byte[sectorSize];
                Array.Copy(stockData, byteOffset, blockData, 0, sectorSize);

                await _client.WritePartitionBlocksAsync(
                    entry.PartitionName, blockData, sectorNumber,
                    lun: 0, sectorSize: sectorSize, ct: ct);

                partResult.WrittenSectorAddresses.Add(addr);
                writtenCount++;
            }

            partResult.SectorsWritten = writtenCount;
            Console.WriteLine($"  [SPARSE] {entry.PartitionName}: {writtenCount} sectors written, verifying...");

            // d. Read back and verify every written sector
            var deviceData = await _client.ReadPartitionAsync(entry.PartitionName, 0, ct: ct);
            int verifiedCount = 0;
            bool allMatch = true;

            foreach (var addr in partResult.WrittenSectorAddresses)
            {
                int byteOffset = (int)addr;
                if (byteOffset + sectorSize > deviceData.Length ||
                    byteOffset + sectorSize > stockData.Length)
                {
                    allMatch = false;
                    Console.WriteLine($"    VERIFY FAIL: Sector at offset {addr} out of bounds on readback");
                    continue;
                }

                bool sectorMatches = true;
                for (int b = 0; b < sectorSize; b++)
                {
                    if (deviceData[byteOffset + b] != stockData[byteOffset + b])
                    {
                        sectorMatches = false;
                        break;
                    }
                }

                if (sectorMatches)
                {
                    verifiedCount++;
                }
                else
                {
                    allMatch = false;
                    Console.WriteLine($"    VERIFY FAIL: Sector at offset {addr} does not match stock after write");
                }
            }

            partResult.SectorsVerified = verifiedCount;

            if (allMatch && verifiedCount == writtenCount)
            {
                partResult.Status = SparseRepairStatus.Repaired;
                Console.WriteLine($"  [SPARSE] {entry.PartitionName}: VERIFIED - all {verifiedCount} sectors match stock");
            }
            else
            {
                partResult.Status = SparseRepairStatus.VerificationFailed;
                partResult.ErrorMessage = $"Verification failed: {verifiedCount}/{writtenCount} sectors verified";
                Console.WriteLine($"  [SPARSE] {entry.PartitionName}: VERIFICATION FAILED - {verifiedCount}/{writtenCount} sectors verified");
            }

            result.Partitions.Add(partResult);
        }

        Console.WriteLine($"  [SPARSE] Summary: {result.TotalPartitionsRepaired} repaired, {result.TotalPartitionsFailed} failed, {result.TotalPartitionsSkipped} skipped, {result.TotalSectorsWritten} total sectors written");
        return result;
    }

    /// <summary>
    /// Wipe the userdata partition at block level.
    /// THREE safety gates before any erase call:
    ///   1. --wipe-data flag must be set on the constructor (_wipeData == true)
    ///   2. Typed confirmation must match expectedToken (or special "DRY-RUN-AUTO" token)
    ///   3. --dry-run forces ZERO ErasePartitionAsync calls regardless of other gates
    /// After erase: readback samples first 1000 sectors + last 100 sectors for any
    /// non-zero byte. Any non-zero byte = VerificationFailed = ABORT rescue.
    /// </summary>
    public async Task<WipeDataResult> WipeUserDataAsync(
        string forceConfirmation,
        string expectedToken = "CONFIRM-WIPE-DATA",
        CancellationToken ct = default)
    {
        var result = new WipeDataResult { ConfirmationRequested = expectedToken };

        // GATE 1: --wipe-data flag must be set
        if (!_wipeData)
        {
            result.Status = WipeDataStatus.WipeNotAuthorized;
            result.ErrorMessage = "--wipe-data not set. Userdata was NOT erased.";
            return result;
        }

        // GATE 2: Typed confirmation must match
        if (string.IsNullOrEmpty(forceConfirmation) || forceConfirmation != expectedToken)
        {
            result.Status = WipeDataStatus.ConfirmationFailed;
            result.ConfirmationProvided = false;
            result.ErrorMessage = $"Confirmation mismatch. Expected '{expectedToken}', got '{forceConfirmation}'.";
            Console.WriteLine($"  [WIPE] CONFIRMATION FAILED. Userdata NOT erased.");
            return result;
        }
        result.ConfirmationProvided = true;

        // GATE 3: Dry-run forces ZERO calls
        if (_dryRun)
        {
            result.Status = WipeDataStatus.DryRunLogged;
            result.DryRun = true;
            Console.WriteLine($"  [DRY-RUN] [WIPE] Would erase userdata partition at block level.");
            Console.WriteLine($"  [DRY-RUN] [WIPE] Would verify post-erase readback shows all zeros.");
            return result;
        }

        // ACTUAL WIPE: erase userdata, then readback-verify
        Console.WriteLine($"  [WIPE] Erasing userdata partition...");
        var eraseOk = await _client.ErasePartitionAsync("userdata", 0, ct);
        if (!eraseOk)
        {
            result.Status = WipeDataStatus.VerificationFailed;
            result.ErrorMessage = "ErasePartitionAsync returned false.";
            Console.WriteLine($"  [WIPE] ❌ Erase command returned NAK.");
            return result;
        }

        Console.WriteLine($"  [WIPE] Erase complete. Reading back for verification...");

        // Read back userdata and verify sectors are all zeros
        // Sample first 1000 sectors + last 100 sectors (if partition has more)
        byte[] readback;
        try
        {
            readback = await _client.ReadPartitionAsync("userdata", 0, 512, ct);
        }
        catch (FirehoseException ex)
        {
            result.Status = WipeDataStatus.VerificationFailed;
            result.ErrorMessage = $"Readback failed: {ex.Message}";
            Console.WriteLine($"  [WIPE] ❌ Readback failed: {ex.Message}");
            return result;
        }

        const int firstSampleSize = 1000;  // sectors
        const int lastSampleSize = 100;    // sectors
        const int sectorSize = 512;
        long totalSectors = readback.Length / sectorSize;

        long sectorsToCheck = Math.Min(firstSampleSize, totalSectors);
        long checkedCount = 0;
        long? firstNonZero = null;

        // Check first N sectors
        for (long s = 0; s < sectorsToCheck; s++)
        {
            int byteOffset = (int)(s * sectorSize);
            if (byteOffset + sectorSize > readback.Length) break;
            bool allZero = true;
            for (int b = 0; b < sectorSize; b++)
            {
                if (readback[byteOffset + b] != 0)
                {
                    allZero = false;
                    break;
                }
            }
            if (!allZero)
            {
                firstNonZero = s;
                break;
            }
            checkedCount++;
        }

        // Check last N sectors (if partition is large enough)
        if (firstNonZero == null && totalSectors > firstSampleSize)
        {
            long startLast = totalSectors - lastSampleSize;
            for (long s = startLast; s < totalSectors; s++)
            {
                int byteOffset = (int)(s * sectorSize);
                if (byteOffset + sectorSize > readback.Length) break;
                bool allZero = true;
                for (int b = 0; b < sectorSize; b++)
                {
                    if (readback[byteOffset + b] != 0)
                    {
                        allZero = false;
                        break;
                    }
                }
                if (!allZero)
                {
                    firstNonZero = s;
                    break;
                }
                checkedCount++;
            }
        }

        result.SectorsChecked = checkedCount;
        result.FirstNonZeroSector = firstNonZero;

        if (firstNonZero != null)
        {
            result.Status = WipeDataStatus.VerificationFailed;
            result.ErrorMessage = $"Sector {firstNonZero} contains non-zero data after erase.";
            Console.WriteLine($"  [WIPE] ❌ VERIFICATION FAILED — sector {firstNonZero} has non-zero data.");
            return result;
        }

        result.Status = WipeDataStatus.WipedAndVerified;
        Console.WriteLine($"  [WIPE] ✅ Wiped and verified — {checkedCount} sectors confirmed zero.");
        return result;
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
