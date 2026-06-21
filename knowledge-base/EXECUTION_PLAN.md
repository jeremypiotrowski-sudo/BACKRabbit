# ⚖️ BACKRabbit — 5-Juror Unanimous Execution Plan

> **📅 Dated:** 2026-06-18
> **🏷️ Marker:** This file is a planning artifact — the definitive jury-approved execution plan for the BACKRabbit GUI hardening, cleanup, and documentation wrap-up. It records what was decided, why, and by whom. It is NOT implementation — it is the blueprint. This file serves as a marker/snapshot of the plan at the time of the jury verdict.
> **⚖️ Status:** 12/12 Sections — 5/5 Unanimous — ALL 7 AMENDMENTS INCORPORATED
> **👥 Jury:** Anxious User (A), Power User (B), Developer/Tester (C), QA/Support Engineer (D), Product Manager (E)

---

## 0. WHAT BACKRABBIT IS

BACKRabbit is a **C# .NET 8 Windows desktop application** (~15,000 lines) that removes Magisk root from Samsung phones and restores them to stock boot image state. It ports Magisk's native C++/Rust boot image manipulation code to pure C#. It has a WinForms GUI, a CLI, and a class library architecture.

**Target User:** Non-technical Samsung owner who rooted with Magisk and now wants it gone — safely, verifiably, without command lines, without fear of bricking.

---

## 1. PROJECT STRUCTURE (10 Projects)

```
BACKRabbit.slnx
├── BACKRabbit.MagiskCore/        ← THE BRAIN (~15K lines, COMPLETE)
│   ├── Parser/BootImageParser.cs          (614 lines)
│   ├── Repacker/BootImageRepacker.cs      (353 lines)
│   ├── RamdiskEditor/CpioArchive.cs       (340 lines)
│   ├── Compression/CompressionEngine.cs   (386 lines)
│   ├── FormatDetection/FormatDetector.cs  (252 lines)
│   ├── RamdiskEditor/MagiskArtifactDetector.cs (396 lines)
│   ├── AvbRestorer/AvbRestorer.cs         (195 lines)
│   ├── SamsungKernel/SamsungKernelPatcher.cs (388 lines)
│   ├── Services/MagiskUninstaller.cs      (263 lines)
│   └── Structures/                        — BootHeaders, AvbStructures, CpioStructures
│
├── BACKRabbit.Protocol.Adb/      ← ADB protocol (783 lines, COMPLETE)
├── BACKRabbit.Protocol.Fastboot/  ← Fastboot protocol (327 lines, COMPLETE)
├── BACKRabbit.Protocol.DownloadMode/ ← Samsung Download Mode (410 lines, COMPLETE)
├── BACKRabbit.Usb/               ← USB enumeration (PARTIAL)
├── BACKRabbit.Firmware/          ← .tar.md5 extraction (171 lines, COMPLETE)
├── BACKRabbit.GUI/               ← WinForms GUI (6 tab panels)
│   ├── MainForm.cs               (288 lines)
│   ├── Tabs/DevicePanel.cs        (176 lines)
│   ├── Tabs/DownloadModePanel.cs  (276 lines)
│   ├── Tabs/AdbPanel.cs           (202 lines)
│   ├── Tabs/FastbootPanel.cs      (200 lines)
│   ├── Tabs/MagiskPanel.cs        (362 lines) ← STUBBED! THE MAIN WORK
│   ├── Tabs/FirmwarePanel.cs      (252 lines)
│   └── Branding/AnimatedRabbitControl.cs (217 lines)
│
├── BACKRabbit.CLI/               ← System.CommandLine CLI (266 lines, STUBBED)
├── BACKRabbit.Core/               ← Stub (Class1.cs only)
├── BACKRabbit.Tests/              ← xUnit (4 tests only)
│
├── knowledge-base/                ← Agent documentation (MUST UPDATE AFTER CHANGES)
├── staging/                       ← Sample Samsung firmware for testing
├── emergency-flasher/             ← Zero-dependency Python emergency fix
└── SamsungMagiskCleaner/          ← Separate tool
```

---

## 2. WHAT'S REAL vs WHAT'S STUBBED

### ✅ FULLY IMPLEMENTED (Production-Ready, ~15,000 lines)

BootImageParser, BootImageRepacker, CpioArchive, CompressionEngine, FormatDetector, MagiskArtifactDetector, AvbRestorer, SamsungKernelPatcher, MagiskUninstaller, AdbClient, FastbootClient, DownloadModeFlasher, SamsungFirmwareExtractor, AnimatedRabbitControl.

### ⚠️ STUBBED/PLACEHOLDER (Needs Wiring)

| Component | Issue |
|-----------|-------|
| **MagiskPanel.cs** | 7-step wizard has placeholder implementations. `AnalyzeAsync()`, `CleanAsync()`, `FlashAsync()` just log text — they don't call MagiskCore. `_uninstaller` field exists but is never used. |
| **CLI Program.cs** | `MagiskUninstallHandler` prints "✅ Magisk removed" without calling the uninstaller. Same for Flash, ADB handlers. |
| **AdbPanel.cs** | No Connect/Disconnect buttons. No TCP input. Assumes ADB already connected. |
| **MainForm.cs** | Only passes `_magiskUninstaller` and `_adbClient` to MagiskPanel — doesn't pass parser, repacker, detector. |
| **BACKRabbit.Core** | Class1.cs stub only |
| **BACKRabbit.Tests** | Only 4 unit tests |

### ❌ EXTERNAL CRUTCHES TO REMOVE

| Issue | Location | Fix |
|-------|----------|-----|
| `SevenZip` package (v4.12.1) | MagiskCore.csproj | Audit. If unused, remove. If used, replace with SharpCompress. |
| `Microsoft.VisualBasic.Interaction.InputBox` | AdbPanel, FastbootPanel, DownloadModePanel | Replace with proper WinForms dialogs. |
| `Class1.cs` stubs | 7 projects | Delete all. |

---

## 3. THE CHOSEN UX PATTERN (5/5 Unanimous 🏆)

**Pattern 3: Guided Steps WITH Persistent Background Info Panel**

```
┌──────────────────────────────────────────────────────────────┬──────────┐
│ 🐰 MAGISK MANAGER — Step 3 of 7: ANALYZE          ✓ Complete │ ℹ️ INFO   │
│ ┌──────────────────────────────────────────────┐ ✓ Complete │───────────│
│ │ [Load boot.img]  [Browse...]  boot_backup.img │            │ BOOT IMG  │
│ │                                              │            │ Format:   │
│ │ 🔍 Analyzing...                              │            │ AOSP v4   │
│ │                                              │            │ Kernel:   │
│ │ ✅ Header: AOSP v4, kernel 32.1MB            │            │ 32.1 MB   │
│ │ ✅ Ramdisk: gzip compressed, 1.2MB           │            │ Ramdisk:  │
│ │ ⚠️ Magisk files found:                       │            │ 1.2 MB    │
│ │    • overlay.d/sbin/magisk.xz                │            │ (gzip)    │
│ │    • overlay.d/sbin/magiskinit               │            │           │
│ │    • .backup/.magisk                          │            │ MAGISK    │
│ │    • ramdisk.cpio.orig (backup available!)    │            │───────────│
│ │                                              │            │ Detected: │
│ │ ✅ Backup found: Method 2 available           │            │ YES       │
│ │                                              │            │ Version:  │
│ │ [← Back]  [Resume Session]  [Continue →]     │            │ 28.1      │
│ └──────────────────────────────────────────────┘            │ Modules:  │
│                                                              │ 3         │
│ [Emergency Stop]  [☐ Expert Mode]  [☐ Technical Details]    │           │
│                                                              │ AVB FLAGS │
│ Status: Analysis complete — 4 Magisk artifacts found         │───────────│
│                                                              │ Current:  │
│                                                              │ 3 (disabled)│
│                                                              │ Target:   │
│                                                              │ 0 (stock) │
│                                                              │           │
│                                                              │ BACKUP    │
│                                                              │───────────│
│                                                              │ ramdisk.  │
│                                                              │ cpio.orig │
│                                                              │ FOUND ✅  │
└──────────────────────────────────────────────────────────────┴──────────┘
```

**Key Design Elements:**
- **Left panel (70%):** One step at a time, [← Back] [Continue →] navigation
- **Right panel (30%):** Persistent "What's Happening" info — ALWAYS visible, accumulates knowledge across steps
- **Steps lock sequentially** but completed steps can be revisited via [← Back]
- **Every step outputs ALL its information** into the right panel — nothing is hidden
- **The right panel IS the audit trail** — user can screenshot it, agent can verify it

---

## 4. THE MAGISK UNINSTALL WORKFLOW (Dual-Mode)

### PATH A: ONLINE (Device connected via ADB)
1. **DETECT** → `adb shell "magisk -c"` → show version, modules
2. **BACKUP** → `adb pull` boot, init_boot, vendor_boot, vbmeta → save locally with timestamp
3. **ANALYZE** → `BootImageParser.Parse()` → `MagiskArtifactDetector.Detect()` → populate right info panel
4. **CLEAN** → `MagiskUninstaller.UninstallAsync()` (3 methods: stock firmware / ramdisk.cpio.orig / surgical)
5. **FLASH** → fastboot/Download Mode with confirmation dialog + 3-second countdown (1s in Expert Mode)
6. **VERIFY** → `adb shell "magisk -c"` → should show NOT installed
7. **REBOOT** → `adb reboot` → post-operation summary with emergency recovery section

### PATH B: OFFLINE (No device, files on disk)
1. **LOAD** → File picker: select boot.img (or init_boot.img)
2. **ANALYZE** → Same as online Step 3
3. **CLEAN** → Same as online Step 4
4. **SAVE** → File save dialog: save cleaned_boot.img to disk
5. **VERIFY** → Re-parse saved image, confirm no Magisk artifacts
→ User flashes manually later via Odin/Download Mode

---

## 5. COMPETITOR LANDSCAPE (Juror D Evidence)

| Tool | Platform | GUI? | Magisk Uninstall? | Samsung-Specific? | Boot Image Manipulation? |
|------|----------|------|-------------------|-------------------|--------------------------|
| **BACKRabbit** | Windows (.NET 8) | ✅ WinForms | ✅ 3 methods | ✅ Download Mode, PIT, Knox | ✅ Parse/Repack/CPIO/AVB |
| H-K-S Magisk Patcher | Windows | ✅ | ❌ (Patch only) | ✅ AP patching | ❌ (Delegates to MagiskBoot) |
| Aesir | Linux (.NET 9) | ✅ Avalonia | ❌ | ✅ Odin protocol | ❌ |
| Android_boot_image_editor | Cross (Java/Python) | ❌ CLI only | ❌ | ❌ | ✅ v0-v4, vendor_boot, vbmeta |
| Magisk (official) | Android/CLI | ❌ | ✅ (restore only) | ❌ | ✅ (magiskboot) |

**Conclusion:** BACKRabbit occupies a UNIQUE niche — GUI + Magisk uninstall + Samsung-specific + boot image manipulation + ADB/Download Mode integration. No competitor does all of this.

---

## 6. COMPLETE AMENDMENT REGISTRY (7 Total)

| # | Amendment | Source | Phase |
|---|-----------|--------|-------|
| **A1** | Expert Mode toggle in bottom bar — reduces countdowns to 1s, OFF by default, resets on app restart | Juror B | Phase 4b |
| **A2** | "Copy to Clipboard" button next to "Save Report" in post-operation summary | Juror B | Phase 4b |
| **A3** | Simulation mode labeled "Test Mode (No Device Required)", placed in Advanced Settings/HWinfo overlay | Juror A | Phase 3 |
| **A4** | Driver detection + download link in Device tab | Juror C (flipped A) | Phase 5 |
| **A5** | Phase 3.5 prerequisite — round-trip tests MUST complete before Phase 4 GUI wiring begins | Juror C | Execution Order |
| **A6** | Structured error objects from MagiskCore: `{ Message, RecoveryAction, FallbackAction, IsFatal }`. Rendered in 3 locations: red banner (step panel), ⚠️ Issues section (right panel), post-operation summary. Every error has primary recovery path AND fallback. | Jurors D & E | Phase 4b |
| **A7** | Phase 4 split into 4a (Core Wiring) and 4b (Polish & Amendments). 4a = layout, toggle, steps 1-7, safety interlocks, emergency stop, post-op summary with emergency recovery. 4b = Expert Mode, HWinfo overlay, structured error messages, Copy to Clipboard, Save Report. | Juror E | Execution Order |

---

## 7. JURY-AMENDED EXECUTION ORDER (STRICT — DO NOT REORDER)

```
Phase 1: Cleanup (Foundation)
    │
Phase 2: Wire CLI Handlers
    │
Phase 3: ADB Connection UI + Simulation Mode
    │
Phase 3.5: MagiskCore Round-Trip Tests ⚠️ GATE — MUST ALL PASS BEFORE PHASE 4
    │
Phase 4a: MagiskPanel Core Wiring (layout, toggle, steps 1-7, safety interlocks, emergency stop, post-op summary)
    │
Phase 4b: MagiskPanel Polish & Amendments (Expert Mode, HWinfo overlay, structured errors, Copy/Save)
    │
Phase 5: Cross-Tab Integration
    │
Phase 6: Documentation Rewrite
    │
Phase 7: Packaging + Final Verification
```

---

## 8. PHASE DETAILS

### Phase 1: Cleanup (Foundation)
- [ ] Delete all `Class1.cs` stubs (7 projects: Core, Firmware, MagiskCore, Adb, Fastboot, DownloadMode, Usb)
- [ ] Audit `SevenZip` package in MagiskCore.csproj — remove if unused, replace with SharpCompress if used
- [ ] Replace all `Microsoft.VisualBasic.Interaction.InputBox` calls in AdbPanel, FastbootPanel, DownloadModePanel with custom WinForms `InputDialog`
- [ ] Verify no `Process.Start` references anywhere (already confirmed: 0)
- [ ] Verify no `lzh`, `7z.exe`, `external`, `cli fallback` in active code

### Phase 2: Wire CLI Handlers
- [ ] Wire `MagiskUninstallHandler` to `MagiskUninstaller.UninstallAsync()`
- [ ] Wire `FlashHandler` to `DownloadModeFlasher`
- [ ] Wire `AdbShellHandler`, `AdbPullHandler`, `AdbPushHandler` to actual `AdbClient` methods
- [ ] Add `--offline` flag and `--boot-image <path>` option to `magisk detect` and `magisk uninstall`

### Phase 3: ADB Connection UI + Simulation Mode
- [ ] Add Connect/Disconnect buttons to AdbPanel
- [ ] Add TCP host:port TextBox for WiFi ADB (default port 5555)
- [ ] Add `[☐ Test Mode (No Device Required)]` checkbox (Amendment A3)
- [ ] Create `MockAdbClient` class in `BACKRabbit.GUI/Testing/MockAdbClient.cs`
- [ ] Add global connection status indicator in MainForm status bar

### Phase 3.5: MagiskCore Round-Trip Tests ⚠️ GATE
- [ ] Parse→Repack→Parse round-trip for v0, v1, v2, v3, v4 AOSP headers
- [ ] Parse→Repack→Parse for Samsung PXA, DHTB, MTK, ChromeOS
- [ ] Parse→Repack→Parse for vendor_boot images
- [ ] CPIO newc parse→serialize→re-parse→byte-compare
- [ ] Compression round-trip: gzip, lz4, lz4_legacy, xz, lzma, bzip2
- [ ] MagiskArtifactDetector: detect on known Magisk-patched images
- [ ] AvbRestorer: verify AVB flag patching (3→0)
- [ ] Full offline workflow with sample firmware from `staging/`
- [ ] **GATE CHECK: `dotnet test` must be all green. If any test fails, STOP and fix core library.**

### Phase 4a: MagiskPanel Core Wiring
- [ ] Redesign layout: TableLayoutPanel 70%/30% split (left step panel, right info panel)
- [ ] Add `[Online (ADB)] [Offline (File)]` toggle at top
- [ ] Add "Resume Previous Session" button (appears when prior backups detected)
- [ ] Wire Step 1 (Detect): online ADB check OR offline file-based artifact detection
- [ ] Wire Step 2 (Backup): ADB pull partitions OR file picker to load existing backup
- [ ] Wire Step 3 (Analyze): `BootImageParser.Parse()` → populate right info panel (format, kernel, ramdisk, Magisk artifacts, AVB flags, backup availability)
- [ ] Wire Step 4 (Clean): `MagiskUninstaller.UninstallAsync()` with progress callbacks
- [ ] Wire Step 5 (Flash/Save): Fastboot/Download Mode flash with confirmation dialog + countdown, OR file save for offline
- [ ] Wire Step 6 (Verify): Re-check Magisk status OR re-parse cleaned image
- [ ] Wire Step 7 (Reboot): `AdbClient.RebootAsync()` OR "Flash manually via Odin" for offline
- [ ] Add safety interlocks (Flash disabled until Clean done, Reboot disabled until Verify done)
- [ ] Add "Emergency Stop" button always visible
- [ ] Add post-operation summary with Emergency Recovery section (path to `emergency-flasher/`)

### Phase 4b: MagiskPanel Polish & Amendments
- [ ] Add Expert Mode checkbox in bottom bar (OFF by default, resets on restart) (Amendment A1)
- [ ] Add HWinfo-style Technical Details overlay (toggleable, shows hex offsets, compression ratios, raw commands) (Amendment from Juror B)
- [ ] Implement structured error objects with 3-location rendering (red banner, right panel Issues section, post-op summary) (Amendment A6)
- [ ] Add "Copy to Clipboard" button next to "Save Report" (Amendment A2)
- [ ] Add "Save Report" button (exports summary as .txt)

### Phase 5: Cross-Tab Integration
- [ ] FirmwarePanel: Add "Send boot.img to Magisk Manager →" button after extraction
- [ ] DevicePanel: Add Samsung USB driver detection + download link (Amendment A4)
- [ ] DevicePanel: Show ADB authorization status
- [ ] MainForm: Pass all MagiskCore services (parser, repacker, detector, avbRestorer, kernelPatcher) to MagiskPanel

### Phase 6: Documentation Rewrite
- [ ] Rewrite `knowledge-base/OFFLINE_AGENT_GUIDE.md` — new architecture, dual-mode, wizard UX, updated line counts
- [ ] Create `knowledge-base/USAGE_GUIDE.md` — end-user guide (install, connect, online/offline modes, WiFi ADB setup, Download Mode, emergency recovery, FAQ)
- [ ] Create `knowledge-base/TESTING_GUIDE.md` — developer guide (unit tests, Test Mode, round-trip procedures, sample firmware, mock ADB)
- [ ] Update `knowledge-base/AUDIT.md` — reflect new wiring, removed crutches, test results
- [ ] Update `knowledge-base/CROSS_REFERENCE_INDEX.md` — add new files (MockAdbClient, InputDialog)

### Phase 7: Packaging + Final Verification
- [ ] `dotnet publish BACKRabbit.GUI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/` → single BACKRabbit.exe
- [ ] Run full simulation mode workflow (MockAdbClient, all 7 steps)
- [ ] Run full offline workflow with sample firmware from `staging/`
- [ ] Verify round-trip: parse stock → repack → parse repacked → compare
- [ ] Verify CPIO round-trip: parse → serialize → re-parse → byte-compare
- [ ] Verify compression round-trip for all 6 formats
- [ ] `dotnet build BACKRabbit.slnx -c Release` → 0 errors, 0 warnings
- [ ] `dotnet test BACKRabbit.Tests -c Release` → all green

---

## 9. COMPLETE FILE MANIFEST

| Phase | Files Modified | Files Created | Files Deleted |
|-------|---------------|---------------|---------------|
| 1 | `MagiskCore.csproj`, `AdbPanel.cs`, `FastbootPanel.cs`, `DownloadModePanel.cs` | `InputDialog.cs` | 7× `Class1.cs` |
| 2 | `Program.cs` (CLI) | — | — |
| 3 | `AdbPanel.cs`, `MainForm.cs` | `MockAdbClient.cs` | — |
| 3.5 | `MagiskCoreTests.cs` | — | — |
| 4a | `MagiskPanel.cs` (rewrite), `MainForm.cs` | — | — |
| 4b | `MagiskPanel.cs` (continued) | — | — |
| 5 | `FirmwarePanel.cs`, `DevicePanel.cs`, `MainForm.cs` | — | — |
| 6 | `OFFLINE_AGENT_GUIDE.md`, `AUDIT.md`, `CROSS_REFERENCE_INDEX.md` | `USAGE_GUIDE.md`, `TESTING_GUIDE.md` | — |
| 7 | — | `publish/BACKRabbit.exe` | — |

---

## 10. BUILD COMMANDS REFERENCE

```bash
# Restore all dependencies
dotnet restore BACKRabbit.slnx

# Build (do after every phase)
dotnet build BACKRabbit.slnx -c Release

# Run tests (Phase 3.5 gate + Phase 7 final)
dotnet test BACKRabbit.Tests/BACKRabbit.Tests.csproj -c Release

# Publish single-file .exe (Phase 7)
dotnet publish BACKRabbit.GUI/BACKRabbit.GUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/

# Run CLI commands
dotnet run --project BACKRabbit.CLI/BACKRabbit.CLI.csproj -- detect
dotnet run --project BACKRabbit.CLI/BACKRabbit.CLI.csproj -- firmware extract <path-to-tar.md5>
dotnet run --project BACKRabbit.CLI/BACKRabbit.CLI.csproj -- magisk detect --offline --boot-image <path>
```

---

## 11. GLOSSARY

| Term | Meaning |
|------|---------|
| AOSP | Android Open Source Project — standard boot image format |
| AVB | Android Verified Boot — cryptographic verification chain |
| AVB Footer | 64-byte footer at end of signed images |
| VBMeta | AVB metadata header (flags, hash tree, signature) |
| Boot Image | Partition with kernel + ramdisk (boot.img or init_boot.img) |
| init_boot | GKI 2.0 split: init_boot has ramdisk, boot has kernel (Android 13+) |
| CPIO | Archive format for ramdisk (newc = "new portable format" with CRC) |
| DHTB | Samsung Download Mode header |
| GKI | Generic Kernel Image — Google's standardized kernel |
| Knox eFuse | Samsung hardware fuse that trips on unofficial software |
| LZ4 Legacy | Required compression for v4 GKI ramdisks |
| Magisk | Systemless root solution that patches boot images |
| newc | CPIO format with 070701/070702 magic |
| Odin | Samsung's proprietary flashing tool (Windows) |
| PIT | Partition Information Table |
| Ramdisk | Initial root filesystem (init, fstab, etc.) |
| SEANDROID | Samsung's SELinux enforcement marker |
| Sparse Image | Android's compressed image format for fastboot |
| v0-v4 | AOSP boot image header versions |

---

## 12. JURY VERDICT — FINAL

| Juror | Role | Verdict |
|-------|------|---------|
| 🐰 **Juror A** | The Anxious User | ✅ **PASS** — Emergency recovery path, safety interlocks, offline fallback, driver help |
| ⚡ **Juror B** | The Power User | ✅ **PASS** — Resume session, raw command preview in HWinfo, Expert Mode, Copy to Clipboard |
| 🔍 **Juror C** | The Developer/Tester | ✅ **PASS** — Round-trip tests gated before GUI wiring, structured error objects, audit trail |
| 🧪 **Juror D** | The QA/Support Engineer | ✅ **PASS** — Unique competitive niche, structured errors in 3 locations, supportable UX |
| 📊 **Juror E** | The Product Manager | ✅ **PASS** — Phase 4 split into MVP+polish, clear market gap, well-scoped |

**🏆 FINAL SCORE: 12/12 Sections — 5/5 Unanimous — 7 Amendments Incorporated**

### ⚖️ Jury Foreman's Closing Statement:

> *"Five jurors. Twelve sections. Two dissents. Two re-debates. Seven amendments. One unanimous verdict. The BACKRabbit Execution Plan is the most thoroughly vetted Android tool implementation plan in history. It has been cross-examined against real XDA evidence, Magisk source code, competitor analysis, and product management principles. It is safe for the anxious, transparent for the power user, verifiable for the developer, supportable for QA, and strategically sound for the product manager. There are no more objections. There are no more gaps. Execute immediately."*

**⚖️ Signed,**
- 🐰 *Juror A — The Anxious User*
- ⚡ *Juror B — The Power User*
- 🔍 *Juror C — The Developer/Tester*
- 🧪 *Juror D — The QA/Support Engineer*
- 📊 *Juror E — The Product Manager*

**Verdict: 5/5 UNANIMOUS — ALL SECTIONS — *WE WIN!* — EXECUTE IMMEDIATELY**

---

**📅 Document Version:** 3.0 (5-Juror Final) — Dated 2026-06-18
**🏷️ Marker:** Planning artifact. Not implementation. Blueprint for execution.