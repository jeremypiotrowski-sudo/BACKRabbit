using System;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BACKRabbit.Protocol.Firehose;
using BACKRabbit.Protocol.Firehose.Rescue;

namespace BACKRabbit.CLI.Commands;

public static class FirehoseCommands
{
    private static readonly Option<string> DeviceOpt = new("--device", "Device path (COM port or USB ID)");
    private static readonly Option<string> LoaderOpt = new("--loader", "Path to Firehose programmer (.elf/.mbn)");
    private static readonly Option<int> LunOpt = new("--lun", () => 0, "Logical Unit Number");
    private static readonly Option<string> PartitionOpt = new("--partition", "Partition name") { IsRequired = true };
    private static readonly Option<string> OutputOpt = new("--output", "Output file path") { IsRequired = true };
    private static readonly Option<string> InputOpt = new("--input", "Input file to flash") { IsRequired = true };
    private static readonly Option<string> ModeOpt = new("--mode", () => "system", "Reset mode: edl, system, off");

    // Rescue-specific options
    private static readonly Option<string> BackupOpt = new("--backup", "Path to known-good backup directory") { IsRequired = true };
    private static readonly Option<string> PartitionsOpt = new("--partitions", "Comma-separated partition names to restore");
    private static readonly Option<string> SocOpt = new("--soc", "SoC model override for QFuse lookup");
    private static readonly Option<string> SlotOpt = new("--slot", () => "both", "Boot slot: a, b, or both");
    private static readonly Option<string?> ModelOpt = new("--model", "Samsung model for firmware sourcing (e.g., SM-F966U1)");
    private static readonly Option<string?> RegionOpt = new("--region", "CSC/region code for firmware sourcing (e.g., XAA)");
    private static readonly Option<bool> SkipTuiOpt = new("--skip-tui", "Skip interactive TUI for firmware sourcing");
    private static readonly Option<bool> DryRunOpt = new("--dry-run", () => false, "Run full diagnosis and detection without flashing any partitions");
    private static readonly Option<bool> ForceOpt = new("--force", () => false, "Override blocklist protection for device-unique partitions (requires typed confirmation)");
    private static readonly Option<bool> SkipDlModeCheckOpt = new("--skip-dl-mode-check", () => false, "Skip automatic Download Mode reboot (use if device is already in Download Mode)");

    static FirehoseCommands()
    {
        PartitionOpt.AddAlias("-p");
        OutputOpt.AddAlias("-o");
        InputOpt.AddAlias("-i");
        DeviceOpt.AddAlias("-d");
        LoaderOpt.AddAlias("-l");
        LunOpt.AddAlias("-u");
        ModeOpt.AddAlias("-m");
        BackupOpt.AddAlias("-b");
        PartitionsOpt.AddAlias("-P");
        SocOpt.AddAlias("-S");
        SlotOpt.AddAlias("-s");
        ModelOpt.AddAlias("-M");
        RegionOpt.AddAlias("-R");
    }

    public static Command CreateCommand()
    {
        var firehose = new Command("firehose", "Qualcomm EDL Firehose operations");
        firehose.AddCommand(CreateDetect());
        firehose.AddCommand(CreateInfo());
        firehose.AddCommand(CreatePrintGpt());
        firehose.AddCommand(CreateDump());
        firehose.AddCommand(CreateFlash());
        firehose.AddCommand(CreateErase());
        firehose.AddCommand(CreateReset());
        firehose.AddCommand(CreateNop());
        firehose.AddCommand(CreateStorageInfo());
        firehose.AddCommand(CreateRescueCommand());
        return firehose;
    }

    private static Command CreateDetect()
    {
        var cmd = new Command("detect", "List connected EDL devices");
        cmd.Handler = CommandHandler.Create(() =>
        {
            var devices = FirehoseDeviceDetector.EnumerateDevices();
            if (devices.Count == 0)
            {
                Console.WriteLine("No Qualcomm EDL devices detected.");
                Console.WriteLine("Ensure device is in EDL mode (VID=05C6, PID=9008/900E/901D).");
                return;
            }
            Console.WriteLine($"Found {devices.Count} EDL device(s):");
            foreach (var d in devices)
                Console.WriteLine($"  {d}");
        });
        return cmd;
    }

    private static Command CreateInfo()
    {
        var cmd = new Command("info", "Show chip info for a device") { DeviceOpt, LoaderOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var transport = await ConnectAsync(ctx);
            var sm = new SaharaStateMachine();
            var db = LoaderDatabase.FromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Loaders"));
            var detector = new LoaderDetector(transport, db);
            var result = await detector.DetectAsync();

            Console.WriteLine($"Device: {transport.DevicePath}");
            Console.WriteLine($"  Chip: {result.ChipInfo}");
            Console.WriteLine($"  Loader: {(result.Success ? result.Loader!.FilePath : "NONE")}");
            Console.WriteLine($"  Fused: {result.ChipInfo?.IsFused}");
            await transport.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreatePrintGpt()
    {
        var cmd = new Command("printgpt", "Dump GPT partition table") { DeviceOpt, LoaderOpt, LunOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var (client, _) = await InitClientAsync(ctx);
            var lun = ctx.ParseResult.GetValueForOption(LunOpt);
            var entries = await client.PrintGptAsync(lun);
            Console.WriteLine($"GPT Table (LUN {lun}):");
            foreach (var e in entries)
                Console.WriteLine($"  {e.Name,-24} start={e.StartSector,10} sectors={e.Sectors,10}");
            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateDump()
    {
        var cmd = new Command("dump", "Read a partition to file")
            { DeviceOpt, LoaderOpt, LunOpt, PartitionOpt, OutputOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var partition = ctx.ParseResult.GetValueForOption(PartitionOpt)!;
            var output = ctx.ParseResult.GetValueForOption(OutputOpt)!;
            var (client, _) = await InitClientAsync(ctx);
            var lun = ctx.ParseResult.GetValueForOption(LunOpt);
            Console.WriteLine($"Reading '{partition}' (LUN {lun})...");
            var data = await client.ReadPartitionAsync(partition, lun);
            await File.WriteAllBytesAsync(output, data);
            Console.WriteLine($"Wrote {data.Length:N0} bytes to {output}");
            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateFlash()
    {
        var cmd = new Command("flash", "Write a file to partition")
            { DeviceOpt, LoaderOpt, LunOpt, PartitionOpt, InputOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var partition = ctx.ParseResult.GetValueForOption(PartitionOpt)!;
            var input = ctx.ParseResult.GetValueForOption(InputOpt)!;
            if (!File.Exists(input)) { Console.WriteLine($"File not found: {input}"); return; }
            var (client, _) = await InitClientAsync(ctx);
            var lun = ctx.ParseResult.GetValueForOption(LunOpt);
            var data = await File.ReadAllBytesAsync(input);
            Console.WriteLine($"Writing {data.Length:N0} bytes to '{partition}' (LUN {lun})...");
            var ok = await client.WritePartitionAsync(partition, data, lun);
            Console.WriteLine(ok ? "Write successful." : "Write FAILED.");
            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateErase()
    {
        var cmd = new Command("erase", "Erase a partition")
            { DeviceOpt, LoaderOpt, LunOpt, PartitionOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var partition = ctx.ParseResult.GetValueForOption(PartitionOpt)!;
            var (client, _) = await InitClientAsync(ctx);
            var lun = ctx.ParseResult.GetValueForOption(LunOpt);
            Console.WriteLine($"Erasing '{partition}' (LUN {lun})...");
            var ok = await client.ErasePartitionAsync(partition, lun);
            Console.WriteLine(ok ? "Erase successful." : "Erase FAILED.");
            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateReset()
    {
        var cmd = new Command("reset", "Reboot device") { DeviceOpt, LoaderOpt, ModeOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var mode = ctx.ParseResult.GetValueForOption(ModeOpt) ?? "system";
            var (client, _) = await InitClientAsync(ctx);
            Console.WriteLine($"Resetting to '{mode}'...");
            await client.ResetAsync(mode);
        });
        return cmd;
    }

    private static Command CreateNop()
    {
        var cmd = new Command("nop", "Test if Firehose is alive") { DeviceOpt, LoaderOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var (client, _) = await InitClientAsync(ctx);
            var ok = await client.NopAsync();
            Console.WriteLine(ok ? "NOP: ACK — Firehose alive." : "NOP: NAK — not responding.");
            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateStorageInfo()
    {
        var cmd = new Command("storageinfo", "Query storage type") { DeviceOpt, LoaderOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var (client, _) = await InitClientAsync(ctx);
            var info = await client.GetStorageInfoAsync();
            Console.WriteLine($"Storage: {info}");
            await client.DisconnectAsync();
        });
        return cmd;
    }

    // ─── RESCUE COMMANDS ──────────────────────────────────────

    private static Command CreateRescueCommand()
    {
        var rescue = new Command("rescue", "Post-attack device recovery operations");
        rescue.AddCommand(CreateRescueDiagnose());
        rescue.AddCommand(CreateRescueRestore());
        rescue.AddCommand(CreateRescueFuses());
        rescue.AddCommand(CreateRescueUnmagisk());
        rescue.AddCommand(CreateRescueFull());
        return rescue;
    }

    private static Command CreateRescueDiagnose()
    {
        var cmd = new Command("diagnose", "Diagnose all security-critical partitions")
            { DeviceOpt, LoaderOpt, BackupOpt, OutputOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var (client, _) = await InitClientAsync(ctx);
            var backupDir = ctx.ParseResult.GetValueForOption(BackupOpt);
            var outputPath = ctx.ParseResult.GetValueForOption(OutputOpt);

            var report = new RescueReport();
            var diagnostics = new PartitionDiagnostics(client, report, backupDir);
            await diagnostics.RunAsync();

            var json = report.ToJson();
            if (!string.IsNullOrEmpty(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, json);
                Console.WriteLine($"Report saved to: {outputPath}");
            }
            else
            {
                Console.WriteLine(json);
            }
            report.PrintSummary();
            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateRescueRestore()
    {
        var cmd = new Command("restore", "Restore partitions from known-good backup")
            { DeviceOpt, LoaderOpt, BackupOpt, PartitionsOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var (client, _) = await InitClientAsync(ctx);
            var backupDir = ctx.ParseResult.GetValueForOption(BackupOpt)!;
            var partitionsStr = ctx.ParseResult.GetValueForOption(PartitionsOpt);

            var report = new RescueReport();
            var restorer = new PartitionRestorer(client, backupDir, report);

            List<string> partitions;
            if (!string.IsNullOrEmpty(partitionsStr))
                partitions = partitionsStr.Split(',').Select(p => p.Trim()).ToList();
            else
            {
                // Restore all tampered partitions from diagnosis
                var diagnostics = new PartitionDiagnostics(client, report, backupDir);
                await diagnostics.RunAsync();
                partitions = report.Partitions
                    .Where(p => p.Status == "Tampered")
                    .Select(p => p.PartitionName)
                    .ToList();
                Console.WriteLine($"Auto-selected tampered partitions: {string.Join(", ", partitions)}");
            }

            await restorer.RestoreAsync(partitions);
            report.PrintSummary();
            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateRescueFuses()
    {
        var cmd = new Command("fuses", "Audit QFuse status")
            { DeviceOpt, LoaderOpt, SocOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var (client, _) = await InitClientAsync(ctx);
            var socModel = ctx.ParseResult.GetValueForOption(SocOpt);

            var auditor = new QFuseAuditor(client, socModel);
            var result = await auditor.AuditAsync();

            Console.WriteLine($"\nQFuse Audit — SoC: {socModel ?? "auto-detected"}");
            Console.WriteLine($"Blown: {result.TotalBlown}/{result.TotalAvailable}");
            Console.WriteLine($"{"Fuse",-35} {"Addr",-10} {"Bit",-5} {"Status",-8} {"Implication"}");
            Console.WriteLine(new string('-', 90));
            foreach (var f in result.Fuses)
            {
                Console.WriteLine($"{f.FuseName,-35} 0x{f.Address:X8} {f.BitNumber,-5} {(f.IsBlown ? "BLOWN" : "ok"),-8} {f.Implication}");
            }

            if (result.PermanentDamageWarnings.Count > 0)
            {
                Console.WriteLine($"\nPERMANENT DAMAGE:");
                foreach (var w in result.PermanentDamageWarnings)
                    Console.WriteLine($"  * {w}");
            }

            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateRescueUnmagisk()
    {
        var cmd = new Command("unmagisk", "Surgically remove Magisk from boot images")
            { DeviceOpt, LoaderOpt, BackupOpt, SlotOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var (client, _) = await InitClientAsync(ctx);
            var backupDir = ctx.ParseResult.GetValueForOption(BackupOpt)!;
            var slot = ctx.ParseResult.GetValueForOption(SlotOpt) ?? "both";

            var report = new RescueReport();
            var remover = new MagiskRemover(client, backupDir, report);

            if (slot == "both")
                await remover.RemoveAllAsync();
            else
                await remover.RemoveFromSlotAsync(slot);

            report.PrintSummary();
            await client.DisconnectAsync();
        });
        return cmd;
    }

    private static Command CreateRescueFull()
    {
        var cmd = new Command("full", "Run complete rescue sequence (diagnose + fuses + restore + unmagisk)")
            { DeviceOpt, LoaderOpt, BackupOpt, ModelOpt, RegionOpt, SkipTuiOpt, DryRunOpt, ForceOpt, SkipDlModeCheckOpt };
        cmd.Handler = CommandHandler.Create(async (InvocationContext ctx) =>
        {
            var (client, _) = await InitClientAsync(ctx);
            var backupDir = ctx.ParseResult.GetValueForOption(BackupOpt);
            var model = ctx.ParseResult.GetValueForOption(ModelOpt);
            var region = ctx.ParseResult.GetValueForOption(RegionOpt);
            var skipTui = ctx.ParseResult.GetValueForOption(SkipTuiOpt);

            // Phase 0: Source firmware if no backup provided
            string? resolvedBackupDir = backupDir;
            if (string.IsNullOrEmpty(resolvedBackupDir))
            {
                // If model and region provided with skip-tui, source directly
                if (!string.IsNullOrEmpty(model) && !string.IsNullOrEmpty(region) && skipTui)
                {
                    Console.WriteLine($"[0/7] Sourcing firmware for {model}/{region}...");
                    var sourcer = new BACKRabbit.Firmware.FirmwareSourcer();
                    var outputDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "BACKRabbit", "Firmware", $"{model}_{region}_{DateTime.Now:yyyyMMdd_HHmmss}");
                    var result = await sourcer.SourceAsync(model, region, null, outputDir);
                    if (result.Success)
                    {
                        resolvedBackupDir = result.FirmwarePath;
                        Console.WriteLine($"  Firmware sourced: {resolvedBackupDir}");
                    }
                }
                else if (!skipTui)
                {
                    // Launch interactive TUI
                    Console.WriteLine("[0/7] Launching firmware sourcing TUI...");
                    var tui = new BACKRabbit.CLI.TUI.FirmwareTui();
                    var tuiResult = await tui.RunAsync();
                    if (tuiResult?.Success == true)
                    {
                        resolvedBackupDir = tuiResult.FirmwarePath;
                        Console.WriteLine($"  Firmware sourced: {resolvedBackupDir}");
                    }
                }
            }

            if (string.IsNullOrEmpty(resolvedBackupDir))
            {
                Console.WriteLine("⚠️ No firmware backup available. Rescue will diagnose only.");
                Console.WriteLine("   Provide --backup, --model/--region, or use 'firmware source' first.");
            }

            var dryRun = ctx.ParseResult.GetValueForOption(DryRunOpt);
            var force = ctx.ParseResult.GetValueForOption(ForceOpt);
            var skipDlModeCheck = ctx.ParseResult.GetValueForOption(SkipDlModeCheckOpt);

            // --force requires explicit typed confirmation
            if (force && !dryRun)
            {
                Console.WriteLine("\n⚠️  --force WILL override the blocklist and allow flashing of:");
                Console.WriteLine("   sec, ddr, limits, apdp, msadp — device-unique partitions.");
                Console.WriteLine("   Flashing these partitions can cause PERMANENT DAMAGE.");
                Console.Write("\n   Type \"I understand the risks\" to proceed: ");
                var confirmation = Console.ReadLine();
                if (confirmation != "I understand the risks")
                {
                    Console.WriteLine("   Confirmation failed. Aborting rescue.");
                    return;
                }
                Console.WriteLine("   Confirmation accepted. Proceeding with force override.\n");
            }

            var orchestrator = new RescueOrchestrator(client, resolvedBackupDir ?? "", dryRun, force, skipDlModeCheck);
            await orchestrator.RunFullRescueAsync();
            // Device resets at end of full rescue — no disconnect needed
        });
        return cmd;
    }

    // ─── HELPERS ──────────────────────────────────────────────

    private static async Task<WinUsbTransport> ConnectAsync(InvocationContext ctx)
    {
        var devicePath = ctx.ParseResult.GetValueForOption(DeviceOpt);
        if (!string.IsNullOrEmpty(devicePath))
        {
            var t = new WinUsbTransport(devicePath);
            await t.ConnectAsync();
            return t;
        }

        var devices = FirehoseDeviceDetector.EnumerateDevices();
        if (devices.Count == 0)
            throw new InvalidOperationException("No EDL devices found.");

        var dev = devices[0];
        Console.WriteLine($"Auto-detected: {dev}");
        var transport = new WinUsbTransport(dev.VendorId, dev.ProductId);
        await transport.ConnectAsync();
        return transport;
    }

    private static async Task<(FirehoseClient client, string loaderPath)> InitClientAsync(InvocationContext ctx)
    {
        var transport = await ConnectAsync(ctx);
        var loaderPath = ctx.ParseResult.GetValueForOption(LoaderOpt)
                      ?? AutoDetectLoader();
        if (loaderPath == null)
            throw new InvalidOperationException(
                "No loader. Use --loader <path> or place .elf/.mbn in ./Loaders/");

        var sm = new SaharaStateMachine();
        var client = new FirehoseClient(transport, sm);
        await client.InitializeAsync(loaderPath);
        return (client, loaderPath);
    }

    private static string? AutoDetectLoader()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Loaders");
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir, "*.elf")
            .Concat(Directory.GetFiles(dir, "*.mbn"))
            .Concat(Directory.GetFiles(dir, "*.bin"))
            .ToList();
        if (files.Count > 0)
        {
            Console.WriteLine($"Auto-selected loader: {Path.GetFileName(files[0])}");
            return files[0];
        }
        return null;
    }
}
