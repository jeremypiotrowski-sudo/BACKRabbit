 # Companion Documentation: BACKRabbit.MagiskCore.FormatDetection.FormatDetector.cs

## Purpose
File format detection utility that identifies boot image formats, compression formats, and special headers by analyzing magic bytes. Direct port of Magisk's `check_fmt()` and `check_fmt_lg()` functions.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FormatDetector                               │
├─────────────────────────────────────────────────────────────────────┤
│  CheckFmt(buf) ──→ FileFormat (basic detection)                    │
│       │                                                             │
│       ├─→ Boot formats: AOSP, AOSP_VENDOR, CHROMEOS, MTK, DTB,    │
│       │    DHTB, BLOB, ZIMAGE                                      │
│       ├─→ Compression: GZIP, XZ, LZMA, BZIP2, LZ4, LZ4_LEGACY     │
│       └─→ UNKNOWN if no match                                       │
│                                                                     │
│  CheckFmtLg(buf) ──→ FileFormat (enhanced LZ4_LG detection)       │
│       │                                                             │
│       ├─→ Calls CheckFmt()                                         │
│       └─→ If LZ4_LEGACY: Check for LZ4_LG block format             │
│                                                                     │
│  IsCompressed(fmt) ──→ bool                                        │
│  GetExtension(fmt) ──→ string                                      │
│  GetName(fmt) ──→ string                                           │
│                                                                     │
│  Extensions:                                                        │
│  byte[].DetectFormat()                                             │
│  Stream.DetectFormat()                                             │
│  string(filePath).DetectFormat()                                   │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Core Detection
```csharp
/// <summary>
/// Detect file format from magic bytes
/// </summary>
/// <param name="buf">Buffer (at least first 16 bytes)</param>
/// <returns>Detected FileFormat</returns>
public static FileFormat CheckFmt(ReadOnlySpan<byte> buf)

/// <summary>
/// Enhanced detection for LZ4_LG variant
/// </summary>
public static FileFormat CheckFmtLg(ReadOnlySpan<byte> buf)
```

### Format Properties
```csharp
/// <summary>
/// Check if format is compressed
/// </summary>
public static bool IsCompressed(FileFormat fmt)

/// <summary>
/// Get file extension for format
/// </summary>
public static string GetExtension(FileFormat fmt)

/// <summary>
/// Get human-readable format name
/// </summary>
public static string GetName(FileFormat fmt)
```

### Extension Methods
```csharp
public static FileFormat DetectFormat(this byte[] data)
public static FileFormat DetectFormat(this Stream stream)
public static FileFormat DetectFormat(string filePath)
```

## FileFormat Enumeration

### Boot Image Formats
| Value | Constant | Magic | Description |
|-------|----------|-------|-------------|
| 1 | AOSP | `ANDROID!` | Standard Android boot image |
| 2 | AOSP_VENDOR | `VNDRBOOT` | Vendor boot (GKI) |
| 3 | CHROMEOS | `CHROMEOS` | ChromeOS boot image |
| 4 | MTK | `88 16 88 16` | MediaTek boot image |
| 5 | DTB | `D0 0D FE ED` | Device Tree Blob |
| 6 | DHTB | `DHTBHDR!` | DHTB header (Motorola) |
| 7 | BLOB | `1E 2A 3E 4F` | Tegra blob (NVIDIA) |
| 8 | ZIMAGE | `zImage` at 0x24 | ARM zImage kernel |

### Compression Formats
| Value | Constant | Magic | Description |
|-------|----------|-------|-------------|
| 10 | GZIP | `1F 8B` | gzip (also zopfli) |
| 11 | ZOPFLI | `1F 8B` | zopfli (same magic as gzip) |
| 12 | LZOP | - | lzop |
| 13 | XZ | `FD 37 7A 58 5A 00` | XZ/LZMA2 |
| 14 | LZMA | Heuristic | Raw LZMA (no container) |
| 15 | BZIP2 | `42 5A 68` ("BZh") | bzip2 |
| 16 | LZ4 | `02 21 4C 18` | LZ4 frame |
| 16 | LZ4 | `03 21 4C 18` | LZ4 frame (alt) |
| 17 | LZ4_LEGACY | `04 22 4C 18` | LZ4 legacy |
| 18 | LZ4_LG | Block format | LZ4 LG (LG/Samsung) |

---

## Detection Logic (CheckFmt)

### Priority Order
```csharp
// 1. Boot formats (checked first)
CHROMEOS    → "CHROMEOS" at offset 0
AOSP        → "ANDROID!" at offset 0
AOSP_VENDOR → "VNDRBOOT" at offset 0

// 2. Compression formats
GZIP        → 1F 8B at offset 0
XZ          → FD 37 7A 58 5A 00 at offset 0
LZMA        → Heuristic (byte 0=0x5D, dict power of 2, bytes 5-12=0xFF)
BZIP2       → "BZh" at offset 0
LZ4         → 02/03 21 4C 18 at offset 0
LZ4_LEGACY  → 04 22 4C 18 at offset 0

// 3. Special formats
MTK         → 88 16 88 16 at offset 0
DTB         → D0 0D FE ED at offset 0
DHTB        → "DHTBHDR!" at offset 0
BLOB        → 1E 2A 3E 4F at offset 0

// 4. zImage (at specific offset)
ZIMAGE      → "zImage" at offset 0x24

// 5. UNKNOWN
```

---

## LZ4_LG Detection (CheckFmtLg)

### LZ4 Legacy Format
```
Magic: 04 22 4C 18
Structure: [magic][block1_size][block1_data][block2_size][block2_data]...[trailer]
```

### LZ4_LG Format (LG/Samsung variant)
```
Magic: 04 22 4C 18 (same as legacy)
Structure: [magic][block1_size][block1_data]...[trailer_with_total_size]
```

### Detection Algorithm
```csharp
if (fmt == LZ4_LEGACY) {
    off = 4;  // Skip magic
    while (off + 4 <= buf.Length) {
        blockSize = read_uint32_le(off);
        off += 4;
        if (off + blockSize > buf.Length) {
            // Block extends past buffer = LZ4_LG
            return LZ4_LG;
        }
        off += blockSize;
    }
}
```

---

## LZMA Heuristic Detection (IsLzma)

LZMA has no fixed magic, uses properties byte + dictionary size + padding:

```csharp
// LZMA properties byte (0x5D = pb=0, lp=0, lc=4 or similar)
if (buf[0] != 0x5D) return false;

// Dictionary size (bytes 1-4, little-endian)
// Must be power of 2 and > 0
dictSz = read_uint32_le(1);
if (dictSz == 0 || (dictSz & (dictSz - 1)) != 0) return false;

// Bytes 5-12 must be all 0xFF (uncompressed size = unknown)
if (buf.Slice(5, 8) != [0xFF]*8) return false;

return true;
```

---

## Usage Examples

### Basic Detection
```csharp
var format = FormatDetector.CheckFmt(fileData);
Console.WriteLine($"Format: {FormatDetector.GetName(format)}");
Console.WriteLine($"Extension: .{FormatDetector.GetExtension(format)}");
Console.WriteLine($"Compressed: {FormatDetector.IsCompressed(format)}");
```

### Detect from File
```csharp
var format = "boot.img".DetectFormat();
// or
using var fs = File.OpenRead("boot.img");
var format = fs.DetectFormat();
```

### Check if Ramdisk is Compressed
```csharp
var ramdisk = bootParser.ExtractRamdisk(bootImage);
var format = FormatDetector.CheckFmtLg(ramdisk);

if (FormatDetector.IsCompressed(format)) {
    // Decompress before parsing CPIO
    using var compression = new CompressionEngine();
    var decompressed = compression.Decompress(ramdisk);
    var cpio = CpioArchive.Parse(decompressed);
}
```

### Batch Detection
```csharp
var files = Directory.GetFiles("firmware", "*.img");
foreach (var file in files) {
    var fmt = file.DetectFormat();
    Console.WriteLine($"{Path.GetFileName(file)}: {FormatDetector.GetName(fmt)}");
}
```

---

## Magic Bytes Reference

| Format | Offset | Magic Bytes (Hex) | ASCII |
|--------|--------|-------------------|-------|
| AOSP | 0 | `41 4E 44 52 4F 49 44 21` | "ANDROID!" |
| VENDOR | 0 | `56 4E 44 52 42 4F 4F 54` | "VNDRBOOT" |
| CHROMEOS | 0 | `43 48 52 4F 4D 45 4F 53` | "CHROMEOS" |
| MTK | 0 | `88 16 88 16` | - |
| DTB | 0 | `D0 0D FE ED` | - |
| DHTB | 0 | `44 48 54 42 48 44 52 21` | "DHTBHDR!" |
| BLOB | 0 | `1E 2A 3E 4F` | - |
| GZIP | 0 | `1F 8B` | - |
| XZ | 0 | `FD 37 7A 58 5A 00` | - |
| BZIP2 | 0 | `42 5A 68` | "BZh" |
| LZ4 | 0 | `02 21 4C 18` / `03 21 4C 18` | - |
| LZ4_LEGACY | 0 | `04 22 4C 18` | - |
| LZMA | 0 | `5D` + heuristic | - |
| SEANDROID | Tail | `53 45 41 4E 44 52 4F 49 44 45 4E 46 4F 52 43 45` | "SEANDROIDENFORCE" |
| AVB | Tail | `41 56 42 30` | "AVB0" |
| ZIMAGE | 0x24 | `7A 49 6D 61 67 65` | "zImage" |

---

## Missing/Partial Implementations

1. **ZOPFLI** - Same magic as GZIP, not distinguished
2. **LZOP** - No magic detection implemented
3. **LZ4 frame variants** - Only basic magic check
4. **CRC32 validation** - Not performed on detected formats
5. **Nested formats** - Doesn't detect compressed boot images (e.g., gzipped boot.img)

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| CheckFmt_AOSP_Magic_ReturnsAOSP | ❌ |
| CheckFmt_VENDOR_Magic_ReturnsAOSPVendor | ❌ |
| CheckFmt_GZIP_Magic_ReturnsGZIP | ❌ |
| CheckFmt_XZ_Magic_ReturnsXZ | ❌ |
| CheckFmt_LZMA_Heuristic_ReturnsLZMA | ❌ |
| CheckFmt_BZIP2_Magic_ReturnsBZIP2 | ❌ |
| CheckFmt_LZ4_Magic_ReturnsLZ4 | ❌ |
| CheckFmt_LZ4_LEGACY_Magic_ReturnsLZ4_LEGACY | ❌ |
| CheckFmtLg_LZ4LG_DetectsCorrectly | ❌ |
| CheckFmtLg_LZ4Legacy_NotLZ4LG | ❌ |
| IsCompressed_AllCompressed_ReturnsTrue | ❌ |
| IsCompressed_BootFormats_ReturnsFalse | ❌ |
| GetExtension_Compressed_ReturnsExt | ❌ |
| GetName_AllFormats_ReturnsString | ❌ |
| Extensions_ByteArray_DetectsFormat | ❌ |
| Extensions_Stream_DetectsFormat | ❌ |
| Extensions_FilePath_DetectsFormat | ❌ |

---

## Related Files

| File | Relationship |
|------|--------------|
| `BootImageParser.cs` | Uses CheckFmt for header detection |
| `BootImageRepacker.cs` | Uses CheckFmt for ramdisk format |
| `CompressionEngine.cs` | Uses format for decompression |
| `MagiskArtifactDetector.cs` | Uses IsCompressed |
| `CpioArchive.cs` | Requires decompressed data |

---

## References

1. **Magisk format.rs** - `native/src/boot/format.rs`
2. **Magisk magisk_bootimg.cpp** - `native/src/boot/magisk_bootimg.cpp`
3. **Magisk bootimg.cpp** - `native/src/boot/bootimg.cpp`
4. **LZ4 Specification** - LZ4 frame format spec
5. **LZMA Format** - LZMA SDK documentation
6. **XZ Format** - XZ file format spec