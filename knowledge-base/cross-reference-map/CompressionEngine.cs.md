# Cross-Reference Map: BACKRabbit.MagiskCore.Compression.CompressionEngine.cs ↔ Magisk Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/Compression/CompressionEngine.cs` |
| **Magisk Source** | `native/src/boot/compression.rs`, `scripts/boot_patch.sh` (compression functions) |
| **Total Lines (BACKRabbit)** | 386 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v27.0 (native/src/boot/compression.rs)
| BACKRabbit Line(s) | Magisk v26.x Line(s) | Function |
|---|---|---|
| 26-35 | ~50-70 | `CompressionFormat` enum |
| 37-61 | ~70-100 | `DetectFormat()` - `check_fmt()` |
| 63-79 | ~100-130 | `Decompress()` - dispatch |
| 81-95 | ~130-150 | `Compress()` - dispatch |
| 99-116 | ~150-180 | `DecompressGzip()` / `CompressGzip()` |
| 122-139 | ~180-210 | `DecompressBzip2()` / `CompressBzip2()` |
| 145-180 | ~210-280 | `DecompressLz4()` / `CompressLz4()` |
| 213-244 | ~280-330 | `DecompressXz()` / `CompressXz()` |
| 302-346 | ~330-380 | `DecompressLzma()` / `CompressLzma()` |

### Magisk compression.rs (v26.3)
| BACKRabbit Line(s) | compression.rs Line(s) | Notes |
|---|---|---|
| 37-61 | ~50-80 | `detect_format()` |
| 63-79 | ~80-110 | `decompress()` dispatch |
| 81-95 | ~110-140 | `compress()` dispatch |
| 99-116 | ~140-170 | gzip via flate2 |
| 122-139 | ~170-200 | bzip2 via bzip2 |
| 145-180 | ~200-270 | lz4 via lz4_flex |
| 213-244 | ~270-320 | xz via xz2 |
| 302-346 | ~320-370 | lzma via lzma |

---

## Detailed Function Mapping

### DetectFormat() - Lines 37-61
**Magisk Equivalent:** `detect_format()` in compression.rs

```rust
// Magisk v26.3 (compression.rs ~line 50)
pub fn detect_format(data: &[u8]) -> CompressionFormat {
    if data.len() < 6 { return CompressionFormat::Unknown; }
    
    if data[0] == 0x1F && data[1] == 0x8B { return CompressionFormat::Gzip; }
    if data[0] == 0x42 && data[1] == 0x5A && data[2] == 0x68 { return CompressionFormat::Bzip2; }
    
    if data[0] == 0x02 && data[1] == 0x21 && data[2] == 0x4C && data[3] == 0x18 {
        return CompressionFormat::Lz4;
    }
    if data[0] == 0x03 && data[1] == 0x21 && data[2] == 0x4C && data[3] == 0x18 {
        return CompressionFormat::Lz4Legacy;
    }
    
    if data[0] == 0xFD && data[1] == 0x37 && data[2] == 0x7A && 
       data[3] == 0x58 && data[4] == 0x5A && data[5] == 0x00 {
        return CompressionFormat::Xz;
    }
    
    if data[0] == 0x5D && data[1] <= 0x40 { return CompressionFormat::Lzma; }
    
    CompressionFormat::Unknown
}
```

**BACKRabbit mapping:** Lines 42-58 direct port with .NET byte array access.

### DecompressGzip/CompressGzip - Lines 99-116
**Magisk Equivalent:** gzip via `flate2` crate

```rust
// Magisk (compression.rs)
fn decompress_gzip(data: &[u8]) -> Vec<u8> {
    let mut decoder = flate2::read::GzDecoder::new(data);
    let mut out = Vec::new();
    decoder.read_to_end(&mut out).unwrap();
    out
}

fn compress_gzip(data: &[u8]) -> Vec<u8> {
    let mut encoder = flate2::write::GzEncoder::new(Vec::new(), flate2::Compression::default());
    encoder.write_all(data).unwrap();
    encoder.finish().unwrap()
}
```

**BACKRabbit mapping:** Uses `System.IO.Compression.GZipStream` (built-in .NET)

### DecompressBzip2/CompressBzip2 - Lines 122-139
**Magisk Equivalent:** bzip2 via `bzip2` crate

```rust
// Magisk (compression.rs)
fn decompress_bzip2(data: &[u8]) -> Vec<u8> {
    let mut decoder = bzip2::read::BzDecoder::new(data);
    let mut out = Vec::new();
    decoder.read_to_end(&mut out).unwrap();
    out
}

fn compress_bzip2(data: &[u8]) -> Vec<u8> {
    let mut encoder = bzip2::write::BzEncoder::new(Vec::new(), bzip2::Compression::default());
    encoder.write_all(data).unwrap();
    encoder.finish().unwrap()
}
```

**BACKRabbit mapping:** Uses `SharpCompress.Compressors.BZip2.BZip2Stream`

### DecompressLz4/CompressLz4 - Lines 145-207
**Magisk Equivalent:** lz4 via `lz4_flex` crate

```rust
// Magisk (compression.rs)
fn decompress_lz4(data: &[u8]) -> Vec<u8> {
    if data.starts_with(&[0x04, 0x22, 0x4C, 0x18]) {  // Legacy
        let size = u32::from_le_bytes(data[4..8].try_into().unwrap()) as usize;
        lz4_flex::decompress(&data[8..], size).unwrap()
    } else {  // Frame
        lz4_flex::frame::decompress(data).unwrap()
    }
}

fn compress_lz4(data: &[u8]) -> Vec<u8> {
    lz4_flex::frame::compress(data)
}
```

**BACKRabbit mapping:** Uses `K4os.Compression.LZ4` (.NET 8 compatible)
- Lines 151-169: Legacy format (uncompressed size at offset 4)
- Lines 172-179: Frame format via `LZ4Stream.Decode`
- Lines 182-207: Compression with frame header + block encode

### DecompressXz/CompressXz - Lines 213-296
**Magisk Equivalent:** xz via `xz2` crate (LZMA2)

```rust
// Magisk (compression.rs)
fn decompress_xz(data: &[u8]) -> Vec<u8> {
    let mut decoder = xz2::read::XzDecoder::new(data);
    let mut out = Vec::new();
    decoder.read_to_end(&mut out).unwrap();
    out
}

fn compress_xz(data: &[u8]) -> Vec<u8> {
    let mut encoder = xz2::write::XzEncoder::new(Vec::new(), 6);  // preset 6
    encoder.write_all(data).unwrap();
    encoder.finish().unwrap()
}
```

**BACKRabbit mapping:** Uses `SevenZip.Compression.LZMA` with LZMA2 properties
- Reads XZ header (properties + size)
- Uses SevenZipDecoder with LZMA2 settings

### DecompressLzma/CompressLzma - Lines 302-372
**Magisk Equivalent:** lzma via `lzma` crate

```rust
// Magisk (compression.rs)
fn decompress_lzma(data: &[u8]) -> Vec<u8> {
    let props = &data[0..5];
    let dict_size = u32::from_le_bytes(data[5..9].try_into().unwrap());
    let out_size = u64::from_le_bytes(data[9..17].try_into().unwrap());
    
    let mut decoder = lzma::LzmaDecoder::new(props, dict_size);
    decoder.decompress(data, out_size).unwrap()
}

fn compress_lzma(data: &[u8]) -> Vec<u8> {
    let mut encoder = lzma::LzmaEncoder::new();
    encoder.set_dict_size(1 << 23);
    encoder.set_fast_bytes(128);
    encoder.compress(data).unwrap()
}
```

**BACKRabbit mapping:** Uses `SevenZip.Compression.LZMA` with explicit properties
- Reads 5-byte properties + 4-byte dict + 8-byte size
- Encoder config: 8MB dict, 128 fast bytes, bt4 match finder

---

## Library Dependencies

| Format | Magisk (Rust) | BACKRabbit (.NET) |
|--------|---------------|-------------------|
| GZIP | `flate2` | `System.IO.Compression` (built-in) |
| BZIP2 | `bzip2` | `SharpCompress` |
| LZ4 | `lz4_flex` | `K4os.Compression.LZ4` |
| XZ | `xz2` (LZMA2) | `SevenZip.Compression.LZMA` |
| LZMA | `lzma` | `SevenZip.Compression.LZMA` |

---

## CompressionFormat Enum (Lines 26-35)

```csharp
public enum CompressionFormat
{
    Unknown,
    Gzip,
    Bzip2,
    Lz4,
    Lz4Legacy,
    Xz,
    Lzma
}
```

---

## Usage Examples

### Auto-detect and Decompress
```csharp
using var engine = new CompressionEngine();

var compressed = File.ReadAllBytes("ramdisk.gz");
var decompressed = engine.Decompress(compressed);  // Auto-detects format

Console.WriteLine($"Decompressed: {decompressed.Length} bytes");
```

### Compress for Boot Image
```csharp
using var engine = new CompressionEngine();

var rawCpio = cpioArchive.Serialize();
var compressed = engine.Compress(rawCpio, CompressionEngine.CompressionFormat.Gzip);

// Magisk default is gzip
```

### Round-trip Test
```csharp
var original = Encoding.UTF8.GetBytes("test data");

using var engine = new CompressionEngine();
var compressed = engine.Compress(original, CompressionEngine.CompressionFormat.Gzip);
var decompressed = engine.Decompress(compressed);

Console.WriteLine(original.SequenceEqual(decompressed)); // true
```

---

## Missing/Partial Implementations

1. **ZOPFLI** - Detected as GZIP, no separate implementation
2. **LZOP** - Not implemented (magic: 0x89 0x4C 0x5A 0x4F)
3. **LZ4 LG** - Frame format but with trailer (LG/Samsung variant)
4. **Multi-threaded compression** - Not supported
5. **Streaming API** - Only byte[] in/out
6. **Compression level control** - Fixed presets only

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| DetectFormat_GZIP_ReturnsGzip | ❌ |
| DetectFormat_BZIP2_ReturnsBzip2 | ❌ |
| DetectFormat_LZ4_Variants_ReturnsLz4 | ❌ |
| DetectFormat_LZ4Legacy_ReturnsLegacy | ❌ |
| DetectFormat_XZ_ReturnsXz | ❌ |
| DetectFormat_LZMA_ReturnsLzma | ❌ |
| DetectFormat_Unknown_ReturnsUnknown | ❌ |
| Decompress_GZIP_RoundTrip | ❌ |
| Decompress_BZIP2_RoundTrip | ❌ |
| Decompress_LZ4_Legacy_RoundTrip | ❌ |
| Decompress_LZ4_Frame_RoundTrip | ❌ |
| Decompress_XZ_RoundTrip | ❌ |
| Decompress_LZMA_RoundTrip | ❌ |
| Compress_GZIP_RoundTrip | ❌ |
| Compress_BZIP2_RoundTrip | ❌ |
| Compress_LZ4_RoundTrip | ❌ |
| Compress_XZ_RoundTrip | ❌ |
| Compress_LZMA_RoundTrip | ❌ |
| Decompress_UnknownFormat_Throws | ❌ |
| Compress_UnknownFormat_Throws | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/Compression/CompressionEngine.cs` (386 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_compression.rs` through `v27.0_compression.rs`
- `scripts/boot_patch.sh` (compress/decompress functions)

**Libraries Used:**
- `System.IO.Compression` - GZIP (built-in)
- `SharpCompress` - BZIP2
- `K4os.Compression.LZ4` - LZ4/LZ4 Legacy
- `SevenZip.Compression.LZMA` - XZ/LZMA