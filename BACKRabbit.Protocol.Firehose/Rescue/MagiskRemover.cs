using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BACKRabbit.MagiskCore.Parser;
using BACKRabbit.MagiskCore.Repacker;
using BACKRabbit.MagiskCore.RamdiskEditor;

namespace BACKRabbit.Protocol.Firehose.Rescue;

public class MagiskRemover
{
    private readonly FirehoseClient _client;
    private readonly string _backupDir;
    private readonly RescueReport _report;

    public MagiskRemover(FirehoseClient client, string backupDir, RescueReport report)
    {
        _client = client;
        _backupDir = backupDir;
        _report = report;
    }

    public async Task<List<MagiskRemovalResult>> RemoveAllAsync(CancellationToken ct = default)
    {
        var results = new List<MagiskRemovalResult>();

        // Check which boot slots exist
        var gptEntries = await _client.PrintGptAsync(0, ct);
        bool hasA = gptEntries.Exists(e => e.Name.Equals("boot_a", StringComparison.OrdinalIgnoreCase));
        bool hasB = gptEntries.Exists(e => e.Name.Equals("boot_b", StringComparison.OrdinalIgnoreCase));

        if (hasA)
        {
            Console.WriteLine("[Magisk Removal] Processing boot_a...");
            results.Add(await RemoveFromSlotAsync("a", ct));
        }
        if (hasB)
        {
            Console.WriteLine("[Magisk Removal] Processing boot_b...");
            results.Add(await RemoveFromSlotAsync("b", ct));
        }

        if (!hasA && !hasB)
        {
            // Try single boot partition (non-A/B device)
            if (gptEntries.Exists(e => e.Name.Equals("boot", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("[Magisk Removal] Processing boot (single slot)...");
                results.Add(await RemoveFromSlotAsync("", ct));
            }
        }

        _report.MagiskRemovals.AddRange(results);
        return results;
    }

    public async Task<MagiskRemovalResult> RemoveFromSlotAsync(string slot, CancellationToken ct = default)
    {
        var bootName = string.IsNullOrEmpty(slot) ? "boot" : $"boot_{slot}";
        var vbmetaName = string.IsNullOrEmpty(slot) ? "vbmeta" : $"vbmeta_{slot}";
        var result = new MagiskRemovalResult { BootPartition = bootName };

        try
        {
            // 1. Read current boot image
            Console.WriteLine($"  Reading {bootName}...");
            var bootData = await _client.ReadPartitionAsync(bootName, 0, ct: ct);
            result.OriginalHash = ComputeSha256(bootData);

            // 2. Parse boot image
            var parser = new BootImageParser();
            BootImage bootImage;
            try
            {
                bootImage = parser.Parse(bootData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Cannot parse {bootName}: {ex.Message}");
                return result;
            }

            // 3. Extract ramdisk and check for Magisk
            var ramdiskArchive = parser.ExtractRamdiskArchive(bootImage);
            if (ramdiskArchive == null)
            {
                Console.WriteLine($"  Cannot extract ramdisk from {bootName}");
                return result;
            }

            var detector = new MagiskArtifactDetector();
            var magiskResult = detector.Detect(ramdiskArchive);
            result.MagiskFound = magiskResult.IsMagiskInstalled;

            if (!magiskResult.IsMagiskInstalled)
            {
                Console.WriteLine($"  No Magisk detected in {bootName}");
                return result;
            }

            Console.WriteLine($"  Magisk detected in {bootName} ({magiskResult.FoundArtifacts.Count} artifacts)");

            // 4. Get clean ramdisk
            byte[]? cleanRamdisk = null;

            // Try backup first
            var ramdiskBackupPath = Path.Combine(_backupDir, $"{bootName}_ramdisk.img");
            if (File.Exists(ramdiskBackupPath))
            {
                cleanRamdisk = await File.ReadAllBytesAsync(ramdiskBackupPath, ct);
                Console.WriteLine($"  Using clean ramdisk from backup: {ramdiskBackupPath}");
            }
            else
            {
                // Try extracting from stock boot image in backup
                var bootBackupPath = Path.Combine(_backupDir, $"{bootName}.img");
                if (File.Exists(bootBackupPath))
                {
                    var stockBootData = await File.ReadAllBytesAsync(bootBackupPath, ct);
                    var stockParser = new BootImageParser();
                    var stockBootImage = stockParser.Parse(stockBootData);
                    cleanRamdisk = stockParser.ExtractRamdisk(stockBootImage);
                    Console.WriteLine($"  Extracted clean ramdisk from stock {bootName}.img");
                }
            }

            // 5. If no clean ramdisk available, try surgical removal
            if (cleanRamdisk == null)
            {
                Console.WriteLine($"  No clean ramdisk available — attempting surgical Magisk removal...");
                var cleanedArchive = detector.SurgicalRemoval(ramdiskArchive);
                cleanRamdisk = cleanedArchive.Serialize();
                Console.WriteLine($"  Surgical removal complete (best-effort)");
            }

            // 6. Repack boot image with clean ramdisk
            Console.WriteLine($"  Repacking {bootName}...");
            var repacker = new BootImageRepacker();
            var repackedBoot = repacker.Repack(bootImage, cleanRamdisk);

            // 7. Save original to forensic directory
            var forensicDir = Path.Combine(_backupDir, "forensic", "pre-unmagisk");
            Directory.CreateDirectory(forensicDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            await File.WriteAllBytesAsync(
                Path.Combine(forensicDir, $"{bootName}_{timestamp}.img"), bootData, ct);

            // 8. Write repacked boot image
            Console.WriteLine($"  Writing clean {bootName} ({repackedBoot.Length:N0} bytes)...");
            await _client.WritePartitionAsync(bootName, repackedBoot, 0, ct: ct);

            // 9. Verify
            var writtenData = await _client.ReadPartitionAsync(bootName, 0, ct: ct);
            result.CleanHash = ComputeSha256(writtenData);
            result.MagiskRemoved = true;

            // Verify no Magisk in written image
            var verifyParser = new BootImageParser();
            var verifyImage = verifyParser.Parse(writtenData);
            var verifyRamdisk = verifyParser.ExtractRamdiskArchive(verifyImage);
            if (verifyRamdisk != null)
            {
                var verifyResult = detector.Detect(verifyRamdisk);
                if (verifyResult.IsMagiskInstalled)
                {
                    result.MagiskRemoved = false;
                    Console.WriteLine($"  WARNING: Magisk still detected after write!");
                }
            }

            Console.WriteLine($"  {bootName}: Magisk removed={result.MagiskRemoved}");

            // 10. Check and restore vbmeta if needed
            try
            {
                var vbmetaData = await _client.ReadPartitionAsync(vbmetaName, 0, ct: ct);
                // Check if verification is disabled
                if (vbmetaData.Length >= 8)
                {
                    var vbmetaMagic = System.Text.Encoding.ASCII.GetString(vbmetaData, 0, 4);
                    if (vbmetaMagic == "AVB0")
                    {
                        uint flags = BitConverter.ToUInt32(vbmetaData, 4);
                        if ((flags & 1) != 0) // verification disabled
                        {
                            Console.WriteLine($"  {vbmetaName}: verification disabled — attempting restore...");
                            var vbmetaBackupPath = Path.Combine(_backupDir, $"{vbmetaName}.img");
                            if (File.Exists(vbmetaBackupPath))
                            {
                                var stockVbmeta = await File.ReadAllBytesAsync(vbmetaBackupPath, ct);
                                await _client.WritePartitionAsync(vbmetaName, stockVbmeta, 0, ct: ct);
                                result.VbmetaRestored = true;
                                Console.WriteLine($"  {vbmetaName}: restored from backup");
                            }
                            else
                            {
                                Console.WriteLine($"  {vbmetaName}: no backup available — vbmeta NOT restored");
                            }
                        }
                    }
                }
            }
            catch (FirehoseException)
            {
                // vbmeta partition may not exist — non-critical
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {bootName}: FAILED — {ex.Message}");
        }

        return result;
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
