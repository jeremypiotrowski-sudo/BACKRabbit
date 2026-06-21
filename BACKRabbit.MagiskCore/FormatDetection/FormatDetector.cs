using System.Buffers.Binary;

namespace BACKRabbit.MagiskCore.FormatDetection;

/// <summary>
/// File format enumeration - matches Magisk's FileFormat
/// From: Magisk native/src/boot/format.rs
/// </summary>
public enum FileFormat
{
    UNKNOWN = 0,
    
    // Boot image formats
    AOSP = 1,           // "ANDROID!" magic
    AOSP_VENDOR = 2,    // "VNDRBOOT" magic
    CHROMEOS = 3,       // ChromeOS boot image
    MTK = 4,            // MediaTek boot image
    DTB = 5,            // Device tree blob
    DHTB = 6,           // DHTB header (Motorola)
    BLOB = 7,           // Tegra blob (NVIDIA)
    ZIMAGE = 8,         // ARM zImage kernel
    
    // Compression formats
    GZIP = 10,          // gzip (1F 8B)
    ZOPFLI = 11,        // zopfli (also 1F 8B)
    LZOP = 12,          // lzop
    XZ = 13,            // XZ (FD 37 7A 58 5A 00)
    LZMA = 14,          // LZMA (5D + specific pattern)
    BZIP2 = 15,         // bzip2 (42 5A 68 = "BZh")
    LZ4 = 16,           // LZ4 (02 21 4C 18 or 03 21 4C 18)
    LZ4_LEGACY = 17,    // LZ4 legacy (04 22 4C 18)
    LZ4_LG = 18,        // LZ4 LG format (block-based with trailer)
}

/// <summary>
/// Format detection utilities - port of Magisk's check_fmt() and check_fmt_lg()
/// From: Magisk native/src/boot/magisk_bootimg.cpp
/// </summary>
public static class FormatDetector
{
    // Magic bytes from Magisk source (bootimg.cpp + format.rs)
    private static readonly byte[] BOOT_MAGIC = "ANDROID!"u8.ToArray();
    private static readonly byte[] VENDOR_BOOT_MAGIC = "VNDRBOOT"u8.ToArray();
    private static readonly byte[] CHROMEOS_MAGIC = "CHROMEOS"u8.ToArray();
    private static readonly byte[] MTK_MAGIC = [0x88, 0x16, 0x88, 0x16];
    private static readonly byte[] DTB_MAGIC = [0xD0, 0x0D, 0xFE, 0xED];
    private static readonly byte[] DHTB_MAGIC = "DHTBHDR!"u8.ToArray();
    private static readonly byte[] TEGRABLOB_MAGIC = [0x1E, 0x2A, 0x3E, 0x4F];
    private static readonly byte[] GZIP1_MAGIC = [0x1F, 0x8B];
    private static readonly byte[] XZ_MAGIC = [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00];
    private static readonly byte[] BZIP_MAGIC = "BZh"u8.ToArray();
    private static readonly byte[] LZ41_MAGIC = [0x02, 0x21, 0x4C, 0x18];
    private static readonly byte[] LZ42_MAGIC = [0x03, 0x21, 0x4C, 0x18];
    private static readonly byte[] LZ4_LEG_MAGIC = [0x04, 0x22, 0x4C, 0x18];
    private static readonly byte[] SEANDROID_MAGIC = "SEANDROIDENFORCE"u8.ToArray();
    private static readonly byte[] LG_BUMP_MAGIC = "LG_BUMP_REV"u8.ToArray();
    private static readonly byte[] AVB_FOOTER_MAGIC = "AVB0"u8.ToArray();
    private static readonly byte[] AVB_MAGIC = "AVB0"u8.ToArray();
    private static readonly byte[] ZIMAGE_MAGIC = "zImage"u8.ToArray();

/// <summary>
    /// Detect file format from magic bytes - port of check_fmt()
    /// </summary>
    /// <param name="buf">Buffer to analyze (at least first 16 bytes of file)</param>
    /// <returns>Detected FileFormat</returns>
    public static FileFormat CheckFmt(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < 8) return FileFormat.UNKNOWN;

        // Check boot formats first
        if (buf.Length >= CHROMEOS_MAGIC.Length && buf.Slice(0, CHROMEOS_MAGIC.Length).SequenceEqual(CHROMEOS_MAGIC)) return FileFormat.CHROMEOS;
        if (buf.Length >= BOOT_MAGIC.Length && buf.Slice(0, BOOT_MAGIC.Length).SequenceEqual(BOOT_MAGIC)) return FileFormat.AOSP;
        if (buf.Length >= VENDOR_BOOT_MAGIC.Length && buf.Slice(0, VENDOR_BOOT_MAGIC.Length).SequenceEqual(VENDOR_BOOT_MAGIC)) return FileFormat.AOSP_VENDOR;
        
        // Check compression formats
        if (buf.Length >= GZIP1_MAGIC.Length && buf.Slice(0, GZIP1_MAGIC.Length).SequenceEqual(GZIP1_MAGIC)) return FileFormat.GZIP;
        if (buf.Length >= XZ_MAGIC.Length && buf.Slice(0, XZ_MAGIC.Length).SequenceEqual(XZ_MAGIC)) return FileFormat.XZ;
        if (IsLzma(buf)) return FileFormat.LZMA;
        if (buf.Length >= BZIP_MAGIC.Length && buf.Slice(0, BZIP_MAGIC.Length).SequenceEqual(BZIP_MAGIC)) return FileFormat.BZIP2;
        if (buf.Length >= LZ41_MAGIC.Length && buf.Slice(0, LZ41_MAGIC.Length).SequenceEqual(LZ41_MAGIC)) return FileFormat.LZ4;
        if (buf.Length >= LZ42_MAGIC.Length && buf.Slice(0, LZ42_MAGIC.Length).SequenceEqual(LZ42_MAGIC)) return FileFormat.LZ4;
        if (buf.Length >= LZ4_LEG_MAGIC.Length && buf.Slice(0, LZ4_LEG_MAGIC.Length).SequenceEqual(LZ4_LEG_MAGIC)) return FileFormat.LZ4_LEGACY;
        
        // Check special formats
        if (buf.Length >= MTK_MAGIC.Length && buf.Slice(0, MTK_MAGIC.Length).SequenceEqual(MTK_MAGIC)) return FileFormat.MTK;
        if (buf.Length >= DTB_MAGIC.Length && buf.Slice(0, DTB_MAGIC.Length).SequenceEqual(DTB_MAGIC)) return FileFormat.DTB;
        if (buf.Length >= DHTB_MAGIC.Length && buf.Slice(0, DHTB_MAGIC.Length).SequenceEqual(DHTB_MAGIC)) return FileFormat.DHTB;
        if (buf.Length >= TEGRABLOB_MAGIC.Length && buf.Slice(0, TEGRABLOB_MAGIC.Length).SequenceEqual(TEGRABLOB_MAGIC)) return FileFormat.BLOB;
        
        // Check for zImage (at offset 0x24)
        if (buf.Length >= 0x28 && buf.Slice(0x24, 4).SequenceEqual(ZIMAGE_MAGIC)) 
            return FileFormat.ZIMAGE;

        return FileFormat.UNKNOWN;
    }

    /// <summary>
    /// Enhanced format detection for LZ4_LG variant - port of check_fmt_lg()
    /// LZ4_LG is a special block-based format used by LG/Samsung
    /// </summary>
    /// <param name="buf">Buffer to analyze</param>
    /// <returns>Detected FileFormat (may be LZ4_LG)</returns>
    public static FileFormat CheckFmtLg(ReadOnlySpan<byte> buf)
    {
        var fmt = CheckFmt(buf);
        
        if (fmt == FileFormat.LZ4_LEGACY && buf.Length >= 8)
        {
            // Check for LZ4_LG format
            // LZ4_LEGACY has magic 04 22 4C 18
            // LZ4_LG adds a trailer with total uncompressed size
            // Structure: magic(4) + [block_size(4) + data(n)]* + trailer(4)
            uint off = 4;
            while (off + 4 <= buf.Length)
            {
                uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice((int)off, 4));
                off += 4;
                if (off + blockSize > buf.Length)
                {
                    // Block extends past buffer - this is LZ4_LG format
                    return FileFormat.LZ4_LG;
                }
                off += blockSize;
            }
        }
        
        return fmt;
    }

    /// <summary>
    /// LZMA detection - matches Magisk's guess_lzma() function
    /// LZMA doesn't have a fixed magic, so we use heuristics:
    /// - Byte 0: 0x5D (pb * 5 + lp) * 9 + lc
    /// - Bytes 1-4: dict size (must be power of 2)
    /// - Bytes 5-12: all 0xFF
    /// </summary>
    private static bool IsLzma(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < 13) return false;
        if (buf[0] != 0x5D) return false;
        
        // Dictionary size must be power of 2
        uint dictSz = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(1, 4));
        if (dictSz == 0 || (dictSz & (dictSz - 1)) != 0) return false;
        
// Bytes 5-12 must be all 0xFF
        var allFF = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        if (!buf.Slice(5, 8).SequenceEqual(allFF)) 
            return false;
        
        return true;
    }

    /// <summary>
    /// Check if format is compressed
    /// </summary>
    public static bool IsCompressed(FileFormat fmt)
    {
        return fmt is FileFormat.GZIP or FileFormat.ZOPFLI or FileFormat.XZ 
            or FileFormat.LZMA or FileFormat.BZIP2 or FileFormat.LZ4 
            or FileFormat.LZ4_LEGACY or FileFormat.LZ4_LG or FileFormat.LZOP;
    }

    /// <summary>
    /// Get file extension for format
    /// </summary>
    public static string GetExtension(FileFormat fmt) => fmt switch
    {
        FileFormat.GZIP or FileFormat.ZOPFLI => "gz",
        FileFormat.LZOP => "lzo",
        FileFormat.XZ => "xz",
        FileFormat.LZMA => "lzma",
        FileFormat.BZIP2 => "bz2",
        FileFormat.LZ4 or FileFormat.LZ4_LEGACY or FileFormat.LZ4_LG => "lz4",
        _ => ""
    };

    /// <summary>
    /// Get human-readable format name
    /// </summary>
    public static string GetName(FileFormat fmt) => fmt switch
    {
        FileFormat.UNKNOWN => "unknown",
        FileFormat.AOSP => "aosp",
        FileFormat.AOSP_VENDOR => "aosp_vendor",
        FileFormat.CHROMEOS => "chromeos",
        FileFormat.MTK => "mtk",
        FileFormat.DTB => "dtb",
        FileFormat.DHTB => "dhtb",
        FileFormat.BLOB => "blob",
        FileFormat.ZIMAGE => "zimage",
        FileFormat.GZIP => "gzip",
        FileFormat.ZOPFLI => "zopfli",
        FileFormat.LZOP => "lzop",
        FileFormat.XZ => "xz",
        FileFormat.LZMA => "lzma",
        FileFormat.BZIP2 => "bzip2",
        FileFormat.LZ4 => "lz4",
        FileFormat.LZ4_LEGACY => "lz4_legacy",
        FileFormat.LZ4_LG => "lz4_lg",
        _ => fmt.ToString().ToLower()
    };
}

/// <summary>
/// Extension methods for format detection on byte arrays and streams
/// </summary>
public static class FormatDetectionExtensions
{
    /// <summary>
    /// Check if format is compressed
    /// </summary>
    public static bool IsCompressed(this FileFormat fmt)
    {
        return FormatDetector.IsCompressed(fmt);
    }

    /// <summary>
    /// Detect format from byte array
    /// </summary>
    public static FileFormat DetectFormat(this byte[] data)
    {
        if (data == null || data.Length == 0) return FileFormat.UNKNOWN;
        return FormatDetector.CheckFmtLg(data);
    }

    /// <summary>
    /// Detect format from stream (reads first 512 bytes)
    /// </summary>
    public static FileFormat DetectFormat(this Stream stream)
    {
        if (stream == null || !stream.CanRead) return FileFormat.UNKNOWN;
        
        var buf = new byte[512];
        var pos = stream.Position;
        var read = stream.Read(buf, 0, buf.Length);
        stream.Position = pos;
        
        return FormatDetector.CheckFmtLg(buf.AsSpan(0, read));
    }

    /// <summary>
    /// Detect format from file path
    /// </summary>
    public static FileFormat DetectFormat(string filePath)
    {
        if (!File.Exists(filePath)) return FileFormat.UNKNOWN;
        
        using var fs = File.OpenRead(filePath);
        return fs.DetectFormat();
    }
}