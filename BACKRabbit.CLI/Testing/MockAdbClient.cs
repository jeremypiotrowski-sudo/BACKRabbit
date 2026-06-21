using BACKRabbit.Protocol.Adb;

namespace BACKRabbit.CLI.Testing;

/// <summary>
/// Mock ADB client for testing the CLI wizard without a physical device.
/// Implements IAdbClient with canned responses.
/// Activated via --test-mode flag on 'magisk wizard' command.
/// </summary>
public class MockAdbClient : IAdbClient
{
    private readonly int _simulatedLatencyMs;
    private bool _mockConnected;
    private bool _mockMagiskInstalled = true;

    public bool IsConnected => _mockConnected;
    public string? Serial { get; private set; }
    public string? DeviceModel { get; private set; }
    public string? AndroidVersion { get; private set; }

    public event EventHandler<string>? LogMessage;
    public event EventHandler? DeviceStateChanged;

    public MockAdbClient(int simulatedLatencyMs = 200)
    {
        _simulatedLatencyMs = simulatedLatencyMs;
        _mockConnected = true;
        DeviceModel = "SM-S928U1 (Simulated)";
        Serial = "MOCK-R58M94XQ7BV";
        AndroidVersion = "15";
    }

    private async Task SimulateLatencyAsync()
    {
        await Task.Delay(_simulatedLatencyMs);
    }

    public async Task<bool> ConnectTcpAsync(string host, int port = 5555, CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        _mockConnected = true;
        Serial = $"{host}:{port}";
        EmitLog($"Mock: Connected to {host}:{port}");
        EmitStateChanged();
        return true;
    }

    public async Task<bool> ConnectUsbAsync(Usb.UsbDeviceManager usb, CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        _mockConnected = true;
        Serial = "MOCK-R58M94XQ7BV";
        EmitLog("Mock: Connected via USB");
        EmitStateChanged();
        return true;
    }

    public async Task<MagiskStatus> CheckMagiskStatusAsync(CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog("Mock: adb shell magisk -c");
        return new MagiskStatus
        {
            IsInstalled = _mockMagiskInstalled,
            Version = _mockMagiskInstalled ? "28.1" : "",
            ModuleCount = _mockMagiskInstalled ? 3 : 0
        };
    }

    public async Task<string> ExecuteShellAsync(string command, CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog($"Mock: adb shell {command}");

        return command switch
        {
            "magisk -c" => _mockMagiskInstalled ? "28.1:MAGISKSU" : "",
            "ls /sdcard" => "Download\nDCIM\nPictures\nMusic\nboot.img\ninit_boot.img",
            "ls /data/adb" => _mockMagiskInstalled ? "magisk\nmodules\nmagisk.db" : "ls: /data/adb: No such file or directory",
            "su -c whoami" => _mockMagiskInstalled ? "root" : "Permission denied",
            _ => $"Mock output for: {command}"
        };
    }

    public async Task<bool> PullFileAsync(string remote, string local, CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog($"Mock: adb pull {remote} → {local}");

        var dir = Path.GetDirectoryName(local);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Write mock data (simulates a boot image with ANDROID! magic)
        var mockData = new byte[1024 * 1024 * 32]; // 32MB mock boot image
        new Random(42).NextBytes(mockData);
        System.Text.Encoding.ASCII.GetBytes("ANDROID!").CopyTo(mockData, 0);
        await File.WriteAllBytesAsync(local, mockData);

        return true;
    }

    public async Task<bool> PushFileAsync(string local, string remote, CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog($"Mock: adb push {local} → {remote}");
        return true;
    }

    public async Task<bool> RebootAsync(string mode = "", CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog($"Mock: adb reboot {mode}");
        _mockConnected = false;
        EmitStateChanged();
        return true;
    }

    public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog("Mock: adb reboot bootloader");
        _mockConnected = false;
        EmitStateChanged();
        return true;
    }

    public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog("Mock: adb reboot recovery");
        _mockConnected = false;
        EmitStateChanged();
        return true;
    }

    /// <summary>
    /// Toggle mock Magisk state for testing detection scenarios
    /// </summary>
    public void SetMockMagiskInstalled(bool installed)
    {
        _mockMagiskInstalled = installed;
    }

    /// <summary>
    /// Simulate disconnection for testing error recovery
    /// </summary>
    public void SimulateDisconnect()
    {
        _mockConnected = false;
        EmitStateChanged();
    }

    /// <summary>
    /// Simulate reconnection
    /// </summary>
    public void SimulateReconnect()
    {
        _mockConnected = true;
        EmitStateChanged();
    }

    public async Task<Dictionary<string, string>> GetPropertiesAsync(CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog("Mock: adb shell getprop");
        return new Dictionary<string, string>
        {
            ["ro.product.model"] = "SM-S928U1",
            ["ro.build.version.release"] = "15",
            ["ro.boot.other.locked"] = _mockBootloaderLocked ? "1" : "0",
            ["ro.boot.verifiedbootstate"] = _mockBootloaderLocked ? "green" : "orange",
            ["ro.boot.warranty_bit"] = _mockKnoxTripped ? "1" : "0",
            ["ro.boot.flashing.locked"] = _mockBootloaderLocked ? "1" : "0"
        };
    }

    public async Task<BootloaderLockStatus> CheckBootloaderLockStatusAsync(CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog($"Mock: Checking bootloader lock state (locked={_mockBootloaderLocked})");
        return new BootloaderLockStatus
        {
            IsLocked = _mockBootloaderLocked,
            IsUnlocked = !_mockBootloaderLocked,
            LockProperty = $"ro.boot.other.locked={(_mockBootloaderLocked ? "1" : "0")}",
            VerifiedBootState = _mockBootloaderLocked ? "green" : "orange",
            RawProperties = await GetPropertiesAsync(ct)
        };
    }

    /// <summary>Configurable bootloader lock state for testing branched workflow</summary>
    private bool _mockBootloaderLocked;
    public void SetBootloaderLocked(bool locked) => _mockBootloaderLocked = locked;
    public bool IsMockBootloaderLocked => _mockBootloaderLocked;

    /// <summary>Configurable Knox warranty bit for testing Knox messaging</summary>
    private bool _mockKnoxTripped = true;
    public void SetKnoxTripped(bool tripped) => _mockKnoxTripped = tripped;
    public bool IsMockKnoxTripped => _mockKnoxTripped;

    public async Task<string> ExecuteRootShellAsync(string command, CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog($"Mock: ExecuteRootShell: {command}");
        // Simulate root access — return success for rm -rf commands
        if (command.Contains("rm -rf"))
            return "";
        if (command.Contains("Permission denied"))
            return await ExecuteShellAsync($"su -c '{command}'", ct);
        return await ExecuteShellAsync(command, ct);
    }

    public async Task<bool> WaitForDeviceAsync(int timeoutMs = 60000, CancellationToken ct = default)
    {
        await SimulateLatencyAsync();
        EmitLog($"Mock: WaitForDevice (timeout={timeoutMs}ms) — simulated ready");
        return true;
    }

    public void Dispose()
    {
        _mockConnected = false;
        EmitLog("Mock: Disconnected");
        EmitStateChanged();
    }

    private void EmitLog(string message)
    {
        LogMessage?.Invoke(this, message);
    }

    private void EmitStateChanged()
    {
        DeviceStateChanged?.Invoke(this, EventArgs.Empty);
    }
}