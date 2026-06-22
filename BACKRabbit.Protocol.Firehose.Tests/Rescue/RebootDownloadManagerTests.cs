using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BACKRabbit.Protocol.Adb;
using BACKRabbit.Protocol.Firehose.Rescue;
using BACKRabbit.Usb;
using Xunit;

namespace BACKRabbit.Protocol.Firehose.Tests.Rescue;

public class RebootDownloadManagerTests
{
    [Fact]
    public void IsDeviceInDownloadMode_NoDevices_ReturnsFalse()
    {
        // When no Samsung devices are connected, should return false gracefully
        var result = RebootDownloadManager.TryRebootToDownloadMode(
            adbClient: null,
            skipInteractive: true);

        Assert.False(result);
    }

    [Fact]
    public async Task TryRebootToDownloadModeAsync_SkipInteractive_NoAdb_ReturnsFalse()
    {
        // With skipInteractive=true and no ADB, should return false immediately
        // without blocking on Console.ReadLine
        var result = await RebootDownloadManager.TryRebootToDownloadModeAsync(
            adbClient: null,
            skipInteractive: true,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryRebootToDownloadModeAsync_SkipInteractive_DisconnectedAdb_ReturnsFalse()
    {
        // With a disconnected ADB client and skipInteractive, should return false
        // (no TCP fallback — only pre-connected ADB clients are used)
        var mockAdb = new MockDisconnectedAdbClient();
        var result = await RebootDownloadManager.TryRebootToDownloadModeAsync(
            adbClient: mockAdb,
            skipInteractive: true,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryRebootToDownloadModeAsync_Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RebootDownloadManager.TryRebootToDownloadModeAsync(
                adbClient: null,
                skipInteractive: true,
                ct: cts.Token));
    }

    [Fact]
    public void SynchronousWrapper_ReturnsSameAsAsync()
    {
        // The sync wrapper should not throw and should return a boolean
        var result = RebootDownloadManager.TryRebootToDownloadMode(
            adbClient: null,
            skipInteractive: true);

        Assert.IsType<bool>(result);
    }

    [Fact]
    public void DownloadModeTimeout_Is90Seconds()
    {
        // Verify the timeout was increased for Z Fold 7 slow USB enumeration
        var timeoutField = typeof(RebootDownloadManager)
            .GetField("DOWNLOAD_MODE_TIMEOUT_MS", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(timeoutField);
        var timeoutValue = timeoutField!.GetValue(null);
        Assert.Equal(90_000, timeoutValue);
    }

    [Fact]
    public void MaxButtonAttempts_Is3()
    {
        // Verify retry count for button path
        var attemptsField = typeof(RebootDownloadManager)
            .GetField("MAX_BUTTON_ATTEMPTS", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(attemptsField);
        var attemptsValue = attemptsField!.GetValue(null);
        Assert.Equal(3, attemptsValue);
    }
}

/// <summary>
/// Mock ADB client that reports as disconnected.
/// Used to test the "ADB unavailable → fallback" path.
/// </summary>
internal class MockDisconnectedAdbClient : IAdbClient
{
    public bool IsConnected => false;
    public string? Serial => null;
    public string? DeviceModel => null;
    public string? AndroidVersion => null;

    public event EventHandler<string>? LogMessage;
    public event EventHandler? DeviceStateChanged;

    public Task<bool> ConnectTcpAsync(string host, int port = 5555, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> ConnectUsbAsync(UsbDeviceManager usb, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<MagiskStatus> CheckMagiskStatusAsync(CancellationToken ct = default)
        => Task.FromResult(new MagiskStatus { IsInstalled = false });

    public Task<string> ExecuteShellAsync(string command, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task<bool> PullFileAsync(string remote, string local, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> PushFileAsync(string local, string remote, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> RebootAsync(string mode = "", CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<Dictionary<string, string>> GetPropertiesAsync(CancellationToken ct = default)
        => Task.FromResult(new Dictionary<string, string>());

    public Task<BootloaderLockStatus> CheckBootloaderLockStatusAsync(CancellationToken ct = default)
        => Task.FromResult(new BootloaderLockStatus { IsLocked = true });

    public Task<string> ExecuteRootShellAsync(string command, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task<bool> WaitForDeviceAsync(int timeoutMs = 60000, CancellationToken ct = default)
        => Task.FromResult(false);

    public void Dispose() { }
}