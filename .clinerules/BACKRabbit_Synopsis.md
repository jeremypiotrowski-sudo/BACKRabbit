# BACKRabbit — Project Synopsis for Agents

> **Read this before working on this codebase.** It contains everything you need to know without being retold.

---

## 1. Project Identity

**BACKRabbit** is a Samsung Android device toolkit for boot/firmware verification, Magisk removal, and post-attack device recovery. It communicates directly with devices via USB using pure C# protocol implementations — **no external `adb.exe` or `fastboot.exe` required.**

**Philosophy:** Verify > Trust | Evidence > Claims | Audit > Assertion

---

## 2. Solution Map (12 Projects)

| # | Project | Purpose | Key Files |
|---|---------|---------|-----------|
| 1 | `BACKRabbit.CLI` | Command-line interface | `Program.cs` (537 lines), `Commands/FirehoseCommands.cs` (483 lines) |
| 2 | `BACKRabbit.Core` | Shared utilities, compression | `CompressionEngine.cs` (206 lines) |
| 3 | `BACKRabbit.Usb` | USB device enumeration/communication | `UsbDeviceManager.cs` (468 lines) |
| 4 | `BACKRabbit.Protocol.Adb` | Full ADB wire protocol | `AdbClient.cs` (1295 lines), `IAdbClient.cs` (51 lines) |
| 5 | `BACKRabbit.Protocol.Fastboot` | Fastboot flashing protocol | `FastbootClient.cs` (327 lines), `SparseImage.cs` |
| 6 | `BACKRabbit.Protocol.DownloadMode` | Samsung Odin/Download Mode | `DownloadModeFlasher.cs`, `PitFile.cs` |
| 7 | `BACKRabbit.Protocol.Firehose` | Qualcomm EDL/Firehose protocol | `FirehoseClient.cs`, `SaharaStateMachine.cs`, `Rescue/` subsystem |
| 8 | `BACKRabbit.MagiskCore` | Boot image parsing/repacking, Magisk detection | `Parser/BootImageParser.cs` (613 lines), `Repacker/BootImageRepacker.cs` (350 lines) |
| 9 | `BACKRabbit.Firmware` | Samsung firmware download/extraction | `FirmwareSourcer.cs` (438 lines), `FirmwareImporter.cs`, `SamsungFirmwareExtractor.cs` (171 lines) |
| 10 | `BACKRabbit.Tests` | Integration/unit tests | Various test files |
| 11 | `BACKRabbit.Protocol.Firehose.Tests` | Firehose/rescue tests | `Rescue/` test files |
| 12 | `SamsungMagiskCleaner` | Python alternative (standalone) | `main.py`, `boot_image.py`, etc. |

---

## 3. Architecture Layers

```
┌─────────────────────────────────────────┐
│  BACKRabbit.CLI (Program.cs)            │  ← User commands
│  Commands: firehose, firmware, magisk,  │
│            adb, fastboot, flash, detect  │
├─────────────────────────────────────────┤
│  Protocol Layer                          │
│  ├── BACKRabbit.Protocol.Adb            │  ← Pure C# ADB (no adb.exe)
│  ├── BACKRabbit.Protocol.Fastboot       │  ← Pure C# Fastboot
│  ├── BACKRabbit.Protocol.DownloadMode   │  ← Samsung Odin protocol
│  └── BACKRabbit.Protocol.Firehose       │  ← Qualcomm EDL/Sahara
│      └── Rescue/ (orchestrator)         │
├─────────────────────────────────────────┤
│  BACKRabbit.MagiskCore                   │  ← Boot image parse/repack
│  BACKRabbit.Firmware                     │  ← Firmware source/import
├─────────────────────────────────────────┤
│  BACKRabbit.Usb (UsbDeviceManager)      │  ← LibUsbDotNet wrapper
│  BACKRabbit.Core                         │  ← Compression, utilities
└─────────────────────────────────────────┘
```

---

## 4. Key Components

### 4.1 ADB Client (`BACKRabbit.Protocol.Adb/AdbClient.cs`)
- **1,295 lines** — Full ADB wire protocol implementation
- Connects via USB (`ConnectUsbAsync`) or TCP (`ConnectTcpAsync`)
- Falls back to ADB server proxy (`127.0.0.1:5037`) for TLS/Android 14+
- Methods: `ExecuteShellAsync`, `ExecuteRootShellAsync`, `PushFileAsync`, `PullFileAsync`, `RebootAsync`, `RebootDownloadAsync`, `RebootBootloaderAsync`, `RebootRecoveryAsync`, `CheckMagiskStatusAsync`, `GetPropertiesAsync`, `CheckBootloaderLockStatusAsync`, `WaitForDeviceAsync`
- **No external `adb.exe` needed** — this IS the ADB client

### 4.2 Fastboot Client (`BACKRabbit.Protocol.Fastboot/FastbootClient.cs`)
- **327 lines** — Full Fastboot wire protocol
- `ConnectAsync`, `FlashAsync`, `EraseAsync`, `RebootAsync`, `GetVariableAsync`
- Supports sparse images via `SparseImage.cs`

### 4.3 Firehose/EDL (`BACKRabbit.Protocol.Firehose/`)
- `FirehoseClient.cs` — Qualcomm EDL communication
- `SaharaStateMachine.cs` — Sahara protocol handshake
- `LoaderDatabase.cs` — Auto-detects Firehose loaders from `Loaders/` directory
- `LoaderDetector.cs` — Matches loader to device MSM ID + PK Hash
- `WinUsbTransport.cs` — WinUSB transport for EDL

### 4.4 Rescue Subsystem (`BACKRabbit.Protocol.Firehose/Rescue/`)
- `RescueOrchestrator.cs` — 7-phase rescue flow (0-7)
- `RebootDownloadManager.cs` — ADB + button-guided Download Mode entry (90s timeout, 3 retries)
- `PartitionDiagnostics.cs` — SHA256 comparison against known-good backup
- `PartitionRestorer.cs` — Flash partitions from backup
- `MagiskRemover.cs` — Surgical Magisk removal from boot images
- `QFuseAuditor.cs` — QFuse status audit
- `RescueReport.cs` — JSON report generation with `OverallVerdict` enum

### 4.5 USB Manager (`BACKRabbit.Usb/UsbDeviceManager.cs`)
- Wraps LibUsbDotNet 2.2.8
- `EnumerateSamsungDevices()` — Samsung VID 0x04E8 only
- `EnumerateAllAdbDevices()` — **NEW** — All Android vendors (Nokia 0x0421, Google 0x18D1, Xiaomi 0x2717, etc.) + ADB interface class detection
- `ListDevices(samsungOnly)` — Static USB enumeration
- Samsung Download Mode PIDs: `0x685D, 0x6860, 0x6862, 0x6864, 0x6601`
- Samsung ADB PIDs: `0x6866, 0x6867, 0x6868, 0x6869`

### 4.6 Firmware (`BACKRabbit.Firmware/`)
- `FirmwareSourcer.cs` — Samsung FUS download (⚠️ **FUS API returns 403 — dead**)
- `FirmwareImporter.cs` — **NEW** — Imports pre-downloaded ZIP, extracts .tar.md5 → .img, generates `manifest.json` with SHA256
- `SamsungFirmwareExtractor.cs` — Extracts .tar.md5 (MD5 verify → TarArchive → partition images), also handles super.img sparse images

### 4.7 Magisk Core (`BACKRabbit.MagiskCore/`)
- `Parser/BootImageParser.cs` — AOSP v0-v4, Samsung PXA, MTK, DHTB, BLOB formats
- `Repacker/BootImageRepacker.cs` — Rebuild boot images with modified ramdisk/kernel
- `RamdiskEditor/MagiskArtifactDetector.cs` — Detects Magisk artifacts in ramdisk
- `AvbRestorer/` — AVB footer detection and flag patching
- `Compression/` — 20+ compression format detection
- `SamsungKernel/` — Samsung kernel patching (syscall table hooks)

---

## 5. CLI Command Tree

```
backrabbit
├── detect                          # Detect connected Samsung devices
├── magisk
│   ├── detect                      # Check Magisk status
│   ├── wizard                      # 7-step interactive uninstall
│   └── uninstall                   # Remove Magisk
├── flash                           # Flash via Download Mode
├── adb
│   ├── shell <command>             # Execute shell command
│   ├── pull <remote> <local>       # Pull file
│   └── push <local> <remote>       # Push file
├── fastboot
│   ├── flash <partition> <image>   # Flash via Fastboot
│   └── reboot                      # Reboot device
├── firmware
│   ├── extract <input>             # Extract .tar.md5 → .img
│   ├── source                      # Download from Samsung FUS (⚠️ dead)
│   └── import --input <ZIP> --output <DIR>  # NEW: Import pre-downloaded ZIP
├── firehose
│   ├── detect                      # List EDL devices
│   ├── info                        # Show chip info
│   ├── printgpt                    # Dump GPT
│   ├── dump                        # Read partition
│   ├── flash                       # Write partition
│   ├── erase                       # Erase partition
│   ├── reset                       # Reboot device
│   ├── nop                         # Test if Firehose alive
│   ├── storageinfo                 # Query storage type
│   └── rescue
│       ├── diagnose                # Diagnose partitions
│       ├── restore                 # Restore from backup
│       ├── fuses                   # Audit QFuses
│       ├── unmagisk                # Remove Magisk
│       └── full                    # Full rescue sequence
│           --device <COM>          # Device path
│           --loader <path>         # Firehose .elf path
│           --backup <dir>          # Known-good backup dir
│           --dry-run               # Zero writes
│           --force                 # Override blocklist
│           --skip-dl-mode-check    # Skip Download Mode check
└── trap-escape                     # Clean /data/adb/ residue
```

---

## 6. What's Built vs Pending

### ✅ Complete & Compiling
- ADB client (full protocol, no external binary)
- Fastboot client (full protocol)
- Firehose/EDL client with Sahara
- Rescue orchestrator (7 phases)
- Download Mode reboot manager (ADB + button fallback, 90s timeout, 3 retries)
- Partition diagnostics (SHA256 compare)
- Partition restorer (flash from backup)
- Magisk remover (surgical)
- QFuse auditor
- Firmware importer (ZIP → .img + manifest.json)
- Samsung firmware extractor (.tar.md5 → .img)
- Boot image parser/repacker (AOSP v0-v4, Samsung, MTK)
- Magisk artifact detector
- USB device enumeration (Samsung + all Android vendors)
- Rescue report generation (JSON)

### ❌ Known Dead/Broken
- **Samsung FUS API** — `fota-cloud-dn.ospserver.net` returns HTTP 403 for all auth methods (MD5, bearer). Confirmed via `probe_fus.ps1`.

### 🔜 Pending (Needs External Files)
- Firehose loader `.elf` for SM8650 (Snapdragon 8 Gen 3) — must be sourced from Telegram/XDA
- Stock firmware ZIP for target device — must be downloaded from SamMobile/SamFw
- **Nothing has been tested on an actual phone yet** — all testing has been unit tests only

---

## 7. Critical APIs

### AdbClient (IAdbClient)
```csharp
Task<bool> ConnectTcpAsync(string host, int port, CancellationToken ct);
Task<bool> ConnectUsbAsync(UsbDeviceManager usb, CancellationToken ct);
Task<string> ExecuteShellAsync(string command, CancellationToken ct);
Task<string> ExecuteRootShellAsync(string command, CancellationToken ct);
Task<bool> PushFileAsync(string local, string remote, CancellationToken ct);
Task<bool> PullFileAsync(string remote, string local, CancellationToken ct);
Task<bool> RebootAsync(string mode, CancellationToken ct);
Task<bool> RebootDownloadAsync(CancellationToken ct);  // Samsung-specific
Task<bool> RebootBootloaderAsync(CancellationToken ct);
Task<bool> RebootRecoveryAsync(CancellationToken ct);
Task<MagiskStatus> CheckMagiskStatusAsync(CancellationToken ct);
Task<Dictionary<string, string>> GetPropertiesAsync(CancellationToken ct);
Task<BootloaderLockStatus> CheckBootloaderLockStatusAsync(CancellationToken ct);
Task<bool> WaitForDeviceAsync(int timeoutMs, CancellationToken ct);
```

### UsbDeviceManager
```csharp
static List<UsbDeviceInfo> ListDevices(bool samsungOnly = false);
static List<BackRabbitUsbDeviceInfo> EnumerateAllAdbDevices();  // NEW
List<BackRabbitUsbDeviceInfo> EnumerateSamsungDevices();
bool Open(int vid, int pid);
bool OpenDevice(int productId);
int Read(byte[] buffer, int timeout);
int Write(byte[] buffer, int timeout);
```

### FirehoseClient
```csharp
Task InitializeAsync(string loaderPath);
Task<byte[]> ReadPartitionAsync(string name, int lun, int sectorSize, CancellationToken ct);
Task<bool> WritePartitionAsync(string name, byte[] data, int lun, int sectorSize, CancellationToken ct);
Task<bool> ErasePartitionAsync(string name, int lun, CancellationToken ct);
Task<List<GptPartitionEntry>> PrintGptAsync(int lun, CancellationToken ct);
Task ResetAsync(string mode, CancellationToken ct);
Task<byte[]> PeekAsync(uint address, uint size, CancellationToken ct);
```

### FirmwareImporter (NEW)
```csharp
Task<FirmwareImportResult> ImportAsync(string zipPath, string outputDir);
// Result contains: Manifest (with PartitionEntry list + SHA256), OutputDir, Success
```

---

## 8. Dependencies

### NuGet Packages
- `LibUsbDotNet` 2.2.8 (⚠️ .NET Framework 4.6.1 target, works on net8.0 with warning)
- `SharpCompress` 0.37.2/0.38.0 (⚠️ moderate vulnerability GHSA-6c8g-7p36-r338)
- `System.CommandLine` (CLI framework)
- `System.IO.Ports` 8.0.0 (serial port for EDL)
- `Spectre.Console` (TUI rendering)
- `xUnit` 2.5.3 (testing)

### Project References
- `BACKRabbit.CLI` → all protocol projects + Firmware + MagiskCore
- `BACKRabbit.Protocol.Firehose` → Usb, MagiskCore, **Adb** (added for RebootDownloadManager)
- `BACKRabbit.Firmware` → MagiskCore, Fastboot, **Adb** (added for CSC auto-detection)
- `BACKRabbit.Protocol.Adb` → Usb
- `BACKRabbit.Protocol.Fastboot` → Usb

---

## 9. Testing Status

| Test Suite | Status | Notes |
|-----------|--------|-------|
| `RebootDownloadManagerTests` | ✅ 7 tests passing | Timeout constant, retry count, skip paths |
| `RescueOrchestratorTests` | ✅ 32 tests passing | Dry-run, live-run, blocklist, Download Mode skip |
| `FirmwareSourcerIntegrationTests` | ⚠️ Conditional | Requires `BACKRABBIT_TEST_IMEI` env var |
| `BootImageParserTests` | ✅ Passing | AOSP v0-v4 format parsing |
| Phone testing | ❌ **Never done** | Needs Firehose loader + firmware ZIP |

---

## 10. Rules for Agents

1. **Do NOT assume external tools exist.** `adb` and `fastboot` are NOT on PATH. Use `AdbClient` and `FastbootClient` classes directly.
2. **Do NOT invent APIs.** Check the codebase before suggesting `ILogger`, `GetAllDevices()`, or other imaginary methods.
3. **Samsung FUS is dead.** Don't suggest `firmware source` — it returns 403. Use `firmware import` with pre-downloaded ZIPs.
4. **USB detection was Samsung-only.** `EnumerateAllAdbDevices()` now exists for non-Samsung devices (Nokia, Xiaomi, etc.).
5. **The rescue flow needs two external files:** Firehose loader (.elf) and stock firmware ZIP. The agent cannot download these.
6. **Build before committing.** `dotnet build BACKRabbit.slnx` must pass with 0 errors.
7. **CRLF warnings are normal.** Git converts line endings automatically.
8. **LibUsbDotNet warnings are normal.** The package targets .NET Framework but works on net8.0.
9. **The `Loaders/` directory** is where Firehose .elf/.mbn files go. Naming: `{MSM_ID}_{PK_HASH}_{description}.elf`.
10. **The `.clinerules/WhoTheArchitechIs.md.txt`** file contains the project creator's history and philosophy — read it for context.

---

## 11. Recent Commits

| Commit | Date | Description |
|--------|------|-------------|
| `4cda7bd` | 2026-06-21 | Firmware import command + Loaders directory + FUS probe |
| `13e30b9` | 2026-06-21 | Download Mode reboot + firmware sourcing enhancements |

---

*Generated 2026-06-21. Append-only. Additions welcome.*