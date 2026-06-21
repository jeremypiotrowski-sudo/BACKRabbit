using BACKRabbit.Protocol.Adb;

namespace BACKRabbit.CLI.Wizard;

/// <summary>
/// Device interrogation shared by WizardRunner (Step 1: Detect) and
/// TrapEscapeRunner (path selection). Eight diagnostic checks that
/// determine which cleanup path is available.
/// 
/// Path Selection Logic:
///   HasInitBoot → PATH 4: Recovery-Mode Cleanup (3 commands, 2 min)
///   !HasInitBoot && OemUnlockSupported → PATH A: Bootloader Unlock (6 steps)
///   !HasInitBoot && !OemUnlockSupported → PATH B: Firehose/EDL Required
/// </summary>
public class ForensicDiagnostics
{
    private readonly IAdbClient _adb;

    public ForensicDiagnostics(IAdbClient adb)
    {
        _adb = adb;
    }

    /// <summary>
    /// Run all 8 diagnostic checks against the connected device.
    /// </summary>
    public async Task<ForensicDiagnosis> DiagnoseAsync(CancellationToken ct = default)
    {
        var d = new ForensicDiagnosis();

        // 1. init_boot partition (GKI 2.0 indicator — enables Path 4)
        var initBoot = await _adb.ExecuteShellAsync(
            "ls -la /dev/block/by-name/init_boot 2>&1", ct);
        d.HasInitBoot = !initBoot.Contains("No such file");
        d.InitBootRaw = initBoot.Trim();

        // 2. OEM Unlock support (enables Path A)
        var oemUnlock = await _adb.ExecuteShellAsync(
            "getprop ro.oem_unlock_supported", ct);
        d.OemUnlockSupported = oemUnlock.Trim() == "1";
        d.OemUnlockRaw = oemUnlock.Trim();

        // 3. Bootloader lock state
        var flashLocked = await _adb.ExecuteShellAsync(
            "getprop ro.boot.flash.locked", ct);
        d.BootloaderLocked = flashLocked.Trim() == "1";

        // 4. Magisk residue — /data/adb/ directory existence
        var adbExists = await _adb.ExecuteShellAsync(
            "test -d /data/adb && echo 'EXISTS' || echo 'NOT_FOUND'", ct);
        d.HasMagiskResidue = adbExists.Contains("EXISTS");

        // 5. magisk.db — confirms Magisk was actually active (not just empty dirs)
        var dbCheck = await _adb.ExecuteShellAsync(
            "test -f /data/adb/magisk/magisk.db && echo 'EXISTS' || echo 'NOT_FOUND'", ct);
        d.HasMagiskDb = dbCheck.Contains("EXISTS");

        // 6. Device identity
        d.Platform = (await _adb.ExecuteShellAsync("getprop ro.board.platform", ct)).Trim();
        d.Model = (await _adb.ExecuteShellAsync("getprop ro.product.model", ct)).Trim();
        d.SecurityPatch = (await _adb.ExecuteShellAsync(
            "getprop ro.build.version.security_patch", ct)).Trim();

        // 7. Knox state (ro.boot.warranty_bit — Android property, NOT the eFuse)
        var knox = await _adb.ExecuteShellAsync("getprop ro.boot.warranty_bit", ct);
        d.KnoxState = knox.Trim() == "0" ? "0x0" :
                      knox.Trim() == "1" ? "0x1" : "unknown";

        // 8. Verified boot state
        d.VerifiedBootState = (await _adb.ExecuteShellAsync(
            "getprop ro.boot.verifiedbootstate", ct)).Trim();

        // Determine available cleanup path
        d.AvailablePath = DeterminePath(d);

        return d;
    }

    /// <summary>
    /// Path selection based on forensic diagnosis.
    /// </summary>
    public static CleanupPath DeterminePath(ForensicDiagnosis d)
    {
        if (!d.HasMagiskResidue)
            return CleanupPath.None;

        if (d.HasInitBoot)
            return CleanupPath.RecoveryMagisk;

        if (d.OemUnlockSupported)
            return CleanupPath.BootloaderUnlock;

        return CleanupPath.FirehoseRequired;
    }
}

/// <summary>
/// Result of the 8-point forensic diagnostic check.
/// </summary>
public class ForensicDiagnosis
{
    // Path-determining checks
    public bool HasInitBoot { get; set; }
    public string InitBootRaw { get; set; } = string.Empty;
    public bool OemUnlockSupported { get; set; }
    public string OemUnlockRaw { get; set; } = string.Empty;
    public bool BootloaderLocked { get; set; }
    public bool HasMagiskResidue { get; set; }
    public bool HasMagiskDb { get; set; }

    // Device identity
    public string Platform { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SecurityPatch { get; set; } = string.Empty;

    // Security state
    public string KnoxState { get; set; } = string.Empty;
    public string VerifiedBootState { get; set; } = string.Empty;

    // Computed
    public CleanupPath AvailablePath { get; set; }

    public override string ToString()
        => $"Model={Model}, Platform={Platform}, Knox={KnoxState}, " +
           $"init_boot={HasInitBoot}, OEM_Unlock={OemUnlockSupported}, " +
           $"BL_Locked={BootloaderLocked}, Residue={HasMagiskResidue}, " +
           $"magisk.db={HasMagiskDb}, VB={VerifiedBootState}, " +
           $"Path={AvailablePath}";
}

/// <summary>
/// Which cleanup path is available for this device.
/// </summary>
public enum CleanupPath
{
    /// <summary>No /data/adb/ residue detected — nothing to clean.</summary>
    None,

    /// <summary>Path 4: GKI device with init_boot. Boot to recovery, rm -rf, reboot. 3 commands, 2 minutes, zero risk.</summary>
    RecoveryMagisk,

    /// <summary>Path A: OEM Unlock supported. Unlock BL → Magisk root → clean → stock → re-lock. 6 steps, ~15 minutes, low risk.</summary>
    BootloaderUnlock,

    /// <summary>Path B: Firehose/EDL programmer required. Raw block write to zero /data/adb/. No root needed, no BL unlock needed, but programmer file is hard to obtain.</summary>
    FirehoseRequired
}