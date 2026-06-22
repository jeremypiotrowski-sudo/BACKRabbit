using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using System.Formats.Tar;
using BACKRabbit.Protocol.Fastboot;
using K4os.Compression.LZ4.Streams;

namespace BACKRabbit.Firmware;

public class SamsungFirmwareExtractor
{
    public static FirmwarePackage ExtractTarMd5(string tarMd5Path, bool skipMd5Verification = false)
    {
        var package = new FirmwarePackage();
        var partitions = new Dictionary<string, byte[]>();
        
        using var fs = new FileStream(tarMd5Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < 16)
            throw new InvalidDataException("Invalid .tar.md5 file");
        
        fs.Seek(-16, SeekOrigin.End);
        var embeddedMd5 = new byte[16];
        fs.ReadExactly(embeddedMd5);
        
        fs.Seek(0, SeekOrigin.Begin);
        using var md5 = System.Security.Cryptography.MD5.Create();
        var buffer = new byte[1024 * 1024];
        long remaining = fs.Length - 16;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = fs.Read(buffer, 0, toRead);
            if (read == 0) break;
            md5.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var calculatedMd5 = md5.Hash!;
        
        if (!skipMd5Verification && !embeddedMd5.SequenceEqual(calculatedMd5))
            throw new InvalidDataException("MD5 verification failed");
        
        fs.Seek(0, SeekOrigin.Begin);
        var payloadLength = fs.Length - 16;
        
        // Samsung has shipped at least three AP archive layouts:
        // 1. Plain TAR containing raw .img files (legacy)
        // 2. Plain TAR containing individually lz4-compressed .img.lz4 files (2020+)
        // 3. Entire payload is an lz4-compressed TAR (rare variants)
        //
        // We attempt them in that order, tracking which succeeded.
        bool extracted = false;
        string? methodUsed = null;
        
        // Case 1 & 2: open the outer container as a TAR directly.
        if (ExtractPartitionsFromTar(fs, payloadLength, partitions, out methodUsed))
        {
            extracted = true;
        }
        else
        {
            // Case 3: whole-file lz4 TAR. Try streaming decompression into a TAR.
            fs.Seek(0, SeekOrigin.Begin);
            if (TryDecodeLz4Tar(fs, payloadLength, partitions))
            {
                extracted = true;
                methodUsed = "lz4-tar";
            }
        }
        
        if (!extracted)
        {
            Console.WriteLine($"   ⚠️ Could not extract any partitions from {Path.GetFileName(tarMd5Path)} (tried raw-tar, lz4-in-tar, lz4-tar)");
        }
        else
        {
            Console.WriteLine($"   ✅ Extracted {partitions.Count} partition(s) via {methodUsed}");
        }
        
        package.Partitions = partitions;
        package.Metadata = ParseFirmwareMetadata(tarMd5Path);
        
        return package;
    }
    
    private class LimitedStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _limit;
        private long _position;
        
        public LimitedStream(Stream baseStream, long limit)
        {
            _baseStream = baseStream;
            _limit = limit;
            _position = 0;
        }
        
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _limit;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _limit) return 0;
            long remaining = _limit - _position;
            int toRead = (int)Math.Min(count, remaining);
            int read = _baseStream.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }
        
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        

        protected override void Dispose(bool disposing)
        {
            // Do NOT dispose the base stream here — the caller owns it.
            base.Dispose(disposing);
        }
    }
    
    public static Dictionary<string, byte[]> ExtractSuperImg(string superImgPath)
    {
        var partitions = new Dictionary<string, byte[]>();
        var data = File.ReadAllBytes(superImgPath);
        
        if (data.Length >= 4 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data) == 0xED26)
        {
            var sparse = SparseImage.Parse(data);
            var raw = sparse.ToRawImage();
            partitions["super"] = raw;
        }
        else
        {
            partitions["super"] = data;
        }
        
        return partitions;
    }
    

    private static bool ExtractPartitionsFromTar(FileStream fs, long length, Dictionary<string, byte[]> partitions, out string? methodUsed)
    {
        methodUsed = null;
        try
        {
            // System.Formats.Tar is built into .NET 7+ and handles Samsung tar.md5 correctly.
            // It expects a non-seekable stream, so we use our LimitedStream wrapper.
            using var limitedStream = new LimitedStream(fs, length);
            using var tarReader = new TarReader(limitedStream, leaveOpen: false);
            bool hasLz4Entries = false;
            bool hasRawEntries = false;

            TarEntry? entry;
            while ((entry = tarReader.GetNextEntry(copyData: true)) != null)
            {
                if (entry.EntryType is TarEntryType.Directory or TarEntryType.DirectoryList)
                    continue;

                var key = entry.Name;
                var fileName = System.IO.Path.GetFileName(key).ToLowerInvariant();
                var data = entry.DataStream == null ? Array.Empty<byte>() : ReadAllBytes(entry.DataStream);

                if (fileName.EndsWith(".lz4"))
                {
                    hasLz4Entries = true;
                    data = DecompressLz4Block(data, fileName);
                    if (data.Length == 0)
                        continue;
                    key = key.Substring(0, key.Length - 4);
                }
                else
                {
                    hasRawEntries = true;
                }

                var partitionName = GetPartitionName(key);
                partitions[partitionName] = data;
            }

            if (partitions.Count == 0)
                return false;

            methodUsed = hasLz4Entries ? "tar-lz4" : "tar-raw";
            if (hasLz4Entries && hasRawEntries)
                methodUsed = "tar-mixed";

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ TAR extraction failed: {ex.Message}");
            return false;
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek)
        {
            var length = stream.Length;
            if (length > int.MaxValue)
                throw new InvalidDataException("TAR entry too large for memory buffer");
            var buffer = new byte[length];
            stream.ReadExactly(buffer);
            return buffer;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }


    /// <summary>
    /// Decompresses a Samsung .lz4 file.
    /// Samsung ships both LZ4 frame format (magic 0x184D2204) and raw blocks.
    /// </summary>
    private static byte[] DecompressLz4Block(byte[] compressed, string context)
    {
        if (compressed.Length == 0)
            return Array.Empty<byte>();

        try
        {
            // Detect LZ4 frame format by magic number.
            bool isFrame = compressed.Length >= 4 &&
                compressed[0] == 0x04 && compressed[1] == 0x22 &&
                compressed[2] == 0x4D && compressed[3] == 0x18;

            if (isFrame)
            {
                using var input = new MemoryStream(compressed, writable: false);
                using var decoder = LZ4Stream.Decode(input);
                using var output = new MemoryStream();
                decoder.CopyTo(output);
                return output.ToArray();
            }

            // Fallback: raw LZ4 block (no frame header).
            long estimateLong = Math.Max((long)compressed.Length * 15, 1024 * 1024);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (estimateLong > int.MaxValue)
                    throw new InvalidDataException($"LZ4 output would exceed {int.MaxValue} bytes for {context}");
                int estimate = (int)estimateLong;

                var output = new byte[estimate];
                var decoded = K4os.Compression.LZ4.LZ4Codec.Decode(compressed.AsSpan(), output.AsSpan());
                if (decoded >= 0)
                {
                    if (decoded == output.Length)
                        return output;
                    var trimmed = new byte[decoded];
                    Buffer.BlockCopy(output, 0, trimmed, 0, decoded);
                    return trimmed;
                }

                if (decoded == -1) // overflow
                {
                    estimateLong *= 4;
                    continue;
                }

                throw new InvalidDataException($"LZ4 decode error {decoded}");
            }

            throw new InvalidDataException($"LZ4 output buffer exhausted after growth for {context}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Failed to decompress {context}: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Fallback: entire payload is an lz4-compressed TAR (rare Samsung variants).
    /// </summary>
    private static bool TryDecodeLz4Tar(FileStream fs, long length, Dictionary<string, byte[]> partitions)
    {
        try
        {
            var compressedLength = (int)length;
            if (compressedLength < 0 || compressedLength > int.MaxValue)
                return false;

            var compressed = new byte[compressedLength];
            fs.ReadExactly(compressed, 0, compressedLength);

            // Estimate 15:1, max 2 GB to avoid OOM on massive files.
            long estimateLong = Math.Min((long)compressed.Length * 15, 2L * 1024 * 1024 * 1024);
            int estimate = (int)estimateLong;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var output = new byte[estimate];
                var decoded = K4os.Compression.LZ4.LZ4Codec.Decode(compressed.AsSpan(), output.AsSpan());
                if (decoded >= 0)
                {
                    using var ms = new MemoryStream(output, 0, decoded);
                    using var archive = TarArchive.Open(ms);
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.IsDirectory)
                            continue;

                        var key = entry.Key ?? "";
                        var fileName = System.IO.Path.GetFileName(key).ToLowerInvariant();
                        using var entryStream = entry.OpenEntryStream();
                        using var memStream = new MemoryStream();
                        entryStream.CopyTo(memStream);
                        var data = memStream.ToArray();

                        if (fileName.EndsWith(".lz4"))
                        {
                            data = DecompressLz4Block(data, fileName);
                            key = key.Substring(0, key.Length - 4);
                        }

                        if (data.Length > 0)
                        {
                            var partitionName = GetPartitionName(key);
                            partitions[partitionName] = data;
                        }
                    }
                    return partitions.Count > 0;
                }

                if (decoded == -1)
                {
                    estimateLong = Math.Min((long)estimate * 4, 2L * 1024 * 1024 * 1024);
                    estimate = (int)estimateLong;
                    continue;
                }

                return false;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ LZ4-TAR fallback failed: {ex.Message}");
            return false;
        }
    }

    private static string GetPartitionName(string entryName)
    {
        var name = System.IO.Path.GetFileName(entryName);
        name = System.IO.Path.GetFileNameWithoutExtension(name);
        return name.ToLower();
    }
    
    private static FirmwareMetadata ParseFirmwareMetadata(string tarMd5Path)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(tarMd5Path);
        fileName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        
        var parts = fileName.Split('_');
        
        var metadata = new FirmwareMetadata
        {
            FileName = fileName,
            Type = parts.FirstOrDefault() ?? "Unknown"
        };
        
        if (parts.Length > 1)
            metadata.Model = parts[1];
        
        return metadata;
    }
}

public class FirmwarePackage
{
    public Dictionary<string, byte[]> Partitions { get; set; } = new();
    public FirmwareMetadata Metadata { get; set; } = new();
    
    public byte[]? GetPartition(string name)
    {
        return Partitions.TryGetValue(name.ToLower(), out var data) ? data : null;
    }
    
    public IEnumerable<string> GetPartitionNames() => Partitions.Keys;
}

public class FirmwareMetadata
{
    public string FileName { get; set; } = "";
    public string Type { get; set; } = "";
    public string Model { get; set; } = "";
    public string Version { get; set; } = "";
    public string Region { get; set; } = "";
    public DateTime? BuildDate { get; set; }
}