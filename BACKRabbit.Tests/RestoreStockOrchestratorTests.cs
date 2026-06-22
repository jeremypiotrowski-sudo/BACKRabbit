using System.Security.Cryptography;
using System.Text;
using BACKRabbit.CLI.Magisk;
using BACKRabbit.CLI.Testing;
using BACKRabbit.MagiskCore.RamdiskEditor;
using BACKRabbit.Protocol.Adb;
using BACKRabbit.Protocol.Fastboot;
using BACKRabbit.Usb;

namespace BACKRabbit.Tests;

/// <summary>
/// Unit tests for the PC-based restore-stock rescue orchestrator.
/// Uses mock ADB/Fastboot clients so no physical device is required.
/// </summary>
public class RestoreStockOrchestratorTests
{
    private static string Sha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] BuildBootImage(CpioArchive ramdisk, string cmdline = "")
    {
        using var compression = new CompressionEngine();
        var compressedRamdisk = compression.Compress(ramdisk.Serialize(), CompressionEngine.CompressionFormat.Gzip);

        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        const uint pageSize = 2048;
        const uint headerVersion = 0;

        var kernel = new byte[4096];
        Array.Fill<byte>(kernel, 0x55);

        writer.Write("ANDROID!"u8.ToArray());           // offset 0
        ms.Position = 8;
        writer.Write((uint)kernel.Length);              // offset 8 kernel_size
        writer.Write(0x00008000u);                      // offset 12 kernel_addr
        writer.Write((uint)compressedRamdisk.Length);   // offset 16 ramdisk_size
        writer.Write(0x01000000u);                      // offset 20 ramdisk_addr
        writer.Write(0u);                               // offset 24 second_size
        writer.Write(0u);                               // offset 28 second_addr
        writer.Write(0x00000100u);                      // offset 32 tags_addr
        writer.Write(pageSize);                         // offset 36 page_size
        writer.Write(headerVersion);                    // offset 40 header_version
        writer.Write(0u);                               // offset 44 os_version
        ms.Position = 48;
        writer.Write(new byte[16]);                     // offset 48 name
        ms.Position = 64;
        var cmdlineBytes = new byte[512];
        var cmdlineData = Encoding.ASCII.GetBytes(cmdline);
        Array.Copy(cmdlineData, cmdlineBytes, Math.Min(cmdlineData.Length, cmdlineBytes.Length));
        writer.Write(cmdlineBytes);                     // offset 64 cmdline
        ms.Position = 576;
        writer.Write(new byte[32]);                     // offset 576 id
        ms.Position = 608;
        writer.Write(new byte[1024]);                   // offset 608 extra_cmdline
        ms.Position = 1632;
        writer.Write(0u);                               // offset 1632 recovery_dtbo_size
        ms.Position = 1636;
        writer.Write(0ul);                              // offset 1636 recovery_dtbo_offset
        ms.Position = 1644;
        writer.Write(1660u);                            // offset 1644 header_size

        ms.Position = pageSize;
        writer.Write(kernel);
        ms.Position = pageSize + AlignUp(kernel.Length, pageSize);
        writer.Write(compressedRamdisk);

        return ms.ToArray();
    }

    private static int AlignUp(int value, uint alignment)
    {
        if (alignment == 0) return value;
        var a = (int)alignment;
        return (value + a - 1) / a * a;
    }

    private static CpioArchive CreateCleanRamdisk()
    {
        var archive = new CpioArchive();
        archive.Entries.Add(new CpioEntry
        {
            Name = "init",
            Data = "#!/system/bin/sh\nexec /system/bin/init\n"u8.ToArray(),
            Mode = 0x81ED
        });
        archive.Entries.Add(new CpioEntry
        {
            Name = "fstab.default",
            Data = "system /system ext4 ro,verify\n"u8.ToArray(),
            Mode = 0x81A4
        });
        archive.Entries.Add(new CpioEntry
        {
            Name = "sepolicy",
            Data = new byte[128],
            Mode = 0x81A4
        });
        archive.Entries.Add(new CpioEntry
        {
            Name = "init.rc",
            Data = "on early-init\n    start ueventd\n"u8.ToArray(),
            Mode = 0x81A4
        });
        return archive;
    }

    private static CpioArchive CreatePatchedRamdisk()
    {
        var archive = CreateCleanRamdisk();
        archive.Entries.Add(new CpioEntry
        {
            Name = "overlay.d/sbin/magisk",
            Data = new byte[1024],
            Mode = 0x81ED
        });
        archive.Entries.Add(new CpioEntry
        {
            Name = ".backup/.magisk",
            Data = "KEEPVERITY=true\nKEEPFORCEENCRYPT=true\nSHA1=abc123\n"u8.ToArray(),
            Mode = 0x81A4
        });
        archive.Entries.Add(new CpioEntry
        {
            Name = "init.magisk.rc",
            Data = "service magisk /sbin/magisk\n"u8.ToArray(),
            Mode = 0x81A4
        });
        // Alter init.rc to contain a Magisk pattern
        var initRc = archive.Entries.First(e => e.Name == "init.rc");
        initRc.Data = "on early-init\n    exec u:r:magisk:s0 -- /system/bin/init.magisk\n"u8.ToArray();
        return archive;
    }

    private static void PrepareStockDirectory(string stockDir, byte[] boot, byte[] vbmeta)
    {
        Directory.CreateDirectory(stockDir);
        File.WriteAllBytes(Path.Combine(stockDir, "boot.img"), boot);
        File.WriteAllBytes(Path.Combine(stockDir, "vbmeta.img"), vbmeta);
    }

    /// <summary>
    /// Mock ADB client that serves predefined boot/vbmeta bytes for pull operations.
    /// </summary>
    private class ConfigurableMockAdbClient : IAdbClient
    {
        private readonly string _slotSuffix;
        private readonly byte[]? _bootData;
        private readonly byte[]? _recoveryData;
        private readonly byte[]? _vbmetaData;
        private readonly string _dataAdbListing;
        private readonly string _overlayListing;
        private readonly List<string> _executedShellCommands = new();
        private byte[]? _postFlashBootData;
        private bool _cleaned;

        public bool IsConnected => true;
        public string? Serial { get; } = "MOCK-ADB";
        public string? DeviceModel { get; } = "SM-T307U";
        public string? AndroidVersion { get; } = "13";

        public event EventHandler<string>? LogMessage;
        public event EventHandler? DeviceStateChanged;

        public IReadOnlyList<string> ExecutedShellCommands => _executedShellCommands;

        public ConfigurableMockAdbClient(
            string slotSuffix,
            byte[]? bootData,
            byte[]? vbmetaData,
            string dataAdbListing,
            string overlayListing,
            byte[]? recoveryData = null)
        {
            _slotSuffix = slotSuffix;
            _bootData = bootData;
            _vbmetaData = vbmetaData;
            _dataAdbListing = dataAdbListing;
            _overlayListing = overlayListing;
            _recoveryData = recoveryData;
        }

        public void SetPostFlashBootData(byte[]? data) => _postFlashBootData = data;

        public Task<bool> ConnectTcpAsync(string host, int port = 5555, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ConnectUsbAsync(UsbDeviceManager usb, CancellationToken ct = default) => Task.FromResult(true);
        public Task<MagiskStatus> CheckMagiskStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new MagiskStatus { IsInstalled = false, Version = "", ModuleCount = 0 });

        public Task<string> ExecuteShellAsync(string command, CancellationToken ct = default)
        {
            _executedShellCommands.Add(command);
            var trimmed = command.Trim();
            if (trimmed.Contains("rm -rf /data/adb/magisk"))
                _cleaned = true;

            return Task.FromResult(trimmed switch
            {
                "getprop ro.boot.slot_suffix" => _slotSuffix,
                var c when c.StartsWith("ls -la /data/adb/") => _cleaned ? "total 0\n" : _dataAdbListing,
                var c when c.StartsWith("ls /data/adb/") => _cleaned ? "" : _dataAdbListing.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => !l.StartsWith("total") && !string.IsNullOrWhiteSpace(l)) ?? "",
                var c when c.StartsWith("mount") => _cleaned ? "NO_OVERLAYS" : _overlayListing,
                _ => ""
            });
        }

        public Task<bool> PullFileAsync(string remote, string local, CancellationToken ct = default)
        {
            var dir = Path.GetDirectoryName(local);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            byte[]? data = remote switch
            {
                "/dev/block/by-name/boot" => _bootData,
                "/dev/block/by-name/boot_a" => _bootData,
                "/dev/block/by-name/boot_b" => _bootData,
                "/dev/block/by-name/vbmeta" => _vbmetaData,
                "/dev/block/by-name/recovery" => _recoveryData,
                "/dev/block/by-name/recovery_a" => _recoveryData,
                "/dev/block/by-name/recovery_b" => _recoveryData,
                _ => null
            };

            if (data != null)
            {
                // Verification pulls happen after flash; use post-flash data if configured
                if (_postFlashBootData != null && (remote.Contains("boot")) && !remote.Contains("recovery"))
                {
                    File.WriteAllBytes(local, _postFlashBootData);
                    return Task.FromResult(true);
                }
                File.WriteAllBytes(local, data);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<bool> PushFileAsync(string local, string remote, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RebootAsync(string mode = "", CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RebootBootloaderAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RebootRecoveryAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<Dictionary<string, string>> GetPropertiesAsync(CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, string>());
        public Task<BootloaderLockStatus> CheckBootloaderLockStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new BootloaderLockStatus { IsLocked = false });
        public Task<string> ExecuteRootShellAsync(string command, CancellationToken ct = default) =>
            ExecuteShellAsync(command, ct);
        public Task<bool> WaitForDeviceAsync(int timeoutMs = 60000, CancellationToken ct = default) => Task.FromResult(true);

        public void Dispose() { }
    }

    [Fact]
    public async Task RestoreStock_AnalysisPhase_DetectsModifications()
    {
        var stockDir = Path.Combine(Path.GetTempPath(), $"restore_stock_stock_{Guid.NewGuid():N}");
        var workDir = Path.Combine(Path.GetTempPath(), $"restore_stock_work_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        var cleanRamdisk = CreateCleanRamdisk();
        var patchedRamdisk = CreatePatchedRamdisk();

        var stockBoot = BuildBootImage(cleanRamdisk, "androidboot.selinux=enforcing");
        var currentBoot = BuildBootImage(patchedRamdisk, "androidboot.selinux=permissive magisk");
        var vbmeta = new byte[256];
        new Random(1).NextBytes(vbmeta);

        PrepareStockDirectory(stockDir, stockBoot, vbmeta);

        var adb = new ConfigurableMockAdbClient("", currentBoot, vbmeta,
            "drwxrwx--x 2 root root 4096 /data/adb\ndrwxrwx--x 3 root root 4096 /data/adb/modules\n",
            "overlay on /system type overlay (ro)");
        var fastboot = new MockFastbootClient();
        var usb = new UsbDeviceManager();
        var parser = new BootImageParser();
        var detector = new MagiskArtifactDetector();
        var options = new RestoreStockOptions
        {
            StockDirectory = stockDir,
            WorkDirectory = workDir,
            DryRun = true
        };

        var orchestrator = new RestoreStockOrchestrator(adb, fastboot, usb, parser, detector, options);
        var result = await orchestrator.RunAsync();

        Assert.NotNull(result.Analysis);
        Assert.True(result.Analysis.BootModified, "Expected device boot to be flagged as modified");
        Assert.NotNull(result.Analysis.CurrentArtifactResult);
        Assert.True(result.Analysis.CurrentArtifactResult.IsMagiskInstalled, "Expected Magisk artifacts in current boot");
        Assert.Contains(result.Analysis.RamdiskEntryDiff, d => d.Contains("overlay.d/sbin/magisk"));
        Assert.Contains(result.Analysis.RamdiskEntryDiff, d => d.Contains("init.magisk.rc"));
        Assert.NotNull(result.Analysis.CmdlineDiff);
        Assert.Contains("magisk", result.Analysis.CmdlineDiff);

        // Dry-run must not flash or clean
        Assert.Empty(fastboot.FlashedPartitions);
        Assert.DoesNotContain(adb.ExecutedShellCommands, c => c.Contains("rm -rf /data/adb"));
        Assert.Equal(RestoreStockStatus.DryRun, result.Status);

        Directory.Delete(stockDir, true);
        Directory.Delete(workDir, true);
    }

    [Fact]
    public async Task RestoreStock_DryRun_NoFlashNoClean()
    {
        var stockDir = Path.Combine(Path.GetTempPath(), $"restore_stock_stock_{Guid.NewGuid():N}");
        var workDir = Path.Combine(Path.GetTempPath(), $"restore_stock_work_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        var cleanRamdisk = CreateCleanRamdisk();
        var stockBoot = BuildBootImage(cleanRamdisk);
        var currentBoot = BuildBootImage(cleanRamdisk);
        var vbmeta = new byte[256];
        new Random(2).NextBytes(vbmeta);

        PrepareStockDirectory(stockDir, stockBoot, vbmeta);

        var adb = new ConfigurableMockAdbClient("_a", currentBoot, vbmeta,
            "ls: /data/adb: No such file or directory",
            "NO_OVERLAYS");
        var fastboot = new MockFastbootClient();
        var usb = new UsbDeviceManager();
        var parser = new BootImageParser();
        var detector = new MagiskArtifactDetector();
        var options = new RestoreStockOptions
        {
            StockDirectory = stockDir,
            WorkDirectory = workDir,
            DryRun = true
        };

        var orchestrator = new RestoreStockOrchestrator(adb, fastboot, usb, parser, detector, options);
        var result = await orchestrator.RunAsync();

        Assert.Equal(RestoreStockStatus.DryRun, result.Status);
        Assert.Empty(fastboot.FlashedPartitions);
        Assert.DoesNotContain(adb.ExecutedShellCommands, c => c.Contains("rm -rf"));
        Assert.NotNull(result.Analysis);
        Assert.False(result.Analysis.BootModified);

        Directory.Delete(stockDir, true);
        Directory.Delete(workDir, true);
    }

    [Fact]
    public async Task RestoreStock_VerifyPhase_ConfirmsMatch()
    {
        var stockDir = Path.Combine(Path.GetTempPath(), $"restore_stock_stock_{Guid.NewGuid():N}");
        var workDir = Path.Combine(Path.GetTempPath(), $"restore_stock_work_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        var cleanRamdisk = CreateCleanRamdisk();
        var stockBoot = BuildBootImage(cleanRamdisk);
        var modifiedBoot = BuildBootImage(CreatePatchedRamdisk());
        var vbmeta = new byte[256];
        new Random(3).NextBytes(vbmeta);

        PrepareStockDirectory(stockDir, stockBoot, vbmeta);

        var adb = new ConfigurableMockAdbClient("", modifiedBoot, vbmeta,
            "drwxrwx--x 2 root root 4096 /data/adb\n",
            "overlay on /system type overlay (ro)");
        adb.SetPostFlashBootData(stockBoot);
        var fastboot = new MockFastbootClient();
        var usb = new UsbDeviceManager();
        var parser = new BootImageParser();
        var detector = new MagiskArtifactDetector();
        var options = new RestoreStockOptions
        {
            StockDirectory = stockDir,
            WorkDirectory = workDir,
            DryRun = false
        };

        var orchestrator = new RestoreStockOrchestrator(adb, fastboot, usb, parser, detector, options);
        var result = await orchestrator.RunAsync();

        Assert.True(result.FlashSuccess, "Flash phase should succeed");
        Assert.True(result.BootVerified, "Boot should verify against stock SHA256");
        Assert.True(result.VbmetaVerified, "vbmeta should verify against stock SHA256");
        Assert.Equal(RestoreStockStatus.Restored, result.Status);
        Assert.Contains(fastboot.FlashedPartitions, f => f.Partition == "boot");
        Assert.Contains(fastboot.FlashedPartitions, f => f.Partition == "vbmeta");
        Assert.Contains(adb.ExecutedShellCommands, c => c.Contains("rm -rf /data/adb/magisk"));

        Directory.Delete(stockDir, true);
        Directory.Delete(workDir, true);
    }
}
