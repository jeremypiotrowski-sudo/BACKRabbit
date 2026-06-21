namespace BACKRabbit.Tests;

/// <summary>
/// Magisk artifact detection tests on known patched and clean images.
/// </summary>
public class MagiskArtifactDetectorTests
{
    private static string StagingDir => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "staging");

    /// <summary>
    /// Creates a CPIO archive with Magisk artifacts (simulating a patched ramdisk).
    /// </summary>
    private CpioArchive CreatePatchedRamdisk()
    {
        var archive = new CpioArchive();

        // Standard init files
        archive.Entries.Add(new CpioEntry
        {
            Name = "init",
            Data = "#!/system/bin/sh\nexec /init.magisk\n"u8.ToArray(),
            Mode = 0x81ED
        });

        // Magisk init
        archive.Entries.Add(new CpioEntry
        {
            Name = "init.magisk",
            Data = new byte[512],
            Mode = 0x81ED
        });

        // Magisk overlay directory
        archive.Entries.Add(new CpioEntry
        {
            Name = "overlay.d/",
            Data = Array.Empty<byte>(),
            Mode = 0x41ED
        });

        archive.Entries.Add(new CpioEntry
        {
            Name = "overlay.d/sbin/magisk",
            Data = new byte[1024],
            Mode = 0x81ED
        });

        // Magisk backup config
        archive.Entries.Add(new CpioEntry
        {
            Name = ".backup/.magisk",
            Data = "KEEPVERITY=true\nKEEPFORCEENCRYPT=true\nSHA1=abc123def456\n"u8.ToArray(),
            Mode = 0x81A4
        });

        // Stock backup
        archive.Entries.Add(new CpioEntry
        {
            Name = "ramdisk.cpio.orig",
            Data = new byte[2048],
            Mode = 0x81A4
        });

        // Init backup
        archive.Entries.Add(new CpioEntry
        {
            Name = ".backup/init.xz",
            Data = new byte[512],
            Mode = 0x81A4
        });

        // fstab with verity patch
        archive.Entries.Add(new CpioEntry
        {
            Name = "fstab.samsung",
            Data = "system / ext4 ro,verify=/dev/block/by-name/vbmeta\n"u8.ToArray(),
            Mode = 0x81A4
        });

        return archive;
    }

    /// <summary>
    /// Creates a clean CPIO archive (no Magisk artifacts).
    /// </summary>
    private CpioArchive CreateCleanRamdisk()
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
            Name = "fstab.samsung",
            Data = "system / ext4 ro\n"u8.ToArray(),
            Mode = 0x81A4
        });

        archive.Entries.Add(new CpioEntry
        {
            Name = "sbin/",
            Data = Array.Empty<byte>(),
            Mode = 0x41ED
        });

        return archive;
    }

    [Fact]
    public void Detect_KnownPatchedImage_ReturnsIsMagiskInstalledTrue()
    {
        var detector = new MagiskArtifactDetector();
        var patched = CreatePatchedRamdisk();

        var result = detector.Detect(patched);

        Assert.True(result.IsMagiskInstalled,
            "Patched ramdisk should be detected as Magisk-installed");
        Assert.NotEmpty(result.FoundArtifacts);
    }

    [Fact]
    public void Detect_CleanStockImage_ReturnsIsMagiskInstalledFalse()
    {
        var detector = new MagiskArtifactDetector();
        var clean = CreateCleanRamdisk();

        var result = detector.Detect(clean);

        Assert.False(result.IsMagiskInstalled,
            "Clean ramdisk should not be detected as Magisk-installed");
        Assert.Empty(result.FoundArtifacts);
    }

    [Fact]
    public void Detect_HasBackup_ReturnsBackupAvailableTrue()
    {
        var detector = new MagiskArtifactDetector();
        var patched = CreatePatchedRamdisk();

        var result = detector.Detect(patched);

        Assert.True(result.HasFullBackup,
            "Patched ramdisk with ramdisk.cpio.orig should have full backup");
        Assert.True(result.HasInitBackup,
            "Patched ramdisk with .backup/init.xz should have init backup");
        Assert.True(result.HasBackup,
            "Patched ramdisk with .backup/.magisk should have backup config");
    }

    [Fact]
    public void Detect_NoBackup_ReturnsBackupAvailableFalse()
    {
        var detector = new MagiskArtifactDetector();
        var clean = CreateCleanRamdisk();

        var result = detector.Detect(clean);

        Assert.False(result.HasFullBackup);
        Assert.False(result.HasInitBackup);
        Assert.False(result.HasBackup);
    }

    [Fact]
    public void Detect_PatchedImage_ReturnsCorrectArtifactCount()
    {
        var detector = new MagiskArtifactDetector();
        var patched = CreatePatchedRamdisk();

        var result = detector.Detect(patched);

        // We added: init.magisk, overlay.d/sbin/magisk, .backup/.magisk,
        // ramdisk.cpio.orig, .backup/init.xz = 5 Magisk-specific artifacts
        // Plus overlay.d/ directory and init (which references magisk)
        Assert.True(result.FoundArtifacts.Count >= 5,
            $"Expected at least 5 artifacts, found {result.FoundArtifacts.Count}");
    }
}