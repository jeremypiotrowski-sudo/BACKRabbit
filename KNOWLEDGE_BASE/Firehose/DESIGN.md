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