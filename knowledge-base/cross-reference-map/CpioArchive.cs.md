# Cross-Reference Map: BACKRabbit.MagiskCore.RamdiskEditor.CpioArchive.cs ↔ Magisk/AOSP Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/RamdiskEditor/CpioArchive.cs` |
| **Magisk Source** | `native/src/rootdir.rs`, `scripts/boot_patch.sh` (cpio functions) |
| **AOSP Source** | `system/core/cpio/mkbootfs.c`, `system/core/include/cpio.h` |
| **Total Lines (BACKRabbit)** | 340 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v27.0 (native/src/rootdir.rs)
| BACKRabbit Line(s) | Magisk v26.x Line(s) | Function |
|---|---|---|
| 21-96 | ~50-150 | `RootDir::parse()` / `Cpio::parse()` - parse newc format |
| 98-118 | ~150-180 | `serialize()` / `to_bytes()` - serialize to newc |
| 120-189 | ~180-250 | `WriteEntry()` - header construction |
| 190-198 | ~250-260 | `WriteHexField()` - ASCII hex encoding |
| 193-220 | ~260-280 | Entry management (GetEntry, GetDirectory, ReplaceEntry, RemoveEntry, RemoveDirectory) |
| 222-253 | ~280-300 | `Clone()` - deep copy |
| 255-266 | ~300-310 | `DetectDeviceName()` - from fstab |
| 268-300 | ~310-350 | `FindMagiskArtifacts()` - Magisk detection |

### Magisk cpio.rs (v26.3)
| BACKRabbit Line(s) | cpio.rs Line(s) | Notes |
|---|---|---|
| 21-96 | ~100-200 | `RootDir::parse()` - manual header parsing (no MemoryMarshal) |
| 98-118 | ~200-230 | `serialize()` - writes TRAILER!!! |
| 120-189 | ~230-300 | `write_entry()` - manual header building |
| 190-198 | ~300-310 | `write_hex()` - ASCII hex |
| 193-220 | ~310-340 | Entry CRUD operations |
| 222-253 | ~340-360 | `clone()` |
| 255-266 | ~360-370 | `detect_device_name()` |
| 268-300 | ~370-410 | `find_magisk_artifacts()` |

---

## Detailed Function Mapping

### Parse() - Lines 21-96
**Magisk Equivalent:** `RootDir::parse()` in rootdir.rs

```rust
// Magisk v26.3 (rootdir.rs ~line 100)
pub fn parse(data: &[u8]) -> RootDir {
    let mut entries = Vec::new();
    let mut offset = 0;
    
    while offset + CPIO_HEADER_SIZE <= data.len() {
        // Validate magic
        let magic = str::from_utf8(&data[offset..offset+6]).unwrap().trim_end_matches('\0');
        if magic != "070701" && magic != "070702" {
            break; // Invalid or end
        }
        
        // Parse ASCII hex fields
        let namesize = u32::from_str_radix(
            str::from_utf8(&data[offset+94..offset+102]).unwrap().trim_end_matches('\0'), 
            16
        ).unwrap();
        
        let filesize = u32::from_str_radix(
            str::from_utf8(&data[offset+54..offset+62]).unwrap().trim_end_matches('\0'), 
            16
        ).unwrap();
        
        // Read name (null-terminated within namesize)
        let name_offset = offset + CPIO_HEADER_SIZE;
        let name_end = data[name_offset..name_offset+namesize as usize]
            .iter().position(|&b| b == 0)
            .unwrap_or(namesize as usize);
        let name = str::from_utf8(&data[name_offset..name_offset+name_end]).unwrap();
        
        // TRAILER!!! check
        if name == "TRAILER!!!" {
            break;
        }
        
        // Data offset (4-byte aligned)
        let data_offset = name_offset + ((namesize + 3) / 4) * 4;
        let file_data = if filesize > 0 {
            data[data_offset..data_offset+filesize as usize].to_vec()
        } else {
            Vec::new()
        };
        
        entries.push(CpioEntry { name, data: file_data, mode, offset });
        
        // Next entry (4-byte aligned)
        offset += CPIO_HEADER_SIZE + ((namesize + 3) / 4) * 4 + ((filesize + 3) / 4) * 4;
    }
    
    RootDir { entries }
}
```

**BACKRabbit mapping:** Lines 26-90 direct port with manual header parsing (avoids MemoryMarshal for non-blittable).

### Serialize() - Lines 98-118
**Magisk Equivalent:** `RootDir::serialize()` / `to_bytes()` in rootdir.rs

```rust
// Magisk v26.3 (rootdir.rs ~line 150)
pub fn serialize(&self) -> Vec<u8> {
    let mut buf = Vec::new();
    
    for entry in &self.entries {
        write_entry(&mut buf, entry);
    }
    
    // TRAILER!!!
    write_entry(&mut buf, &CpioEntry { 
        name: "TRAILER!!!", 
        data: Vec::new(), 
        mode: 0 
    });
    
    buf
}
```

**BACKRabbit mapping:** Lines 103-117 direct port.

### WriteEntry() - Lines 120-179
**Magisk Equivalent:** `write_entry()` in rootdir.rs

```rust
// Magisk v26.3 (rootdir.rs ~line 230)
fn write_entry(buf: &mut Vec<u8>, entry: &CpioEntry) {
    let name = entry.name.as_bytes();
    let name_with_null = [name, &[0]].concat();
    let namesize = align_up(name_with_null.len(), 4);
    let filesize = entry.data.len();
    let aligned_size = align_up(filesize, 4);
    
    // Build header (110 bytes)
    let mut header = vec![0; CPIO_HEADER_SIZE];
    
    // magic: "070701"
    header[0..6].copy_from_slice(b"070701");
    
    // ino: 0
    write_hex(&mut header[6..14], 0);
    
    // mode
    write_hex(&mut header[14..22], entry.mode);
    
    // uid: 0, gid: 0
    write_hex(&mut header[22..30], 0);
    write_hex(&mut header[30..38], 0);
    
    // nlink: 1
    write_hex(&mut header[38..46], 1);
    
    // mtime: 0
    write_hex(&mut header[46..54], 0);
    
    // filesize
    write_hex(&mut header[54..62], filesize as u32);
    
    // maj: 0, min: 0, rmaj: 0, rmin: 0
    for i in [62, 70, 78, 86] {
        write_hex(&mut header[i..i+8], 0);
    }
    
    // namesize
    write_hex(&mut header[94..102], namesize as u32);
    
    // chksum: 0
    write_hex(&mut header[102..110], 0);
    
    buf.extend_from_slice(&header);
    buf.extend_from_slice(&name_with_null);
    buf.extend(vec![0; namesize - name_with_null.len()]);
    buf.extend_from_slice(&entry.data);
    buf.extend(vec![0; aligned_size - filesize]);
}
```

**BACKRabbit mapping:** Lines 127-178 direct port.

---

## CPIO newc Format (070701)

### Header Structure (110 bytes)
| Offset | Size | Field | Format |
|--------|------|-------|--------|
| 0 | 6 | magic | "070701" ASCII |
| 6 | 8 | ino | ASCII hex |
| 14 | 8 | mode | ASCII hex |
| 22 | 8 | uid | ASCII hex |
| 30 | 8 | gid | ASCII hex |
| 38 | 8 | nlink | ASCII hex |
| 46 | 8 | mtime | ASCII hex |
| 54 | 8 | filesize | ASCII hex |
| 62 | 8 | maj | ASCII hex |
| 70 | 8 | min | ASCII hex |
| 78 | 8 | rmaj | ASCII hex |
| 86 | 8 | rmin | ASCII hex |
| 94 | 8 | namesize | ASCII hex |
| 102 | 8 | chksum | ASCII hex |

### Alignment Rules
- **namesize**: Rounded up to 4 bytes (includes null terminator)
- **filesize**: Rounded up to 4 bytes
- **Next entry**: Starts at 4-byte boundary

---

## Data Structures

### CpioEntry (Lines 306-328)
```csharp
public class CpioEntry
{
    public string Name { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public uint Mode { get; set; } = 0x81A4; // Regular file, 0644
    public int Offset { get; set; }
    
    public CpioEntry Clone()
    public byte[] GetData() => Data;
    public void SetData(byte[] data) => Data = data;
    public string GetString() => Encoding.UTF8.GetString(Data);
    public void SetString(string text) => Data = Encoding.UTF8.GetBytes(text);
}
```

### MagiskArtifacts (Lines 333-340)
```csharp
public class MagiskArtifacts
{
    public bool IsMagiskInstalled { get; set; }
    public bool HasFullBackup { get; set; }
    public bool HasMagiskRc { get; set; }
    public List<string> OverlayDSbin { get; set; } = new();
    public List<string> BackupFiles { get; set; } = new();
}
```

---

## Usage Examples

### Parse and Modify Ramdisk
```csharp
var ramdiskData = File.ReadAllBytes("ramdisk.cpio");
var cpio = CpioArchive.Parse(ramdiskData);

// Modify fstab
var fstab = cpio.GetEntry("fstab.r8q");
if (fstab != null) {
    var content = fstab.GetString();
    content = content.Replace("verity", "verify");
    content = content.Replace("forceencrypt", "encryptable");
    fstab.SetString(content);
}

// Add new file
cpio.Entries.Add(new CpioEntry {
    Name = "newfile.txt",
    Data = Encoding.UTF8.GetBytes("content"),
    Mode = 0x81A4
});

// Serialize
var newRamdisk = cpio.Serialize();
```

### Remove Magisk Artifacts
```csharp
// Remove overlay.d directory
cpio.RemoveDirectory("overlay.d");

// Remove .backup directory
cpio.RemoveDirectory(".backup");

// Remove specific files
cpio.RemoveEntry("ramdisk.cpio.orig");
cpio.RemoveEntry("init.magisk.rc");
cpio.RemoveEntry("sepolicy.rules");
```

### Find Magisk Artifacts
```csharp
var artifacts = cpio.FindMagiskArtifacts();
if (artifacts.IsMagiskInstalled) {
    Console.WriteLine("Magisk detected!");
    Console.WriteLine($"Overlay files: {artifacts.OverlayDSbin.Count}");
    Console.WriteLine($"Backup files: {artifacts.BackupFiles.Count}");
    Console.WriteLine($"Full backup: {artifacts.HasFullBackup}");
    Console.WriteLine($"init.magisk.rc: {artifacts.HasMagiskRc}");
}
```

---

## Missing/Partial Implementations

1. **Checksum (chksum)** - Always writes 0, doesn't calculate actual checksum
2. **Symbolic links** - Mode handling for symlinks (0xA1A4) not tested
3. **Device files** - Block/char device support not tested
4. **Hard links** - nlink > 1 not handled
5. **mtime** - Always writes 0, doesn't preserve timestamps
6. **UID/GID** - Always writes 0
7. **070702 format** - Detects but parses as 070701
8. **Large files** - No 64-bit filesize support (newc is 32-bit)

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| Parse_ValidNewc_ParsesAllEntries | ❌ |
| Parse_TrailerStopsParsing | ❌ |
| Parse_4ByteAlignment_Handled | ❌ |
| Parse_ZeroSizeFiles_Handled | ❌ |
| Parse_NullTerminatedNames_Handled | ❌ |
| Serialize_RoundTrip_PreservesData | ❌ |
| WriteEntry_HeaderFields_Correct | ❌ |
| WriteEntry_NameAlignment_4Bytes | ❌ |
| WriteEntry_DataAlignment_4Bytes | ❌ |
| GetEntry_PartialMatch_Works | ❌ |
| GetDirectory_ReturnsChildren | ❌ |
| ReplaceEntry_UpdatesData | ❌ |
| RemoveEntry_RemovesFile | ❌ |
| RemoveDirectory_RemovesAll | ❌ |
| Clone_DeepCopy_Independent | ❌ |
| DetectDeviceName_FromFstab | ❌ |
| FindMagiskArtifacts_DetectsAll | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/RamdiskEditor/CpioArchive.cs` (340 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_rootdir.rs` through `v27.0_rootdir.rs`
- `scripts/boot_patch.sh` (cpio functions)

**AOSP Sources:**
- `system/core/cpio/mkbootfs.c` - Reference implementation
- `system/core/include/cpio.h` - Format definitions