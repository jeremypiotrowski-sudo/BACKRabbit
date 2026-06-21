namespace BACKRabbit.CLI.TUI;

/// <summary>
/// Device information detected via USB/Firehose for firmware sourcing.
/// </summary>
public class DeviceInfo
{
    public string? Model { get; set; }
    public string? Region { get; set; }
    public string? SerialNumber { get; set; }
    public string? Imei { get; set; }
    public bool IsDetected => Model != null;

    public override string ToString()
    {
        return $"Model={Model ?? "<not detected>"}, Region={Region ?? "<not detected>"}, " +
               $"Serial={SerialNumber ?? "<not detected>"}, IMEI={Imei ?? "<not detected>"}";
    }
}