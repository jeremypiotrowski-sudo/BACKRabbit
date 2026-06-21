# Companion Documentation: BACKRabbit.MagiskCore.RamdiskEditor.CpioArchive.cs

## Purpose
Complete CPIO archive parser and serializer for the `newc` (070701) format used in Android ramdisks. Includes Magisk artifact detection, device identification, and full entry manipulation API for ramdisk patching.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                          CpioArchive                                │
├─────────────────────────────────────────────────────────────────────┤
│  Parse(byte[]) ──→ List<CpioEntry>                                 │
│       │                                                             │
│       ├─→ Validate magic ("070701"/"070702")                       │
│       ├─→ Parse ASCII hex fields (ino, mode, uid, gid, nlink,      │
│       │    mtime, filesize, maj, min, rmaj, rmin, namesize,        │
│       │    chksum)                                                  │
│       ├─→ Read filename (null-terminated, 4-byte aligned)          │
│       ├─→ Check for TRAILER!!!                                      │
│       ├─→ Read file data (4-byte aligned)                          │
│       └─→ Advance offset (4-byte aligned)                          │
│                                                                     │
│  Serialize() ──→ byte[]                                             │
│       │                                                             │
│       ├─→ For each entry: WriteEntry()                             │
│       ├─→ Build 110-byte header with ASCII hex fields              │
│       ├─→ Write name (null-terminated, padded to 4 bytes)          │
│       ├─→ Write data (padded to 4 bytes)                           │
│       └─→ Write TRAILER!!! entry                                   │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Static Factory
```csharp
/// <summary>
/// Parse CPIO archive from byte array
/// </summary>
/// <param name="data">Raw CPIO data (decompressed)</param>
/// <returns>Parsed CpioArchive with entries</returns>
/// <exception cref="InvalidDataException">Invalid magic or corrupt data</exception>
public static CpioArchive Parse(byte[] data)
```

### Serialization
```csharp
/// <summary>
/// Serialize archive to byte array (newc format)
/// </summary>
/// <returns>Raw CPIO data ready for compression</returns>
public byte[] Serialize()
```

### Entry Access
```csharp
/// <summary>
/// Get entry by name (exact match or ends-with)
/// </summary>
public CpioEntry? GetEntry(string name)

/// <summary>
/// Get all entries under a directory
/// </summary>
public List<CpioEntry> GetDirectory(string dir)

/// <summary>
/// Replace entry data (or add new if not found)
/// </summary>
public void ReplaceEntry(string name, byte[] newData)

/// <summary>
/// Remove entry by name
/// </summary>
public void RemoveEntry(string name)

/// <summary>
/// Remove entire directory tree
/// </summary>
public void RemoveDirectory(string dir)
```

### Utility
```csharp
/// <summary>
/// Deep clone archive
/// </summary>
public CpioArchive Clone()

/// <summary>
/// Detect device name from fstab files
/// </summary>
public string DeviceName { get; }

/// <summary>
/// Find Magisk installation artifacts
/// </summary>
public MagiskArtifacts FindMagiskArtifacts()
```

## CPIO newc Format Specification

### Header Structure (110 bytes fixed)
```
Offset  Size  Field       Format          Description
0       6     magic       ASCII           "070701" (newc) or "070702" (crc)
6       8     ino         ASCII hex       Inode number (unused, 0)
14      8     mode        ASCII hex       File mode (e.g., 0x81A4 = 0644)
22      8     uid         ASCII hex       User ID (0)
30      8     gid         ASCII hex       Group ID (0)
38      8     nlink       ASCII hex       Link count (1)
46      8     mtime       ASCII hex       Modification time (0)
54      8     filesize    ASCII hex       File data size in bytes
62      8     maj         ASCII hex       Major device number (0)
70      8     min         ASCII hex       Minor device number (0)
78      8     rmaj        ASCII hex       Major for special files (0)
86      8     rmin        ASCII hex       Minor for special files (0)
94      8     namesize    ASCII hex       Filename length (incl. null, 4-byte aligned)
102     8     chksum      ASCII hex       Checksum (0, not used)
```

### Data Layout
```
[Header: 110 bytes]
[Filename: namesize bytes, null-terminated, padded to 4-byte boundary]
[File Data: filesize bytes, padded to 4-byte boundary]
[Next Header...]
...
[TRAILER!!! Header + name + padding]  // Terminator entry
```

### Alignment Rules
- `namesize` includes null terminator, rounded up to 4 bytes
- `filesize` rounded up to 4 bytes for data padding
- Header is NOT aligned (fixed 110 bytes)
- `align4(x) = (x + 3) & ~3`

## Data Structures

### CpioArchive
```csharp
public class CpioArchive {
    public List<CpioEntry> Entries { get; set; } = new();
    public string DeviceName { get; set; } = "";
    public byte[]? RawData { get; set; }
}
```

### CpioEntry
```csharp
public class CpioEntry {
    public string Name { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public uint Mode { get; set; } = 0x81A4;  // Regular file, 0644
    public int Offset { get; set; }            // Offset in original archive

    // Helpers
    public CpioEntry Clone() { ... }
    public byte[] GetData() => Data;
    public void SetData(byte[] data) => Data = data;
    public string GetString() => Encoding.UTF8.GetString(Data);
    public void SetString(string text) => Data = Encoding.UTF8.GetBytes(text);
}
```

### MagiskArtifacts
```csharp
public class MagiskArtifacts {
    public bool IsMagiskInstalled { get; set; }
    public bool HasFullBackup { get; set; }      // ramdisk.cpio.orig exists
    public bool HasMagiskRc { get; set; }        // init.magisk.rc exists
    public List<string> OverlayDSbin { get; set; } = new();  // overlay.d/sbin/*
    public List<string> BackupFiles { get; set; } = new();   // .backup/*
}
```

## Parsing Algorithm (Parse)

```csharp
// 1. Start at offset 0
// 2. While offset + 110 <= data.Length:
//    a. Read 110-byte header
//    b. Validate magic == "070701" or "070702"
//    c. Parse ASCII hex fields:
//       namesize = ParseHex(header[94..102])
//       filesize = ParseHex(header[54..62])
//       mode     = ParseHex(header[14..22])
//    d. Read filename at offset + 110, length namesize
//       Find null terminator within namesize
//    e. If name == "TRAILER!!!" → break
//    f. Calculate data_offset = offset + 110 + align4(namesize)
//    g. Read filesize bytes from data_offset
//    h. Create CpioEntry
//    i. Advance offset += 110 + align4(namesize) + align4(filesize)
// 3. Detect device name from fstab.* entries
// 4. Return CpioArchive
```

## Serialization Algorithm (Serialize)

```csharp
// 1. Create MemoryStream
// 2. For each entry in Entries:
//    a. WriteEntry(ms, entry)
// 3. Write TRAILER!!! entry
// 4. Return ms.ToArray()

// WriteEntry:
// 1. nameBytes = Encoding.ASCII.GetBytes(name + '\0')
// 2. namesize = align4(nameBytes.Length)
// 3. filesize = entry.Data.Length
// 4. alignedSize = align4(filesize)
// 5. Build 110-byte header:
//    - magic: "070701"
//    - ino: "00000000"
//    - mode: WriteHexField(entry.Mode)
//    - uid/gid/nlink/mtime: "00000000"
//    - filesize: WriteHexField(filesize)
//    - maj/min/rmaj/rmin: "00000000"
//    - namesize: WriteHexField(namesize)
//    - chksum: "00000000"
// 6. Write header
// 7. Write nameBytes + padding to namesize
// 8. Write entry.Data + padding to alignedSize
```

## Magisk Artifact Detection

### What It Detects
| Artifact | Pattern | Significance |
|----------|---------|--------------|
| **overlay.d/sbin/** | `entry.Name.Contains("overlay.d/sbin/")` | Magisk modules active |
| **.backup/** | `entry.Name.StartsWith(".backup/")` | Magisk backup of original files |
| **ramdisk.cpio.orig** | `entry.Name == "ramdisk.cpio.orig"` | Full ramdisk backup (uninstall possible) |
| **init.magisk.rc** | `entry.Name == "init.magisk.rc"` | Magisk init script injected |

### Installation Status
```csharp
IsMagiskInstalled = OverlayDSbin.Count > 0 || HasFullBackup || HasMagiskRc
```

## Device Name Detection

Scans for `fstab.*` files and extracts device codename:
```csharp
// fstab.gta4xl → "gta4xl"
// fstab.sm8250 → "sm8250"
var fstab = Entries.FirstOrDefault(e => e.Name.StartsWith("fstab."));
if (fstab != null) {
    DeviceName = fstab.Name.Substring("fstab.".Length).Split('.').First();
}
```

## Usage Examples

### Parse and Inspect Ramdisk
```csharp
var parser = new BootImageParser();
var bootImage = parser.Parse("boot.img");

// Extract and parse ramdisk
var ramdiskArchive = parser.ExtractRamdiskArchive(bootImage);

Console.WriteLine($"Entries: {ramdiskArchive.Entries.Count}");
Console.WriteLine($"Device: {ramdiskArchive.DeviceName}");

// Check for Magisk
var artifacts = ramdiskArchive.FindMagiskArtifacts();
if (artifacts.IsMagiskInstalled) {
    Console.WriteLine("Magisk detected!");
    Console.WriteLine($"  Overlay d/sbin: {artifacts.OverlayDSbin.Count} files");
    Console.WriteLine($"  Backup files: {artifacts.BackupFiles.Count}");
    Console.WriteLine($"  Full backup: {artifacts.HasFullBackup}");
    Console.WriteLine($"  init.magisk.rc: {artifacts.HasMagiskRc}");
}
```

### Modify Ramdisk (fstab example)
```csharp
var ramdiskArchive = parser.ExtractRamdiskArchive(bootImage);

// Find and modify fstab
var fstab = ramdiskArchive.GetEntry("fstab.default");
if (fstab != null) {
    var content = fstab.GetString();
    // Make /system writable
    content = content.Replace("ro,", "rw,");
    // Disable verity
    content = content.Replace("verify=", "# verify=");
    fstab.SetString(content);
}

// Or add a new file
ramdiskArchive.ReplaceEntry("init.myscript.rc", 
    Encoding.UTF8.GetBytes("on init\n    log -t test \"Hello\""));

// Repack
var repacker = new BootImageRepacker();
var newImage = repacker.RepackWithRamdisk(bootImage, ramdiskArchive);
File.WriteAllBytes("boot_patched.img", newImage);
```

### Remove Magisk (Uninstall)
```csharp
var ramdiskArchive = parser.ExtractRamdiskArchive(bootImage);
var artifacts = ramdiskArchive.FindMagiskArtifacts();

if (artifacts.HasFullBackup) {
    // Restore from ramdisk.cpio.orig
    var backupEntry = ramdiskArchive.GetEntry("ramdisk.cpio.orig");
    if (backupEntry != null) {
        var originalArchive = CpioArchive.Parse(backupEntry.Data);
        var newImage = repacker.RepackWithRamdisk(bootImage, originalArchive);
        File.WriteAllBytes("boot_unpatched.img", newImage);
    }
} else {
    // Manual removal
    ramdiskArchive.RemoveDirectory("overlay.d");
    ramdiskArchive.RemoveDirectory(".backup");
    ramdiskArchive.RemoveEntry("init.magisk.rc");
    ramdiskArchive.RemoveEntry("ramdisk.cpio.orig");
    
    var newImage = repacker.RepackWithRamdisk(bootImage, ramdiskArchive);
    File.WriteAllBytes("boot_clean.img", newImage);
}
```

### Round-Trip Test
```csharp
// Parse → Serialize → Parse
var original = parser.ExtractRamdiskArchive(bootImage);
var serialized = original.Serialize();
var reparsed = CpioArchive.Parse(serialized);

// Verify
Assert.AreEqual(original.Entries.Count, reparsed.Entries.Count);
for (int i = 0; i < original.Entries.Count; i++) {
    Assert.AreEqual(original.Entries[i].Name, reparsed.Entries[i].Name);
    Assert.AreEqual(original.Entries[i].Data, reparsed.Entries[i].Data);
    Assert.AreEqual(original.Entries[i].Mode, reparsed.Entries[i].Mode);
}
```

## Magisk Version Compatibility

| Magisk Version | CPIO Features | BACKRabbit Status |
|----------------|---------------|-------------------|
| v25.0 | Shell-based (boot_patch.sh) | ✅ Compatible |
| v25.1-v25.2 | Shell-based | ✅ Compatible |
| v26.0 | Rust cpio.rs (newc) | ✅ Full |
| v26.1-v26.4 | Rust cpio.rs (newc + crc parse) | ✅ Full* |
| v27.0 | Same | ✅ Full* |

*BACKRabbit serializes as newc only (070701)

## Limitations & Known Gaps

1. **CRC Format (070702)** - Parses but serializes as newc (070701)
2. **Old Binary CPIO** - Not supported (pre-POSIX)
3. **Symlinks** - Mode parsed but not preserved as symlinks on extract
4. **Hardlinks** - ino field parsed but not resolved
5. **Checksum** - chksum field always 0 (Magisk behavior)
6. **Extended Attributes** - Not supported (xattr)
7. **Large Files** - 32-bit filesize limit (4GB)

## Related Files

| File | Relationship |
|------|--------------|
| `BootImageParser.cs` | `ExtractRamdiskArchive()` uses this |
| `BootImageRepacker.cs` | `RepackWithRamdisk()` uses this |
| `FormatDetector.cs` | Detects CPIO format before parsing |
| `CompressionEngine.cs` | Decompresses ramdisk before CPIO parse |
| `MagiskArtifactDetector.cs` | Higher-level Magisk detection |

## References

1. **Magisk cpio.rs** - `native/src/boot/cpio.rs` (v26.3 reference)
2. **Magisk rootdir.cpp** - `native/src/init/rootdir.cpp` (artifact detection)
3. **Magisk boot_patch.sh** - `scripts/boot_patch.sh` (original shell impl)
4. **CPIO man page** - `man 5 cpio` (format specification)
5. **AOSP cpio.h** - `system/core/cpio/cpio.h` (header constants)

## Testing Recommendations

### Unit Tests
```csharp
[Test] void Parse_EmptyArchive_ReturnsEmptyEntries()
[Test] void Parse_SingleFile_CreatesCorrectEntry()
[Test] void Parse_DirectoryTree_PreservesStructure()
[Test] void Parse_TrailerOnly_StopsParsing()
[Test] void Serialize_RoundTrip_ProducesIdenticalData()
[Test] void Parse_AlignmentEdgeCases_HandlesCorrectly()
[Test] void Parse_ZeroSizeFile_HandlesCorrectly()
[Test] void FindMagiskArtifacts_DetectsOverlay()
[Test] void FindMagiskArtifacts_DetectsBackup()
[Test] void FindMagiskArtifacts_DetectsFullBackup()
[Test] void FindMagiskArtifacts_DetectsMagiskRc()
[Test] void DetectDeviceName_FromFstab_ReturnsCodename()
[Test] void ReplaceEntry_UpdatesData()
[Test] void RemoveEntry_RemovesFile()
[Test] void RemoveDirectory_RemovesTree()
[Test] void Clone_CreatesIndependentCopy()
```

### Test Data
Create test CPIO archives with:
```bash
# Simple archive
mkdir -p test_ramdisk/{system,etc,overlay.d/sbin}
echo "test" > test_ramdisk/file.txt
echo "/dev/block/platform/... /system ext4 ro" > test_ramdisk/fstab.device
find test_ramdisk | cpio -o -H newc > test.cpio

# Magisk-patched
cp -r test_ramdisk test_magisk
mkdir -p test_magisk/overlay.d/sbin
echo "magisk" > test_magisk/overlay.d/sbin/magisk
echo "backup" > test_magisk/.backup/fstab.device
echo "orig" > test_magisk/ramdisk.cpio.orig
find test_magisk | cpio -o -H newc > test_magisk.cpio
```

## Cross-Reference
See `knowledge-base/cross-reference-map/CpioArchive.cs.md` for line-by-line Magisk source mapping.