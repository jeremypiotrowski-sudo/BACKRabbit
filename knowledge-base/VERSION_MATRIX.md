# Magisk Version Matrix — Native Boot Image Source Evolution

## Overview

This document maps every Magisk version from v25.0 through v30.7 (plus canary builds) to its native boot image source files, tracking the evolution of the codebase that BACKRabbit ports to C#.

**Source:** `topjohnwu/Magisk` git mirror at `knowledge-base/magisk-versions/magisk-mirror/`
**Extracted files:** 340 files across 32 versions
**Earliest native boot code:** v25.0 (earlier versions used Java/shell scripts for boot image manipulation)

---

## File Inventory Per Version

| Version | Files | Key Changes |
|---------|-------|-------------|
| **v25.0** | 5 | Initial native boot code: bootimg.cpp/hpp, compress.cpp, cpio.rs, format.cpp |
| **v25.1** | 5 | Minor fixes |
| **v25.2** | 5 | Minor fixes |
| **v26.0** | 16 | **Major expansion**: +compress.hpp, +format.hpp, +dtb.cpp/hpp, +magiskboot.hpp, +patch.rs, +payload.rs, +ramdisk.rs, +rootdir.cpp, +selinux.cpp, +sign.rs |
| **v26.1** | 16 | Refinements |
| **v26.2** | 16 | Refinements |
| **v26.3** | 18 | +rootdir.rs, +selinux.hpp |
| **v26.4** | 16 | Removed rootdir.rs, selinux.hpp |
| **v27.0** | 16 | Stable v26.x structure |
| **v28.0** | 11 | **Consolidation**: -dtb.cpp/hpp, -ramdisk.rs, -rootdir.cpp, -selinux.cpp (moved to Rust core) |
| **v28.1** | 11 | Refinements |
| **v29.0** | 11 | Stable v28.x structure |
| **v30.0** | 9 | **Rust migration**: -compress.cpp/hpp (compression moved to Rust `compress_bytes`) |
| **v30.1** | 9 | Refinements |
| **v30.2** | 9 | Refinements |
| **v30.3** | 7 | **Further consolidation**: -format.cpp/hpp (format detection merged into bootimg.cpp) |
| **v30.4** | 7 | Refinements |
| **v30.5** | 7 | Refinements |
| **v30.6** | 7 | Refinements |
| **v30.7** | 7 | Current stable |
| **canary-27005→29001** | 11 each | Bleeding-edge builds, v28.x structure |

---

## File-by-File Evolution

### `bootimg.cpp` — Main Boot Image Parser/Repacker
| Version | Lines | Key Features |
|---------|-------|-------------|
| v25.0 | 788 | Basic v0-v3 parsing, no vendor v4, no zImage, no AVB |
| v26.0 | 900+ | +vendor v3/v4, +zImage parsing, +AVB footer, +DTB finder, +MTK headers |
| v27.0 | 950+ | +NOOKHD/ACCLAIM/AMONET, +LZ4_LG detection, +bootconfig |
| v28.0 | 980+ | +`dyn_img_hdr` refactor, +`parse_zimage()`, +vendor ramdisk table |
| v29.0 | 1000+ | Refinements |
| v30.0 | 1020+ | Format detection merged in (`check_fmt` now in bootimg.cpp) |
| v30.3 | 1040+ | `compress_len`/`decompress` now call Rust FFI |
| v30.7 | 1064 | Current: full v0-v4, vendor v3-v4, all Samsung/MTK/LG/Nook/ChromeOS quirks |

### `bootimg.hpp` — Header Structures
| Version | Lines | Key Features |
|---------|-------|-------------|
| v25.0 | ~200 | v0-v3 headers, PXA |
| v26.0 | ~300 | +v4, +vendor v3/v4, +AvbFooter, +AvbVBMetaImageHeader, +mtk_hdr, +zimage_hdr |
| v27.0 | ~320 | +vendor_ramdisk_table_entry_v4, +dhtb_hdr, +blob_hdr |
| v28.0+ | ~350 | Stable, minor additions |

### `format.cpp` / `format.hpp` — Format Detection
| Version | Status |
|---------|--------|
| v25.0-v25.2 | `format.cpp` only (no hpp) |
| v26.0-v30.2 | `format.cpp` + `format.hpp` — `FileFormat` enum, `check_fmt()`, `check_fmt_lg()`, `fmt2name()` |
| v30.3+ | **Removed** — merged into `bootimg.cpp` |

### `compress.cpp` / `compress.hpp` — Compression
| Version | Status |
|---------|--------|
| v25.0-v25.2 | `compress.cpp` only |
| v26.0-v29.0 | `compress.cpp` + `compress.hpp` — gzip, lz4, lz4_legacy, lz4_lg, xz, lzma, bzip2, lzop, zopfli |
| v30.0+ | **Removed** — moved to Rust `compress_bytes()` / `decompress_bytes()` FFI |

### `cpio.rs` — CPIO Archive (Rust)
| Version | Status |
|---------|--------|
| v25.0-v30.7 | Present in ALL versions — newc format (070701/070702) parser/serializer |
| v26.0+ | +`CpioResult` enum, +`add_file()`, +`rm_file()`, +`mv_file()`, +`patch_cmdline()` |

### `magiskboot.hpp` — Shared Constants/Macros
| Version | Status |
|---------|--------|
| v26.0+ | Present — `FileFormat` enum, magic bytes, `check_fmt()`, `unpack()`, `repack()`, `cleanup()` declarations |

### `patch.rs` — Magisk Patching Logic (Rust)
| Version | Status |
|---------|--------|
| v26.0+ | Present — `patch_boot_img()`, `patch_vbmeta_flag()`, `patch_ramdisk()`, `patch_sepolicy()` |

### `payload.rs` — Payload Handling (Rust)
| Version | Status |
|---------|--------|
| v26.0+ | Present — `PayloadConfig`, `extract_payload()`, `sign_payload()` |

### `sign.rs` — Boot Image Signing (Rust)
| Version | Status |
|---------|--------|
| v26.0+ | Present — AVB1 signing, `sign_payload()`, `verify()` |

### `ramdisk.rs` — Ramdisk Operations (Rust)
| Version | Status |
|---------|--------|
| v26.0-v27.0 | Present — `patch_ramdisk()`, Magisk init injection |
| v28.0+ | **Removed** — merged into `patch.rs` or Rust core |

### `rootdir.cpp` — Root Directory Setup
| Version | Status |
|---------|--------|
| v26.0-v27.0 | Present — `inject_magisk_rc()`, overlay.d setup |
| v28.0+ | **Removed** — moved to Rust `rootdir.rs` in main Magisk crate |

### `selinux.cpp` — SELinux Policy Patching
| Version | Status |
|---------|--------|
| v26.0-v27.0 | Present — `patch_sepolicy()`, sepolicy rules |
| v28.0+ | **Removed** — moved to Rust `selinux.rs` in main Magisk crate |

### `dtb.cpp` / `dtb.hpp` — Device Tree Blob
| Version | Status |
|---------|--------|
| v26.0-v27.0 | Present — `find_dtb_offset()`, FDT header parsing |
| v28.0+ | **Removed** — merged into `bootimg.cpp` |

---

## BACKRabbit Compatibility Matrix

| Magisk Version | BACKRabbit Coverage | Notes |
|----------------|---------------------|-------|
| v25.0-v25.2 | ✅ Full | BootImageParser handles v0-v3, FormatDetector covers all formats |
| v26.0-v26.4 | ✅ Full | +AvbRestorer, +MagiskArtifactDetector, +SamsungKernelPatcher |
| v27.0 | ✅ Full | All structures supported |
| v28.0-v28.1 | ✅ Full | DTB merged into parser, ramdisk/rootdir/selinux handled |
| v29.0 | ✅ Full | Stable |
| v30.0-v30.2 | ✅ Full | Compression handled by CompressionEngine (pure C#) |
| v30.3-v30.7 | ✅ Full | Format detection merged into BootImageParser |
| canary-* | ⚠️ Partial | Bleeding-edge changes may not be fully ported |

---

## Key Architectural Shifts

### v25 → v26: The Big Bang
- Rust enters: cpio.rs, patch.rs, payload.rs, sign.rs, ramdisk.rs
- DTB support added
- AVB footer/vbmeta detection added
- Vendor boot v3/v4 support
- MTK headers, zImage parsing
- magiskboot.hpp consolidates constants

### v26 → v28: Consolidation
- DTB code merged into bootimg.cpp
- Ramdisk/rootdir/selinux move to main Rust crate
- Cleaner separation: bootimg.cpp handles parsing/repacking, Rust handles patching

### v28 → v30: Rust Migration
- Compression moves entirely to Rust FFI (`compress_bytes`/`decompress_bytes`)
- Format detection merges into bootimg.cpp
- Final state (v30.7): 7 files — bootimg.cpp/hpp, cpio.rs, magiskboot.hpp, patch.rs, payload.rs, sign.rs

---

## BACKRabbit's C# Port Strategy

BACKRabbit ports the **C++ logic** to C# while keeping the **Rust logic** as reference:

| Magisk C++ | BACKRabbit C# |
|------------|---------------|
| `bootimg.cpp` | `BootImageParser.cs` + `BootImageRepacker.cs` |
| `bootimg.hpp` | `Structures/BootHeaders/` |
| `format.cpp` | `FormatDetector.cs` |
| `compress.cpp` | `CompressionEngine.cs` (pure C#, no CLI) |
| `cpio.rs` | `CpioArchive.cs` |
| `patch.rs` | `MagiskArtifactDetector.cs` + `MagiskUninstaller.cs` |
| `sign.rs` | `AvbRestorer.cs` |
| `magiskboot.hpp` | Constants spread across FormatDetector + Structures |

---

## Version Timeline

```
v25.0 ─── v25.1 ─── v25.2 ─── v26.0 ─── v26.1 ─── v26.2 ─── v26.3 ─── v26.4
  │                                 │
  └─ 5 files                        └─ 16 files (Rust enters, DTB, AVB, vendor v4)

v27.0 ─── v28.0 ─── v28.1 ─── v29.0 ─── v30.0 ─── v30.1 ─── v30.2 ─── v30.3 ─── v30.4 ─── v30.5 ─── v30.6 ─── v30.7
  │        │                          │         │                           │
  └─ 16    └─ 11 (consolidation)      └─ 11     └─ 9 (compress→Rust)       └─ 7 (format merged)
```

---

## Last Updated
2026-06-18 — Generated from git mirror extraction