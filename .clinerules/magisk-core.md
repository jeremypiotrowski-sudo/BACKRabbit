---
paths:
  - "BACKRabbit.MagiskCore/**"
  - "BACKRabbit.Tests/**"
---
# MagiskCore Rules

- MagiskCore parses and repacks boot images; it does not perform device writes.
- Supported formats: AOSP v0-v4, Samsung PXA, MTK, DHTB, BLOB, vendor boot v3-v4.
- Round-trip tests must preserve kernel and ramdisk content (decompressed comparison for ramdisk).
- `BootImageParser.GetHeaderSize()` must return STRUCT SIZES for V0-V2 headers (V0=1632, V1=1660, V2=1660), NOT `page_size`. `page_size` is for alignment/padding only, not for header buffer allocation.
- `BootImageParser.ParseSections()` must use `GetPageSize()` to compute file offsets; section payloads start after the header is padded to `page_size`.
- `BootImageParser.ParseSections()` must add the prefix/base offset for DHTB/MTK/ChromeOS images.
- `GetPageSize()` must return 4096 as the default when the `page_size` field is 0 (prevents `PadTo` divide-by-zero on synthetic/malformed images).
- `PadTo()` must guard against divide-by-zero: if `alignment == 0`, return without writing padding.
- `BootImageRepacker` buffer size must be based on header struct size, not `page_size`.
- AVB footer detection must not underflow on small images.
