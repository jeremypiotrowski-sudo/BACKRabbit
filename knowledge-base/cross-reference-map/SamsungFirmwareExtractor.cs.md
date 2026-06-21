# Cross-Reference Map: BACKRabbit.Firmware.SamsungFirmwareExtractor.cs ↔ Magisk/External Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.Firmware/SamsungFirmwareExtractor.cs` |
| **External Sources** | Samsung firmware format, Android sparse image format |
| **Magisk Sources** | `scripts/firmware.sh`, `scripts/flash.sh` (firmware extraction) |
| **Total Lines (BACKRabbit)** | 119 |
| **Samsung Firmware Format** | `.tar.md5` (tar + 16-byte MD5 footer) |

---

## Version-by-Version Cross-Reference

### Samsung Firmware Format
| BACKRabbit Line(s) | Source | Function |
|---|---|---|
| 13-50 | Samsung firmware spec | `ExtractTarMd5()` - parse .tar.md5 |
| 52-69 | Android sparse format | `ExtractSuperImg()` - parse sparse super.img |
| 71-76 | Samsung naming convention | `GetPartitionName()` |
| 78-95 | Samsung firmware naming | `ParseFirmwareMetadata()` |

### Magisk Sources (scripts/)
| BACKRabbit Line(s) | Magisk Script | Notes |
|---|---|---|
| 13-50 | `scripts/firmware.sh` | `extract_firmware()` |
| 52-69 | `scripts/flash.sh` | `extract_super()` |
| - | `native/src/boot/sparse.rs` | Sparse image parsing |

---

## Detailed Function Mapping

### ExtractTarMd5() - Lines 13-50
**Samsung Firmware Format (.tar.md5):**
```
[ TAR Archive ] [ 16-byte MD5 Checksum ]
```

**Algorithm:**
```bash
# Magisk v25.0 (firmware.sh ~line 100)
extract_firmware() {
    local file=$1
    
    # Verify MD5
    local embedded_md5=$(tail -c 16 "$file" | xxd -p)
    local data=$(head -c -16 "$file")
    local calc_md5=$(echo -n "$data" | md5sum | cut -d' ' -f1)
    
    if [ "$embedded_md5" != "$calc_md5" ]; then
        echo "MD5 mismatch"
        return 1
    fi
    
    # Extract tar
    tar -xf <(echo "$data") -C "$output_dir"
}
```

**BACKRabbit mapping:**
- Lines 17-22: Read file, split MD5 footer (last 16 bytes)
- Lines 24-28: Calculate MD5 of data portion, verify match
- Lines 30-44: Open tar archive, extract entries to dictionary
- Lines 46-49: Package with metadata

### ExtractSuperImg() - Lines 52-69
**Android Sparse Image Format:**
```c
// system/extras/ext4_utils/sparse_format.h
struct sparse_header {
    uint16_t magic;           // 0xED26
    uint16_t major_version;   // 1
    uint16_t minor_version;   // 0
    // ...
};
```

**BACKRabbit mapping:**
- Line 57: Check sparse magic (0xED26)
- Line 59: Parse with SparseImage
- Line 60: Convert to raw image
- Line 65: If not sparse, use as-is

### GetPartitionName() - Lines 71-76
**Samsung Partition Naming:**
```
AP_SM-S921B_xxx_REV00.tar.md5
       └─ partition name (boot, init_boot, vbmeta, dtbo, etc.)
```

**BACKRabbit mapping:** Extract filename without extensions, lowercase.

### ParseFirmwareMetadata() - Lines 78-95
**Samsung Firmware Filename Format:**
```
AP_SM-S921B_14.0.0.XXX_20240101_REV00.tar.md5
│   │           │           │         │
│   │           │           │         └─ Revision
│   │           │           └─ Build date
│   │           └─ Version
│   └─ Model (SM-S921B)
└─ Type (AP, CP, BL, CSC, HOME)
```

---

## Data Structures

### FirmwarePackage
```csharp
public class FirmwarePackage
{
    public Dictionary<string, byte[]> Partitions { get; set; } = new();
    public FirmwareMetadata Metadata { get; set; } = new();
    
    public byte[]? GetPartition(string name)
    public IEnumerable<string> GetPartitionNames()
}
```

### FirmwareMetadata
```csharp
public class FirmwareMetadata
{
    public string FileName { get; set; } = "";
    public string Type { get; set; } = "";      // AP, CP, BL, CSC, HOME
    public string Model { get; set; } = "";     // SM-S921B
    public string Version { get; set; } = "";
    public string Region { get; set; } = "";
    public DateTime? BuildDate { get; set; }
}
```

---

## Samsung Firmware Types (from filename prefix)

| Prefix | Type | Contains |
|--------|------|----------|
| `AP_` | Application Processor | boot, init_boot, vbmeta, dtbo, super, system, vendor, product |
| `CP_` | Modem/Cellular Processor | modem firmware |
| `BL_` | Bootloader | sboot, cm, upsbl, etc. |
| `CSC_` | Consumer Software Customization | carrier/region configs |
| `HOME_` | Home CSC | user data, apps |

---

## Common Partition Names in AP Tar

| Partition | Description | S24/S25 (GKI 2.0) |
|-----------|-------------|-------------------|
| `boot` | Kernel + ramdisk | Kernel only |
| `init_boot` | Ramdisk only | Ramdisk only |
| `vbmeta` | AVB metadata | Yes |
| `vbmeta_system` | System vbmeta | Yes |
| `vbmeta_vendor` | Vendor vbmeta | Yes |
| `dtbo` | DTB overlay | Yes |
| `super` | Dynamic partitions (sparse) | Yes |
| `system` | System partition | In super |
| `vendor` | Vendor partition | In super |
| `product` | Product partition | In super |
| `system_ext` | System extension | In super |

---

## Missing/Partial Implementations

1. **Super.img Dynamic Partitions** - Extracts raw super but doesn't unpack logical partitions
2. **LZ4 Compression** - Some Samsung ramdisks use LZ4, not handled in tar extraction
3. **Multi-file Firmware** - Some firmware split across multiple tar.md5 files
4. **Encrypted Firmware** - Newer Samsung firmwares may be encrypted
5. **Metadata Parsing** - Version, region, build date not fully parsed from filename
6. **VBmeta Chaining** - Doesn't handle chained vbmeta partitions

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| ExtractTarMd5_ValidFile_VerifiesMD5 | ❌ |
| ExtractTarMd5_InvalidMD5_Throws | ❌ |
| ExtractTarMd5_ExtractsAllPartitions | ❌ |
| ExtractTarMd5_PartitionNamesNormalized | ❌ |
| ExtractSuperImg_SparseImage_ParsesCorrectly | ❌ |
| ExtractSuperImg_RawImage_ReturnsAsIs | ❌ |
| ParseFirmwareMetadata_APFilename_ParsesModel | ❌ |
| GetPartition_CaseInsensitiveLookup | ❌ |
| GetPartitionNames_ReturnsAllKeys | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.Firmware/SamsungFirmwareExtractor.cs` (119 lines)

**External Sources:**
- Samsung firmware format documentation
- Android sparse image format: `system/extras/ext4_utils/sparse_format.h`
- `SparseImage.cs` in BACKRabbit.Protocol.Fastboot

**Magisk Sources (in knowledge-base):**
- `scripts/firmware.sh` - Firmware extraction
- `scripts/flash.sh` - Flash functions
- `native/src/boot/sparse.rs` - Sparse image handling