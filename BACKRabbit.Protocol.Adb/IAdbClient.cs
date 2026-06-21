namespace BACKRabbit.Protocol.Adb;

/// <summary>
/// Interface for ADB client operations.
/// Implemented by AdbClient (real device) and MockAdbClient (test mode).
/// Enables testing the GUI without a physical device.
/// </summary>
public interface IAdbClient : IDisposable
{
    bool IsConnected { get; }
    string? Serial { get; }
    string? DeviceModel { get; }
    string? AndroidVersion { get; }

    event EventHandler<string>? LogMessage;
    event EventHandler? DeviceStateChanged;

    Task<bool> ConnectTcpAsync(string host, int port = 5555, CancellationToken ct = default);
    Task<bool> ConnectUsbAsync(Usb.UsbDeviceManager usb, CancellationToken ct = default);
    Task<MagiskStatus> CheckMagiskStatusAsync(CancellationToken ct = default);
    Task<string> ExecuteShellAsync(string command, CancellationToken ct = default);
    Task<bool> PullFileAsync(string remote, string local, CancellationToken ct = default);
    Task<bool> PushFileAsync(string local, string remote, CancellationToken ct = default);
    Task<bool> RebootAsync(string mode = "", CancellationToken ct = default);
    Task<bool> RebootBootloaderAsync(CancellationToken ct = default);
    Task<bool> RebootRecoveryAsync(CancellationToken ct = default);
    
    /// <summary>Gets all device properties via getprop</summary>
    Task<Dictionary<string, string>> GetPropertiesAsync(CancellationToken ct = default);
    
    /// <summary>Checks bootloader lock state (ro.boot.other.locked, ro.boot.flashing.locked, ro.boot.verifiedbootstate)</summary>
    Task<BootloaderLockStatus> CheckBootloaderLockStatusAsync(CancellationToken ct = default);
    
    /// <summary>Execute shell command as root. Tries direct first, falls back to su -c.</summary>
    Task<string> ExecuteRootShellAsync(string command, CancellationToken ct = default);
    
    /// <summary>Wait for device ADB to become available after reboot. Polls until device responds or timeout.</summary>
    Task<bool> WaitForDeviceAsync(int timeoutMs = 60000, CancellationToken ct = default);
}

/// <summary>
/// Bootloader lock state result
/// </summary>
public class BootloaderLockStatus
{
    public bool IsLocked { get; set; }
    public bool IsUnlocked { get; set; }
    public string? LockProperty { get; set; }
    public string? VerifiedBootState { get; set; }
    public Dictionary<string, string>? RawProperties { get; set; }
}
