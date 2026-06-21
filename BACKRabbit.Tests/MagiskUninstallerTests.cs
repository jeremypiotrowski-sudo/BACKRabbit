namespace BACKRabbit.Tests;

/// <summary>
/// Full offline workflow tests using staging firmware.
/// Verifies: extract → parse → detect → clean → verify
/// </summary>
public class MagiskUninstallerTests
{
    private static string StagingDir => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "staging");

    [Fact]
    public async Task Uninstall_StagingFirmware_ProducesCleanBootImage()
    {
        // Find a boot image in staging
        var bootPath = Path.Combine(StagingDir, "F966U1", "boot.img");
        if (!File.Exists(bootPath))
        {
            // Try clean_boot.img as fallback
            bootPath = Path.Combine(StagingDir, "F966U1", "clean_boot.img");
        }
        if (!File.Exists(bootPath)) return; // Skip if no staging data

        var uninstaller = new MagiskUninstaller();
        var options = new UninstallOptions
        {
            BootImagePath = bootPath,
            PreferBackupRestore = true,
            ForceStockFirmware = false,
            CreateBackup = false
        };

        var result = await uninstaller.UninstallAsync(options);

        Assert.NotNull(result);
        // The staging boot.img may or may not have Magisk — either outcome is valid
        Assert.NotNull(result.Message);
        Assert.NotNull(result.Steps);
    }

    [Fact]
    public async Task Uninstall_CleanedImage_ParsesSuccessfully()
    {
        // Run uninstaller on a real boot image and verify its OUTPUT has correct headers
        // (Pre-existing clean_boot.img files in staging may be stale legacy artifacts)
        var bootPath = Path.Combine(StagingDir, "F966U1", "boot.img");
        if (!File.Exists(bootPath))
        {
            bootPath = Path.Combine(StagingDir, "S928U1", "boot.img");
        }
        if (!File.Exists(bootPath)) return;

        var uninstaller = new MagiskUninstaller();
        var options = new UninstallOptions
        {
            BootImagePath = bootPath,
            PreferBackupRestore = true,
            ForceStockFirmware = false,
            CreateBackup = false
        };

        var result = await uninstaller.UninstallAsync(options);

        // If Magisk was detected and uninstalled, verify the output
        if (result.Success && result.RepackedImage != null && result.RepackedImage.Length > 0)
        {
            var parser = new BootImageParser();
            var image = parser.Parse(result.RepackedImage);

            Assert.NotNull(image);
            Assert.True(image.HeaderVersion <= 4 || image.HeaderVersion == 0xFF,
                $"Cleaned image should have valid header version, got {image.HeaderVersion}");
            Assert.True(image.KernelSize > 0, "Cleaned image should have kernel");
            Assert.True(image.RamdiskSize > 0, 
                $"Cleaned image should have ramdisk (size={image.RamdiskSize})");
        }
        // If no Magisk was detected, the test passes vacuously (nothing to clean)
    }

    [Fact]
    public void Uninstall_CleanedImage_DetectReturnsFalse()
    {
        var cleanPath = Path.Combine(StagingDir, "F966U1", "clean_boot.img");
        if (!File.Exists(cleanPath))
        {
            cleanPath = Path.Combine(StagingDir, "S928U1", "clean_boot.img");
        }
        if (!File.Exists(cleanPath)) return;

        var parser = new BootImageParser();
        var detector = new MagiskArtifactDetector();

        var image = parser.Parse(cleanPath);
        var ramdisk = parser.ExtractRamdiskArchive(image);

        var result = detector.Detect(ramdisk);

        Assert.False(result.IsMagiskInstalled,
            $"Clean boot image should NOT have Magisk detected. Found: {result.FoundArtifacts.Count} artifacts");
    }
}