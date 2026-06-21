# Cross-Reference Map: BACKRabbit.MagiskCore.FormatDetection.FormatDetector.cs ↔ Magisk Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/FormatDetection/FormatDetector.cs` |
| **Magisk Source** | `native/src/boot/format.rs`, `native/src/boot/magisk_bootimg.cpp` |
| **Total Lines (BACKRabbit)** | 252 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v27.0
| BACKRabbit Line(s) | Magisk Source | Function |
|---|---|---|
| 9-33 | `format.rs` | `FileFormat` enum |
| 41-60 | `format.rs` / `magisk_bootimg.cpp` | Magic byte constants |
| 66-95 | `format.rs` | `CheckFmt()` - `check_fmt()` |
| 103-128 | `format.rs` | `CheckFmtLg()` - `check_fmt_lg()` |
| 137-152 | `format.rs` | `IsLzma()` - `guess_lzma()` |
| 157-162 | `format.rs` | `IsCompressed()` |
| 167-176 | `format.rs` | `GetExtension()` |
| 181-202 | `format.rs` | `GetName()` |
| 210-251 | `format.rs` | Extension methods |

### Magisk format.rs (v26.3)
| BACKRabbit Line(s) | format.rs Line(s) | Notes |
|---|---|---|
| 9-33 | ~50-80 | `FileFormat` enum |
| 41-60 | ~80-110 | Magic byte arrays |
| 66-95 | ~110-170 | `check_fmt()` |
| 1()` | `check_fmt_lg()` |
| 137-152 | ~190-220 | `guess_lzma()` |
| 157-162 | ~220-230 | `is_compressed()` |
| 167-176 | ~230-240 | `get_extension()` |
| 181-202 | ~240-260 | `get_name()` |

---

## Detailed Function Mapping

### CheckFmt() - Lines 66-95
**Magisk Equivalent:** `check_fmt()` in format.rs

```rust
// Magisk v26.3 (format.rs ~line 110)
pub fn check_fmt(buf: &[u8]) -> FileFormat {
    if buf.len() < 8 { return FileFormat::Unknown; }
    
    // Boot formats
    if buf.starts_with(b"CHROMEOS") { return FileFormat::ChromeOS; }
    if buf.starts_with(b"ANDROID!") { return FileFormat::AOSP; }
    if buf.starts_with(b"VNDRBOOT") { return FileFormat::AOSPVendor; }
    
    // Compression
    if buf.starts_with(&[0x1F, 0x8B]) { return FileFormat::Gzip; }
    if buf.starts_with(&[0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]) { return FileFormat::Xz; }
    if guess_lzma(buf) { return FileFormat::Lzma; }
    if buf.starts_with(b"BZh") { return FileFormat::Bzip2; }
    if buf.starts_with(&[0x02, 0x21, 0x4C, 0x18]) { return FileFormat::Lz4; }
    if buf.starts_with(&[0x03, 0x21, 0x4C, 0x18]) { return FileFormat::Lz4; }
    if buf.starts_with(&[0x04, 0x22, 0x4C, 0x18]) { return FileFormat::Lz4Legacy; }
    
    // Special
    if buf.starts_with(&[0x88, 0x16, 0x88, 0x16]) { return FileFormat::Mtk; }
    if buf.starts_with(&[0xD0, 0x0D, 0xFE, 0xED]) { return FileFormat::Dtb; }
    if buf.starts_with(b"DHTBHDR!") { return FileFormat::Dhtb; }
    if buf.starts_with(&[0x1E, 0x2A, 0x3E, 0x4F]) { return FileFormat::Blob; }
    
    // zImage at offset 0x24
    if buf.len() >= 0x28 && &buf[0x24..0x28] == b"zImage" {
        return FileFormat::ZImage;
    }
    
    FileFormat::Unknown
}
```

**BACKRabbit mapping:** Lines 71-94 direct port with SequenceEqual.

### CheckFmtLg() - Lines 103-128
**Magisk Equivalent:** `check_fmt_lg()` in format.rs

```rust
// Magisk v26.3 (format.rs ~line 170)
pub fn check_fmt_lg(buf: &[u8]) -> FileFormat {
    let fmt = check_fmt(buf);
    
    if fmt == FileFormat::Lz4Legacy && buf.len() >= 8 {
        let mut off = 4;
        while off + 4 <= buf.len() {
            let block_size = u32::from_le_bytes([buf[off], buf[off+1], buf[off+2], buf[off+3]]);
            off += 4;
            if off + block_size as usize > buf.len() {
                return FileFormat::Lz4Lg;
            }
            off += block_size as usize;
        }
    }
    
    fmt
}
```

**BACKRabbit mapping:** Lines 107-125 direct port.

### IsLzma() - Lines 137-152
**Magisk Equivalent:** `guess_lzma()` in format.rs

```rust
// Magisk v26.3 (format.rs ~line 190)
fn guess_lzma(buf: &[u8]) -> bool {
    if buf.len() < 13 { return false; }
    if buf[0] != 0x5D { return false; }
    
    // Dictionary size (bytes 1-4) must be power of 2
    let dict_sz = u32::from_le_bytes([buf[1], buf[2], buf[3], buf[4]]);
    if dict_sz == 0 || (dict_sz & (dict_sz - 1)) != 0 { return false; }
    
    // Bytes 5-12 must be all 0xFF
    if &buf[5..13] != &[0xFF; 8] { return false; }
    
    true
}
```

**BACKRabbit mapping:** Lines 139-151 direct port.

---

## Magic Byte Constants (from Magisk)

| Constant | Hex | Description |
|---|---|---|
| `BOOT_MAGIC` | 41 4E 44 52 4F 49 44 21 | "ANDROID!" |
| `VENDOR_BOOT_MAGIC` | 56 4E 44 52 42 4F 4F 54 | "VNDRBOOT" |
| `CHROMEOS_MAGIC` | 43 48 52 4F 4D 45 4F 53 | "CHROMEOS" |
| `MTK_MAGIC` | 88 16 88 16 | MediaTek |
| `DTB_MAGIC` | D0 0D FE ED | Device Tree Blob |
| `DHTB_MAGIC` | 44 48 54 42 48 44 52 21 | "DHTBHDR!" |
| `TEGRABLOB_MAGIC` | 1E 2A 3E 4F | NVIDIA Tegra |
| `GZIP1_MAGIC` | 1F 8B | gzip/zopfli |
| `XZ_MAGIC` | FD 37 7A 58 5A 00 | XZ |
| `BZIP_MAGIC` | 42 5A 68 | "BZh" |
| `LZ41_MAGIC` | 02 21 4C 18 | LZ4 |
| `LZ42_MAGIC` | 03 21 4C 18 | LZ4 |
| `LZ4_LEG_MAGIC` | 04 22 4C 18 | LZ4 Legacy |
| `SEANDROID_MAGIC` | 53 45 41 4E 44 52 4F 49 44 45 4E 46 4F 52 43 45 | "SEANDROIDENFORCE" |
| `LG_BUMP_MAGIC` | 4C 47 5F 42 55 4D 50 5F 52 45 56 | "LG_BUMP_REV" |
| `AVB_FOOTER_MAGIC` | 41 56 42 30 | "AVB0" |
| `ZIMAGE_MAGIC` | 7A 49 6D 61 67 65 | "zImage" |

---

## FileFormat Enum

```csharp
public enum FileFormat
{
    UNKNOWN = 0,
    
    // Boot image formats
    AOSP = 1,
    AOSP_VENDOR = 2,
    CHROMEOS = 3,
    MTK = 4,
    DTB = 5,
    DHTB = 6,
    BLOB = 7,
    ZIMAGE = 8,
    
    // Compression formats
    GZIP = 10,
    ZOPFLI = 11,
    LZOP = 12,
    XZ = 13,
    LZMA = 14,
    BZIP2 = 15,
    LZ4 = 16,
    LZ4_LEGACY = 17,
    LZ4_LG = 18,
}
```

---

## Extension Methods (Lines 208-251)

```csharp
// Detect from byte array
data.DetectFormat() → FormatDetector.CheckFmtLg(data)

// Detect from stream (reads 512 bytes)
stream.DetectFormat() → CheckFmtLg(first 512 bytes)

// Detect from file path
"path/to/file".DetectFormat() → Open file, read 512 bytes, CheckFmtLg
```

---

## Missing/Partial Implementations

1. **ZOPFLI** - Returns GZIP (same magic), no separate detection
2. **LZOP** - Magic not defined (0x89 0x4C 0x5A 0x4F)
3. **CRC32 validation** - Not performed on compressed data
4. **Nested formats** - e.g., gzip'd cpio not recursively detected
5. **MTK variations** - Multiple MTK formats exist

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| CheckFmt_AOSP_ReturnsAOSP | ❌ |
| CheckFmt_VendorBoot_ReturnsVendor | ❌ |
| CheckFmt_GZIP_ReturnsGZIP | ❌ |
| CheckFmt_XZ_ReturnsXZ | ❌ |
| CheckFmt_LZMA_HeuristicWorks | ❌ |
| CheckFmt_BZIP2_ReturnsBZIP2 | ❌ |
| CheckFmt_LZ4_Variants_ReturnsLZ4 | ❌ |
| CheckFmt_LZ4_LG_Detected | ❌ |
| CheckFmt_MTK_Detected | ❌ |
| CheckFmt_DTB_Detected | ❌ |
| CheckFmt_DHTB_Detected | ❌ |
| CheckFmt_BLOB_Detected | ❌ |
| CheckFmt_ZIMAGE_Offset0x24_Detected | ❌ |
| CheckFmt_Unknown_ReturnsUnknown | ❌ |
| IsCompressed_AllFormats_Correct | ❌ |
| GetExtension_ReturnsCorrectExt | ❌ |
| GetName_ReturnsReadableName | ❌ |
| DetectFormat_ByteArray_Works | ❌ |
| DetectFormat_Stream_Works | ❌ |
| DetectFormat_FilePath_Works | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/FormatDetection/FormatDetector.cs` (252 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_format.rs` through `v27.0_format.rs`
- `native/src/boot/magisk_bootimg.cpp`
- `scripts/boot_patch.sh` (format detection functions)