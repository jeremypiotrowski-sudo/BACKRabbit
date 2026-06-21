using System.Security.Cryptography;

namespace BACKRabbit.Tests;

/// <summary>
/// Compression round-trip tests: Compress → Decompress → SHA256 compare
/// </summary>
public class CompressionEngineTests
{
    private static string Sha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private void AssertCompressionRoundTrip(byte[] original, CompressionEngine.CompressionFormat format, string label)
    {
        using var engine = new CompressionEngine();

        // Compress
        var compressed = engine.Compress(original, format);
        Assert.NotNull(compressed);
        Assert.True(compressed.Length > 0, $"{label}: Compressed data is empty");

        // Decompress
        var decompressed = engine.Decompress(compressed, format);
        Assert.NotNull(decompressed);
        Assert.True(decompressed.Length > 0, $"{label}: Decompressed data is empty");

        // Verify
        Assert.Equal(original.Length, decompressed.Length);
        Assert.True(Sha256(original) == Sha256(decompressed),
            $"{label}: SHA256 mismatch after round-trip");
    }

    private static byte[] GenerateTestData(int size = 4096, int seed = 42)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Fact]
    public void CompressDecompress_Gzip_ProducesIdenticalData()
    {
        var data = GenerateTestData(4096, 1);
        AssertCompressionRoundTrip(data, CompressionEngine.CompressionFormat.Gzip, "Gzip");
    }

    [Fact]
    public void CompressDecompress_Lz4_ProducesIdenticalData()
    {
        var data = GenerateTestData(4096, 2);
        AssertCompressionRoundTrip(data, CompressionEngine.CompressionFormat.Lz4, "Lz4");
    }

    [Fact]
    public void CompressDecompress_Lz4Legacy_ProducesIdenticalData()
    {
        var data = GenerateTestData(4096, 3);
        AssertCompressionRoundTrip(data, CompressionEngine.CompressionFormat.Lz4Legacy, "Lz4Legacy");
    }

    [Fact]
    public void CompressDecompress_Xz_ProducesIdenticalData()
    {
        var data = GenerateTestData(4096, 4);
        AssertCompressionRoundTrip(data, CompressionEngine.CompressionFormat.Xz, "Xz");
    }

    [Fact]
    public void CompressDecompress_Lzma_ProducesIdenticalData()
    {
        var data = GenerateTestData(4096, 5);
        AssertCompressionRoundTrip(data, CompressionEngine.CompressionFormat.Lzma, "Lzma");
    }

    [Fact]
    public void CompressDecompress_Bzip2_ProducesIdenticalData()
    {
        var data = GenerateTestData(4096, 6);
        AssertCompressionRoundTrip(data, CompressionEngine.CompressionFormat.Bzip2, "Bzip2");
    }

    [Fact]
    public void CompressDecompress_LargeData_ProducesIdenticalData()
    {
        // Test with larger data (1MB) to catch buffer boundary issues
        var data = GenerateTestData(1024 * 1024, 7);
        AssertCompressionRoundTrip(data, CompressionEngine.CompressionFormat.Gzip, "Large Gzip");
        AssertCompressionRoundTrip(data, CompressionEngine.CompressionFormat.Lz4, "Large Lz4");
    }
}