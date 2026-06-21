namespace BACKRabbit.Tests;

/// <summary>
/// AVB footer detection and flag patching tests.
/// Uses the real AvbRestorer API: RestoreVerificationFlags() → AvbRestoreResult
/// </summary>
public class AvbRestorerTests
{
    /// <summary>
    /// Creates a boot image with a valid AVB footer appended.
    /// The AVB footer structure (64 bytes) contains:
    /// - 4 bytes: magic "AVB0"
    /// - 8 bytes: version major/minor
    /// - 8 bytes: original_image_size
    /// - 8 bytes: vbmeta_offset (from end of image)
    /// - 8 bytes: vbmeta_size
    /// - 28 bytes: reserved
    /// 
    /// The vbmeta header (256 bytes) contains flags at offset 88.
    /// </summary>
    private byte[] CreateImageWithAvbFooter(uint flags)
    {
        using var ms = new MemoryStream();

        // ANDROID! magic
        ms.Write("ANDROID!"u8.ToArray());

        // Minimal v0 header (1632 bytes total after magic)
        var header = new byte[1632 - 8];
        BitConverter.GetBytes(8192u).CopyTo(header, 0);   // kernel_size
        BitConverter.GetBytes(4096u).CopyTo(header, 8);   // ramdisk_size
        BitConverter.GetBytes(2048u).CopyTo(header, 24);  // page_size
        ms.Write(header);

        // Pad to page boundary (2048)
        ms.Position = 2048;
        var kernel = new byte[8192];
        new Random(1).NextBytes(kernel);
        ms.Write(kernel);

        // Pad kernel to page boundary
        ms.Position = 2048 + ((8192 + 2047) / 2048) * 2048;
        var ramdisk = new byte[4096];
        new Random(2).NextBytes(ramdisk);
        ms.Write(ramdisk);

        // Now append vbmeta header (256 bytes) with flags at offset 88
        var vbmetaHeader = new byte[256];
        "AVB0"u8.ToArray().CopyTo(vbmetaHeader, 0); // vbmeta magic
        BitConverter.GetBytes(flags).CopyTo(vbmetaHeader, 88); // flags at offset 88
        var vbmetaStart = ms.Position;
        ms.Write(vbmetaHeader);

        // Append AVB footer (64 bytes) pointing to vbmeta
        var footer = new byte[64];
        "AVB0"u8.ToArray().CopyTo(footer, 0); // footer magic @0
        // original_image_size @12 (8 bytes) = total image size (without footer)
        BitConverter.GetBytes((ulong)ms.Position).CopyTo(footer, 12);
        // vbmeta_offset @20 (8 bytes) = ABSOLUTE offset from partition start (AOSP avb_footer.h spec)
        var vbmetaOffset = (ulong)vbmetaStart;
        BitConverter.GetBytes(vbmetaOffset).CopyTo(footer, 20);
        // vbmeta_size @28 (8 bytes)
        BitConverter.GetBytes(256ul).CopyTo(footer, 28);
        ms.Write(footer);

        return ms.ToArray();
    }

    [Fact]
    public void Restore_FlagsAre3_RestoresTo0()
    {
        var image = CreateImageWithAvbFooter(3);
        var restorer = new AvbRestorer();

        var result = restorer.RestoreVerificationFlags(image);

        Assert.NotNull(result);
        Assert.True(result.Success, $"AVB restore should succeed: {result.Message}");
        Assert.True(result.FooterFound, "AVB footer should be found");
        Assert.True(result.FlagsChanged, "Flags should have changed from 3 to 0");
        Assert.NotNull(result.PatchedImage);
        Assert.True(result.PatchedImage!.Length > 0);
    }

    [Fact]
    public void Restore_FlagsAlready0_Remains0()
    {
        var image = CreateImageWithAvbFooter(0);
        var restorer = new AvbRestorer();

        var result = restorer.RestoreVerificationFlags(image);

        Assert.NotNull(result);
        Assert.True(result.Success, $"AVB restore should succeed: {result.Message}");
        Assert.True(result.FooterFound, "AVB footer should be found");
        Assert.False(result.FlagsChanged, "Flags should NOT change when already 0");
    }

    [Fact]
    public void Restore_NoAvbFooter_ReturnsGracefully()
    {
        // Create an image without AVB footer
        var image = new byte[4096];
        "ANDROID!"u8.ToArray().CopyTo(image, 0);
        new Random(3).NextBytes(image.AsSpan(8));

        var restorer = new AvbRestorer();

        var result = restorer.RestoreVerificationFlags(image);

        Assert.NotNull(result);
        Assert.False(result.Success, "Should fail gracefully when no AVB footer");
        Assert.False(result.FooterFound);
        Assert.NotNull(result.Message);
        Assert.Contains("No AVB footer", result.Message);
    }
}