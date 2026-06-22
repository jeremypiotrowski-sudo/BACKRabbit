using BACKRabbit.Protocol.Fastboot;
using BACKRabbit.Usb;

namespace BACKRabbit.CLI.Testing;

/// <summary>
/// Mock Fastboot client for testing restore-stock without a physical device.
/// </summary>
public class MockFastbootClient : IFastbootClient
{
    private readonly Dictionary<string, byte[]> _expectedImages = new();
    private readonly List<(string Partition, int Length)> _flashedPartitions = new();

    public bool IsConnected { get; private set; }
    public string? Serial { get; private set; }
    public string? Product { get; private set; }
    public string? CurrentSlot { get; set; }

    public event EventHandler<string>? LogMessage;

    public bool ShouldConnect { get; set; } = true;
    public bool ShouldFlashSucceed { get; set; } = true;
    public bool ShouldRebootSucceed { get; set; } = true;

    public void SetExpectedImage(string partition, byte[] data)
    {
        _expectedImages[partition] = data;
    }

    public IReadOnlyList<(string Partition, int Length)> FlashedPartitions => _flashedPartitions;

    public async Task<bool> ConnectAsync(UsbDeviceManager usb, CancellationToken ct = default)
    {
        await Task.Yield();
        if (ShouldConnect)
        {
            IsConnected = true;
            Serial = "MOCK-FASTBOOT";
            Product = "SM-T307U";
            CurrentSlot ??= "_a";
        }
        Log($"Mock: fastboot connect {(IsConnected ? "OK" : "FAIL")}");
        return ShouldConnect;
    }

    public async Task<bool> FlashAsync(string partition, byte[] data, CancellationToken ct = default)
    {
        await Task.Yield();
        Log($"Mock: fastboot flash {partition} ({data.Length} bytes)");
        if (!ShouldFlashSucceed) return false;
        _flashedPartitions.Add((partition, data.Length));
        return true;
    }

    public async Task<bool> RebootAsync(CancellationToken ct = default)
    {
        await Task.Yield();
        Log("Mock: fastboot reboot");
        IsConnected = false;
        return ShouldRebootSucceed;
    }

    public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
    {
        await Task.Yield();
        Log("Mock: fastboot reboot-bootloader");
        IsConnected = false;
        return ShouldRebootSucceed;
    }

    public void Dispose()
    {
        IsConnected = false;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, message);
    }
}