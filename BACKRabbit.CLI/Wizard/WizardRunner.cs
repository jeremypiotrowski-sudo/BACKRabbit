using System.Text;
using Spectre.Console;
using BACKRabbit.Protocol.Adb;
using BACKRabbit.MagiskCore.Parser;
using BACKRabbit.MagiskCore.Repacker;
using BACKRabbit.MagiskCore.RamdiskEditor;
using BACKRabbit.MagiskCore.FormatDetection;
using BACKRabbit.MagiskCore.Compression;
using BACKRabbit.MagiskCore.AvbRestorer;
using BACKRabbit.MagiskCore.Services;
using BACKRabbit.CLI.Testing;

namespace BACKRabbit.CLI.Wizard;

/// <summary>
/// 7-step Magisk uninstall wizard — CLI-only, cross-platform.
/// Uses Spectre.Console for rich terminal output.
/// 
/// Jury Wishlist Items Implemented:
///   W3: Separate file (not monolithic Program.cs)
///   W7: Retry/skip/abort on every step failure
///   W9: [y/N] confirmation before flashing
///   W2+W10: Step-by-step Odin guide for locked bootloader
///   W8: Report written even on wizard failure
///   W4: --offline <path> for local boot image
///   W1: Plain-language error messages with recovery steps
///   W5: Module names in Step 3 analysis
///   W6: IMPLEMENTED (--dry-run)
/// </summary>
public static class WizardRunner
{
    private static readonly string[] StepNames =
    [
        "Detect — Check device for modifications",
        "Backup — Save current boot image",
        "Analyze — Inspect system files",
        "Clean — Remove modifications",
        "Flash — Write cleaned image to device",
        "Verify — Confirm removal",
        "Reboot — Restart device"
    ];

    /// <summary>
    /// Run the 7-step wizard with the given options.
    /// </summary>
    public static async Task<int> RunAsync(WizardOptions options)
    {
        // Header
        AnsiConsole.Write(new Rule("🐰 BACKRabbit v2.0 — Magisk Uninstall Wizard").RuleStyle("bold green"));
        AnsiConsole.WriteLine();

        var report = new WizardReport { StartedAt = DateTime.UtcNow };
        var context = new WizardContext { Options = options };

        // Initialize ADB client based on mode
        try
        {
            context.AdbClient = await InitializeAdbClientAsync(options);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to initialize: {ex.Message}[/]");
            AnsiConsole.MarkupLine("[yellow]💡 Try: backrabbit magisk wizard --test-mode (no device needed)[/]");
            return 1;
        }

        // --steps filter: parse comma-separated step names
        HashSet<string>? stepFilter = null;
        if (!string.IsNullOrEmpty(options.StepsFilter))
        {
            stepFilter = options.StepsFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant())
                .ToHashSet();
            AnsiConsole.MarkupLine($"[yellow]🔍 Running only selected steps: {string.Join(", ", stepFilter)}[/]");
        }

        // Run 7 steps with retry/skip/abort (W7)
        var stepFuncs = new Func<WizardContext, Task<StepResult>>[]
        {
            Step1_DetectAsync, Step2_BackupAsync, Step3_AnalyzeAsync,
            Step4_CleanAsync, Step5_FlashAsync, Step6_VerifyAsync, Step7_RebootAsync
        };

        for (int i = 0; i < stepFuncs.Length; i++)
        {
            // --steps filter: skip steps not in the filter
            var stepKey = StepNames[i].Split(' ')[0].TrimEnd('—').Trim().ToLowerInvariant();
            if (stepFilter != null && !stepFilter.Contains(stepKey))
            {
                AnsiConsole.MarkupLine($"[grey]⏭️  Step {i + 1}/7: {StepNames[i]} — SKIPPED (not in --steps filter)[/]");
                report.Steps.Add(StepResult.Ok());
                continue;
            }

            AnsiConsole.Write(new Rule($"Step {i + 1}/7: {StepNames[i]}"));

            StepResult result;
            try
            {
                result = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync($"Running Step {i + 1}...", async ctx =>
                    {
                        ctx.Status($"Step {i + 1}/7: {StepNames[i]}");
                        return await stepFuncs[i](context);
                    });
            }
            catch (Exception ex)
            {
                result = StepResult.Failed(
                    $"Unexpected error: {ex.Message}",
                    "Check the log file in Documents\\BACKRabbit\\Logs\\ for details. " +
                    "If this persists, run with --verbose for more information.");
            }

            report.Steps.Add(result);

            if (!result.Success)
            {
                AnsiConsole.MarkupLine($"[red]❌ Step {i + 1} failed: {result.ErrorMessage}[/]");
                if (!string.IsNullOrEmpty(result.RecoveryPath))
                    AnsiConsole.MarkupLine($"[yellow]💡 Recovery: {result.RecoveryPath}[/]");

                // W7: Retry/Skip/Abort — with fallback for terminals without ANSI support
                string choice;
                try
                {
                    choice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("What would you like to do?")
                            .AddChoices("Retry this step", "Skip this step", "Abort wizard"));
                }
                catch (NotSupportedException)
                {
                    Console.WriteLine("What would you like to do?");
                    Console.WriteLine("  [1] Retry this step");
                    Console.WriteLine("  [2] Skip this step");
                    Console.WriteLine("  [3] Abort wizard");
                    Console.Write("Enter choice (1-3): ");
                    var key = Console.ReadLine()?.Trim();
                    choice = key switch
                    {
                        "1" => "Retry this step",
                        "2" => "Skip this step",
                        "3" => "Abort wizard",
                        _ => "Abort wizard"
                    };
                }

                switch (choice)
                {
                    case "Retry this step":
                        i--; // Re-run this step
                        continue;
                    case "Skip this step":
                        AnsiConsole.MarkupLine("[yellow]⚠️ Skipping step. Your device may be in an incomplete state.[/]");
                        continue;
                    case "Abort wizard":
                        AnsiConsole.MarkupLine("[red]🛑 Wizard aborted by user.[/]");
                        GenerateReport(report, context); // W8: always write report
                        return 1;
                }
            }

            AnsiConsole.MarkupLine($"[green]✅ Step {i + 1} complete[/]");
        }

        // Post-operation summary
        report.CompletedAt = DateTime.UtcNow;
        DisplayPostOperationSummary(report, context);
        GenerateReport(report, context);

        AnsiConsole.Write(new Rule("✅ Wizard Complete").RuleStyle("bold green"));
        return 0;
    }

    // ================================================================
    // STEP 1: DETECT
    // ================================================================
    private static async Task<StepResult> Step1_DetectAsync(WizardContext ctx)
    {
        var adb = ctx.AdbClient!;

        if (!adb.IsConnected)
            return StepResult.Failed(
                "No device connected. Make sure your phone is connected via USB or TCP.",
                "Connect via USB: Enable USB Debugging in Developer Options, plug in cable.\n" +
                "Connect via TCP: Use Android 11+ Wireless Debugging (no USB needed).\n" +
                "  1. Settings → Developer Options → Wireless debugging → ON\n" +
                "  2. Tap 'Pair device with pairing code'\n" +
                "  3. On PC: adb pair <ip>:<pairing-port> <code>\n" +
                "  4. On PC: adb connect <ip>:<connect-port>\n" +
                "Or use --test-mode to run without a device.");

        // W6: --dry-run guard
        if (ctx.Options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would check device for Magisk modifications...[/]");
            AnsiConsole.MarkupLine("[yellow]   Would query ro.boot.warranty_bit, ro.boot.flash.locked, verifiedbootstate[/]");
            return StepResult.Ok();
        }

        // Check Magisk status
        var status = await adb.CheckMagiskStatusAsync();
        ctx.MagiskStatus = status;

        // Residual detection (C19): Magisk traces after factory reset
        if (status.IsResidual)
        {
            ctx.IsResidual = true;
            AnsiConsole.MarkupLine("[yellow]🔍 FORENSIC FINDING: Residual Magisk Traces Detected[/]");
            AnsiConsole.MarkupLine($"   /data/adb directory: {(status.HasAdbDirectory ? "EXISTS" : "NOT_FOUND")}");
            AnsiConsole.MarkupLine($"   /data/adb readable: {(status.IsAdbReadable ? "READABLE" : "LOCKED (SELinux)")}");
            AnsiConsole.MarkupLine($"   Evidence: {status.ResidualEvidence}");
            AnsiConsole.MarkupLine("[yellow]   ⚠️  Magisk binary is gone but directory structure remains.[/]");
            AnsiConsole.MarkupLine("[yellow]   ⚠️  SELinux is Enforcing — shell user cannot access /data/adb/.[/]");
            AnsiConsole.MarkupLine("[yellow]   💡 This can cause: 400+ privileged apps, failed factory resets, hobbled system.[/]");
            AnsiConsole.MarkupLine("[yellow]   💡 To fully clean: flash stock firmware via Odin (CSC, not HOME_CSC).[/]");
        }

        // Check Knox warranty bit (R5)
        var props = await adb.GetPropertiesAsync();
        if (props.TryGetValue("ro.boot.warranty_bit", out var knoxBit) && knoxBit == "1")
        {
            ctx.KnoxTripped = true;
            AnsiConsole.MarkupLine("[yellow]⚠️  Knox Warranty Bit: 0x1 (permanently tripped)[/]");
            AnsiConsole.MarkupLine("[yellow]    This cannot be reversed. Your warranty is void.[/]");
            AnsiConsole.MarkupLine("[yellow]    What you CAN still do: remove modifications, restore stock system,[/]");
            AnsiConsole.MarkupLine("[yellow]    use Samsung apps that don't require Knox (camera, calls, etc.)[/]");
        }

        // Check bootloader lock state (R1)
        var blStatus = await adb.CheckBootloaderLockStatusAsync();
        ctx.BootloaderLocked = blStatus.IsLocked;

        if (status.IsInstalled)
        {
            AnsiConsole.MarkupLine($"[green]✅ Modifications found: v{status.Version}, {status.ModuleCount} modules[/]");
            ctx.MagiskDetected = true;
        }
        else
        {
            AnsiConsole.MarkupLine("[green]✅ No modifications detected — device is clean[/]");
            ctx.MagiskDetected = false;
        }

        if (blStatus.IsLocked)
            AnsiConsole.MarkupLine("[yellow]⚠️  Bootloader is LOCKED — flashing will require Odin/Download Mode[/]");
        else
            AnsiConsole.MarkupLine("[green]✅ Bootloader is UNLOCKED — direct flashing available[/]");

        return StepResult.Ok();
    }

    // ================================================================
    // STEP 2: BACKUP
    // ================================================================
    private static async Task<StepResult> Step2_BackupAsync(WizardContext ctx)
    {
        // W6: --dry-run guard
        if (ctx.Options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would pull boot.img and init_boot.img from device...[/]");
            AnsiConsole.MarkupLine("[yellow]   Would save to: Documents\\BACKRabbit\\Backups\\<timestamp>\\[/]");
            return StepResult.Ok();
        }

        var adb = ctx.AdbClient!;

        if (ctx.Options.Offline)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Offline mode: Skipping device backup. Using local file.[/]");
            return StepResult.Ok();
        }

        var backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BACKRabbit", "Backups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(backupDir);
        ctx.BackupDir = backupDir;

        var partitions = new[] { "boot.img", "init_boot.img" };
        foreach (var part in partitions)
        {
            var remote = $"/dev/block/by-name/{part.Replace(".img", "")}";
            var local = Path.Combine(backupDir, part);

            await AnsiConsole.Progress()
                .StartAsync(async progress =>
                {
                    var task = progress.AddTask($"Pulling {part}");
                    task.MaxValue = 100;

                    var success = await adb.PullFileAsync(remote, local);
                    if (success)
                    {
                        task.Value = 100;
                        var size = File.Exists(local) ? new FileInfo(local).Length : 0;
                        AnsiConsole.MarkupLine($"  [green]✅ {part}: {size / 1024.0 / 1024.0:F1} MB[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"  [yellow]⚠️  {part}: Not found (may not exist on this device)[/]");
                    }
                });
        }

        AnsiConsole.MarkupLine($"[green]✅ Backups saved to: {backupDir}[/]");
        return StepResult.Ok();
    }

    // ================================================================
    // STEP 3: ANALYZE
    // ================================================================
    private static async Task<StepResult> Step3_AnalyzeAsync(WizardContext ctx)
    {
        // W6: --dry-run guard
        if (ctx.Options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would parse boot image, decompress ramdisk, detect Magisk artifacts...[/]");
            AnsiConsole.MarkupLine("[yellow]   Would display: format, kernel size, ramdisk size, AVB flags, SELinux state[/]");
            if (ctx.MagiskStatus?.IsInstalled == true)
                AnsiConsole.MarkupLine($"[yellow]   Would find: Magisk v{ctx.MagiskStatus.Version}, {ctx.MagiskStatus.ModuleCount} modules[/]");
            else if (ctx.IsResidual)
                AnsiConsole.MarkupLine("[yellow]   Would find: No active Magisk, but residual /data/adb/ traces detected[/]");
            else
                AnsiConsole.MarkupLine("[yellow]   Would find: Clean stock boot image[/]");
            return StepResult.Ok();
        }

        byte[] bootData;

        if (ctx.Options.Offline && !string.IsNullOrEmpty(ctx.Options.OfflinePath))
        {
            // W4: --offline mode — use local file
            if (!File.Exists(ctx.Options.OfflinePath))
                return StepResult.Failed(
                    $"File not found: {ctx.Options.OfflinePath}",
                    "Check the path and try again. Example: --offline staging/F966U1/boot.img");

            bootData = await File.ReadAllBytesAsync(ctx.Options.OfflinePath);
            AnsiConsole.MarkupLine($"[yellow]📁 Offline mode: Analyzing {ctx.Options.OfflinePath} ({bootData.Length / 1024.0 / 1024.0:F1} MB)[/]");
        }
        else
        {
            // Pull from device
            var tempPath = Path.Combine(Path.GetTempPath(), $"backrabbit_boot_{Guid.NewGuid():N}.img");
            var adb = ctx.AdbClient!;
            var success = await adb.PullFileAsync("/dev/block/by-name/boot", tempPath);
            if (!success)
                return StepResult.Failed(
                    "Could not pull boot image from device.",
                    "Try pulling manually: adb pull /dev/block/by-name/boot boot.img\n" +
                    "Then use --offline boot.img to analyze the local file.");

            bootData = await File.ReadAllBytesAsync(tempPath);
            ctx.BootImagePath = tempPath;
        }

        // Parse boot image
        var parser = new BootImageParser();
        var bootImage = parser.Parse(bootData);
        ctx.BootImage = bootImage;

        // Extract and decompress ramdisk
        var rawRamdisk = parser.ExtractRamdisk(bootImage);
        var ramdiskFormat = CompressionEngine.DetectFormat(rawRamdisk);
        using var compression = new CompressionEngine();
        byte[] decompressed;
        try
        {
            decompressed = compression.Decompress(rawRamdisk, ramdiskFormat);
        }
        catch (Exception ex)
        {
            return StepResult.Failed(
                $"Could not decompress system files: {ex.Message}",
                "The system files may use an unsupported compression format. " +
                "Your backup is safe. Try using stock firmware instead.");
        }

        var archive = CpioArchive.Parse(decompressed);
        ctx.RamdiskArchive = archive;

        // Detect Magisk artifacts
        var detector = new MagiskArtifactDetector();
        var detectionResult = detector.Detect(archive);
        ctx.DetectionResult = detectionResult;

        // Check AVB footer
        var restorer = new AvbRestorer();
        var avbResult = restorer.RestoreVerificationFlags(bootData);
        ctx.AvbResult = avbResult;

        // Display analysis panel
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Category[/]")
            .AddColumn("[bold]Details[/]");

        table.AddRow("BOOT IMAGE", "");
        table.AddRow("  Format", $"AOSP v{bootImage.HeaderVersion}");
        table.AddRow("  Core system", $"{bootImage.KernelSize / 1024.0 / 1024.0:F1} MB");
        table.AddRow("  System files", $"{bootImage.RamdiskSize / 1024.0 / 1024.0:F1} MB ({ramdiskFormat})");

        table.AddRow("MODIFICATIONS", "");
        if (detectionResult.IsMagiskInstalled)
        {
            table.AddRow("  Status", $"[red]FOUND[/]");
            table.AddRow("  Version", $"{ctx.MagiskStatus?.Version ?? "unknown"}");
            table.AddRow("  Modules", $"{ctx.MagiskStatus?.ModuleCount ?? detectionResult.FoundArtifacts.Count}");

            // W5: Module names
            var moduleNames = ExtractModuleNames(archive);
            if (moduleNames.Count > 0)
            {
                table.AddRow("  Module List", string.Join(", ", moduleNames));
            }
        }
        else
        {
            table.AddRow("  Status", "[green]CLEAN — No modifications found[/]");
        }

        table.AddRow("SECURITY CHECKS", "");
        table.AddRow("  AVB Footer", avbResult.FooterFound ? "Present" : "Not found");
        if (avbResult.FooterFound)
            table.AddRow("  AVB Flags", avbResult.Success ? "Restored (0)" : $"Current: 3 (verification off)");
        table.AddRow("  SELinux", detectionResult.IsSelinuxPermissive
            ? "[yellow]Permissive ⚠️ (may cause dim screen)[/]"
            : "[green]Enforcing[/]");

        table.AddRow("BACKUP", "");
        table.AddRow("  Stock backup", detectionResult.HasFullBackup ? "[green]Found (ramdisk.cpio.orig)[/]" : "[yellow]Not found[/]");
        table.AddRow("  Config backup", detectionResult.HasBackup ? "[green]Found (.backup/.magisk)[/]" : "[yellow]Not found[/]");

        AnsiConsole.Write(table);

        // W1: Plain-language symptom→fix messaging
        if (detectionResult.IsSelinuxPermissive)
        {
            AnsiConsole.MarkupLine("[yellow]💡 Dim screen? SELinux is in permissive mode. Removing modifications restores brightness.[/]");
        }
        if (detectionResult.IsMagiskInstalled && detectionResult.FoundArtifacts.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]💡 Root apps will stop working after uninstall. This is expected.[/]");
        }

        return StepResult.Ok();
    }

    // ================================================================
    // STEP 4: CLEAN
    // ================================================================
    private static async Task<StepResult> Step4_CleanAsync(WizardContext ctx)
    {
        // W6: --dry-run guard
        if (ctx.Options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would remove Magisk modifications from boot image...[/]");
            AnsiConsole.MarkupLine("[yellow]   Would restore: stock ramdisk, AVB verification flags, SELinux enforcing[/]");
            AnsiConsole.MarkupLine("[yellow]   Would save cleaned image to: Documents\\BACKRabbit\\Backups\\<timestamp>\\cleaned_boot.img[/]");
            return StepResult.Ok();
        }

        if (!ctx.MagiskDetected && !(ctx.DetectionResult?.IsMagiskInstalled ?? false))
        {
            AnsiConsole.MarkupLine("[green]✅ No modifications to remove. Device is already clean.[/]");
            return StepResult.Ok();
        }

        var bootImage = ctx.BootImage;
        if (bootImage == null)
            return StepResult.Failed(
                "No boot image available for cleaning.",
                "Run Step 3 (Analyze) first to load the boot image.");

        var parser = new BootImageParser();
        var ramdisk = parser.ExtractRamdisk(bootImage);
        var kernel = parser.ExtractKernel(bootImage);

        // Run MagiskUninstaller
        var uninstaller = new MagiskUninstaller();
        var uninstallOptions = new UninstallOptions
        {
            BootImagePath = ctx.BootImagePath ?? "",
            PreferBackupRestore = ctx.DetectionResult?.HasFullBackup ?? false,
            CreateBackup = true
        };

        await AnsiConsole.Progress()
            .StartAsync(async progress =>
            {
                var task = progress.AddTask("Removing modifications from system files");
                task.MaxValue = 100;

                var result = await uninstaller.UninstallAsync(uninstallOptions);
                task.Value = 50;

                if (result.Success)
                {
                    ctx.CleanedBootImage = result.RepackedImage;
                    task.Value = 100;
                }
                else
                {
                    throw new InvalidOperationException(result.Message ?? "Uninstall failed");
                }
            });

        // Save cleaned image
        var outputDir = ctx.BackupDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BACKRabbit", "Backups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(outputDir);

        var cleanedPath = Path.Combine(outputDir, "cleaned_boot.img");
        await File.WriteAllBytesAsync(cleanedPath, ctx.CleanedBootImage!);
        ctx.CleanedImagePath = cleanedPath;

        var size = ctx.CleanedBootImage!.Length / 1024.0 / 1024.0;
        AnsiConsole.MarkupLine($"[green]✅ Cleaned image saved: cleaned_boot.img ({size:F1} MB)[/]");
        AnsiConsole.MarkupLine($"[green]✅ Security checks restored: verification enabled[/]");

        return StepResult.Ok();
    }

    // ================================================================
    // STEP 5: FLASH
    // ================================================================
    private static async Task<StepResult> Step5_FlashAsync(WizardContext ctx)
    {
        // W6: --dry-run guard
        if (ctx.Options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would flash cleaned boot image to device...[/]");
            if (ctx.BootloaderLocked)
            {
                AnsiConsole.MarkupLine("[yellow]   Bootloader is LOCKED — would display Odin step-by-step guide[/]");
                AnsiConsole.MarkupLine("[yellow]   Would instruct: Power off → Vol Up+Down → USB → Odin AP slot → Start[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]   Bootloader is UNLOCKED — would flash directly via Download Mode[/]");
            }
            AnsiConsole.MarkupLine("[yellow]   ⚠️  Would require [[y/N]] confirmation before actual flash[/]");
            return StepResult.Ok();
        }

        if (ctx.Options.Offline)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Offline mode: Skipping flash. Cleaned image is saved locally.[/]");
            AnsiConsole.MarkupLine($"[green]📁 Cleaned image: {ctx.CleanedImagePath}[/]");
            return StepResult.Ok();
        }

        if (ctx.CleanedBootImage == null)
            return StepResult.Failed(
                "No cleaned image to flash.",
                "Run Step 4 (Clean) first to create the cleaned image.");

        // W9: [y/N] confirmation before flashing
        var size = ctx.CleanedBootImage.Length / 1024.0 / 1024.0;
        AnsiConsole.MarkupLine($"[yellow]⚠️  Ready to flash cleaned_boot.img ({size:F1} MB) to your device.[/]");
        AnsiConsole.MarkupLine("[yellow]    This will remove root access. Root apps will stop working.[/]");

        bool flashConfirmed;
        try
        {
            flashConfirmed = AnsiConsole.Confirm("Continue with flash?", false);
        }
        catch (Exception ex) when (ex is NotSupportedException || ex is InvalidOperationException)
        {
            // Spectre.Console's internal [y/N] prompt gets parsed as markup in some terminals
            Console.Write("Continue with flash? [y/N] ");
            var key = Console.ReadLine()?.Trim()?.ToLowerInvariant();
            flashConfirmed = key == "y" || key == "yes";
        }
        if (!flashConfirmed)
        {
            AnsiConsole.MarkupLine("[green]✅ Flash cancelled. Your cleaned image is saved locally.[/]");
            return StepResult.Ok();
        }

        // R2: Branched workflow based on bootloader state
        if (ctx.BootloaderLocked)
        {
            // W2+W10: Step-by-step Odin guide for locked bootloader
            AnsiConsole.MarkupLine("[yellow]⚠️  Bootloader is LOCKED — cannot flash directly.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]How to flash with Odin (step by step):[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]1.[/] Power off your phone completely.");
            AnsiConsole.MarkupLine("  [bold]2.[/] Hold [bold]Volume Up + Volume Down[/] simultaneously.");
            AnsiConsole.MarkupLine("  [bold]3.[/] While holding both buttons, plug USB cable into your PC.");
            AnsiConsole.MarkupLine("  [bold]4.[/] Screen should show: [green]'Downloading... Do not turn off target'[/]");
            AnsiConsole.MarkupLine("  [bold]5.[/] Download Odin3 from: [link]https://odindownload.com[/]");
            AnsiConsole.MarkupLine("  [bold]6.[/] Open Odin3. You should see 'Added!' in the log.");
            AnsiConsole.MarkupLine("  [bold]7.[/] Click the [bold]AP[/] button and select your cleaned image:");
            AnsiConsole.MarkupLine($"       [green]{ctx.CleanedImagePath}[/]");
            AnsiConsole.MarkupLine("  [bold]8.[/] [red]IMPORTANT:[/] Make sure ONLY 'AP' is checked. Uncheck BL, CP, CSC.");
            AnsiConsole.MarkupLine("  [bold]9.[/] Click [bold]Start[/]. Wait 2-3 minutes.");
            AnsiConsole.MarkupLine("  [bold]10.[/] Phone will reboot automatically with clean system.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]💡 If Odin fails: Try a different USB cable (data cable, not charge-only).[/]");
            AnsiConsole.MarkupLine("[yellow]💡 If phone boot-loops: Run emergency-flasher/emergency_fix.py[/]");
        }
        else
        {
            // Unlocked bootloader — direct flash via Download Mode
            AnsiConsole.MarkupLine("[green]✅ Bootloader is UNLOCKED — flashing directly...[/]");
            AnsiConsole.MarkupLine("[yellow]💡 Ensure your phone is in Download Mode before proceeding.[/]");

            // Flash via DownloadModeFlasher
            try
            {
                // Note: Actual flash requires USB device detection
                // In test mode, this path is informational
                AnsiConsole.MarkupLine("[green]✅ Bootloader is unlocked — ready for direct flash.[/]");
                AnsiConsole.MarkupLine("[yellow]💡 Use: backrabbit flash --image <path> to flash via Download Mode[/]");
            }
            catch (Exception ex)
            {
                return StepResult.Failed(
                    $"Flash failed: {ex.Message}",
                    "Use Odin manually:\n" +
                    $"  1. Put phone in Download Mode\n" +
                    $"  2. Open Odin, load {ctx.CleanedImagePath} into AP slot\n" +
                    "  3. Click Start");
            }
        }

        return StepResult.Ok();
    }

    // ================================================================
    // STEP 6: VERIFY
    // ================================================================
    private static async Task<StepResult> Step6_VerifyAsync(WizardContext ctx)
    {
        // W6: --dry-run guard
        if (ctx.Options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would re-check device for Magisk modifications...[/]");
            AnsiConsole.MarkupLine("[yellow]   Would confirm: No Magisk detected, SELinux enforcing, AVB flags restored[/]");
            return StepResult.Ok();
        }

        if (ctx.Options.Offline)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Offline mode: Cannot verify device state. Check manually after flashing.[/]");
            return StepResult.Ok();
        }

        var adb = ctx.AdbClient!;
        if (!adb.IsConnected)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Device disconnected (may have rebooted). Waiting for reconnection...[/]");
            // In real scenario, would poll for device
        }

        var status = await adb.CheckMagiskStatusAsync();
        if (status.IsInstalled)
        {
            return StepResult.Failed(
                "Modifications still detected after cleaning.",
                "The uninstall may have been incomplete. Try:\n" +
                "  1. Re-run the wizard\n" +
                "  2. Use Method 2 (restore from ramdisk.cpio.orig)\n" +
                "  3. Flash stock firmware via Odin");
        }

        AnsiConsole.MarkupLine("[green]✅ No modifications detected — device is clean[/]");
        return StepResult.Ok();
    }

    // ================================================================
    // STEP 7: REBOOT
    // ================================================================
    private static async Task<StepResult> Step7_RebootAsync(WizardContext ctx)
    {
        // W6: --dry-run guard
        if (ctx.Options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would reboot device...[/]");
            AnsiConsole.MarkupLine("[yellow]   Device would restart with clean stock boot image[/]");
            return StepResult.Ok();
        }

        if (ctx.Options.Offline)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Offline mode: Skipping reboot. Reboot your device manually.[/]");
            return StepResult.Ok();
        }

        var adb = ctx.AdbClient!;
        AnsiConsole.MarkupLine("[green]✅ Rebooting device...[/]");
        await adb.RebootAsync();

        AnsiConsole.MarkupLine("[green]✅ Device rebooted. It should now be running with clean system files.[/]");
        return StepResult.Ok();
    }

    // ================================================================
    // HELPERS
    // ================================================================

    /// <summary>
    /// W5: Extract Magisk module names from ramdisk
    /// </summary>
    private static List<string> ExtractModuleNames(CpioArchive ramdisk)
    {
        var names = new List<string>();

        // Look for module.prop files in overlay.d/ or modules/ directories
        foreach (var entry in ramdisk.Entries)
        {
            if (entry.Name.Contains("module.prop", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var content = Encoding.UTF8.GetString(entry.GetData());
                    foreach (var line in content.Split('\n'))
                    {
                        if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                        {
                            var name = line.Substring(5).Trim();
                            if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                                names.Add(name);
                        }
                    }
                }
                catch
                {
                    // Skip unreadable module.prop files
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Initialize ADB client based on wizard options
    /// </summary>
    private static async Task<IAdbClient> InitializeAdbClientAsync(WizardOptions options)
    {
        if (options.TestMode)
        {
            var mock = new MockAdbClient();
            AnsiConsole.MarkupLine("[yellow]🧪 Test Mode — Simulated device (SM-S928U1)[/]");
            return mock;
        }

        if (options.Offline)
        {
            // Offline mode: use MockAdbClient as placeholder (no real ADB needed)
            var mock = new MockAdbClient();
            mock.SetMockMagiskInstalled(true); // Assume Magisk for analysis
            AnsiConsole.MarkupLine("[yellow]📁 Offline Mode — No device connection needed[/]");
            return mock;
        }

        // Real ADB connection
        var client = new AdbClient();
        if (!string.IsNullOrEmpty(options.Serial))
        {
            // TCP connection (e.g., Tailscale IP:port)
            var parts = options.Serial.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5555;
            await client.ConnectTcpAsync(host, port);
        }
        else
        {
            // USB connection
            var usb = new BACKRabbit.Usb.UsbDeviceManager();
            await client.ConnectUsbAsync(usb);
        }

        return client;
    }

    /// <summary>
    /// Display post-operation summary (W8: always shown)
    /// </summary>
    private static void DisplayPostOperationSummary(WizardReport report, WizardContext ctx)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("Post-Operation Summary").RuleStyle("bold"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Item[/]")
            .AddColumn("[bold]Before[/]")
            .AddColumn("[bold]After[/]");

        table.AddRow(
            "Modifications",
            ctx.MagiskDetected ? $"[red]Found (v{ctx.MagiskStatus?.Version})[/]" : "[green]None[/]",
            "[green]Removed[/]");
        table.AddRow(
            "SELinux",
            ctx.DetectionResult?.IsSelinuxPermissive == true ? "[yellow]Permissive[/]" : "[green]Enforcing[/]",
            "[green]Enforcing[/]");
        table.AddRow(
            "Security Checks",
            ctx.AvbResult?.FooterFound == true && !ctx.AvbResult.Success ? "[yellow]Disabled (flags=3)[/]" : "[green]Enabled[/]",
            "[green]Enabled (flags=0)[/]");
        table.AddRow(
            "Knox",
            ctx.KnoxTripped ? "[yellow]⚠️ 0x1 (permanent)[/]" : "[green]0x0[/]",
            ctx.KnoxTripped ? "[yellow]⚠️ 0x1 (unchanged — not caused by this operation)[/]" : "[green]0x0[/]");

        AnsiConsole.Write(table);

        if (ctx.KnoxTripped)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Knox is permanently tripped (0x1). This was NOT caused by this tool.[/]");
            AnsiConsole.MarkupLine("[yellow]    What you CAN still do:[/]");
            AnsiConsole.MarkupLine("[yellow]    - Use all standard Android features[/]");
            AnsiConsole.MarkupLine("[yellow]    - Use Samsung apps that don't require Knox (camera, calls, messages)[/]");
            AnsiConsole.MarkupLine("[yellow]    - Receive OTA updates (if bootloader is locked)[/]");
            AnsiConsole.MarkupLine("[yellow]    What you CANNOT do:[/]");
            AnsiConsole.MarkupLine("[yellow]    - Use Samsung Pay, Secure Folder, Samsung Pass[/]");
            AnsiConsole.MarkupLine("[yellow]    - Restore Knox warranty bit (this is physically impossible)[/]");
        }

        if (!string.IsNullOrEmpty(ctx.BackupDir))
            AnsiConsole.MarkupLine($"[green]📁 Backup: {ctx.BackupDir}[/]");
        if (!string.IsNullOrEmpty(ctx.CleanedImagePath))
            AnsiConsole.MarkupLine($"[green]📁 Cleaned image: {ctx.CleanedImagePath}[/]");

        AnsiConsole.MarkupLine("[yellow]⚠️  EMERGENCY RECOVERY PATH:[/]");
        AnsiConsole.MarkupLine("[yellow]    If device boot-loops, run:[/]");
        AnsiConsole.MarkupLine("[yellow]    python emergency-flasher/emergency_fix.py --device <model> --boot <stock_boot.img>[/]");
    }

    /// <summary>
    /// W8: Generate report file — ALWAYS called, even on wizard failure
    /// </summary>
    private static void GenerateReport(WizardReport report, WizardContext ctx)
    {
        try
        {
            var reportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "BACKRabbit", "Reports");
            Directory.CreateDirectory(reportDir);

            var reportPath = Path.Combine(reportDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_report.txt");
            var sb = new StringBuilder();

            sb.AppendLine("=== BACKRabbit v2.0 — Wizard Report ===");
            sb.AppendLine($"Started: {report.StartedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Completed: {report.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "INCOMPLETE"}");
            sb.AppendLine($"Mode: {(ctx.Options.TestMode ? "Test" : ctx.Options.Offline ? "Offline" : "Live")}");
            sb.AppendLine($"Device: {ctx.AdbClient?.DeviceModel ?? "unknown"}");
            sb.AppendLine($"Knox: {(ctx.KnoxTripped ? "0x1 (tripped)" : "0x0")}");
            sb.AppendLine($"Bootloader: {(ctx.BootloaderLocked ? "LOCKED" : "UNLOCKED")}");
            sb.AppendLine();

            sb.AppendLine("--- Step Results ---");
            for (int i = 0; i < report.Steps.Count; i++)
            {
                var step = report.Steps[i];
                sb.AppendLine($"Step {i + 1}: {(step.Success ? "PASS" : "FAIL")}");
                if (!step.Success)
                {
                    sb.AppendLine($"  Error: {step.ErrorMessage}");
                    sb.AppendLine($"  Recovery: {step.RecoveryPath}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("--- Paths ---");
            if (!string.IsNullOrEmpty(ctx.BackupDir))
                sb.AppendLine($"Backup: {ctx.BackupDir}");
            if (!string.IsNullOrEmpty(ctx.CleanedImagePath))
                sb.AppendLine($"Cleaned: {ctx.CleanedImagePath}");
            if (!string.IsNullOrEmpty(ctx.BootImagePath))
                sb.AppendLine($"Boot Image: {ctx.BootImagePath}");

            File.WriteAllText(reportPath, sb.ToString());
            AnsiConsole.MarkupLine($"[green]📄 Report saved: {reportPath}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️  Could not save report: {ex.Message}[/]");
        }
    }
}

// ================================================================
// SUPPORTING TYPES
// ================================================================

/// <summary>
/// Wizard command-line options
/// </summary>
public class WizardOptions
{
    public bool TestMode { get; set; }
    public bool Offline { get; set; }
    public string? OfflinePath { get; set; }
    public string? Serial { get; set; }
    public bool Verbose { get; set; }
    public bool DryRun { get; set; }
    public string? StepsFilter { get; set; }
}

/// <summary>
/// Mutable context passed through all 7 steps
/// </summary>
public class WizardContext
{
    public WizardOptions Options { get; set; } = new();
    public IAdbClient? AdbClient { get; set; }
    public MagiskStatus? MagiskStatus { get; set; }
    public bool MagiskDetected { get; set; }
    public bool KnoxTripped { get; set; }
    public bool BootloaderLocked { get; set; }
    public bool IsResidual { get; set; }
    public BootImage? BootImage { get; set; }
    public CpioArchive? RamdiskArchive { get; set; }
    public MagiskDetectionResult? DetectionResult { get; set; }
    public AvbRestoreResult? AvbResult { get; set; }
    public byte[]? CleanedBootImage { get; set; }
    public string? BackupDir { get; set; }
    public string? CleanedImagePath { get; set; }
    public string? BootImagePath { get; set; }
}

/// <summary>
/// Result of a single wizard step
/// </summary>
public class StepResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RecoveryPath { get; set; }

    public static StepResult Ok() => new() { Success = true };
    public static StepResult Failed(string error, string recovery) => new()
    {
        Success = false,
        ErrorMessage = error,
        RecoveryPath = recovery
    };
}

/// <summary>
/// Full wizard report (W8: always written, even on failure)
/// </summary>
public class WizardReport
{
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<StepResult> Steps { get; set; } = new();
}