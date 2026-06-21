# Cross-Reference Map: BACKRabbit.Protocol.DownloadMode.PitFile.cs ↔ Heimdall/Samsung Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.Protocol.DownloadMode/PitFile.cs` |
| **Heimdall Source** | `heimdall/src/pit.cpp`, `heimdall/src/pit.h` |
| **Samsung Source** | Proprietary (no public source) |
| **Total Lines (BACKRabbit)** | 217 |

---

## Version-by-Version Cross-Reference

### Heimdall PIT Parser (heimdall/src/pit.cpp)
| BACKRabbit Line(s) | Heimdall Source | Function |
|---|---|---|
| 18-57 | `pit.cpp` | `PitData::Parse()` / `Pit::Parse()` |
| 59-65 | `pit.cpp` | `GetPartition()` |
| 67-73 | `pit.cpp` | `GetPartitions()` |
| 78-120 | `pit.cpp` | `Serialize()` / `toBinary()` |
| 144-154 | `pit.h` | `PitHeader` struct |
| 159-174 | `pit.h` | `PitEntryRaw` struct |

### Heimdall pit.cpp (v1.4.2)
| BACKRabbit Line(s) | pit.cpp Line(s) | Notes |
|---|---|---|
| 22-25 | ~100-110 | Read header fields |
| 28-54 | ~110-160 | Loop entries, parse each |
| 43-44 | ~140-150 | Partition name decoding |
| 97-117 | ~160-220 | Write header + entries |

---

## Detailed Function Mapping

### Parse() - Lines 18-57
**Heimdall Equivalent:** `PitData::Parse()` in pit.cpp

```cpp
// Heimdall (pit.cpp ~line 100)
bool PitData::Parse(const uint8_t* data, size_t size) {
    // Read header
    const PitHeader* header = reinterpret_cast<const PitHeader*>(data);
    header_size = header->header_size;
    entry_count = header->entry_count;
    
    // Read entries
    size_t entry_offset = header_size;
    size_t entry_size = sizeof(PitEntryRaw);
    
    for (uint32_t i = 0; i < entry_count; i++) {
        if (entry_offset + entry_size > size) break;
        
        const PitEntryRaw* entry_raw = reinterpret_cast<const PitEntryRaw*>(data + entry_offset);
        
        PitEntry entry;
        entry.entry_type = entry_raw->entry_type;
        entry.partition_id = entry_raw->partition_id;
        entry.partition_name = string((char*)entry_raw->partition_name).c_str();
        entry.flash_filename = string((char*)entry_raw->flash_filename).c_str();
        entry.block_count = entry_raw->block_count;
        entry.block_size = entry_raw->block_size;
        entry.offset = entry_raw->offset;
        entry.attributes = entry_raw->attributes;
        entry.update_attributes = entry_raw->update_attributes;
        entry.flags = entry_raw->flags;
        
        entries.push_back(entry);
        entry_offset += entry_size;
    }
    
    return true;
}
```

**BACKRabbit mapping:** Lines 22-54 direct port with MemoryMarshal.Read.

### Serialize() - Lines 78-120
**Heimdall Equivalent:** `PitData::toBinary()` in pit.cpp

```cpp
// Heimdall (pit.cpp ~line 160)
vector<uint8_t> PitData::toBinary() const {
    size_t entry_size = sizeof(PitEntryRaw);
    size_t total_size = header_size + entries.size() * entry_size;
    vector<uint8_t> data(total_size);
    
    // Write header
    PitHeader header;
    header.header_size = header_size;
    header.entry_count = entries.size();
    header.unknown = 0x1234BAAD;
    memcpy(data.data(), &header, sizeof(header));
    
    // Write entries
    size_t offset = header_size;
    for (const auto& entry : entries) {
        PitEntryRaw raw;
        raw.entry_type = entry.entry_type;
        raw.partition_id = entry.partition_id;
        strcpy((char*)raw.partition_name, entry.partition_name.c_str());
        strcpy((char*)raw.flash_filename, entry.flash_filename.c_str());
        raw.block_count = entry.block_count;
        raw.block_size = entry.block_size;
        raw.offset = entry.offset;
        raw.attributes = entry.attributes;
        raw.update_attributes = entry.update_attributes;
        raw.flags = entry.flags;
        
        memcpy(data.data() + offset, &raw, entry_size);
        offset += entry_size;
    }
    
    return data;
}
```

**BACKRabbit mapping:** Lines 84-117 direct port with MemoryMarshal.Write.

---

## PIT Structures

### PitHeader (Lines 147-154)
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PitHeader
{
    public uint header_size;      // Usually 0x100 (256)
    public uint unknown;          // 0x1234BAAD
    public uint entry_count;      // Number of entries
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 252)] public byte[] reserved;
}
// Total: 256 bytes
```

### PitEntryRaw (Lines 159-174)
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PitEntryRaw
{
    public uint entry_type;           // 0=partition, 1=???
    public uint partition_id;         // Unique ID
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] partition_name;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] flash_filename;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] fota_filename;
    public uint block_count;          // Number of blocks
    public uint block_size;           // Block size (usually 512)
    public uint offset;               // Block offset
    public uint attributes;           // Partition attributes
    public uint update_attributes;    // Update attributes
    public uint flags;                // Flags
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] reserved;
}
// Total: 520 bytes per entry
```

### PitEntry (Managed) - Lines 126-142
```csharp
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
}
```

---

## PIT Entry Types

| EntryType | Description |
|---|---|
| 0 | Regular partition |
| 1 | Unknown (possibly special/bootloader) |
| 2 | GPT partition |
| 3 | Unknown |

### Common Partition Names (Samsung S24/S25)
| Partition | EntryType | Purpose |
|---|---|---|
| `boot` | 0 | Kernel + ramdisk |
| `init_boot` | 0 | Ramdisk only (GKI 2.0) |
| `vbmeta` | 0 | AVB metadata (boot) |
| `vbmeta_system` | 0 | AVB metadata (system) |
| `vbmeta_vendor` | 0 | AVB metadata (vendor) |
| `dtbo` | 0 | Device tree overlay |
| `super` | 0 | Dynamic partitions container |
| `recovery` | 0 | Recovery image |
| `dt` | 0 | Device tree (legacy) |

---

## Usage Examples

### Parse PIT from Device
```csharp
var flasher = new DownloadModeFlasher(usb);
await flasher.InitializeAsync();
var pit = await flasher.ReadPitAsync();

foreach (var entry in pit.GetPartitions()) {
    Console.WriteLine($"{entry.PartitionName}: {entry.Size} bytes, offset={entry.Offset}");
}
```

### Get Specific Partition
```csharp
var bootEntry = pit.GetPartition("boot");
if (bootEntry != null) {
    Console.WriteLine($"Boot partition: {bootEntry.Size} bytes");
    Console.WriteLine($"Block count: {bootEntry.BlockCount}, Block size: {bootEntry.BlockSize}");
}
```

### Modify and Re-serialize PIT
```csharp
// Add new partition
pit.Entries.Add(new PitEntry {
    PartitionName = "my_partition",
    BlockCount = 1000,
    BlockSize = 512,
    Offset = 0,  // Will be calculated
    EntryType = 0
});

var pitBytes = pit.Serialize();
await flasher.FlashPartitionAsync("PIT", pitBytes);
```

---

## Samsung Firmware Package (Lines 179-217)

### FirmwarePackage
```csharp
public class FirmwarePackage
{
    public string Model { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, byte[]> Partitions { get; set; } = new();
    
    public byte[]? GetPartition(string name)
    public void AddPartition(string name, string filePath)
    public void AddPartition(string name, byte[] data)
    public List<string> GetPartitionNames()
}
```

### Usage with SamsungFirmwareExtractor
```csharp
var extractor = new SamsungFirmwareExtractor();
var firmware = extractor.ExtractTarMd5("AP_*.tar.md5");

// Access partitions
var boot = firmware.GetPartition("boot");
var vbmeta = firmware.GetPartition("vbmeta");
var super = firmware.GetPartition("super");

Console.WriteLine($"Partitions: {string.Join(", ", firmware.GetPartitionNames())}");
```

---

## Missing/Partial Implementations

1. **PIT Version Detection** - Doesn't detect PIT version (v1, v2, etc.)
2. **GPT Parsing** - Doesn't parse GPT within PIT
3. **Partition Validation** - No checksum/CRC validation
4. **FOTA Filename** - `fota_filename` field parsed but not exposed in PitEntry
5. **Attributes Decoding** - Attributes/flags not decoded to meaningful values
6. **Offset Calculation** - Auto-calculate offsets when adding entries
7. **Multiple PIT Support** - Some devices have multiple PITs

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| Parse_ValidPIT_ParsesHeader | ❌ |
| Parse_ValidPIT_ParsesAllEntries | ❌ |
| Parse_InvalidSize_Throws | ❌ |
| GetPartition_ExistingName_ReturnsEntry | ❌ |
| GetPartition_CaseInsensitive_Works | ❌ |
| GetPartitions_FiltersType0 | ❌ |
| Serialize_RoundTrip_PreservesData | ❌ |
| Serialize_EntryNamePadding_64Bytes | ❌ |
| Serialize_HeaderFields_Correct | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.Protocol.DownloadMode/PitFile.cs` (217 lines)

**Heimdall Sources:**
- `heimdall/src/pit.cpp` - PIT parsing/serialization
- `heimdall/src/pit.h` - Structure definitions

**Related BACKRabbit Files:**
- `DownloadModeFlasher.cs` - Uses PitFile for ReadPitAsync
- `SamsungFirmwareExtractor.cs` - Creates FirmwarePackage