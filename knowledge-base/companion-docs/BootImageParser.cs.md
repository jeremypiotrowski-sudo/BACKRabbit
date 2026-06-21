# Companion Documentation: BACKRabbit.MagiskCore.Parser.BootImageParser.cs

> **📚 Expanded Knowledge-Base Available:** Magisk source files for v25.0 through v30.7 (plus 12 canary builds) are now extracted in `knowledge-base/`. See `VERSION_MATRIX.md` for the full evolution timeline, `CROSS_REFERENCE_INDEX.md` for unified function mapping, and `AUDIT.md` for the complete codebase audit.

## Purpose
Complete boot image parser supporting all Android boot image formats (AOSP v0-v4, vendor v3-v4, Samsung PXA/DHTB/BLOB, ChromeOS) with full AVB footer detection and SEANDROID enforcement flag parsing.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        BootImageParser                              │
├─────────────────────────────────────────────────────────────────────┤
│  Parse() ──→ ParseHeader() ──→ ParseSections() ──→ ParseTail()     │
│                            │                    │                   │
│                            ▼                    ▼                   │
│                    ┌─────────────┐    ┌─────────────┐              │
│                    │ FindAvbFtr  │    │ VendorRam   │              │
│                    │ ParseVendor │    │ DiskTable   │              │
│                    └─────────────┘    └─────────────┘              │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Parse Methods
```csharp
// Parse from file path
public BootImage Parse(string path)

// Parse from byte array
public BootImage Parse(byte[] data)
```

### Extraction Methods
```csharp
// Extract raw kernel bytes
public byte[] ExtractKernel(BootImage img)

// Extract raw ramdisk bytes (compressed)
public byte[] ExtractRamdisk(BootImage img)

// Extract and parse ramdisk as CPIO archive
public CpioArchive ExtractRamdiskArchive(BootImage img)

// Extract DTB blob
public byte[] ExtractDtb(BootImage img)
```

## Supported Formats

| Format | Magic | Version | Notes |
|--------|-------|---------|-------|
| **AOSP v0** | `ANDROID!` | 0 | Original Android boot format |
| **AOSP v1** | `ANDROID!` |   | 1 | Adds recovery DTBO, header_size |
| **AOSP v2** | `ANDROID!` | 2 | Adds DTB, recovery DTBO |
| **AOSP v3** | `ANDROID!` | 3 | 4KB header, no second/extra |
| **AOSP v4** | `ANDROID!` | 4 | Adds signature, SHA-256 id |
| **Vendor v3** | `VNDRBOOT` | 3 | Vendor boot partition |
| **Vendor v4** | `VNDRBOOT` | 4 | Vendor boot + ramdisk table |
| **Samsung PXA** | `ANDROID!` | (page_size ≥ 0x02000000) | Samsung proprietary |
| **Samsung DHTB** | `DHTB` | - | Samsung download mode |
| **Samsung BLOB** | `BLOB` | - | Samsung secure boot |
| **ChromeOS** | `CHROMEOS` | - | Chromebook boot images |

## Internal Data Structures

### BootImage (Main Result Class)
```csharp
public class BootImage {
    public byte[] RawData { get; set; }           // Original image data
    public bool IsVendor { get; set; }            // Vendor boot partition
    public uint HeaderVersion { get; set; }       // 0-4
    
    // Headers (one populated based on version)
    public BootImgHdrV0 HeaderV0 { get; set; }
    public BootImgHdrV1 HeaderV1 { get; set; }
    public BootImgHdrV2 HeaderV2 { get; set; }
    public BootImgHdrV3 HeaderV3 { get; set; }
    public BootImgHdrV4 HeaderV4 { get; set; }
    public BootImgHdrPxa HeaderPxa { get; set; }
    public VendorBootImgHdrV3 HeaderV3Vendor { get; set; }
    public VendorBootImgHdrV4 HeaderV4Vendor { get; set; }
    
    // Section offsets/sizes
    public long KernelOffset { get; set; }
    public uint KernelSize { get; set; }
    public long RamdiskOffset { get; set; }
    public uint RamdiskSize { get; set; }
    // ... (second, extra, recovery_dtbo, dtb, signature)
    
    // Tail (after payload)
    public long TailOffset { get; set; }
    public uint TailSize { get; set; }
    public byte[] TailData { get; set; }
    
    // AVB
    public long AvbFooterOffset { get; set; }
    public AvbFooter AvbFooter { get; set; }
    public ulong VbmetaOffset { get; set; }
    public AvbVBMetaImageHeader Vbmeta { get; set; }
    
    // Vendor v4
    public List<VendorRamdiskTableEntryV4> VendorRamdiskEntries { get; set; }
    
    // Flags
    public BootImageFlags Flags { get; set; }
}
```

### BootImageFlags
```csharp
public class BootImageFlags {
    public bool HasDhtb { get; set; }      // Samsung DHTB header present
    public bool HasBlob { get; set; }      // Samsung BLOB header present
    public bool HasChromeos { get; set; }  // ChromeOS header present
    public bool HasSeandroid { get; set; } // SEANDROIDENFORCE in tail
    public bool HasAvb { get; set; }       // AVB footer found
    public bool IsPxa { get; set; }        // Samsung PXA format
}
```

## Parsing Algorithm

### Phase 1: Header Detection (Parse)
Scans entire image byte-by-byte for known magic signatures:
1. **DHTB** (8 bytes: `DHTB`) → Samsung download mode
2. **BLOB** (20 bytes: `BLOB` magic) → Samsung secure boot
3. **CHROMEOS** → ChromeOS boot image (skips 64KB)
4. **AOSP/AOSP_VENDOR** → Standard Android boot headers

### Phase 2: Header Parsing (ParseHeader)
Once AOSP/AOSP_VENDOR detected:
1. Check for `VNDRBOOT` magic → Vendor boot (v3/v4)
2. Check `page_size >= 0x02000000` → Samsung PXA
3. Read `header_version` field → Select v0-v4 struct

### Phase 3: Section Layout (ParseSections)
Calculates offsets using page alignment (typically 4096 bytes):
```
[Header] → [Kernel] → [Ramdisk] → [Second] → [Extra v0] → 
[Recovery DTBO v1-v2] → [DTB v2+/vendor] → [Signature v4] → 
[Vendor Ramdisk Table v4] → [Tail]
```

### Phase 4: Tail Analysis (ParseTail)
- SEANDROID enforcement: checks for `SEANDROIDENFORCE` magic
- AVB footer: searches last 1024 bytes for `AVB0` magic
- If AVB found: reads vbmeta header at `footer_offset - vbmeta_offset`

## Header Struct Definitions (from AOSP + Magisk Extensions)

### BootImgHdrV0 (512 bytes) - Original Format
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct BootImgHdrV0 {
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;     // "ANDROID!"
    public uint kernel_size, kernel_addr;
    public uint ramdisk_size, ramdisk_addr;
    public uint second_size, second_addr;
    public uint tags_addr;
    public uint page_size;
    public uint header_version;          // 0
    public uint os_version;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] name;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public byte[] cmdline;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] id;       // SHA-1
    // Union: extra_cmdline OR recovery_dtbo fields (v0 only uses extra_cmdline)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] extra_cmdline;
}
```

### BootImgHdrV1 (512 bytes) - Adds recovery DTBO
```csharp
// Same as V0 plus:
public uint recovery_dtbo_size;
public ulong recovery_dtbo_offset;
public uint header_size;  // Total header size (512)
```

### BootImgHdrV2 (512 bytes) - Adds DTB
```csharp
// Same as V1 plus:
public uint dtb_size;
public ulong dtb_addr;
```

### BootImgHdrV3 (4096 bytes) - Modern Format
```csharp
[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;
public uint kernel_size;
public uint ramdisk_size;
public uint os_version;
public uint header_size;    // 4096
public uint reserved_0, reserved_1, reserved_2, reserved_3;
public uint header_version; // 3
[MarshalAs(UnmanagedType.ByValArray, SizeConst = 1536)] public byte[] cmdline;
// No second_addr, tags_addr, extra_cmdline, recovery_dtbo
```

### BootImgHdrV4 (4096 bytes) - Signed Images
```csharp
// Same as V3 plus:
public uint signature_size;  // AVB signature size
```

### VendorBootImgHdrV3/V4
```csharp
// Magic: "VNDRBOOT" (8 bytes)
// Fields: page_size, kernel/ramdisk addr, cmdline (2048 bytes), 
// tags_addr, name, header_size, dtb_size, dtb_addr
// V4 adds: vendor_ramdisk_table_* fields
```

### Samsung PXA Header
```csharp
// Detected when hdr0.page_size >= 0x02000000
// Contains: kernel_size, ramdisk_size, second_size, page_size, etc.
// Samsung-specific offsets and encryption info
```

## Format Detection (FormatDetector Integration)

```csharp
var fmt = FormatDetector.CheckFmt(data.AsSpan(i));
```

Returns `FileFormat` enum:
- `AOSP` - Standard boot image
- `AOSP_VENDOR` - Vendor boot image
- `DHTB` - Samsung download mode
- `BLOB` - Samsung secure blob
- `CHROMEOS` - ChromeOS
- `CPIO` - Raw CPIO archive
- `GZIP`, `LZ4`, `ZSTD`, `XZ` - Compression formats
- `DTB` - Device Tree Blob
- `AVB_FOOTER` - AVB footer

## AVB Footer & VBMeta

### AvbFooter (from AOSP external/avb)
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AvbFooter {
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] magic;     // "AVB0"
    public uint version;
    public ulong original_image_size;
    public ulong vbmeta_offset;
    public uint vbmeta_size;
    // ... additional fields
}
```

### AvbVBMetaImageHeader
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AvbVBMetaImageHeader {
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] magic;     // "AVB0"
    public uint required_libavb_version_major;
    public uint required_libavb_version_minor;
    public uint authentication_data_block_size;
    // ... hash descriptors, public key, etc.
}
```

## Usage Examples

### Basic Parsing
```csharp
var parser = new BootImageParser();
var bootImage = parser.Parse("boot.img");

Console.WriteLine($"Format: {(bootImage.IsVendor ? "Vendor" : "AOSP")} v{bootImage.HeaderVersion}");
Console.WriteLine($"Kernel: {bootImage.KernelSize} bytes at offset 0x{bootImage.KernelOffset:X}");
Console.WriteLine($"Ramdisk: {bootImage.RamdiskSize} bytes at offset 0x{bootImage.RamdiskOffset:X}");
Console.WriteLine($"Flags: DHTB={bootImage.Flags.HasDhtb}, BLOB={bootImage.Flags.HasBlob}, " +
                  $"SEAndroid={bootImage.Flags.HasSeandroid}, AVB={bootImage.Flags.HasAvb}");
```

### Extract & Modify Ramdisk
```csharp
// Extract ramdisk as CPIO archive
var ramdiskArchive = parser.ExtractRamdiskArchive(bootImage);

// Modify a file
var fstab = ramdiskArchive.GetEntry("fstab.default");
if (fstab != null) {
    var content = fstab.GetString();
    content = content.Replace("ro", "rw");
    fstab.SetString(content);
}

// Repack
var repacker = new BootImageRepacker();
var newImage = repacker.RepackWithRamdisk(bootImage, ramdiskArchive);
File.WriteAllBytes("boot_modified.img", newImage);
```

### Check AVB Status
```csharp
if (bootImage.Flags.HasAvb) {
    Console.WriteLine($"AVB Footer at 0x{bootImage.AvbFooterOffset:X}");
    Console.WriteLine($"VBMeta at 0x{bootImage.VbmetaOffset:X}, size={bootImage.AvbFooter.vbmeta_size}");
    Console.WriteLine($"AVB Version: {bootImage.AvbFooter.version}");
}
```

## Magisk Version Compatibility

| Magisk Version | Boot Image Formats Supported | BACKRabbit Status |
|----------------|------------------------------|-------------------|
| v25.0 | AOSP v0-v3, Vendor v3, PXA, DHTB, BLOB, CHROMEOS | ✅ Full |
| v25.1 | Same + bug fixes | ✅ Full |
| v25.2 | Same + bug fixes | ✅ Full |
| v26.0 | + AOSP v4, Vendor v4 | ✅ Full |
| v26.1 | Same | ✅ Full |
| v26.2 | + Vendor ramdisk table | ✅ Full |
| v26.3 | Same | ✅ Full |
| v26.4 | Same | ✅ Full |
| v27.0 | Minor field additions | ✅ Full |

## Limitations & Known Gaps

1. **AVB Re-signing** - Not implemented. Requires AVB private key (`avbtool`).
2. **Signature Recalculation** - v4 signature cleared on repack (line 217).
3. **Vendor Ramdisk Table Recalculation** - Table copied as-is; offsets not updated.
4. **DTB Overlay Application** - No DTBO apply logic (Magisk's `dtb.cpp`).
5. **ChromeOS Special Parsing** - Only detection, no deep parsing.
6. **Recovery DTBO Integration** - Parsed but not used in repack flow.

## Related Files

| File | Relationship |
|------|--------------|
| `BootImageRepacker.cs` | Repacks images parsed by this class |
| `FormatDetector.cs` | Provides `CheckFmt()` for header detection |
| `CompressionEngine.cs` | Decompresses ramdisk in `ExtractRamdiskArchive()` |
| `CpioArchive.cs` | Parses decompressed ramdisk |
| `AvbRestorer.cs` | Handles AVB footer restoration |
| `BootImgHeaders.cs` | Header struct definitions |
| `AvbStructures.cs` | AVB footer & vbmeta structs |

## References

1. **AOSP bootimg.h** - `system/core/mkbootimg/bootimg.h`
2. **AOSP AVB headers** - `external/avb/libavb/avb_footer.h`, `avb_vbmeta_image.h`
3. **Magisk bootimg.cpp** - `native/src/boot/bootimg.cpp` (v26.3 reference)
4. **Magisk bootimg.hpp** - `native/src/boot/bootimg.hpp` (v26.3 reference)
5. **Magisk format.cpp** - `native/src/boot/format.cpp` (format detection)
6. **Samsung PXA/DHTB/BLOB** - Magisk's Samsung-specific handling
7. **Cross-reference map** - `knowledge-base/cross-reference-map/BootImageParser.cs.md`

## Testing Recommendations

Create test images for each format:
```bash
# AOSP v0-v4: Use mkbootimg from AOSP
# Vendor v3/v4: Use mkbootimg --vendor_boot
# Samsung PXA: Extract from Samsung firmware
# DHTB/BLOB: Extract from Samsung ODIN packages
# AVB: Use avbtool to add footer
# ChromeOS: Extract from Chromebook recovery
```

Verify parsing accuracy against Magisk's `magiskboot unpack` output.