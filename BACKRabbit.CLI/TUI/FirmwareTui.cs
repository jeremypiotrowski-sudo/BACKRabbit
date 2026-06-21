using BACKRabbit.Firmware;
using Spectre.Console;

namespace BACKRabbit.CLI.TUI;

/// <summary>
/// Interactive Text-User-Interface for sourcing genuine Samsung firmware
/// before rescue operations. Launched when user has not supplied --backup-dir.
/// </summary>
public class FirmwareTui
{
    private readonly FirmwareSourcer _sourcer;
    private string? _imei;

    public FirmwareTui(FirmwareSourcer? sourcer = null)
    {
        _sourcer = sourcer ?? new FirmwareSourcer();
    }

    /// <summary>
    /// Run the full interactive firmware sourcing flow.
    /// Returns the output directory path on success, null if user skipped.
    /// </summary>
    public async Task<FirmwareSourceResult?> RunAsync(CancellationToken ct = default)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("BACKRabbit").Color(Color.Green));
        AnsiConsole.Write(new Rule("[green]Firmware Sourcing[/]").RuleStyle(Style.Parse("green")));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]This wizard will download genuine Samsung firmware[/]");
        AnsiConsole.MarkupLine("[grey]from Samsung's servers to use as known-good backup.[/]");
        AnsiConsole.WriteLine();

        // Step 1: Detect device
        var deviceInfo = await DetectDeviceAsync(ct);

        // Step 2: Show detected info and get user choice
        var (model, region) = await ShowDeviceSummaryAsync(deviceInfo, ct);

        if (model == null || region == null)
        {
            AnsiConsole.MarkupLine("[yellow]Firmware sourcing skipped. You will need to provide your own backup.[/]");
            return null;
        }

        // Step 3: Confirm and download
        var confirmed = await ConfirmDownloadAsync(model, region);
        if (!confirmed)
        {
            AnsiConsole.MarkupLine("[yellow]Download cancelled.[/]");
            return null;
        }

        // Step 4: Download with progress
        var outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BACKRabbit", "Firmware", $"{model}_{region}_{DateTime.Now:yyyyMMdd_HHmmss}");

        FirmwareSourceResult result;
        try
        {
            result = await ProgressRenderer.RunWithProgressAsync(
                $"Sourcing {model}/{region}",
                async renderer =>
                {
                    return await _sourcer.SourceAsync(model, region, _imei, outputDir, renderer, ct);
                });
        }
        catch (FirmwareSourceException ex)
        {
            AnsiConsole.MarkupLine($"[red]Firmware sourcing failed: {ex.Message}[/]");
            return await HandleFailureAsync(model, region, outputDir, ct);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Download cancelled by user.[/]");
            return null;
        }

        // Step 5: Show results
        ShowResultScreen(result);

        return result;
    }

    /// <summary>
    /// Try to detect device model and region via USB/Firehose.
    /// </summary>
    private async Task<DeviceInfo> DetectDeviceAsync(CancellationToken ct)
    {
        var info = new DeviceInfo();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("[grey]Detecting connected device...[/]", async _ =>
            {
                try
                {
                    // Try Firehose device detection first
                    var devices = BACKRabbit.Protocol.Firehose.FirehoseDeviceDetector.EnumerateDevices();
                    if (devices.Count > 0)
                    {
                        var dev = devices[0];
                        info.SerialNumber = dev.DevicePath;

                        // Try to get chip info via Sahara
                        try
                        {
                            var transport = new BACKRabbit.Protocol.Firehose.WinUsbTransport(
                                dev.VendorId, dev.ProductId);
                            await transport.ConnectAsync();
                            var sm = new BACKRabbit.Protocol.Firehose.SaharaStateMachine();
                            var client = new BACKRabbit.Protocol.Firehose.FirehoseClient(transport, sm);

                            // Read HELLO_REQ to get chip info
                            var helloReq = await transport.ReceivePacketAsync(ct);
                            if (helloReq.Command == (uint)BACKRabbit.Protocol.Firehose.SaharaCommand.HelloReq)
                            {
                                var chipInfo = BACKRabbit.Protocol.Firehose.SaharaChipInfo.FromHelloRequest(helloReq);
                                info.SerialNumber ??= chipInfo.SerialNumber;

                                // Map MSM ID to known model (best-effort)
                                info.Model = MapMsmToModel(chipInfo.MsmId);
                            }

                            await transport.DisconnectAsync();
                        }
                        catch
                        {
                            // Firehose detection failed — non-critical
                        }
                    }

                    // Fallback: try USB device enumeration for Samsung devices
                    if (info.Model == null)
                    {
                        try
                        {
                            using var usb = new BACKRabbit.Usb.UsbDeviceManager();
                            var samsungDevices = usb.EnumerateSamsungDevices();
                            if (samsungDevices.Count > 0)
                            {
                                var sd = samsungDevices[0];
                                info.SerialNumber ??= sd.SerialNumber;
                            }
                        }
                        catch
                        {
                            // USB detection failed
                        }
                    }
                }
                catch
                {
                    // All detection methods failed
                }

                await Task.Delay(300, ct);
            });

        return info;
    }

    /// <summary>
    /// Show detected device info and prompt user for action.
    /// Returns (model, region) or (null, null) if skipped.
    /// </summary>
    private async Task<(string? model, string? region)> ShowDeviceSummaryAsync(
        DeviceInfo info, CancellationToken ct)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Property[/]").Centered())
            .AddColumn(new TableColumn("[bold]Value[/]"));

        table.AddRow("Model", info.Model ?? "[grey]<not detected>[/]");
        table.AddRow("Region/CSC", info.Region ?? "[grey]<not detected>[/]");
        table.AddRow("Serial", info.SerialNumber ?? "[grey]<not detected>[/]");
        table.AddRow("IMEI", info.Imei ?? "[grey]<not detected> (optional, helps FUS auth)[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]What would you like to do?[/]")
                .AddChoices(new[]
                {
                    "[C] Confirm and continue",
                    "[E] Edit model/region/IMEI",
                    "[S] Skip firmware sourcing",
                    "[Q] Quit",
                }));

        switch (choice[1])
        {
            case 'C':
                if (info.Model == null)
                {
                    AnsiConsole.MarkupLine("[yellow]Model not detected. Switching to edit mode.[/]");
                    return await EditDeviceInfoAsync(info, ct);
                }
                var region = info.Region ?? "XAA";
                _imei = info.Imei;
                return (info.Model, region);

            case 'E':
                return await EditDeviceInfoAsync(info, ct);

            case 'S':
                return (null, null);

            case 'Q':
                Environment.Exit(0);
                return (null, null);

            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Let user manually enter model, region, and IMEI.
    /// </summary>
    private async Task<(string? model, string? region)> EditDeviceInfoAsync(
        DeviceInfo info, CancellationToken ct)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow]Edit Device Information[/]").RuleStyle(Style.Parse("yellow")));
        AnsiConsole.WriteLine();

        var model = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Model (e.g., SM-F966U1):[/]")
                .DefaultValue(info.Model ?? "")
                .Validate(m =>
                {
                    if (string.IsNullOrWhiteSpace(m))
                        return ValidationResult.Error("Model is required");
                    if (!m.StartsWith("SM-", StringComparison.OrdinalIgnoreCase))
                        return ValidationResult.Error("Model should start with 'SM-' (e.g., SM-F966U1)");
                    return ValidationResult.Success();
                }));

        var region = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Region/CSC (e.g., XAA, EUX, KOO):[/]")
                .DefaultValue(info.Region ?? "XAA")
                .Validate(r =>
                {
                    if (string.IsNullOrWhiteSpace(r))
                        return ValidationResult.Error("Region is required");
                    if (r.Length != 3)
                        return ValidationResult.Error("Region/CSC must be 3 characters (e.g., XAA, EUX)");
                    return ValidationResult.Success();
                }));

        var imei = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]IMEI (optional, 15 digits — helps FUS auth):[/]")
                .DefaultValue(info.Imei ?? "")
                .AllowEmpty()
                .Validate(i =>
                {
                    if (!string.IsNullOrEmpty(i) && (i.Length != 15 || !i.All(char.IsDigit)))
                        return ValidationResult.Error("IMEI must be exactly 15 digits");
                    return ValidationResult.Success();
                }));

        _imei = string.IsNullOrWhiteSpace(imei) ? null : imei;
        info.Imei = _imei;

        return (model.ToUpperInvariant(), region.ToUpperInvariant());
    }

    /// <summary>
    /// Confirm before starting download.
    /// </summary>
    private async Task<bool> ConfirmDownloadAsync(string model, string region)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[green]Confirm Download[/]").RuleStyle(Style.Parse("green")));
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[bold]Model:[/] [green]{model}[/]");
        AnsiConsole.MarkupLine($"[bold]Region/CSC:[/] [green]{region}[/]");
        if (!string.IsNullOrEmpty(_imei))
            AnsiConsole.MarkupLine($"[bold]IMEI:[/] [green]{_imei}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Firmware will be downloaded from Samsung FUS servers.[/]");
        AnsiConsole.MarkupLine("[grey]This may take 10-30 minutes depending on your connection.[/]");
        AnsiConsole.WriteLine();

        return AnsiConsole.Prompt(
            new ConfirmationPrompt("[bold]Start firmware download?[/]")
            {
                DefaultValue = true
            });
    }

    /// <summary>
    /// Show results after successful download.
    /// </summary>
    private void ShowResultScreen(FirmwareSourceResult result)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[green]Download Complete[/]").RuleStyle(Style.Parse("green")));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Detail[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("Model", $"[green]{result.Model}[/]");
        table.AddRow("Region", $"[green]{result.Region}[/]");
        table.AddRow("Version", result.Version);
        table.AddRow("Build Date", result.BuildDate);
        table.AddRow("Size", FormatBytes(result.FirmwareSize));
        table.AddRow("Output Path", $"[green]{result.FirmwarePath}[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (result.ExtractedPartitions.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]Extracted Partitions:[/]");
            foreach (var part in result.ExtractedPartitions.OrderBy(p => p))
            {
                var imgPath = Path.Combine(result.FirmwarePath, $"{part}.img");
                var size = File.Exists(imgPath) ? new FileInfo(imgPath).Length : 0;
                AnsiConsole.MarkupLine($"  [green]✓[/] {part,-20} ({FormatBytes(size)})");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green bold]Firmware ready for rescue operations.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to continue to rescue...[/]");
        Console.ReadKey(true);
    }

    /// <summary>
    /// Handle download failure — offer retry/edit/abort.
    /// </summary>
    private async Task<FirmwareSourceResult?> HandleFailureAsync(
        string model, string region, string outputDir, CancellationToken ct)
    {
        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold red]Download failed. What would you like to do?[/]")
                .AddChoices(new[]
                {
                    "[R] Retry with same model/region",
                    "[E] Edit model/region/IMEI and retry",
                    "[A] Abort (skip firmware sourcing)",
                }));

        switch (choice[1])
        {
            case 'R':
                try
                {
                    var result = await ProgressRenderer.RunWithProgressAsync(
                        $"Retrying {model}/{region}",
                        async renderer =>
                        {
                            return await _sourcer.SourceAsync(model, region, _imei, outputDir, renderer, ct);
                        });
                    ShowResultScreen(result);
                    return result;
                }
                catch (FirmwareSourceException ex)
                {
                    AnsiConsole.MarkupLine($"[red]Retry also failed: {ex.Message}[/]");
                    return await HandleFailureAsync(model, region, outputDir, ct);
                }

            case 'E':
                var info = new DeviceInfo { Model = model, Region = region, Imei = _imei };
                var (newModel, newRegion) = await EditDeviceInfoAsync(info, ct);
                if (newModel == null || newRegion == null) return null;
                return await HandleFailureAsync(newModel, newRegion, outputDir, ct);

            case 'A':
            default:
                return null;
        }
    }

    /// <summary>
    /// Map Qualcomm MSM ID to Samsung model (best-effort).
    /// </summary>
    private static string? MapMsmToModel(uint msmId)
    {
        return msmId switch
        {
            0x008600E1 => "SM-G960U",   // SDM845 — Galaxy S9
            0x008700E1 => "SM-G991U",   // SM8550 — Galaxy S21
            0x008800E1 => "SM-S928U",   // SM8650 — Galaxy S24
            0x007000E1 => "SM-G950U",   // MSM8998 — Galaxy S8
            0x006900E1 => "SM-J700F",   // MSM8937 — Galaxy J7
            _ => null,
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F1} GB";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F1} MB";
        if (bytes >= 1_000) return $"{bytes / 1_000.0:F1} KB";
        return $"{bytes} B";
    }
}