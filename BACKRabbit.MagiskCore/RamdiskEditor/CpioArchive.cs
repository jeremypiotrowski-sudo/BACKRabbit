using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using BACKRabbit.MagiskCore.Structures.Ramdisk;

namespace BACKRabbit.MagiskCore.RamdiskEditor;

/// <summary>
/// CPIO archive parser/serializer - newc format (070701 magic)
/// Ported from Magisk's cpio.rs and boot_patch.sh logic
/// </summary>
public class CpioArchive
{
    public List<CpioEntry> Entries { get; set; } = new();
    public string DeviceName { get; set; } = "";
    public byte[]? RawData { get; set; }

    /// <summary>
    /// Parse CPIO archive from byte array
    /// </summary>
    public static CpioArchive Parse(byte[] data)
    {
        var archive = new CpioArchive { RawData = data };
        var offset = 0;

while (offset + CpioConstants.HEADER_SIZE <= data.Length)
        {
            // Read header manually (avoid MemoryMarshal.Read with non-blittable struct)
            var headerSpan = data.AsSpan(offset, CpioConstants.HEADER_SIZE);
            
            // Validate magic
            var magic = Encoding.ASCII.GetString(headerSpan.Slice(0, 6)).TrimEnd('\0');
            if (magic != "070701" && magic != "070702")
            {
                throw new InvalidDataException($"Invalid CPIO magic: {magic} at offset {offset}");
            }

            // Parse header fields from fixed positions
            var namesize = int.Parse(Encoding.ASCII.GetString(headerSpan.Slice(94, 8)).TrimEnd('\0'), 
                System.Globalization.NumberStyles.HexNumber);
            var filesize = uint.Parse(Encoding.ASCII.GetString(headerSpan.Slice(54, 8)).TrimEnd('\0'), 
                System.Globalization.NumberStyles.HexNumber);
            var mode = uint.Parse(Encoding.ASCII.GetString(headerSpan.Slice(14, 8)).TrimEnd('\0'), 
                System.Globalization.NumberStyles.HexNumber);

// Read filename (starts after header, null-terminated)
            var nameOffset = offset + CpioConstants.HEADER_SIZE;
            // Find null terminator within the namesize bytes
            var nameSpan = data.AsSpan(nameOffset, (int)namesize);
            var nullIndex = nameSpan.IndexOf((byte)0);
            var actualNameLength = nullIndex >= 0 ? nullIndex : nameSpan.Length;
            var nameBytes = nameSpan.Slice(0, actualNameLength);
            var name = Encoding.ASCII.GetString(nameBytes);

            // Check for trailer
            if (name == "TRAILER!!!")
            {
                break;
            }

// Read file data (align to 4 bytes)
            var dataOffset = nameOffset + (int)(((namesize + 3) / 4) * 4);
            // Allow dataOffset == data.Length for zero-size files (e.g., TRAILER!!!)
            if (dataOffset < 0 || (filesize > 0 && dataOffset >= data.Length))
            {
                throw new InvalidDataException($"CPIO entry data offset out of bounds at offset {offset}");
            }
            var fileData = new byte[filesize];
            if (filesize > 0 && dataOffset + (int)filesize <= data.Length)
            {
                Array.Copy(data, dataOffset, fileData, 0, (int)filesize);
            }

            // Create entry
            var entry = new CpioEntry
            {
                Name = name,
                Data = fileData,
                Mode = mode,
                Offset = offset
            };

            archive.Entries.Add(entry);

// Move to next entry (align to 4 bytes)
            var entrySize = CpioConstants.HEADER_SIZE + 
                           ((namesize + 3) / 4) * 4 + 
                           ((filesize + 3) / 4) * 4;
            offset += checked((int)entrySize);
        }

        // Detect device name from fstab files
        archive.DeviceName = archive.DetectDeviceName();

        return archive;
    }

    /// <summary>
    /// Serialize CPIO archive to byte array
    /// </summary>
    public byte[] Serialize()
    {
        using var ms = new MemoryStream();

        foreach (var entry in Entries)
        {
            WriteEntry(ms, entry);
        }

        // Write trailer
        WriteEntry(ms, new CpioEntry 
        { 
            Name = "TRAILER!!!", 
            Data = Array.Empty<byte>() 
        });

        return ms.ToArray();
    }

    private void WriteEntry(MemoryStream ms, CpioEntry entry)
    {
        var nameBytes = Encoding.ASCII.GetBytes(entry.Name + '\0');
        var namesize = ((nameBytes.Length + 3) / 4) * 4; // Align to 4
        var filesize = entry.Data.Length;
        var alignedSize = ((filesize + 3) / 4) * 4;

// Build header manually - CPIO newc format uses ASCII hex strings for all numeric fields
        var headerBytes = new byte[CpioConstants.HEADER_SIZE];
        var span = headerBytes.AsSpan();
        
        // Write magic (6 bytes)
        "070701"u8.ToArray().CopyTo(span.Slice(0, 6));
        // ino (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(6, 8));
        // mode (8 bytes)
        WriteHexField(entry.Mode).CopyTo(span.Slice(14, 8));
        // uid (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(22, 8));
        // gid (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(30, 8));
        // nlink (8 bytes)
        "00000001"u8.ToArray().CopyTo(span.Slice(38, 8));
        // mtime (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(46, 8));
        // filesize (8 bytes)
        WriteHexField((uint)filesize).CopyTo(span.Slice(54, 8));
        // maj (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(62, 8));
        // min (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(70, 8));
        // rmaj (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(78, 8));
        // rmin (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(86, 8));
        // namesize (8 bytes)
        WriteHexField((uint)namesize).CopyTo(span.Slice(94, 8));
        // chksum (8 bytes)
        "00000000"u8.ToArray().CopyTo(span.Slice(102, 8));

        // Write header
        ms.Write(headerBytes, 0, headerBytes.Length);

        // Write name (aligned)
        ms.Write(nameBytes, 0, nameBytes.Length);
        if (nameBytes.Length < namesize)
        {
            ms.Write(new byte[namesize - nameBytes.Length], 0, namesize - nameBytes.Length);
        }

        // Write data (aligned)
        if (filesize > 0)
        {
            ms.Write(entry.Data, 0, filesize);
            if (filesize < alignedSize)
            {
                ms.Write(new byte[alignedSize - filesize], 0, alignedSize - filesize);
            }
        }
    }

    /// <summary>
    /// Write a uint value as 8-character ASCII hex string
    /// </summary>
    private static byte[] WriteHexField(uint value)
    {
        var hexString = value.ToString("X8");
        return Encoding.ASCII.GetBytes(hexString);
    }

    /// <summary>
    /// Get entry by name (supports partial match)
    /// </summary>
    public CpioEntry? GetEntry(string name)
    {
        return Entries.FirstOrDefault(e => e.Name.EndsWith(name) || e.Name == name);
    }

    /// <summary>
    /// Get all entries in a directory
    /// </summary>
    public List<CpioEntry> GetDirectory(string dir)
    {
        return Entries.Where(e => e.Name.StartsWith(dir + "/") || e.Name == dir).ToList();
    }

    /// <summary>
    /// Replace entry data
    /// </summary>
    public void ReplaceEntry(string name, byte[] newData)
    {
        var entry = GetEntry(name);
        if (entry != null)
        {
            entry.Data = newData;
        }
        else
        {
            Entries.Add(new CpioEntry { Name = name, Data = newData });
        }
    }

    /// <summary>
    /// Remove entry by name
    /// </summary>
    public void RemoveEntry(string name)
    {
        var entry = GetEntry(name);
        if (entry != null)
        {
            Entries.Remove(entry);
        }
    }

    /// <summary>
    /// Remove entire directory
    /// </summary>
    public void RemoveDirectory(string dir)
    {
        Entries.RemoveAll(e => e.Name.StartsWith(dir + "/") || e.Name == dir);
    }

    /// <summary>
    /// Clone archive (deep copy)
    /// </summary>
    public CpioArchive Clone()
    {
        return new CpioArchive
        {
            Entries = Entries.Select(e => e.Clone()).ToList(),
            DeviceName = DeviceName,
            RawData = RawData?.ToArray()
        };
    }

    /// <summary>
    /// Detect device name from fstab files
    /// </summary>
    private string DetectDeviceName()
    {
        var fstab = Entries.FirstOrDefault(e => e.Name.StartsWith("fstab."));
        if (fstab != null)
        {
            return fstab.Name.Substring("fstab.".Length).Split('.').First();
        }
        return "";
    }

    /// <summary>
    /// Find Magisk artifacts
    /// </summary>
    public MagiskArtifacts FindMagiskArtifacts()
    {
        var artifacts = new MagiskArtifacts();

        foreach (var entry in Entries)
        {
            if (entry.Name.Contains("overlay.d/sbin/"))
            {
                artifacts.OverlayDSbin.Add(entry.Name);
            }
            if (entry.Name.StartsWith(".backup/"))
            {
                artifacts.BackupFiles.Add(entry.Name);
            }
            if (entry.Name == "ramdisk.cpio.orig")
            {
                artifacts.HasFullBackup = true;
            }
            if (entry.Name == "init.magisk.rc")
            {
                artifacts.HasMagiskRc = true;
            }
        }

        artifacts.IsMagiskInstalled = artifacts.OverlayDSbin.Count > 0 || 
                                      artifacts.HasFullBackup || 
                                      artifacts.HasMagiskRc;

        return artifacts;
    }
}

/// <summary>
/// CPIO entry representation
/// </summary>
public class CpioEntry
{
    public string Name { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public uint Mode { get; set; } = 0x81A4; // Regular file, 0644
    public int Offset { get; set; }

    public CpioEntry Clone()
    {
        return new CpioEntry
        {
            Name = Name,
            Data = Data.ToArray(),
            Mode = Mode,
            Offset = Offset
        };
    }

    public byte[] GetData() => Data;
    public void SetData(byte[] data) => Data = data;
    public string GetString() => Encoding.UTF8.GetString(Data);
    public void SetString(string text) => Data = Encoding.UTF8.GetBytes(text);
}

/// <summary>
/// Magisk artifacts found in ramdisk
/// </summary>
public class MagiskArtifacts
{
    public bool IsMagiskInstalled { get; set; }
    public bool HasFullBackup { get; set; }
    public bool HasMagiskRc { get; set; }
    public List<string> OverlayDSbin { get; set; } = new();
    public List<string> BackupFiles { get; set; } = new();
}