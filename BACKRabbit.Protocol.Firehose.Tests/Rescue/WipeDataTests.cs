using BACKRabbit.Protocol.Firehose.Rescue;
using Xunit;

namespace BACKRabbit.Protocol.Firehose.Tests.Rescue;

/// <summary>
/// Tests for --wipe-data capability (Sub-step C).
///
/// Five test scenarios:
/// 1. WipeData_WithConfirmation_ErasesUserdata — full happy path
/// 2. WipeData_WithoutFlag_NoErase — gate 1 enforcement
/// 3. WipeData_WrongConfirmation_AbortsWipe — gate 2 enforcement
/// 4. WipeData_VerificationFailure_AbortsRescue — readback detects residual data
/// 5. WipeData_DryRun_ZeroEraseCalls — gate 3 enforcement (dry-run override)
///
/// All tests target PartitionRestorer.WipeUserDataAsync directly (not the orchestrator)
/// because the orchestrator adds a SECOND typed confirmation gate that blocks on
/// Console.ReadLine — which would hang tests. The orchestrator's gate is verified
/// manually via the CLI command flow; the partition-restorer-level gate is tested here.
/// </summary>
public class WipeDataTests : IDisposable
{
    private readonly string _testBackupDir;

    public WipeDataTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), $"backrabbit_wipe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBackupDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBackupDir))
            Directory.Delete(_testBackupDir, recursive: true);
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
            PartitionData = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase),
            GptEntries = new List<GptPartitionEntry>(),
        };
    }

    // ─── Test 1: --wipe-data + correct confirmation → erases userdata ──

    [Fact]
    public async Task WipeData_WithConfirmation_ErasesUserdata()
    {
        // Arrange
        var client = CreateMockClient();
        client.SetEraseSimulatesWipe(true, userdataSectorCount: 100);  // 100 sectors = 51200 bytes

        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report,
            force: false, dryRun: false, wipeData: true);

        // Act
        var result = await restorer.WipeUserDataAsync(
            forceConfirmation: "CONFIRM-WIPE-DATA",
            expectedToken: "CONFIRM-WIPE-DATA");

        // Assert
        Assert.Contains("userdata", client.EraseCalls);
        Assert.Equal(WipeDataStatus.WipedAndVerified, result.Status);
        Assert.True(result.ConfirmationProvided);
        Assert.Null(result.FirstNonZeroSector);
        Assert.True(result.SectorsChecked > 0, $"Expected SectorsChecked > 0, got {result.SectorsChecked}");
        Assert.False(result.DryRun);
    }

    // ─── Test 2: --wipe-data NOT set → no erase, status = WipeNotAuthorized ──

    [Fact]
    public async Task WipeData_WithoutFlag_NoErase()
    {
        // Arrange
        var client = CreateMockClient();
        client.SetEraseSimulatesWipe(true);

        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report,
            force: false, dryRun: false, wipeData: false);  // wipeData = false

        // Act
        var result = await restorer.WipeUserDataAsync(
            forceConfirmation: "CONFIRM-WIPE-DATA",
            expectedToken: "CONFIRM-WIPE-DATA");

        // Assert
        Assert.DoesNotContain("userdata", client.EraseCalls);
        Assert.Empty(client.EraseCalls);
        Assert.Equal(WipeDataStatus.WipeNotAuthorized, result.Status);
        Assert.Contains("--wipe-data not set", result.ErrorMessage);
        Assert.Equal(0, result.SectorsChecked);
    }

    // ─── Test 3: Wrong confirmation → no erase, status = ConfirmationFailed ──

    [Fact]
    public async Task WipeData_WrongConfirmation_AbortsWipe()
    {
        // Arrange
        var client = CreateMockClient();
        client.SetEraseSimulatesWipe(true);

        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report,
            force: false, dryRun: false, wipeData: true);

        // Act — wrong confirmation
        var result = await restorer.WipeUserDataAsync(
            forceConfirmation: "wrong",
            expectedToken: "CONFIRM-WIPE-DATA");

        // Assert — no crash, no erase
        Assert.DoesNotContain("userdata", client.EraseCalls);
        Assert.Empty(client.EraseCalls);
        Assert.Equal(WipeDataStatus.ConfirmationFailed, result.Status);
        Assert.False(result.ConfirmationProvided);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Confirmation mismatch", result.ErrorMessage);
    }

    // ─── Test 4: Verification failure — non-zero data after erase ──

    [Fact]
    public async Task WipeData_VerificationFailure_AbortsRescue()
    {
        // Arrange
        var client = CreateMockClient();
        client.SetEraseSimulatesWipe(true, userdataSectorCount: 100);

        // OVERRIDE the readback AFTER wipe simulation with non-zero data in sector 5
        // (sector 5 = byte offset 2560)
        // We do this by calling SetReadPartitionOverride AFTER triggering the wipe
        // (but BEFORE readback). The wipe simulation populates the override with zeros,
        // so we override it again with data that has non-zero in sector 5.
        // We'll do this by setting an override AFTER erase has been called once.

        // Step 1: Run wipe (this will populate override with zeros via simulation)
        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report,
            force: false, dryRun: false, wipeData: true);

        // Pre-contaminate the readback with non-zero data so verification fails.
        // We do this by setting an override BEFORE the wipe simulation runs.
        // The mock's readback priority: ReadPartitionOverrides > PartitionData.
        // The wipe simulation populates ReadPartitionOverrides with zeros.
        // We need to set our "bad data" BEFORE the simulation runs.
        var badUserdata = new byte[100 * 512];  // all zeros initially
        badUserdata[2560] = 0x42;  // non-zero byte in sector 5
        badUserdata[2561] = 0x99;
        client.SetReadPartitionOverride("userdata", badUserdata);

        // Act
        var result = await restorer.WipeUserDataAsync(
            forceConfirmation: "CONFIRM-WIPE-DATA",
            expectedToken: "CONFIRM-WIPE-DATA");

        // Assert — erase WAS called (the gate passed), but verification FAILED
        Assert.Contains("userdata", client.EraseCalls);
        Assert.Equal(WipeDataStatus.VerificationFailed, result.Status);
        Assert.NotNull(result.FirstNonZeroSector);
        Assert.Equal(5L, result.FirstNonZeroSector);  // sector 5 had non-zero data
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("non-zero data", result.ErrorMessage);
    }

    // ─── Test 5: Dry-run + --wipe-data → ZERO erase calls ──

    [Fact]
    public async Task WipeData_DryRun_ZeroEraseCalls()
    {
        // Arrange
        var client = CreateMockClient();
        client.SetEraseSimulatesWipe(true, userdataSectorCount: 100);

        var report = new RescueReport();
        var restorer = new PartitionRestorer(client, _testBackupDir, report,
            force: false, dryRun: true, wipeData: true);  // BOTH dry-run AND wipe-data

        // Act
        var result = await restorer.WipeUserDataAsync(
            forceConfirmation: "DRY-RUN-AUTO",
            expectedToken: "DRY-RUN-AUTO");

        // Assert — zero erase calls, dry-run logged
        Assert.Empty(client.EraseCalls);
        Assert.Equal(WipeDataStatus.DryRunLogged, result.Status);
        Assert.True(result.DryRun);
        Assert.True(result.ConfirmationProvided);
    }
}