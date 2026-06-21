using System.Runtime.InteropServices;

namespace BACKRabbit.Protocol.DownloadMode;

/// <summary>
/// PIT (Partition Information Table) file parser
/// Samsung's partition layout format
/// </summary>
public class PitFile
{
    public uint HeaderSize { get; set; }
    public uint EntryCount { get; set; }
    public List<PitEntry> Entries { get; set; } = new();
    
    /// <summary>
    /// Parse PIT from byte array
    /// </summary>
    public static PitFile Parse(byte[] data)
    {
        var pit = new PitFile();
        
        // Read header
        var header = MemoryMarshal.Read<PitHeader>(data);
        pit.HeaderSize = header.header_size;
        pit.EntryCount = header.entry_count;
        
        // Read entries
        var entryOffset = (int)header.header_size;
        var entrySize = Marshal.SizeOf<PitEntryRaw>();
        
        for (int i = 0; i < header.entry_count; i++)
        {
            if (entryOffset + i * entrySize + entrySize > data.Length)
                break;
                
            var entryData = data.Skip(entryOffset + i * entrySize).Take(entrySize).ToArray();
            var entryRaw = MemoryMarshal.Read<PitEntryRaw>(entryData);
            
            var entry = new PitEntry
            {
                EntryType = entryRaw.entry_type,
                PartitionId = entryRaw.partition_id,
                PartitionName = System.Text.Encoding.ASCII.GetString(entryRaw.partition_name).TrimEnd('\0'),
                FlashFilename = System.Text.Encoding.ASCII.GetString(entryRaw.flash_filename).TrimEnd('\0'),
                BlockCount = entryRaw.block_count,
                BlockSize = entryRaw.block_size,
                Offset = entryRaw.offset,
                Attributes = entryRaw.attributes,
                UpdateAttributes = entryRaw.update_attributes,
                Flags = entryRaw.flags
            };
            
            pit.Entries.Add(entry);
        }
        
        return pit;
    }
    
    /// <summary>
    /// Get partition by name
    /// </summary>
    public PitEntry? GetPartition(string name)
    {
        return Entries.FirstOrDefault(e => e.PartitionName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Get all partitions
    /// </summary>
    public IEnumerable<PitEntry> GetPartitions()
    {
        return Entries.Where(e => e.EntryType == 0); // 0 = partition
    }
    
    /// <summary>
    /// Serialize PIT to byte array
    /// </summary>
    public byte[] Serialize()
    {
        var entrySize = Marshal.SizeOf<PitEntryRaw>();
        var totalSize = (int)HeaderSize + Entries.Count * entrySize;
        var data = new byte[totalSize];
        
        // Write header
        var header = new PitHeader
        {
            header_size = HeaderSize,
            entry_count = (uint)Entries.Count,
            unknown = 0x1234BAAD
        };
        
        var headerBytes = new byte[Marshal.SizeOf<PitHeader>()];
        MemoryMarshal.Write(headerBytes.AsSpan(), ref header);
        headerBytes.CopyTo(data, 0);
        
        // Write entries
        for (int i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            var entryRaw = new PitEntryRaw
            {
                entry_type = entry.EntryType,
                partition_id = entry.PartitionId,
                partition_name = System.Text.Encoding.ASCII.GetBytes(entry.PartitionName.PadRight(64, '\0')),
                flash_filename = System.Text.Encoding.ASCII.GetBytes(entry.FlashFilename.PadRight(64, '\0')),
                block_count = entry.BlockCount,
                block_size = entry.BlockSize,
                offset = entry.Offset,
                attributes = entry.Attributes,
                update_attributes = entry.UpdateAttributes,
                flags = entry.Flags
            };
            
            var entryBytes = new byte[entrySize];
            MemoryMarshal.Write(entryBytes.AsSpan(), ref entryRaw);
            entryBytes.CopyTo(data, (int)HeaderSize + i * entrySize);
        }
        
        return data;
    }
}

/// <summary>
/// PIT Entry
/// </summary>
public class PitEntry
{
    public uint EntryType { get; set; }
    public uint PartitionId { get; set; }
    public string PartitionName { get; set; } = "";
    public string FlashFilename { get; set; } = "";
    public uint BlockCount { get; set; }
    public uint BlockSize { get; set; }
    public uint Offset { get; set; }
    public uint Attributes { get; set; }
    public uint UpdateAttributes { get; set; }
    public uint Flags { get; set; }
    
    public ulong Size => (ulong)BlockCount * BlockSize;
    
    public override string ToString() => $"{PartitionName}: {Size} bytes (offset: {Offset})";
}

/// <summary>
/// PIT Header structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PitHeader
{
    public uint header_size;
    public uint unknown;
    public uint entry_count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 252)] public byte[] reserved;
}

/// <summary>
/// PIT Entry raw structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PitEntryRaw
{
    public uint entry_type;
    public uint partition_id;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] partition_name;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] flash_filename;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] fota_filename;
    public uint block_count;
    public uint block_size;
    public uint offset;
    public uint attributes;
    public uint update_attributes;
    public uint flags;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] reserved;
}

/// <summary>
/// Firmware package for flashing
/// </summary>
public class FirmwarePackage
{
    public string Model { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, byte[]> Partitions { get; set; } = new();
    
    /// <summary>
    /// Get partition data by name
    /// </summary>
    public byte[]? GetPartition(string name)
    {
        Partitions.TryGetValue(name, out var data);
        return data;
    }
    
    /// <summary>
    /// Add partition from file
    /// </summary>
    public void AddPartition(string name, string filePath)
    {
        if (File.Exists(filePath))
        {
            Partitions[name] = File.ReadAllBytes(filePath);
        }
    }
    
    /// <summary>
    /// Add partition from byte array
    /// </summary>
    public void AddPartition(string name, byte[] data)
    {
        Partitions[name] = data;
    }
    
    /// <summary>
    /// Get list of partition names
    /// </summary>
    public List<string> GetPartitionNames() => Partitions.Keys.ToList();
}