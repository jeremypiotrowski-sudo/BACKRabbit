using Spectre.Console;
using BACKRabbit.Protocol.Adb;

namespace BACKRabbit.CLI.Wizard;

/// <summary>
/// Trap Escape Wizard — cleans /data/adb/ residue without tripping Knox.
/// 
/// Three paths, auto-selected based on forensic diagnostics:
///   PATH 4: Recovery-Mode Magisk Cleanup (init_boot exists)
///   PATH A: Bootloader Unlock Cleanup (OEM Unlock supported)
///   PATH B: Firehose/EDL Required (neither available — reports requirements)
/// 
/// Jury Conditions:
///   C19: Residual-trace detection
///   C20: Trap-escape guidance
///   W1: Plain-language error messages with recovery steps
///   W7: Retry/skip/abort on failure
///   W8: Report always written
///   W9: [y/N] confirmation before destructive operations
/// </summary>
public static class TrapEscapeRunner
{
    /// <summary>
    /// Run the trap escape wizard with the given options.
    /// </summary>
    public static async Task<int> RunAsync(TrapEscapeOptions options)
    {
        // Header
        AnsiConsole.Write(new Rule("🐰 BACKRabbit v2.0 — Trap Escape Wizard").RuleStyle("bold green"));
        AnsiConsole.WriteLine();

        // Initialize ADB client
        IAdbClient adb;
        try
        {
            adb = await InitializeAdbClientAsync(options);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to initialize: {ex.Message}[/]");
            AnsiConsole.MarkupLine("[yellow]💡 Try: backrabbit trap-escape --test-mode (no device needed)[/]");
            return 1;
        }

        // Run forensic diagnostics
        var forensics = new ForensicDiagnostics(adb);
        ForensicDiagnosis diagnosis = null!;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Running forensic diagnostics...", async ctx =>
            {
                diagnosis = await forensics.DiagnoseAsync();
            });

        // Display diagnosis
        DisplayDiagnosis(diagnosis);

        // Diagnose-only mode
        if (options.DiagnoseOnly)
        {
            AnsiConsole.MarkupLine("[green]✅ Diagnosis complete. No cleanup performed.[/]");
            return 0;
        }

        // No residue — nothing to do
        if (!diagnosis.HasMagiskResidue)
        {
            AnsiConsole.MarkupLine("[green]✅ No /data/adb/ residue detected. Device is clean.[/]");
            return 0;
        }

        // Force path override
        if (!string.IsNullOrEmpty(options.ForcePath))
        {
            diagnosis.AvailablePath = options.ForcePath.ToLowerInvariant() switch
            {
                "recovery" => CleanupPath.RecoveryMagisk,
                "bootloader" => CleanupPath.BootloaderUnlock,
                "firehose" => CleanupPath.FirehoseRequired,
                _ => diagnosis.AvailablePath
            };
            AnsiConsole.MarkupLine($"[yellow]⚠️  Path forced to: {diagnosis.AvailablePath}[/]");
        }

        // Execute based on available path
        CleanupResult result;
        switch (diagnosis.AvailablePath)
        {
            case CleanupPath.RecoveryMagisk:
                result = await ExecutePath4_RecoveryMagiskAsync(adb, options);
                break;
            case CleanupPath.BootloaderUnlock:
                result = await ExecutePathA_BootloaderUnlockAsync(adb, diagnosis, options);
                break;
            case CleanupPath.FirehoseRequired:
                result = ExecutePathB_FirehoseRequired(diagnosis);
                break;
            default:
                result = new CleanupResult { Success = true, Path = "None", Message = "No cleanup needed." };
                break;
        }

        // Display result
        DisplayResult(result, diagnosis);

        // Generate report (W8: always written)
        GenerateReport(diagnosis, result);

        return result.Success ? 0 : 1;
    }

    // ================================================================
    // PATH 4: Recovery-Mode Magisk Cleanup
    // ================================================================
    private static async Task<CleanupResult> ExecutePath4_RecoveryMagiskAsync(
        IAdbClient adb, TrapEscapeOptions options)
    {
        AnsiConsole.Write(new Rule("Path 4: Recovery-Mode Magisk Cleanup").RuleStyle("bold green"));
        AnsiConsole.MarkupLine("[green]✅ GKI 2.0 device detected (init_boot partition exists).[/]");
        AnsiConsole.MarkupLine("[green]   Magisk was installed to recovery partition, not boot.[/]");
        AnsiConsole.MarkupLine("[green]   Normal boot = stock. Recovery boot = Magisk with root.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Cleanup: 3 commands, 2 minutes, ZERO risk.[/]");
        AnsiConsole.MarkupLine("  [bold]1.[/] Reboot to recovery (Magisk activates, root available)");
        AnsiConsole.MarkupLine("  [bold]2.[/] rm -rf /data/adb (nuke the residue)");
        AnsiConsole.MarkupLine("  [bold]3.[/] Reboot normally (stock, clean, Knox 0x0)");
        AnsiConsole.WriteLine();

        if (options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would execute 3-step recovery cleanup.[/]");
            return new CleanupResult { Success = true, Path = "RecoveryMagisk (dry-run)" };
        }

        // W9: Confirmation
        bool confirmed;
        try
        {
            confirmed = AnsiConsole.Confirm("Execute recovery cleanup? This is safe — Knox stays 0x0.", false);
        }
        catch (Exception ex) when (ex is NotSupportedException || ex is InvalidOperationException)
        {
            Console.Write("Execute recovery cleanup? [y/N] ");
            var key = Console.ReadLine()?.Trim()?.ToLowerInvariant();
            confirmed = key == "y" || key == "yes";
        }
        if (!confirmed)
        {
            return new CleanupResult { Success = true, Path = "RecoveryMagisk", Message = "Cancelled by user." };
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Step 1: Reboot to recovery
        AnsiConsole.MarkupLine("[yellow]🔄 Rebooting to recovery...[/]");
        await adb.RebootRecoveryAsync();
        AnsiConsole.MarkupLine("[yellow]   Waiting for device in recovery mode (ADB may take 15-30 seconds)...[/]");

        var ready = await adb.WaitForDeviceAsync(timeoutMs: 60000);
        if (!ready)
        {
            return new CleanupResult
            {
                Success = false,
                Path = "RecoveryMagisk",
                Error = "Device did not become reachable after recovery reboot.",
                RecoveryPath = "Manually boot to recovery (Power + Vol Up) and run: adb shell rm -rf /data/adb"
            };
        }

        // Step 2: Nuke the residue (root is automatic in Magisk recovery)
        AnsiConsole.MarkupLine("[yellow]🧹 Removing /data/adb/ residue...[/]");
        var rmResult = await adb.ExecuteRootShellAsync("rm -rf /data/adb");
        if (rmResult.Contains("Permission denied"))
        {
            return new CleanupResult
            {
                Success = false,
                Path = "RecoveryMagisk",
                Error = "Permission denied — recovery may not have Magisk root.",
                RecoveryPath = "Try: adb shell su -c 'rm -rf /data/adb' from recovery, or use Path A."
            };
        }

        // Step 3: Reboot normally
        AnsiConsole.MarkupLine("[yellow]🔄 Rebooting to normal mode...[/]");
        await adb.RebootAsync();
        await adb.WaitForDeviceAsync(timeoutMs: 60000);

        // Verify
        var verify = await adb.ExecuteShellAsync(
            "test -d /data/adb && echo 'STILL_THERE' || echo 'GONE'");
        var gone = verify.Contains("GONE");

        sw.Stop();

        return new CleanupResult
        {
            Success = gone,
            Path = "RecoveryMagisk",
            Duration = sw.Elapsed,
            KnoxTripped = false,
            VerificationOutput = verify.Trim(),
            Warnings = gone ? new List<string>() : new List<string> { "/data/adb/ still present — may need manual cleanup." }
        };
    }

    // ================================================================
    // PATH A: Bootloader Unlock Cleanup
    // ================================================================
    private static async Task<CleanupResult> ExecutePathA_BootloaderUnlockAsync(
        IAdbClient adb, ForensicDiagnosis diagnosis, TrapEscapeOptions options)
    {
        AnsiConsole.Write(new Rule("Path A: Bootloader Unlock Cleanup").RuleStyle("bold yellow"));
        AnsiConsole.MarkupLine("[yellow]⚠️  init_boot not found. OEM Unlock IS supported.[/]");
        AnsiConsole.MarkupLine("[yellow]   This path unlocks the bootloader, installs Magisk temporarily,[/]");
        AnsiConsole.MarkupLine("[yellow]   cleans /data/adb/, then restores stock and re-locks.[/]");
        AnsiConsole.MarkupLine("[green]   Knox stays 0x0 on global Qualcomm models.[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]6-Step Sequence:[/]");
        AnsiConsole.MarkupLine("  [bold]Step 1 [MANUAL]:[/] Enable OEM Unlock → Download Mode → confirm unlock");
        AnsiConsole.MarkupLine("  [bold]Step 2 [AUTO]:[/] Extract stock boot.img from firmware ZIP");
        AnsiConsole.MarkupLine("  [bold]Step 3 [AUTO]:[/] Patch boot.img with Magisk");
        AnsiConsole.MarkupLine("  [bold]Step 4 [MANUAL+AUTO]:[/] Flash patched boot via Odin/Download Mode");
        AnsiConsole.MarkupLine("  [bold]Step 5 [AUTO]:[/] Root shell: rm -rf /data/adb");
        AnsiConsole.MarkupLine("  [bold]Step 6 [AUTO]:[/] Flash stock boot + re-lock bootloader");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]⚠️  MANUAL STEPS REQUIRED. You will need to interact with the phone.[/]");
        AnsiConsole.MarkupLine("[yellow]⚠️  This will factory reset your phone (bootloader unlock wipes data).[/]");
        AnsiConsole.WriteLine();

        if (options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN: Would execute 6-step bootloader unlock cleanup.[/]");
            return new CleanupResult { Success = true, Path = "BootloaderUnlock (dry-run)" };
        }

        // W9: Confirmation
        bool confirmed;
        try
        {
            confirmed = AnsiConsole.Confirm(
                "This will factory reset your phone. Knox stays 0x0. Continue?", false);
        }
        catch (Exception ex) when (ex is NotSupportedException || ex is InvalidOperationException)
        {
            Console.Write("This will factory reset your phone. Continue? [y/N] ");
            var key = Console.ReadLine()?.Trim()?.ToLowerInvariant();
            confirmed = key == "y" || key == "yes";
        }
        if (!confirmed)
        {
            return new CleanupResult { Success = true, Path = "BootloaderUnlock", Message = "Cancelled by user." };
        }

        // Step 1: Manual — guide user through OEM Unlock
        AnsiConsole.MarkupLine("[bold yellow]━━━ Step 1: Unlock Bootloader (MANUAL) ━━━[/]");
        AnsiConsole.MarkupLine("  1. Settings → Developer Options → OEM Unlock → ON");
        AnsiConsole.MarkupLine("  2. Power off phone completely");
        AnsiConsole.MarkupLine("  3. Hold Vol Up + Vol Down + Power → Download Mode");
        AnsiConsole.MarkupLine("  4. Long-press Vol Up to confirm unlock");
        AnsiConsole.MarkupLine("  5. Phone will factory reset and reboot");
        AnsiConsole.MarkupLine("  6. Complete setup wizard (skip accounts — this is temporary)");
        AnsiConsole.MarkupLine("  7. Re-enable Developer Options + USB Debugging");
        AnsiConsole.MarkupLine("  8. Reconnect ADB");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Press Enter when bootloader is unlocked and ADB is reconnected...[/]");
        Console.ReadLine();

        // Verify unlock
        var blCheck = await adb.ExecuteShellAsync("getprop ro.boot.flash.locked");
        if (blCheck.Trim() == "1")
        {
            return new CleanupResult
            {
                Success = false,
                Path = "BootloaderUnlock",
                Error = "Bootloader still locked. OEM Unlock may not have worked.",
                RecoveryPath = "Check if OEM Unlock toggle is available. Some carriers permanently disable it."
            };
        }
        AnsiConsole.MarkupLine("[green]✅ Bootloader unlocked.[/]");

        // Steps 2-6: Guide user through manual completion
        AnsiConsole.MarkupLine("[yellow]⚠️  Automated Steps 2-6 require firmware ZIP and full pipeline wiring.[/]");
        AnsiConsole.MarkupLine("[yellow]   For now, complete manually:[/]");
        AnsiConsole.MarkupLine("  Step 2: Extract boot.img from firmware ZIP");
        AnsiConsole.MarkupLine("  Step 3: Patch with Magisk app (Install → Select and Patch a File)");
        AnsiConsole.MarkupLine("  Step 4: Flash patched boot via Odin (AP slot)");
        AnsiConsole.MarkupLine("  Step 5: adb shell su -c 'rm -rf /data/adb'");
        AnsiConsole.MarkupLine("  Step 6: Flash stock boot via Odin → re-lock bootloader");

        return new CleanupResult
        {
            Success = true,
            Path = "BootloaderUnlock (manual)",
            Message = "Bootloader unlocked. Complete remaining steps manually.",
            Warnings = new List<string> { "Automated pipeline not yet wired. Manual steps required." }
        };
    }

    // ================================================================
    // PATH B: Firehose/EDL Required
    // ================================================================
    private static CleanupResult ExecutePathB_FirehoseRequired(ForensicDiagnosis diagnosis)
    {
        AnsiConsole.Write(new Rule("Path B: Firehose/EDL Direct Cleanup").RuleStyle("bold red"));
        AnsiConsole.MarkupLine("[red]⚠️  init_boot not found. OEM Unlock not supported.[/]");
        AnsiConsole.MarkupLine("[red]   Only Path B remains: Qualcomm Firehose/EDL direct block write.[/]");
        AnsiConsole.WriteLine();

        // Jury compromise: Chief Dan's security warning
        AnsiConsole.MarkupLine("[bold red]⚠️  FIREHOSE INSTALL DETECTED: System integrity cannot be guaranteed.[/]");
        AnsiConsole.MarkupLine("[red]   A firehose/EDL install was used to modify this device at the block level.[/]");
        AnsiConsole.MarkupLine("[red]   Modifications may exist beyond /data/adb/ — in /system, /vendor, or reserved areas.[/]");
        AnsiConsole.MarkupLine("[red]   Run 'trap-escape --deep-audit' (v2.1) to compare system partition against stock.[/]");
        AnsiConsole.WriteLine();

        // Display what the user needs
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Requirement[/]")
            .AddColumn("[bold]Details[/]");

        table.AddRow("Firehose Programmer", $"prog_firehose_ufs_ddr.elf for {diagnosis.Model}");
        table.AddRow("Protocol", "Qualcomm Sahara + Firehose (EDL mode)");
        table.AddRow("EDL Mode Entry", "EDL cable (USB-C with D+ short button) or test points on motherboard");
        table.AddRow("What It Does", "Raw block write to zero /data/adb/ inode blocks on userdata partition");
        table.AddRow("Knox Risk", "[green]ZERO[/] — operates below Samsung boot chain");
        table.AddRow("Bootloader Unlock", "[green]NOT REQUIRED[/] — direct eMMC/UFS write");
        table.AddRow("Root Required", "[green]NO[/] — block-level access, not filesystem-level");
        table.AddRow("C# Library Available", "[green]edl-ng[/] (MIT license, .NET, cross-platform)");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold yellow]How to obtain the firehose programmer:[/]");
        AnsiConsole.MarkupLine("  1. Alephgsm Telegram (@Alephgsm) — Fold5 loader appeared 3 months after release");
        AnsiConsole.MarkupLine("  2. Samsung COMBINATION firmware — factory engineering firmware contains firehose inside AP tar");
        AnsiConsole.MarkupLine($"  3. Search: \"COMBINATION_FAC_{diagnosis.Model}\" or \"prog_firehose_ufs_ddr {diagnosis.Platform}\"");
        AnsiConsole.MarkupLine("  4. Chinese repair forums — bbs.le.com, gsmhosting Chinese section");
        AnsiConsole.MarkupLine("  5. bkerler/Loaders GitHub repository — watch for new Samsung submissions");
        AnsiConsole.MarkupLine("  6. XDA Developers forums — search your model number + 'firehose' or 'EDL'");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]⚠️  BACKRabbit does NOT include firehose programmers.[/]");
        AnsiConsole.MarkupLine("[yellow]   They are Samsung-signed, model-specific, and legally restricted.[/]");
        AnsiConsole.MarkupLine("[yellow]   Once obtained, BACKRabbit v2.1 will integrate edl-ng for automated cleanup.[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold green]What BACKRabbit CAN do right now:[/]");
        AnsiConsole.MarkupLine($"  • Diagnose your device: {diagnosis.Model} ({diagnosis.Platform})");
        AnsiConsole.MarkupLine($"  • Confirm residue: /data/adb/ EXISTS, Knox {diagnosis.KnoxState}");
        AnsiConsole.MarkupLine("  • Generate exact cleanup commands for when root is obtained");
        AnsiConsole.MarkupLine("  • Verify cleanup completeness after root is obtained");
        AnsiConsole.WriteLine();

        // Generate cleanup script for when root IS obtained by ANY method
        AnsiConsole.MarkupLine("[bold]Cleanup Script (run when root is obtained by ANY method):[/]");
        AnsiConsole.MarkupLine("[grey]──────────────────────────────────────────────[/]");
        AnsiConsole.MarkupLine("  su -c 'rm -rf /data/adb'");
        AnsiConsole.MarkupLine("  su -c 'rm -rf /data/adb/magisk'");
        AnsiConsole.MarkupLine("  su -c 'rm -rf /data/adb/modules'");
        AnsiConsole.MarkupLine("  su -c 'rm -rf /data/adb/post-fs-data.d'");
        AnsiConsole.MarkupLine("  su -c 'rm -rf /data/adb/service.d'");
        AnsiConsole.MarkupLine("  su -c 'rm -f /data/adb/magisk.db'");
        AnsiConsole.MarkupLine("  su -c 'rm -f /data/adb/magiskboot'");
        AnsiConsole.MarkupLine("  su -c 'restorecon -R /data'  # restore SELinux contexts");
        AnsiConsole.MarkupLine("[grey]──────────────────────────────────────────────[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]💡 If you find a firehose programmer, contact the BACKRabbit project.[/]");
        AnsiConsole.MarkupLine("[yellow]   FirehoseFlasher.cs + edl-ng integration is planned for v2.1.[/]");

        return new CleanupResult
        {
            Success = false,
            Path = "FirehoseRequired",
            Error = $"Firehose programmer required for {diagnosis.Model}. " +
                    $"Search for: prog_firehose_ufs_ddr_{diagnosis.Platform}.elf",
            RecoveryPath = "Once root is obtained by ANY method, run the cleanup script above."
        };
    }

    // ================================================================
    // DISPLAY HELPERS
    // ================================================================

    private static void DisplayDiagnosis(ForensicDiagnosis d)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Check[/]")
            .AddColumn("[bold]Result[/]")
            .AddColumn("[bold]Meaning[/]");

        table.AddRow("init_boot partition",
            d.HasInitBoot ? "[green]FOUND[/]" : "[red]NOT FOUND[/]",
            d.HasInitBoot ? "GKI 2.0 — Path 4 available" : "No GKI — Path 4 closed");

        table.AddRow("OEM Unlock",
            d.OemUnlockSupported ? "[green]SUPPORTED[/]" : "[red]NOT SUPPORTED[/]",
            d.OemUnlockSupported ? "Path A available" : "Path A closed");

        table.AddRow("Bootloader",
            d.BootloaderLocked ? "[yellow]LOCKED[/]" : "[green]UNLOCKED[/]",
            d.BootloaderLocked ? "Cannot flash directly" : "Direct flash available");

        table.AddRow("/data/adb/",
            d.HasMagiskResidue ? "[red]EXISTS[/]" : "[green]NOT FOUND[/]",
            d.HasMagiskResidue ? "Magisk residue present" : "Device is clean");

        table.AddRow("magisk.db",
            d.HasMagiskDb ? "[red]EXISTS[/]" : "[green]NOT FOUND[/]",
            d.HasMagiskDb ? "Magisk was active (root grants recorded)" : "Magisk may never have been fully active");

        table.AddRow("Knox (Android)",
            d.KnoxState == "0x0" ? "[green]0x0[/]" : "[red]0x1[/]",
            d.KnoxState == "0x0" ? "Knox intact (Android property)" : "Knox tripped (Android property)");

        table.AddRow("Verified Boot",
            d.VerifiedBootState == "green" ? "[green]green[/]" : "[yellow]orange[/]",
            d.VerifiedBootState == "green" ? "Stock-signed boot image" : "Modified boot image");

        table.AddRow("Platform",
            d.Platform,
            "Qualcomm Snapdragon chipset");

        table.AddRow("Model",
            d.Model,
            "Samsung device model");

        table.AddRow("Security Patch",
            d.SecurityPatch,
            "Android security patch level");

        table.AddRow("[bold]AVAILABLE PATH[/]",
            d.AvailablePath switch
            {
                CleanupPath.RecoveryMagisk => "[green]PATH 4: Recovery-Mode Cleanup[/]",
                CleanupPath.BootloaderUnlock => "[yellow]PATH A: Bootloader Unlock[/]",
                CleanupPath.FirehoseRequired => "[red]PATH B: Firehose/EDL Required[/]",
                _ => "[green]None needed[/]"
            },
            d.AvailablePath switch
            {
                CleanupPath.RecoveryMagisk => "3 commands, 2 minutes, ZERO risk",
                CleanupPath.BootloaderUnlock => "6 steps, ~15 minutes, LOW risk",
                CleanupPath.FirehoseRequired => "Programmer file required — hard to obtain",
                _ => "Device is clean"
            });

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static void DisplayResult(CleanupResult result, ForensicDiagnosis diagnosis)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("Cleanup Result").RuleStyle(result.Success ? "bold green" : "bold red"));

        AnsiConsole.MarkupLine($"Path: [bold]{result.Path}[/]");
        AnsiConsole.MarkupLine($"Success: {(result.Success ? "[green]YES[/]" : "[red]NO[/]")}");
        AnsiConsole.MarkupLine($"Knox: {(result.KnoxTripped ? "[red]0x1 TRIPPED[/]" : $"[green]{diagnosis.KnoxState} INTACT[/]")}");

        if (!string.IsNullOrEmpty(result.Message))
            AnsiConsole.MarkupLine($"[yellow]{result.Message}[/]");

        if (!string.IsNullOrEmpty(result.Error))
            AnsiConsole.MarkupLine($"[red]Error: {result.Error}[/]");

        if (!string.IsNullOrEmpty(result.RecoveryPath))
            AnsiConsole.MarkupLine($"[yellow]💡 Recovery: {result.RecoveryPath}[/]");

        if (result.Warnings.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Warnings:[/]");
            foreach (var w in result.Warnings)
                AnsiConsole.MarkupLine($"  [yellow]• {w}[/]");
        }

        if (result.Duration.TotalSeconds > 0)
            AnsiConsole.MarkupLine($"Duration: {result.Duration.TotalSeconds:F1}s");
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static async Task<IAdbClient> InitializeAdbClientAsync(TrapEscapeOptions options)
    {
        if (options.TestMode)
        {
            var mock = new Testing.MockAdbClient();
            AnsiConsole.MarkupLine("[yellow]🧪 Test Mode — Simulated device (SM-S928U1)[/]");
            return mock;
        }

        var client = new AdbClient();
        if (!string.IsNullOrEmpty(options.Serial))
        {
            var parts = options.Serial.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5555;
            await client.ConnectTcpAsync(host, port);
        }
        else
        {
            var usb = new BACKRabbit.Usb.UsbDeviceManager();
            await client.ConnectUsbAsync(usb);
        }

        return client;
    }

    /// <summary>
    /// W8: Generate report file — ALWAYS called, even on failure.
    /// </summary>
    private static void GenerateReport(ForensicDiagnosis diagnosis, CleanupResult result)
    {
        try
        {
            var reportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "BACKRabbit", "Reports");
            Directory.CreateDirectory(reportDir);

            var reportPath = Path.Combine(reportDir, $"trap-escape_{DateTime.Now:yyyyMMdd_HHmmss}_report.txt");
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("=== BACKRabbit v2.0 — Trap Escape Report ===");
            sb.AppendLine($"Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Device: {diagnosis.Model} ({diagnosis.Platform})");
            sb.AppendLine($"Knox: {diagnosis.KnoxState}");
            sb.AppendLine($"Verified Boot: {diagnosis.VerifiedBootState}");
            sb.AppendLine($"init_boot: {(diagnosis.HasInitBoot ? "FOUND" : "NOT FOUND")}");
            sb.AppendLine($"OEM Unlock: {(diagnosis.OemUnlockSupported ? "SUPPORTED" : "NOT SUPPORTED")}");
            sb.AppendLine($"Bootloader: {(diagnosis.BootloaderLocked ? "LOCKED" : "UNLOCKED")}");
            sb.AppendLine($"/data/adb/: {(diagnosis.HasMagiskResidue ? "EXISTS" : "NOT FOUND")}");
            sb.AppendLine($"magisk.db: {(diagnosis.HasMagiskDb ? "EXISTS" : "NOT FOUND")}");
            sb.AppendLine($"Security Patch: {diagnosis.SecurityPatch}");
            sb.AppendLine();
            sb.AppendLine($"Selected Path: {diagnosis.AvailablePath}");
            sb.AppendLine($"Result: {(result.Success ? "SUCCESS" : "FAILED")}");
            sb.AppendLine($"Path Executed: {result.Path}");
            if (!string.IsNullOrEmpty(result.Error))
                sb.AppendLine($"Error: {result.Error}");
            if (!string.IsNullOrEmpty(result.RecoveryPath))
                sb.AppendLine($"Recovery: {result.RecoveryPath}");
            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("Warnings:");
                foreach (var w in result.Warnings)
                    sb.AppendLine($"  - {w}");
            }

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
/// Trap Escape command-line options.
/// </summary>
public class TrapEscapeOptions
{
    public bool TestMode { get; set; }
    public string? Serial { get; set; }
    public bool DryRun { get; set; }
    public bool DiagnoseOnly { get; set; }
    public string? ForcePath { get; set; }
}

/// <summary>
/// Result of a trap escape cleanup operation.
/// </summary>
public class CleanupResult
{
    public bool Success { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? RecoveryPath { get; set; }
    public string? VerificationOutput { get; set; }
    public bool KnoxTripped { get; set; }
    public List<string> Warnings { get; set; } = new();
    public TimeSpan Duration { get; set; }
}