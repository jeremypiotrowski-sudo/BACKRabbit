# BACKRabbit Firehose — Zero-Brick Testing Protocol

> **Purpose**: Safe, phased validation of the Firehose rescue pipeline on live Samsung devices.
> **Generated**: 2026-06-21 | **Commit**: `5ec4f78`
> **Prerequisite**: P0 `--dry-run` flag implemented in `RescueOrchestrator.cs`

---

## The Bricking Risk Reality (Samsung/EDL Specific)

| Scenario | Actual Risk | Why Your Tool Mitigates It | Your Action |
|----------|-------------|----------------------------|-------------|
| **Flashing wrong partition** | ⚠️ Medium (if validation bypassed) | Tool uses **PartitionDiagnostics** to verify target partition via GPT SHA256 *before* flash | ✅ Never skip `PartitionRestorer`'s pre-flash read/verify |
| **Corrupting vbmeta/xbl** | ⚠️ High (triggers AVB rollback/brick) | Tool **aborts immediately** on critical partition tampering (Rule 6 in `FAILURES.md`) | ✅ Trust the "Critical Abort" — don't override |
| **USB interrupt during flash** | ⚠️ Low-Medium (recoverable) | Tool uses **per-partition retry logic** (max 1 retry) + verifies post-flash SHA256 | ✅ Monitor USB health metrics in logs |
| **Flashing incompatible firmware** | ⚠️ High (bootloop/brick) | Tool **only flashes from YOUR verified backup** — never downloads/modifies externally | ✅ **Critical**: Use only backups *you* created from known-good state |
| **Magisk removal failure** | ⚠️ Low (device may stay rooted but bootable) | Tool falls back to **stock boot flash** if verification fails (Design Decision 11 in `DESIGN.md`) | ✅ Verify `MagiskRemovalVerified` in logs before declaring success |

---

## Phase 0: Preparation (Do NOT Skip)

### 1. Create a FRESH backup of your test device *in stock state*

```bash
# In Download Mode (Odin):
heimdall flash --RECOVERY twrp.img --no-reboot
# Then in TWRP: Backup → System, Vendor, Boot, DTBO, Vbmeta (save to PC)
```

### 2. Verify backup integrity

```bash
# On your PC:
sha256sum boot.img dtbo.img vbmeta.img system.img vendor.img
# Save these hashes — your tool will compare against them
```

### 3. Enable OEM Unlock (critical for Samsung)

- Settings → Developer Options → **OEM Unlocking ON** + **USB Debugging ON**
- Reboot to Download Mode: `adb reboot download` (or VolDown+Home+Power)

---

## Phase 1: Zero-Risk Dry Runs (No Flashing)

**Command:**
```powershell
backrabbit firehose rescue full --device <PORT> --loader <ELF> --backup ./stock-backup --dry-run
```

**What runs:**
- ✅ Phase 0: Backup directory verification
- ✅ Phase 1: Full partition diagnosis (reads partitions, compares SHA256 against backup)
- ✅ Phase 2: QFuse audit (reads fuse registers)
- ❌ Phase 3: Restore — **SKIPPED** (logged as `[DRY-RUN] Would restore...`)
- ❌ Phase 4: Magisk removal — **SKIPPED** (logged as `[DRY-RUN] Would parse → detect → clean → repack → flash → verify`)
- ❌ Phase 5: Final verification — **SKIPPED** (no writes were performed)
- ❌ Reset — **SKIPPED** (logged as `[DRY-RUN] Would reset device to system`)

**Success criteria:**
- GPT validation logs show correct partition sizes/hashes
- Magisk detection logs identify expected artifacts (or confirm clean state)
- USB transport metrics report >1.2MB/s read speed
- Report shows `IsDryRun: true`, all actions `DryRunSkipped`
- Tool completes without attempting any flash/erase operations

---

## Phase 2: Controlled Flash Testing (Stock Device First)

### Step 1: Flash only the boot partition from verified stock backup

Manually via Odin: `AP` = your stock `boot.img`. **Do NOT flash other partitions yet.**

### Step 2: Boot to Android → verify stock boot works

### Step 3: Run dry-run on stock device

```powershell
backrabbit firehose rescue full --device <PORT> --loader <ELF> --backup ./stock-backup --dry-run
```

**Expected output:**
```
🔥 FIREHOSE DRY-RUN MODE ENABLED — NO FLASHING WILL OCCUR
[1/7] Diagnosing partitions...
  Done. 13 partitions analyzed. Verdict: Clean
[2/7] Auditing QFuses...
  Done. 3/8 fuses blown.
[3/7] Restoring tampered partitions...
  No tampered partitions to restore.
[4/7] Removing Magisk...
  No Magisk detected in boot partitions.
[5/7] Final verification...
  [DRY-RUN] Skipping post-rescue verification (no writes were performed)
🔥 DRY-RUN COMPLETE — No partitions were modified.
```

**Success criteria:**
- `MAGISK_DETECTED: false` (or equivalent in logs)
- `RESCUE_SKIPPED: true` (no tampered partitions)
- Device boots normally, no changes made

---

## Phase 3: Magisk Injection Test (The Real Validation)

### Step 1: Install Magisk via TWRP (or Magisk Manager) — **only patch boot.img**

*Do not* enable MagiskHide or modules yet.

### Step 2: Verify device boots with Magisk

```bash
adb shell getprop | grep magisk
```

### Step 3: Run dry-run FIRST

```powershell
backrabbit firehose rescue full --device <PORT> --loader <ELF> --backup ./stock-backup --dry-run
```

**Expected dry-run output:**
```
🔥 FIREHOSE DRY-RUN MODE ENABLED — NO FLASHING WILL OCCUR
[1/7] Diagnosing partitions...
  Done. 13 partitions analyzed. Verdict: Tampered
[3/7] [DRY-RUN] Would restore 1 tampered partition(s): boot_a
    [DRY-RUN] Would flash boot_a from ./stock-backup/boot_a.img (96,468,992 bytes)
[4/7] [DRY-RUN] Magisk detected in 1 partition(s): boot_a
  [DRY-RUN] Would parse boot image → detect artifacts → clean ramdisk → repack → flash → verify
  [DRY-RUN] DRY-RUN PLAN: Flash boot_a, remove Magisk, verify
🔥 DRY-RUN COMPLETE — No partitions were modified.
```

### Step 4: If dry-run confirms detection → run live rescue

```powershell
backrabbit firehose rescue full --device <PORT> --loader <ELF> --backup ./stock-backup
```

**Critical log lines to see:**
```
[MagiskRemover] ARTIFACTS_FOUND: [magiskinit in init.rc]
[MagiskRemover] FLASHING boot from backup (SHA256: [your_backup_hash])
[PartitionRestorer] POST-FLASH VERIFY: SHA256 MATCH (0 retries)
[RescueOrchestrator] MAGISK_REMOVAL_VERIFIED: true
```

**Success criteria:**
- Device boots stock (no Magisk)
- All partitions unchanged except boot
- `MagiskRemovalResult.MagiskRemoved: true` in rescue report JSON

---

## Phase 4: Stress Test (DTBO/Critical Partitions)

### Step 1: Install Magisk + enable a module that modifies DTBO

(e.g., a custom kernel module)

### Step 2: Verify DTBO has `/magisk` nodes

```bash
# In TWRP advanced terminal:
dd if=/dev/block/dtbo | strings | grep -i magisk
```

### Step 3: Run dry-run FIRST

```powershell
backrabbit firehose rescue full --device <PORT> --loader <ELF> --backup ./stock-backup --dry-run
```

**Expected:**
- DTBO tampering detected (via GPT validation + SHA256 mismatch)
- Dry-run plan shows DTBO reflash *before* boot partition processing

### Step 4: Run live rescue

**Success criteria:**
- DTBO and boot both restored to stock hashes
- No `/magisk` nodes in DTBO after rescue
- Device boots clean

---

## Emergency Recovery Kit

If something goes wrong (extremely unlikely with dry-runs first):

| Symptom | Recovery Action |
|---------|-----------------|
| Stuck in Download Mode | `heimdall reboot-download` → try Odin flash stock firmware |
| Bootloop after flash | Reboot to Recovery → restore your **fresh stock backup** via TWRP |
| USB not recognized | Reinstall Samsung USB drivers → try different USB port/cable |
| Tool hangs at Sahara handshake | Power cycle device → verify in Download Mode (Odin shows "Added!") |

---

## Key Safety Mindset

- **"Cannot brick" is false** → **"Bricking is recoverable IF you have backups and know Download Mode"**
  - Your greatest protection: **Download Mode + Odin/Heimdall + your verified backups**
  - *Never* test without a fresh stock backup you can restore via TWRP/Odin
- **The tool's validation is your seatbelt** → Trust its aborts (they're there for a reason)
- **Start smaller than you think** → Validate USB transport *before* touching flash commands
- **Log everything** → Redirect output: `dotnet run > rescue_test_$(date +%s).log`

---

## Go/No-Go Checklist

Before connecting your device:

- [ ] Fresh stock backup created and verified (SHA256 hashes recorded)
- [ ] OEM Unlock enabled + USB Debugging on
- [ ] Device boots to Android normally
- [ ] You have TWRP/Odin recovery options ready
- [ ] Tool run in dry-run mode passed validation checks
- [ ] Emergency recovery kit (Odin stock firmware, drivers, backup) prepared

**If all boxes are checked → your risk of permanent brick is near-zero.** The worst case is a temporary bootloop fixed by restoring your backup via TWRP (which takes 2 minutes).

---

## Dry-Run Validation Checklist

After each dry-run execution, verify:

| Validation Step | Check | Expected |
|-----------------|-------|----------|
| 1. Dry-run banner | `grep "FIREHOSE DRY-RUN MODE" log.txt` | Present at start of output |
| 2. Diagnosis completed | `grep "partitions analyzed" log.txt` | `13 partitions analyzed` (or device-specific count) |
| 3. No flash operations | `grep "Erased\|Flashed\|WritePartition\|ErasePartition" log.txt` | **ZERO matches** |
| 4. All restore actions dry-run | `grep "RestoreActions" rescue-report.json` | All actions = `DryRunSkipped` |
| 5. Magisk removals not executed | `grep "MagiskRemoved" rescue-report.json` | All = `false` |
| 6. Dry-run completion banner | `grep "DRY-RUN COMPLETE" log.txt` | Present at end of output |
| 7. Report IsDryRun flag | `grep "IsDryRun" rescue-report.json` | `true` |
| 8. No device reset | `grep "Resetting device\|ResetAsync" log.txt` | Only `[DRY-RUN] Would reset` |
| 9. USB speed adequate | `grep "USB_READ_SPEED\|read speed" log.txt` | >1.2 MB/s (if implemented) |
| 10. Build still passes | `dotnet build BACKRabbit.CLI -c Release` | 0 errors |

---

## Code Gap Map

Which protocol steps are fully implemented vs. need work:

| Protocol Step | Status | Implementation |
|---------------|--------|----------------|
| Phase 0: Backup verification | ✅ Complete | `RescueOrchestrator.cs:27-44` |
| Phase 1: Dry-run diagnosis | ✅ Complete | P0 `--dry-run` flag, `RescueOrchestrator.cs` |
| Phase 1: USB health metrics | ⚠️ Partial | Speed logging not yet implemented (P2) |
| Phase 2: Controlled flash | ✅ Complete | `PartitionRestorer.cs` with SHA256 verify |
| Phase 3: Magisk detection | ✅ Complete | `MagiskArtifactDetector.cs` via `MagiskRemover.cs` |
| Phase 3: Magisk removal verification | ✅ Complete | `MagiskRemover.cs:160-172` inline verification |
| Phase 3: Structured log output | ⚠️ Partial | Prose-only, no machine-parseable tags (P2) |
| Phase 4: DTBO Magisk detection | ⚠️ Partial | SHA256 comparison works, but no Magisk-specific DTBO overlay scanning (P1) |
| Phase 4: Vbmeta restore | ✅ Complete | `MagiskRemover.cs:176-209` |
| Emergency recovery | ✅ Complete | TWRP/Odin/Heimdall external to tool |
| Blocklist enforcement | ✅ Complete | `PartitionRestorer.cs:17-24` `_neverRestore` |
| Forensic evidence save | ✅ Complete | `PartitionRestorer.cs:70-86`, `MagiskRemover.cs:144-149` |

---

## Cross-References

- **Operations manual**: See `OPERATIONS.md` for Sahara handshake, Firehose XML commands, and USB transport deep dive
- **Failure modes**: See `FAILURES.md` for 15 documented failure modes with GPT validation mechanics
- **Design rationale**: See `DESIGN.md` for Design Decision 11 (MagiskCore↔Firehose handoff) and flash-then-patch-verify ordering
- **Build/deploy**: See `BUILD.md` for producing distributable EXE
- **Health check**: See `../HEALTH_CHECK.md` for system validation commands
- **Testing guide**: See `../TESTING_GUIDE.md` for MagiskCore/ADB test modes and regression checklist