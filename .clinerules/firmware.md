---
paths:
  - "BACKRabbit.Firmware/**"
---
# Firmware Rules

- Samsung FUS API is dead (`fota-cloud-dn.ospserver.net` returns HTTP 403). Do not suggest `firmware source`.
- Use `firmware import` with pre-downloaded stock firmware ZIPs.
- `FirmwareImporter` extracts .tar.md5 → .img and generates `manifest.json` with SHA256 hashes.
- Stock integrity verification compares manifest SHA256 before any write.