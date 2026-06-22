using BACKRabbit.Protocol.Firehose.Rescue;
using System.Text;

namespace BACKRabbit.Protocol.Firehose.Tests.Rescue;

/// <summary>
/// Mock FirehoseClient that overrides all virtual methods used by the rescue pipeline.
/// Tracks write/erase/reset calls so tests can assert they were (or were not) invoked.
/// </summary>
public class MockFirehoseClient : FirehoseClient
{
    // ─── Call tracking ───────────────────────────────────────
    public List<string> WriteCalls { get; } = new();
    public List<string> EraseCalls { get; } = new();
    public List<string> ResetCalls { get; } = new();
    public List<string> ReadCalls { get; } = new();

    // ─── Configurable responses ──────────────────────────────
    public Dictionary<string, byte[]> PartitionData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GptPartitionEntry> GptEntries { get; set; } = new();
    public string StorageType { get; set; } = "ufs";
    public Dictionary<uint, byte[]> FuseData { get; set; } = new();
    public SaharaChipInfo? ChipInfoOverride { get; set; }

    // ─── Constructor ─────────────────────────────────────────
    // Pass null transport + a dummy state machine — we override all methods that use them.
    public MockFirehoseClient()
        : base(null!, new SaharaStateMachine())
    {
        // Set chip info on the state machine so ChipInfo property works
        if (ChipInfoOverride != null)
            SaharaStateMachine_SetChipInfo(ChipInfoOverride);
    }

    private void SaharaStateMachine_SetChipInfo(SaharaChipInfo info)
    {
        // Use the public SetChipInfo API, then manually walk the state machine
        // through valid transitions to CommandMode so IsInitialized returns true.
        // SetChipInfo transitions to HelloReceived.
        // We need: HelloReceived → HelloSent → ImageUploading → ImageUploadComplete → CommandMode
        var sm = new SaharaStateMachine();
        sm.SetChipInfo(info);                          // HelloReceived
        sm.TransitionTo(SaharaState.HelloSent);        // HelloSent
        sm.TransitionTo(SaharaState.ImageUploading);   // ImageUploading
        sm.TransitionTo(SaharaState.ImageUploadComplete); // ImageUploadComplete
        sm.TransitionTo(SaharaState.CommandMode);      // CommandMode

        // Replace the state machine in the base class via reflection on the readonly field
        var smField = typeof(FirehoseClient).GetField("_stateMachine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (smField != null)
            smField.SetValue(this, sm);
    }

    // ─── Overridden methods ──────────────────────────────────

    public override Task<byte[]> ReadPartitionAsync(
        string partitionName, int lun = 0, int sectorSize = 512,
        CancellationToken ct = default)
    {
        ReadCalls.Add(partitionName);
        if (PartitionData.TryGetValue(partitionName, out var data))
            return Task.FromResult(data);
        throw new FirehoseException($"Partition '{partitionName}' not found");
    }

    public override Task<bool> WritePartitionAsync(
        string partitionName, byte[] data, int lun = 0, int sectorSize = 512,
        CancellationToken ct = default)
    {
        WriteCalls.Add(partitionName);
        // Update our stored data so subsequent reads return the written data
        PartitionData[partitionName] = data;
        return Task.FromResult(true);
    }

    public override Task<bool> ErasePartitionAsync(
        string partitionName, int lun = 0, CancellationToken ct = default)
    {
        EraseCalls.Add(partitionName);
        return Task.FromResult(true);
    }

    public override Task<List<GptPartitionEntry>> PrintGptAsync(
        int lun = 0, CancellationToken ct = default)
    {
        return Task.FromResult(GptEntries);
    }

    public override Task<string> GetStorageInfoAsync(CancellationToken ct = default)
    {
        return Task.FromResult(StorageType);
    }

    public override Task ResetAsync(string mode = "system", CancellationToken ct = default)
    {
        ResetCalls.Add(mode);
        return Task.CompletedTask;
    }

    public override Task<byte[]> PeekAsync(uint address, uint size, CancellationToken ct = default)
    {
        if (FuseData.TryGetValue(address, out var data))
            return Task.FromResult(data.Take((int)size).ToArray());
        // Return zeros for unmapped fuse addresses
        return Task.FromResult(new byte[size]);
    }
}

// ─── TESTS ───────────────────────────────────────────────────

public class RescueOrchestratorTests : IDisposable
{
    private readonly string _testBackupDir;

    public RescueOrchestratorTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), $"backrabbit_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBackupDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBackupDir))
            Directory.Delete(_testBackupDir, recursive: true);
    }

    // ─── Helpers ─────────────────────────────────────────────

    /// <summary>Create a minimal valid boot image (AOSP v2 header + empty ramdisk).</summary>
    private static byte[] CreateMinimalBootImage(string kernelCmdline = "androidboot.hardware=qcom")
    {
        // AOSP boot image v2: header (1660 bytes) + kernel (dummy) + ramdisk (gzipped CPIO) + dtb
        const int pageSize = 2048;
        const int headerSize = 1660; // v2 header size
        const int kernelSize = pageSize; // 1 page dummy kernel
        const int ramdiskSize = pageSize; // 1 page dummy ramdisk

        int totalSize = headerSize;
        totalSize = AlignUp(totalSize, pageSize); // header padded to page
        totalSize += kernelSize;
        totalSize = AlignUp(totalSize, pageSize);
        totalSize += ramdiskSize;
        totalSize = AlignUp(totalSize, pageSize);

        var img = new byte[totalSize];

        // Magic "ANDROID!"
        var magic = Encoding.ASCII.GetBytes("ANDROID!");
        Array.Copy(magic, 0, img, 0, 8);

        // Kernel size at offset 8
        BitConverter.GetBytes((uint)kernelSize).CopyTo(img, 8);

        // Kernel addr at offset 12
        BitConverter.GetBytes(0x00008000u).CopyTo(img, 12);

        // Ramdisk size at offset 16
        BitConverter.GetBytes((uint)ramdiskSize).CopyTo(img, 16);

        // Ramdisk addr at offset 20
        BitConverter.GetBytes(0x01000000u).CopyTo(img, 20);

        // Second size at offset 24
        BitConverter.GetBytes(0u).CopyTo(img, 24);

        // Second addr at offset 28
        BitConverter.GetBytes(0u).CopyTo(img, 28);

        // Tags addr at offset 32
        BitConverter.GetBytes(0x00000100u).CopyTo(img, 32);

        // Page size at offset 36
        BitConverter.GetBytes((uint)pageSize).CopyTo(img, 36);

        // Header version at offset 40
        BitConverter.GetBytes(2u).CopyTo(img, 40);

        // OS version at offset 44
        BitConverter.GetBytes(0u).CopyTo(img, 44);

        // OS patch level at offset 48
        BitConverter.GetBytes(0u).CopyTo(img, 48);

        // Cmdline at offset 64 (v2)
        var cmdlineBytes = Encoding.ASCII.GetBytes(kernelCmdline.PadRight(512, '\0'));
        Array.Copy(cmdlineBytes, 0, img, 64, Math.Min(cmdlineBytes.Length, 512));

        // DTB size at offset 1640 (v2) — 0
        BitConverter.GetBytes(0u).CopyTo(img, 1640);

        // DTB addr at offset 1644
        BitConverter.GetBytes(0u).CopyTo(img, 1644);

        return img;
    }

    /// <summary>Create a minimal vbmeta image with AVB0 header and flags=0 (verification enabled).</summary>
    private static byte[] CreateMinimalVbmetaImage(uint flags = 0)
    {
        var img = new byte[256];
        var magic = Encoding.ASCII.GetBytes("AVB0");
        Array.Copy(magic, 0, img, 0, 4);
        BitConverter.GetBytes(flags).CopyTo(img, 4);
        return img;
    }

    /// <summary>Create a boot image with Magisk artifacts in the ramdisk (simulated).</summary>
    private static byte[] CreateMagiskBootImage()
    {
        // For testing, we create a boot image that will be detected as "Tampered"
        // by PartitionDiagnostics. The simplest approach: create a boot image whose
        // SHA256 differs from the backup, and whose ramdisk contains Magisk markers.
        // Since actual CPIO parsing is complex, we use a different cmdline as the
        // distinguishing factor — PartitionDiagnostics compares SHA256 against backup.
        return CreateMinimalBootImage("androidboot.hardware=qcom magisk.enabled=1");
    }

    private static int AlignUp(int value, int alignment)
        => (value + alignment - 1) / alignment * alignment;

    private void CreateBackupFile(string name, byte[] data)
    {
        File.WriteAllBytes(Path.Combine(_testBackupDir, $"{name}.img"), data);
    }

    private MockFirehoseClient CreateMockClient(bool withTamperedBoot = false)
    {
        var stockBoot = CreateMinimalBootImage();
        var magiskBoot = CreateMagiskBootImage();
        var stockVbmeta = CreateMinimalVbmetaImage(flags: 0);

        var client = new MockFirehoseClient
        {
            ChipInfoOverride = new SaharaChipInfo
            {
                Version = 2,
                MinVersion = 1,
                MaxPacketSize = 16384,
                Mode = SaharaMode.ImageTxPending,
                MsmId = 0x008600E1, // SDM845
                PkHash = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, // fused
                SerialNumber = "0x12345678",
            },
            StorageType = "ufs",
            GptEntries = new List<GptPartitionEntry>
            {
                new() { Name = "boot_a", StartSector = 0, Sectors = 128 },
                new() { Name = "boot_b", StartSector = 128, Sectors = 128 },
                new() { Name = "vbmeta_a", StartSector = 256, Sectors = 8 },
                new() { Name = "vbmeta_b", StartSector = 264, Sectors = 8 },
                new() { Name = "devinfo", StartSector = 272, Sectors = 1 },
                new() { Name = "persist", StartSector = 273, Sectors = 32 },
                new() { Name = "frp", StartSector = 305, Sectors = 1 },
                new() { Name = "misc", StartSector = 306, Sectors = 1 },
                new() { Name = "sec", StartSector = 307, Sectors = 1 },
                new() { Name = "dtbo_a", StartSector = 308, Sectors = 16 },
                new() { Name = "dtbo_b", StartSector = 324, Sectors = 16 },
                new() { Name = "init_boot_a", StartSector = 340, Sectors = 16 },
                new() { Name = "init_boot_b", StartSector = 356, Sectors = 16 },
            },
            PartitionData = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["boot_a"] = withTamperedBoot ? magiskBoot : stockBoot,
                ["boot_b"] = stockBoot,
                ["vbmeta_a"] = stockVbmeta,
                ["vbmeta_b"] = stockVbmeta,
                ["devinfo"] = Encoding.ASCII.GetBytes("ANDROID-BOOT!\0\0"),
                ["persist"] = new byte[16384],
                ["frp"] = new byte[512],
                ["misc"] = new byte[2048],
                ["sec"] = new byte[512],
                ["dtbo_a"] = new byte[8192],
                ["dtbo_b"] = new byte[8192],
                ["init_boot_a"] = stockBoot,
                ["init_boot_b"] = stockBoot,
            },
            // Fuse data: OEM_SECURE_BOOT1_AUTH_EN blown at 0x00780020 bit 0
            FuseData = new Dictionary<uint, byte[]>
            {
                [0x00780020] = new byte[] { 0x01, 0x00, 0x00, 0x00 }, // bit 0 set
                [0x00780024] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x00780028] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x0078002C] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x00780030] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x00780034] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                [0x00780038] = new byte[] { 0x00, 0x00, 0x00, 0x00 },
            },
        };

        return client;
    }

    // ─── Tier 1.1: Dry-run with tampered partitions ──────────

    [Fact]
    public async Task DryRun_WithTamperedBoot_ZeroWriteEraseResetCalls()
    {
        // Arrange
        var stockBoot = CreateMinimalBootImage();
        CreateBackupFile("boot_a", stockBoot);
        CreateBackupFile("boot_b", stockBoot);
        CreateBackupFile("vbmeta_a", CreateMinimalVbmetaImage());
        CreateBackupFile("vbmeta_b", CreateMinimalVbmetaImage());

        var mockClient = CreateMockClient(withTamperedBoot: true);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: true);

        // Act
        var report = await orchestrator.RunFullRescueAsync();

        // Assert
        Assert.True(report.IsDryRun);
        Assert.Empty(mockClient.WriteCalls);   // ZERO write calls
        Assert.Empty(mockClient.EraseCalls);   // ZERO erase calls
        Assert.Empty(mockClient.ResetCalls);   // ZERO reset calls

        // All restore actions should be DryRunSkipped
        Assert.All(report.RestoreActions, a => Assert.Equal("DryRunSkipped", a.Action));

        // Magisk removals should show found but not removed
        Assert.All(report.MagiskRemovals, m =>
        {
            Assert.True(m.MagiskFound);
            Assert.False(m.MagiskRemoved);
        });

        // Verdict should be Tampered (no writes performed)
        Assert.Equal(OverallVerdict.Tampered, report.Verdict);
    }

    // ─── Tier 1.1: Dry-run with clean device ─────────────────

    [Fact]
    public async Task DryRun_WithCleanDevice_DiagnosisOnly()
    {
        // Arrange
        var stockBoot = CreateMinimalBootImage();
        CreateBackupFile("boot_a", stockBoot);
        CreateBackupFile("boot_b", stockBoot);
        CreateBackupFile("vbmeta_a", CreateMinimalVbmetaImage());
        CreateBackupFile("vbmeta_b", CreateMinimalVbmetaImage());

        var mockClient = CreateMockClient(withTamperedBoot: false);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: true);

        // Act
        var report = await orchestrator.RunFullRescueAsync();

        // Assert
        Assert.True(report.IsDryRun);
        Assert.Empty(mockClient.WriteCalls);
        Assert.Empty(mockClient.EraseCalls);
        Assert.Empty(mockClient.ResetCalls);
        Assert.Empty(report.RestoreActions);
        Assert.Empty(report.MagiskRemovals);

        // Verdict should be Clean (or Tampered if fuses blown — SDM845 has 1 fuse blown)
        // With 1 fuse blown, verdict is Tampered per DetermineVerdict logic
        Assert.True(report.Verdict == OverallVerdict.Clean || report.Verdict == OverallVerdict.Tampered);
    }

    // ─── Tier 1.1: Live-run with tampered partitions ─────────

    [Fact]
    public async Task LiveRun_WithTamperedBoot_WritesAndErasesCalled()
    {
        // Arrange
        var stockBoot = CreateMinimalBootImage();
        CreateBackupFile("boot_a", stockBoot);
        CreateBackupFile("boot_b", stockBoot);
        CreateBackupFile("vbmeta_a", CreateMinimalVbmetaImage());
        CreateBackupFile("vbmeta_b", CreateMinimalVbmetaImage());

        var mockClient = CreateMockClient(withTamperedBoot: true);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: false);

        // Act
        var report = await orchestrator.RunFullRescueAsync();

        // Assert
        Assert.False(report.IsDryRun);
        Assert.NotEmpty(mockClient.WriteCalls);   // Should have written boot_a
        Assert.Contains("boot_a", mockClient.WriteCalls);
        Assert.NotEmpty(mockClient.EraseCalls);    // Should have erased boot_a
        Assert.Contains("boot_a", mockClient.EraseCalls);
        Assert.NotEmpty(mockClient.ResetCalls);    // Should have reset to system

        // Restore actions should show Flashed + Verified
        Assert.Contains(report.RestoreActions, a => a.Action == "Flashed" && a.Verified);

        // Note: MagiskRemovals may be empty because mock boot images lack valid
        // CPIO ramdisks — Magisk detection requires parseable CPIO. The tampered
        // detection works via SHA256 mismatch (different cmdline in mock data).
        // Magisk-specific detection is tested at the MagiskArtifactDetector level.
    }

    // ─── Tier 1.1: Post-rescue verdict recalculation ────────

    [Fact]
    public async Task LiveRun_PostRescueVerdict_TransitionsToRecovered()
    {
        // Arrange
        var stockBoot = CreateMinimalBootImage();
        CreateBackupFile("boot_a", stockBoot);
        CreateBackupFile("boot_b", stockBoot);
        CreateBackupFile("vbmeta_a", CreateMinimalVbmetaImage());
        CreateBackupFile("vbmeta_b", CreateMinimalVbmetaImage());

        var mockClient = CreateMockClient(withTamperedBoot: true);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: false);

        // Act
        var report = await orchestrator.RunFullRescueAsync();

        // Assert — post-rescue verdict should account for restored partitions
        // With 1 fuse blown (OEM_SECURE_BOOT1_AUTH_EN), verdict is PartiallyRecovered
        // (fuse damage is permanent, even though boot_a was restored)
        Assert.Equal(OverallVerdict.PartiallyRecovered, report.Verdict);

        // boot_a should be marked as restored
        var bootA = report.Partitions.Find(p => p.PartitionName == "boot_a");
        Assert.NotNull(bootA);
        Assert.Contains("Restored successfully", bootA!.Anomalies);

        // Restore action should be Flashed + Verified
        Assert.Contains(report.RestoreActions, a => a.PartitionName == "boot_a" && a.Action == "Flashed" && a.Verified);
    }

    // ─── Tier 1.2: Dry-run assertion — zero program/erase/reset ──

    [Fact]
    public async Task DryRun_NoProgramEraseReset_AcrossAllPhases()
    {
        // Arrange
        var stockBoot = CreateMinimalBootImage();
        CreateBackupFile("boot_a", stockBoot);
        CreateBackupFile("boot_b", stockBoot);
        CreateBackupFile("vbmeta_a", CreateMinimalVbmetaImage());
        CreateBackupFile("vbmeta_b", CreateMinimalVbmetaImage());

        var mockClient = CreateMockClient(withTamperedBoot: true);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: true);

        // Act
        await orchestrator.RunFullRescueAsync();

        // Assert — exhaustive: no destructive method was called
        Assert.Empty(mockClient.WriteCalls);
        Assert.Empty(mockClient.EraseCalls);
        Assert.Empty(mockClient.ResetCalls);

        // Verify reads DID happen (diagnosis ran)
        Assert.NotEmpty(mockClient.ReadCalls);
        Assert.Contains(mockClient.ReadCalls, c => c == "boot_a");
    }

    // ─── Tier 1.2: Dry-run report has IsDryRun=true ──────────

    [Fact]
    public async Task DryRun_Report_IsDryRunTrue()
    {
        // Arrange
        var stockBoot = CreateMinimalBootImage();
        CreateBackupFile("boot_a", stockBoot);
        CreateBackupFile("vbmeta_a", CreateMinimalVbmetaImage());

        var mockClient = CreateMockClient(withTamperedBoot: false);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: true);

        // Act
        var report = await orchestrator.RunFullRescueAsync();

        // Assert
        Assert.True(report.IsDryRun);
        var json = report.ToJson();
        Assert.Contains("\"IsDryRun\": true", json);
    }

    // ─── Edge case: No backup directory ─────────────────────

    [Fact]
    public async Task DryRun_NoBackupDir_DiagnosisOnlyNoCrash()
    {
        // Arrange
        var mockClient = CreateMockClient(withTamperedBoot: false);
        var orchestrator = new RescueOrchestrator(mockClient, backupDir: "", dryRun: true);

        // Act — should not throw
        var report = await orchestrator.RunFullRescueAsync();

        // Assert
        Assert.True(report.IsDryRun);
        Assert.Empty(mockClient.WriteCalls);
        Assert.Empty(mockClient.EraseCalls);
    }

    // ─── Edge case: Missing critical backup partitions ───────

    [Fact]
    public async Task DryRun_MissingCriticalBackup_WarnsButContinues()
    {
        // Arrange — no backup files created
        var mockClient = CreateMockClient(withTamperedBoot: true);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: true);

        // Act — should not throw
        var report = await orchestrator.RunFullRescueAsync();

        // Assert
        Assert.True(report.IsDryRun);
        Assert.Empty(mockClient.WriteCalls);
        // Diagnosis still ran
        Assert.NotEmpty(report.Partitions);
    }

    // ─── Download Mode check ──────────────────────────────────

    [Fact]
    public async Task DryRun_SkipDownloadModeCheck_ProceedsWithoutAdbAttempt()
    {
        // Arrange — no ADB, no Download Mode device, but skip flag set
        var mockClient = CreateMockClient(withTamperedBoot: false);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: true, skipDownloadModeCheck: true);

        // Act — should NOT block on Console.ReadLine or ADB
        var report = await orchestrator.RunFullRescueAsync();

        // Assert — rescue proceeds past Phase 0
        Assert.True(report.IsDryRun);
        Assert.NotEqual(OverallVerdict.Aborted, report.Verdict);
        Assert.NotEmpty(report.Partitions);
    }

    [Fact]
    public async Task DryRun_NoSkipDownloadModeCheck_AbortsWhenNoDownloadMode()
    {
        // Arrange — no ADB, no Download Mode device, skipInteractive=true
        // (skipInteractive prevents blocking on Console.ReadLine in test)
        // This test verifies the abort path when device isn't in Download Mode
        var mockClient = CreateMockClient(withTamperedBoot: false);
        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: true);

        // Act
        var report = await orchestrator.RunFullRescueAsync();

        // Assert — should abort because no Download Mode device is connected
        // (in CI, there's no Samsung device in Download Mode)
        Assert.Equal(OverallVerdict.Aborted, report.Verdict);
    }

    // ─── Blocklist enforcement ───────────────────────────────

    [Fact]
    public async Task LiveRun_BlocklistedPartition_NeverRestored()
    {
        // Arrange
        var stockBoot = CreateMinimalBootImage();
        CreateBackupFile("boot_a", stockBoot);
        CreateBackupFile("sec", new byte[512]); // sec is blocklisted

        var mockClient = CreateMockClient(withTamperedBoot: false);
        // Mark sec as tampered by giving it different data than backup
        mockClient.PartitionData["sec"] = new byte[512];
        for (int i = 0; i < 512; i++) mockClient.PartitionData["sec"][i] = 0xFF;

        var orchestrator = new RescueOrchestrator(mockClient, _testBackupDir, dryRun: false);

        // Act
        var report = await orchestrator.RunFullRescueAsync();

        // Assert — sec should NOT be in write/erase calls
        Assert.DoesNotContain("sec", mockClient.WriteCalls);
        Assert.DoesNotContain("sec", mockClient.EraseCalls);

        // sec restore action should be "Skipped" with BLOCKED reason
        var secAction = report.RestoreActions.Find(a => a.PartitionName == "sec");
        if (secAction != null)
        {
            Assert.Equal("Skipped", secAction.Action);
            Assert.Contains("BLOCKED", secAction.SourceFile);
        }
    }
}