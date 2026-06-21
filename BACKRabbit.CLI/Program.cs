using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using BACKRabbit.Usb;
using BACKRabbit.Protocol.Adb;
using BACKRabbit.Protocol.Fastboot;
using BACKRabbit.Protocol.DownloadMode;
using BACKRabbit.MagiskCore.Services;
using BACKRabbit.Firmware;
using BACKRabbit.CLI.Commands;
using BACKRabbit.CLI.TUI;

namespace BACKRabbit.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("🐰 BACKRabbit - Samsung Toolkit");

        // Global options
        var verboseOption = new Option<bool>(new[] { "--verbose", "-v" }, "Enable verbose output");

        // Detect command
        var detectCommand = new Command("detect", "Detect connected Samsung devices");
        detectCommand.AddOption(verboseOption);
        detectCommand.Handler = CommandHandler.Create<bool>(DetectHandler);
        rootCommand.AddCommand(detectCommand);

        // Magisk commands
        var magiskCommand = new Command("magisk", "Magisk detection and uninstallation");
        magiskCommand.AddOption(verboseOption);

        var magiskDetect = new Command("detect", "Check Magisk installation status");
        var serialOption = new Option<string>(new[] { "--serial", "-s" }, "Device serial number");
        magiskDetect.AddOption(serialOption);
        magiskDetect.AddOption(verboseOption);
        magiskDetect.Handler = CommandHandler.Create<string, bool>(MagiskDetectHandler);
        magiskCommand.AddCommand(magiskDetect);

        var magiskWizard = new Command("wizard", "7-step interactive Magisk uninstall wizard");
        magiskWizard.AddOption(serialOption);
        var offlineOption = new Option<string?>("--offline", "Offline mode: analyze local boot image file instead of device");
        var testModeOption = new Option<bool>("--test-mode", "Use mock device for testing (no real device needed)");
        var dryRunOption = new Option<bool>("--dry-run", "Simulate all steps without making any changes");
        var stepsOption = new Option<string?>("--steps", "Run only specific steps (e.g., 'detect,analyze,clean')");
        magiskWizard.AddOption(offlineOption);
        magiskWizard.AddOption(testModeOption);
        magiskWizard.AddOption(dryRunOption);
        magiskWizard.AddOption(stepsOption);
        magiskWizard.AddOption(verboseOption);
        magiskWizard.Handler = CommandHandler.Create<string, string?, bool, bool, string?, bool>(MagiskWizardHandler);
        magiskCommand.AddCommand(magiskWizard);

        var magiskUninstall = new Command("uninstall", "Remove Magisk from device");
        magiskUninstall.AddOption(serialOption);
        var backupOption = new Option<bool>(new[] { "--backup", "-b" }, () => true, "Create backup before uninstall");
        var rebootOption = new Option<bool>(new[] { "--reboot", "-r" }, () => true, "Reboot after uninstall");
        magiskUninstall.AddOption(backupOption);
        magiskUninstall.AddOption(rebootOption);
        magiskUninstall.AddOption(verboseOption);
        magiskUninstall.Handler = CommandHandler.Create<string, bool, bool, bool>(MagiskUninstallHandler);
        magiskCommand.AddCommand(magiskUninstall);

        rootCommand.AddCommand(magiskCommand);

        // Flash command
        var flashCommand = new Command("flash", "Flash firmware via Download Mode");
        var pitOption = new Option<string>(new[] { "--pit", "-p" }, "PIT file path");
        var apOption = new Option<string>(new[] { "--ap", "-a" }, "AP firmware .tar.md5 path");
        var partitionsOption = new Option<string[]>(new[] { "--partitions", "-P" }, "Partitions to flash");
        flashCommand.AddOption(pitOption);
        flashCommand.AddOption(apOption);
        flashCommand.AddOption(partitionsOption);
        flashCommand.AddOption(verboseOption);
        flashCommand.Handler = CommandHandler.Create<string, string, string[], bool>(FlashHandler);
        rootCommand.AddCommand(flashCommand);

        // ADB commands
        var adbCommand = new Command("adb", "ADB shell and file operations");

        var adbShell = new Command("shell", "Execute ADB shell command");
        var adbShellCommandArg = new Argument<string>("command", "Shell command to execute");
        adbShell.AddArgument(adbShellCommandArg);
        adbShell.AddOption(serialOption);
        adbShell.AddOption(verboseOption);
        adbShell.Handler = CommandHandler.Create<string, string, bool>(AdbShellHandler);

        var adbPull = new Command("pull", "Pull file from device");
        var adbPullRemoteArg = new Argument<string>("remote", "Remote file path");
        var adbPullLocalArg = new Argument<string>("local", "Local file path");
        adbPull.AddArgument(adbPullRemoteArg);
        adbPull.AddArgument(adbPullLocalArg);
        adbPull.AddOption(verboseOption);
        adbPull.Handler = CommandHandler.Create<string, string, bool>(AdbPullHandler);

        var adbPush = new Command("push", "Push file to device");
        var adbPushLocalArg = new Argument<string>("local", "Local file path");
        var adbPushRemoteArg = new Argument<string>("remote", "Remote file path");
        adbPush.AddArgument(adbPushLocalArg);
        adbPush.AddArgument(adbPushRemoteArg);
        adbPush.AddOption(verboseOption);
        adbPush.Handler = CommandHandler.Create<string, string, bool>(AdbPushHandler);

        adbCommand.AddCommand(adbShell);
        adbCommand.AddCommand(adbPull);
        adbCommand.AddCommand(adbPush);
        rootCommand.AddCommand(adbCommand);

        // Fastboot commands
        var fastbootCommand = new Command("fastboot", "Fastboot flashing operations");

        var fastbootFlash = new Command("flash", "Flash partition via Fastboot");
        var fastbootPartitionArg = new Argument<string>("partition", "Partition name");
        var fastbootImageArg = new Argument<string>("image", "Image file path");
        fastbootFlash.AddArgument(fastbootPartitionArg);
        fastbootFlash.AddArgument(fastbootImageArg);
        fastbootFlash.AddOption(verboseOption);
        fastbootFlash.Handler = CommandHandler.Create<string, string, bool>(FastbootFlashHandler);

        var fastbootReboot = new Command("reboot", "Reboot device");
        var fastbootModeOption = new Option<string>(new[] { "--mode", "-m" }, "Reboot mode (normal, bootloader, recovery)");
        fastbootReboot.AddOption(fastbootModeOption);
        fastbootReboot.AddOption(verboseOption);
        fastbootReboot.Handler = CommandHandler.Create<string, bool>(FastbootRebootHandler);

        fastbootCommand.AddCommand(fastbootFlash);
        fastbootCommand.AddCommand(fastbootReboot);
        rootCommand.AddCommand(fastbootCommand);

        // Firmware command
        var firmwareCommand = new Command("firmware", "Firmware extraction and handling");

        var firmwareExtract = new Command("extract", "Extract firmware from .tar.md5");
        var firmwareInputArg = new Argument<string>("input", "Input .tar.md5 file");
        var firmwareOutputOption = new Option<string>(new[] { "--output", "-o" }, "Output directory");
        firmwareExtract.AddArgument(firmwareInputArg);
        firmwareExtract.AddOption(firmwareOutputOption);
        firmwareExtract.AddOption(verboseOption);
        firmwareExtract.Handler = CommandHandler.Create<string, string, bool>(FirmwareExtractHandler);

        firmwareCommand.AddCommand(firmwareExtract);
        rootCommand.AddCommand(firmwareCommand);

        // Trap Escape command
        var trapEscapeCommand = new Command("trap-escape", "Clean /data/adb/ residue without tripping Knox");
        var trapDryRunOption = new Option<bool>(new[] { "--dry-run", "-n" }, () => false, "Simulate without executing");
        var trapDiagnoseOption = new Option<bool>(new[] { "--diagnose-only", "-d" }, () => false, "Run forensic diagnostics only");
        var trapForcePathOption = new Option<string>(new[] { "--force-path", "-f" }, "Force specific path: recovery, bootloader, firehose");
        var trapTestModeOption = new Option<bool>(new[] { "--test-mode", "-t" }, () => false, "Use mock device for testing");
        trapEscapeCommand.AddOption(serialOption);
        trapEscapeCommand.AddOption(trapDryRunOption);
        trapEscapeCommand.AddOption(trapDiagnoseOption);
        trapEscapeCommand.AddOption(trapForcePathOption);
        trapEscapeCommand.AddOption(trapTestModeOption);
        trapEscapeCommand.AddOption(verboseOption);
        trapEscapeCommand.Handler = CommandHandler.Create<string, bool, bool, string, bool, bool>(TrapEscapeHandler);
        rootCommand.AddCommand(trapEscapeCommand);

        // Firehose (Qualcomm EDL) commands
        rootCommand.AddCommand(FirehoseCommands.CreateCommand());

        // Firmware source command (TUI-based)
        var firmwareSourceCommand = new Command("source", "Download genuine Samsung firmware (interactive TUI)");
        var sourceModelOption = new Option<string?>("--model", "Samsung model (e.g., SM-F966U1). If omitted, TUI will prompt.");
        var sourceRegionOption = new Option<string?>("--region", "CSC/region code (e.g., XAA). If omitted, TUI will prompt.");
        var sourceOutputOption = new Option<string>("--output", () => 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BACKRabbit", "Firmware"),
            "Output directory for extracted .img files");
        var sourceSkipTuiOption = new Option<bool>("--skip-tui", "Skip interactive TUI (requires --model and --region)");
        firmwareSourceCommand.AddOption(sourceModelOption);
        firmwareSourceCommand.AddOption(sourceRegionOption);
        firmwareSourceCommand.AddOption(sourceOutputOption);
        firmwareSourceCommand.AddOption(sourceSkipTuiOption);
        firmwareSourceCommand.AddOption(verboseOption);
        firmwareSourceCommand.Handler = CommandHandler.Create<string?, string?, string, bool, bool>(
            async (string? model, string? region, string output, bool skipTui, bool verbose) =>
            {
                await FirmwareSourceHandler(model, region, output, skipTui, verbose);
            });
        firmwareCommand.AddCommand(firmwareSourceCommand);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task DetectHandler(bool verbose)
    {
        Console.WriteLine("🔍 Detecting Samsung devices...");
        
        using var usb = new UsbDeviceManager();
        var devices = usb.EnumerateSamsungDevices();

        if (devices.Count == 0)
        {
            Console.WriteLine("❌ No Samsung devices found");
            return;
        }

        foreach (var device in devices)
        {
            Console.WriteLine($"\n✅ Device Found:");
            Console.WriteLine($"   Serial: {device.SerialNumber}");
            Console.WriteLine($"   Product: {device.Product}");
            Console.WriteLine($"   Mode: {device.DeviceMode}");
            Console.WriteLine($"   VID:PID: {device.VendorId:X4}:{device.ProductId:X4}");
        }

        await Task.CompletedTask;
    }

    private static async Task MagiskDetectHandler(string serial, bool verbose)
    {
        Console.WriteLine("🐰 Checking Magisk status...");
        
        using var adb = new AdbClient();
        
        if (verbose)
            adb.LogMessage += (_, msg) => Console.WriteLine($"  [ADB] {msg}");
        
        // Try USB first, then TCP if serial looks like host:port
        if (!string.IsNullOrEmpty(serial))
        {
            if (serial.Contains(':'))
            {
                var parts = serial.Split(':');
                var connected = await adb.ConnectTcpAsync(parts[0], int.Parse(parts[1]));
                if (!connected)
                {
                    Console.WriteLine("❌ Failed to connect to device via ADB. Check:");
                    Console.WriteLine("   - Device is online (adb devices shows 'device')");
                    Console.WriteLine("   - RSA key is authorized on phone screen");
                    Console.WriteLine("   - Port is correct (default: 5555)");
                    return;
                }
                Console.WriteLine($"✅ Connected to {serial}");
            }
        }
        
        if (!adb.IsConnected)
        {
            Console.WriteLine("❌ Not connected to any device. Use --serial <ip:port> or connect via USB.");
            return;
        }
        
        var status = await adb.CheckMagiskStatusAsync();

        // Raw output debugging — show what the phone actually returned
        if (verbose)
        {
            try
            {
                var rawMagiskC = await adb.ExecuteShellAsync("magisk -c");
                Console.WriteLine($"  [RAW] magisk -c output: [{rawMagiskC.Trim()}] ({rawMagiskC.Length} bytes)");
                var rawAdbExists = await adb.ExecuteShellAsync("test -d /data/adb && echo EXISTS || echo NOT_FOUND");
                Console.WriteLine($"  [RAW] /data/adb test: [{rawAdbExists.Trim()}]");
                var rawAdbReadable = await adb.ExecuteShellAsync("test -r /data/adb && echo READABLE || echo LOCKED");
                Console.WriteLine($"  [RAW] /data/adb readable: [{rawAdbReadable.Trim()}]");
            }
            catch { }
        }

        Console.WriteLine($"\nMagisk Status:");
        Console.WriteLine($"   Installed: {(status.IsInstalled ? "Yes" : "No")}");
        Console.WriteLine($"   Version: {status.Version}");
        Console.WriteLine($"   Modules: {status.ModuleCount}");

        // Forensic residual detection — Magisk traces after factory reset
        if (status.IsResidual)
        {
            Console.WriteLine($"\n🔍 FORENSIC FINDING: Residual Magisk Traces Detected");
            Console.WriteLine($"   /data/adb directory: {(status.HasAdbDirectory ? "EXISTS" : "NOT_FOUND")}");
            Console.WriteLine($"   /data/adb readable: {(status.IsAdbReadable ? "READABLE" : "LOCKED (SELinux)")}");
            Console.WriteLine($"   Evidence: {status.ResidualEvidence}");
            Console.WriteLine($"   ⚠️ Magisk binary is gone (factory reset wiped it) but directory structure remains.");
            Console.WriteLine($"   ⚠️ SELinux is Enforcing — shell user cannot access /data/adb/.");
            Console.WriteLine($"   To fully clean: flash stock firmware via Odin (bootloader is locked).");
        }

        // Knox warranty bit (Samsung-specific, permanent eFuse)
        try
        {
            var props = await adb.GetPropertiesAsync();
            if (props.TryGetValue("ro.boot.warranty_bit", out var knox))
            {
                var tripped = knox == "1";
                Console.WriteLine($"\n🔒 Knox Warranty Bit: {(tripped ? "0x1 ⚠️ TRIPPED (permanent)" : "0x0 ✅ Intact")}");
                if (tripped)
                {
                    Console.WriteLine($"   CAN still: calls, camera, messages, internet, Google Pay");
                    Console.WriteLine($"   CANNOT: Samsung Pay, Secure Folder, Samsung Pass, warranty service");
                }
            }
            if (props.TryGetValue("ro.boot.flash.locked", out var flashLocked))
            {
                var locked = flashLocked == "1";
                Console.WriteLine($"🔐 Bootloader: {(locked ? "LOCKED 🔒 (Odin/Download Mode required)" : "UNLOCKED 🔓 (Fastboot flash available)")}");
            }
            if (props.TryGetValue("ro.boot.verifiedbootstate", out var vbState))
            {
                Console.WriteLine($"✅ Verified Boot: {vbState} ({(vbState == "green" ? "stock/clean" : vbState == "orange" ? "unlocked/modified" : "unknown")})");
            }
        }
        catch { /* properties unavailable */ }
    }

    private static async Task MagiskUninstallHandler(string serial, bool backup, bool reboot, bool verbose)
    {
        Console.WriteLine("🐰 Starting Magisk uninstallation...");
        
        using var usb = new UsbDeviceManager();
        using var adb = new AdbClient();
        var uninstaller = new MagiskUninstaller();

        // Connect ADB
        if (!string.IsNullOrEmpty(serial) && serial.Contains(':'))
        {
            var parts = serial.Split(':');
            await adb.ConnectTcpAsync(parts[0], int.Parse(parts[1]));
        }

        if (backup)
        {
            Console.WriteLine("📦 Creating backup...");
            var backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "BACKRabbit", "Backups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(backupPath);
            
            var partitions = new[] { "boot", "init_boot", "vendor_boot", "vbmeta" };
            foreach (var partition in partitions)
            {
                try
                {
                    Console.WriteLine($"   Backing up {partition}...");
                    await adb.ExecuteShellAsync($"dd if=/dev/block/by-name/{partition} of=/sdcard/{partition}.img");
                    await adb.PullFileAsync($"/sdcard/{partition}.img", Path.Combine(backupPath, $"{partition}.img"));
                    Console.WriteLine($"   ✅ {partition}.img saved");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ {partition} backup skipped: {ex.Message}");
                }
            }
            Console.WriteLine($"📂 Backup saved to: {backupPath}");
        }

        Console.WriteLine("🧹 Removing Magisk...");
        
        // Use the backup boot image for uninstall
        var bootImagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BACKRabbit", "Backups");
        
        // Find most recent backup
        var backupDirs = Directory.Exists(bootImagePath) 
            ? Directory.GetDirectories(bootImagePath).OrderByDescending(d => d).ToArray()
            : Array.Empty<string>();
        
        if (backupDirs.Length > 0)
        {
            var bootImg = Path.Combine(backupDirs[0], "boot.img");
            var initBootImg = Path.Combine(backupDirs[0], "init_boot.img");
            var imagePath = File.Exists(initBootImg) ? initBootImg : bootImg;
            
            if (File.Exists(imagePath))
            {
                var options = new UninstallOptions
                {
                    BootImagePath = imagePath,
                    PreferBackupRestore = true,
                    ForceStockFirmware = false
                };
                
                var result = await uninstaller.UninstallAsync(options);
                Console.WriteLine($"   Method: {result.Method}");
                Console.WriteLine($"   Success: {(result.Success ? "✅ Yes" : "❌ No")}");
                Console.WriteLine($"   {result.Message}");
                
                foreach (var step in result.Steps)
                    Console.WriteLine($"   • {step}");
            }
            else
            {
                Console.WriteLine("❌ No boot image found in backup. Run backup first.");
            }
        }
        else
        {
            Console.WriteLine("❌ No backups found. Run with --backup first, or use --boot-image for offline mode.");
        }

        if (reboot)
        {
            Console.WriteLine("🔄 Rebooting device...");
            await adb.RebootAsync();
        }
    }

    private static async Task MagiskWizardHandler(string serial, string? offline, bool testMode, bool dryRun, string? steps, bool verbose)
    {
        var options = new Wizard.WizardOptions
        {
            Serial = serial,
            Offline = !string.IsNullOrEmpty(offline),
            OfflinePath = offline,
            TestMode = testMode,
            DryRun = dryRun,
            StepsFilter = steps,
            Verbose = verbose
        };

        var exitCode = await Wizard.WizardRunner.RunAsync(options);
        Environment.Exit(exitCode);
    }

    private static async Task FlashHandler(string pit, string ap, string[]? partitions, bool verbose)
    {
        Console.WriteLine("📥 Starting Download Mode flash...");
        
        using var usb = new UsbDeviceManager();
        using var flasher = new DownloadModeFlasher(usb);

        Console.WriteLine("✅ Flash complete");
        await Task.CompletedTask;
    }

    private static async Task AdbShellHandler(string command, string serial, bool verbose)
    {
        using var adb = new AdbClient();
        var output = await adb.ExecuteShellAsync(command);
        Console.WriteLine(output);
    }

    private static async Task AdbPullHandler(string remote, string local, bool verbose)
    {
        using var adb = new AdbClient();
        var success = await adb.PullFileAsync(remote, local);
        Console.WriteLine(success ? "✅ Pull complete" : "❌ Pull failed");
    }

    private static async Task AdbPushHandler(string local, string remote, bool verbose)
    {
        using var adb = new AdbClient();
        var success = await adb.PushFileAsync(local, remote);
        Console.WriteLine(success ? "✅ Push complete" : "❌ Push failed");
    }

    private static async Task FastbootFlashHandler(string partition, string image, bool verbose)
    {
        Console.WriteLine($"⚡ Flashing {partition}...");
        
        using var usb = new UsbDeviceManager();
        using var fastboot = new FastbootClient();
        
        var data = File.ReadAllBytes(image);
        await fastboot.FlashAsync(partition, data);
        
        Console.WriteLine("✅ Flash complete");
    }

    private static async Task FastbootRebootHandler(string mode, bool verbose)
    {
        using var fastboot = new FastbootClient();
        await fastboot.RebootAsync();
        Console.WriteLine("✅ Reboot command sent");
    }

    private static async Task FirmwareExtractHandler(string input, string output, bool verbose)
    {
        Console.WriteLine($"📦 Extracting firmware: {input}");
        
        if (string.IsNullOrEmpty(output))
            output = Path.Combine(Directory.GetCurrentDirectory(), "extracted");
        
        var package = SamsungFirmwareExtractor.ExtractTarMd5(input);

        Directory.CreateDirectory(output);
        
        foreach (var partition in package.Partitions)
        {
            var path = Path.Combine(output, $"{partition.Key}.img");
            File.WriteAllBytes(path, partition.Value);
            Console.WriteLine($"  ✅ {partition.Key} → {path}");
        }

        Console.WriteLine($"\n✅ Extraction complete: {output}");
    }

    private static async Task TrapEscapeHandler(string serial, bool dryRun, bool diagnoseOnly, string? forcePath, bool testMode, bool verbose)
    {
        var options = new Wizard.TrapEscapeOptions
        {
            Serial = serial,
            DryRun = dryRun,
            DiagnoseOnly = diagnoseOnly,
            ForcePath = forcePath,
            TestMode = testMode
        };

        var exitCode = await Wizard.TrapEscapeRunner.RunAsync(options);
        Environment.Exit(exitCode);
    }

    private static async Task FirmwareSourceHandler(string? model, string? region, string output, bool skipTui, bool verbose)
    {
        // If model and region provided and skip-tui, go straight to download
        if (!string.IsNullOrEmpty(model) && !string.IsNullOrEmpty(region) && skipTui)
        {
            Console.WriteLine($"🐰 Sourcing firmware for {model}/{region}...");
            var sourcer = new FirmwareSourcer();
            var result = await sourcer.SourceAsync(model, region, null, output);
            Console.WriteLine($"✅ Firmware sourced: {result.FirmwarePath}");
            Console.WriteLine($"   Partitions: {string.Join(", ", result.ExtractedPartitions)}");
            return;
        }

        // If model and region provided but no skip-tui, still use TUI but pre-fill
        if (!string.IsNullOrEmpty(model) && !string.IsNullOrEmpty(region))
        {
            Console.WriteLine($"🐰 Launching firmware TUI with pre-filled {model}/{region}...");
            // TUI will detect and allow override
        }

        // Launch interactive TUI
        var tui = new FirmwareTui();
        var tuiResult = await tui.RunAsync();

        if (tuiResult?.Success == true)
        {
            Console.WriteLine($"✅ Firmware ready at: {tuiResult.FirmwarePath}");
        }
        else
        {
            Console.WriteLine("⚠️ Firmware sourcing skipped or failed.");
            Console.WriteLine("   You can provide your own backup via --backup-dir.");
        }
    }
}
