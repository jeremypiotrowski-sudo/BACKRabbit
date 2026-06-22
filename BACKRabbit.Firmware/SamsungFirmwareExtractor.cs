using SharpCompress.Archives.Tar;
using SharpCompress.Common;
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
        
        // Try raw TAR first (older firmware), then lz4 decompression (2020+ Samsung firmware)
        // SharpCompress TarArchive.Open() doesn't throw on invalid data — it returns empty.
        // We detect failure by checking if any entries were extracted.
        ExtractPartitionsFromTar(fs, fs.Length - 16, partitions);
        
        if (partitions.Count == 0)
        {
            // Raw TAR produced nothing — try lz4 decompression (2020+ Samsung firmware)
            // Samsung uses raw lz4 blocks without frame headers, so we decompress
            // the entire payload into memory first, then open the TAR from bytes.
            fs.Seek(0, SeekOrigin.Begin);
            var compressedLength = fs.Length - 16;
            var compressed = new byte[compressedLength];
            fs.ReadExactly(compressed, 0, (int)compressedLength);
            
            try
            {
                var estimatedSize = compressed.Length * 10;
                var decompressed = new byte[estimatedSize];
                var decoded = K4os.Compression.LZ4.LZ4Codec.Decode(
                    compressed.AsSpan(), decompressed.AsSpan());
                if (decoded < 0)
                    throw new InvalidDataException("LZ4 decompression failed");
                using var ms = new MemoryStream(decompressed, 0, decoded);
                using var archive = TarArchive.Open(ms);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        using var entryStream = entry.OpenEntryStream();
                        using var memStream = new MemoryStream();
                        entryStream.CopyTo(memStream);
                        
                        var partitionName = GetPartitionName(entry.Key);
                        partitions[partitionName] = memStream.ToArray();
                    }
                }
            }
            catch
            {
                // lz4 decompression failed — file may use a different compression format
            }
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
            if (disposing) _baseStream.Dispose();
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
    
    private static bool ExtractPartitionsFromTar(FileStream fs, long length, Dictionary<string, byte[]> partitions)
    {
        try
        {
            using var limitedStream = new LimitedStream(fs, length);
            using var archive = TarArchive.Open(limitedStream);
            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var memStream = new MemoryStream();
                    entryStream.CopyTo(memStream);
                    
                    var partitionName = GetPartitionName(entry.Key);
                    partitions[partitionName] = memStream.ToArray();
                }
            }
            return partitions.Count > 0;
        }
        catch
        {
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