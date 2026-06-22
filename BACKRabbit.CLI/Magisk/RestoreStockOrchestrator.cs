using System.Security.Cryptography;
using System.Text;
using BACKRabbit.MagiskCore.Parser;
using BACKRabbit.MagiskCore.RamdiskEditor;
using BACKRabbit.Protocol.Adb;
using BACKRabbit.Protocol.Fastboot;
using BACKRabbit.Usb;

namespace BACKRabbit.CLI.Magisk;

/// <summary>
/// PC-based MagiskCore rescue orchestrator.
/// Pulls current boot/vbmeta from device, compares against stock firmware,
/// flashes stock images via fastboot, and cleans /data/adb/ via ADB shell.
/// </summary>
public class RestoreStockOrchestrator
{
    private readonly IAdbClient _adb;
    private readonly IFastbootClient _fastboot;
    private readonly UsbDeviceManager _usb;
    private readonly BootImageParser _parser;
    private readonly MagiskArtifactDetector _detector;
    private readonly RestoreStockOptions _options;
    private readonly List<string> _log = new();

    public RestoreStockOrchestrator(
        IAdbClient adb,
        IFastbootClient fastboot,
        UsbDeviceManager usb,
        BootImageParser parser,
        MagiskArtifactDetector detector,
        RestoreStockOptions options)
    {
        _adb = adb;
        _fastboot = fastboot;
        _usb = usb;
        _parser = parser;
        _detector = detector;
        _options = options;
    }

    public IReadOnlyList<string> Log => _log;


    public async Task<RestoreStockResult> RunAsync(CancellationToken ct = default)
    {
        _options.EnsureWorkDirectory();
        var result = new RestoreStockResult { StockDirectory = _options.StockDirectory };

        try
        {
            // Phase 1: Connect ADB
            WriteLine("=== Phase 1: Connect ADB ===");
            if (!await _adb.ConnectUsbAsync(_usb, ct))
            {
                result.Status = RestoreStockStatus.Failed;
                result.Error = "Failed to connect ADB. Ensure device is connected with USB debugging enabled.";
                return result;
            }

            result.Serial = _adb.Serial;
            result.DeviceModel = _adb.DeviceModel;
            WriteLine($"ADB connected: {_adb.Serial} ({_adb.DeviceModel})");

            // Phase 2: Analysis
            WriteLine();
            WriteLine("=== Phase 2: Analyze Stock vs Device ===");
            var analysis = await RunAnalysisAsync(ct);
            result.Analysis = analysis;

            if (_options.DryRun)
            {
                WriteLine();
                WriteLine("=== DRY-RUN COMPLETE ===");
                WriteLine("No flash, clean, or reboot operations were performed.");
                result.Status = RestoreStockStatus.DryRun;
                return result;
            }

            // Phase 3: Flash stock images via fastboot
            WriteLine();
            WriteLine("=== Phase 3: Flash Stock Images ===");
            var flashResult = await RunFlashAsync(analysis, ct);
            result.FlashSuccess = flashResult.Success;
            result.FlashError = flashResult.Error;

            if (!flashResult.Success)
            {
                result.Status = RestoreStockStatus.FlashFailed;
                return result;
            }

            // Phase 4: Reboot to recovery and clean /data/adb
            // Recovery runs as root with permissive SELinux (androidboot.selinux=permissive in cmdline).
            // Normal ADB shell (UID 2000) can't access root-owned /data/adb even in permissive mode.
            // Recovery ADB runs as root — can rm -rf /data/adb subdirectories.
            WriteLine();
            WriteLine("=== Phase 4: Clean /data/adb via Recovery ===");
            WriteLine("Rebooting to recovery (root access + permissive SELinux)...");
            var cleanResult = await RunRecoveryCleanAsync(ct);
            result.CleanSuccess = cleanResult.Success;
            result.CleanError = cleanResult.Error;

            // Phase 5: Reboot to system and verify
            WriteLine();
            WriteLine("=== Phase 5: Verify ===");
            WriteLine("Rebooting to system...");
            if (!await _adb.RebootAsync("", ct))
            {
                WriteLine("WARNING: Reboot command may have failed — device may still reboot");
            }
            WriteLine("Waiting for device to come back online...");
            if (!await _adb.WaitForDeviceAsync(60000, ct))
            {
                result.Status = RestoreStockStatus.Partial;
                result.Error = "Device did not return to ADB after reboot. Clean may have succeeded but verification failed.";
                return result;
            }

            var verifyResult = await RunVerifyAsync(analysis, ct);
            result.BootVerified = verifyResult.BootVerified;
            result.VbmetaVerified = verifyResult.VbmetaVerified;
            result.DataAdbClean = verifyResult.DataAdbClean;
            result.OverlaysActive = verifyResult.OverlaysActive;

            result.Status = (result.BootVerified && result.VbmetaVerified && result.DataAdbClean && !result.OverlaysActive)
                ? RestoreStockStatus.Restored
                : RestoreStockStatus.Partial;

            return result;
        }
        catch (Exception ex)
        {
            result.Status = RestoreStockStatus.Failed;
            result.Error = $"Unexpected error: {ex.Message}";
            WriteLine($"ERROR: {ex.Message}");
            return result;
        }
    }

    private async Task<RestoreStockAnalysis> RunAnalysisAsync(CancellationToken ct)
    {
        var analysis = new RestoreStockAnalysis();

        // Determine active slot
        var slotSuffix = await _adb.ExecuteShellAsync("getprop ro.boot.slot_suffix", ct);
        analysis.SlotSuffix = slotSuffix.Trim();
        analysis.IsAbSlotting = !string.IsNullOrEmpty(analysis.SlotSuffix);

        var bootBlock = analysis.IsAbSlotting
            ? $"/dev/block/by-name/boot{analysis.SlotSuffix}"
            : "/dev/block/by-name/boot";

        analysis.StockBootPath = Path.Combine(_options.StockDirectory, "boot.img");
        analysis.StockVbmetaPath = Path.Combine(_options.StockDirectory, "vbmeta.img");
        analysis.StockRecoveryPath = Path.Combine(_options.StockDirectory, "recovery.img");
        analysis.CurrentBootPath = Path.Combine(_options.WorkDirectory, "current_boot.img");
        analysis.CurrentVbmetaPath = Path.Combine(_options.WorkDirectory, "current_vbmeta.img");
        analysis.CurrentRecoveryPath = Path.Combine(_options.WorkDirectory, "current_recovery.img");

        // Pull current images
        WriteLine($"Pulling current boot from {bootBlock}...");
        analysis.BootPulled = await _adb.PullFileAsync(bootBlock, analysis.CurrentBootPath, ct);
        if (!analysis.BootPulled) WriteLine("WARNING: Failed to pull current boot image");

        WriteLine("Pulling current vbmeta from /dev/block/by-name/vbmeta...");
        analysis.VbmetaPulled = await _adb.PullFileAsync("/dev/block/by-name/vbmeta", analysis.CurrentVbmetaPath, ct);
        if (!analysis.VbmetaPulled) WriteLine("WARNING: Failed to pull current vbmeta image");

        // Load stock images
        if (File.Exists(analysis.StockBootPath))
        {
            analysis.StockBootData = await File.ReadAllBytesAsync(analysis.StockBootPath, ct);
            analysis.StockBoot = _parser.Parse(analysis.StockBootData);
            WriteLine($"Stock boot: AOSP v{analysis.StockBoot.HeaderVersion}, page size {GetPageSize(analysis.StockBoot)}, kernel {analysis.StockBoot.KernelSize}, ramdisk {analysis.StockBoot.RamdiskSize}");

            // Tab A / legacy devices: if stock boot has no ramdisk, recovery.img may carry it
            if (analysis.StockBoot.RamdiskSize == 0 && File.Exists(analysis.StockRecoveryPath))
            {
                WriteLine($"Stock boot ramdisk is 0 bytes; investigating {analysis.StockRecoveryPath}...");
                analysis.StockRecoveryData = await File.ReadAllBytesAsync(analysis.StockRecoveryPath, ct);
                analysis.StockRecovery = _parser.Parse(analysis.StockRecoveryData);
                WriteLine($"Stock recovery: AOSP v{analysis.StockRecovery.HeaderVersion}, ramdisk {analysis.StockRecovery.RamdiskSize}");
            }
        }
        else
        {
            WriteLine($"ERROR: Stock boot image not found: {analysis.StockBootPath}");
        }

        if (File.Exists(analysis.StockVbmetaPath))
        {
            analysis.StockVbmetaData = await File.ReadAllBytesAsync(analysis.StockVbmetaPath, ct);
            analysis.StockVbmetaSha256 = ComputeSha256(analysis.StockVbmetaData);
        }
        else
        {
            WriteLine($"WARNING: Stock vbmeta image not found: {analysis.StockVbmetaPath}");
        }

        // Parse and analyze current boot
        if (analysis.BootPulled && File.Exists(analysis.CurrentBootPath))
        {
            analysis.CurrentBootData = await File.ReadAllBytesAsync(analysis.CurrentBootPath, ct);
            analysis.CurrentBoot = _parser.Parse(analysis.CurrentBootData);
            WriteLine($"Device boot: AOSP v{analysis.CurrentBoot.HeaderVersion}, page size {GetPageSize(analysis.CurrentBoot)}, kernel {analysis.CurrentBoot.KernelSize}, ramdisk {analysis.CurrentBoot.RamdiskSize}");

            // If stock boot has no ramdisk but recovery does, pull and compare device recovery too
            if (analysis.StockRecovery != null)
            {
                WriteLine($"Pulling current recovery from /dev/block/by-name/recovery{analysis.SlotSuffix}...");
                var recoveryBlock = analysis.IsAbSlotting
                    ? $"/dev/block/by-name/recovery{analysis.SlotSuffix}"
                    : "/dev/block/by-name/recovery";
                analysis.RecoveryPulled = await _adb.PullFileAsync(recoveryBlock, analysis.CurrentRecoveryPath, ct);
                if (analysis.RecoveryPulled && File.Exists(analysis.CurrentRecoveryPath))
                {
                    analysis.CurrentRecoveryData = await File.ReadAllBytesAsync(analysis.CurrentRecoveryPath, ct);
                    analysis.CurrentRecovery = _parser.Parse(analysis.CurrentRecoveryData);
                }
            }

            // Compare core properties
            if (analysis.StockBoot != null)
            {
                analysis.BootModified =
                    analysis.CurrentBoot.KernelSize != analysis.StockBoot.KernelSize ||
                    analysis.CurrentBoot.RamdiskSize != analysis.StockBoot.RamdiskSize ||
                    analysis.CurrentBoot.HeaderVersion != analysis.StockBoot.HeaderVersion;

                // Compare cmdline
                var stockCmdline = GetCmdline(analysis.StockBoot);
                var currentCmdline = GetCmdline(analysis.CurrentBoot);
                if (stockCmdline != currentCmdline)
                {
                    analysis.BootModified = true;
                    analysis.CmdlineDiff = $"Stock: {stockCmdline}\nCurrent: {currentCmdline}";
                }

                // Compare ramdisk entries
                try
                {
                    var stockBootRamdisk = analysis.StockBoot.RamdiskSize > 0
                        ? _parser.ExtractRamdiskArchive(analysis.StockBoot)
                        : null;
                    var stockRecoveryRamdisk = analysis.StockRecovery?.RamdiskSize > 0
                        ? _parser.ExtractRamdiskArchive(analysis.StockRecovery)
                        : null;
                    var currentBootRamdisk = analysis.CurrentBoot.RamdiskSize > 0
                        ? _parser.ExtractRamdiskArchive(analysis.CurrentBoot)
                        : null;
                    var currentRecoveryRamdisk = analysis.CurrentRecovery?.RamdiskSize > 0
                        ? _parser.ExtractRamdiskArchive(analysis.CurrentRecovery)
                        : null;

                    var stockRamdisk = stockBootRamdisk ?? stockRecoveryRamdisk;
                    var currentRamdisk = currentBootRamdisk ?? currentRecoveryRamdisk;

                    if (stockRamdisk != null && currentRamdisk != null)
                    {
                        analysis.StockArtifactResult = _detector.Detect(stockRamdisk);
                        analysis.CurrentArtifactResult = _detector.Detect(currentRamdisk);

                        var stockEntries = stockRamdisk.Entries.Select(e => e.Name).OrderBy(n => n).ToList();
                        var currentEntries = currentRamdisk.Entries.Select(e => e.Name).OrderBy(n => n).ToList();
                        var added = currentEntries.Except(stockEntries).ToList();
                        var removed = stockEntries.Except(currentEntries).ToList();
                        if (added.Any() || removed.Any())
                        {
                            analysis.BootModified = true;
                            analysis.RamdiskEntryDiff.AddRange(added.Select(a => $"+ {a}"));
                            analysis.RamdiskEntryDiff.AddRange(removed.Select(r => $"- {r}"));
                        }

                        // Compare .rc files and sepolicy
                        CompareRamdiskFiles(stockRamdisk, currentRamdisk, analysis);
                    }
                    else
                    {
                        WriteLine("WARNING: Could not extract ramdisk from stock or current image.");
                    }
                }
                catch (Exception ex)
                {
                    WriteLine($"Ramdisk analysis skipped: {ex.Message}");
                }
            }
        }

        // /data/adb forensic analysis — use CheckMagiskStatusAsync for proper SELinux-aware detection
        // "Permission denied" = directory EXISTS but SELinux blocks access → Magisk was installed
        // "No such file" = directory genuinely doesn't exist → stock device
        analysis.MagiskStatus = await _adb.CheckMagiskStatusAsync(ct);
        analysis.DataAdbListing = analysis.MagiskStatus.HasAdbDirectory
            ? (analysis.MagiskStatus.IsAdbReadable ? "READABLE" : "LOCKED (SELinux — Permission denied)")
            : "NOT PRESENT";
        // Magisk overlay system detection — Magisk uses BIND MOUNTS, not kernel overlayfs.
        // /data/adb IS the overlay system: modules, scripts, binaries, configuration.
        // Check bind mounts, /data/adb subdirectories, and Magisk daemon.
        analysis.OverlaySystem = await CheckMagiskOverlaySystemAsync(ct);
        analysis.Overlays = analysis.OverlaySystem.Summary;

        // Determine verdict
        analysis.BootPullFailed = !analysis.BootPulled;
        analysis.HasAdbDirectory = analysis.MagiskStatus.HasAdbDirectory;
        analysis.HasOverlays = analysis.OverlaySystem.IsActive;
        analysis.HasMagiskArtifacts = analysis.CurrentArtifactResult?.IsMagiskInstalled ?? false;

        // Verdict logic:
        // - /data/adb exists (even if locked) → COMPROMISED (Magisk was installed) — this is independent evidence
        // - Boot pull failed + no /data/adb → UNKNOWN (cannot determine, do NOT assume clean)
        // - Boot differs from stock + /data/adb → COMPROMISED
        // - Boot differs from stock only → MODIFIED
        // - None of the above → CLEAN
        if (analysis.HasAdbDirectory && analysis.BootModified)
            analysis.Verdict = "COMPROMISED — boot image modified AND /data/adb persistence detected";
        else if (analysis.HasAdbDirectory)
            analysis.Verdict = $"COMPROMISED — /data/adb directory exists (Magisk persistence). {(analysis.BootPullFailed ? "Boot image could not be pulled for comparison." : "Boot image matches stock but residual traces remain.")}";
        else if (analysis.BootPullFailed)
            analysis.Verdict = "UNKNOWN — boot image pull failed, cannot compare against stock";
        else if (analysis.BootModified)
            analysis.Verdict = "MODIFIED — boot image differs from stock";
        else if (analysis.HasMagiskArtifacts)
            analysis.Verdict = "MODIFIED — Magisk artifacts detected in ramdisk";
        else
            analysis.Verdict = "CLEAN — boot matches stock, no /data/adb, no overlays, no Magisk artifacts";

        // Print summary
        WriteLine();
        WriteLine("--- Analysis Summary ---");
        WriteLine($"Device slot suffix: {analysis.SlotSuffix} (A/B: {analysis.IsAbSlotting})");
        WriteLine($"Stock boot: {(analysis.StockBoot != null ? "parsed" : "not found")}");
        WriteLine($"Current boot: {(analysis.CurrentBoot != null ? "parsed" : (analysis.BootPullFailed ? "pull failed" : "not pulled"))}");
        WriteLine($"Boot modified: {analysis.BootModified}");
        if (!string.IsNullOrEmpty(analysis.CmdlineDiff)) WriteLine($"Cmdline diff:\n{analysis.CmdlineDiff}");
        if (analysis.RamdiskEntryDiff.Any())
        {
            WriteLine($"Ramdisk entry differences: {analysis.RamdiskEntryDiff.Count}");
            foreach (var d in analysis.RamdiskEntryDiff.Take(20))
                WriteLine($"  {d}");
        }
        if (analysis.FileDiffs.Any())
        {
            WriteLine($"Ramdisk file content differences: {analysis.FileDiffs.Count}");
            foreach (var d in analysis.FileDiffs.Take(10))
                WriteLine($"  {d}");
        }
        if (analysis.StockArtifactResult != null)
        {
            WriteLine($"Magisk artifacts in stock boot: {analysis.StockArtifactResult.FoundArtifacts.Count}");
            foreach (var a in analysis.StockArtifactResult.FoundArtifacts.Take(5))
                WriteLine($"  - {a}");
        }
        if (analysis.CurrentArtifactResult != null)
        {
            WriteLine($"Magisk artifacts in device boot: {analysis.CurrentArtifactResult.FoundArtifacts.Count}");
            foreach (var a in analysis.CurrentArtifactResult.FoundArtifacts.Take(10))
                WriteLine($"  - {a}");
        }
        WriteLine($"/data/adb status: {analysis.DataAdbListing}");
        if (analysis.MagiskStatus.IsResidual)
            WriteLine($"  Residual evidence: {analysis.MagiskStatus.ResidualEvidence}");
        WriteLine($"Overlay status:\n{analysis.Overlays}");
        WriteLine();
        WriteLine($"VERDICT: {analysis.Verdict}");

        return analysis;
    }

    private async Task<(bool Success, string? Error)> RunFlashAsync(RestoreStockAnalysis analysis, CancellationToken ct)
    {
        if (analysis.StockBootData == null || analysis.StockBootData.Length == 0)
            return (false, "No stock boot image available to flash.");

        if (analysis.StockVbmetaData == null || analysis.StockVbmetaData.Length == 0)
            return (false, "No stock vbmeta image available to flash.");

        // Reboot to bootloader
        WriteLine("Rebooting to bootloader...");
        if (!await _adb.RebootBootloaderAsync(ct))
            return (false, "Failed to reboot to bootloader.");

        // Wait a moment for USB re-enumeration
        await Task.Delay(3000, ct);

        // Connect fastboot
        WriteLine("Connecting fastboot...");
        if (!await _fastboot.ConnectAsync(_usb, ct))
            return (false, "Failed to connect fastboot after reboot.");

        // Determine partition names
        var bootPartition = analysis.IsAbSlotting
            ? $"boot{analysis.SlotSuffix}"
            : "boot";

        WriteLine($"Flashing stock boot to {bootPartition}...");
        if (!await _fastboot.FlashAsync(bootPartition, analysis.StockBootData, ct))
            return (false, $"Failed to flash {bootPartition}.");

        WriteLine("Flashing stock vbmeta...");
        if (!await _fastboot.FlashAsync("vbmeta", analysis.StockVbmetaData, ct))
            return (false, "Failed to flash vbmeta.");

        WriteLine("Rebooting device...");
        if (!await _fastboot.RebootAsync(ct))
            return (false, "Failed to reboot device.");

        // Wait for ADB to come back
        WriteLine("Waiting for device to come back online...");
        if (!await _adb.WaitForDeviceAsync(60000, ct))
            return (false, "Device did not return to ADB after reboot.");

        return (true, null);
    }

    private async Task<(bool Success, string? Error)> RunCleanAsync(CancellationToken ct)
    {
        try
        {
            var commands = new[]
            {
                "rm -rf /data/adb/magisk",
                "rm -rf /data/adb/modules",
                "rm -rf /data/adb/post-fs-data.d/*",
                "rm -rf /data/adb/service.d/*",
                "find /data/adb -name \"*.sh\" -delete 2>/dev/null || true",
                "ls -la /data/adb/ 2>&1 || echo 'NO_DATA_ADB'"
            };

            foreach (var cmd in commands)
            {
                WriteLine($" adb shell {cmd}");
                var output = await _adb.ExecuteShellAsync(cmd, ct);
                if (!string.IsNullOrWhiteSpace(output))
                    WriteLine(output);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<(bool BootVerified, bool VbmetaVerified, bool DataAdbClean, bool OverlaysActive)> RunVerifyAsync(RestoreStockAnalysis analysis, CancellationToken ct)
    {
        var bootBlock = analysis.IsAbSlotting
            ? $"/dev/block/by-name/boot{analysis.SlotSuffix}"
            : "/dev/block/by-name/boot";

        var verifyBootPath = Path.Combine(_options.WorkDirectory, "verify_boot.img");
        var verifyVbmetaPath = Path.Combine(_options.WorkDirectory, "verify_vbmeta.img");

        bool bootVerified = false;
        bool vbmetaVerified = false;

        if (await _adb.PullFileAsync(bootBlock, verifyBootPath, ct))
        {
            var verifyData = await File.ReadAllBytesAsync(verifyBootPath, ct);
            var verifySha = ComputeSha256(verifyData);
            var stockSha = ComputeSha256(analysis.StockBootData ?? Array.Empty<byte>());
            bootVerified = verifySha.Equals(stockSha, StringComparison.OrdinalIgnoreCase);
            WriteLine(bootVerified
                ? "✅ Boot image verified — matches stock"
                : $"❌ Boot image mismatch\n  Stock SHA: {stockSha}\n  Verify SHA: {verifySha}");
        }
        else
        {
            WriteLine("❌ Failed to pull boot image for verification");
        }

        if (await _adb.PullFileAsync("/dev/block/by-name/vbmeta", verifyVbmetaPath, ct))
        {
            var verifyData = await File.ReadAllBytesAsync(verifyVbmetaPath, ct);
            var verifySha = ComputeSha256(verifyData);
            vbmetaVerified = verifySha.Equals(analysis.StockVbmetaSha256 ?? "", StringComparison.OrdinalIgnoreCase);
            WriteLine(vbmetaVerified
                ? "✅ vbmeta verified — matches stock"
                : "❌ vbmeta mismatch");
        }
        else
        {
            WriteLine("❌ Failed to pull vbmeta for verification");
        }

        var dataAdb = await _adb.ExecuteShellAsync("ls /data/adb/ 2>&1 || echo 'NO_DATA_ADB'", ct);
        var dataAdbClean = dataAdb.Contains("NO_DATA_ADB") ||
            string.IsNullOrWhiteSpace(dataAdb) ||
            dataAdb.Trim() == "magisk" || dataAdb.Trim().Split('\n').Length <= 2;
        WriteLine(dataAdbClean
            ? "✅ /data/adb is clean or minimal"
            : $"⚠️ /data/adb still contains: {dataAdb.Trim()}");

        var overlays = await _adb.ExecuteShellAsync("mount 2>&1 | grep overlay || echo 'NO_OVERLAYS'", ct);
        var overlaysActive = !overlays.Contains("NO_OVERLAYS");
        WriteLine(overlaysActive
            ? $"⚠️ Overlays still active: {overlays.Trim()}"
            : "✅ No overlay mounts active");

        return (bootVerified, vbmetaVerified, dataAdbClean, overlaysActive);
    }

    private uint GetPageSize(BootImage img)
    {
        if (img.HeaderV0.page_size != 0) return img.HeaderV0.page_size;
        if (img.HeaderV1.page_size != 0) return img.HeaderV1.page_size;
        if (img.HeaderV2.page_size != 0) return img.HeaderV2.page_size;
        return 4096;
    }

    private string GetCmdline(BootImage img)
    {
        byte[]? cmdline = img.HeaderVersion switch
        {
            0 => img.HeaderV0.cmdline,
            1 => img.HeaderV1.cmdline,
            2 => img.HeaderV2.cmdline,
            3 => img.HeaderV3.cmdline,
            4 => img.HeaderV4.cmdline,
            _ => img.HeaderV0.cmdline
        };
        return cmdline != null ? Encoding.ASCII.GetString(cmdline).TrimEnd('\0') : "";
    }

    private void CompareRamdiskFiles(CpioArchive stock, CpioArchive current, RestoreStockAnalysis analysis)
    {
        var stockRc = stock.Entries.Where(e => e.Name.EndsWith(".rc", StringComparison.OrdinalIgnoreCase)).ToList();
        var currentRc = current.Entries.Where(e => e.Name.EndsWith(".rc", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var rc in currentRc)
        {
            var stockEntry = stock.GetEntry(rc.Name);
            if (stockEntry == null)
            {
                analysis.BootModified = true;
                analysis.FileDiffs.Add($"+ {rc.Name}");
                continue;
            }
            var stockData = stockEntry.GetData();
            var currentData = rc.GetData();
            if (!stockData.SequenceEqual(currentData))
            {
                analysis.BootModified = true;
                analysis.FileDiffs.Add($"~ {rc.Name}");
            }
        }
        foreach (var rc in stockRc.Where(s => current.GetEntry(s.Name) == null))
        {
            analysis.BootModified = true;
            analysis.FileDiffs.Add($"- {rc.Name}");
        }

        // Sepolicy comparison
        const string sepolicyName = "sepolicy";
        var stockSepolicy = stock.GetEntry(sepolicyName);
        var currentSepolicy = current.GetEntry(sepolicyName);
        if (stockSepolicy != null && currentSepolicy != null)
        {
            if (!stockSepolicy.GetData().SequenceEqual(currentSepolicy.GetData()))
            {
                analysis.BootModified = true;
                analysis.FileDiffs.Add("~ sepolicy");
            }
        }
        else if (stockSepolicy != null || currentSepolicy != null)
        {
            analysis.BootModified = true;
            analysis.FileDiffs.Add(stockSepolicy == null ? "+ sepolicy" : "- sepolicy");
        }
    }

    private string ComputeSha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    /// <summary>
    /// Comprehensive Magisk overlay system detection.
    /// Magisk uses BIND MOUNTS (not kernel overlayfs) via Magic Mount.
    /// /data/adb IS the overlay system — modules, scripts, binaries, configuration.
    /// </summary>
    private async Task<MagiskOverlaySystemStatus> CheckMagiskOverlaySystemAsync(CancellationToken ct)
    {
        var status = new MagiskOverlaySystemStatus();

        // Check bind mounts (Magisk's actual mechanism)
        var bindMounts = await _adb.ExecuteShellAsync("cat /proc/mounts 2>&1 | grep -i bind || echo 'NO_BIND_MOUNTS'", ct);
        status.BindMountsFound = !bindMounts.Contains("NO_BIND_MOUNTS");
        status.BindMountsRaw = bindMounts.Trim();

        // Check for Magisk-specific mounts
        var magiskMounts = await _adb.ExecuteShellAsync("cat /proc/mounts 2>&1 | grep /data/adb || echo 'NO_MAGISK_MOUNTS'", ct);
        status.MagiskMountsFound = !magiskMounts.Contains("NO_MAGISK_MOUNTS");

        // Check /data/adb subdirectories (permission denied = exists, SELinux locked)
        var magiskBinary = await _adb.ExecuteShellAsync("ls /data/adb/magisk 2>&1 || echo 'NOT_FOUND'", ct);
        status.MagiskBinaryPresent = magiskBinary.Contains("Permission denied");

        var modules = await _adb.ExecuteShellAsync("ls /data/adb/modules 2>&1 || echo 'NOT_FOUND'", ct);
        status.ModulesPresent = modules.Contains("Permission denied");

        var postFsData = await _adb.ExecuteShellAsync("ls /data/adb/post-fs-data.d 2>&1 || echo 'NOT_FOUND'", ct);
        status.PostFsScriptsPresent = postFsData.Contains("Permission denied");

        var serviceD = await _adb.ExecuteShellAsync("ls /data/adb/service.d 2>&1 || echo 'NOT_FOUND'", ct);
        status.ServiceScriptsPresent = serviceD.Contains("Permission denied");

        // Check Magisk daemon
        var daemon = await _adb.ExecuteShellAsync("ps 2>&1 | grep magisk || pgrep magisk 2>&1 || echo 'NO_DAEMON'", ct);
        status.DaemonRunning = !daemon.Contains("NO_DAEMON") && !daemon.Contains("not found") && !daemon.Contains("inaccessible");

        // Determine overlay system status
        if (status.BindMountsFound || status.MagiskMountsFound || status.DaemonRunning)
            status.IsActive = true;
        else if (status.MagiskBinaryPresent || status.ModulesPresent || status.PostFsScriptsPresent || status.ServiceScriptsPresent)
            status.IsResidual = true;

        // Build summary
        var sb = new StringBuilder();
        sb.AppendLine("### Magisk Overlay System Analysis");
        sb.AppendLine($"- /data/adb/magisk: {(status.MagiskBinaryPresent ? "permission denied (binary present)" : "not found")}");
        sb.AppendLine($"- /data/adb/modules: {(status.ModulesPresent ? "permission denied (modules present)" : "not found")}");
        sb.AppendLine($"- /data/adb/post-fs-data.d: {(status.PostFsScriptsPresent ? "permission denied (scripts present)" : "not found")}");
        sb.AppendLine($"- /data/adb/service.d: {(status.ServiceScriptsPresent ? "permission denied (scripts present)" : "not found")}");
        sb.AppendLine($"- Bind mounts referencing /data/adb: {(status.MagiskMountsFound ? "found" : "none")}");
        sb.AppendLine($"- Magisk daemon process: {(status.DaemonRunning ? "detected" : "not detected")}");
        sb.Append($"- Overlay system: ");
        if (status.IsActive)
            sb.AppendLine("ACTIVE (bind mounts or daemon running)");
        else if (status.IsResidual)
            sb.AppendLine("RESIDUAL (directory structure exists, no active mounts)");
        else
            sb.AppendLine("ABSENT");

        status.Summary = sb.ToString();
        return status;
    }

    /// <summary>
    /// Clean /data/adb from recovery mode.
    /// Recovery runs as root with permissive SELinux (androidboot.selinux=permissive in cmdline).
    /// Normal ADB shell (UID 2000) can't access root-owned /data/adb even in permissive mode.
    /// Recovery ADB runs as root — can rm -rf /data/adb subdirectories.
    /// </summary>
    private async Task<(bool Success, string? Error)> RunRecoveryCleanAsync(CancellationToken ct)
    {
        try
        {
            // Reboot to recovery
            if (!await _adb.RebootRecoveryAsync(ct))
                return (false, "Failed to reboot to recovery.");

            // Wait for recovery to boot and ADB to come up
            WriteLine("Waiting for recovery ADB (up to 60s)...");
            await Task.Delay(5000, ct); // Recovery typically boots in 5-10 seconds
            
            // Reconnect ADB after recovery boot
            if (!await _adb.ConnectUsbAsync(_usb, ct))
                return (false, "Failed to connect ADB in recovery mode. Device may not have recovery ADB enabled.");

            WriteLine($"Recovery ADB connected: {_adb.Serial}");

            // Verify we're in recovery
            var whoami = await _adb.ExecuteShellAsync("whoami 2>&1", ct);
            WriteLine($"Recovery shell user: {whoami.Trim()}");

            // Clean /data/adb subdirectories (recovery runs as root)
            var commands = new[]
            {
                "rm -rf /data/adb/magisk",
                "rm -rf /data/adb/modules",
                "rm -rf /data/adb/post-fs-data.d",
                "rm -rf /data/adb/service.d",
                "find /data/adb -name '*.sh' -delete 2>/dev/null; true",
                "ls -la /data/adb/ 2>&1 || echo 'NO_DATA_ADB'"
            };

            foreach (var cmd in commands)
            {
                WriteLine($" recovery shell: {cmd}");
                var output = await _adb.ExecuteShellAsync(cmd, ct);
                if (!string.IsNullOrWhiteSpace(output))
                    WriteLine($"   {output.Trim()}");
            }

            // Verify clean
            var verify = await _adb.ExecuteShellAsync("ls -la /data/adb/ 2>&1 || echo 'NO_DATA_ADB'", ct);
            var isClean = verify.Contains("NO_DATA_ADB") || 
                          string.IsNullOrWhiteSpace(verify) ||
                          (verify.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 1);

            WriteLine(isClean
                ? "✅ /data/adb cleaned successfully from recovery"
                : $"⚠️ /data/adb may still have content: {verify.Trim()}");

            return (isClean, isClean ? null : "Clean may have been partial — some content remains in /data/adb");
        }
        catch (Exception ex)
        {
            return (false, $"Recovery clean failed: {ex.Message}");
        }
    }

    private void WriteLine(string message = "")
    {
        _log.Add(message);
        Console.WriteLine(message);
    }
}

/// <summary>
/// Options for RestoreStockOrchestrator.
/// </summary>
public class RestoreStockOptions
{
    public string StockDirectory { get; set; } = "";
    public string WorkDirectory { get; set; } = "";
    public bool DryRun { get; set; }

    public void EnsureWorkDirectory()
    {
        if (string.IsNullOrEmpty(WorkDirectory))
            WorkDirectory = Path.Combine(Path.GetTempPath(), $"backrabbit_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(WorkDirectory);
    }
}

/// <summary>
/// Result of the restore-stock operation.
/// </summary>
public class RestoreStockResult
{
    public RestoreStockStatus Status { get; set; }
    public string? Serial { get; set; }
    public string? DeviceModel { get; set; }
    public string? StockDirectory { get; set; }
    public string? Error { get; set; }
    public RestoreStockAnalysis? Analysis { get; set; }
    public bool FlashSuccess { get; set; }
    public string? FlashError { get; set; }
    public bool CleanSuccess { get; set; }
    public string? CleanError { get; set; }
    public bool BootVerified { get; set; }
    public bool VbmetaVerified { get; set; }
    public bool DataAdbClean { get; set; }
    public bool OverlaysActive { get; set; }
}

public enum RestoreStockStatus
{
    DryRun,
    Restored,
    Partial,
    FlashFailed,
    Failed
}

/// <summary>
/// Analysis data collected during restore-stock dry-run and live modes.
/// </summary>
public class RestoreStockAnalysis
{
    public string SlotSuffix { get; set; } = "";
    public bool IsAbSlotting { get; set; }
    public bool BootPulled { get; set; }
    public bool VbmetaPulled { get; set; }
    public string StockBootPath { get; set; } = "";
    public string StockVbmetaPath { get; set; } = "";
    public string StockRecoveryPath { get; set; } = "";
    public string CurrentBootPath { get; set; } = "";
    public string CurrentVbmetaPath { get; set; } = "";
    public string CurrentRecoveryPath { get; set; } = "";
    public byte[]? StockBootData { get; set; }
    public byte[]? StockVbmetaData { get; set; }
    public byte[]? StockRecoveryData { get; set; }
    public string? StockVbmetaSha256 { get; set; }
    public byte[]? CurrentBootData { get; set; }
    public byte[]? CurrentRecoveryData { get; set; }
    public BootImage? StockBoot { get; set; }
    public BootImage? CurrentBoot { get; set; }
    public BootImage? StockRecovery { get; set; }
    public BootImage? CurrentRecovery { get; set; }
    public bool RecoveryPulled { get; set; }
    public bool BootModified { get; set; }
    public string? CmdlineDiff { get; set; }
    public List<string> RamdiskEntryDiff { get; set; } = new();
    public List<string> FileDiffs { get; set; } = new();
    public MagiskDetectionResult? StockArtifactResult { get; set; }
    public MagiskDetectionResult? CurrentArtifactResult { get; set; }
    public string DataAdbListing { get; set; } = "";
    public string Overlays { get; set; } = "";
    public MagiskStatus? MagiskStatus { get; set; }
    public bool BootPullFailed { get; set; }
    public bool HasAdbDirectory { get; set; }
    public bool HasOverlays { get; set; }
    public bool HasMagiskArtifacts { get; set; }
    public string Verdict { get; set; } = "";
    public MagiskOverlaySystemStatus? OverlaySystem { get; set; }
}

/// <summary>
/// Comprehensive Magisk overlay system status.
/// Magisk uses BIND MOUNTS (not kernel overlayfs) via Magic Mount.
/// /data/adb IS the overlay system.
/// </summary>
public class MagiskOverlaySystemStatus
{
    public bool IsActive { get; set; }
    public bool IsResidual { get; set; }
    public bool BindMountsFound { get; set; }
    public string BindMountsRaw { get; set; } = "";
    public bool MagiskMountsFound { get; set; }
    public bool MagiskBinaryPresent { get; set; }
    public bool ModulesPresent { get; set; }
    public bool PostFsScriptsPresent { get; set; }
    public bool ServiceScriptsPresent { get; set; }
    public bool DaemonRunning { get; set; }
    public string Summary { get; set; } = "";
}
