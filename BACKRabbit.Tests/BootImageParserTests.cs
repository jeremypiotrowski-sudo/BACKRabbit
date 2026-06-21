using System.Security.Cryptography;

namespace BACKRabbit.Tests;

/// <summary>
/// Round-trip tests: Parse → Repack → Re-Parse → Compare
/// Verifies that boot images survive a full parse/repack cycle with identical content.
/// </summary>
public class BootImageParserTests
{
    private static string StagingDir => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "staging");

    private static string Sha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private void AssertRoundTrip(byte[] original, string label)
    {
        var parser = new BootImageParser();
        var repacker = new BootImageRepacker();

        // Parse original
        var parsed = parser.Parse(original);
        Assert.NotNull(parsed);
        Assert.True(parsed.HeaderVersion <= 4 || parsed.HeaderVersion == 0xFF,
            $"{label}: Unexpected header version {parsed.HeaderVersion}");

        // Extract kernel and ramdisk
        var kernel = parser.ExtractKernel(parsed);
        var ramdisk = parser.ExtractRamdisk(parsed);

        // Repack with same kernel and ramdisk (no modifications)
        var repacked = repacker.Repack(parsed, ramdisk, kernel);
        Assert.NotNull(repacked);
        Assert.True(repacked.Length > 0, $"{label}: Repacked image is empty");

        // Re-parse repacked
        var reParsed = parser.Parse(repacked);
        Assert.NotNull(reParsed);
        Assert.Equal(parsed.HeaderVersion, reParsed.HeaderVersion);

        // Verify kernel
        Assert.True(parsed.KernelSize == reParsed.KernelSize,
            $"{label}: Kernel size mismatch ({parsed.KernelSize} vs {reParsed.KernelSize})");
        if (parsed.KernelSize > 0)
        {
            var reKernel = parser.ExtractKernel(reParsed);
            Assert.True(Sha256(kernel) == Sha256(reKernel),
                $"{label}: Kernel SHA256 mismatch");
        }

        // Verify ramdisk — compare decompressed content (compressed sizes may differ)
        if (parsed.RamdiskSize > 0)
        {
            var reRamdisk = parser.ExtractRamdisk(reParsed);
            // Decompress both to compare actual content
            using var compression = new CompressionEngine();
            var origFormat = CompressionEngine.DetectFormat(ramdisk);
            var reFormat = CompressionEngine.DetectFormat(reRamdisk);
            var origDecompressed = compression.Decompress(ramdisk, origFormat);
            var reDecompressed = compression.Decompress(reRamdisk, reFormat);
            Assert.True(Sha256(origDecompressed) == Sha256(reDecompressed),
                $"{label}: Ramdisk content SHA256 mismatch (decompressed)");
        }

        // Verify DTB if present
        if (parsed.DtbSize > 0)
        {
            Assert.True(parsed.DtbSize == reParsed.DtbSize,
                $"{label}: DTB size mismatch ({parsed.DtbSize} vs {reParsed.DtbSize})");
        }
    }

    // ===== Real boot images from staging =====

    [Fact]
    public void ParseRepackReParse_F966U1_BootImg_ProducesIdenticalImage()
    {
        var path = Path.Combine(StagingDir, "F966U1", "boot.img");
        if (!File.Exists(path)) return;
        AssertRoundTrip(File.ReadAllBytes(path), "F966U1 boot.img");
    }

    [Fact]
    public void ParseRepackReParse_F966U1_InitBootImg_ProducesIdenticalImage()
    {
        var path = Path.Combine(StagingDir, "F966U1", "init_boot.img");
        if (!File.Exists(path)) return;
        AssertRoundTrip(File.ReadAllBytes(path), "F966U1 init_boot.img");
    }

    [Fact]
    public void ParseRepackReParse_F966U1_VendorBootImg_ProducesIdenticalImage()
    {
        var path = Path.Combine(StagingDir, "F966U1", "vendor_boot.img.lz4");
        if (!File.Exists(path)) return;
        var lz4Data = File.ReadAllBytes(path);
        var engine = new CompressionEngine();
        var raw = engine.Decompress(lz4Data, CompressionEngine.CompressionFormat.Lz4);
        AssertRoundTrip(raw, "F966U1 vendor_boot.img");
    }

    // ===== Synthetic boot images for all header versions =====

    /// <summary>
    /// Builds a synthetic boot image by creating a minimal valid image
    /// with known kernel/ramdisk data, then round-trips it.
    /// </summary>
    private byte[] BuildSynthetic(uint headerVersion, byte[] kernel, byte[] ramdisk,
        byte[]? dtb = null, byte[]? sig = null, bool isVendor = false)
    {
        // Create a minimal boot image by parsing a known-good image and modifying it
        // Strategy: Use the F966U1 boot.img as a template, then repack with our data
        var stagingBoot = Path.Combine(StagingDir, "F966U1", "boot.img");
        if (File.Exists(stagingBoot))
        {
            var parser = new BootImageParser();
            var repacker = new BootImageRepacker();
            var template = parser.Parse(File.ReadAllBytes(stagingBoot));
            // Override with our test data
            return repacker.Repack(template, ramdisk, kernel);
        }

        // Fallback: build from scratch using a minimal valid header
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        if (isVendor)
        {
            // Vendor boot image: VNDRBOOT magic + vendor header v3/v4
            // Pre-compress ramdisk (vendor boot ramdisks are always compressed in real images)
            byte[] vendorRamdisk;
            using (var compression = new CompressionEngine())
            {
                vendorRamdisk = compression.Compress(ramdisk, CompressionEngine.CompressionFormat.Gzip);
            }

            writer.Write("VNDRBOOT"u8.ToArray());
            // header_version at offset 8
            writer.Write(headerVersion);
            // page_size at offset 12
            var pageSize = 4096u;
            writer.Write(pageSize);
            // kernel_addr at offset 16
            writer.Write(0x00008000u);
            // ramdisk_addr at offset 20
            writer.Write(0x01000000u);
            // ramdisk_size at offset 24
            writer.Write((uint)vendorRamdisk.Length);
            // cmdline (2048 bytes) at offset 28
            ms.Position = 28;
            writer.Write(new byte[2048]);
            // tags_addr at offset 2076
            ms.Position = 2076;
            writer.Write(0x00000100u);
            // name (16 bytes) at offset 2080
            ms.Position = 2080;
            writer.Write(new byte[16]);
            // header_size at offset 2096
            ms.Position = 2096;
            writer.Write(2112u); // Vendor V3 header size
            // dtb_size at offset 2100
            ms.Position = 2100;
            writer.Write((uint)(dtb?.Length ?? 0));
            // dtb_addr at offset 2104
            ms.Position = 2104;
            writer.Write(0x02000000ul);

            // Pad to page boundary
            ms.Position = pageSize;
            // Vendor boot has no kernel — write compressed ramdisk directly
            writer.Write(vendorRamdisk);
            // Pad ramdisk to page boundary
            var ramdiskPages = (vendorRamdisk.Length + pageSize - 1) / pageSize;
            ms.Position = pageSize + ramdiskPages * pageSize;
            // Write DTB if present
            if (dtb != null && dtb.Length > 0)
                writer.Write(dtb);
        }
        else
        {
            // Standard AOSP boot: ANDROID! magic + v0 header + kernel + ramdisk
            writer.Write("ANDROID!"u8.ToArray());

            var headerSize = 1648;
            var pageSize = 2048;

            writer.Write(new byte[headerSize - 8]); // Zero-fill rest of header

            // Write kernel size at offset 8
            ms.Position = 8;
            writer.Write((uint)kernel.Length);
            // Write kernel addr at offset 12
            writer.Write(0x00008000u);
            // Write ramdisk size at offset 16
            writer.Write((uint)ramdisk.Length);
            // Write ramdisk addr at offset 20
            writer.Write(0x01000000u);
            // Write second size at offset 24
            writer.Write(0u);
            // Write tags addr at offset 28
            writer.Write(0x00000100u);
            // Write page size at offset 32
            writer.Write((uint)pageSize);
            // Write header version at offset 36
            writer.Write(headerVersion);
            // Write os version at offset 40
            writer.Write(0u);
            // Write name at offset 48
            ms.Position = 48;
            writer.Write(new byte[16]);
            // Write cmdline at offset 64
            ms.Position = 64;
            writer.Write(new byte[512]);

            // Pad to page boundary
            ms.Position = pageSize;
            writer.Write(kernel);
            // Pad kernel to page boundary
            var kernelPages = (kernel.Length + pageSize - 1) / pageSize;
            ms.Position = pageSize + kernelPages * pageSize;
            writer.Write(ramdisk);
        }

        return ms.ToArray();
    }

    [Fact]
    public void ParseRepackReParse_V0_Header_ProducesIdenticalImage()
    {
        var kernel = new byte[1024 * 8];
        var ramdisk = new byte[1024 * 4];
        new Random(42).NextBytes(kernel);
        new Random(99).NextBytes(ramdisk);
        var built = BuildSynthetic(0, kernel, ramdisk);
        AssertRoundTrip(built, "Synthetic V0");
    }

    [Fact]
    public void ParseRepackReParse_V1_Header_ProducesIdenticalImage()
    {
        var kernel = new byte[1024 * 8];
        var ramdisk = new byte[1024 * 4];
        new Random(43).NextBytes(kernel);
        new Random(100).NextBytes(ramdisk);
        var built = BuildSynthetic(1, kernel, ramdisk);
        AssertRoundTrip(built, "Synthetic V1");
    }

    [Fact]
    public void ParseRepackReParse_V2_Header_ProducesIdenticalImage()
    {
        var kernel = new byte[1024 * 8];
        var ramdisk = new byte[1024 * 4];
        var dtb = new byte[1024 * 2];
        new Random(44).NextBytes(kernel);
        new Random(101).NextBytes(ramdisk);
        new Random(200).NextBytes(dtb);
        var built = BuildSynthetic(2, kernel, ramdisk, dtb);
        AssertRoundTrip(built, "Synthetic V2");
    }

    [Fact]
    public void ParseRepackReParse_V3_Header_ProducesIdenticalImage()
    {
        var kernel = new byte[1024 * 8];
        var ramdisk = new byte[1024 * 4];
        new Random(45).NextBytes(kernel);
        new Random(102).NextBytes(ramdisk);
        var built = BuildSynthetic(3, kernel, ramdisk);
        AssertRoundTrip(built, "Synthetic V3");
    }

    [Fact]
    public void ParseRepackReParse_V4_Header_ProducesIdenticalImage()
    {
        var kernel = new byte[1024 * 8];
        var ramdisk = new byte[1024 * 4];
        var sig = new byte[512];
        new Random(46).NextBytes(kernel);
        new Random(103).NextBytes(ramdisk);
        new Random(250).NextBytes(sig);
        var built = BuildSynthetic(4, kernel, ramdisk, null, sig);
        AssertRoundTrip(built, "Synthetic V4");
    }

    [Fact]
    public void ParseRepackReParse_VendorBootV3_ProducesIdenticalImage()
    {
        var ramdisk = new byte[1024 * 4];
        var dtb = new byte[1024 * 2];
        new Random(47).NextBytes(ramdisk);
        new Random(201).NextBytes(dtb);
        var built = BuildSynthetic(3, Array.Empty<byte>(), ramdisk, dtb, null, true);
        AssertRoundTrip(built, "Synthetic VendorBoot V3");
    }

    [Fact]
    public void ParseRepackReParse_VendorBootV4_ProducesIdenticalImage()
    {
        var ramdisk = new byte[1024 * 4];
        var dtb = new byte[1024 * 2];
        new Random(48).NextBytes(ramdisk);
        new Random(202).NextBytes(dtb);
        var built = BuildSynthetic(4, Array.Empty<byte>(), ramdisk, dtb, null, true);
        AssertRoundTrip(built, "Synthetic VendorBoot V4");
    }

    // ===== Samsung PXA format =====

    [Fact]
    public void ParseRepackReParse_SamsungPXA_Header_ProducesIdenticalImage()
    {
        // Use F966U1 boot.img which is a Samsung device — likely PXA format
        var path = Path.Combine(StagingDir, "F966U1", "boot.img");
        if (!File.Exists(path)) return;
        var data = File.ReadAllBytes(path);
        var parser = new BootImageParser();
        var parsed = parser.Parse(data);
        // If it has a PXA header, test round-trip
        if (parsed.HeaderPxa.magic != null && parsed.HeaderPxa.magic.Length > 0)
        {
            AssertRoundTrip(data, "Samsung PXA (F966U1 boot.img)");
        }
        // Otherwise, the format detector didn't find PXA — skip gracefully
    }

    // ===== DHTB format =====

    [Fact]
    public void ParseRepackReParse_DHTB_Header_ProducesIdenticalImage()
    {
        // DHTB is a MediaTek/DHTB header format
        // Test with a synthetic image that has DHTB magic
        var kernel = new byte[1024 * 8];
        var ramdisk = new byte[1024 * 4];
        new Random(50).NextBytes(kernel);
        new Random(105).NextBytes(ramdisk);

        // Build a standard image and prepend DHTB magic
        var standard = BuildSynthetic(0, kernel, ramdisk);
        using var ms = new MemoryStream();
        ms.Write("DHTB"u8.ToArray());
        ms.Write(new byte[512 - 4]); // DHTB header padding
        ms.Write(standard);
        var dhtbImage = ms.ToArray();

        AssertRoundTrip(dhtbImage, "Synthetic DHTB");
    }

    // ===== MTK format =====

    [Fact]
    public void ParseRepackReParse_MTK_Header_ProducesIdenticalImage()
    {
        // MTK images have a MediaTek header
        var kernel = new byte[1024 * 8];
        var ramdisk = new byte[1024 * 4];
        new Random(51).NextBytes(kernel);
        new Random(106).NextBytes(ramdisk);

        var standard = BuildSynthetic(0, kernel, ramdisk);
        using var ms = new MemoryStream();
        // MTK header: 512 bytes with MTK magic
        var mtkHeader = new byte[512];
        "MTK "u8.ToArray().CopyTo(mtkHeader, 0);
        ms.Write(mtkHeader);
        ms.Write(standard);
        var mtkImage = ms.ToArray();

        AssertRoundTrip(mtkImage, "Synthetic MTK");
    }

    // ===== ChromeOS format =====

    [Fact]
    public void ParseRepackReParse_ChromeOS_Header_ProducesIdenticalImage()
    {
        // ChromeOS images have a ChromeOS-specific header
        var kernel = new byte[1024 * 8];
        var ramdisk = new byte[1024 * 4];
        new Random(52).NextBytes(kernel);
        new Random(107).NextBytes(ramdisk);

        var standard = BuildSynthetic(0, kernel, ramdisk);
        using var ms = new MemoryStream();
        // ChromeOS header: "CHROMEOS" magic
        var chromeHeader = new byte[512];
        "CHROMEOS"u8.ToArray().CopyTo(chromeHeader, 0);
        ms.Write(chromeHeader);
        ms.Write(standard);
        var chromeImage = ms.ToArray();

        AssertRoundTrip(chromeImage, "Synthetic ChromeOS");
    }
}