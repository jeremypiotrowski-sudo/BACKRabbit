# BACKRabbit — Offline Agent Guide

> **If you are an AI agent seeing this codebase for the first time, read this first.**

## What BACKRabbit Is

BACKRabbit is a C# .NET 8 tool that parses, analyzes, modifies, and repacks Android boot images — specifically to remove Magisk root from Samsung devices. It ports ~15,000 lines of Magisk's native C++/Rust boot image manipulation code to pure C#.

**Primary use case:** Uninstall Magisk from a Samsung phone and restore stock boot image state.

## Architecture in 500 Words

```
Samsung firmware (.tar.md5)
        │
        ▼
SamsungFirmwareExtractor ──→ boot.img / init_boot.img
        │
        ▼
BootImageParser ──→ BootImage object (header, kernel, ramdisk, AVB info)
        │
        ▼
CompressionEngine ──→ decompress ramdisk (gzip/lz4/xz/lzma)
        │
        ▼
CpioArchive ──→ parse ramdisk file tree (CPIO newc format)
        │
        ▼
MagiskArtifactDetector ──→ find Magisk files (overlay.d/, .backup/, etc.)
        │
        ├── Method 1: Stock firmware flash (skip to repack)
        ├── Method 2: Restore from ramdisk.cpio.orig backup
        └── Method 3: Surgical removal of Magisk files
        │
        ▼
CpioArchive.Serialize() ──→ clean CPIO
        │
        ▼
CompressionEngine.Compress() ──→ compressed ramdisk
        │
        ▼
BootImageRepacker ──→ rebuild boot.img with clean ramdisk
        │
        ▼
AvbRestorer ──→ restore AVB flags (3→0)
        │
        ▼
Flash via ADB / Fastboot / Download Mode
```

## Key Files Ranked by Importance

### Tier 1: Core Logic (must understand these first)

| # | File | Lines | What It Does |
|---|------|-------|-------------|
| 1 | `BACKRabbit.MagiskCore/Parser/BootImageParser.cs` | 614 | Parse ALL boot image formats (v0-v4, vendor, Samsung, MTK, ChromeOS) |
| 2 | `BACKRabbit.MagiskCore/Repacker/BootImageRepacker.cs` | 353 | Rebuild boot images with modified ramdisk/kernel |
| 3 | `BACKRabbit.MagiskCore/RamdiskEditor/CpioArchive.cs` | 340 | Parse/serialize CPIO newc archives (ramdisk file system) |
| 4 | `BACKRabbit.MagiskCore/Compression/CompressionEngine.cs` | 386 | Pure C# gzip/bzip2/lz4/xz/lzma (no CLI fallbacks) |
| 5 | `BACKRabbit.MagiskCore/FormatDetection/FormatDetector.cs` | 252 | Detect 20+ file formats by magic bytes |

### Tier 2: Magisk-Specific Logic

| # | File | Lines | What It Does |
|---|------|-------|-------------|
| 6 | `BACKRabbit.MagiskCore/RamdiskEditor/MagiskArtifactDetector.cs` | 396 | Find Magisk files in ramdisk, 3 restoration methods |
| 7 | `BACKRabbit.MagiskCore/Services/MagiskUninstaller.cs` | 263 | Orchestrate full uninstall workflow |
| 8 | `BACKRabbit.MagiskCore/AvbRestorer/AvbRestorer.cs` | 195 | Restore AVB verification flags |
| 9 | `BACKRabbit.MagiskCore/SamsungKernel/SamsungKernelPatcher.cs` | 388 | Detect/reverse Samsung kernel patches |

### Tier 3: Device Communication

| # | File | Lines | What It Does |
|---|------|-------|-------------|
| 10 | `BACKRabbit.Protocol.Adb/AdbClient.cs` | 783 | Full ADB protocol (USB/TCP, shell, sync, auth) |
| 11 | `BACKRabbit.Protocol.Fastboot/FastbootClient.cs` | 327 | Fastboot protocol (flash, erase, boot) |
| 12 | `BACKRabbit.Protocol.DownloadMode/DownloadModeFlasher.cs` | 410 | Samsung Download Mode (Odin/Heimdall) |

### Tier 4: Supporting

| # | File | Lines | What It Does |
|---|------|-------|-------------|
| 13 | `BACKRabbit.Firmware/SamsungFirmwareExtractor.cs` | ? | Extract boot.img from Samsung .tar.md5 |
| 14 | `BACKRabbit.Protocol.Fastboot/SparseImage.cs` | ? | Android sparse image format |
| 15 | `BACKRabbit.Protocol.DownloadMode/PitFile.cs` | ? | Samsung PIT partition table |

## Common Debugging Scenarios

### "No valid boot image header found"
**Where to look:** `BootImageParser.cs` lines 23-68
**Causes:**
- File is not a boot image (check with `file` command or hex dump)
- Image has non-standard header (DHTB, BLOB, CHROMEOS) that wasn't detected
- Image is encrypted or corrupted
**Fix:** Check `FormatDetector.CheckFmt()` output for the first bytes. Verify magic bytes.

### "Invalid CPIO magic at offset X"
**Where to look:** `CpioArchive.cs` lines 26-35
**Causes:**
- Ramdisk wasn't properly decompressed before CPIO parsing
- Ramdisk uses odc/bin format instead of newc (rare)
- Data corruption
**Fix:** Verify decompression succeeded. Check first 6 bytes of ramdisk data — should be "070701" or "070702".

### "No AVB footer found"
**Where to look:** `AvbRestorer.cs` lines 37-44
**Causes:**
- Image doesn't have AVB signing (older devices)
- Footer was stripped
- Image is truncated
**Fix:** Check last 64 bytes of image for "AVBf" magic. If absent, AVB restore is not needed.

### "Compression mismatch / boot loop after flash"
**Where to look:** `CompressionEngine.cs` + `BootImageRepacker.cs`
**Causes:**
- v4 GKI images require LZ4 legacy compression — using gzip will cause boot loop
- Ramdisk size changed significantly
**Fix:** Check `BootImage.HeaderVersion`. If v4, force LZ4 legacy compression. Verify ramdisk size matches original ±10%.

### "Magisk still detected after uninstall"
**Where to look:** `MagiskArtifactDetector.cs`
**Causes:**
- Surgical removal missed some files
- Backup restoration used wrong backup
- init binary still Magisk-patched
**Fix:** Use Method 1 (stock firmware flash) for guaranteed removal. Check for residual files: `/data/adb/`, `/data/adb/magisk.db`.

## How to Verify Correctness

### 1. Parse → Repack Round-Trip
```
1. Parse stock boot.img → BootImage object
2. Repack without modifications → new_boot.img
3. Parse new_boot.img → BootImage object
4. Compare: headers, kernel size, ramdisk size, checksums
5. Expected: identical (except timestamp fields)
```

### 2. CPIO Round-Trip
```
1. Parse ramdisk.cpio → CpioArchive
2. Serialize → new_ramdisk.cpio
3. Parse new_ramdisk.cpio → CpioArchive
4. Compare: entry count, names, sizes, data
5. Expected: identical
```

### 3. Compression Round-Trip
```
1. Compress test data → compressed
2. Decompress compressed → decompressed
3. Compare: original == decompressed
4. Expected: identical
```

### 4. AVB Flag Verification
```
1. Parse Magisk-patched boot.img
2. Check AvbVBMetaImageHeader.flags → should be 3
3. Run AvbRestorer.RestoreVerificationFlags()
4. Check flags → should be 0
```

## Glossary

| Term | Definition |
|------|------------|
| **AOSP** | Android Open Source Project — standard boot image format |
| **AVB** | Android Verified Boot — cryptographic verification chain |
| **AVB Footer** | 64-byte footer at end of signed images, contains vbmeta offset |
| **VBMeta** | AVB metadata header containing flags, hash tree, signature |
| **Boot Image** | Partition containing kernel + ramdisk (boot.img or init_boot.img) |
| **init_boot** | GKI 2.0 split: init_boot has ramdisk, boot has kernel (Android 13+) |
| **CPIO** | Archive format for ramdisk (newc = "new portable format" with CRC) |
| **DHTB** | Samsung Download Mode header |
| **BLOB** | Tegra/NVIDIA secure boot signature blob |
| **DTB** | Device Tree Blob — hardware description for kernel |
| **GKI** | Generic Kernel Image — Google's standardized kernel |
| **Knox eFuse** | Samsung hardware fuse that trips when unofficial software is flashed |
| **LZ4 Legacy** | Required compression for v4 GKI ramdisks |
| **Magisk** | Systemless root solution that patches boot images |
| **newc** | CPIO format with 070701/070702 magic, used in Android ramdisks |
| **Odin** | Samsung's proprietary flashing tool (Windows only) |
| **Heimdall** | Open-source Odin alternative (cross-platform) |
| **PIT** | Partition Information Table — Samsung's partition layout |
| **Ramdisk** | Initial root filesystem loaded by kernel (contains init, fstab, etc.) |
| **SEANDROID** | Samsung's SELinux enforcement marker |
| **Sparse Image** | Android's compressed image format for fastboot flashing |
| **v0-v4** | AOSP boot image header versions (v4 = latest, adds signature) |
| **Vendor Boot** | Separate partition for vendor-specific ramdisks (v3/v4) |
| **zImage** | Compressed ARM kernel image format |

## Magisk Artifact Signatures

When examining a ramdisk, these files indicate Magisk installation:

| Path | Purpose | Restoration |
|------|---------|-------------|
| `overlay.d/sbin/magisk.xz` | Magisk binary | Delete |
| `overlay.d/sbin/stub.xz` | Magisk stub APK | Delete |
| `overlay.d/sbin/magiskinit` | Magisk init replacement | Restore stock init |
| `.backup/.magisk` | Magisk config backup | Delete |
| `ramdisk.cpio.orig` | Stock ramdisk backup | **Use this to restore!** |
| `.backup/init.xz` | Stock init backup | Decompress and restore as /init |
| `init` (patched) | Magisk-patched init | Replace with stock |
| `fstab.*` (patched) | Verity/encryption disabled | Restore original flags |

## Version Compatibility Quick Reference

| Magisk Version | Boot Image Format | Key Changes |
|----------------|-------------------|-------------|
| v25.0-v25.2 | v0-v3 | Initial native code |
| v26.0-v26.4 | v0-v4, vendor v3-v4 | +AVB, +DTB, +Rust patching |
| v27.0 | v0-v4, vendor v3-v4 | +NOOKHD, +ACCLAIM, +AMONET |
| v28.0-v29.0 | v0-v4, vendor v3-v4 | Consolidation |
| v30.0-v30.7 | v0-v4, vendor v3-v4 | Rust compression, format merged |

## Knowledge-Base File Naming Convention

Files in `knowledge-base/` follow this pattern:
```
{version}_{sourcefile}
```

Examples:
- `v30.7_bootimg.cpp` — Magisk v30.7's bootimg.cpp
- `v26.0_cpio.rs` — Magisk v26.0's cpio.rs
- `canary-29001_patch.rs` — Canary build 29001's patch.rs

Companion docs: `companion-docs/{BackRabbitFile}.md` — explains BACKRabbit's C# implementation
Cross-reference maps: `cross-reference-map/{BackRabbitFile}.md` — maps C# lines to Magisk source lines

## Emergency Procedures

### If flash fails and device won't boot:
1. **Don't panic** — Samsung devices have Download Mode (Vol Down + Bixby + USB cable)
2. Flash stock firmware via Odin/Heimdall in Download Mode
3. Stock firmware files are in `staging/` directory

### If you need a zero-dependency fix RIGHT NOW:
Use `emergency-flasher/emergency_fix.py` — pure Python 3, no pip installs needed.
```bash
python emergency_fix.py --firmware staging/F966U1/AP_*.tar.md5 --output clean_boot.img
```

---

## v2.0 Architecture: CLI-Only, Cross-Platform

BACKRabbit v2.0 is **CLI-only** — the Windows Forms GUI (`BACKRabbit.GUI/`) has been deleted. The sole user-facing entry point is `BACKRabbit.CLI/`.

### Entry Point
```
BACKRabbit.CLI/Program.cs          (~600 lines) — All commands + wizard dispatch
BACKRabbit.CLI/Wizard/WizardRunner.cs (~500 lines) — 7-step interactive wizard
BACKRabbit.CLI/Testing/MockAdbClient.cs (218 lines) — Test mode (no device needed)
```

### Key Commands
```bash
backrabbit magisk wizard              # 7-step interactive uninstall wizard
backrabbit magisk wizard --test-mode  # Test with simulated device
backrabbit magisk wizard --offline <path>  # Analyze local boot image file
backrabbit magisk wizard --serial <ip:port> # Connect via TCP (Tailscale/Wireless Debugging)
backrabbit magisk detect              # Quick Magisk detection
backrabbit magisk uninstall           # Direct uninstall (non-interactive)
backrabbit adb shell/pull/push        # ADB operations
backrabbit fastboot flash/reboot      # Fastboot operations
backrabbit firmware extract           # Extract from Samsung .tar.md5
backrabbit detect                     # Device detection
backrabbit flash                      # Flash via Download Mode
```

### NuGet Dependencies
- **Spectre.Console v0.49.0** — Rich terminal output (tables, progress bars, colored markup, spinners)
- **System.CommandLine v2.0-beta4** — CLI parsing
- **Serilog** — Structured logging

### Build & Publish (Cross-Platform)
```bash
dotnet restore BACKRabbit.slnx
dotnet build BACKRabbit.slnx -c Release
dotnet test BACKRabbit.slnx -c Release
dotnet publish BACKRabbit.CLI/BACKRabbit.CLI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/win
dotnet publish BACKRabbit.CLI/BACKRabbit.CLI.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/linux
```

---

## R1-R5: Jury Research Results

### R1: Bootloader Detection
`IAdbClient.CheckBootloaderLockStatusAsync()` reads `ro.boot.other.locked`, `ro.boot.flashing.locked`, and `ro.boot.verifiedbootstate` to determine if the bootloader is locked. This gates the flash step.

### R2: Branched Workflow
- **Bootloader UNLOCKED** → Direct flash via Download Mode (`backrabbit flash --image <path>`)
- **Bootloader LOCKED** → Step-by-step Odin guide (10 steps, plain language, with recovery tips)

### R3: SELinux Detection
`MagiskArtifactDetector` scans `InitRcPatterns` in the ramdisk. `IsSelinuxPermissive` field indicates if SELinux is in permissive mode (common Magisk side effect).

### R4: Symptom→Fix Messaging
Plain-language messages connect symptoms to fixes:
- "Dim screen? SELinux is in permissive mode. Removing modifications restores brightness."
- "Root apps will stop working after uninstall. This is expected."

### R5: Knox Radical Honesty
5 prohibitions (never claim Knox can be reset, never hide tripped state, never minimize impact, never suggest "fixes" that don't exist, never omit the "what you CAN still do" path) and 6 requirements (persistent warning, plain language, CAN/CANNOT lists, emergency recovery path, report documentation, no false hope).

---

## Dual-Mode Architecture

| Mode | Flag | ADB Client | Use Case |
|------|------|-----------|----------|
| **Live** | (default) | `AdbClient` → USB or TCP | Real device connected |
| **Test** | `--test-mode` | `MockAdbClient` | No device needed, simulates SM-S928U1 |
| **Offline** | `--offline <path>` | `MockAdbClient` (placeholder) | Analyze local boot.img file |

### Remote ADB via Tailscale/Wireless Debugging
Android 11+ supports **Wireless Debugging** without USB:
1. Settings → Developer Options → Wireless debugging → ON
2. Tap "Pair device with pairing code" → get IP:port + 6-digit code
3. On PC: `adb pair <tailscale-ip>:<pairing-port> <code>`
4. On PC: `adb connect <tailscale-ip>:<connect-port>`
5. Use with BACKRabbit: `backrabbit magisk wizard --serial <tailscale-ip>:<connect-port>`

---

## Anti-Scooping Protocol — 5 Enforcement Mechanisms

1. **STUBS.md Gate** — Any `.cs` file <100 lines (excluding Class1.cs, AssemblyInfo, GlobalUsings) is flagged as a stub. Must be resolved or documented.
2. **Empty File Scan** — Zero-byte source files are flagged. Build artifacts excluded.
3. **Catch-Block Audit** — Every `catch` block must have a recovery path. No empty catches, no `// TODO` catches.
4. **Demonstrability Requirement** — Every feature must be demonstrable via `--test-mode` or `--offline`.
5. **Jury Certification** — 10-gate system (G0-G10) with 5-juror voting. 3/5 votes required to pass.

### What NOT to Delete (Protected Files)
- `BACKRabbit.MagiskCore/` — Core engine (~3,500 lines)
- `BACKRabbit.Protocol.*/` — ADB, Fastboot, Download Mode protocols
- `BACKRabbit.Firmware/` — Samsung firmware extractor
- `BACKRabbit.Usb/` — USB device detection
- `BACKRabbit.Core/` — Shared utilities
- `BACKRabbit.Tests/` — Test suite (45 tests)
- `emergency-flasher/` — Emergency recovery script
- `knowledge-base/` — Agent documentation
- `staging/` — Pre-extracted boot images for testing
- `IAdbClient.cs` — Interface enabling MockAdbClient

---

## Updated Line Counts (v2.0)

| File | v1.0 Lines | v2.0 Lines | Change |
|------|-----------|-----------|--------|
| `BootImageParser.cs` | 614 | 686 | +72 (SELinux detection) |
| `BootImageRepacker.cs` | 353 | 547 | +194 (AVB restore integration) |
| `CompressionEngine.cs` | 386 | 392 | +6 |
| `MagiskArtifactDetector.cs` | 396 | 444 | +48 (module names, SELinux) |
| `MagiskUninstaller.cs` | 263 | 263 | unchanged |
| `AdbClient.cs` | 783 | 783 | unchanged |
| `IAdbClient.cs` | — | 45 | NEW (interface extraction) |
| `MockAdbClient.cs` | — | 218 | NEW (test mode) |
| `WizardRunner.cs` | — | ~500 | NEW (7-step CLI wizard) |
| `Program.cs` (CLI) | 345 | ~600 | +255 (wizard command) |
| `F966U1IntegrationTests.cs` | — | 250 | NEW (real firmware test) |
| `emergency_fix.py` | 0 | 300+ | FILLED (was empty stub) |
| `BACKRabbit.GUI/` (entire) | ~1,500 | 0 | DELETED (WinForms) |

---

## Last Updated
2026-06-18 — v2.0 Ship (CLI-only, cross-platform, 9 jury wishlist items implemented)

---

## v2.0 Critical Discoveries (June 18, 2026)

### Discovery 1: ADB Server Proxy Required for Android 14+ Wireless Debugging

**Problem:** Android 14+ Wireless Debugging mandates TLS encryption. The device sends `A_STLS` after the first CNXN. .NET's `SslStream` (SChannel on Windows) cannot negotiate with Android's BoringSSL — TLS handshake fails with "unexpected EOF."

**Solution:** Use the local ADB server (`127.0.0.1:5037`) as a transparent proxy. The server handles all TLS, authentication, and transport complexity. BACKRabbit sends `host:transport:<serial>` to get a bridged connection, then `shell:<command>` on the same stream.

**Implementation:** `AdbClient.cs` — `ConnectViaAdbServerAsync()` + `ExecuteShellViaServerAsync()` with two-step protocol (host:transport → OKAY → shell:command → OKAY → output).

**Key Insight:** Pure C# TLS is not viable for Android 14+ Wireless Debugging. The ADB server proxy approach is the correct solution — it's how Google's own tools work.

### Discovery 2: `test -d` False Negative on SELinux Enforcing Devices

**Problem:** `test -d /data/adb` returns "NOT_FOUND" even when the directory EXISTS because `test(1)` cannot traverse `/data/` (mode 0771, owner root:system) on SELinux Enforcing devices.

**Solution:** Use `ls -d /data/adb 2>&1` instead:
- `Permission denied` → directory EXISTS but SELinux blocks access
- `No such file or directory` → directory genuinely doesn't exist

**Implementation:** `AdbClient.cs` — `CheckMagiskStatusAsync()` Layer 2 changed from `test -d` to `ls -d`.

**Impact:** BACKRabbit now correctly detects residual Magisk traces on SELinux Enforcing devices where the Magisk binary was wiped by factory reset but `/data/adb/` directory structure survives.

### Discovery 3: Knox Two-Indicator Model

**Android property** `ro.boot.warranty_bit` reflects CURRENT boot session (0 when running stock-signed firmware, 1 when running modified firmware). This CAN revert to 0 by flashing stock.

**Download Mode "WARRANTY VOID"** reflects the permanent eFuse state. Once blown (0x1), it NEVER reverts. Samsung replaces the entire motherboard to "fix" it.

**BACKRabbit's position:** The wizard distinguishes "Current Boot State" from "Permanent Knox eFuse State." It warns that permanent eFuse state requires Download Mode check. It NEVER claims Knox can be restored.

### Discovery 4: Live Phone Test Results (SM-F966U1, Android 16)

| Check | Result |
|-------|--------|
| Magisk binary | NOT FOUND (wiped by factory reset) |
| `/data/adb/` | EXISTS but LOCKED (SELinux Enforcing) |
| Knox (Android property) | 0x0 (intact — running stock firmware) |
| Bootloader | LOCKED |
| Verified Boot | green (stock-signed) |
| SELinux | Enforcing |

**Diagnosis:** Magisk was installed, then removed by flashing stock boot WITHOUT running the Magisk uninstaller first. `/data/adb/` residue survives. Factory resets fail because recovery cannot handle non-standard root-owned directories in `/data/`.

**Cleaning options:**
- Option A: Full Odin flash with CSC (Knox safe, wipes all data)
- Option B: Unlock → root → rm -rf → stock → relock (trips Knox 0x1 permanently)
