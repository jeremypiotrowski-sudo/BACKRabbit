# BACKRabbit — Verification Checklist

## Purpose
Step-by-step verification procedures to confirm each BACKRabbit component works correctly. Use this checklist before flashing any device.

---

## 1. Format Detection Verification

### 1.1 Magic Byte Detection
- [ ] **Gzip**: `0x1F 0x8B` → `FileFormat.GZIP`
- [ ] **Bzip2**: `0x42 0x5A 0x68` ("BZh") → `FileFormat.BZIP2`
- [ ] **LZ4**: `0x02 0x21 0x4C 0x18` → `FileFormat.LZ4`
- [ ] **LZ4 Legacy**: `0x04 0x22 0x4C 0x18` → `FileFormat.LZ4_LEGACY`
- [ ] **XZ**: `0xFD 0x37 0x7A 0x58 0x5A 0x00` → `FileFormat.XZ`
- [ ] **LZMA**: `0x5D` + valid dict size + `0xFF` padding → `FileFormat.LZMA`
- [ ] **AOSP Boot**: `"ANDROID!"` → `FileFormat.AOSP`
- [ ] **Vendor Boot**: `"VNDRBOOT"` → `FileFormat.AOSP_VENDOR`
- [ ] **ChromeOS**: `"CHROMEOS"` → `FileFormat.CHROMEOS`
- [ ] **MTK**: `0x88 0x16 0x88 0x16` → `FileFormat.MTK`
- [ ] **DTB**: `0xD0 0x0D 0xFE 0xED` → `FileFormat.DTB`
- [ ] **DHTB**: `"DHTBHDR!"` → `FileFormat.DHTB`
- [ ] **BLOB**: `"-SIGNED-BY-SIGNBLOB-"` → `FileFormat.BLOB`
- [ ] **zImage**: `0x18 0x28 0x6F 0x01` at offset 0x24 → `FileFormat.ZIMAGE`
- [ ] **SEANDROID**: `"SEANDROIDENFORCE"` → `FileFormat.SEANDROID`
- [ ] **AVB Footer**: `"AVBf"` → `FileFormat.AVB_FOOTER`
- [ ] **AVB Magic**: `"AVB0"` → `FileFormat.AVB`

### 1.2 Compression Detection
- [ ] `IsCompressed(GZIP)` → `true`
- [ ] `IsCompressed(LZ4)` → `true`
- [ ] `IsCompressed(AOSP)` → `false`
- [ ] `IsCompressed(UNKNOWN)` → `false`

---

## 2. Compression Round-Trip Verification

### 2.1 Gzip
- [ ] Compress "Hello World" → gzip bytes
- [ ] Decompress gzip bytes → "Hello World"
- [ ] Verify: original == decompressed

### 2.2 Bzip2
- [ ] Compress test data (1KB+) → bzip2 bytes
- [ ] Decompress bzip2 bytes → original data
- [ ] Verify: original == decompressed

### 2.3 LZ4
- [ ] Compress test data (1KB+) → LZ4 bytes
- [ ] Decompress LZ4 bytes → original data
- [ ] Verify: original == decompressed

### 2.4 LZ4 Legacy
- [ ] Compress test data → LZ4 Legacy bytes
- [ ] Decompress LZ4 Legacy bytes → original data
- [ ] Verify: original == decompressed

### 2.5 XZ
- [ ] Compress test data (1KB+) → XZ bytes
- [ ] Decompress XZ bytes → original data
- [ ] Verify: original == decompressed

### 2.6 LZMA
- [ ] Compress test data (1KB+) → LZMA bytes
- [ ] Decompress LZMA bytes → original data
- [ ] Verify: original == decompressed

---

## 3. CPIO Archive Verification

### 3.1 Parse/Serialize Round-Trip
- [ ] Create CpioArchive with 3 entries (file1.txt, dir/, file2.bin)
- [ ] Serialize to bytes
- [ ] Parse bytes back to CpioArchive
- [ ] Verify: entry count matches (3)
- [ ] Verify: all names match
- [ ] Verify: all data matches (byte-for-byte)
- [ ] Verify: TRAILER!!! present at end

### 3.2 Edge Cases
- [ ] Empty file (0 bytes) → parses correctly
- [ ] Large file (1MB+) → parses correctly
- [ ] Deep paths (a/b/c/d/e/file) → parses correctly
- [ ] Special characters in names → parses correctly
- [ ] Multiple entries with same name → all preserved

### 3.3 Operations
- [ ] `AddFile()` → new entry appears in archive
- [ ] `RemoveFile()` → entry removed, archive still valid
- [ ] `MoveFile()` → entry renamed, data preserved
- [ ] `PatchCmdline()` → cmdline modified correctly

---

## 4. Boot Image Parser Verification

### 4.1 Header Parsing (v0-v4)
- [ ] Parse known v0 boot image → `HeaderVersion == 0`, `HeaderV0` populated
- [ ] Parse known v1 boot image → `HeaderVersion == 1`, `HeaderV1` populated
- [ ] Parse known v2 boot image → `HeaderVersion == 2`, `HeaderV2` populated
- [ ] Parse known v3 boot image → `HeaderVersion == 3`, `HeaderV3` populated
- [ ] Parse known v4 boot image → `HeaderVersion == 4`, `HeaderV4` populated

### 4.2 Vendor Boot
- [ ] Parse vendor v3 image → `IsVendor == true`, `HeaderV3Vendor` populated
- [ ] Parse vendor v4 image → `IsVendor == true`, `HeaderV4Vendor` populated

### 4.3 Samsung Formats
- [ ] Parse PXA image → `HeaderPxa` populated, `page_size >= 0x02000000`
- [ ] Parse DHTB image → `HasDhtb == true`, `HasSeandroid == true`
- [ ] Parse BLOB image → `HasBlob == true`

### 4.4 AVB Detection
- [ ] Parse AVB-signed image → AVB footer found, vbmeta detected
- [ ] Parse non-AVB image → no AVB footer, no error

### 4.5 Extraction
- [ ] `ExtractKernel()` → returns correct kernel bytes
- [ ] `ExtractRamdisk()` → returns correct ramdisk bytes (compressed)
- [ ] `ExtractRamdiskArchive()` → returns parsed CpioArchive
- [ ] `ExtractDtb()` → returns DTB bytes (if present)

### 4.6 Error Handling
- [ ] Parse random data → throws `InvalidDataException`
- [ ] Parse truncated image → throws `InvalidDataException`
- [ ] Parse empty file → throws `InvalidDataException`

---

## 5. Boot Image Repacker Verification

### 5.1 Round-Trip (No Modification)
- [ ] Parse stock boot.img → BootImage
- [ ] Repack with same ramdisk/kernel → new_boot.img
- [ ] Parse new_boot.img → BootImage2
- [ ] Verify: header version matches
- [ ] Verify: kernel size matches
- [ ] Verify: ramdisk size matches
- [ ] Verify: page size matches
- [ ] Verify: cmdline matches
- [ ] Verify: SHA256 ID matches (if v4)

### 5.2 Modified Ramdisk
- [ ] Parse boot.img → BootImage
- [ ] Modify ramdisk (add/remove files)
- [ ] Repack with modified ramdisk → new_boot.img
- [ ] Parse new_boot.img → BootImage2
- [ ] Verify: ramdisk size updated correctly
- [ ] Verify: kernel unchanged
- [ ] Verify: page alignment correct

### 5.3 AVB Preservation
- [ ] Repack AVB-signed image
- [ ] Verify: AVB footer present in output
- [ ] Verify: vbmeta_offset updated
- [ ] Verify: original_image_size updated

### 5.4 Samsung Format Preservation
- [ ] Repack DHTB image → DHTB header preserved
- [ ] Repack SEANDROID image → SEANDROID magic preserved
- [ ] Repack BLOB image → BLOB header preserved

---

## 6. AVB Restorer Verification

### 6.1 Flag Restoration
- [ ] Load Magisk-patched image (flags=3)
- [ ] Run `RestoreVerificationFlags()`
- [ ] Verify: flags changed from 3 to 0
- [ ] Verify: other vbmeta fields unchanged

### 6.2 Footer Detection
- [ ] Image with AVB footer → footer found, offset correct
- [ ] Image without AVB footer → returns failure, no crash

### 6.3 Edge Cases
- [ ] Already-stock image (flags=0) → no change, success
- [ ] Corrupted AVB footer → graceful failure

---

## 7. Magisk Artifact Detector Verification

### 7.1 Detection
- [ ] Ramdisk with overlay.d/sbin/magisk.xz → detected
- [ ] Ramdisk with .backup/.magisk → detected, config parsed
- [ ] Ramdisk with ramdisk.cpio.orig → `HasFullBackup == true`
- [ ] Ramdisk with patched fstab → `HasVerityPatches == true`
- [ ] Stock ramdisk (no Magisk) → `IsMagiskInstalled == false`

### 7.2 Restoration Methods
- [ ] Method 1 (Stock flash) → returns stock image path
- [ ] Method 2 (Backup restore) → restores from ramdisk.cpio.orig
- [ ] Method 3 (Surgical) → removes all detected artifacts

### 7.3 Post-Restoration Verification
- [ ] After restore: no overlay.d/ entries
- [ ] After restore: no .backup/ entries
- [ ] After restore: init is stock binary
- [ ] After restore: fstab verity flags restored

---

## 8. Samsung Kernel Patcher Verification

### 8.1 Detection
- [ ] Kernel with RKP magic → `HasRkp == true`
- [ ] Kernel with DEFEX magic → `HasDefex == true`
- [ ] Kernel with PROCA magic → `HasProca == true`
- [ ] Kernel with KNOX magic → `HasKnox == true`
- [ ] Stock kernel → all false

### 8.2 Syscall Table Analysis
- [ ] Stock syscall prologue detected → `IsPatched == false`
- [ ] Hook branch detected → `IsPatched == true`
- [ ] Hook count reported correctly

### 8.3 Patch Reversal
- [ ] `RevertPatches()` on patched kernel → stock state
- [ ] `VerifyRestoration()` → confirms reversal

---

## 9. End-to-End Integration Test

### 9.1 Full Uninstall Pipeline (Dry Run)
- [ ] Load stock firmware boot.img
- [ ] Parse → BootImage
- [ ] Extract ramdisk → decompress → CpioArchive
- [ ] Detect Magisk → (should be clean)
- [ ] Repack without changes → new_boot.img
- [ ] Parse new_boot.img → verify identical structure
- [ ] AVB flags verified (0 → 0, no change)

### 9.2 Full Uninstall Pipeline (Magisk-Patched Image)
- [ ] Load Magisk-patched boot.img
- [ ] Parse → BootImage (verify flags=3)
- [ ] Extract ramdisk → decompress → CpioArchive
- [ ] Detect Magisk → artifacts found
- [ ] Restore (Method 2 or 3) → clean CpioArchive
- [ ] Repack → new_boot.img
- [ ] AVB restore → flags=0
- [ ] Parse new_boot.img → verify clean
- [ ] Verify: no Magisk artifacts in new ramdisk
- [ ] Verify: AVB flags = 0

---

## 10. Protocol Verification (Requires Device)

### 10.1 ADB
- [ ] Connect to device via USB
- [ ] Authenticate (RSA key)
- [ ] Execute shell command → response received
- [ ] Push file → file appears on device
- [ ] Pull file → file received correctly

### 10.2 Fastboot
- [ ] Connect to device in fastboot mode
- [ ] `getvar:product` → returns product name
- [ ] `getvar:current-slot` → returns slot
- [ ] Flash boot image → success
- [ ] Reboot → device boots

### 10.3 Download Mode
- [ ] Connect to device in Download Mode
- [ ] Initialize session → success
- [ ] Read PIT → partition table received
- [ ] Flash partition → success
- [ ] Reboot → device boots

---

## Test Vectors (Known-Good Hashes)

### Sample Boot Image v2 (Generic)
```
Header Version: 2
Page Size: 4096
Kernel Size: [varies]
Ramdisk Size: [varies]
Cmdline: androidboot.hardware=qcom
```

### Sample Boot Image v4 (GKI)
```
Header Version: 4
Page Size: 4096
Signature Size: 4096
Ramdisk Compression: LZ4 Legacy
AVB Footer: Present
VBMeta Flags: 0 (stock) or 3 (Magisk-patched)
```

### Sample CPIO Archive
```
Entries: init, fstab.qcom, sepolicy, TRAILER!!!
Format: newc (070701)
Compression: gzip (before CPIO parse)
```

---

## Quick Verification Script

```bash
# Run existing unit tests
dotnet test BACKRabbit.Tests

# Expected output:
#   FormatDetector_DetectsGzip: PASS
#   FormatDetector_DetectsLZ4: PASS
#   FormatDetector_DetectsBootImage: PASS
#   CpioArchive_ParseAndSerialize_RoundTrip: PASS
#   CompressionEngine_Gzip_RoundTrip: PASS
```

---

## Last Updated
2026-06-18 — Generated from codebase audit