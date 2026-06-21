# Cross-Reference Map: BACKRabbit.Protocol.Fastboot.SparseImage.cs ↔ Android/Magisk Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.Protocol.Fastboot/SparseImage.cs` |
| **Android Source** | `system/extras/ext4_utils/sparse_format.h`, `system/core/fastboot/sparse.cpp` |
| **Magisk Source** | `scripts/flash.sh`, `native/src/boot/sparse.rs` |
| **Total Lines (BACKRabbit)** | 141 |

---

## Version-by-Version Cross-Reference

### Android Sparse Format (system/core/fastboot/sparse.cpp)
| BACKRabbit Line(s) | Android Source | Function |
|---|---|---|
| 24-69 | `sparse.cpp` | `SparseImage::parse()` - parse header + chunks |
| 71-100 | `sparse.cpp` | `SparseImage::to_raw_image()` - expand to raw |
| 103-115 | `sparse_format.h` | `sparse_header` struct |
| 117-124 | `sparse_format.h` | `chunk_header` struct |
| 135-141 | `sparse_format.h` | Chunk type constants |

### Magisk Sources
| BACKRabbit Line(s) | Magisk Source | Notes |
|---|---|---|
| 24-69 | `native/src/boot/sparse.rs` | `SparseImage::parse()` |
| 71-100 | `native/src/boot/sparse.rs` | `to_raw()` |
| 52-69 | `scripts/flash.sh` | Flash sparse super.img |

---

## Detailed Function Mapping

### Parse() - Lines 24-69
**Android Equivalent:** `parse_sparse_image()` in sparse.cpp

```cpp
// Android (system/core/fastboot/sparse.cpp)
unique_ptr<SparseImage> SparseImage::parse(const uint8_t* data, size_t size) {
    // Read header
    auto header = *reinterpret_cast<const sparse_header*>(data);
    
    if (header.magic != SPARSE_HEADER_MAGIC)  // 0xED26
        return nullptr;
    
    SparseImage img;
    img.magic = header.magic;
    img.major_version = header.major_version;
    img.minor_version = header.minor_version;
    img.file_header_size = header.file_header_size;
    img.chunk_header_size = header.chunk_header_size;
    img.block_size = header.block_size;
    img.total_blocks = header.total_blocks;
    img.total_chunks = header.total_chunks;
    img.image_checksum = header.image_checksum;
    
    // Parse chunks
    size_t offset = header.file_header_size;
    for (uint32_t i = 0; i < header.total_chunks; i++) {
        if (offset + header.chunk_header_size > size) break;
        
        auto chunk_hdr = *reinterpret_cast<const chunk_header*>(data + offset);
        offset += header.chunk_header_size;
        
        SparseChunk chunk;
        chunk.type = chunk_hdr.chunk_type;
        chunk.reserved = chunk_hdr.reserved;
        chunk.blocks = chunk_hdr.chunk_size;
        chunk.data_size = chunk_hdr.total_size - header.chunk_header_size;
        
        if (chunk.data_size > 0 && offset + chunk.data_size <= size) {
            chunk.data = vector<uint8_t>(data + offset, data + offset + chunk.data_size);
            offset += chunk.data_size;
        }
        
        img.chunks.push_back(move(chunk));
    }
    
    return img;
}
```

**BACKRabbit mapping:** Lines 28-66 direct port with MemoryMarshal.

### ToRawImage() - Lines 71-100
**Android Equivalent:** `sparse_to_raw()` in sparse.cpp

```cpp
// Android (system/core/fastboot/sparse.cpp)
vector<uint8_t> SparseImage::to_raw() {
    size_t raw_size = total_blocks * block_size;
    vector<uint8_t> raw(raw_size);
    size_t offset = 0;
    
    for (auto& chunk : chunks) {
        switch (chunk.type) {
            case CHUNK_TYPE_RAW:  // 0xCAC1
                copy(chunk.data.begin(), chunk.data.end(), raw.begin() + offset);
                offset += chunk.data.size();
                break;
                
            case CHUNK_TYPE_FILL:  // 0xCAC2
                // First 4 bytes = fill pattern
                uint32_t pattern = *reinterpret_cast<uint32_t*>(chunk.data.data());
                for (size_t i = 0; i < chunk.data_size; i += 4) {
                    *reinterpret_cast<uint32_t*>(raw.data() + offset + i) = pattern;
                }
                offset += chunk.data_size;
                break;
                
            case CHUNK_TYPE_DONT_CARE:  // 0xCAC3
                offset += chunk.blocks * block_size;
                break;
        }
    }
    
    return raw;
}
```

**BACKRabbit mapping:** Lines 72-99 direct port.

---

## Sparse Image Format (AOSP)

### Header (28 bytes) - sparse_header
```c
struct sparse_header {
    uint16_t magic;           // 0xED26 (SPARSE_HEADER_MAGIC)
    uint16_t major_version;   // 1
    uint16_t minor_version;   // 0
    uint16_t file_header_size;  // 28
    uint16_t chunk_header_size; // 12
    uint32_t block_size;      // 4096 (typical)
    uint32_t total_blocks;    // Total blocks in output image
    uint32_t total_chunks;    // Number of chunks
    uint32_t image_checksum;  // CRC32 of raw image
};
```

### Chunk Header (12 bytes) - chunk_header
```c
struct chunk_header {
    uint16_t chunk_type;      // Type of chunk
    uint16_t reserved;        // Reserved
    uint32_t chunk_size;      // Number of blocks in chunk
    uint32_t total_size;      // Total size of chunk (header + data)
};
```

### Chunk Types
| Constant | Value | Description |
|---|---|---|
| `CHUNK_TYPE_RAW` | 0xCAC1 | Raw data follows |
| `CHUNK_TYPE_FILL` | 0xCAC2 | Fill with 4-byte pattern |
| `CHUNK_TYPE_DONT_CARE` | 0xCAC3 | Skip blocks (don't write) |
| `CHUNK_TYPE_CRC32` | 0xCAC4 | CRC32 checksum chunk |

---

## Data Structures

### SparseImage
```csharp
public class SparseImage
{
    public ushort Magic { get; set; }
    public ushort MajorVersion { get; set; }
    public ushort MinorVersion { get; set; }
    public ushort FileHeaderSize { get; set; }
    public ushort ChunkHeaderSize { get; set; }
    public uint BlockSize { get; set; }
    public uint TotalBlocks { get; set; }
    public uint TotalChunks { get; set; }
    public uint ImageChecksum { get; set; }
    
    public List<SparseChunk> Chunks { get; set; } = new();
    
    public static SparseImage Parse(byte[] data)
    public byte[] ToRawImage()
}
```

### SparseChunk
```csharp
public class SparseChunk
{
    public SparseChunkType Type { get; set; }
    public ushort Reserved { get; set; }
    public uint Blocks { get; set; }
    public uint DataSize { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
```

### SparseChunkType
```csharp
public enum SparseChunkType : ushort
{
    Raw = 0xCAC1,
    Fill = 0xCAC2,
    DontCare = 0xCAC3,
    Crc32 = 0xCAC4
}
```

---

## Usage Examples

### Parse Sparse Image
```csharp
var sparseData = File.ReadAllBytes("super.img");

try {
    var sparse = SparseImage.Parse(sparseData);
    
    Console.WriteLine($"Magic: 0x{sparse.Magic:X4}");
    Console.WriteLine($"Version: {sparse.MajorVersion}.{sparse.MinorVersion}");
    Console.WriteLine($"Block Size: {sparse.BlockSize}");
    Console.WriteLine($"Total Blocks: {sparse.TotalBlocks}");
    Console.WriteLine($"Total Chunks: {sparse.TotalChunks}");
    Console.WriteLine($"Image Checksum: 0x{sparse.ImageChecksum:X8}");
    
    foreach (var chunk in sparse.Chunks) {
        Console.WriteLine($"  Chunk: {chunk.Type}, Blocks: {chunk.Blocks}, Data: {chunk.DataSize} bytes");
    }
    
    // Convert to raw
    var rawImage = sparse.ToRawImage();
    Console.WriteLine($"Raw size: {rawImage.Length} bytes");
    
    File.WriteAllBytes("super_raw.img", rawImage);
} catch (InvalidDataException ex) {
    Console.WriteLine($"Not a valid sparse image: {ex.Message}");
}
```

### Flash Sparse Image via Fastboot
```csharp
// FastbootClient.FlashSparseAsync handles this automatically
var sparse = SparseImage.Parse(superData);

await fastboot.SendCommandAsync($"flash:super");

// For each chunk:
foreach (var chunk in sparse.Chunks) {
    switch (chunk.Type) {
        case SparseChunkType.Raw:
            // Send DATA header + raw data
            break;
        case SparseChunkType.Fill:
            // Expand pattern, send DATA header + expanded data
            break;
        case SparseChunkType.DontCare:
            // Skip (no data sent)
            break;
    }
    await fastboot.ReadResponseAsync(); // OKAY after each chunk
}
```

---

## Samsung super.img Specifics

### Samsung Dynamic Partitions
Samsung uses sparse super.img for dynamic partitions:
- `super` partition contains: system, vendor, product, system_ext, odm
- Each logical partition defined in `super_partition_table`
- Flashing super.img writes all logical partitions at once

### Common Block Size
- Samsung: 4096 bytes (standard)
- Some devices: 8192 bytes

---

## Missing/Partial Implementations

1. **CRC32 Verification** - ImageChecksum not validated
2. **CRC32 Chunk** - CHUNK_TYPE_CRC32 not handled
3. **Logical Partition Extraction** - Doesn't extract individual partitions from super
4. **Metadata Parsing** - No parsing of super_partition_table / metadata
5. **Write Support** - No creating sparse images from raw

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| Parse_ValidSparse_ReturnsImage | ❌ |
| Parse_InvalidMagic_Throws | ❌ |
| Parse_RawChunks_ExtractsData | ❌ |
| Parse_FillChunks_ExtractsPattern | ❌ |
| Parse_DontCareChunks_SkipsBlocks | ❌ |
| ToRawImage_RoundTrip_MatchesOriginal | ❌ |
| ToRawImage_FillPattern_ExpandsCorrectly | ❌ |
| ToRawImage_DontCare_SkipsCorrectly | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.Protocol.Fastboot/SparseImage.cs` (141 lines)

**Android Sources:**
- `system/extras/ext4_utils/sparse_format.h` - Format definition
- `system/core/fastboot/sparse.cpp` - Reference implementation

**Magisk Sources (in knowledge-base):**
- `native/src/boot/sparse.rs` - Rust implementation
- `scripts/flash.sh` - Flash sparse images