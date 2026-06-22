# BACKRabbit ↔ Magisk Cross-Reference Index

## Unified Function Mapping

This index maps every BACKRabbit C# function to its corresponding Magisk C++/Rust source across all versions (v25.0-v30.7).

---

## v2.0 New Files (No Magisk Equivalent — Orchestration Layer)

| BACKRabbit File | Lines | Purpose |
|-----------------|-------|---------|
| `BACKRabbit.CLI/Wizard/WizardRunner.cs` | ~500 | 7-step interactive CLI wizard (orchestration) |
| `BACKRabbit.CLI/Testing/MockAdbClient.cs` | 218 | Test mode ADB client (implements IAdbClient) |
| `BACKRabbit.Protocol.Adb/IAdbClient.cs` | 45 | ADB client interface (enables test mode) |
| `BACKRabbit.Tests/F966U1IntegrationTests.cs` | 250 | Real firmware integration test (Z Fold 6) |
| `emergency-flasher/emergency_fix.py` | 300+ | Emergency USB recovery script |

## v2.0 Deleted Files

| File | v1.0 Lines | Reason |
|------|-----------|--------|
| `BACKRabbit.GUI/MainForm.cs` | 312 | WinForms — replaced by CLI wizard |
| `BACKRabbit.GUI/Tabs/MagiskPanel.cs` | 839 | WinForms — replaced by WizardRunner.cs |
| `BACKRabbit.GUI/Tabs/AdbPanel.cs` | ~200 | WinForms — replaced by CLI commands |
| `BACKRabbit.GUI/Tabs/FastbootPanel.cs` | ~200 | WinForms — replaced by CLI commands |
| `BACKRabbit.GUI/Tabs/DownloadModePanel.cs` | ~200 | WinForms — replaced by CLI commands |
| `BACKRabbit.GUI/Dialogs/InputDialog.cs` | ~50 | VB.NET InputBox — replaced by Spectre.Console |
| `BACKRabbit.GUI/Branding/` | ~100 | Rabbit animation — removed |
| `Class1.cs` ×7 | ~10 each | Placeholder stubs — all deleted |
| `UnitTest1.cs` | ~10 | Placeholder test — deleted |

---

## BootImageParser.cs ↔ bootimg.cpp

| BACKRabbit Function | Lines | Magisk Function | Magisk Lines (v30.7) | Versions |
|---------------------|-------|-----------------|----------------------|----------|
| `Parse(string path)` | 23-27 | `boot_img::boot_img(const char *image)` | ~50-55 | v25.0+ |
| `Parse(byte[] data)` | 32-68 | `boot_img::boot_img()` constructor body | ~55-120 | v25.0+ |
| `ParseHeader()` | 70-135 | `boot_img::parse_hdr()` | ~200-280 | v25.0+ |
| `ParseSections()` | 137-197 | `boot_img::parse_image()` | ~280-380 | v25.0+ |
| `ParseTail()` | 199-221 | tail parsing in `parse_image()` | ~380-420 | v26.0+ |
| `FindAvbFooter()` | 223-250 | AVB footer detection in `parse_image()` | ~420-450 | v26.0+ |
| `ParseVendorRamdiskTable()` | 252-266 | `boot_img::vendor_ramdisk_tbl()` | ~340-360 | v26.0+ |
| `GetHeaderSize()` | 270-290 | `dyn_img_hdr::hdr_size()` | ~150-170 | v25.0+ |
| `GetPageSize()` | 292-310 | `dyn_img_hdr::page_size()` | ~130-140 | v25.0+ |
| `ExtractKernel()` | 408-420 | `unpack()` kernel section | ~500-520 | v25.0+ |
| `ExtractRamdisk()` | 422-435 | `unpack()` ramdisk section | ~520-540 | v25.0+ |
| `ExtractRamdiskArchive()` | 437-444 | `unpack()` + `load_cpio()` | ~520-540 | v25.0+ |
| `ExtractDtb()` | 446-460 | `unpack()` dtb section | ~560-570 | v26.0+ |

---

## BootImageRepacker.cs ↔ bootimg.cpp repack()

| BACKRabbit Function | Lines | Magisk Function | Magisk Lines (v30.7) | Versions |
|---------------------|-------|-----------------|----------------------|----------|
| `Repack()` | 29-38 | `repack()` entry | ~620-640 | v25.0+ |
| `WriteHeader()` | 40-80 | header write in `repack()` | ~640-680 | v25.0+ |
| Kernel write | 54-63 | kernel block in `repack()` | ~680-720 | v25.0+ |
| Ramdisk write | 65-67 | ramdisk block in `repack()` | ~720-760 | v25.0+ |
| Second/Extra write | 69-80 | second/extra blocks | ~760-790 | v25.0+ |
| DTB write | 81-90 | dtb block | ~790-810 | v26.0+ |
| Signature write | 91-100 | signature block | ~810-820 | v26.0+ |
| Vendor ramdisk table | 101-115 | vendor ramdisk table | ~820-850 | v26.0+ |
| Bootconfig write | 116-125 | bootconfig block | ~850-860 | v27.0+ |
| SEANDROID/LG_BUMP | 126-140 | proprietary stuffs | ~860-880 | v25.0+ |
| AVB vbmeta write | 141-160 | vbmeta block | ~880-900 | v26.0+ |
| MTK header patch | 161-180 | MTK header patching | ~900-920 | v26.0+ |
| Checksum update | 181-220 | SHA256 ID recalculation | ~920-960 | v25.0+ |
| AVB footer patch | 221-250 | AVB footer patching | ~960-990 | v26.0+ |
| DHTB/BLOB header | 251-270 | DHTB/BLOB header update | ~990-1010 | v25.0+ |
| AVB1 signing | 271-300 | `sign_payload()` | ~1010-1030 | v26.0+ |
| `GetHeaderSize()` | 310-330 | `dyn_img_hdr::hdr_space()` | ~150-170 | v25.0+ |
| `GetPageSize()` | 332-353 | `dyn_img_hdr::page_size()` | ~130-140 | v25.0+ |

---

## CpioArchive.cs ↔ cpio.rs

| BACKRabbit Function | Lines | Magisk Function | Magisk Lines (v30.7) | Versions |
|---------------------|-------|-----------------|----------------------|----------|
| `Parse()` | 21-80 | `load_cpio()` | ~1-80 | v25.0+ |
| `Serialize()` | 161-240 | `dump_cpio()` | ~80-160 | v25.0+ |
| `AddFile()` | 241-270 | `add_file()` | ~160-190 | v26.0+ |
| `RemoveFile()` | 271-300 | `rm_file()` | ~190-220 | v26.0+ |
| `MoveFile()` | 301-320 | `mv_file()` | ~220-240 | v26.0+ |
| `PatchCmdline()` | 321-340 | `patch_cmdline()` | ~240-260 | v26.0+ |

---

## CompressionEngine.cs ↔ compress.cpp + compress.rs

| BACKRabbit Function | Lines | Magisk Function | Magisk Lines | Versions |
|---------------------|-------|-----------------|-------------|----------|
| `DetectFormat()` | 37-60 | `check_fmt()` compression section | format.cpp ~40-70 | v25.0-v29.0 |
| `Decompress()` | 61-200 | `decompress_bytes()` | compress.cpp ~50-200 | v25.0-v29.0 |
| `Compress()` | 201-340 | `compress_bytes()` | compress.cpp ~200-350 | v25.0-v29.0 |
| Gzip impl | 61-100 | gzip decompress | compress.cpp ~60-100 | v25.0+ |
| Bzip2 impl | 101-130 | bzip2 decompress | compress.cpp ~100-130 | v25.0+ |
| LZ4 impl | 131-170 | lz4 decompress | compress.cpp ~130-170 | v25.0+ |
| XZ impl | 171-200 | xz decompress | compress.cpp ~170-200 | v25.0+ |
| LZMA impl | 201-240 | lzma decompress | compress.cpp ~200-240 | v25.0+ |

**Note:** v30.0+ moved compression to Rust FFI (`compress_bytes`/`decompress_bytes` in `lib.rs`). BACKRabbit's CompressionEngine is a pure C# reimplementation.

---

## FormatDetector.cs ↔ format.cpp + magiskboot.hpp

| BACKRabbit Function | Lines | Magisk Function | Magisk Lines | Versions |
|---------------------|-------|-----------------|-------------|----------|
| `CheckFmt()` | 61-120 | `check_fmt()` | format.cpp ~30-80 / bootimg.cpp ~40-90 (v30.3+) | v25.0+ |
| `IsCompressed()` | 121-150 | `fmt_compressed()` | format.cpp ~90-110 | v26.0+ |
| `Fmt2Name()` | 151-180 | `fmt2name()` | format.cpp ~110-130 | v26.0+ |
| `CheckFmtLg()` | 181-210 | `check_fmt_lg()` | bootimg.cpp ~130-160 | v26.0+ |
| `DetectCompression()` | 211-252 | (BACKRabbit extension) | N/A | N/A |
| Magic constants | 39-60 | `magiskboot.hpp` defines | magiskboot.hpp ~10-50 | v26.0+ |

---

## AvbRestorer.cs ↔ sign.rs + bootimg.cpp AVB section

| BACKRabbit Function | Lines | Magisk Function | Magisk Lines | Versions |
|---------------------|-------|-----------------|-------------|----------|
| `RestoreVerificationFlags()` | 33-60 | `repack()` AVB section | bootimg.cpp ~960-990 | v26.0+ |
| `FindAvbFooter()` | 61-120 | AVB footer detection | bootimg.cpp ~420-450 | v26.0+ |
| `PatchVbmetaFlags()` | 121-160 | `patch_vbmeta_flag()` | patch.rs ~50-80 | v26.0+ |
| `RestoreOriginalImageSize()` | 161-195 | footer patching | bootimg.cpp ~970-980 | v26.0+ |

---

## MagiskArtifactDetector.cs ↔ patch.rs + rootdir.rs

| BACKRabbit Function | Lines | Magisk Function | Magisk Lines | Versions |
|---------------------|-------|-----------------|-------------|----------|
| `Detect()` | 19-60 | `patch_boot_img()` detection phase | patch.rs ~1-50 | v26.0+ |
| Artifact path scanning | 26-33 | `MagiskArtifacts::Paths` | rootdir.rs ~10-30 | v26.0+ |
| Fstab analysis | 36-44 | fstab patching detection | patch.rs ~100-130 | v26.0+ |
| `ParseBackupConfig()` | 48-53 | `.backup/.magisk` parsing | rootdir.rs ~50-70 | v26.0+ |
| `RestoreFromBackup()` | 80-150 | backup restoration | patch.rs ~150-200 | v26.0+ |
| `SurgicalRemoval()` | 151-250 | manual artifact removal | patch.rs ~200-280 | v26.0+ |
| `RestoreFstab()` | 251-300 | fstab verity/encryption restore | patch.rs ~280-320 | v26.0+ |
| `RestoreInit()` | 301-350 | init binary restoration | patch.rs ~320-360 | v26.0+ |
| `CleanupOverlay()` | 351-396 | overlay.d/ removal | patch.rs ~360-396 | v26.0+ |

---

## MagiskUninstaller.cs ↔ Magisk uninstall workflow

| BACKRabbit Function | Lines | Magisk Equivalent | Versions |
|---------------------|-------|-------------------|----------|
| `UninstallAsync()` | 50-80 | `magisk --uninstall` workflow | v26.0+ |
| Step 1: Load image | 57-60 | `boot_img::parse()` | v25.0+ |
| Step 2: Extract ramdisk | 63-65 | `unpack()` + `load_cpio()` | v25.0+ |
| Step 3: Detect Magisk | 65-74 | `patch_boot_img()` detection | v26.0+ |
| Step 4: Choose method | 76-120 | Method selection logic | v26.0+ |
| Method 1: Stock flash | 80-90 | Stock firmware flash | v26.0+ |
| Method 2: Backup restore | 91-110 | `ramdisk.cpio.orig` restore | v26.0+ |
| Method 3: Surgical | 111-150 | Manual artifact removal | v26.0+ |
| Step 5: Repack | 151-180 | `repack()` | v25.0+ |
| Step 6: AVB restore | 181-200 | `patch_vbmeta_flag()` | v26.0+ |
| Step 7: Flash | 201-230 | ADB/fastboot/Download Mode | v26.0+ |
| Step 8: Verify | 231-263 | Post-flash verification | v26.0+ |

---

## SamsungKernelPatcher.cs ↔ Samsung-specific analysis

| BACKRabbit Function | Lines | Magisk Equivalent | Versions |
|---------------------|-------|-------------------|----------|
| `Analyze()` | 46-80 | Kernel security analysis | v26.0+ |
| `ContainsPattern()` | 81-100 | Pattern scanning | v26.0+ |
| `AnalyzeSyscallTable()` | 101-180 | Syscall table analysis | v26.0+ |
| `FindHookPatterns()` | 181-220 | Hook branch detection | v26.0+ |
| `RevertPatches()` | 221-300 | Patch reversal | v26.0+ |
| `RestoreSyscallTable()` | 301-350 | Syscall table restore | v26.0+ |
| `VerifyRestoration()` | 351-388 | Post-restore verification | v26.0+ |

---

## AdbClient.cs ↔ ADB Protocol Specification

| BACKRabbit Function | Lines | ADB Protocol | Versions |
|---------------------|-------|-------------|----------|
| `ConnectAsync()` | 41-100 | A_CNXN handshake | All |
| `AuthenticateAsync()` | 101-180 | A_AUTH RSA signing | All |
| `ShellAsync()` | 181-250 | A_OPEN shell | All |
| `SyncPullAsync()` | 251-350 | A_SYNC pull | All |
| `SyncPushAsync()` | 351-450 | A_SYNC push | All |
| `ReadAsync()` | 451-550 | A_WRTE/A_OKAY | All |
| `WriteAsync()` | 551-650 | A_WRTE | All |
| `CloseStream()` | 651-700 | A_CLSE | All |
| `DisconnectAsync()` | 701-783 | Connection close | All |

---

## FastbootClient.cs ↔ Fastboot Protocol

| BACKRabbit Function | Lines | Fastboot Protocol | Versions |
|---------------------|-------|-------------------|----------|
| `ConnectAsync()` | 31-80 | Fastboot handshake | All |
| `FlashAsync()` | 81-150 | `flash:` command | All |
| `EraseAsync()` | 151-180 | `erase:` command | All |
| `RebootAsync()` | 181-210 | `reboot` / `reboot-bootloader` | All |
| `GetVarAsync()` | 211-250 | `getvar:` command | All |
| `SparseFlashAsync()` | 251-327 | Sparse image flashing | All |

---

## DownloadModeFlasher.cs ↔ Heimdall Protocol

| BACKRabbit Function | Lines | Heimdall Protocol | Versions |
|---------------------|-------|-------------------|----------|
| `InitializeAsync()` | 35-80 | INIT handshake | All |
| `IdentifyDeviceAsync()` | 81-120 | IDENTIFY request | All |
| `ReadPitAsync()` | 121-180 | PIT dump | All |
| `FlashPartitionAsync()` | 181-280 | FILE_TRANSFER | All |
| `EndSessionAsync()` | 281-320 | END_SESSION | All |
| `RebootDeviceAsync()` | 321-360 | REBOOT | All |
| `VerifyFlashAsync()` | 361-410 | Post-flash verify | All |

---

## Version Compatibility Matrix

| BACKRabbit Component | v25.0 | v26.0 | v27.0 | v28.0 | v29.0 | v30.0 | v30.3 | v30.7 |
|---------------------|-------|-------|-------|-------|-------|-------|-------|-------|
| BootImageParser | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| BootImageRepacker | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| CpioArchive | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| CompressionEngine | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️¹ | ⚠️¹ | ⚠️¹ |
| FormatDetector | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️² | ⚠️² |
| AvbRestorer | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| MagiskArtifactDetector | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| MagiskUninstaller | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| SamsungKernelPatcher | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

**Notes:**
1. ⚠️ v30.0+ moved compression to Rust FFI — BACKRabbit uses pure C# reimplementation
2. ⚠️ v30.3+ merged format.cpp into bootimg.cpp — BACKRabbit keeps separate FormatDetector

---

## AOSP Header References

| BACKRabbit Structure | AOSP Header | Field |
|----------------------|-------------|-------|
| `BootImgHdrV0` | `system/core/include/android/bootimg.h` | `boot_img_hdr_v0` |
| `BootImgHdrV1` | `system/core/include/android/bootimg.h` | `boot_img_hdr_v1` |
| `BootImgHdrV2` | `system/core/include/android/bootimg.h` | `boot_img_hdr_v2` |
| `BootImgHdrV3` | `system/core/include/android/bootimg.h` | `boot_img_hdr_v3` |
| `BootImgHdrV4` | `system/core/include/android/bootimg.h` | `boot_img_hdr_v4` |
| `VendorBootImgHdrV3` | `system/core/include/android/bootimg.h` | `vendor_boot_img_hdr_v3` |
| `VendorBootImgHdrV4` | `system/core/include/android/bootimg.h` | `vendor_boot_img_hdr_v4` |
| `AvbFooter` | `external/avb/libavb/avb_footer.h` | `AvbFooter` |
| `AvbVBMetaImageHeader` | `external/avb/libavb/avb_vbmeta_image.h` | `AvbVBMetaImageHeader` |

---

## Firehose Protocol ↔ Source Code

| Firehose KB Document | Source File | Lines | Purpose |
|----------------------|-------------|-------|---------|
| `firehose/OPERATIONS.md` | `FirehoseClient.cs` | 406 | Firehose XML command execution |
| `firehose/OPERATIONS.md` | `SaharaStateMachine.cs` | 65 | EDL handshake state machine |
| `firehose/OPERATIONS.md` | `SaharaLoaderUploader.cs` | — | Programmer ELF upload in 4KB chunks |
| `firehose/OPERATIONS.md` | `WinUsbTransport.cs` | 115 | USB transport (EP 0x02 OUT, EP 0x81 IN) |
| `firehose/OPERATIONS.md` | `UsbDeviceManager.cs` | 468 | LibUsbDotNet wrapper, Samsung VID/PID detection |
| `firehose/FAILURES.md` | `PartitionDiagnostics.cs` | ~200 | GPT validation, SHA256 comparison, 6 validation rules |
| `firehose/FAILURES.md` | `PartitionRestorer.cs` | ~150 | Flash + verify with retry, blocklist enforcement |
| `firehose/FAILURES.md` | `MagiskRemover.cs` | ~200 | Magisk detection + removal + verification |
| `firehose/DESIGN.md` | `RescueOrchestrator.cs` | 152 | 7-phase rescue pipeline, flash-then-patch ordering |
| `firehose/DESIGN.md` | `FirmwareSourcer.cs` | 320 | Dual-method FUS auth, .enc4 decryption |
| `firehose/DESIGN.md` | `CompressionEngine.cs` | 206 | Pure C# compression (no CLI fallbacks) |
| `firehose/BUILD.md` | `BACKRabbit.CLI.csproj` | — | NuGet dependency graph, publish configuration |

---

## Knowledge Base Cross-Reference Map

| From | To | Topic |
|------|----|-------|
| `OFFLINE_AGENT_GUIDE.md` | `firehose/DESIGN.md` | MagiskCore↔Firehose handoff (Design Decision 11) |
| `OFFLINE_AGENT_GUIDE.md` | `firehose/FAILURES.md` | Magisk artifact detection failure modes |
| `firehose/OPERATIONS.md` | `OFFLINE_AGENT_GUIDE.md` | Magisk artifact signatures table |
| `firehose/OPERATIONS.md` | `firehose/FAILURES.md` | Sahara/Firehose error codes and recovery |
| `firehose/OPERATIONS.md` | `firehose/DESIGN.md` | Sequential USB transfer rationale (Design Decision 1) |
| `firehose/FAILURES.md` | `OFFLINE_AGENT_GUIDE.md` | 5 common debugging scenarios |
| `firehose/FAILURES.md` | `firehose/DESIGN.md` | Forensic evidence preservation (Design Decision 5) |
| `firehose/DESIGN.md` | `OFFLINE_AGENT_GUIDE.md` | MagiskCore architecture and file ranking |
| `firehose/DESIGN.md` | `firehose/BUILD.md` | Build stick points and NuGet mirror |
| `HEALTH_CHECK.md` | `firehose/BUILD.md` | Offline build viability, test suite status |

---

## Last Updated
2026-06-21 — Merged Firehose knowledge base, added cross-reference map

---

## v2.0 New Files (June 18, 2026)

| BACKRabbit File | Lines | Purpose |
|-----------------|-------|---------|
| `BACKRabbit.CLI/Wizard/TrapEscapeRunner.cs` | ~200 | Knox-safe trap-escape path analysis |
| `BACKRabbit.CLI/Wizard/ForensicDiagnostics.cs` | ~150 | Forensic sweep (paths, privapps, SELinux) |

## v2.0 Updated Line Counts

| File | v1.0 | v2.0 | Change |
|------|------|------|--------|
| `AdbClient.cs` | 783 | ~980 | +197 (TLS upgrade, ADB server proxy, ls -d fix) |
| `AdbKeyManager.cs` | ~60 | ~140 | +80 (ANDROID_PUBKEY_MODULUS, ParseSshRsaPublicKey) |
| `Program.cs` | 345 | ~615 | +270 (wizard dispatch, trap-escape, raw logging) |
| `WizardRunner.cs` | — | 961 | NEW (7-step wizard, all jury wishlist items) |
| `TrapEscapeRunner.cs` | — | ~200 | NEW (forensic diagnostics) |
| `ForensicDiagnostics.cs` | — | ~150 | NEW (trap-escape path analysis) |
