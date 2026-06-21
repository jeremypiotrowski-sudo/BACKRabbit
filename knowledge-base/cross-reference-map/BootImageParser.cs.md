# Cross-Reference Map: BACKRabbit.MagiskCore.Parser.BootImageParser.cs ↔ Magisk/AOSP Sources

> **📚 Expanded Knowledge-Base Available:** Magisk source files for v25.0 through v30.7 (plus 12 canary builds) are now extracted in `knowledge-base/`. See `VERSION_MATRIX.md` for the full evolution timeline, `CROSS_REFERENCE_INDEX.md` for unified function mapping across all versions, and `AUDIT.md` for the complete codebase audit.

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/Parser/BootImageParser.cs` |
| **Magisk Source** | `native/src/boot/bootimg.cpp`, `native/src/boot/bootimg.h` |
| **AOSP Source** | `system/core/include/android/bootimg.h`, `system/core/fs_mgr/bootimg.h` |
| **Total Lines (BACKRabbit)** | 614 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v27.0 (native/src/boot/bootimg.cpp)
| BACKRabbit Line(s) | Magisk v26.x Line(s) | Function |
|---|---|---|
| 23-68 | ~50-120 | `BootImg::parse()` - main parse entry |
| 70-135 | ~120-200 | `ParseHeader()` - header detection |
| 137-197 | ~200-280 | `ParseSections()` - section offsets |
| 199-221 | ~280-320 | `ParseTail()` - tail/SEANDROID/AVB |
| 223-250 | ~320-370 | `FindAvbFooter()` - AVB detection |
| 252-266 | ~370-400 | `ParseVendorRamdiskTable()` - vendor v4 |
| 270-402 | ~400-500 | Helper methods (GetHeaderSize, GetPageSize, etc.) |
| 408-444 | ~500-550 | Extraction methods |

### AOSP Boot Image Format
| BACKRabbit Line(s) | AOSP Source | Description |
|---|---|---|
| 99-134 | `bootimg.h` | AOSP v0-v4 header parsing |
| 102-108 | `bootimg.h` | Samsung PXA detection (page_size >= 0x02000000) |
| 75-96 | `bootimg.h` | Vendor boot v3/v4 ("VNDRBOOT" magic) |
| 165-171 | `bootimg.h` | Recovery DTBO (v1-v2) |
| 173-179 | `bootimg.h` | DTB (v2+) |
| 181-187 | `bootimg.h` | Signature (v4) |
| 190-193 | `bootimg.h` | Vendor ramdisk table (v4) |

---

## Detailed Function Mapping

### Parse() - Lines 23-68
**Magisk Equivalent:** `BootImg::parse()` in bootimg.cpp

```cpp
// Magisk v26.3 (bootimg.cpp ~line 50)
BootImg BootImg::parse(const uint8_t* data, size_t size) {
    BootImg img;
    img.raw_data = data;
    
    // Scan for special formats
    for (size_t i = 0; i < size; i++) {
        FileFormat fmt = FormatDetector::check(data + i);
        switch (fmt) {
            case FileFormat::DHTB:
                img.flags.has_dhtb = true;
                i += sizeof(DhtbHdr) - 1;
                break;
            case FileFormat::BLOB:
                img.flags.has_blob = true;
                i += sizeof(BlobHdr) - 1;
                break;
            case FileFormat::CHROMEOS:
                img.flags.has_chromeos = true;
                i += 65535;
                break;
            case FileFormat::AOSP:
            case FileFormat::AOSP_VENDOR:
                if (parse_header(data + i, fmt, img)) {
                    parse_sections(data, img);
                    parse_tail(data, img);
                    return img;
                }
                break;
        }
    }
    throw InvalidDataException("No valid boot image header");
}
```

**BACKRabbit mapping:** Lines 37-65 direct port with FormatDetector.

### ParseHeader() - Lines 70-135
**Magisk Equivalent:** `parse_header()` in bootimg.cpp

```cpp
// Magisk v26.3 (bootimg.cpp ~line 120)
bool BootImg::parse_header(const uint8_t* data, FileFormat type, BootImg& img) {
    // Vendor boot check
    if (type == FileFormat::AOSP_VENDOR) {
        if (memcmp(data, "VNDRBOOT", 8) == 0) {
            auto hdr = *reinterpret_cast<const vendor_boot_img_hdr_v3*>(data);
            img.is_vendor = true;
            if (hdr.header_version == 4) {
                img.header_v4_vendor = *reinterpret_cast<const vendor_boot_img_hdr_v4*>(data);
            } else {
                img.header_v3_vendor = hdr;
            }
            return true;
        }
    }
    
    // Standard AOSP
    auto hdr0 = *reinterpret_cast<const boot_img_hdr*>(data);
    
    // Samsung PXA
    if (hdr0.page_size >= 0x02000000) {
        img.header_pxa = *reinterpret_cast<const boot_img_hdr_pxa*>(data);
        img.flags.is_pxa = true;
        return true;
    }
    
    // v0-v4
    img.header_version = hdr0.header_version;
    switch (hdr0.header_version) {
        case 0: img.header_v0 = hdr0; break;
        case 1: img.header_v1 = *reinterpret_cast<const boot_img_hdr_v1*>(data); break;
        case 2: img.header_v2 = *reinterpret_cast<const boot_img_hdr_v2*>(data); break;
        case 3: img.header_v3 = *reinterpret_cast<const boot_img_hdr_v3*>(data); break;
        case 4: img.header_v4 = *reinterpret_cast<const boot_img_hdr_v4*>(data); break;
    }
    return true;
}
```

**BACKRabbit mapping:** Lines 98-134 direct port.

### ParseSections() - Lines 137-197
**Magisk Equivalent:** `parse_sections()` in bootimg.cpp

```cpp
// Magisk v26.3 (bootimg.cpp ~line 200)
void BootImg::parse_sections(const uint8_t* data, BootImg& img) {
    size_t offset = get_header_size(img);
    size_t page_size = get_page_size(img);
    
    // Kernel
    img.kernel_offset = offset;
    img.kernel_size = get_kernel_size(img);
    offset += align(img.kernel_size, page_size);
    
    // Ramdisk
    img.ramdisk_offset = offset;
    img.ramdisk_size = get_ramdisk_size(img);
    offset += align(img.ramdisk_size, page_size);
    
    // Second (v0 only)
    if (img.header_version == 0) {
        img.second_offset = offset;
        img.second_size = get_second_size(img);
        offset += align(img.second_size, page_size);
    }
    
    // Recovery DTBO (v1-v2)
    if (img.header_version == 1 || img.header_version == 2) {
        img.recovery_dtbo_offset = get_recovery_dtbo_offset(img);
        img.recovery_dtbo_size = get_recovery_dtbo_size(img);
        offset += align(img.recovery_dtbo_size, page_size);
    }
    
    // DTB (v2+, vendor)
    if (img.header_version >= 2 || img.is_vendor) {
        img.dtb_offset = get_dtb_offset(img);
        img.dtb_size = get_dtb_size(img);
        offset += align(img.dtb_size, page_size);
    }
    
    // Signature (v4)
    if (img.header_version == 4 && !img.is_vendor) {
        img.signature_offset = offset;
        img.signature_size = get_signature_size(img);
        offset += align(img.signature_size, page_size);
    }
    
    // Vendor ramdisk table (vendor v4)
    if (img.is_vendor && img.header_version == 4) {
        parse_vendor_ramdisk_table(data, img, offset);
    }
    
    img.payload_size = offset;
}
```

**BACKRabbit mapping:** Lines 139-196 direct port.

### ParseTail() / FindAvbFooter() - Lines 199-250
**Magisk Equivalent:** `parse_tail()` + `find_avb_footer()` in bootimg.cpp

```cpp
// Magisk v26.3 (bootimg.cpp ~line 280)
void BootImg::parse_tail(const uint8_t* data, BootImg& img) {
    if (data.size() > img.payload_size) {
        img.tail_offset = img.payload_size;
        img.tail_size = data.size() - img.payload_size;
        
        // SEANDROID
        if (img.tail_size >= 16 && memcmp(data + img.tail_offset, "SEANDROIDENFORCE", 16) == 0) {
            img.flags.has_seandroid = true;
        }
        
        // AVB Footer
        find_avb_footer(img);
    }
}

void BootImg::find_avb_footer(BootImg& img) {
    const uint8_t magic[] = { 'A', 'V', 'B', '0' };
    size_t min_offset = max(0, img.tail_offset + img.tail_size - 1024);
    
    for (size_t i = img.tail_offset + img.tail_size - sizeof(avb_footer); 
         i >= min_offset; i--) {
        if (memcmp(data + i, magic, 4) == 0) {
            img.avb_footer_offset = i;
            img.avb_footer = *reinterpret_cast<const avb_footer*>(data + i);
            
            // Read vbmeta
            size_t vbmeta_start = i - img.avb_footer.vbmeta_offset;
            img.vbmeta_offset = vbmeta_start;
            img.vbmeta = *reinterpret_cast<const avb_vbmeta_image_header*>(data + vbmeta_start);
            img.flags.has_avb = true;
            break;
        }
    }
}
```

**BACKRabbit mapping:** Lines 199-250 direct port.

---

## Header Structure Summary

### AOSP Boot Image Headers
| Version | Header Size | Page Size | Sections |
|---------|-------------|-----------|----------|
| v0 | 512 (page aligned) | From header | kernel, ramdisk, second, extra |
| v1 | 512 | From header | v0 + recovery_dtbo |
| v2 | 512 | From header | v1 + dtb |
| v3 | 4096 (fixed) | 4096 | kernel, ramdisk (no second/extra) |
| v4 | 4096 (fixed) | 4096 | v3 + signature |
| PXA | 512 | >= 0x02000000 | Samsung-specific |

### Vendor Boot Headers (GKI 2.0)
| Version | Magic | Key Feature |
|---------|-------|-------------|
| v3 | "VNDRBOOT" | Single ramdisk |
| v4 | "VNDRBOOT" | Multiple ramdisks (vendor_ramdisk_table) |

---

## Data Structures

### BootImage (Lines 552-601)
```csharp
public class BootImage
{
    public byte[] RawData { get; set; }
    public bool IsVendor { get; set; }
    public uint HeaderVersion { get; set; }
    
    // Headers (mutually exclusive based on version)
    public BootImgHdrV0 HeaderV0 { get; set; }
    public BootImgHdrV1 HeaderV1 { get; set; }
    public BootImgHdrV2 HeaderV2 { get; set; }
    public BootImgHdrV3 HeaderV3 { get; set; }
    public BootImgHdrV4 HeaderV4 { get; set; }
    public BootImgHdrPxa HeaderPxa { get; set; }
    public VendorBootImgHdrV3 HeaderV3Vendor { get; set; }
    public VendorBootImgHdrV4 HeaderV4Vendor { get; set; }
    
    // Section offsets/sizes
    public long KernelOffset properties for: Kernel, Ramdisk, Second, Extra, RecoveryDtbo, DTB, Signature, Payload
    
    // Tail
    long TailOffset { get; set; }
    uint TailSize { get; set; }
    byte[] TailData { get; set; }
    
    // AVB
    long AvbFooterOffset { get; set; }
    AvbFooter AvbFooter { get; set; }
    ulong VbmetaOffset { get; set; }
    AvbVBMetaImageHeader Vbmeta { get; set; }
    
    // Vendor v4
    List<VendorRamdiskTableEntryV4> VendorRamdiskEntries { get; set; }
    
    // Flags
    BootImageFlags Flags { get; set; }
}
```

### BootImageFlags (Lines 606-614)
```csharp
public class BootImageFlags
{
    public bool HasDhtb { get; set; }
    public bool HasBlob { get; set; }
    public bool HasChromeos { get; set; }
    public bool HasSeandroid { get; set; }
    public bool HasAvb { get; set; }
    public bool IsPxa { get; set; }
}
```

---

## Missing/Partial Implementations

1. **BootImgHdrV4** - Referenced but struct defined in BootImgHeaders.cs (separate file)
2. **DTB parsing** - Only extracts, doesn't parse DTB structure
3. **ChromeOS header** - Detected but not parsed
4. **DHTB/BLOB** - Detected but not fully parsed
5. **Multiple vendor ramdisks** - Parses table but doesn't extract individual ramdisks
6. **Signature verification** - Extracts but doesn't verify v4 signature
7. **AVB verification** - Finds footer but doesn't verify hash chain

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| Parse_AOSP_v0_ValidHeader_ParsesAllSections | ❌ |
| Parse_AOSP_v1_RecoveryDtbo_Parses | ❌ |
| Parse_AOSP_v2_Dtb_Parses | ❌ |
| Parse_AOSP_v3_FixedHeader_Parses | ❌ |
| Parse_AOSP_v4_Signature_Parses | ❌ |
| Parse_Vendor_v3_InitBoot_Parses | ❌ |
| Parse_Vendor_v4_MultiRamdisk_ParsesTable | ❌ |
| Parse_SamsungPXA_Detected | ❌ |
| Parse_Tail_SEANDROID_Detected | ❌ |
| Parse_Tail_AVBFooter_Found | ❌ |
| ExtractKernel_ValidOffset_ReturnsKernel | ❌ |
| ExtractRamdisk_ValidOffset_ReturnsRamdisk | ❌ |
| ExtractRamdiskArchive_Compressed_Decompresses | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/Parser/BootImageParser.cs` (614 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_bootimg.cpp` through `v27.0_bootimg.cpp`
- `v26.0_bootimg.h` through `v27.0_bootimg.h`
- `v26.0_bootimg.hpp` through `v27.0_bootimg.hpp`

**AOSP Sources:**
- `system/core/include/android/bootimg.h`
- `system/libbootloader/include/bootloader.h`