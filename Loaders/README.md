# Firehose Loaders Directory

Place your Qualcomm Firehose programmer files here.

## Naming Convention

BACKRabbit auto-detects loaders by filename pattern:
```
{MSM_ID}_{PK_HASH}_{description}.elf
```

Example: `008600E1_a1b2c3d4e5f6a7b8_sdm845.elf`

## Where to Find Loaders

### For Z Fold 7 (Snapdragon 8 Gen 3 for Galaxy — SM8650-AC)

| Source | Search Terms |
|--------|-------------|
| **Alephgsm Telegram** | `@Alephgsm` — search `SM8650 firehose` or `Z Fold 6 firehose` |
| **XDA Developers** | `firehose SM8650 programmer elf` |
| **GSM-Forum** | `prog_firehose_ddr SM8650` |
| **Samsung COMBINATION firmware** | `COMBINATION_FAC_SM-F956U` — contains firehose inside AP tar |

### Known MSM IDs for Reference

| Chipset | MSM ID | Devices |
|---------|--------|---------|
| SDM845 | 0x008600E1 | Galaxy S9, Note 9 |
| SM8550 | 0x008700E1 | Galaxy S23, Z Fold 5 |
| SM8650 | 0x008800E1 | Galaxy S24, Z Fold 6, Z Fold 7 |
| SM8750 | TBD | Galaxy S25, Z Fold 7 (late 2025) |

## Verify Your Device's MSM ID

Run this command with your phone in EDL mode:
```
backrabbit firehose info --device COM3
```

This will print the MSM ID and PK Hash you need to match against loader files.