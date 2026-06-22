using BACKRabbit.Protocol.Firehose.Rescue;
using System.Text;

namespace BACKRabbit.Protocol.Firehose.Tests.Rescue;

/// <summary>
/// Tests for the full-GPT audit feature (Sub-step A).
/// Verifies that RunFullGptAuditAsync correctly iterates all GPT partitions,
/// compares against stock firmware, and flags mismatches with block-level diff.
/// Also verifies dry-run safety: audit runs but zero destructive calls occur.
/// </summary>
public class FullGptAuditTests : IDisposable
{
    private readonly string _testBackupDir;

    public FullGptAuditTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), $"backrabbit_audit_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBackupDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBackupDir))
            Directory.Delete(_testBackupDir, recursive: true);
    }

    // ─── Helpers ─────────────────────────────────────────────

    private static byte[] CreateSectorData(int sectorCount, byte fillValue = 0)
    {
        var data = new byte[sectorCount * 512];
        for (int i = 0; i < data.Length; i++) data[i] = fillValue;
        return data;
    }

    private void CreateBackupFile(string name, byte[] data)
    {
        File.WriteAllBytes(Path.Combine(_testBackupDir, $"{name}.img"), data);
    }

    /// <summary>
    /// Create a mock client with 6 GPT partitions, all readable.
    /// Partitions: boot_a, boot_b, vbmeta_a, userdata, persist, devinfo
    /// </summary>
    private MockFirehoseClient CreateMockClientWith6Partitions()
    {
        return new MockFirehoseClient
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
                new() { Name = "boot_a", StartSector = 0, Sectors = 128 },
                new() { Name = "boot_b", StartSector = 128, Sectors = 128 },
                new() { Name = "vbmeta_a", StartSector = 256, Sectors = 8 },
                new() { Name = "userdata", StartSector = 264, Sectors = 1024 },
                new() { Name = "persist", StartSector = 1288, Sectors = 32 },
                new() { Name = "devinfo", StartSector = 1320, Sectors = 1 },
            },
            PartitionData = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["boot_a"] = CreateSectorData(128, 0xAA),
                ["boot_b"] = CreateSectorData(128, 0xBB),
                ["vbmeta_a"] = CreateSectorData(8, 0xCC),
                ["userdata"] = CreateSectorData(100, 0xDD),
                ["persist"] = CreateSectorData(32, 0xEE),
                ["devinfo"] = Encoding.ASCII.GetBytes("ANDROID-BOOT!\0\0").Concat(new byte[500]).ToArray(),
            },
            FuseData = new Dictionary<uint, byte[]>
            {
                [0x00780020] = new byte[] { 0x01, 0x00, 0x00, 0x00 },
                [0x00780024] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x00780028] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x0078002C] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x00780030] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x00780034] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x00780038] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
            },
        };
    }

    // ─── TEST 1: All partitions checked ──────────────────────

    [Fact]
    public async Task FullGptAudit_AllPartitionsChecked()
    {
        // Arrange — 6 partitions in GPT, 3 with stock backup files
        var mockClient = CreateMockClientWith6Partitions();

        // Create stock backup files for boot_a, boot_b, persist (matching device data)
        CreateBackupFile("boot_a", CreateSectorData(128, 0xAA));
        CreateBackupFile("boot_b", CreateSectorData(128, 0xBB));
        CreateBackupFile("persist", CreateSectorData(32, 0xEE));

        var report = new RescueReport();
        var diagnostics = new PartitionDiagnostics(mockClient, report, _testBackupDir);

        // Act
        var audit = await diagnostics.RunFullGptAuditAsync();

        // Assert — all 6 partitions are in the audit result
        Assert.Equal(6, audit.TotalPartitions);

        // All 6 partitions were read from the device
        Assert.Contains("boot_a", mockClient.ReadCalls);
        Assert.Contains("boot_b", mockClient.ReadCalls);
        Assert.Contains("vbmeta_a", mockClient.ReadCalls);
        Assert.Contains("userdata", mockClient.ReadCalls);
        Assert.Contains("persist", mockClient.ReadCalls);
        Assert.Contains("devinfo", mockClient.ReadCalls);

        // Each entry has a DeviceSha256 (was hashed)
        Assert.All(audit.Entries, e => Assert.False(string.IsNullOrEmpty(e.DeviceSha256)));

        // Partitions with matching stock files should be Match
        var bootA = audit.Entries.Find(e => e.PartitionName == "boot_a");
        Assert.NotNull(bootA);
        Assert.Equal(GptAuditStatus.Match, bootA!.Status);

        var bootB = audit.Entries.Find(e => e.PartitionName == "boot_b");
        Assert.NotNull(bootB);
        Assert.Equal(GptAuditStatus.Match, bootB!.Status);

        // Partitions without stock files should be NoStockComparison
        var vbmetaA = audit.Entries.Find(e => e.PartitionName == "vbmeta_a");
        Assert.NotNull(vbmetaA);
        Assert.Equal(GptAuditStatus.NoStockComparison, vbmetaA!.Status);

        var userdata = audit.Entries.Find(e => e.PartitionName == "userdata");
        Assert.NotNull(userdata);
        Assert.Equal(GptAuditStatus.NoStockComparison, userdata!.Status);
    }

    // ─── TEST 2: Mismatched partition flagged with sector details ─

    [Fact]
    public async Task FullGptAudit_MismatchedPartition_Flagged()
    {
        // Arrange — boot_a on device differs from stock backup
        var mockClient = CreateMockClientWith6Partitions();

        // Stock boot_a: 128 sectors of 0xAA
        var stockBootA = CreateSectorData(128, 0xAA);
        CreateBackupFile("boot_a", stockBootA);

        // Device boot_a: same size but sectors 1, 3, 5 are different
        var deviceBootA = CreateSectorData(128, 0xAA);
        // Corrupt sector 1 (offset 512-1023)
        for (int i = 512; i < 1024; i++) deviceBootA[i] = 0xFF;
        // Corrupt sector 3 (offset 1536-2047)
        for (int i = 1536; i < 2048; i++) deviceBootA[i] = 0xFF;
        // Corrupt sector 5 (offset 2560-3071)
        for (int i = 2560; i < 3072; i++) deviceBootA[i] = 0xFF;
        mockClient.PartitionData["boot_a"] = deviceBootA;

        // Also create matching backups for other partitions to isolate the test
        CreateBackupFile("boot_b", CreateSectorData(128, 0xBB));
        CreateBackupFile("persist", CreateSectorData(32, 0xEE));

        var report = new RescueReport();
        var diagnostics = new PartitionDiagnostics(mockClient, report, _testBackupDir);

        // Act
        var audit = await diagnostics.RunFullGptAuditAsync();

        // Assert — boot_a should be Mismatch
        var bootA = audit.Entries.Find(e => e.PartitionName == "boot_a");
        Assert.NotNull(bootA);
        Assert.Equal(GptAuditStatus.Mismatch, bootA!.Status);

        // Device and stock hashes should differ
        Assert.NotEqual(bootA.DeviceSha256, bootA.StockSha256);

        // Differing sector count should be 3 (sectors 1, 3, 5)
        Assert.True(bootA.DifferingSectorCount >= 3,
            $"Expected at least 3 differing sectors, got {bootA.DifferingSectorCount}");

        // Differing sector addresses should contain offsets for sectors 1, 3, 5
        // (offset = sector_index * 512)
        Assert.Contains(1 * 512, bootA.DifferingSectorAddresses);
        Assert.Contains(3 * 512, bootA.DifferingSectorAddresses);
        Assert.Contains(5 * 512, bootA.DifferingSectorAddresses);

        // boot_b should still be Match (not affected)
        var bootB = audit.Entries.Find(e => e.PartitionName == "boot_b");
        Assert.NotNull(bootB);
        Assert.Equal(GptAuditStatus.Match, bootB!.Status);

        // MismatchCount should be exactly 1
        Assert.Equal(1, audit.MismatchCount);
    }

    // ─── TEST 3: No stock comparison flagged ─────────────────

    [Fact]
    public async Task FullGptAudit_NoStockComparison_Flagged()
    {
        // Arrange — userdata and vbmeta_a have no stock backup files
        var mockClient = CreateMockClientWith6Partitions();

        // Only create backup for boot_a and persist — NOT for userdata, vbmeta_a, boot_b, devinfo
        CreateBackupFile("boot_a", CreateSectorData(128, 0xAA));
        CreateBackupFile("persist", CreateSectorData(32, 0xEE));

        var report = new RescueReport();
        var diagnostics = new PartitionDiagnostics(mockClient, report, _testBackupDir);

        // Act
        var audit = await diagnostics.RunFullGptAuditAsync();

        // Assert — partitions without stock backup should be NoStockComparison
        var userdata = audit.Entries.Find(e => e.PartitionName == "userdata");
        Assert.NotNull(userdata);
        Assert.Equal(GptAuditStatus.NoStockComparison, userdata!.Status);
        Assert.Null(userdata.StockSha256);
        Assert.Equal(0, userdata.DifferingSectorCount);

        var vbmetaA = audit.Entries.Find(e => e.PartitionName == "vbmeta_a");
        Assert.NotNull(vbmetaA);
        Assert.Equal(GptAuditStatus.NoStockComparison, vbmetaA!.Status);
        Assert.Null(vbmetaA.StockSha256);

        var bootB = audit.Entries.Find(e => e.PartitionName == "boot_b");
        Assert.NotNull(bootB);
        Assert.Equal(GptAuditStatus.NoStockComparison, bootB!.Status);

        var devinfo = audit.Entries.Find(e => e.PartitionName == "devinfo");
        Assert.NotNull(devinfo);
        Assert.Equal(GptAuditStatus.NoStockComparison, devinfo!.Status);

        // NoStockComparisonCount should be 4 (boot_b, vbmeta_a, userdata, devinfo)
        Assert.Equal(4, audit.NoStockComparisonCount);

        // Match count should be 2 (boot_a, persist)
        Assert.Equal(2, audit.MatchCount);

        // No mismatches
        Assert.Equal(0, audit.MismatchCount);
    }

    // ─── TEST 4: Dry-run — audit runs but zero destructive calls ─

    [Fact]
    public async Task DryRun_FullGptAudit_RunsButNoDestructiveCalls()
    {
        // Arrange — full rescue in dry-run mode with tampered partition
        var mockClient = CreateMockClientWith6Partitions();

        // Create stock boot_a that differs from device data (mismatch)
        var stockBootA = CreateSectorData(128, 0xAA);
        CreateBackupFile("boot_a", stockBootA);

        // Tamper device boot_a
        var tamperedBootA = CreateSectorData(128, 0xAA);
        for (int i = 512; i < 1024; i++) tamperedBootA[i] = 0xFF;
        mockClient.PartitionData["boot_a"] = tamperedBootA;

        // Create matching backups for other stock-comparable partitions
        CreateBackupFile("boot_b", CreateSectorData(128, 0xBB));
        CreateBackupFile("persist", CreateSectorData(32, 0xEE));

        var orchestrator = new RescueOrchestrator(
            mockClient, _testBackupDir,
            dryRun: true,
            skipDownloadModeCheck: true);

        // Act
        var report = await orchestrator.RunFullRescueAsync();

        // Assert — dry-run mode
        Assert.True(report.IsDryRun);

        // Full-GPT audit ran and is in the report
        Assert.NotNull(report.FullGptAudit);
        Assert.True(report.FullGptAudit!.TotalPartitions >= 6);

        // The audit detected the mismatch
        Assert.True(report.FullGptAudit.MismatchCount >= 1,
            $"Expected at least 1 mismatch, got {report.FullGptAudit.MismatchCount}");

        // ZERO destructive calls — this is the critical safety assertion
        Assert.Empty(mockClient.WriteCalls);
        Assert.Empty(mockClient.EraseCalls);
        Assert.Empty(mockClient.ResetCalls);

        // Reads DID happen (audit requires reading partitions)
        Assert.NotEmpty(mockClient.ReadCalls);
    }
}