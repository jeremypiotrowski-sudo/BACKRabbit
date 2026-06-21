# Cross-Reference Map: BACKRabbit.MagiskCore.Structures.BootHeaders.BootImgHeaders.cs ↔ Magisk/AOSP Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/Structures/BootHeaders/BootImgHeaders.cs` |
| **AOSP Source** | `system/libbootloader/include/bootloader.h`, `system/core/include/android/bootimg.h` |
| **Magisk Source** | `native/src/boot/bootimg.h`, `bootimg.hpp` (repo root) |
| **Total Lines (BACKRabbit)** | 228 |

---

## Version-by-Version Cross-Reference

### AOSP Boot Image Headers (system/core/include/android/bootimg.h)
| BACKRabbit Line(s) | AOSP Header | Version |
|---|---|---|
| 9-30 | `boot_img_hdr` | v0 |
| 35-58 | `boot_img_hdr_v1` | v1 |
| 63-88 | `boot_img_hdr_v2` | v2 |
| 93-110 | `boot_img_hdr_v3` | v3 |
| 115-133 | `boot_img_hdr_v4` | v4 |
| 138-158 | Samsung PXA | - |
| 164-181 | `vendor_boot_img_hdr_v3` | vendor v3 |
| 187-208 | `vendor_boot_img_hdr_v4` | vendor v4 |
| 214-227 | `vendor_ramdisk_table_entry_v4` | vendor v4 |

### Magisk Headers (bootimg.h / bootimg.hpp)
| BACKRabbit Line(s) | Magisk Source | Notes |
|---|---|---|
| 9-30 | `bootimg.h` / `bootimg.hpp` | v0 |
| 35-58 | `bootimg.h` | v1 |
| 63-88 | `bootimg.h` | v2 |
| 93-110 | `bootimg.h` | v3 |
| 115-133 | `bootimg.h` | v4 |
| 138-158 | `bootimg.h` | PXA |
| 164-181 | `bootimg.h` | vendor v3 |
| 187-208 | `bootimg.h` | vendor v4 |
| 214-227 | `bootimg.h` | vendor ramdisk entry |

---

## Detailed Structure Mapping

### BootImgHdrV0 (Lines 9-30) - AOSP v0
```c
// AOSP (system/core/include/android/bootimg.h)
struct boot_img_hdr {
    uint8_t magic[8];          // "ANDROID!"
    uint32_t kernel_size;
    uint32_t kernel_addr;
    uint32_t ramdisk_size;
    uint32_t ramdisk_addr;
    uint32_t second_size;
    uint32_t second_addr;
    uint32_t tags_addr;
    uint32_t page_size;
    uint32_t header_version;   // = 0
    uint32_t os_version;
    uint8_t name[16];
    uint8_t cmdline[512];
    uint32_t id[8];
    uint8_t extra_cmdline[1024];
};
// Total: 1632 bytes (but page aligned to page_size, typically 2048)
```

**BACKRabbit mapping:** Direct port with `HEADER_SIZE = 1632`

### BootImgHdrV1 (Lines 35-58) - AOSP v1
```c
// Adds recovery DTBO support
struct boot_img_hdr_v1 {
    // ... v0 fields ...
    uint32_t recovery_dtbo_size;
    uint64_t recovery_dtbo_offset;
    uint32_t header_size;
};
// header_version = 1
```

**BACKRabbit mapping:** Direct port with `HEADER_SIZE = 1664`

### BootImgHdrV2 (Lines 63-88) - AOSP v2
```c
// Adds DTB support
struct boot_img_hdr_v2 {
    // ... v1 fields ...
    uint32_t dtb_size;
    uint64_t dtb_addr;
};
// header_version = 2
```

**BACKRabbit mapping:** Direct port with `HEADER_SIZE = 1696`

### BootImgHdrV3 (Lines 93-110) - AOSP v3 (GKI 1.0)
```c
// New format: no addresses, fixed 4096 page size
struct boot_img_hdr_v3 {
    uint8_t magic[8];
    uint32_t kernel_size;
    uint32_t ramdisk_size;
    uint32_t os_version;
    uint32_t header_size;      // = 2112
    uint32_t reserved[4];
    uint32_t header_version;   // = 3
    uint8_t cmdline[1536];     // BOOT_ARGS_SIZE + EXTRA_ARGS_SIZE
};
// header_version = 3, page_size = 4096 (fixed)
```

**BACKRabbit mapping:** Direct port with `HEADER_SIZE = 2112`, `FIXED_PAGE_SIZE = 4096`

### BootImgHdrV4 (Lines 115-133) - AOSP v4 (GKI 2.0)
```c
// Adds signature support
struct boot_img_hdr_v4 {
    // ... v3 fields ...
    uint32_t signature_size;
};
// header_version = 4
```

**BACKRabbit mapping:** Direct port with `HEADER_SIZE = 2116`

### BootImgHdrPxa (Lines 138-158) - Samsung PXA
```c
// Samsung-specific: detected by page_size >= 0x02000000
struct boot_img_hdr_pxa {
    uint8_t magic[8];
    uint32_t kernel_size, kernel_addr;
    uint32_t ramdisk_size, ramdisk_addr;
    uint32_t second_size, second_addr;
    uint32_t extra_size;       // instead of tags_addr
    uint32_t unknown;
    uint32_t tags_addr;
    uint32_t page_size;        // >= 0x02000000
    uint8_t name[24];          // 24 bytes (not 16)
    uint8_t cmdline[512];
    uint32_t id[8];
    uint8_t extra_cmdline[1024];
};
```

**BACKRabbit mapping:** Direct port with `PXA_PAGE_SIZE_THRESHOLD = 0x02000000`

### VendorBootImgHdrV3 (Lines 164-181) - Vendor v3 (GKI 2.0)
```c
// Magic: "VNDRBOOT"
struct vendor_boot_img_hdr_v3 {
    uint8_t magic[8];           // "VNDRBOOT"
    uint32_t header_version;    // = 3
    uint32_t page_size;
    uint32_t kernel_addr;
    uint32_t ramdisk_addr;
    uint32_t ramdisk_size;
    uint8_t cmdline[2048];      // VENDOR_BOOT_ARGS_SIZE
    uint32_t tags_addr;
    uint8_t name[16];
    uint32_t header_size;
    uint32_t dtb_size;
    uint64_t dtb_addr;
};
```

**BACKRabbit mapping:** Direct port with `MAGIC = "VNDRBOOT"`

### VendorBootImgHdrV4 (Lines 187-208) - Vendor v4 (Multiple Ramdisks)
```c
// Adds vendor ramdisk table
struct vendor_boot_img_hdr_v4 {
    // ... v3 fields ...
    uint32_t vendor_ramdisk_table_size;
    uint32_t vendor_ramdisk_table_entry_num;
    uint32_t vendor_ramdisk_table_entry_size;
    uint32_t bootconfig_size;
};
// header_version = 4
```

**BACKRabbit mapping:** Direct port

### VendorRamdiskTableEntryV4 (Lines 214-227) - Vendor v4 Entry
```c
struct vendor_ramdisk_table_entry_v4 {
    uint32_t ramdisk_size;
    uint32_t ramdisk_offset;
    uint32_t ramdisk_type;      // 0=none, 1=platform, 2=recovery, 3=dlkm
    uint8_t ramdisk_name[32];
    uint32_t board_id[16];      // 16 * 4 = 64 bytes
};
```

**BACKRabbit mapping:** Direct port with `SIZE = 116`, type constants

---

## Header Size Summary

| Version | Header Size | Page Size | Key Changes |
|---------|-------------|-----------|-------------|
| v0 | 1632 | From header (2048/4096) | Original |
| v1 | 1664 | From header | + recovery_dtbo |
| v2 | 1696 | From header | + dtb |
| v3 | 2112 | 4096 (fixed) | No addresses, larger cmdline |
| v4 | 2116 | 4096 (fixed) | + signature |
| PXA | Variable | ≥ 0x02000000 | Samsung-specific |
| Vendor v3 | Variable | From header | "VNDRBOOT" magic |
| Vendor v4 | Variable | From header | + ramdisk table |

---

## Missing/Partial Implementations

1. **BootImgHdrV4 in Parser** - Referenced in BootImageParser but struct defined here
2. **VendorBootImgHdrV4 in Parser** - Referenced but defined here
3. **Recovery Image Headers** - Not defined (separate format)
4. **DTB/Blob/DHTB Headers** - In BootImageParser.cs, not here

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| BootImgHdrV0_Size_MatchesAOSP | ❌ |
| BootImgHdrV1_Size_MatchesAOSP | ❌ |
| BootImgHdrV2_Size_MatchesAOSP | ❌ |
| BootImgHdrV3_Size_MatchesAOSP | ❌ |
| BootImgHdrV4_Size_MatchesAOSP | ❌ |
| BootImgHdrPxa_PXAThreshold | ❌ |
| VendorBootImgHdrV3_Magic_V3_Magic | ❌ |
| VendorBootImgHdrV_V4_RamdiskTable | ❌ |
| VendorRamdiskTableEntryV4_Size | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/Structures/BootHeaders/BootImgHeaders.cs` (228 lines)

**AOSP Sources:**
- `system/core/include/android/bootimg.h`
- `system/libbootloader/include/bootloader.h`

**Magisk Sources (in knowledge-base):**
- `v26.0_bootimg.h` through `v27.0_bootimg.h`
- `bootimg.hpp` (C++ header in repo root)