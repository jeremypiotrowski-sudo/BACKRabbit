using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace BACKRabbit.Protocol.Fastboot;

/// <summary>
/// Android Sparse Image Parser
/// Handles the sparse image format used for fastboot flashing
/// </summary>
public class SparseImage
{
    public ushort Magic { get; set; }
    public ushort MajorVersion { get; set; }
    public ushort MinorVersion { get; set; }
    public ushort FileHeaderSize { get; set; }
    public ushort ChunkHeaderSize { get; set; }
    public uint BlockSize { get; set; }
    public uint TotalBlocks { get; set; }
    public uint TotalChunks { get; set; }
    public uint ImageChecksum { get; set; }
    
    public List<SparseChunk> Chunks { get; set; } = new();
    
    public static SparseImage Parse(byte[] data)
    {
        var sparse = new SparseImage();
        
        var header = MemoryMarshal.Read<SparseHeader>(data);
        
        if (header.magic != 0xED26)
            throw new InvalidDataException($"Invalid sparse image magic: 0x{header.magic:X4}");
        
        sparse.Magic = header.magic;
        sparse.MajorVersion = header.major_version;
        sparse.MinorVersion = header.minor_version;
        sparse.FileHeaderSize = header.file_header_size;
        sparse.ChunkHeaderSize = header.chunk_header_size;
        sparse.BlockSize = header.block_size;
        sparse.TotalBlocks = header.total_blocks;
        sparse.TotalChunks = header.total_chunks;
        sparse.ImageChecksum = header.image_checksum;
        
var offset = (int)sparse.FileHeaderSize;
        for (int i = 0; i < sparse.TotalChunks; i++)
        {
            if (offset + sparse.ChunkHeaderSize > data.Length) break;
            
            var chunkHeader = MemoryMarshal.Read<SparseChunkHeader>(data.AsSpan(offset));
            offset += sparse.ChunkHeaderSize;
            
            var chunk = new SparseChunk
            {
                Type = (SparseChunkType)chunkHeader.chunk_type,
                Reserved = chunkHeader.reserved,
                Blocks = chunkHeader.chunk_size,
                DataSize = chunkHeader.total_size - sparse.ChunkHeaderSize
            };
            
if (chunk.DataSize > 0 && offset + (int)chunk.DataSize <= data.Length)
            {
                chunk.Data = data.Skip(offset).Take((int)chunk.DataSize).ToArray();
                offset += (int)chunk.DataSize;
            }
            
            sparse.Chunks.Add(chunk);
        }
        
        return sparse;
    }
    
    public byte[] ToRawImage()
    {
        var rawSize = TotalBlocks * BlockSize;
        var raw = new byte[rawSize];
        var offset = 0UL;
        
        foreach (var chunk in Chunks)
        {
            switch (chunk.Type)
            {
                case SparseChunkType.Raw:
                    chunk.Data.CopyTo(raw, (int)offset);
                    offset += (ulong)chunk.Data.Length;
                    break;
                    
                case SparseChunkType.Fill:
                    var pattern = chunk.Data.Take(4).ToArray();
                    for (var i = 0; i < chunk.DataSize; i += 4)
                        pattern.CopyTo(raw, (int)offset + i);
                    offset += chunk.DataSize;
                    break;
                    
                case SparseChunkType.DontCare:
                    offset += (ulong)chunk.Blocks * BlockSize;
                    break;
            }
        }
        
        return raw;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SparseHeader
{
    public ushort magic;
    public ushort major_version;
    public ushort minor_version;
    public ushort file_header_size;
    public ushort chunk_header_size;
    public uint block_size;
    public uint total_blocks;
    public uint total_chunks;
    public uint image_checksum;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SparseChunkHeader
{
    public ushort chunk_type;
    public ushort reserved;
    public uint chunk_size;
    public uint total_size;
}

public class SparseChunk
{
    public SparseChunkType Type { get; set; }
    public ushort Reserved { get; set; }
    public uint Blocks { get; set; }
    public uint DataSize { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public enum SparseChunkType : ushort
{
    Raw = 0xCAC1,
    Fill = 0xCAC2,
    DontCare = 0xCAC3,
    Crc32 = 0xCAC4
}