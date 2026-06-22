# BACKRabbit Firehose — Pre-Flight Checklist for Live-Device Testing

> **Generated**: 2026-06-22 | **Commit**: `4cda7bd`  
> **Purpose**: Step-by-step validation before connecting a fused Samsung device in EDL mode.  
> **Rule**: Every step must pass before proceeding to the next. If any step fails, STOP and resolve.

---

## Prerequisites

- [ ] Samsung device with Snapdragon chipset (S25/Z Fold 7 = SM8750, S24 Ultra = SM8650)
- [ ] Original Samsung USB-C cable (data-capable, not charge-only)
- [ ] USB 3.0+ port directly on the PC (no hubs, no docks)
- [ ] Firehose loader ELF in `Loaders/` directory
- [ ] Stock firmware backup directory with `boot_a.img`, `vbmeta_a.img`, etc.
- [ ] OEM Unlock enabled on device (Settings → Developer Options)
- [ ] USB Debugging enabled on device
- [ ] Device fully charged (>50% battery)

---

## Phase 0: Build & Test Verification

| Step | Command | Success Looks Like | Failure Looks Like | On Failure |
|------|---------|-------------------|-------------------|------------|
| 0.1 | `dotnet build -c Release` | `Build succeeded. 0 Error(s)` | Any error | Fix build errors before proceeding |
| 0.2 | `dotnet test BACKRabbit.Protocol.Firehose.Tests -c Release` | `Passed: 38, Failed: 1` (1 pre-existing) | New test failures | Investigate and fix new failures |
| 0.3 | Verify SM8750 loader exists | `dir Loaders\001920E100200000_4A14C27B518909E1_fhprg.elf` | File found, non-zero size | File missing or 0 bytes | Source loader from bkerler/Loaders repo |

---

## Phase 1: Device Detection (No Flashing)

| Step | Command | Success Looks Like | Failure Looks Like | On Failure |
|------|---------|-------------------|-------------------|------------|
| 1.1 | Enter EDL mode | Power off → hold VolUp+VolDown → plug USB | Device Manager shows "Qualcomm HS-USB QDLoader 9008" | No device appears | Try different USB port/cable; ensure device is fully powered off |
| 1.2 | Verify USB enumeration | `backrabbit firehose detect` | Lists device on COM port with VID 05C6, PID 9008 | "No EDL devices found" | Check USB drivers (Zadig/WinUSB); verify cable is data-capable |
| 1.3 | Verify COM port | `backrabbit firehose detect` | Shows COM port number (e.g., COM3) | No COM port listed | Install Qualcomm USB drivers; check Device Manager for driver errors |

---

## Phase 2: Sahara Handshake (Read-Only)

| Step | Command | Success Looks Like | Failure Looks Like | On Failure |
|------|---------|-------------------|-------------------|------------|
| 2.1 | Test Firehose alive | `backrabbit firehose nop --device COM3 --loader Loaders/001920E100200000_4A14C27B518909E1_fhprg.elf` | "NOP: ACK" or "Firehose alive" | Timeout or "NAK" | Power cycle device; verify loader matches device MSM ID |
| 2.2 | Get chip info | `backrabbit firehose info --device COM3` | Shows MSM ID, PK Hash, Serial Number | "ChipInfo not available" | Sahara handshake failed — check loader compatibility |
| 2.3 | Get storage info | `backrabbit firehose storageinfo --device COM3 --loader <ELF>` | "ufs" or "emmc" | Error or timeout | Device may not be in proper EDL state; retry from power-off |

---

## Phase 3: GPT Read-Only Dump (No Flashing)

| Step | Command | Success Looks Like | Failure Looks Like | On Failure |
|------|---------|-------------------|-------------------|------------|
| 3.1 | Dump GPT | `backrabbit firehose printgpt --device COM3 --loader <ELF>` | Lists all partitions with names and sizes | Empty list or error | Device may have locked storage; check if device is fused |
| 3.2 | Verify boot partitions exist | Look for `boot_a`, `boot_b`, `vbmeta_a`, `vbmeta_b` in GPT output | All 4 partitions listed | Missing partitions | Device may use single-slot (non-A/B) — adjust backup expectations |
| 3.3 | Read boot_a (test read) | `backrabbit firehose dump --device COM3 --loader <ELF> --partition boot_a --output test_boot_a.img` | File created, non-zero size | Error or 0-byte file | Partition may be locked; check QFuse status |

---

## Phase 4: Dry-Run Rescue (Zero Writes)

| Step | Command | Success Looks Like | Failure Looks Like | On Failure |
|------|---------|-------------------|-------------------|------------|
| 4.1 | Run dry-run | `backrabbit firehose rescue full --device COM3 --loader <ELF> --backup ./stock-backup --dry-run --skip-dl-mode-check` | "DRY-RUN COMPLETE — No partitions were modified" | "Aborted" or crash | Check backup directory has required .img files |
| 4.2 | Verify report | Check `rescue-report.json` in backup dir | `IsDryRun: true`, all actions `DryRunSkipped` | Missing report or `IsDryRun: false` | Dry-run flag not propagated — check CLI argument parsing |
| 4.3 | Verify no writes | Grep console output for "Erased\|Flashed\|program\|erase" | **ZERO matches** | Any match found | **CRITICAL: DO NOT PROCEED** — dry-run safety is compromised |
| 4.4 | Review diagnosis | Read partition statuses in report | Shows which partitions are Tampered/Normal | All "Unknown" | GPT read may have failed; retry from Phase 3 |

---

## Phase 5: Live Rescue (FLASHES DEVICE)

### ⚠️ DO NOT PROCEED UNLESS ALL PHASES 0-4 PASSED

| Step | Command | Success Looks Like | Failure Looks Like | On Failure |
|------|---------|-------------------|-------------------|------------|
| 5.1 | Run live rescue | `backrabbit firehose rescue full --device COM3 --loader <ELF> --backup ./stock-backup --skip-dl-mode-check` | "FullyRecovered" or "PartiallyRecovered" verdict | "Aborted" or "PermanentDamage" | Review report for specific failures |
| 5.2 | Verify post-rescue | Check `rescue-report.json` | Tampered partitions now "Normal (restored)" | Partitions still "Tampered" | Flash may have failed — check USB connection, retry |
| 5.3 | Reboot device | Device should reboot automatically | Boots to Android normally | Bootloop or Download Mode | Restore via Odin with stock firmware |

---

## DO NOT PROCEED IF

- [ ] Any build error in `dotnet build -c Release`
- [ ] Any NEW test failure in Firehose test suite (1 pre-existing failure is acceptable)
- [ ] SM8750 loader file is missing or 0 bytes
- [ ] Device does not enumerate as Qualcomm QDLoader 9008 (VID 05C6, PID 9008)
- [ ] Sahara handshake fails (NOP returns NAK or timeout)
- [ ] GPT dump returns empty or errors
- [ ] Dry-run shows ANY write/erase/program calls in console output
- [ ] Dry-run report does not have `IsDryRun: true`
- [ ] Backup directory is missing `boot_a.img` or `vbmeta_a.img`
- [ ] Device battery is below 30%
- [ ] USB cable is not the original Samsung cable or a known-good data cable

---

## Emergency Recovery

If the device is stuck in EDL mode or bootloops after rescue:

1. **Force power off**: Hold Power + VolDown for 15+ seconds
2. **Enter Download Mode**: Hold VolDown + VolUp + Power, plug USB
3. **Flash stock firmware via Odin**: Use the stock firmware ZIP you sourced earlier
4. **If Odin fails**: Try Heimdall (`heimdall flash --RECOVERY twrp.img`)

---

## Loader Coverage

| Chipset | Devices | Loader Available |
|---------|---------|-----------------|
| SM8750 (Snapdragon 8 Elite) | S25, Z Fold 7 | ✅ `001920E100200000_4A14C27B518909E1_fhprg.elf` (Samsung) |
| SM8650 (Snapdragon 8 Gen 3) | S24 Ultra | ❌ Not in Loaders/ — source from XDA/Telegram |
| SM8550 (Snapdragon 8 Gen 2) | S23 Ultra | ❌ Not in Loaders/ — source from XDA/Telegram |
| SDM845 | Various older | ✅ Multiple loaders available |

---

## USB Layer Audit

- **Qualcomm EDL detection**: ✅ VID 0x05C6, PIDs 0x9008/0x900E/0x901D correctly mapped
- **Samsung Download Mode**: ✅ VID 0x04E8, PIDs 0x685D/0x6860/0x6862/0x6864/0x6601
- **Endpoint mapping**: ✅ Bulk OUT 0x02, Bulk IN 0x81
- **LibUsbDotNet**: ⚠️ Targets .NET Framework 4.6.1, works on net8.0 with warning
- **COM port mapping**: ✅ Via WinUsbTransport with serial fallback

## Sahara Handshake Audit

- **State machine**: ✅ Full 7-state FSM with validated transitions
- **Error handling**: ✅ Any state can transition to Error; Error → Disconnected recovery
- **Timeout**: ✅ Via CancellationToken passed through all async methods
- **Unexpected state**: ✅ `IsValidTransition` returns false → throws `SaharaProtocolException`
- **Chip info extraction**: ✅ `SaharaChipInfo.FromHelloRequest()` parses MSM ID, PK Hash, Serial

---

*Generated by BACKRabbit Chunk 4 assessment. Ready for live-device testing with SM8750 devices.*