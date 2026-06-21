# Cross-Reference Map: BACKRabbit.MagiskCore.AvbRestorer.AvbRestorer.cs ↔ Magisk/AOSP Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/AvbRestorer/AvbRestorer.cs` |
| **Magisk Source** | `native/src/boot/avb.cpp`, `scripts/boot_patch.sh` (avb flags) |
| **AOSP Source** | `system/libavb/avb_vbmeta_image.h`, `system/libavb/avb_footer.h` |
| **Total Lines (BACKRabbit)** | 195 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v27.0
| BACKRabbit Line(s) | Magisk Source | Function |
|---|---|---|
| 33-110 | `avb.cpp` | `restore_verification_flags()` |
| 115-130 | `avb.cpp` | `find_avb_footer()` |
| 135-177 | `avb.cpp` | `patch_vbmeta_partition()` |

### Magisk avb.cpp (v26.3)
| BACKRabbit Line(s) | avb.cpp Line(s) | Notes |
|---|---|---|
| 33-110 | ~50-120 | Finds footer, reads vbmeta, patches flags 3→0 |
| 115-130 | ~120-140 | Searches for "AVB0" magic near end |
| 135-177 | ~140-180 | Direct vbmeta partition patch (noffset patching |

### AOSP AVB Structures
| BACKRabbit Line(s) | AOSP Source | Structure |
|---|---|---|
| 26 | `avb_vbmeta_image.h` | `AvbVBMetaImageHeader.flags` offset 88 |
| 50 | `avb_footer.h` | `AvbFooter.vbmeta_offset`, `vbmeta_size` |
| 117 | `avb_footer.h` | `AVB_FOOTER_MAGIC` = "AVB0" |

---

## Detailed Function Mapping

### RestoreVerificationFlags() - Lines 33-110
**Magisk Equivalent:** `restore_verification_flags()` in avb.cpp

```cpp
// Magisk v26.3 (avb.cpp ~line 50)
bool AvbRestorer::restore_verification_flags(vector<uint8_t>& img) {
    // 1. Find AVB footer (AVB0 magic in last 1KB)
    ssize_t footer_off = find_avb_footer(img);
    if (footer_off < 0) return false;
    
    // 2. Read footer to get vbmeta offset/size
    auto footer = *reinterpret_cast<const AvbFooter*>(img.data() + footer_off);
    
    // 3. Calculate vbmeta start
    ssize_t vbmeta_start = footer_off - footer.vbmeta_offset;
    if (vbmeta_start < 0) return false;
    
    // 4. Read vbmeta header
    auto vbmeta = *reinterpret_cast<const AvbVBMetaImageHeader*>(img.data() + vbmeta_start);
    
    // 5. Check flags (offset 88 in header)
    if (vbmeta.flags == AVB_VBMETA_IMAGE_FLAGS_DISABLE_VERIFICATION | 
        AVB_VBMETA_IMAGE_FLAGS_DISABLE_VERITY) {  // = 3
        
        // 6. Patch flags to 0
        BinaryPrimitives.WriteUInt32LittleEndian(
            img.data() + vbmeta_start + 88, 0);
        return true;
    }
    
    return true; // Already 0
}
```

**BACKRabbit mapping:**
- Lines 38-44: FindAvbFooter()
- Lines 50-56: Read footer, get vbmeta_offset/size
- Lines 59-65: Calculate vbmeta_start
- Lines 71-72: Read vbmeta header
- Lines 75-77: Check current flags
- Lines 79-109: Patch flags 3→0 if needed

### FindAvbFooter() - Lines 115-130
**Magisk Equivalent:** `find_avb_footer()` in avb.cpp

```cpp
// Magisk v26.3 (avb.cpp ~line 120)
ssize_t AvbRestorer::find_avb_footer(const vector<uint8_t>& img) {
    const uint8_t magic[] = { 'A', 'V', 'B', '0' };
    size_t footer_size = sizeof(AvbFooter);
    size_t min_offset = max(0, img.size() - 1024);
    
    for (size_t i = img.size() - footer_size; i >= min_offset; i--) {
        if (memcmp(img.data() + i, magic, 4) == 0) {
            return i;
        }
    }
    return -1;
}
```

**BACKRabbit mapping:** Lines 117-129 direct port.

### PatchVbmetaPartition() - Lines 135-177
**Magisk Equivalent:** `patch_vbmeta()` in avb.cpp

```cpp
// Magisk v26.3 (avb.cpp ~line 140)
bool AvbRestorer::patch_vbmeta(vector<uint8_t>& vbmeta) {
    if (vbmeta.size() < sizeof(AvbVBMetaImageHeader)) return false;
    
    // Check magic
    if (memcmp(vbmeta.data(), "AVB0", 4) != 0) return false;
    
    // Read flags at offset 88
    uint32_t flags = *reinterpret_cast<uint32_t*>(vbmeta.data() + 88);
    
    if (flags == 3) {
        *reinterpret_cast<uint32_t*>(vbmeta.data() + 88) = 0;
        return true;
    }
    return true;
}
```

**BACKRabbit mapping:** Lines 146-176 direct port.

---

## AVB Structures

### AvbVBMetaImageHeader (from AOSP avb_vbmeta_image.h)
```c
// Offset 0:   uint8_t magic[4] = "AVB0"
// Offset 4:   uint32_t required_libavb_version
// Offset 8:   uint64_t authentication_data_block_size
// Offset 16:  uint64_t authentication_data_block_offset
// Offset 24:  uint64_t auxiliary_data_block_size
// Offset 32:  uint64_t auxiliary_data_block_offset
// Offset 40:  uint64_t public_key_metadata_size
// Offset 48:  uint64_t public_key_metadata_offset
// Offset 56:  uint64_t descriptors_size
// Offset 64:  uint64_t descriptors_offset
// Offset 72:  uint64_t rollback_index
// Offset 80:  uint64_t rollback_index_location
// Offset 88:  uint32_t flags  ← TARGET FIELD
// Offset 92:  uint32_t release_string_size
// Offset 96:  uint64_t release_string_offset
```

### AvbFooter (from AOSP avb_footer.h)
```c
// Offset 0:   uint8_t magic[4] = "AVB0"
// Offset 4:   uint32_t version
// Offset 8:   uint64_t original_image_size
// Offset 16:  uint64_t vbmeta_offset
// Offset 24:  uint64_t vbmeta_size
// Offset 32:  uint8_t reserved[32]
// Total: 64 bytes
```

---

## AVB Flags

| Value | Constant | Meaning |
|---|---|---|
| 0 | `AVB_FLAGS_ENABLED` | Verification enabled (stock) |
| 1 | `AVB_VBMETA_IMAGE_FLAGS_DISABLE_VERIFICATION` | Disable verification |
| 2 | `AVB_VBMETA_IMAGE_FLAGS_DISABLE_VERITY` | Disable dm-verity |
| 3 | `AVB_FLAGS_DISABLED` | Both disabled (Magisk patched) |

**Magisk sets flags = 3 (disable-verity | disable-verification)**
**BACKRabbit restores flags = 0**

---

## Data Structures

### AvbRestoreResult (Lines 183-195)
```csharp
public class AvbRestoreResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool FooterFound { get; set; }
    public long FooterOffset { get; set; }
    public ulong OriginalImageSize { get; set; }
    public ulong VbmetaOffset { get; set; }
    public ulong VbmetaSize { get; set; }
    public uint CurrentFlags { get; set; }
    public bool FlagsChanged { get; set; }
    public byte[]? PatchedImage { get; set; }
}
```

---

## Usage Examples

### Restore AVB Flags in Boot Image
```csharp
var restorer = new AvbRestorer();
var bootImage = File.ReadAllBytes("boot_magisk.img");

var result = restorer.RestoreVerificationFlags(bootImage);
if (result.Success) {
    Console.WriteLine(result.Message);
    if (result.FlagsChanged) {
        File.WriteAllBytes("boot_stock.img", result.PatchedImage!);
        Console.WriteLine("Saved patched image");
    }
} else {
    Console.WriteLine($"Failed: {result.Message}");
}
```

### Patch Separate vbmeta Partition
```csharp
var restorer = new AvbRestorer();
var vbmetaData = File.ReadAllBytes("vbmeta.img");

var result = restorer.PatchVbmetaPartition(vbmetaData);
if (result.Success && result.FlagsChanged) {
    File.WriteAllBytes("vbmeta_patched.img", result.PatchedImage!);
}
```

---

## Samsung Knox Warning

> **Critical**: On Samsung devices, the Knox warranty bit (eFuse) is a **hardware fuse** that permanently trips when:
> - Custom kernel flashed
> - Boot image modified
> - System partition modified
> 
> **This cannot be restored by any software tool.** AvbRestorer restores kernel *software* state only.

---

## Missing/Partial Implementations

1. **Hash Chain Verification** - Doesn't verify vbmeta hash chain
2. **Descriptor Parsing** - Doesn't parse AVB descriptors
3. **Rollback Index** - Doesn't check/update rollback index
4. **Public Key Metadata** - Doesn't handle key rotation
5. **AVB 2.0 Features** - Chained partitions not supported
6. **Signature Recalculation** - Flags patched but signature not recalculated (needs private key)

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| RestoreVerificationFlags_AVB0Footer_Finds | ❌ |
| RestoreVerificationFlags_Flags3_PatchesTo0 | ❌ |
| RestoreVerificationFlags_Flags0_NoChange | ❌ |
| RestoreVerificationFlags_NoFooter_ReturnsFalse | ❌ |
| RestoreVerificationFlags_InvalidOffset_ReturnsFalse | ❌ |
| PatchVbmetaPartition_ValidMagic_PatchesFlags | ❌ |
| PatchVbmetaPartition_InvalidMagic_ReturnsFalse | ❌ |
| FindAvbFooter_Within1KB_Finds | ❌ |
| FindAvbFooter_NotFound_ReturnsMinus1 | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/AvbRestorer/AvbRestorer.cs` (195 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_avb.cpp` through `v27.0_avb.cpp`
- `scripts/boot_patch.sh` (avb functions)

**AOSP Sources:**
- `system/libavb/avb_vbmeta_image.h` - VBMeta header
- `system/libavb/avb_footer.h` - AVB footer
- `system/libavb/avb.h` - Main AVB definitions