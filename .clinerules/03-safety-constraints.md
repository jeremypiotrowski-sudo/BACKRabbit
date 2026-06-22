# Safety Constraints — Always Active

## Dry-Run Gate

Every destructive phase must hard-check `if (_dryRun)` and log the action without executing it. ZERO bypass. This applies to:
- `PartitionRestorer` write/erase operations
- `MagiskRemover` boot partition writes
- `--wipe-data` userdata erase

## Stock-Only Write

`VerifyStockIntegrity` must check the manifest.json SHA256 hash BEFORE any write. If stock integrity fails, the write is refused.

## Blocklist

These partitions are never written without `--force`:
- `sec` — QFuse data (permanent, read-only)
- `ddr` — DDR training data (device-specific calibration)
- `limits` — Hardware limits
- `apdp`, `msadp` — Debug policy

## Wipe-Data Gates

`--wipe-data` has three gates:
1. Flag must be explicitly set
2. Typed confirmation required
3. Dry-run override allowed but logged

## Sector Address Convention

- `FullGptAuditResult` sector addresses are BYTE OFFSETS.
- Convert to sector numbers (`addr / 512`) before calling `WritePartitionBlocksAsync`.
- `startSector` parameter is a SECTOR NUMBER, not a byte offset.