using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace BACKRabbit.Firmware;

/// <summary>
/// Imports pre-downloaded Samsung firmware ZIP and extracts partition images.
/// Works with ZIPs from SamMobile, SamFw, Bifrost, or any source containing
/// AP/BL/CP/HOME_CSC .tar.md5 files.
/// </summary>
public class FirmwareImporter
{
    public async Task<FirmwareImportResult> ImportAsync(string zipPath, string outputDir)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Firmware ZIP not found: {zipPath}");

        Directory.CreateDirectory(outputDir);

        Console.WriteLine($"📦 Extracting {Path.GetFileName(zipPath)}...");
        var tempDir = Path.Combine(Path.GetTempPath(), $"backrabbit_import_{Guid.NewGuid():N}");
        ZipFile.ExtractToDirectory(zipPath, tempDir);

        // Find all .tar.md5 and .tar files
        var tarFiles = Directory.GetFiles(tempDir, "*.tar.md5", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(tempDir, "*.tar", SearchOption.AllDirectories))
            .Where(f => !f.EndsWith(".img"))
            .ToList();

        if (tarFiles.Count == 0)
        {
            Directory.Delete(tempDir, true);
            throw new InvalidOperationException(
                "No .tar.md5 files found in ZIP. Is this a valid Samsung firmware archive?");
        }

        Console.WriteLine($"📋 Found {tarFiles.Count} firmware archives:");
        foreach (var tf in tarFiles)
            Console.WriteLine($"   - {Path.GetFileName(tf)} ({FormatBytes(new FileInfo(tf).Length)})");

        // Extract each .tar.md5 using existing SamsungFirmwareExtractor
        var manifest = new FirmwareImportManifest
        {
            ImportedAt = DateTime.UtcNow,
            SourceFile = Path.GetFileName(zipPath),
            Partitions = new List<PartitionEntry>()
        };

        foreach (var tarFile in tarFiles)
        {
            Console.WriteLine($"🔧 Extracting {Path.GetFileName(tarFile)}...");

            try
            {
                var package = SamsungFirmwareExtractor.ExtractTarMd5(tarFile, skipMd5Verification: true);

                foreach (var partition in package.Partitions)
                {
                    var imgFileName = $"{partition.Key}.img";
                    var imgPath = Path.Combine(outputDir, imgFileName);

                    await File.WriteAllBytesAsync(imgPath, partition.Value);

                    var sha256 = Convert.ToHexString(
                        SHA256.HashData(partition.Value)
                    ).ToLowerInvariant();

                    manifest.Partitions.Add(new PartitionEntry
                    {
                        Name = partition.Key,
                        FileName = imgFileName,
                        Sha256 = sha256,
                        SizeBytes = partition.Value.Length
                    });

                    Console.WriteLine($"   ✅ {imgFileName} ({FormatBytes(partition.Value.Length)}) — SHA256: {sha256[..16]}...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Failed to extract {Path.GetFileName(tarFile)}: {ex.Message}");
            }
        }

        // Save manifest.json
        var manifestPath = Path.Combine(outputDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, json);

        // Cleanup temp
        try { Directory.Delete(tempDir, true); } catch { }

        Console.WriteLine($"\n✅ Import complete: {manifest.Partitions.Count} partitions → {outputDir}");
        Console.WriteLine($"📄 Manifest: {manifestPath}");
        Console.WriteLine($"\n🚀 Ready for rescue:");
        Console.WriteLine($"   backrabbit firehose rescue full --backup {outputDir} --dry-run");

        return new FirmwareImportResult
        {
            Manifest = manifest,
            OutputDir = outputDir,
            Success = true
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F1} GB";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F1} MB";
        if (bytes >= 1_000) return $"{bytes / 1_000.0:F1} KB";
        return $"{bytes} B";
    }
}

// --- Result types ---

public class FirmwareImportResult
{
    public FirmwareImportManifest Manifest { get; set; } = new();
    public string OutputDir { get; set; } = "";
    public bool Success { get; set; }
}

public class FirmwareImportManifest
{
    public DateTime ImportedAt { get; set; }
    public string SourceFile { get; set; } = "";
    public List<PartitionEntry> Partitions { get; set; } = new();
}

public class PartitionEntry
{
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
}