using System;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BACKRabbit.Protocol.Firehose;

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

    static FirehoseCommands()
    {
        PartitionOpt.AddAlias("-p");
        OutputOpt.AddAlias("-o");
        InputOpt.AddAlias("-i");
        DeviceOpt.AddAlias("-d");
        LoaderOpt.AddAlias("-l");
        LunOpt.AddAlias("-u");
        ModeOpt.AddAlias("-m");
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
