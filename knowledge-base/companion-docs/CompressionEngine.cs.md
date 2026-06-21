# Companion Documentation: BACKRabbit.MagiskCore.Compression.CompressionEngine.cs

## Purpose
Pure C# compression/decompression engine supporting all Android boot image formats (gzip, bzip2, lz4, xz, lzma). No external CLI dependencies - works on clean machines with .NET 8.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                       CompressionEngine                             │
├─────────────────────────────────────────────────────────────────────┤
│  DetectFormat(data) ──→ CompressionFormat                           │
│       │                                                             │
│       ├─→ GZIP: 1F 8B                                              │
│       ├─→ BZIP2: 42 5A 68 ("BZh")                                  │
│       ├─→ LZ4 Frame: 02/03 21 4C 18                                │
│       ├─→ LZ4 Legacy: 04 22 4C 18                                  │
│       ├─→ XZ: FD 37 7A 58 5A 00                                    │
│       ├─→ LZMA: 5D + heuristic (dict power of 2, bytes 5-12=FF)   │
│       └─→ Unknown: throw                                           │
│                                                                     │
│  Decompress(data, format?) ──→ byte[]                               │
│       │                                                             │
│       ├─→ Auto-detect if format=null                               │
│       └─→ Dispatch to format-specific decompressor                 │
│                                                                     │
│  Compress(data, format) ──→ byte[]                                  │
│       │                                                             │
│       └─→ Dispatch to format-specific compressor                   │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Detection
```csharp
/// <summary>
/// Detect compression format from magic bytes
/// </summary>
public static CompressionFormat DetectFormat(byte[] data)
```

### Decompression
```csharp
/// <summary>
/// Decompress data (auto-detects format if not specified)
/// </summary>
/// <param name="data">Compressed data</param>
/// <param name="format">Optional format hint (auto-detected if null)</param>
/// <returns>Decompressed data</returns>
public byte[] Decompress(byte[] data, CompressionFormat? format = null)
```

### Compression
```csharp
/// <summary>
/// Compress data to specified format
/// </summary>
/// <param name="data">Raw data to compress</param>
/// <param name="format">Target compression format</param>
/// <returns>Compressed data</returns>
public byte[] Compress(byte[] data, CompressionFormat format)
```

## CompressionFormat Enum

| Value | Constant | Magic Bytes | Library |
|-------|----------|-------------|---------|
| 1 | Gzip | `1F 8B` | `System.IO.Compression` |
| 2 | Bzip2 | `42 5A 68` | `SharpCompress` |
| 3 | Lz4 | `02 21 4C 18` / `03 21 4C 18` | `K4os.Compression.LZ4` |
| 4 | Lz4Legacy | `04 22 4C 18` | `K4os.Compression.LZ4` |
| 5 | Xz | `FD 37 7A 58 5A 00` | `SevenZip.Compression` |
| 6 | Lzma | `5D` + heuristic | `SevenZip.Compression` |

## Format Details

### GZIP (Standard .NET)
```csharp
// Decompress
using var gzip = new GZipStream(input, CompressionMode.Decompress);
gzip.CopyTo(output);

// Compress
using var gzip = new GZipStream(output, CompressionMode.Compress);
input.CopyTo(gzip);
```
- **Magic**: `1F 8B`
- **Library**: Built-in `System.IO.Compression`
- **Used by**: Android ramdisks (most common)

### BZIP2 (SharpCompress)
```csharp
// Decompress
using var bzip2 = new BZip2Stream(input, CompressionMode.Decompress);
bzip2.CopyTo(output);

// Compress
using var bzip2 = new BZip2Stream(output, CompressionMode.Compress);
bzip2.Write(data);
```
- **Magic**: `42 5A 68` ("BZh")
- **Library**: `SharpCompress.Compressors.BZip2`
- **Used by**: Some vendor ramdisks

### LZ4 (K4os)
```csharp
// Legacy format: [magic][uncompressed_size][compressed_data]
if (format == Lz4Legacy) {
    var uncompressedSize = BitConverter.ToInt32(data, 4);
    var decoded = LZ4Codec.Decode(data.AsSpan(8), outputBuffer);
}

// Frame format: standard LZ4 frame
using var lz4Stream = LZ4Stream.Decode(inputStream);
lz4Stream.CopyTo(outputStream);
```
- **Magic (Frame)**: `02 21 4C 18` or `03 21 4C 18`
- **Magic (Legacy)**: `04 22 4C 18`
- **Library**: `K4os.Compression.LZ4` (v1.3.8+)
- **Used by**: Samsung ramdisks, some vendor partitions

### XZ (SevenZip - LZMA2)
```csharp
// XZ structure: [properties(5)][uncompressed_size(8)][LZMA2_data]

// Decompress
var decoder = new SevenZipDecoder();
decoder.SetDecoderProperties(properties);
decoder.Code(inputStream, outputStream, inputLen, outSize, null);

// Compress
var encoder = new SevenZipEncoder();
encoder.SetCoderProperties(props, values);
encoder.WriteCoderProperties(output);
output.Write(BitConverter.GetBytes(data.Length));
encoder.Code(input, output, -1, -1, null);
```
- **Magic**: `FD 37 7A 58 5A 00`
- **Library**: `SevenZip.Compression.LZMA`
- **Used by**: Some kernel images, vendor ramdisks

### LZMA (Raw - SevenZip)
```csharp
// Raw LZMA: [properties(5)][dict_size(4)][uncompressed_size(8)][LZMA_data]
```
- **Magic**: `5D` + heuristic
- **Library**: `SevenZip.Compression.LZMA`
- **Used by**: Rare, some vendor blobs

---

## Usage Examples

### Auto-Detect and Decompress
```csharp
using var engine = new CompressionEngine();

// Ramdisk from boot image
var ramdisk = bootParser.ExtractRamdisk(bootImage);

// Auto-detect format
var format = CompressionEngine.DetectFormat(ramdisk);
Console.WriteLine($"Detected: {format}");

// Decompress
var decompressed = engine.Decompress(ramdisk);
// or explicit
var decompressed = engine.Decompress(ramdisk, CompressionFormat.Gzip);

// Parse CPIO
var cpio = CpioArchive.Parse(decompressed);
```

### Compress Ramdisk for Repack
```csharp
var engine = new CompressionEngine();

// Serialize CPIO
var rawRamdisk = cpioArchive.Serialize();

// Compress with gzip (Magisk default)
var compressed = engine.Compress(rawRamdisk, CompressionFormat.Gzip);

// Repack
var repacked = repacker.Repack(originalImage, compressed);
```

### Batch Decompress
```csharp
var engine = new CompressionEngine();

foreach (var file in Directory.GetFiles("ramdisks", "*.img")) {
    var data = File.ReadAllBytes(file);
    var format = CompressionEngine.DetectFormat(data);
    
    if (format != CompressionFormat.Unknown) {
        var decompressed = engine.Decompress(data, format);
        File.WriteAllBytes(file + ".cpio", decompressed);
        Console.WriteLine($"{file}: {format} → {decompressed.Length} bytes");
    }
}
```

### Round-Trip Test
```csharp
var engine = new CompressionEngine();
var original = File.ReadAllBytes("test.txt");

foreach (var fmt in new[] { 
    CompressionFormat.Gzip, 
    CompressionFormat.Bzip2, 
    CompressionFormat.Lz4,
    CompressionFormat.Xz,
    CompressionFormat.Lzma 
}) {
    var compressed = engine.Compress(original, fmt);
    var decompressed = engine.Decompress(compressed, fmt);
    
    Console.WriteLine($"{fmt}: {original.Length} → {compressed.Length} → {decompressed.Length} ✓");
}
```

---

## Magic Bytes Reference

| Format | Offset | Magic (Hex) | ASCII | Notes |
|--------|--------|-------------|-------|-------|
| GZIP | 0 | `1F 8B` | - | RFC 1952 |
| ZOPFLI | 0 | `1F 8B` | - | Same as GZIP |
| BZIP2 | 0 | `42 5A 68` | "BZh" | bzip2 spec |
| LZ4 Frame | 0 | `02 21 4C 18` | - | LZ4 frame v1 |
| LZ4 Frame | 0 | `03 21 4C 18` | - | LZ4 frame v1 (alt) |
| LZ4 Legacy | 0 | `04 22 4C 18` | - | Legacy block |
| XZ | 0 | `FD 37 7A 58 5A 00` | - | XZ spec |
| LZMA | 0 | `5D` + props | - | LZMA SDK |

---

## Library Requirements (NuGet)

```xml
<PackageReference Include="SharpCompress" Version="0.38.0" />
<PackageReference Include="K4os.Compression.LZ4" Version="1.3.8" />
<PackageReference Include="SevenZip.Compression" Version="1.0.0" />
```

All pure .NET - no native dependencies.

---

## Missing/Partial Implementations

1. **ZOPFLI** - Detected as GZIP, compressed with standard GZIP
2. **LZOP** - Not implemented (rare)
3. **LZ4_LG** - FormatDetector identifies it, but uses standard LZ4 decoder
4. **Streaming API** - All operations buffer full data in MemoryStream
5. **Multi-threaded Compression** - Not available
6. **Dictionary Size Configuration** - Hardcoded (8MB for XZ)
7. **Compression Levels** - Default only

---

## Error Handling

```csharp
try {
    var result = engine.Decompress(data, format);
} catch (InvalidDataException ex) {
    // Invalid format, corrupted data, or size mismatch
} catch (ObjectDisposedException) {
    // Engine disposed
} catch (IOException ex) {
    // Compression/decompression failed
}
```

---

## Test Coverage Needed

### Detection
```csharp
[Test] void DetectFormat_GZIP_ReturnsGzip()
[Test] void DetectFormat_BZIP2_ReturnsBzip2()
[Test] void DetectFormat_LZ4Frame_ReturnsLz4()
[Test] void DetectFormat_LZ4Legacy_ReturnsLz4Legacy()
[Test] void DetectFormat_XZ_ReturnsXz()
[Test] void DetectFormat_LZMA_ReturnsLzma()
[Test] void DetectFormat_Unknown_ReturnsUnknown()
```

### Decompression Round-Trips
```csharp
[Test] void Decompress_GZIP_RoundTrip_MatchesOriginal()
[Test] void Decompress_BZIP2_RoundTrip_MatchesOriginal()
[Test] void Decompress_LZ4Frame_RoundTrip_MatchesOriginal()
[Test] void Decompress_LZ4Legacy_RoundTrip_MatchesOriginal()
[Test] void Decompress_XZ_RoundTrip_MatchesOriginal()
[Test] void Decompress_LZMA_RoundTrip_MatchesOriginal()
```

### Compression Round-Trips
```csharp
[Test] void Compress_GZIP_RoundTrip_MatchesOriginal()
[Test] void Compress_BZIP2_RoundTrip_MatchesOriginal()
[Test] void Compress_LZ4_RoundTrip_MatchesOriginal()
[Test] void Compress_XZ_RoundTrip_MatchesOriginal()
[Test] void Compress_LZMA_RoundTrip_MatchesOriginal()
```

### Error Cases
```csharp
[Test] void Decompress_UnknownFormat_ThrowsInvalidDataException()
[Test] void Compress_UnsupportedFormat_ThrowsArgumentException()
[Test] void Decompress_DisposedEngine_ThrowsObjectDisposedException()
```

---

## Related Files

| File | Relationship |
|------|--------------|
| `FormatDetector.cs` | Provides FileFormat detection (more formats) |
| `BootImageParser.cs` | Extracts ramdisk for decompression |
| `BootImageRepacker.cs` | Compresses ramdisk before repack |
| `CpioArchive.cs` | Requires decompressed data |
| `MagiskArtifactDetector.cs` | Uses IsCompressed check |

---

## References

1. **Magisk compress.cpp** - `native/src/compress/compress.cpp`
2. **Magisk compress.rs** - `native/src/compress/compress.rs`
3. **Magisk boot_patch.sh** - Shell compression functions
4. **RFC 1952** - GZIP specification
5. **LZ4 Frame Format** - LZ4 specification
6. **XZ Format** - XZ specification
7. **LZMA SDK** - 7-Zip LZMA SDK documentation