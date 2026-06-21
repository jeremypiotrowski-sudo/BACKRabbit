using Xunit;
using BACKRabbit.MagiskCore.Parser;
using BACKRabbit.MagiskCore.RamdiskEditor;
using BACKRabbit.MagiskCore.FormatDetection;
using BACKRabbit.MagiskCore.Compression;

namespace BACKRabbit.Tests;

public class MagiskCoreTests
{
[Fact]
    public void FormatDetector_DetectsGzip()
    {
        // GZIP magic (0x1F 0x8B) + minimum 8 bytes for detection
        var gzipData = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var format = FormatDetector.CheckFmt(gzipData);
        Assert.Equal(FileFormat.GZIP, format);
    }

    [Fact]
    public void FormatDetector_DetectsLZ4()
    {
        // LZ4 magic (0x02 0x21 0x4C 0x18) + minimum 8 bytes for detection
        var lz4Data = new byte[] { 0x02, 0x21, 0x4C, 0x18, 0x00, 0x00, 0x00, 0x00 };
        var format = FormatDetector.CheckFmt(lz4Data);
        Assert.Equal(FileFormat.LZ4, format);
    }

    [Fact]
    public void FormatDetector_DetectsBootImage()
    {
        var bootData = "ANDROID!"u8.ToArray();
        var format = FormatDetector.CheckFmt(bootData);
        Assert.Equal(FileFormat.AOSP, format);
    }

    [Fact]
    public void CpioArchive_ParseAndSerialize_RoundTrip()
    {
        var original = new CpioArchive();
        original.Entries.Add(new CpioEntry 
        { 
            Name = "test.txt", 
            Data = "Hello World"u8.ToArray() 
        });

        var serialized = original.Serialize();
        var parsed = CpioArchive.Parse(serialized);

        // Parse stops at TRAILER!!! (marker), so parsed has only original entries
        Assert.Equal(original.Entries.Count, parsed.Entries.Count);
        Assert.Equal(original.Entries[0].Name, parsed.Entries[0].Name);
        Assert.Equal(original.Entries[0].Data, parsed.Entries[0].Data);
    }

[Fact]
    public void CompressionEngine_Gzip_RoundTrip()
    {
        var original = "Test data for compression"u8.ToArray();
        using var compression = new CompressionEngine();
        var compressed = compression.Compress(original, CompressionEngine.CompressionFormat.Gzip);
        var decompressed = compression.Decompress(compressed);
        
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void MagiskArtifactDetector_FindsArtifacts()
    {
        var ramdisk = new CpioArchive();
        ramdisk.Entries.Add(new CpioEntry 
        { 
            Name = "overlay.d/sbin/magisk.xz", 
            Data = new byte[100] 
        });
        ramdisk.Entries.Add(new CpioEntry 
        { 
            Name = ".backup/.magisk", 
            Data = "KEEPVERITY=true"u8.ToArray() 
        });

        var detector = new MagiskArtifactDetector();
        var result = detector.Detect(ramdisk);

        Assert.True(result.IsMagiskInstalled);
        Assert.Contains("overlay.d/sbin/magisk.xz", result.FoundArtifacts);
        Assert.True(result.HasBackup);
    }
}