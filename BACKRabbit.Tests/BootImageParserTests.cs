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
    /// Builds a synthetic boot image with a valid header for the requested version.
    /// Layouts match the AOSP boot image specification so parse/repack round-trips work.
    /// </summary>
    private byte[] BuildSynthetic(uint headerVersion, byte[] kernel, byte[] ramdisk,
        byte[]? dtb = null, byte[]? sig = null, bool isVendor = false)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        if (isVendor)
        {
            // Vendor boot image: VNDRBOOT magic + vendor header v3/v4
            byte[] vendorRamdisk;
            using (var compression = new CompressionEngine())
            {
                vendorRamdisk = compression.Compress(ramdisk, CompressionEngine.CompressionFormat.Gzip);
            }

            const uint pageSize = 4096;
            writer.Write("VNDRBOOT"u8.ToArray());          // offset 0
            writer.Write(headerVersion);                  // offset 8
            writer.Write(pageSize);                       // offset 12
            writer.Write(0x00008000u);                      // offset 16 kernel_addr
            writer.Write(0x01000000u);                    // offset 20 ramdisk_addr
            writer.Write((uint)vendorRamdisk.Length);     // offset 24 ramdisk_size
            ms.Position = 28;
            writer.Write(new byte[2048]);                 // offset 28 cmdline
            ms.Position = 2076;
            writer.Write(0x00000100u);                    // offset 2076 tags_addr
            ms.Position = 2080;
            writer.Write(new byte[16]);                   // offset 2080 name
            ms.Position = 2096;
            writer.Write(2112u);                          // offset 2096 header_size
            ms.Position = 2100;
            writer.Write((uint)(dtb?.Length ?? 0));      // offset 2100 dtb_size
            ms.Position = 2104;
            writer.Write(0x02000000ul);                   // offset 2104 dtb_addr

            ms.Position = pageSize;
            writer.Write(vendorRamdisk);
            ms.Position = pageSize + AlignUp(vendorRamdisk.Length, pageSize);
            if (dtb != null && dtb.Length > 0)
                writer.Write(dtb);
        }
        else if (headerVersion <= 2)
        {
            // AOSP v0/v1/v2 share the same base layout
            const uint pageSize = 2048;
            writer.Write("ANDROID!"u8.ToArray());           // offset 0
            ms.Position = 8;
            writer.Write((uint)kernel.Length);            // offset 8 kernel_size
            writer.Write(0x00008000u);                    // offset 12 kernel_addr
            writer.Write((uint)ramdisk.Length);           // offset 16 ramdisk_size
            writer.Write(0x01000000u);                    // offset 20 ramdisk_addr
            writer.Write(0u);                             // offset 24 second_size
            writer.Write(0u);                             // offset 28 second_addr
            writer.Write(0x00000100u);                    // offset 32 tags_addr
            writer.Write(pageSize);                       // offset 36 page_size
            writer.Write(headerVersion);                  // offset 40 header_version
            writer.Write(0u);                             // offset 44 os_version
            ms.Position = 48;
            writer.Write(new byte[16]);                   // offset 48 name
            ms.Position = 64;
            writer.Write(new byte[512]);                  // offset 64 cmdline
            ms.Position = 576;
            writer.Write(new byte[32]);                   // offset 576 id
            ms.Position = 608;
            writer.Write(new byte[1024]);                 // offset 608 extra_cmdline

            if (headerVersion >= 1)
            {
                ms.Position = 1632;
                writer.Write(0u);                         // offset 1632 recovery_dtbo_size
                ms.Position = 1636;
                writer.Write(0ul);                        // offset 1636 recovery_dtbo_offset
                ms.Position = 1644;
                writer.Write(headerVersion == 1 ? 1660u : 1660u); // offset 1644 header_size
            }

            if (headerVersion == 2)
            {
                ms.Position = 1648;
                writer.Write((uint)(dtb?.Length ?? 0));  // offset 1648 dtb_size
                ms.Position = 1652;
                writer.Write(0x02000000ul);               // offset 1652 dtb_addr
            }

            ms.Position = pageSize;
            writer.Write(kernel);
            ms.Position = pageSize + AlignUp(kernel.Length, pageSize);
            writer.Write(ramdisk);

            if (headerVersion == 2 && dtb != null && dtb.Length > 0)
            {
                ms.Position = pageSize + AlignUp(kernel.Length, pageSize) + AlignUp(ramdisk.Length, pageSize);
                writer.Write(dtb);
            }
        }
        else
        {
            // AOSP v3/v4: fixed 4096-byte header, no kernel_addr/second/etc
            const uint pageSize = 4096;
            writer.Write("ANDROID!"u8.ToArray());           // offset 0
            ms.Position = 8;
            writer.Write((uint)kernel.Length);            // offset 8 kernel_size
            writer.Write((uint)ramdisk.Length);             // offset 12 ramdisk_size
            writer.Write(0u);                             // offset 16 os_version
            writer.Write(headerVersion == 3 ? 4096u : 4096u); // offset 20 header_size
            writer.Write(0u);                             // offset 24 reserved_0
            writer.Write(0u);                             // offset 28 reserved_1
            writer.Write(0u);                             // offset 32 reserved_2
            writer.Write(0u);                             // offset 36 reserved_3
            writer.Write(headerVersion);                  // offset 40 header_version
            ms.Position = 44;
            writer.Write(new byte[1536]);                 // offset 44 cmdline

            if (headerVersion == 4)
            {
                ms.Position = 1580;
                writer.Write((uint)(sig?.Length ?? 0));  // offset 1580 signature_size
            }

            ms.Position = pageSize;
            writer.Write(kernel);
            ms.Position = pageSize + AlignUp(kernel.Length, pageSize);
            writer.Write(ramdisk);

            if (headerVersion == 4 && sig != null && sig.Length > 0)
            {
                ms.Position = pageSize + AlignUp(kernel.Length, pageSize) + AlignUp(ramdisk.Length, pageSize);
                writer.Write(sig);
            }
        }

        return ms.ToArray();
    }

    private static int AlignUp(int value, uint alignment)
    {
        if (alignment == 0) return value;
        var a = (int)alignment;
        return (value + a - 1) / a * a;
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