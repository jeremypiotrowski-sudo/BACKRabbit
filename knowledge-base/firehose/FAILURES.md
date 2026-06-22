# BACKRabbit Firehose — Failure Modes & Recovery

> **Purpose**: Document exactly what breaks and how to fix it — based on code analysis.
> **Generated**: 2026-06-21 | **Commit**: `9d328fe`

---

## Failure Mode Table

| Symptom | Code Location | Root Cause | Recovery |
|---------|--------------|------------|----------|
| `SAHARA_ERROR_INVALID_RESPONSE` | `SaharaChipInfo.cs:FromHelloRequest()` | Wrong USB endpoint or device not in EDL | 1. Verify PID=9008/900E/901D<br>2. Re-enumerate USB<br>3. Check WinUsbTransport constructor (VID/PID vs COM port) |
| `USB_ERROR_PIPE` on WRITE | `FirehoseClient.cs:WritePartitionAsync()` | Partition not found in GPT | 1. Run Phase 1 diagnosis<br>2. Check `ExtractedPartitions` matches device GPT<br>3. Verify LUN number (0 for most devices) |
| `FirehoseException: Configure failed` | `FirehoseClient.cs:ConfigureAsync()` | Wrong MemoryName in config | 1. Check storage type via `GetStorageInfoAsync()`<br>2. Set `MemoryName` to "ufs", "emmc", or "nand"<br>3. Default is "ufs" — override if device uses eMMC |
| `FirehoseException: Read partition failed` | `FirehoseClient.cs:ReadPartitionAsync()` | Partition size mismatch or read timeout | 1. Verify GPT entry exists<br>2. Check `start_sector` and `num_partition_sectors`<br>3. Increase timeout if large partition (>1GB) |
| Silent firmware corruption | `FirmwareSourcer.cs:DecryptEnc4()` | Wrong AES key (model:region format) | 1. Verify IMEI was provided for auth<br>2. Check FUS response for `binaryLogicValue`<br>3. Verify decrypted data starts with PK ZIP header (0x50 0x4B) |
| QFuse audit failure (Phase 2) | `RescueOrchestrator.cs` → `QFuseAuditor.AuditAsync()` | Bootloader version mismatch | 1. Flash compatible BL first<br>2. **Never** force-flash newer BL on older QFuse config<br>3. Some fuse addresses are SoC-specific — verify with `--soc` override |
| `FirmwareSourceException: FUS auth failed (403)` | `FirmwareSourcer.cs:QueryFusAsync()` | Invalid model/region or missing IMEI | 1. Verify model starts with "SM-"<br>2. CSC must be 3 chars (XAA, EUX, KOO)<br>3. Provide IMEI via `--imei` or TUI prompt<br>4. Token cache cleared on 403 — retry with corrected values |
| `FirmwareSourceException: No ZIP header` | `FirmwareSourcer.cs:DecryptEnc4()` | Decryption produced invalid data | 1. Model/region combination may be incorrect<br>2. Samsung may have changed encryption scheme<br>3. Try alternate key derivation (SHA256 instead of MD5) |
| `InvalidDataException: No valid boot image header` | `BootImageParser.cs:Parse()` | Corrupted boot image or unsupported format | 1. Verify image has "ANDROID!" or "VNDRBOOT" magic<br>2. Check for DHTB/BLOB/ChromeOS headers<br>3. Image may be encrypted or sparse format |
| `ArgumentException: Destination array not long enough` | `BootImageRepacker.cs:WriteV0Header()` | Header buffer too small for V0-V2 images | **FIXED in d3daa48**: `GetHeaderSize()` now returns `GetPageSize(img)` with 4096 fallback |
| `DivideByZeroException` in `Align()` | `BootImageParser.cs:Align()` | `pageSize=0` in synthetic test images | **FIXED in d3daa48**: Guard `if (alignment == 0) return value;` |
| `ArgumentOutOfRangeException` in DTB write | `BootImageRepacker.cs:Repack()` line 93 | `DtbOffset` set to memory address instead of file offset | **FIXED in 9d328fe**: `DtbOffset = offset` (file position) |
| TUI hangs on device detection | `FirmwareTui.cs:DetectDeviceAsync()` | No EDL device connected | 1. TUI falls back to manual entry<br>2. User can enter model/region manually<br>3. Press 'E' to edit, 'S' to skip |
| Rescue orchestrator warns "No backup directory" | `RescueOrchestrator.cs:RunFullRescueAsync()` Phase 0 | `--backup` not provided and TUI skipped | 1. Provide `--backup <dir>` with .img files<br>2. Use `--model` and `--region` for auto-sourcing<br>3. Run `firmware source` separately first |
| Magisk still detected after removal | `MagiskRemover.cs:RemoveFromSlotAsync()` | Surgical removal incomplete or vbmeta not restored | 1. Check if clean ramdisk backup exists<br>2. Verify vbmeta was restored (disables verification)<br>3. Re-run with `--slot both` to clean both slots |

---

## Recovery Procedures

### 1. Device Won't Enter EDL Mode

**Symptoms**: `FirehoseDeviceDetector.EnumerateDevices()` returns empty list.

**Recovery**:
1. Power off device completely (hold Power + Vol Down for 10 seconds)
2. Use EDL cable or test points (device-specific)
3. Check USB VID/PID: should be 05C6:9008, 05C6:900E, or 05C6:901D
4. On Windows: check Device Manager for "Qualcomm HS-USB QDLoader 9008"
5. Install Qualcomm USB drivers if device shows as "Unknown Device"

### 2. Firehose Programmer Won't Load

**Symptoms**: Sahara handshake completes but `ImageUploadComplete` never reached.

**Recovery**:
1. Verify programmer ELF matches device MSM ID (`LoaderDatabase.cs`)
2. Check PK hash matches (fused devices require signed programmers)
3. Try alternative programmer from `Loaders/` directory
4. Some devices require specific memory type in configure (try "emmc" instead of "ufs")

### 3. Partition Write Fails Verification

**Symptoms**: `RestoreAction.Verified = false`, SHA256 mismatch.

**Recovery**:
1. Re-read partition and compare again (transient USB errors)
2. Check backup .img file size matches GPT partition size (±10%)
3. Erase partition before re-writing (some devices require explicit erase)
4. If persistent: backup image may be corrupted — re-download firmware

### 4. FUS Download Fails Mid-Stream

**Symptoms**: Partial .enc4 file, network error.

**Recovery**:
1. Delete partial .enc4 and temp files
2. Retry — FUS token is cached for 25 minutes
3. If 403 on retry: token expired, re-auth with IMEI
4. Use `--skip-tui` with explicit `--model` and `--region` for scripted retry

### 5. Rescue Orchestrator Phase 3 Restores Wrong Partitions

**Symptoms**: Partitions marked "Tampered" but backup doesn't match.

**Recovery**:
1. Re-run Phase 1 diagnosis to refresh tampered list
2. Manually specify partitions with `--partitions` flag
3. Check backup directory contains correct .img files for device model
4. Verify backup was sourced from same model/region firmware

---

## Error Code Reference

### Sahara Errors (SaharaCommand.cs)

| Code | Name | Meaning |
|------|------|---------|
| 0x00 | SUCCESS | Operation completed |
| 0x01 | INVALID_CMD | Unknown command received |
| 0x02 | INVALID_PARAM | Parameter out of range |
| 0x03 | INVALID_LENGTH | Data length mismatch |
| 0x04 | INVALID_ADDR | Address out of range |
| 0x05 | IMAGE_TOO_LARGE | Programmer ELF exceeds device memory |
| 0x06 | IMAGE_VERIFY_FAIL | ELF signature/hash mismatch |
| 0x07 | READ_DATA_FAIL | Device couldn't read transmitted data |
| 0x08 | UNSUPPORTED_VERSION | Protocol version mismatch |
| 0x09 | AUTH_FAIL | PK hash mismatch (fused device) |

### Firehose Response Codes

| Value | Meaning |
|-------|---------|
| `ACK` | Command succeeded |
| `NAK` | Command failed (check log for reason) |
| `RAW` | Raw data follows (after read command) |
| `LOG` | Informational log message |

---

## Prevention Checklist

Before starting any rescue operation:
- [ ] Device confirmed in EDL mode (PID=9008/900E/901D)
- [ ] Firehose programmer matches device MSM ID
- [ ] Backup directory contains required .img files (or TUI will source them)
- [ ] IMEI available if FUS sourcing needed
- [ ] USB cable is direct (no hub) for reliability
- [ ] `dotnet test` passes for Firehose test suite (32 tests)

---

## GPT Validation Mechanics

### Comparison Method

`PartitionDiagnostics` performs byte-for-byte comparison between:
1. **Device GPT** — Read from the device via Firehose `read` command (raw GPT header + partition entry array from LBA 1-N)
2. **Reference GPT** — Extracted from the backup firmware's partition table (either from a full dump or from the firmware package's partition.xml/gpt_both0.bin)

### Validation Rules

#### Rule 1: Header Signature Match (Critical — Abort on Mismatch)
- GPT header bytes 0-7 must equal "EFI PART" (0x45 0x46 0x49 0x20 0x50 0x41 0x52 0x54)
- If device GPT lacks this signature → **Critical Abort**: device partition table is corrupted beyond repair
- If reference GPT lacks this signature → **Warning**: backup may be invalid, continue with caution

#### Rule 2: Header CRC32 Match (Critical — Abort on Mismatch)
- GPT header CRC32 (bytes 16-19) must match computed CRC32 of header bytes 0-91
- Mismatch on device → **Critical Abort**: GPT header corrupted
- Mismatch on reference → **Warning**: backup GPT may be damaged

#### Rule 3: Partition Entry Array CRC32 (Warning Only)
- Partition entry array CRC32 must match expected value
- Mismatch → **Warning**: entries may be corrupted, but individual partition validation can still proceed

#### Rule 4: Partition Count Match (Warning Only)
- Number of partition entries in device GPT vs reference GPT
- Mismatch → **Warning**: partition layout has changed (could be legitimate OTA update or tampering)

#### Rule 5: Per-Partition Byte-for-Byte Comparison (Tolerance: 0 bytes)
- For each partition listed in the reference GPT:
  - Read partition content from device via Firehose `read`
  - Compare SHA256 against reference backup SHA256
  - **Tolerance: 0 bytes** — any difference flags the partition as "Tampered"
  - Exception: empty/unused partitions (all 0xFF or all 0x00) are skipped

#### Rule 6: Critical Partition Abort Logic
These partitions, if tampered, trigger an **immediate abort** of the rescue:
- `partition` (partition table itself) — cannot flash without valid partition table
- `xbl` / `xbl_a` — primary bootloader, corruption = brick
- `abl` / `abl_a` — Android bootloader, corruption = brick
- `devcfg` — device configuration fuses
- `rpm` — resource power manager firmware

These partitions, if tampered, are **flagged but rescue continues**:
- `boot` / `boot_a` — recoverable via reflash
- `init_boot` / `init_boot_a` — recoverable via reflash
- `vbmeta` / `vbmeta_a` — recoverable via reflash with AVB disable flags
- `recovery` / `recovery_a` — recoverable via reflash
- `dtbo` / `dtbo_a` — recoverable via reflash

### Tolerance Thresholds Table

| Check | Tolerance | Action on Fail |
|-------|-----------|----------------|
| GPT signature ("EFI PART") | Exact match | Critical abort |
| GPT header CRC32 | Exact match | Critical abort |
| Partition entry CRC32 | Exact match | Warning, continue |
| Partition count | Exact match | Warning, continue |
| Per-partition SHA256 | Exact match | Flag "Tampered" |
| Boot image header magic | Exact match ("ANDROID!") | Flag "Tampered" |
| AVB footer magic | Exact match ("AVBf") | Flag "Tampered" |
| Empty partition (all 0xFF/0x00) | N/A | Skip |
| Magisk artifact in ramdisk | Any presence | Flag "MagiskDetected" |

### Post-Restore Verification

After `PartitionRestorer` flashes a partition:
1. Read back the flashed partition from device
2. Compute SHA256
3. Compare against reference backup SHA256
4. If mismatch → retry flash once (max 1 retry)
5. If still mismatch → flag as "RestoreFailed" in RescueReport
6. If match → flag as "Restored" with Verified=true

### Magisk-Specific Tampering Patterns

When validating partitions during Magisk uninstall prep, watch for:

| Partition | Magisk Tampering Indicator | Action |
|-----------|----------------------------|--------|
| `boot`/`boot_a` | Modified `init.rc` with `magiskinit` calls | Flag for Magisk removal |
| `dtbo`/`dtbo_a` | Extra overlay nodes (`/magisk`, `/overlay/magisk`) | Flag for reflash |
| `vbmeta`/`vbmeta_a` | Disabled AVB flags (`androidboot.veritymode=enforcing` missing) | Critical — requires reflash before boot analysis |
| `system`/`system_a` | MagiskHide props (`ro.magisk.hide`, `ro.debuggable=1`) | Log only (handled in step 4) |
| `vendor`/`vendor_a` | Magisk module traces in `/vendor/lib/modules` | Log only |

After flashing boot partition during Magisk removal:
1. Verify no Magisk artifacts remain in ramdisk
2. Check `init.rc` for clean stock syntax
3. Confirm kernel cmdline lacks `androidboot.magisk=*`

---

## Cross-References

- **Operations manual**: See `OPERATIONS.md` for Sahara handshake, Firehose XML commands, and USB transport layer
- **Design rationale**: See `DESIGN.md` for why forensic evidence is saved before modification (Design Decision 5)
- **MagiskCore debugging**: See `../OFFLINE_AGENT_GUIDE.md` for 5 common debugging scenarios with exact line references
- **Health check**: See `../HEALTH_CHECK.md` for red flags and validation commands