namespace BACKRabbit.Protocol.Firehose;

/// <summary>
/// WP-2: Unified device detection interface for TUI firmware sourcing.
/// Abstracts Firehose/USB detection behind a testable interface.
/// </summary>
public interface IDeviceDetector
{
    /// <summary>
    /// Detect connected device and return model, region, serial, IMEI.
    /// Returns null Model when no device is connected.
    /// </summary>
    Task<DeviceDetectionResult> DetectAsync(CancellationToken ct);
}

/// <summary>
/// Result of device detection for firmware sourcing.
/// </summary>
public class DeviceDetectionResult
{
    public string? Model { get; set; }
    public string? Region { get; set; }
    public string? SerialNumber { get; set; }
    public string? Imei { get; set; }
    public uint MsmId { get; set; }
    public bool IsFused { get; set; }
    public bool IsDetected => Model != null;

    public override string ToString() =>
        $"Model={Model ?? "<not detected>"}, Region={Region ?? "<not detected>"}, " +
        $"Serial={SerialNumber ?? "<not detected>"}, IMEI={Imei ?? "<not detected>"}";
}