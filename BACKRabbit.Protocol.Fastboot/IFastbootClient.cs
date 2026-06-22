namespace BACKRabbit.Protocol.Fastboot;

/// <summary>
/// Abstraction over FastbootClient for testability.
/// </summary>
public interface IFastbootClient : IDisposable
{
    bool IsConnected { get; }
    string? Serial { get; }
    string? Product { get; }
    string? CurrentSlot { get; }

    event EventHandler<string>? LogMessage;

    Task<bool> ConnectAsync(Usb.UsbDeviceManager usb, CancellationToken ct = default);
    Task<bool> FlashAsync(string partition, byte[] data, CancellationToken ct = default);
    Task<bool> RebootAsync(CancellationToken ct = default);
    Task<bool> RebootBootloaderAsync(CancellationToken ct = default);
}