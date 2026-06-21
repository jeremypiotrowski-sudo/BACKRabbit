# Companion Documentation: BACKRabbit.MagiskCore.Repacker.BootImageRepacker.cs

## Purpose
Boot image repacker that rebuilds boot images with modified ramdisk/kernel while preserving all original headers, sections, and metadata. Ported from Magisk's `boot_img::repack()` and `magiskboot` repack functionality.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        BootImageRepacker                            │
├─────────────────────────────────────────────────────────────────────┤
│  Repack()                                                           │
│    │                                                                │
│    ├─→ Detect ramdisk format → Compress if needed                  │
│    ├─→ Calculate layout (header_size, page_size, offsets)          │
│    ├─→ WriteHeader() ──→ WriteAospHeader() / WritePxaHeader() /    │
│    │                    WriteVendorHeader()                         │
│    ├─→ Write sections: Kernel → Ramdisk → Second → Extra →         │
│    │                    Recovery DTBO → DTB → Signature →          │
│    │                    Vendor Ramdisk Table                        │
│    ├─→ Write Tail (SEANDROID, AVB footer)                          │
│    └─→ Return byte[]                                                │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Primary Repack Methods
```csharp
/// <summary>
/// Repack boot image with new ramdisk (and optional new kernel)
/// </summary>
/// <param name="originalImage">Parsed boot image (from BootImageParser)</param>
/// <param name="newRamdisk">New ramdisk data (compressed or raw)</param>
/// <param name="newKernel">Optional new kernel (null = keep original)</param>
/// <returns>Repacked boot image bytes</returns>
public byte[] Repack(BootImage originalImage, byte[] newRamdisk, byte[]? newKernel = null)

/// <summary>
/// Repack with CPIO archive (handles serialization + compression)
/// </summary>
public byte[] RepackWithRamdisk(BootImage originalImage, CpioArchive newRamdiskArchive)

/// <summary>
/// Repack with AVB footer removed (for testing only)
/// </summary>
public byte[] RepackWithoutAvb(BootImage originalImage, byte[] newRamdisk)
```

### Verification
```csharp
/// <summary>
/// Calculate SHA256 for integrity verification
/// </summary>
public static string CalculateSha256(byte[] data)
```

### Result Metadata
```csharp
public class RepackResult {
    public bool Success { get; set; }
    public string Message { get; set; }
    public byte[]? RepackedImage { get; set; }
    public string? OriginalSha256 { get; set; }
    public string? RepackedSha256 { get; set; }
    public uint OriginalSize { get; set; }
    public uint RepackedSize { get; set; }
}
```

## Repack Algorithm

### Phase 1: Ramdisk Preparation (Lines 31-38)
```csharp
// Detect format
var ramdiskFormat = FormatDetector.CheckFmt(newRamdisk);
// If not compressed, compress with gzip (Magisk default)
if (!FormatDetector.IsCompressed(ramdiskFormat)) {
    using var compression = new CompressionEngine();
    newRamdisk = compression.Compress(newRamdisk, CompressionEngine.CompressionFormat.Gzip);
}
```

### Phase 2: Layout Calculation (Lines 42-49)
```csharp
var headerSize = GetHeaderSize(originalImage);  // 512 or 4096
var pageSize = GetPageSize(originalImage);       // Usually 4096
var kernelSize = newKernel?.Length ?? (int)originalImage.KernelSize;
var ramdiskSize = newRamdisk.Length;
```

### Phase 3: Header Writing (Lines 49-52, 162-254)
Writes appropriate header based on image type:
- **AOSP** (v0-v4): `WriteAospHeader()` - updates kernel_size, ramdisk_size, clears signature (v4)
- **Samsung PXA**: `WritePxaHeader()` - updates kernel/ramdisk sizes
- **Vendor** (v3/v4): `WriteVendorHeader()` - updates ramdisk_size

### Phase 4: Section Writing (Lines 54-116)
Each section written with page alignment:
```
[Header] (padded to page_size)
[Kernel] (padded to page_size)
[Ramdisk] (padded to page_size)
[Second] (if present, padded)
[Extra] (v0 only, padded)
[Recovery DTBO] (v1-v2, padded)
[DTB] (v2+/vendor, padded)
[Signature] (v4, padded) - PRESERVES ORIGINAL, NOT RE-SIGNED
[Vendor Ramdisk Table] (vendor v4, padded)
[Tail] (SEANDROID, AVB footer - copied as-is)
```

### Phase 5: Tail Preservation (Lines 118-122)
Copies original tail data including SEANDROID enforcement string and AVB footer.

## Header Writing Details

### WriteAospHeader() - Lines 178-223
```csharp
switch (img.HeaderVersion) {
    case 0:
        var hdr0 = img.HeaderV0;
        hdr0.kernel_size = kernelSize;
        hdr0.ramdisk_size = ramdiskSize;
        MemoryMarshal.Write(headerBytes.AsSpan(), ref hdr0);
        break;
    case 1:
        var hdr1 = img.HeaderV1;
        hdr1.kernel_size = kernelSize;
        hdr1.ramdisk_size = ramdiskSize;
        MemoryMarshal.Write(headerBytes.AsSpan(), ref hdr1);
        break;
    case 2:
        var hdr2 = img.HeaderV2;
        hdr2.kernel_size = kernelSize;
        hdr2.ramdisk_size = ramdiskSize;
        MemoryMarshal.Write(headerBytes.AsSpan(), ref hdr2);
        break;
    case 3:
        var hdr3 = img.HeaderV3;
        hdr3.kernel_size = kernelSize;
        hdr3.ramdisk_size = ramdiskSize;
        MemoryMarshal.Write(headerBytes.AsSpan(), ref hdr3);
        break;
    case 4:
        var hdr4 = img.HeaderV4;
        hdr4.kernel_size = kernelSize;
        hdr4.ramdisk_size = ramdiskSize;
        hdr4.signature_size = 0;  // CLEARED - NEEDS RE-SIGNING
        MemoryMarshal.Write(headerBytes.AsSpan(), ref hdr4);
        break;
}
```

### WriteVendorHeader() - Lines 235-254
```csharp
if (img.HeaderVersion == 4) {
    var hdr4 = img.HeaderV4Vendor;
    hdr4.ramdisk_size = ramdiskSize;
    MemoryMarshal.Write(headerBytes.AsSpan(), ref hdr4);
} else {
    var hdr3 = img.HeaderV3Vendor;
    hdr3.ramdisk_size = ramdiskSize;
    MemoryMarshal.Write(headerBytes.AsSpan(), ref hdr3);
}
```

## Key Behaviors

### Compression Handling
- **Input ramdisk**: Can be raw CPIO or already compressed (gzip, lz4, zstd, xz)
- **Auto-detection**: Uses `FormatDetector.CheckFmt()` to identify format
- **Default compression**: gzip (matches Magisk behavior)
- **Preservation**: If already compressed, uses as-is

### AVB Handling
- **Preserve**: By default, copies AVB footer and vbmeta from original
- **Remove**: `RepackWithoutAvb()` finds `AVB0` magic and truncates
- **Re-sign**: NOT IMPLEMENTED - requires private key and `avbtool`

### Signature Handling (v4)
- **Original signature**: Preserved in `Repack()` if present
- **New signature**: NOT generated - `signature_size` set to 0
- **Verification**: SHA256 provided for integrity checking

### Vendor Ramdisk Table (v4)
- **Copied as-is**: Entries not recalculated when ramdisk size changes
- **Limitation**: Offsets become invalid if ramdisk grows/shrinks significantly

## Usage Examples

### Basic Ramdisk Replacement
```csharp
var parser = new BootImageParser();
var repacker = new BootImageRepacker();

// Parse original
var bootImage = parser.Parse("boot.img");

// Extract, modify, repack ramdisk
var ramdiskArchive = parser.ExtractRamdiskArchive(bootImage);
var fstab = ramdiskArchive.GetEntry("fstab.default");
fstab.SetString(fstab.GetString().Replace("ro,", "rw,"));

// Repack with modified ramdisk
var newImage = repacker.RepackWithRamdisk(bootImage, ramdiskArchive);
File.WriteAllBytes("boot_patched.img", newImage);

// Verify
Console.WriteLine($"Original: {BootImageRepacker.CalculateSha256(File.ReadAllBytes("boot.img"))}");
Console.WriteLine($"Patched:  {BootImageRepacker.CalculateSha256(newImage)}");
```

### Kernel Replacement
```csharp
var newKernel = File.ReadAllBytes("Image.gz-dtb");
var newImage = repacker.Repack(bootImage, 
    parser.ExtractRamdisk(bootImage),  // Keep original ramdisk
    newKernel);                         // Replace kernel
File.WriteAllBytes("boot_new_kernel.img", newImage);
```

### AVB Removal (Testing Only)
```csharp
// Remove AVB footer for devices that don't enforce it
var newImage = repacker.RepackWithoutAvb(bootImage, 
    parser.ExtractRamdisk(bootImage));
File.WriteAllBytes("boot_no_avb.img", newImage);
```

### Round-Trip Verification
```csharp
// Parse → Repack → Parse again
var original = parser.Parse("boot.img");
var repacked = repacker.RepackWithRamdisk(original, 
    parser.ExtractRamdiskArchive(original));
var reparsed = parser.Parse(repacked);

// Compare critical fields
Assert.AreEqual(original.HeaderVersion, reparsed.HeaderVersion);
Assert.AreEqual(original.KernelSize, reparsed.KernelSize);
Assert.AreEqual(original.RamdiskSize, reparsed.RamdiskSize);
Assert.AreEqual(original.Flags.HasAvb, reparsed.Flags.HasAvb);
```

## Magisk Version Compatibility

| Magisk Version | Formats Supported | BACKRabbit Status |
|----------------|-------------------|-------------------|
| v25.0 | AOSP v0-v3, Vendor v3, PXA | ✅ Full |
| v25.1-v25.2 | Same + fixes | ✅ Full |
| v26.0 | + AOSP v4, Vendor v4 | ✅ Full* |
| v26.1-v26.4 | Same | ✅ Full* |
| v27.0 | Minor additions | ✅ Full* |

*Signature cleared on v4 repack (not re-signed)

## Limitations & Known Gaps

### Critical
1. **AVB Re-signing** - Cannot generate new AVB signature. Requires:
   - AVB private key (RSA/ECDSA)
   - `avbtool add_hash_footer` or equivalent
   - Integration with platform build keys

2. **Signature Recalculation (v4)** - `signature_size = 0` breaks verified boot

### Significant
3. **Vendor Ramdisk Table Recalculation** - Offsets not updated
4. **Recovery DTBO Offset/Size** - Not recalculated
5. **DTB Offset/Size** - Not recalculated for vendor images

### Minor
6. **No A/B Slot Awareness** - Doesn't handle slot suffixes
7. **ChromeOS Headers** - Not regenerated
8. **Samsung PXA Encryption** - Not handled (if applicable)

## Related Files

| File | Relationship |
|------|--------------|
| `BootImageParser.cs` | Provides `BootImage` input |
| `FormatDetector.cs` | Ramdisk format detection |
| `CompressionEngine.cs` | Ramdisk compression (gzip/lz4/zstd/xz) |
| `CpioArchive.cs` | Ramdisk serialization for `RepackWithRamdisk()` |
| `AvbRestorer.cs` | AVB footer restoration (complementary) |
| `BootImgHeaders.cs` | Header struct definitions |

## References

1. **Magisk bootimg.cpp** - `native/src/boot/bootimg.cpp` `repack()` function
2. **Magisk magiskboot.cpp** - `magiskboot_repack()` CLI entry
3. **Magisk format.cpp** - `check_fmt()`, `is_compressed()`, compression
4. **Magisk compress.cpp** - gzip/lz4/zstd/xz implementation
5. **AOSP mkbootimg** - Header format specifications
6. **AOSP AVB** - `avbtool` for signing reference

## Testing Recommendations

### Unit Tests
```csharp
[Test] void Repack_AOSP_v0_RamdiskOnly_ProducesValidImage()
[Test] void Repack_AOSP_v4_ClearsSignature()
[Test] void Repack_Vendor_v4_PreservesTable()
[Test] void Repack_PXA_PreservesHeader()
[Test] void RepackWithoutAvb_RemovesFooter()
[Test] void CalculateSha256_MatchesKnownValues()
```

### Integration Tests
```csharp
[Test] void RoundTrip_ParseRepackParse_AllVersions()
[Test] void Repack_ModifiedRamdisk_BootsOnDevice()
[Test] void Repack_KernelReplacement_BootsOnDevice()
[Test] void Repack_SizeChange_PageAlignmentCorrect()
```

### Test Images Needed
| Format | Source |
|--------|--------|
| AOSP v0-v4 | `mkbootimg` from AOSP |
| Vendor v3/v4 | `mkbootimg --vendor_boot` |
| Samsung PXA | Stock Samsung firmware |
| AVB-signed | `avbtool add_hash_footer` |
| ChromeOS | Chromebook recovery image |

## Cross-Reference
See `knowledge-base/cross-reference-map/BootImageRepacker.cs.md` for line-by-line Magisk source mapping.