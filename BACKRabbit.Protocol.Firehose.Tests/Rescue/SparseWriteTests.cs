using BACKRabbit.Protocol.Firehose.Rescue;
using System.Text;
using Xunit;

namespace BACKRabbit.Protocol.Firehose.Tests.Rescue;

/// <summary>
/// Tests for sparse write repair — Sub-step B verification.
///
/// Each test creates a MockFirehoseClient with known partition data, builds a
/// FullGptAuditResult marking partitions as Mismatch with specific
/// DifferingSectorAddresses (byte offsets), runs PartitionRestorer.SparseRepairAsync,
/// and asserts the WriteBlocksCalls list reflects the expected sector numbers
/// (NOT byte offsets — see PartitionRestorer.SparseRepairAsync XML doc).
/// </summary>
public class SparseWriteTests : IDisposable
{
    private readonly string _testBackupDir;

    public SparseWriteTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), $"backrabbit_sparse_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBackupDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBackupDir))
            Directory.Delete(_testBackupDir, recursive: true);
    }

    /// <summary>Build a MockFirehoseClient with one partition that has 100 sectors of data.</summary>
    private MockFirehoseClient CreateMockClient(string partitionName, byte[] partitionData)
    {
        var client = new MockFirehoseClient
        {
            ChipInfoOverride = new SaharaChipInfo
            {
                Version = 2,
                MinVersion = 1,
                MaxPacketSize = 16384,
                Mode = SaharaMode.ImageTxPending,
                MsmId = 0x008600E1,
                PkHash = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                SerialNumber = "0x12345678",
            },
            StorageType = "ufs",
            GptEntries = new List<GptPartitionEntry>
            {
                new() { Name = partitionName, StartSector = 0, Sectors = (ulong)(partitionData.Length / 512) },
            },
            PartitionData = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [partitionName] = partitionData,
            },
        };
        return client;
    }

    /// <summary>Write a 512-byte sector with a known byte pattern (sector index encoded in first 4 bytes).</summary>
    private static byte[] CreateSector(int sectorIndex)
    {
        var sector = new byte[512];
        // Encode sector index in first 4 bytes so verification can spot-check
        BitConverter.GetBytes(sectorIndex).CopyTo(sector, 0);
        // Fill rest with deterministic pattern
        for (int i = 4; i < 512; i++)
            sector[i] = (byte)((sectorIndex * 7 + i) & 0xFF);
        return sector;
    }

    private static byte[] CreatePartitionData(int sectorCount)
    {
        var data = new byte[sectorCount * 512];
        for (int i = 0; i < sectorCount; i++)
        {
            var sector = CreateSector(i);
            Array.Copy(sector, 0, data, i * 512, 512);
        }
        return data;
    }

    /// <summary>
    /// Writes a manifest.json into the test backup directory that contains valid SHA256
    /// hashes for the stock files. Required for stock-only write enforcement (Sub-step E).
    /// Should be called AFTER all stock files are written.
    /// </summary>
    private void WriteValidManifestForAllStockFiles()
    {
        var entries = new List<PartitionRestorer.FirmwareManifestPartitionEntry>();
        foreach (var filePath in Directory.GetFiles(_testBackupDir, "*.img"))
        {
            var data = File.ReadAllBytes(filePath);
            var name = Path.GetFileNameWithoutExtension(filePath);
            entries.Add(new PartitionRestorer.FirmwareManifestPartitionEntry
            {
                Name = name,
                FileName = Path.GetFileName(filePath),
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant(),
                SizeBytes = data.Length,
            });
        }
        var manifest = new PartitionRestorer.FirmwareManifest { Partitions = entries };
        File.WriteAllText(Path.Combine(_testBackupDir, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static FullGptAuditResult BuildAudit(string partitionName, GptAuditStatus status, List<long> differingSectors)
    {
        return new FullGptAuditResult
        {
            Entries = new List<GptPartitionAuditEntry>
            {
                new()
                {
                    PartitionName = partitionName,
                    Status = status,
                    PartitionSize = 100 * 512,
                    DifferingSectorCount = differingSectors.Count,
                    DifferingSectorAddresses = differingSectors,
                },
            },
        };
    }

    // ─── Test 1: Only differing sectors written, with SECTOR NUMBERS (not byte offsets) ──

    [Fact]
    public async Task SparseWrite_OnlyDifferingSectorsWritten()
    {
        // Arrange
        const int sectorCount = 100;
        const string partitionName = "boot_a";

        var partitionData = CreatePartitionData(sectorCount);
        // Tamper sector 1, 3, 5 — bytes at offsets 512, 1536, 2560
        partitionData[512] = 0xDE;
        partitionData[1536] = 0xAD;
        partitionData[2560] = 0xBE;

        // Stock backup has correct sectors 1, 3, 5
        var stockData = CreatePartitionData(sectorCount);
        File.WriteAllBytes(Path.Combine(_testBackupDir, $"{partitionName}.img"), stockData);
        WriteValidManifestForAllStockFiles();

        var client = CreateMockClient(partitionName, partitionData);
        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report, force: false, dryRun: false);

        // Differing byte offsets: 512, 1536, 2560 → expected sector numbers: 1, 3, 5
        var audit = BuildAudit(partitionName, GptAuditStatus.Mismatch,
            new List<long> { 512, 1536, 2560 });

        // Act
        var result = await restorer.SparseRepairAsync(audit);

        // Assert
        Assert.Equal(3, client.WriteBlocksCallCount);
        Assert.Contains(client.WriteBlocksCalls, c => c.Partition == partitionName && c.StartSector == 1L && c.DataLength == 512);
        Assert.Contains(client.WriteBlocksCalls, c => c.Partition == partitionName && c.StartSector == 3L && c.DataLength == 512);
        Assert.Contains(client.WriteBlocksCalls, c => c.Partition == partitionName && c.StartSector == 5L && c.DataLength == 512);

        // Verify byte offsets were NOT passed (the brick prevention check)
        Assert.DoesNotContain(client.WriteBlocksCalls, c => c.StartSector == 512L);
        Assert.DoesNotContain(client.WriteBlocksCalls, c => c.StartSector == 1536L);
        Assert.DoesNotContain(client.WriteBlocksCalls, c => c.StartSector == 2560L);
    }

    // ─── Test 2: Verification after write confirms Repaired status ──

    [Fact]
    public async Task SparseWrite_VerificationAfterWrite_RepairsConfirmed()
    {
        // Arrange
        const string partitionName = "system_a";

        // Stock data — sectors 2 and 7 have known good content
        var stockData = CreatePartitionData(20);
        File.WriteAllBytes(Path.Combine(_testBackupDir, $"{partitionName}.img"), stockData);
        WriteValidManifestForAllStockFiles();

        // Partition data — start identical to stock, then sparse repair will read/write to verify
        var partitionData = CreatePartitionData(20);
        var client = CreateMockClient(partitionName, partitionData);
        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report, force: false, dryRun: false);

        // Differing sectors: byte offsets 1024 (sector 2), 3584 (sector 7)
        var audit = BuildAudit(partitionName, GptAuditStatus.Mismatch,
            new List<long> { 1024, 3584 });

        // Act
        var result = await restorer.SparseRepairAsync(audit);

        // Assert
        var partResult = Assert.Single(result.Partitions);
        Assert.Equal(partitionName, partResult.PartitionName);
        Assert.Equal(SparseRepairStatus.Repaired, partResult.Status);
        Assert.Equal(2, partResult.SectorsWritten);
        Assert.Equal(2, partResult.SectorsVerified);
        Assert.Equal(2, result.TotalSectorsWritten);
        Assert.Equal(1, result.TotalPartitionsRepaired);
        Assert.Equal(0, result.TotalPartitionsFailed);
    }

    // ─── Test 3: Blocklist enforced without --force ──

    [Fact]
    public async Task SparseWrite_BlocklistEnforced_NoWriteWithoutForce()
    {
        // Arrange
        const string partitionName = "sec"; // BLOCKLISTED

        var partitionData = CreatePartitionData(10);
        partitionData[512] = 0xFF; // sector 1 differs
        partitionData[1024] = 0xFF; // sector 2 differs

        var stockData = CreatePartitionData(10);
        File.WriteAllBytes(Path.Combine(_testBackupDir, $"{partitionName}.img"), stockData);

        var client = CreateMockClient(partitionName, partitionData);
        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report, force: false, dryRun: false);

        var audit = BuildAudit(partitionName, GptAuditStatus.Mismatch,
            new List<long> { 512, 1024 });

        // Act
        var result = await restorer.SparseRepairAsync(audit);

        // Assert
        Assert.Equal(0, client.WriteBlocksCallCount);
        var partResult = Assert.Single(result.Partitions);
        Assert.Equal(SparseRepairStatus.Blocklisted, partResult.Status);
        Assert.Equal(0, partResult.SectorsWritten);
        Assert.Equal(1, result.TotalPartitionsSkipped);
    }

    // ─── Test 4: Dry-run makes ZERO write calls ──

    [Fact]
    public async Task SparseWrite_DryRun_ZeroWriteCalls()
    {
        // Arrange
        const string p1 = "boot_a";
        const string p2 = "system_a";

        // 5 mismatched sectors across 2 partitions
        var partitionData1 = CreatePartitionData(10);
        partitionData1[512] = 0xDE;    // sector 1
        partitionData1[1536] = 0xAD;   // sector 3

        var partitionData2 = CreatePartitionData(10);
        partitionData2[2048] = 0xBE;   // sector 4
        partitionData2[2560] = 0xEF;   // sector 5
        partitionData2[3072] = 0xCA;   // sector 6

        File.WriteAllBytes(Path.Combine(_testBackupDir, $"{p1}.img"), CreatePartitionData(10));
        File.WriteAllBytes(Path.Combine(_testBackupDir, $"{p2}.img"), CreatePartitionData(10));

        var client = new MockFirehoseClient
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
                new() { Name = p1, StartSector = 0, Sectors = 10 },
                new() { Name = p2, StartSector = 10, Sectors = 10 },
            },
            PartitionData = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [p1] = partitionData1,
                [p2] = partitionData2,
            },
        };

        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report, force: false, dryRun: true);

        var audit = new FullGptAuditResult
        {
            Entries = new List<GptPartitionAuditEntry>
            {
                new() { PartitionName = p1, Status = GptAuditStatus.Mismatch, DifferingSectorCount = 2,
                    DifferingSectorAddresses = new List<long> { 512, 1536 } },
                new() { PartitionName = p2, Status = GptAuditStatus.Mismatch, DifferingSectorCount = 3,
                    DifferingSectorAddresses = new List<long> { 2048, 2560, 3072 } },
            },
        };

        // Act
        var result = await restorer.SparseRepairAsync(audit);

        // Assert — zero destructive calls
        Assert.Equal(0, client.WriteBlocksCallCount);
        Assert.Equal(0, client.WriteCalls.Count);
        Assert.Equal(0, client.EraseCalls.Count);
        Assert.True(result.DryRun);
        Assert.Equal(0, result.TotalSectorsWritten);
        Assert.Equal(2, result.Partitions.Count);

        // Planned write information is logged (sector addresses populated)
        var p1Result = result.Partitions.Find(p => p.PartitionName == p1);
        Assert.NotNull(p1Result);
        Assert.Equal(2, p1Result!.SectorsWritten);
        Assert.Contains(512L, p1Result.WrittenSectorAddresses);
        Assert.Contains(1536L, p1Result.WrittenSectorAddresses);

        var p2Result = result.Partitions.Find(p => p.PartitionName == p2);
        Assert.NotNull(p2Result);
        Assert.Equal(3, p2Result!.SectorsWritten);
    }

    // ─── Test 5: Stock image missing → graceful skip ──

    [Fact]
    public async Task SparseWrite_StockImageNotFound_GracefulSkip()
    {
        // Arrange
        const string partitionName = "vendor_a";

        var partitionData = CreatePartitionData(10);
        partitionData[512] = 0xDE; // sector 1 differs

        // NOTE: no stock file written to backup directory

        var client = CreateMockClient(partitionName, partitionData);
        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report, force: false, dryRun: false);

        var audit = BuildAudit(partitionName, GptAuditStatus.Mismatch,
            new List<long> { 512 });

        // Act — should NOT throw
        var result = await restorer.SparseRepairAsync(audit);

        // Assert
        Assert.Equal(0, client.WriteBlocksCallCount);
        var partResult = Assert.Single(result.Partitions);
        Assert.Equal(SparseRepairStatus.NoStockImage, partResult.Status);
        Assert.Equal(0, partResult.SectorsWritten);
        Assert.NotNull(partResult.ErrorMessage);
        Assert.Contains("NO STOCK IMAGE", partResult.ErrorMessage);
        Assert.Equal(1, result.TotalPartitionsSkipped);
    }
}