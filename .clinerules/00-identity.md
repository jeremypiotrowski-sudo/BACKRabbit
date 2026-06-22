# BACKRabbit — Project Identity

> **Primary purpose:** NAND normalization engine for Samsung Android devices.
> **Philosophy:** Verify > Trust | Evidence > Claims | Audit > Assertion

## What BACKRabbit Actually Is

BACKRabbit is a NAND normalization engine for Samsung Android devices. It uses Qualcomm EDL/Firehose protocol to read, compare, write, and erase every partition on a device at the block level — below Odin, below Samsung's software layers, directly at the silicon.

The rescue pipeline verifies the device against a known-good stock firmware backup, identifies every byte that differs, and repairs only what is necessary.

## Purpose Hierarchy

1. **PRIMARY:** NAND normalization — make every byte on the device match factory state and prove it.
2. **SECONDARY:** Sparse repair — write ONLY the blocks that differ from stock.
3. **TERTIARY:** Magisk removal — happens automatically when boot/init_boot/vbmeta partitions are normalized to stock.

Magisk removal is a side-effect of clean boot partition restoration, NOT the primary purpose. Do not treat the rescue pipeline as a Magisk-removal wrapper.

## Solution Map (12 Projects)

| # | Project | Purpose |
|---|---------|---------|
| 1 | `BACKRabbit.CLI` | Command-line interface |
| 2 | `BACKRabbit.Core` | Shared utilities, compression |
| 3 | `BACKRabbit.Usb` | USB device enumeration/communication |
| 4 | `BACKRabbit.Protocol.Adb` | Pure C# ADB wire protocol (no adb.exe) |
| 5 | `BACKRabbit.Protocol.Fastboot` | Pure C# Fastboot protocol |
| 6 | `BACKRabbit.Protocol.DownloadMode` | Samsung Odin/Download Mode |
| 7 | `BACKRabbit.Protocol.Firehose` | Qualcomm EDL/Firehose protocol |
| 8 | `BACKRabbit.MagiskCore` | Boot image parse/repack, artifact detection |
| 9 | `BACKRabbit.Firmware` | Samsung firmware import/extraction |
| 10 | `BACKRabbit.Tests` | Integration/unit tests |
| 11 | `BACKRabbit.Protocol.Firehose.Tests` | Firehose/rescue tests |
| 12 | `SamsungMagiskCleaner` | Python alternative |

## Architecture Layers

```
BACKRabbit.CLI
  └── Commands: firehose, firmware, magisk, adb, fastboot, flash, detect
Protocol Layer
  ├── BACKRabbit.Protocol.Adb
  ├── BACKRabbit.Protocol.Fastboot
  ├── BACKRabbit.Protocol.DownloadMode
  └── BACKRabbit.Protocol.Firehose
      └── Rescue/ (orchestrator)
BACKRabbit.MagiskCore
BACKRabbit.Firmware
BACKRabbit.Usb
BACKRabbit.Core
```

## Critical Constraints

- `adb` and `fastboot` are NOT on PATH. Use `AdbClient` and `FastbootClient` classes directly.
- Samsung FUS API is dead (HTTP 403). Use `firmware import` with pre-downloaded ZIPs.
- Firehose loader `.elf` files go in `Loaders/` with naming `{MSM_ID}_{PK_HASH}_{description}.elf`.
- The rescue flow needs two external files: a Firehose loader and a stock firmware ZIP.