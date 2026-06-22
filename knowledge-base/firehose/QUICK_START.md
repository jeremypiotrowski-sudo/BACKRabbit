# BACKRabbit Firehose — Emergency Rescue Quick Start

> **For when you need to act fast.** Full docs are in `OPERATIONS.md`, `FAILURES.md`, `DESIGN.md`.  
> **One rule above all**: Dry-run first. Always.

---

## Step 1: Detect Your Device

```powershell
backrabbit firehose detect
```

If nothing shows: put device in EDL mode (VolUp+VolDown+USB cable, or `adb reboot edl`).  
Look for VID=05C6, PID=9008/900E/901D.

---

## Step 2: Source Firmware (If You Don't Have a Backup)

**Option A — Interactive TUI (recommended):**
```powershell
backrabbit firehose rescue full --device COM3 --loader firehose.elf --model SM-F966U1 --region XAA
```
This launches the firmware sourcing wizard. Follow the prompts.

**Option B — Skip TUI (if you know your model/region):**
```powershell
backrabbit firehose rescue full --device COM3 --loader firehose.elf --model SM-F966U1 --region XAA --skip-tui
```

**Option C — Use existing backup:**
```powershell
backrabbit firehose rescue full --device COM3 --loader firehose.elf --backup ./stock-backup
```

---

## Step 3: DRY-RUN FIRST (Never Skip This)

```powershell
backrabbit firehose rescue full --device COM3 --loader firehose.elf --backup ./stock-backup --dry-run
```

**What happens**: Reads all partitions, compares against backup, audits QFuses, detects Magisk — but writes NOTHING.

**Verify the output:**
- `🔥 FIREHOSE DRY-RUN MODE ENABLED` banner at start
- `🔥 DRY-RUN COMPLETE — No partitions were modified.` at end
- `"IsDryRun": true` in rescue-report.json
- Zero `Erased`/`Flashed` in logs

**If dry-run shows `Verdict: Clean`** → no rescue needed. You're done.

**If dry-run shows `Verdict: Tampered`** → proceed to Step 4.

---

## Step 4: Run Live Rescue

```powershell
backrabbit firehose rescue full --device COM3 --loader firehose.elf --backup ./stock-backup
```

**What happens**: Same diagnosis → then flashes tampered partitions from backup → removes Magisk → re-verifies → resets device.

**Critical log lines to confirm success:**
```
[PartitionRestorer] POST-FLASH VERIFY: SHA256 MATCH
[RescueOrchestrator] Post-rescue verdict: FullyRecovered (or PartiallyRecovered)
```

---

## Step 5: Interpret the Report

The rescue report is saved as `rescue-report.json` in your backup directory (or `%TEMP%\BACKRabbit\reports\`).

| Verdict | Meaning | Action |
|---------|---------|--------|
| `Clean` | No tampering detected | No action needed |
| `FullyRecovered` | All tampered partitions restored, no permanent damage | Device is clean |
| `PartiallyRecovered` | Some partitions restored, but QFuses blown (permanent) | Device is functional but has permanent security state changes |
| `PermanentDamage` | Tampered partitions remain + QFuses blown | Review report for specific failures |
| `Tampered` | Tampering detected (dry-run only — no writes performed) | Proceed to live rescue |

---

## Emergency: If Something Goes Wrong

| Symptom | Recovery |
|---------|----------|
| Stuck in Download Mode | `heimdall reboot-download` → flash stock firmware via Odin |
| Bootloop after rescue | Reboot to Recovery → restore TWRP backup |
| USB not recognized | Reinstall Samsung drivers, try different cable/port |
| Sahara handshake hangs | Power cycle device, verify Download Mode in Odin |

---

## Key Commands Cheat Sheet

```powershell
# Detection & info
backrabbit firehose detect                          # List EDL devices
backrabbit firehose info --device COM3              # Chip info
backrabbit firehose printgpt --device COM3 --loader firehose.elf  # Partition table

# Read/write single partitions
backrabbit firehose dump --device COM3 --loader firehose.elf --partition boot_a --output boot_a.img
backrabbit firehose flash --device COM3 --loader firehose.elf --partition boot_a --input stock_boot.img

# Rescue sub-commands
backrabbit firehose rescue diagnose --device COM3 --loader firehose.elf --backup ./stock
backrabbit firehose rescue fuses --device COM3 --loader firehose.elf
backrabbit firehose rescue unmagisk --device COM3 --loader firehose.elf --backup ./stock
backrabbit firehose rescue restore --device COM3 --loader firehose.elf --backup ./stock --partitions boot_a,vbmeta_a

# Full rescue (the one you want)
backrabbit firehose rescue full --device COM3 --loader firehose.elf --backup ./stock --dry-run   # ALWAYS FIRST
backrabbit firehose rescue full --device COM3 --loader firehose.elf --backup ./stock             # Live

# Force override blocklist (DANGER — requires typed confirmation)
backrabbit firehose rescue full --device COM3 --loader firehose.elf --backup ./stock --force
```

---

## Blocklist: Partitions Never Restored (Unless --force)

These are device-unique and flashing them causes permanent damage:

| Partition | Why Blocked |
|-----------|-------------|
| `sec` | QFuse data — read-only, touching is dangerous |
| `ddr` | DDR training data — device-specific calibration |
| `limits` | Hardware limits — device-specific |
| `apdp` | AP debug policy — device-specific |
| `msadp` | Modem debug policy — device-specific |

To override: add `--force` and type "I understand the risks" when prompted.

---

## Pre-Flight Checklist

Before connecting a fused device:

- [ ] Stock backup created and SHA256 hashes recorded
- [ ] OEM Unlock enabled + USB Debugging on
- [ ] Device boots to Android normally
- [ ] TWRP/Odin recovery options ready
- [ ] Dry-run completed and passed validation
- [ ] Emergency recovery kit prepared (Odin, stock firmware, drivers)

---

*This card is a panic-mode distillation. For deep dives: `OPERATIONS.md` (protocol), `FAILURES.md` (15 failure modes), `DESIGN.md` (architecture decisions), `TESTING_PROTOCOL.md` (zero-brick validation).*