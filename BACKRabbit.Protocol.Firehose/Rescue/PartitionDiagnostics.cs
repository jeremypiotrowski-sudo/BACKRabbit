using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BACKRabbit.MagiskCore.Parser;
using BACKRabbit.MagiskCore.RamdiskEditor;

namespace BACKRabbit.Protocol.Firehose.Rescue;

public class PartitionDiagnostics
{
    private readonly IFirehoseClient _client;
    private readonly RescueReport _report;
    private readonly string? _backupDir;

    // Security-critical partitions to diagnose
    private static readonly (string Name, int Lun, string AnalysisType)[] _criticalPartitions = new[]
    {
        ("devinfo", 0, "StructCheck"),
        ("persist", 0, "HashCompare"),
        ("frp", 0, "StructCheck"),
        ("misc", 0, "StructCheck"),
        ("sec", 0, "StructCheck"),
        ("boot_a", 0, "MagiskDetect"),
        ("boot_b", 0, "MagiskDetect"),
        ("vbmeta_a", 0, "AvbCheck"),
        ("vbmeta_b", 0, "AvbCheck"),
        ("dtbo_a", 0, "StructCheck"),
        ("dtbo_b", 0, "StructCheck"),
        ("init_boot_a", 0, "MagiskDetect"),
        ("init_boot_b", 0, "MagiskDetect"),
    };

    public PartitionDiagnostics(IFirehoseClient client, RescueReport report, string? backupDir = null)
    {
        _client = client;
        _report = report;
        _backupDir = backupDir;
    }

    public async Task<RescueReport> RunAsync(CancellationToken ct = default)
    {
        // Populate device info
        _report.Device = new DeviceInfo
        {
            MsmId = _client.ChipInfo?.MsmId ?? 0,
            SocModel = QFuseDatabase.GetSocModel(_client.ChipInfo?.MsmId ?? 0) ?? "unknown",
            IsFused = _client.ChipInfo?.IsFused ?? false,
            SerialNumber = _client.ChipInfo?.SerialNumber,
        };

        // Get storage info
        try
        {
            _report.Device.StorageType = await _client.GetStorageInfoAsync(ct);
        }
        catch { /* non-critical */ }

        // Get GPT to find actual partitions
        List<GptPartitionEntry> gptEntries;
        try
        {
            gptEntries = await _client.PrintGptAsync(0, ct);
        }
        catch (FirehoseException)
        {
            gptEntries = new List<GptPartitionEntry>();
        }

        foreach (var (name, lun, analysisType) in _criticalPartitions)
        {
            var diagnosis = new PartitionDiagnosis
            {
                PartitionName = name,
                Lun = lun,
                Status = "Unknown",
            };

            // Check if partition exists in GPT
            var gptEntry = gptEntries.Find(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (gptEntry != null)
            {
                diagnosis.StartSector = gptEntry.StartSector;
                diagnosis.Sectors = gptEntry.Sectors;
            }

            try
            {
                diagnosis.RawData = await _client.ReadPartitionAsync(name, lun, ct: ct);
                diagnosis.ActualHash = ComputeSha256(diagnosis.RawData);
                diagnosis.Status = "Normal";

                // Compare to known-good backup if available
                if (_backupDir != null)
                {
                    var backupPath = Path.Combine(_backupDir, $"{name}.img");
                    if (File.Exists(backupPath))
                    {
                        var backupData = await File.ReadAllBytesAsync(backupPath, ct);
                        diagnosis.ExpectedHash = ComputeSha256(backupData);
                        if (diagnosis.ActualHash != diagnosis.ExpectedHash)
                            diagnosis.Status = "Tampered";
                    }
                }

                // Run analysis based on partition type
                switch (analysisType)
                {
                    case "MagiskDetect":
                        await AnalyzeBootForMagisk(diagnosis, ct);
                        break;
                    case "AvbCheck":
                        AnalyzeVbmeta(diagnosis);
                        break;
                    case "StructCheck":
                        AnalyzeStructure(diagnosis, name);
                        break;
                }
            }
            catch (FirehoseException)
            {
                diagnosis.Status = "Missing";
                diagnosis.Anomalies.Add("Partition not readable via Firehose");
            }
            catch (Exception ex)
            {
                diagnosis.Status = "Unknown";
                diagnosis.Anomalies.Add($"Diagnosis error: {ex.Message}");
            }

            _report.Partitions.Add(diagnosis);
        }

        // Determine overall verdict
        _report.Verdict = DetermineVerdict();
        GenerateRecommendations();

        return _report;
    }

    private async Task AnalyzeBootForMagisk(PartitionDiagnosis diagnosis, CancellationToken ct)
    {
        if (diagnosis.RawData == null || diagnosis.RawData.Length == 0) return;

        try
        {
            var parser = new BootImageParser();
            var bootImage = parser.Parse(diagnosis.RawData);
            var ramdiskArchive = parser.ExtractRamdiskArchive(bootImage);
            if (ramdiskArchive == null)
            {
                diagnosis.Anomalies.Add("Cannot extract ramdisk from boot image");
                return;
            }

            var detector = new MagiskArtifactDetector();
            var magiskResult = detector.Detect(ramdiskArchive);

            if (magiskResult.IsMagiskInstalled)
            {
                diagnosis.Status = "Tampered";
                diagnosis.Anomalies.Add("Magisk detected in ramdisk");
                foreach (var artifact in magiskResult.FoundArtifacts)
                    diagnosis.Anomalies.Add($"Magisk artifact: {artifact}");
            }
        }
        catch (Exception ex)
        {
            diagnosis.Anomalies.Add($"Boot image analysis failed: {ex.Message}");
        }
    }

    private void AnalyzeVbmeta(PartitionDiagnosis diagnosis)
    {
        if (diagnosis.RawData == null || diagnosis.RawData.Length < 256) return;

        // AVB footer magic: "AVBf" at end of partition
        var magic = System.Text.Encoding.ASCII.GetString(
            diagnosis.RawData, diagnosis.RawData.Length - 4, 4);
        if (magic != "AVBf")
        {
            diagnosis.Anomalies.Add("No AVB footer found");
            return;
        }

        // Check vbmeta flags at offset 120 in the VBMeta header
        // (after the AVB footer, the vbmeta header starts at footer_offset)
        // Simplified: check if verification is disabled by looking for
        // the disable flag pattern in the vbmeta header
        try
        {
            // The vbmeta header contains flags at offset 4 within the header
            // Flag bit 0 = disable verification, bit 1 = disable rollback
            // We look for the vbmeta header magic "AVB0" and then check flags
            var vbmetaMagic = System.Text.Encoding.ASCII.GetString(diagnosis.RawData, 0, 4);
            if (vbmetaMagic == "AVB0")
            {
                // Flags at offset 4-8 in vbmeta header
                if (diagnosis.RawData.Length >= 8)
                {
                    uint flags = BitConverter.ToUInt32(diagnosis.RawData, 4);
                    if ((flags & 1) != 0)
                        diagnosis.Anomalies.Add("vbmeta verification disabled (flag bit 0 set)");
                    if ((flags & 2) != 0)
                        diagnosis.Anomalies.Add("vbmeta rollback protection disabled (flag bit 1 set)");
                }
            }
        }
        catch { /* best-effort */ }
    }

    private void AnalyzeStructure(PartitionDiagnosis diagnosis, string name)
    {
        if (diagnosis.RawData == null || diagnosis.RawData.Length == 0) return;

        switch (name)
        {
            case "devinfo":
                if (diagnosis.RawData.Length >= 16)
                {
                    var magic = System.Text.Encoding.ASCII.GetString(diagnosis.RawData, 0, 13);
                    if (magic != "ANDROID-BOOT!")
                        diagnosis.Anomalies.Add("devinfo magic bytes corrupted");
                    // Lock bit at offset 0x10
                    if (diagnosis.RawData.Length >= 0x14)
                    {
                        byte lockBit = diagnosis.RawData[0x10];
                        if (lockBit == 1)
                            diagnosis.Anomalies.Add("devinfo: bootloader lock bit set (locked)");
                    }
                }
                break;
            case "persist":
                // Check for all-zeros corruption
                bool allZero = true;
                for (int i = 0; i < Math.Min(diagnosis.RawData.Length, 1024); i++)
                    if (diagnosis.RawData[i] != 0) { allZero = false; break; }
                if (allZero)
                    diagnosis.Anomalies.Add("persist appears all-zeros (possibly corrupted)");
                break;
            case "frp":
                // FRP partition — check for non-zero data indicating FRP is active
                if (diagnosis.RawData.Length > 0 && diagnosis.RawData[0] != 0)
                    diagnosis.Anomalies.Add("FRP partition has data (FRP may be enabled)");
                break;
            case "misc":
                // misc partition contains boot control block
                // Check for "bootonce" or recovery command patterns
                var miscText = System.Text.Encoding.ASCII.GetString(
                    diagnosis.RawData, 0, Math.Min(diagnosis.RawData.Length, 2048));
                if (miscText.Contains("bootonce") || miscText.Contains("recovery"))
                    diagnosis.Anomalies.Add("misc: recovery-on-boot or bootonce command detected");
                break;
            case "sec":
                // sec partition — QFuse data, read-only
                diagnosis.Anomalies.Add("sec partition present (QFuse data — read-only, do not modify)");
                break;
        }
    }

    private OverallVerdict DetermineVerdict()
    {
        bool anyTampered = false;
        bool anyMissing = false;
        bool anyPermanent = _report.FuseAudit?.TotalBlown > 0;

        foreach (var p in _report.Partitions)
        {
            if (p.Status == "Tampered") anyTampered = true;
            if (p.Status == "Missing") anyMissing = true;
        }

        if (!anyTampered && !anyMissing && !anyPermanent) return OverallVerdict.Clean;
        if (anyTampered && !anyPermanent) return OverallVerdict.Tampered;
        if (anyPermanent && anyTampered) return OverallVerdict.PermanentDamage;
        return OverallVerdict.Tampered;
    }

    private void GenerateRecommendations()
    {
        var recs = _report.Recommendations;

        foreach (var p in _report.Partitions)
        {
            if (p.Status == "Tampered" && p.Anomalies.Any(a => a.Contains("Magisk")))
                recs.Add($"Remove Magisk from {p.PartitionName} using 'firehose rescue unmagisk'");
            if (p.Status == "Tampered" && p.PartitionName.StartsWith("vbmeta"))
                recs.Add($"Restore stock {p.PartitionName} to re-enable verified boot");
            if (p.Status == "Tampered" && p.PartitionName == "devinfo")
                recs.Add($"Restore devinfo from known-good backup to unlock bootloader");
            if (p.Status == "Missing")
                recs.Add($"{p.PartitionName} not found — may need stock firmware reflash");
        }

        if (_report.FuseAudit?.TotalBlown > 0)
        {
            recs.Add("QFuses blown — some damage is permanent. See fuse audit for details.");
            foreach (var warn in _report.FuseAudit.PermanentDamageWarnings)
                recs.Add($"PERMANENT: {warn}");
        }

        if (recs.Count == 0)
            recs.Add("Device appears clean. No rescue actions needed.");
    }

    /// <summary>
    /// Full-GPT audit: iterate EVERY partition from PrintGptAsync, read it via firehose,
    /// hash it, compare against stock firmware backup. Returns a FullGptAuditResult
    /// with per-partition MATCH/MISMATCH/NO_STOCK_COMPARISON status.
    /// </summary>
    public async Task<FullGptAuditResult> RunFullGptAuditAsync(CancellationToken ct = default)
    {
        var result = new FullGptAuditResult();

        // Get all partitions from GPT
        List<GptPartitionEntry> gptEntries;
        try
        {
            gptEntries = await _client.PrintGptAsync(0, ct);
        }
        catch (FirehoseException ex)
        {
            // GPT dump failed — return empty result with error
            result.Entries.Add(new GptPartitionAuditEntry
            {
                PartitionName = "GPT_DUMP_FAILED",
                Status = GptAuditStatus.Error,
                ErrorMessage = ex.Message
            });
            return result;
        }

        foreach (var gptEntry in gptEntries)
        {
            var entry = new GptPartitionAuditEntry
            {
                PartitionName = gptEntry.Name,
                PartitionSize = (long)(gptEntry.Sectors * 512),
            };

            try
            {
                // Read partition from device
                var deviceData = await _client.ReadPartitionAsync(gptEntry.Name, 0, ct: ct);
                entry.DeviceSha256 = ComputeSha256(deviceData);

                // Check if stock image exists in backup directory
                if (_backupDir != null)
                {
                    var stockPath = Path.Combine(_backupDir, $"{gptEntry.Name}.img");
                    if (File.Exists(stockPath))
                    {
                        var stockData = await File.ReadAllBytesAsync(stockPath, ct);
                        entry.StockSha256 = ComputeSha256(stockData);

                        if (entry.DeviceSha256 == entry.StockSha256)
                        {
                            entry.Status = GptAuditStatus.Match;
                        }
                        else
                        {
                            entry.Status = GptAuditStatus.Mismatch;
                            // Compute block-level diff: compare 512-byte sectors
                            int sectorSize = 512;
                            int minSectors = (int)Math.Min(deviceData.Length, stockData.Length) / sectorSize;
                            for (int i = 0; i < minSectors; i++)
                            {
                                int offset = i * sectorSize;
                                bool differs = false;
                                for (int b = 0; b < sectorSize && (offset + b) < deviceData.Length && (offset + b) < stockData.Length; b++)
                                {
                                    if (deviceData[offset + b] != stockData[offset + b])
                                    {
                                        differs = true;
                                        break;
                                    }
                                }
                                if (differs)
                                {
                                    entry.DifferingSectorCount++;
                                    entry.DifferingSectorAddresses.Add((long)i * sectorSize);
                                }
                            }
                            // If sizes differ, count remaining sectors as differing
                            if (deviceData.Length != stockData.Length)
                            {
                                int extraSectors = Math.Abs(deviceData.Length - stockData.Length) / sectorSize;
                                entry.DifferingSectorCount += extraSectors;
                            }
                        }
                    }
                    else
                    {
                        entry.Status = GptAuditStatus.NoStockComparison;
                    }
                }
                else
                {
                    entry.Status = GptAuditStatus.NoStockComparison;
                }
            }
            catch (FirehoseException ex)
            {
                entry.Status = GptAuditStatus.Error;
                entry.ErrorMessage = $"Read failed: {ex.Message}";
            }
            catch (Exception ex)
            {
                entry.Status = GptAuditStatus.Error;
                entry.ErrorMessage = ex.Message;
            }

            result.Entries.Add(entry);
        }

        return result;
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
