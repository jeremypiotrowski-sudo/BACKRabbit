# Cross-Reference Map: BACKRabbit.MagiskCore.Repacker.BootImageRepacker.cs ↔ Magisk/AOSP Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/Repacker/BootImageRepacker.cs` |
| **Magisk Source** | `native/src/boot/bootimg.cpp` (`repack()`), `scripts/boot_patch.sh` (`repack_bootimg()`) |
| **AOSP Source** | `system/core/mkbootimg/bootimg.h`, `system/core/mkbootimg/mkbootimg.cpp` |
| **Total Lines (BACKRabbit)** | 353 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v27.0
| BACKRabbit Line(s) | Magisk Source | Function |
|---|---|---|
| 22-125 | `native/src/boot/bootimg.cpp` | `BootImg::repack()` - main repack |
| 127-140 | `scripts/boot_patch.sh` | `repack_bootimg()` - CPIO-based repack |
| 142-158 | `native/src/boot/avb.cpp` | `RepackWithoutAvb()` - AVB removal |
| 160-223 | `native/src/boot/bootimg.cpp` | `WriteAospHeader()`, `WritePxaHeader()`, `WriteVendorHeader()` |
| 260-306 | `native/src/boot/bootimg.cpp` | Helper methods (GetHeaderSize, GetPageSize, PadTo) |
| 308-322 | `native/src/boot/bootimg.cpp` | `FindAvbFooter()` |
| 326-336 | - | `CalculateSha256()` - verification |

### Magisk bootimg.cpp (v26.3)
| BACKRabbit Line(s) | bootimg.cpp Line(s) | Notes |
|---|---|---|
| 22-125 | ~600-700 | `repack()` with page alignment |
| 162-176 | ~700-720 | `write_header()` dispatch |
| 178-223 | ~720-780 | `write_aosp_header()` switch v0-v4 |
| 225-233 | ~780-790 | `write_pxa_header()` |
| 235-254 | ~790-810 | `write_vendor_header()` v3/v4 |
| 260-281 | ~810-830 | `get_header_size()` |
| 283-296 | ~830-840 | `get_page_size()` |
| 298-306 | ~840-850 | `pad_to()` |
| 308-322 | ~850-870 | `find_avb_footer()` |

---

## Detailed Function Mapping

### Repack() - Lines 22-125
**Magisk Equivalent:** `BootImg::repack()` in bootimg.cpp

```cpp
// Magisk v26.3 (bootimg.cpp ~line 600)
vector<uint8_t> BootImg::repack(const vector<uint8_t>& new_ramdisk, 
                                 const optional<vector<uint8_t>>& new_kernel) {
    // 1. Detect format, compress if needed
    // 2. Calculate sizes and offsets
    // 3. Write header (with updated kernel_size, ramdisk_size)
    // 4. Pad to page boundary
    // 5. Write kernel (new or original)
    // 6. Pad
    // 7. Write ramdisk
    // 8. Pad
    // 9. Write second/extra/DTBO/DTB/signature/vendor_ramdisk_table
    // 10. Write tail
    // 11. Return image
}
```

**BACKRabbit mapping:**
- Lines 31-38: Format detection + compression
- Lines 43-46: Size calculation
- Line 49: WriteHeader()
- Line 52: PadTo()
- Lines 55-63: Kernel write + pad
- Lines 66-67: Ramdisk write + pad
- Lines 70-81: Second + Extra + pad
- Lines 84-88: Recovery DTBO + pad
- Lines 91-95: DTB + pad
- Lines 98-103: Signature + pad (v4)
- Lines 106-116: Vendor ramdisk table + pad (vendor v4)
- Lines 119-122: Tail write

### WriteAospHeader() - Lines 178-223
**Magisk Equivalent:** `write_aosp_header()` in bootimg.cpp

```cpp
// Magisk v26.3 (bootimg.cpp ~line 720)
void BootImg::write_aosp_header(ostream& out, size_t kernel_size, size_t ramdisk_size) {
    switch (header_version) {
        case 0: {
            auto hdr = header_v0;
            hdr.kernel_size = kernel_size;
            hdr.ramdisk_size = ramdisk_size;
            write_struct(out, hdr);
            break;
        }
        case 1: {
            auto hdr = header_v1;
            hdr.kernel_size = kernel_size;
            hdr.ramdisk_size = ramdisk_size;
            write_struct(out, hdr);
            break;
        }
        case 2: {
            auto hdr = header_v2;
            hdr.kernel_size = kernel_size;
            hdr.ramdisk_size = ramdisk_size;
            write_struct(out, hdr);
            break;
        }
        case 3: {
            auto hdr = header_v3;
            hdr.kernel_size = kernel_size;
            hdr.ramdisk_size = ramdisk_size;
            write_struct(out, hdr);
            break;
        }
        case 4: {
            auto hdr = header_v4;
            hdr.kernel_size = kernel_size;
            hdr.ramdisk_size = ramdisk_size;
            hdr.signature_size = 0;  // Clear - needs re-signing
            write_struct(out, hdr);
            break;
        }
    }
}
```

**BACKRabbit mapping:** Lines 183-220 direct port with signature clearing.

### WriteVendorHeader() - Lines 235-254
**Magisk Equivalent:** `write_vendor_header()` in bootimg.cpp

```cpp
// Magisk v26.3 (bootimg.cpp ~line 790)
void BootImg::write_vendor_header(ostream& out, size_t kernel_size, size_t ramdisk_size) {
    if (header_version == 4) {
        auto hdr = header_v4_vendor;
        hdr.ramdisk_size = ramdisk_size;  // Vendor boot has no kernel
        write_struct(out, hdr);
    } else {
        auto hdr = header_v3_vendor;
        hdr.ramdisk_size = ramdisk_size;
        write_struct(out, hdr);
    }
}
```

**BACKRabbit mapping:** Lines 240-251 direct port.

---

## Repack Flow Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      BootImageRepacker                          │
├─────────────────────────────────────────────────────────────────┤
│  Repack(original, newRamdisk, newKernel?) ──→ byte[]           │
│       │                                                         │
│       ├─→ FormatDetector.CheckFmt()                             │
│       ├─→ CompressionEngine.Compress(Gzip)                      │
│       ├─→ GetHeaderSize() / GetPageSize()                       │
│       ├─→ WriteHeader() ──→ WriteAospHeader/WritePxaHeader/    │
│       │                      WriteVendorHeader()                │
│       ├─→ PadTo(pageSize)                                       │
│       ├─→ Write Kernel (new or original) + Pad                  │
│       ├─→ Write Ramdisk + Pad                                   │
│       ├─→ Write Second (v0) + Pad                               │
│       ├─→ Write Extra (v0) + Pad                                │
│       ├─→ Write Recovery DTBO (v1-v2) + Pad                     │
│       ├─→ Write DTB (v2+/vendor) + Pad                          │
│       ├─→ Write Signature (v4, cleared) + Pad                   │
│       ├─→ Write Vendor Ramdisk Table (vendor v4) + Pad          │
│       └─→ Write Tail (SEANDROID, AVB footer)                    │
│                                                                 │
│  RepackWithRamdisk(CpioArchive) ──→ Serialize → Compress → Repack│
│                                                                 │
│  RepackWithoutAvb() ──→ Repack → Truncate at AVB footer        │
└─────────────────────────────────────────────────────────────────┘
```

---

## Data Structures

### RepackResult (Lines 344-353)
```csharp
public class RepackResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public byte[]? RepackedImage { get; set; }
    public string? OriginalSha256 { get; set; }
    public string? RepackedSha256 { get; set; }
    public uint OriginalSize { get; set; }
    public uint RepackedSize { get; set; }
}
```

---

## Page Alignment Rules

| Version | Header Size | Page Size | Kernel Align | Ramdisk Align |
|---------|-------------|-----------|--------------|---------------|
| v0 | 512 | From header | page_size | page_size |
| v1 | 512 | From header | page_size | page_size |
| v2 | 512 | From header | page_size | page_size |
| v3 | 4096 | 4096 (fixed) | 4096 | 4096 |
| v4 | 4096 | 4096 (fixed) | 4096 | 4096 |
| PXA | PXA header size | >= 0x02000000 | PXA page | PXA page |
| Vendor v3 | header_size | From header | N/A | page_size |
| Vendor v4 | header_size | From header | N/A | page_size |

---

## Missing/Partial Implementations

1. **Signature Recalculation** - v4 signature cleared but not recalculated (requires private key)
2. **AVB Footer Regeneration** - Only removes, doesn't regenerate with new hash
3. **ID Calculation** - `id` field in v0-v2 not recalculated (should be SHA1 of sections)
4. **Vendor Ramdisk Table Update** - Writes original entries, doesn't recalculate for new ramdisk
5. **Bootconfig** - v4 vendor bootconfig not handled
6. **Multiple DTB/DTBO** - Single DTB only
7. **Recovery DTBO** - Preserves original, doesn't rebuild

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| Repack_AOSP_v0_ValidHeader_KernelRamdisk | ❌ |
| Repack_AOSP_v1_RecoveryDtbo_Preserved | ❌ |
| Repack_AOSP_v2_Dtb_Preserved | ❌ |
| Repack_AOSP_v3_FixedPage_Aligns | ❌ |
| Repack_AOSP_v4_SignatureCleared | ❌ |
| Repack_SamsungPXA_PageSizeRespected | ❌ |
| Repack_Vendor_v3_InitBoot_RamdiskOnly | ❌ |
| Repack_Vendor_v4_MultiRamdisk_TableWritten | ❌ |
| Repack_WithNewKernel_ReplacesKernel | ❌ |
| RepackWithRamdisk_CpioArchive_CompressesGzip | ❌ |
| RepackWithoutAvb_TruncatesAtAVB0 | ❌ |
| CalculateSha256_Deterministic | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/Repacker/BootImageRepacker.cs` (353 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_bootimg.cpp` through `v27.0_bootimg.cpp`
- `scripts/boot_patch.sh` (repack_bootimg function)

**AOSP Sources:**
- `system/core/mkbootimg/mkbootimg.cpp` - Reference implementation
- `system/core/mkbootimg/bootimg.h` - Header definitions