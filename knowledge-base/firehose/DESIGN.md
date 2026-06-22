# BACKRabbit Firehose — Design Decisions Log

> **Purpose**: Explain *why* things are done this way — prevents future "optimizations" that break things.
> **Generated**: 2026-06-21 | **Commit**: `9d328fe`

---

## 1. Why FirehoseClient Uses Sequential USB Transfers (Not Async)

**Problem**: Early prototypes used concurrent `UsbDevice.WriteAsync()` → caused:
- USB buffer overflows on high-latency cables
- Partition table corruption during GPT writes
- Intermittent `ERROR_SEM_TIMEOUT` on Windows 10 1809

**Solution**:
- Strict sequential `SendRawAsync()` → `ReceiveRawAsync()` pairs
- 4KB chunk size (matches Qualcomm 9008 bulk endpoint max packet)
- 50ms implicit delay between chunks (USB turnaround provides natural pacing)

**Evidence**:
- `FirehoseClient.cs:WritePartitionAsync()` lines 198-224
- `FirehoseClient.cs:SendCommandAsync()` lines 85-120 — accumulates response fragments up to 1MB
- Validated with Beagle USB 480 analyzer

**Stick Point**: If someone adds `Task.WhenAll` for parallel partition writes, it WILL corrupt the GPT. The Firehose programmer is single-threaded and expects sequential commands.

---

## 2. Why Partition Names Come From .tar.md5 (Not Hardcoded)

**Problem**: Samsung reuses partition names across models (e.g., `modem` = different size on Fold vs S-series). Hardcoding a partition list would flash wrong-sized images.

**Solution**:
- Extract literal filenames from `AP.tar.md5` (e.g., `boot.img` → partition `boot`)
- Validate against actual device GPT in Phase 1 diagnosis
- Prevents flashing 128MB `modem.img` to 64MB partition (silent corruption)

**Evidence**:
- `SamsungFirmwareExtractor.cs:GetPartitionName()` — strips extension from tar entry name
- `FirmwareSourcer.cs:SourceAsync()` — populates `ExtractedPartitions` from tar entries
- `PartitionDiagnostics.cs:RunAsync()` — cross-references with GPT

**Stick Point**: If someone adds a hardcoded partition list, it will break on new Samsung models. Always derive from the firmware package.

---

## 3. Why Rescue Workflow Has 7 Phases (Not Fewer)

**Problem**: Early designs tried to combine diagnosis + restore into one step. This caused:
- Restoring partitions before knowing which were tampered
- Missing QFuse audit (permanent damage assessment)
- No forensic evidence saved before modification

**Solution**: 7 distinct phases with explicit ordering:
```
[0] Verify backup → [1] Diagnose → [2] Audit fuses → [3] Restore → [4] Remove Magisk → [5] Verify → [6] Report
```

**Evidence**:
- `RescueOrchestrator.cs:RunFullRescueAsync()` — 7 sequential phases
- Each phase produces output consumed by next phase
- Phase 5 re-runs diagnosis to confirm restoration

**Stick Point**: Skipping Phase 2 (QFuse audit) means you won't know if damage is permanent. Skipping Phase 5 (verification) means you won't know if restore succeeded.

---

## 4. Why FUS Auth Uses Dual-Method Tokens

**Problem**: Samsung FUS API returns HTTP 403 without authentication. Different firmware versions require different auth methods.

**Solution**:
- **Method 1 (always)**: `MD5(model:region:imei)` → hex token
- **Method 2 (IMEI available)**: POST to `fota-cloud-dn.ospserver.net/auth/token` → bearer token
- Token cached for 25 minutes (avoids re-auth on retry)
- Cache cleared on HTTP 403 (forces re-auth with corrected values)

**Evidence**:
- `FirmwareSourcer.cs:GetAuthTokenAsync()` — dual-method with fallback
- `FirmwareSourcer.cs:QueryFusAsync()` — sends both `Authorization: Bearer` and `X-Auth-Token` headers
- `FirmwareSourcer.cs:_cachedAuthToken` + `_tokenExpiry` — 25-minute cache

**Stick Point**: If someone removes the MD5 fallback, FUS queries will fail for devices without IMEI. The bearer token endpoint is not always available.

---

## 5. Why Magisk Removal Saves Forensic Evidence First

**Problem**: Once you overwrite a boot partition, the original evidence is gone. If removal fails, you can't analyze what went wrong.

**Solution**:
- Before ANY write: save original partition to `backupDir/forensic/pre-unmagisk/{name}_{timestamp}.img`
- Before ANY restore: save to `backupDir/forensic/pre-restore/{name}_{timestamp}.img`
- Original hashes recorded in `RescueReport`

**Evidence**:
- `MagiskRemover.cs:RemoveFromSlotAsync()` lines 141-146 — forensic save before write
- `PartitionRestorer.cs:RestoreAsync()` lines 70-86 — forensic save before erase
- `RescueReport.cs:RestoreAction.PreRestoreHash` — recorded for audit

**Stick Point**: If someone removes the forensic save step, there's no way to prove what the attacker did. This is an audit trail requirement.

---

## 6. Why Never Restore `sec`, `ddr`, `limits`, `apdp`, `msadp`

**Problem**: These partitions contain device-unique data:
- `sec` — QFuse data (permanent, read-only at hardware level)
- `ddr` — DDR training data (calibrated per-device at factory)
- `limits` — Hardware voltage/frequency limits
- `apdp`/`msadp` — Debug policy (device-specific)

Restoring these from another device's firmware would permanently damage the device.

**Solution**: Hard blocklist in `PartitionRestorer.cs`:
```csharp
private static readonly HashSet<string> _neverRestore = new(StringComparer.OrdinalIgnoreCase)
{
    "sec", "ddr", "limits", "apdp", "msadp",
};
```

**Evidence**: `PartitionRestorer.cs:RestoreAsync()` lines 47-55 — checks blocklist before any operation.

**Stick Point**: NEVER remove entries from this list. Adding is safe; removing is dangerous.

---

## 7. Why TUI Uses Spectre.Console (Not Terminal.Gui or Raw Console)

**Problem**: Raw `Console.WriteLine` can't show progress bars, tables, or interactive prompts. Terminal.Gui is heavier and requires terminal mode switching.

**Solution**: Spectre.Console 0.49.0 provides:
- `AnsiConsole.Progress()` — live progress bars with multiple columns
- `SelectionPrompt<T>` — arrow-key navigable menus
- `TextPrompt<T>` — validated text input with default values
- `Table` — bordered, formatted data display
- All pure .NET, cross-platform, no native dependencies

**Evidence**:
- `FirmwareTui.cs` — full TUI flow using Spectre.Console
- `ProgressRenderer.cs` — wraps Spectre.Console progress for `IProgress<T>`
- `BACKRabbit.CLI.csproj` — Spectre.Console 0.49.0 reference

**Stick Point**: Spectre.Console uses ANSI escape codes. On Windows 10 before 1903, enable VT processing: `Console.OutputEncoding = Encoding.UTF8`. This is handled automatically by Spectre.Console.

---

## 8. Why BootImageParser Scans for Multiple Header Types

**Problem**: Android boot images come in many formats:
- AOSP v0-v4 (standard)
- Vendor v3-v4 (separate vendor_boot partition)
- Samsung PXA (custom header)
- MediaTek DHTB (custom header)
- LG/Qualcomm BLOB (custom header)
- ChromeOS (custom header)

A single-format parser would fail on non-AOSP devices.

**Solution**: `BootImageParser.Parse()` scans byte-by-byte for known magic bytes:
```
ANDROID! → AOSP v0-v4
VNDRBOOT → Vendor v3-v4
DHTB → MediaTek
BLOB → LG/Qualcomm
CHROMEOS → ChromeOS
SEANDROID → Samsung SEAndroid footer
AVB0 → AVB footer
```

**Evidence**: `BootImageParser.cs:Parse()` lines 36-65 — format detection loop.

**Stick Point**: Adding a new format requires adding magic bytes to `FormatDetector.cs` AND a new header struct. Don't add format detection without header parsing.

---

## 9. Why CpioArchive Uses newc Format (070701)

**Problem**: CPIO has multiple formats (bin, odc, newc, crc). Magisk uses newc (070701 magic) exclusively.

**Solution**: `CpioArchive.cs` implements newc format only:
- Magic: `070701` (8-char ASCII hex)
- All numeric fields: 8-char ASCII hex strings
- Filename: null-terminated, 4-byte aligned
- File data: 4-byte aligned
- Trailer: entry with name `TRAILER!!!`

**Evidence**: `CpioArchive.cs:Parse()` lines 26-96 — newc format parser.

**Stick Point**: If someone adds binary CPIO format support, they must NOT break newc compatibility. Magisk only uses newc.

---

## 10. Why CompressionEngine Uses SharpCompress (Not CLI Fallbacks)

**Problem**: Early prototypes shelled out to `gzip`, `lz4`, `xz` CLI tools. This caused:
- Platform dependency (Windows needs separate binaries)
- Path injection vulnerabilities
- Slower than in-process compression

**Solution**: Pure C# via SharpCompress:
- Gzip, LZ4, XZ, Zstd — all in-process
- No CLI fallbacks
- `CompressionEngine.cs` wraps SharpCompress with format detection

**Evidence**: `CompressionEngine.cs` (206 lines) — pure C# compression.
`BACKRabbit.MagiskCore.csproj` — SharpCompress 0.37.2 reference.

**Stick Point**: Do NOT add CLI fallbacks. They were removed for security and portability reasons.

---

## 11. Why Magisk Removal Happens AFTER Partition Restore (Flash-Then-Patch-Verify)

### Problem

Magisk root modifies boot images at the binary level. During rescue, we must:
1. Flash clean partitions to remove tampering
2. Remove Magisk artifacts from boot images
3. **Verify complete Magisk removal before declaring success**
4. Ensure the device boots clean after rescue

The question: **Should Magisk removal happen before or after flashing?**

### Solution: Flash-Then-Patch-Verify (Restore First, Then Remove Magisk, Then Verify Removal)

The `RescueOrchestrator.RunFullRescueAsync()` enforces a strict 7-step sequence with an implicit verification sub-step:

```
[0] Verify backup → [1] Diagnose → [2] Fuse audit → [3] Restore tampered → [4] Remove Magisk → [5] Final verify → [6] Report
```

**Step 3 (PartitionRestorer) runs BEFORE Step 4 (MagiskRemover).**

### Why This Ordering

1. **Clean Slate Principle:** Non-boot partitions (system, vendor, product) must be restored to stock before analyzing boot images. Magisk may have modified sepolicy, init.rc, or other on-disk artifacts that affect boot image analysis.

2. **Boot Image Dependency Chain:** MagiskRemover needs a clean reference to compare against. If the backup boot image is itself Magisk-patched (common in user backups), the remover must:
   - Read the device's current boot image via Firehose `read`
   - Parse it with `BootImageParser` (MagiskCore)
   - Detect Magisk artifacts with `MagiskArtifactDetector` (MagiskCore)
   - Compare against the backup boot image
   - If backup is also patched → use `MagiskUninstaller` (MagiskCore) to produce a clean boot image from the backup
   - Repack with `BootImageRepacker` (MagiskCore)
   - Flash the clean repacked image via Firehose `program`

3. **AVB Footer Chain:** vbmeta must be restored BEFORE boot image analysis because AVB footer verification depends on vbmeta's public key chain. A tampered vbmeta can mask boot image tampering.

4. **Verification Integrity:** Step 5 (final verification) re-runs PartitionDiagnostics to confirm all partitions are clean. If Magisk removal happened before partition restore, the verification would see restored-but-still-patched boot images and report false positives.

### Magisk Removal Verification Protocol

After Step 4 completes, the orchestrator implicitly verifies Magisk removal by re-reading and re-parsing the boot image. To declare Magisk successfully uninstalled:

1. **Boot image**: No `magiskinit` calls in `init.rc`, no `magisk*` binaries in ramdisk
2. **DTBO**: No Magisk-specific overlay nodes (`/magisk`, `/overlay/magisk`)
3. **Vbmeta**: AVB flags restored to stock state (`avb.version=1.0`, `hash_algo=sha256`)
4. **Kernel cmdline**: No `androidboot.magisk=*` parameters
5. **Ramdisk hash**: Matches stock backup SHA256 (0-byte tolerance)

### Handoff Interface

The handoff between Firehose and MagiskCore happens at two points:

#### Point A: MagiskRemover reads boot image from device (Firehose → MagiskCore)
```
FirehoseClient.ReadPartition("boot") → byte[] bootImage
    ↓
BootImageParser.Parse(bootImage) → BootImageStructure
    ↓
MagiskArtifactDetector.Detect(BootImageStructure) → List<Artifact>
```

#### Point B: MagiskRemover flashes repacked image (MagiskCore → Firehose)
```
MagiskUninstaller.Remove(BootImageStructure) → BootImageStructure (clean)
    ↓
BootImageRepacker.Repack(BootImageStructure) → byte[] cleanImage
    ↓
FirehoseClient.ProgramPartition("boot", cleanImage) → FirehoseResponse
```

### MagiskCore Fix Dependencies

MagiskRemover depends on these MagiskCore components being functional:

| Component | File | Critical? | Failure Mode |
|-----------|------|-----------|--------------|
| BootImageParser | Parser/BootImageParser.cs (613 lines) | ✅ Yes | Cannot parse boot image → cannot detect Magisk |
| MagiskArtifactDetector | Services/MagiskArtifactDetector.cs (241 lines) | ✅ Yes | Cannot identify what Magisk modified |
| MagiskUninstaller | Services/MagiskUninstaller.cs (259 lines) | ✅ Yes | Cannot produce clean boot image |
| BootImageRepacker | Repacker/BootImageRepacker.cs (350 lines) | ✅ Yes | Cannot rebuild flashable image |
| CpioArchive | Services/CpioArchive.cs (324 lines) | ⚠️ Indirect | Ramdisk repack fails → boot image incomplete |
| CompressionEngine | Compression/CompressionEngine.cs (206 lines) | ⚠️ Indirect | Cannot decompress/recompress kernel/ramdisk |
| AvbRestorer | AvbRestorer/AvbRestorer.cs (195 lines) | ⚠️ Indirect | AVB footer not patched → device may reject boot |

### Failure Handling During Handoff

If any MagiskCore component fails during Step 4:
1. **BootImageParser fails** → Skip this partition, flag as "MagiskRemovalFailed", continue to next partition
2. **MagiskArtifactDetector finds nothing** → Skip removal, flag as "NoMagiskDetected" (may be false negative)
3. **MagiskUninstaller fails** → Fall back to flashing the backup boot image directly (bypass MagiskCore, raw flash)
4. **BootImageRepacker fails** → Cannot proceed, flag as "RepackFailed", try raw backup flash
5. **Flash verification fails** → Retry once, then flag as "FlashFailed"

If **Magisk removal verification fails** after retry:
1. Fall back to flashing stock boot image from backup (bypass MagiskCore)
2. Re-run verification on flashed stock image
3. If still fails → flag as "UninstallFailed", continue rescue (device may boot but rooted)
4. If verification passes → proceed normally

### Design Stick-Point

The ordering (restore→remove→verify) is **non-negotiable**. Reversing it (remove→restore) would mean:
- MagiskRemover analyzes boot images that may reference tampered system/vendor partitions
- Restoring system/vendor AFTER boot image fix could re-introduce Magisk artifacts
- Final verification would see a mix of cleaned and tampered partitions

**Evidence:** This ordering was validated on Samsung F966U1 where Magisk had modified both boot and init_boot partitions. Restoring system/vendor first revealed additional Magisk artifacts in init_boot that were invisible when boot was analyzed in isolation. On Samsung G998U, Magisk 26.0 left traces in `dtbo` overlay that survived initial boot image reflash — the DTBO check in verification caught this, preventing false "success" reports.

---

## Design Principles Summary

| Principle | Example | Enforced By |
|-----------|---------|-------------|
| **Verify > Trust** | SHA256 after every write | `PartitionRestorer`, `MagiskRemover` |
| **Forensic Evidence First** | Save original before modify | `PartitionRestorer`, `MagiskRemover` |
| **Never Restore Device-Unique** | Blocklist for sec/ddr/limits | `PartitionRestorer._neverRestore` |
| **Sequential USB Only** | No parallel Firehose commands | `FirehoseClient` design |
| **Derive From Firmware** | Partition names from .tar.md5 | `SamsungFirmwareExtractor` |
| **Dual Auth Fallback** | MD5 token + bearer token | `FirmwareSourcer.GetAuthTokenAsync` |
| **Pure C# No CLI** | SharpCompress, no shelling out | `CompressionEngine` |
| **Testable Interfaces** | IDeviceDetector, IDeviceTransport | `IDeviceDetector.cs`, `IDeviceTransport.cs` |
| **Flash-Then-Patch-Verify** | Restore partitions before Magisk removal, verify after | `RescueOrchestrator` phase ordering |

---

## Cross-References

- **Operations manual**: See `OPERATIONS.md` for Sahara handshake, Firehose XML commands, and USB transport deep dive
- **Failure modes**: See `FAILURES.md` for 15 documented failure modes with GPT validation mechanics
- **Build/deploy**: See `BUILD.md` for producing distributable EXE and offline NuGet mirror
- **MagiskCore architecture**: See `../OFFLINE_AGENT_GUIDE.md` for boot image parsing, repacking, and artifact detection
- **Health check**: See `../HEALTH_CHECK.md` for system validation commands