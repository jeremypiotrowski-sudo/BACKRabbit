using System.IO.Compression;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using SevenZip.Compression.LZMA;
using BACKRabbit.MagiskCore.FormatDetection;

// Aliases to resolve ambiguity
using SysCompressionMode = System.IO.Compression.CompressionMode;
using SharpCompressCompressionMode = SharpCompress.Compressors.CompressionMode;
using SevenZipDecoder = SevenZip.Compression.LZMA.Decoder;
using SevenZipEncoder = SevenZip.Compression.LZMA.Encoder;

namespace BACKRabbit.MagiskCore.Compression;

/// <summary>
/// Pure C# compression/decompression engine supporting all Android boot image formats.
/// NO external CLI dependencies - works on clean machines.
/// .NET 8 compatible.
/// </summary>
public class CompressionEngine : IDisposable
{
    private bool _disposed;

    public enum CompressionFormat
    {
        Unknown,
        Gzip,
        Bzip2,
        Lz4,
        Lz4Legacy,
        Xz,
        Lzma
    }

    public static CompressionFormat DetectFormat(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length < 6) return CompressionFormat.Unknown;

        if (data[0] == 0x1F && data[1] == 0x8B)
            return CompressionFormat.Gzip;

        if (data[0] == 0x42 && data[1] == 0x5A && data[2] == 0x68)
            return CompressionFormat.Bzip2;

        // LZ4 frame magic: 0x04, 0x22, 0x4D, 0x18
        if (data[0] == 0x04 && data[1] == 0x22 && data[2] == 0x4D && data[3] == 0x18)
            return CompressionFormat.Lz4;
        // LZ4 legacy magic: 0x02, 0x21, 0x4C, 0x18 (Magisk lz4_legacy)
        if (data[0] == 0x02 && data[1] == 0x21 && data[2] == 0x4C && data[3] == 0x18)
            return CompressionFormat.Lz4Legacy;
        // LZ4 legacy variant: 0x03, 0x21, 0x4C, 0x18
        if (data[0] == 0x03 && data[1] == 0x21 && data[4] == 0x4C && data[5] == 0x18)
            return CompressionFormat.Lz4Legacy;

        if (data[0] == 0xFD && data[1] == 0x37 && data[2] == 0x7A && 
            data[3] == 0x58 && data[4] == 0x5A && data[5] == 0x00)
            return CompressionFormat.Xz;

        if (data[0] == 0x5D && data[1] <= 0x40)
            return CompressionFormat.Lzma;

        return CompressionFormat.Unknown;
    }

    public byte[] Decompress(byte[] data, CompressionFormat? format = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (_disposed) throw new ObjectDisposedException(nameof(CompressionEngine));

        var detectedFormat = format ?? DetectFormat(data);

        return detectedFormat switch
        {
            CompressionFormat.Gzip => DecompressGzip(data),
            CompressionFormat.Bzip2 => DecompressBzip2(data),
            CompressionFormat.Lz4 or CompressionFormat.Lz4Legacy => DecompressLz4(data),
            CompressionFormat.Xz => DecompressXz(data),
            CompressionFormat.Lzma => DecompressLzma(data),
            _ => data  // Uncompressed/unknown — return as-is (raw CPIO, etc.)
        };
    }

    public byte[] Compress(byte[] data, CompressionFormat format)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (_disposed) throw new ObjectDisposedException(nameof(CompressionEngine));

        return format switch
        {
            CompressionFormat.Gzip => CompressGzip(data),
            CompressionFormat.Bzip2 => CompressBzip2(data),
            CompressionFormat.Lz4 => CompressLz4(data),
            CompressionFormat.Lz4Legacy => CompressLz4Legacy(data),
            CompressionFormat.Xz => CompressXz(data),
            CompressionFormat.Lzma => CompressLzma(data),
            _ => throw new ArgumentException($"Unsupported compression format: {format}", nameof(format))
        };
    }

    #region GZIP

    private byte[] DecompressGzip(byte[] data)
    {
        using var inputStream = new MemoryStream(data);
        using var gzipStream = new GZipStream(inputStream, SysCompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        gzipStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    private byte[] CompressGzip(byte[] data)
    {
        using var inputStream = new MemoryStream(data);
        using var outputStream = new MemoryStream();
        using var gzipStream = new GZipStream(outputStream, SysCompressionMode.Compress);
        inputStream.CopyTo(gzipStream);
        gzipStream.Close();
        return outputStream.ToArray();
    }

    #endregion

    #region BZIP2 (SharpCompress)

    private byte[] DecompressBzip2(byte[] data)
    {
        using var inputStream = new MemoryStream(data);
        using var outputStream = new MemoryStream();
        using var bzip2Stream = new BZip2Stream(inputStream, SharpCompressCompressionMode.Decompress, true);
        bzip2Stream.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    private byte[] CompressBzip2(byte[] data)
    {
        using var outputStream = new MemoryStream();
        using (var bzip2Stream = new BZip2Stream(outputStream, SharpCompressCompressionMode.Compress, true))
        {
            bzip2Stream.Write(data, 0, data.Length);
        }
        return outputStream.ToArray();
    }

    #endregion

    #region LZ4 (K4os - .NET 8 Compatible)

    private byte[] DecompressLz4(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        
        var format = DetectFormat(data);
        
        if (format == CompressionFormat.Lz4Legacy)
        {
            // Legacy: First 4 bytes after magic = uncompressed size
            if (data.Length < 8)
                throw new InvalidDataException("LZ4 legacy data too short");
            
            var uncompressedSize = BitConverter.ToInt32(data, 4);
            var compressedData = data.AsSpan(8);
            
            var outputBuffer = new byte[uncompressedSize];
            var decodedBytes = LZ4Codec.Decode(
                compressedData, 
                outputBuffer
            );
            
            if (decodedBytes != uncompressedSize)
                throw new InvalidDataException($"LZ4 decompression size mismatch: expected {uncompressedSize}, got {decodedBytes}");
            
            return outputBuffer;
        }
        else
        {
            // Frame format - use LZ4Stream.Decode static method (K4os 1.3.8)
            using var inputStream = new MemoryStream(data);
            using var outputStream = new MemoryStream();
            using var lz4Stream = LZ4Stream.Decode(inputStream);
            lz4Stream.CopyTo(outputStream);
            return outputStream.ToArray();
        }
    }

    private byte[] CompressLz4(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        // Use LZ4Stream.Encode to produce proper LZ4 frame format
        // that DecompressLz4 can decode with LZ4Stream.Decode
        using var outputStream = new MemoryStream();
        using var lz4Stream = LZ4Stream.Encode(outputStream);
        lz4Stream.Write(data, 0, data.Length);
        lz4Stream.Close(); // Flush and write frame end marker
        return outputStream.ToArray();
    }

    private byte[] CompressLz4Legacy(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        // Legacy format: magic(4) + uncompressed_size(4) + raw LZ4 blocks
        using var outputStream = new MemoryStream();
        outputStream.Write([0x02, 0x21, 0x4C, 0x18]); // LZ4 magic
        outputStream.Write(BitConverter.GetBytes(data.Length)); // Uncompressed size

        var maxCompressedSize = LZ4Codec.MaximumOutputSize(data.Length);
        var compressedBuffer = new byte[maxCompressedSize];
        var compressedSize = LZ4Codec.Encode(data, compressedBuffer);

        if (compressedSize <= 0)
            throw new IOException($"LZ4 legacy compression failed with code {compressedSize}");

        outputStream.Write(compressedBuffer, 0, compressedSize);
        return outputStream.ToArray();
    }

    #endregion

    #region XZ (SevenZip.Compression - XZ uses LZMA2)

    private byte[] DecompressXz(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length < 12)
            throw new InvalidDataException("XZ data too short - invalid header");

        try
        {
            using var inputStream = new MemoryStream(data);
            using var outputStream = new MemoryStream();
            
            // XZ uses LZMA2 - read XZ header and use LZMA decoder
            var properties = new byte[5];
            inputStream.Read(properties, 0, 5);
            
            var outSizeBytes = new byte[8];
            inputStream.Read(outSizeBytes, 0, 8);
            long outSize = BitConverter.ToInt64(outSizeBytes, 0);
            
            // Use SevenZip LZMA decoder for XZ (LZMA2)
            var decoder = new SevenZipDecoder();
            decoder.SetDecoderProperties(properties);
            
            decoder.Code(inputStream, outputStream, inputStream.Length, outSize, null);
            
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"XZ decompression failed: {ex.Message}", ex);
        }
    }

    private byte[] CompressXz(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        try
        {
            using var outputStream = new MemoryStream();
            
            // XZ uses LZMA2 encoder
            var encoder = new SevenZipEncoder();
            encoder.SetCoderProperties(
                new SevenZip.CoderPropID[]
                {
                    SevenZip.CoderPropID.DictionarySize,
                    SevenZip.CoderPropID.PosStateBits,
                    SevenZip.CoderPropID.LitContextBits,
                    SevenZip.CoderPropID.LitPosBits,
                    SevenZip.CoderPropID.Algorithm,
                    SevenZip.CoderPropID.NumFastBytes,
                    SevenZip.CoderPropID.MatchFinder,
                    SevenZip.CoderPropID.EndMarker
                },
                new object[]
                {
                    1 << 23,      // Dictionary size: 8MB
                    2,            // Pos state bits
                    3,            // Lit context bits
                    0,            // Lit pos bits
                    2,            // Algorithm: normal
                    128,          // Num fast bytes
                    "bt4",        // Match finder
                    true          // End marker
                }
            );

            // Write properties
            encoder.WriteCoderProperties(outputStream);
            
            // Write uncompressed size
            outputStream.Write(BitConverter.GetBytes((long)data.Length), 0, 8);
            
            // Compress
            encoder.Code(new MemoryStream(data), outputStream, -1, -1, null);
            
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            throw new IOException($"XZ compression failed: {ex.Message}", ex);
        }
    }

    #endregion

    #region LZMA (SevenZip.Compression - .NET 8 Compatible)

    private byte[] DecompressLzma(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length < 13)
            throw new InvalidDataException("LZMA data too short - invalid properties");

        try
        {
            using var inputStream = new MemoryStream(data);
            
            // Read LZMA properties (5 bytes)
            var properties = new byte[5];
            var read = inputStream.Read(properties, 0, 5);
            if (read != 5)
                throw new InvalidDataException("Failed to read LZMA properties");

            // Read uncompressed size (8 bytes) - standard LZMA SDK format
            var outSizeBytes = new byte[8];
            read = inputStream.Read(outSizeBytes, 0, 8);
            if (read != 8)
                throw new InvalidDataException("Failed to read LZMA output size");

            long outSize = BitConverter.ToInt64(outSizeBytes, 0);
            if (outSize < 0) outSize = -1;

            // Decode using SevenZip.Compression
            var decoder = new SevenZipDecoder();
            decoder.SetDecoderProperties(properties);
            
            using var outputStream = new MemoryStream();
            decoder.Code(inputStream, outputStream, inputStream.Length, outSize, null);
            
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"LZMA decompression failed: {ex.Message}", ex);
        }
    }

    private byte[] CompressLzma(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        try
        {
            using var outputStream = new MemoryStream();
            var encoder = new SevenZipEncoder();
            
            // Write properties
            encoder.WriteCoderProperties(outputStream);
            
            // Write uncompressed size
            outputStream.Write(BitConverter.GetBytes((long)data.Length), 0, 8);
            
            // Compress
            encoder.Code(new MemoryStream(data), outputStream, -1, -1, null);
            
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            throw new IOException($"LZMA compression failed: {ex.Message}", ex);
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~CompressionEngine() => Dispose();
}