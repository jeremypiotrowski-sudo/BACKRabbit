using BACKRabbit.Protocol.Firehose.Rescue;
using System.Security.Cryptography;
using Xunit;

namespace BACKRabbit.Protocol.Firehose.Tests.Rescue;

/// <summary>
/// THE MOST IMPORTANT TEST IN THE ENTIRE SUITE.
///
/// Stock-only write enforcement (Sub-step E).
///
/// This test proves BACKRabbit is structurally incapable of weaponization.
/// Every other test proves BACKRabbit works correctly. This test proves
/// BACKRabbit CANNOT WORK INCORRECTLY when given a tampered stock file.
///
/// Scenario: An attacker substitutes a malicious stock file (e.g., a boot.img
/// with a backdoor rootkit). Even if BACKRabbit is told to repair that partition,
/// the SHA256 in manifest.json (which the operator reviewed at import time) will
/// not match the tampered file's hash. BACKRabbit must REFUSE the write.
///
/// Without this test, BACKRabbit would happily flash malicious firmware. With it,
/// the attack fails and the operator gets a clear "WRITE REFUSED" error.
/// </summary>
public class StockOnlyWriteTests : IDisposable
{
    private readonly string _testBackupDir;

    public StockOnlyWriteTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), $"backrabbit_stock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBackupDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBackupDir))
            Directory.Delete(_testBackupDir, recursive: true);
    }

    private static byte[] CreatePartitionData(int sectorCount, byte fill)
    {
        var data = new byte[sectorCount * 512];
        for (int i = 0; i < data.Length; i++) data[i] = fill;
        return data;
    }

    private static MockFirehoseClient CreateMockClient()
    {
        return new MockFirehoseClient
        {
            ChipInfoOverride = new SaharaChipInfo
            {
                Version = 2, MinVersion = 1, MaxPacketSize = 16384,
                Mode = SaharaMode.ImageTxPending, MsmId = 0x008600E1,
                PkHash = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                SerialNumber = "0x12345678",
            },
            StorageType = "ufs",
            GptEntries = new List<GptPartitionEntry>
            {
                new() { Name = "boot_a", StartSector = 0, Sectors = 10 },
                new() { Name = "boot_b", StartSector = 10, Sectors = 10 },
            },
            PartitionData = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["boot_a"] = CreatePartitionData(10, 0xAA),
                ["boot_b"] = CreatePartitionData(10, 0xBB),
            },
        };
    }

    private static string Sha256Hex(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// THE CRITICAL TEST.
    ///
    /// Stock file for boot_a has its bytes corrupted after manifest was written.
    /// When SparseRepairAsync attempts to write boot_a:
    /// - It must REFUSE to write any blocks
    /// - boot_a partition must be marked Skipped with stock integrity error
    /// - boot_b (with valid stock file) must still be repaired normally
    ///
    /// This proves BACKRabbit cannot be tricked into writing tampered firmware.
    /// </summary>
    [Fact]
    public async Task StockOnlyWrite_TamperedStockFile_Refused()
    {
        // Arrange
        var client = CreateMockClient();

        // Create legitimate stock files for both partitions
        var stockBootA = CreatePartitionData(10, 0xAA);  // matches device boot_a
        var stockBootB = CreatePartitionData(10, 0xBB);  // matches device boot_b
        File.WriteAllBytes(Path.Combine(_testBackupDir, "boot_a.img"), stockBootA);
        File.WriteAllBytes(Path.Combine(_testBackupDir, "boot_b.img"), stockBootB);

        // Generate manifest.json with CORRECT hashes
        var manifest = new PartitionRestorer.FirmwareManifest
        {
            Partitions = new List<PartitionRestorer.FirmwareManifestPartitionEntry>
            {
                new() { Name = "boot_a", FileName = "boot_a.img", Sha256 = Sha256Hex(stockBootA), SizeBytes = stockBootA.Length },
                new() { Name = "boot_b", FileName = "boot_b.img", Sha256 = Sha256Hex(stockBootB), SizeBytes = stockBootB.Length },
            },
        };
        var manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_testBackupDir, "manifest.json"), manifestJson);

        // ── ATTACK SIMULATION ──
        // Attacker corrupts boot_a.img AFTER the operator reviewed the manifest.
        // The manifest hash will no longer match the file's actual content.
        stockBootA[512] = 0xDE;     // corrupt sector 1
        stockBootA[1536] = 0xAD;   // corrupt sector 3
        File.WriteAllBytes(Path.Combine(_testBackupDir, "boot_a.img"), stockBootA);

        // Make boot_b mismatch its pre-rescue data too (so sparse repair wants to write it)
        var deviceBootB = client.PartitionData["boot_b"];
        deviceBootB[2048] = 0xEF;  // corrupt sector 4
        client.PartitionData["boot_b"] = deviceBootB;

        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report,
            force: false, dryRun: false, wipeData: false);

        // Build audit: both boot_a and boot_b mismatched
        var audit = new FullGptAuditResult
        {
            Entries = new List<GptPartitionAuditEntry>
            {
                new() { PartitionName = "boot_a", Status = GptAuditStatus.Mismatch, DifferingSectorCount = 2,
                    DifferingSectorAddresses = new List<long> { 512, 1536 } },
                new() { PartitionName = "boot_b", Status = GptAuditStatus.Mismatch, DifferingSectorCount = 1,
                    DifferingSectorAddresses = new List<long> { 2048 } },
            },
        };

        // Act
        var result = await restorer.SparseRepairAsync(audit);

        // ── ASSERT: boot_a REFUSED (zero writes, status Skipped) ──
        var bootACalls = client.WriteBlocksCalls.Where(c => c.Partition == "boot_a").ToList();
        Assert.Empty(bootACalls);
        var bootAResult = result.Partitions.Find(p => p.PartitionName == "boot_a");
        Assert.NotNull(bootAResult);
        Assert.Equal(SparseRepairStatus.Skipped, bootAResult!.Status);
        Assert.Contains("STOCK INTEGRITY", bootAResult.ErrorMessage);

        // ── ASSERT: boot_b STILL REPAIRED (valid stock, hash matches manifest) ──
        var bootBCalls = client.WriteBlocksCalls.Where(c => c.Partition == "boot_b").ToList();
        Assert.Single(bootBCalls);  // exactly 1 block written
        var bootBResult = result.Partitions.Find(p => p.PartitionName == "boot_b");
        Assert.NotNull(bootBResult);
        Assert.Equal(SparseRepairStatus.Repaired, bootBResult!.Status);
    }
}