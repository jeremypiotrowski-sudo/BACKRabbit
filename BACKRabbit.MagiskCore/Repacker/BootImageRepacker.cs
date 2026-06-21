using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BACKRabbit.MagiskCore.Structures.BootHeaders;
using BACKRabbit.MagiskCore.Structures.Avb;
using BACKRabbit.MagiskCore.FormatDetection;
using BACKRabbit.MagiskCore.RamdiskEditor;
using BACKRabbit.MagiskCore.Compression;
using BACKRabbit.MagiskCore.Parser;

namespace BACKRabbit.MagiskCore.Repacker;

/// <summary>
/// Boot image repacker - rebuilds boot images with modified ramdisk/kernel
/// Ported from Magisk's boot_img::repack() and magiskboot's repack functionality
/// </summary>
public class BootImageRepacker
{
    private const int PageAlign = 4096;

    /// <summary>
    /// Repack boot image with new ramdisk
    /// </summary>
    /// <param name="originalImage">Original boot image (for header reference)</param>
    /// <param name="newRamdisk">New ramdisk data (compressed or raw CPIO)</param>
    /// <param name="newKernel">Optional new kernel (null to keep original)</param>
    /// <returns>Repacked boot image</returns>
    public byte[] Repack(BootImage originalImage, byte[] newRamdisk, byte[]? newKernel = null)
    {
        // Detect ramdisk format and compress if needed
        var ramdiskFormat = FormatDetector.CheckFmt(newRamdisk);
        if (!FormatDetector.IsCompressed(ramdiskFormat))
        {
            // Compress ramdisk (Magisk uses gzip by default)
            using var compression = new CompressionEngine();
            newRamdisk = compression.Compress(newRamdisk, CompressionEngine.CompressionFormat.Gzip);
        }

        using var ms = new MemoryStream();

        // Calculate offsets
        var headerSize = GetHeaderSize(originalImage);
        var pageSize = GetPageSize(originalImage);
        var kernelSize = newKernel?.Length ?? (int)originalImage.KernelSize;
        var ramdiskSize = newRamdisk.Length;

        // Write header first
        WriteHeader(ms, originalImage, (uint)kernelSize, (uint)ramdiskSize);

        // Pad header to page boundary
        PadTo(ms, headerSize, pageSize);

        // Write kernel
        if (newKernel != null)
        {
            ms.Write(newKernel, 0, newKernel.Length);
        }
        else
        {
            ms.Write(originalImage.RawData, (int)originalImage.KernelOffset, (int)originalImage.KernelSize);
        }
        PadTo(ms, ms.Position, pageSize);

        // Write ramdisk
        ms.Write(newRamdisk, 0, newRamdisk.Length);
        PadTo(ms, ms.Position, pageSize);

        // Write second stage (v0-v2 only)
        if (originalImage.SecondSize > 0)
        {
            ms.Write(originalImage.RawData, (int)originalImage.SecondOffset, (int)originalImage.SecondSize);
            PadTo(ms, ms.Position, pageSize);
        }

        // Write extra (v0 only)
        if (originalImage.ExtraSize > 0)
        {
            ms.Write(originalImage.RawData, (int)originalImage.ExtraOffset, (int)originalImage.ExtraSize);
            PadTo(ms, ms.Position, pageSize);
        }

        // Write recovery DTBO (v1-v2)
        if (originalImage.RecoveryDtboSize > 0)
        {
            ms.Write(originalImage.RawData, (int)originalImage.RecoveryDtboOffset, (int)originalImage.RecoveryDtboSize);
            PadTo(ms, ms.Position, pageSize);
        }

        // Write DTB (v2+, vendor)
        if (originalImage.DtbSize > 0)
        {
            ms.Write(originalImage.RawData, (int)originalImage.DtbOffset, (int)originalImage.DtbSize);
            PadTo(ms, ms.Position, pageSize);
        }

        // Write signature (v4)
        if (originalImage.SignatureSize > 0)
        {
            // Signature needs to be recalculated - for now, preserve original
            ms.Write(originalImage.RawData, (int)originalImage.SignatureOffset, (int)originalImage.SignatureSize);
            PadTo(ms, ms.Position, pageSize);
        }

        // Write vendor ramdisk table (vendor v4)
        if (originalImage.IsVendor && originalImage.HeaderVersion == 4)
        {
            foreach (var entry in originalImage.VendorRamdiskEntries)
            {
                var entryBytes = new byte[Marshal.SizeOf<VendorRamdiskTableEntryV4>()];
                var entryCopy = entry;
                MemoryMarshal.Write(entryBytes.AsSpan(), ref entryCopy);
                ms.Write(entryBytes, 0, entryBytes.Length);
            }
            PadTo(ms, ms.Position, pageSize);
        }

        // Write tail (SEANDROID, AVB footer, etc.)
        if (originalImage.TailSize > 0)
        {
            ms.Write(originalImage.TailData, 0, originalImage.TailData.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Repack boot image with modified CPIO archive
    /// </summary>
    public byte[] RepackWithRamdisk(BootImage originalImage, CpioArchive newRamdiskArchive)
    {
        // Serialize CPIO to raw data
        var rawRamdisk = newRamdiskArchive.Serialize();
        
        // Compress (Magisk uses gzip)
        using var compression = new CompressionEngine();
        var compressedRamdisk = compression.Compress(rawRamdisk, CompressionEngine.CompressionFormat.Gzip);
        
        return Repack(originalImage, compressedRamdisk);
    }

    /// <summary>
    /// Repack with AVB signature removed (for testing only)
    /// Note: Real AVB signing requires private key
    /// </summary>
    public byte[] RepackWithoutAvb(BootImage originalImage, byte[] newRamdisk)
    {
        var result = Repack(originalImage, newRamdisk);
        
        // Remove AVB footer by truncating
        var avbOffset = FindAvbFooter(result);
        if (avbOffset > 0)
        {
            return result.Take((int)avbOffset).ToArray();
        }
        
        return result;
    }

    #region Header Writing

    private void WriteHeader(Stream ms, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        if (img.IsVendor)
        {
            WriteVendorHeader(ms, img, kernelSize, ramdiskSize);
        }
        else if (img.Flags.IsPxa)
        {
            WritePxaHeader(ms, img, kernelSize, ramdiskSize);
        }
        else
        {
            WriteAospHeader(ms, img, kernelSize, ramdiskSize);
        }
    }

    private void WriteAospHeader(Stream ms, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        var headerSize = GetHeaderSize(img);
        var headerBytes = new byte[headerSize];

        // Write manually using BinaryPrimitives (structs contain byte[] arrays, incompatible with MemoryMarshal)
        switch (img.HeaderVersion)
        {
            case 0:
                WriteV0Header(headerBytes, img, kernelSize, ramdiskSize);
                break;
            case 1:
                WriteV1Header(headerBytes, img, kernelSize, ramdiskSize);
                break;
            case 2:
                WriteV2Header(headerBytes, img, kernelSize, ramdiskSize);
                break;
            case 3:
                WriteV3Header(headerBytes, img, kernelSize, ramdiskSize);
                break;
            case 4:
                WriteV4Header(headerBytes, img, kernelSize, ramdiskSize);
                break;
        }

        ms.Write(headerBytes, 0, headerBytes.Length);
    }

    private static void WriteV0Header(byte[] buf, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        var hdr = img.HeaderV0;
        // magic(8) @0
        Array.Copy(hdr.magic, 0, buf, 0, 8);
        // kernel_size(4) @8
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), kernelSize);
        // kernel_addr(4) @12
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), hdr.kernel_addr);
        // ramdisk_size(4) @16
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), ramdiskSize);
        // ramdisk_addr(4) @20
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4), hdr.ramdisk_addr);
        // second_size(4) @24
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), hdr.second_size);
        // second_addr(4) @28
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(28, 4), hdr.second_addr);
        // tags_addr(4) @32
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32, 4), hdr.tags_addr);
        // page_size(4) @36
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36, 4), hdr.page_size);
        // header_version(4) @40
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(40, 4), hdr.header_version);
        // os_version(4) @44
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(44, 4), hdr.os_version);
        // name(16) @48
        Array.Copy(hdr.name, 0, buf, 48, Math.Min(hdr.name.Length, 16));
        // cmdline(512) @64
        Array.Copy(hdr.cmdline, 0, buf, 64, Math.Min(hdr.cmdline.Length, 512));
        // id(32) @576
        Array.Copy(hdr.id, 0, buf, 576, Math.Min(hdr.id.Length, 32));
        // extra_cmdline(1024) @608
        Array.Copy(hdr.extra_cmdline, 0, buf, 608, Math.Min(hdr.extra_cmdline.Length, 1024));
    }

    private static void WriteV1Header(byte[] buf, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        var hdr = img.HeaderV1;
        // magic(8) @0
        Array.Copy(hdr.magic, 0, buf, 0, 8);
        // kernel_size(4) @8
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), kernelSize);
        // kernel_addr(4) @12
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), hdr.kernel_addr);
        // ramdisk_size(4) @16
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), ramdiskSize);
        // ramdisk_addr(4) @20
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4), hdr.ramdisk_addr);
        // second_size(4) @24
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), hdr.second_size);
        // second_addr(4) @28
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(28, 4), hdr.second_addr);
        // tags_addr(4) @32
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32, 4), hdr.tags_addr);
        // page_size(4) @36
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36, 4), hdr.page_size);
        // header_version(4) @40
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(40, 4), hdr.header_version);
        // os_version(4) @44
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(44, 4), hdr.os_version);
        // name(16) @48
        Array.Copy(hdr.name, 0, buf, 48, Math.Min(hdr.name.Length, 16));
        // cmdline(512) @64
        Array.Copy(hdr.cmdline, 0, buf, 64, Math.Min(hdr.cmdline.Length, 512));
        // id(32) @576
        Array.Copy(hdr.id, 0, buf, 576, Math.Min(hdr.id.Length, 32));
        // extra_cmdline(1024) @608
        Array.Copy(hdr.extra_cmdline, 0, buf, 608, Math.Min(hdr.extra_cmdline.Length, 1024));
        // recovery_dtbo_size(4) @1632
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1632, 4), hdr.recovery_dtbo_size);
        // recovery_dtbo_offset(8) @1636
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(1636, 8), hdr.recovery_dtbo_offset);
        // header_size(4) @1644
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1644, 4), hdr.header_size);
    }

    private static void WriteV2Header(byte[] buf, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        var hdr = img.HeaderV2;
        // magic(8) @0
        Array.Copy(hdr.magic, 0, buf, 0, 8);
        // kernel_size(4) @8
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), kernelSize);
        // kernel_addr(4) @12
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), hdr.kernel_addr);
        // ramdisk_size(4) @16
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), ramdiskSize);
        // ramdisk_addr(4) @20
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4), hdr.ramdisk_addr);
        // second_size(4) @24
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), hdr.second_size);
        // second_addr(4) @28
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(28, 4), hdr.second_addr);
        // tags_addr(4) @32
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32, 4), hdr.tags_addr);
        // page_size(4) @36
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36, 4), hdr.page_size);
        // header_version(4) @40
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(40, 4), hdr.header_version);
        // os_version(4) @44
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(44, 4), hdr.os_version);
        // name(16) @48
        Array.Copy(hdr.name, 0, buf, 48, Math.Min(hdr.name.Length, 16));
        // cmdline(512) @64
        Array.Copy(hdr.cmdline, 0, buf, 64, Math.Min(hdr.cmdline.Length, 512));
        // id(32) @576
        Array.Copy(hdr.id, 0, buf, 576, Math.Min(hdr.id.Length, 32));
        // extra_cmdline(1024) @608
        Array.Copy(hdr.extra_cmdline, 0, buf, 608, Math.Min(hdr.extra_cmdline.Length, 1024));
        // recovery_dtbo_size(4) @1632
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1632, 4), hdr.recovery_dtbo_size);
        // recovery_dtbo_offset(8) @1636
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(1636, 8), hdr.recovery_dtbo_offset);
        // header_size(4) @1644
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1644, 4), hdr.header_size);
        // dtb_size(4) @1648
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1648, 4), hdr.dtb_size);
        // dtb_addr(8) @1652
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(1652, 8), hdr.dtb_addr);
    }

    private static void WriteV3Header(byte[] buf, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        var hdr = img.HeaderV3;
        // magic(8) @0
        Array.Copy(hdr.magic, 0, buf, 0, 8);
        // kernel_size(4) @8
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), kernelSize);
        // ramdisk_size(4) @12
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), ramdiskSize);
        // os_version(4) @16
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), hdr.os_version);
        // header_size(4) @20
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4), hdr.header_size);
        // reserved_0(4) @24
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), hdr.reserved_0);
        // reserved_1(4) @28
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(28, 4), hdr.reserved_1);
        // reserved_2(4) @32
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32, 4), hdr.reserved_2);
        // reserved_3(4) @36
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36, 4), hdr.reserved_3);
        // header_version(4) @40
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(40, 4), hdr.header_version);
        // cmdline(1536) @44
        Array.Copy(hdr.cmdline, 0, buf, 44, Math.Min(hdr.cmdline.Length, 1536));
    }

    private static void WriteV4Header(byte[] buf, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        var hdr = img.HeaderV4;
        // magic(8) @0
        Array.Copy(hdr.magic, 0, buf, 0, 8);
        // kernel_size(4) @8
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), kernelSize);
        // ramdisk_size(4) @12
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), ramdiskSize);
        // os_version(4) @16
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), hdr.os_version);
        // header_size(4) @20
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4), hdr.header_size);
        // reserved_0(4) @24
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), hdr.reserved_0);
        // reserved_1(4) @28
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(28, 4), hdr.reserved_1);
        // reserved_2(4) @32
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32, 4), hdr.reserved_2);
        // reserved_3(4) @36
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36, 4), hdr.reserved_3);
        // header_version(4) @40
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(40, 4), hdr.header_version);
        // cmdline(1536) @44
        Array.Copy(hdr.cmdline, 0, buf, 44, Math.Min(hdr.cmdline.Length, 1536));
        // signature_size(4) @1580
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1580, 4), 0); // Clear signature
    }

    private void WritePxaHeader(Stream ms, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        var headerBytes = new byte[Marshal.SizeOf<BootImgHdrPxa>()];
        var hdr = img.HeaderPxa;
        // magic(8) @0
        Array.Copy(hdr.magic, 0, headerBytes, 0, 8);
        // kernel_size(4) @8
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(8, 4), kernelSize);
        // kernel_addr(4) @12
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(12, 4), hdr.kernel_addr);
        // ramdisk_size(4) @16
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(16, 4), ramdiskSize);
        // ramdisk_addr(4) @20
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(20, 4), hdr.ramdisk_addr);
        // second_size(4) @24
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(24, 4), hdr.second_size);
        // second_addr(4) @28
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(28, 4), hdr.second_addr);
        // extra_size(4) @32
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(32, 4), hdr.extra_size);
        // unknown(4) @36
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(36, 4), hdr.unknown);
        // tags_addr(4) @40
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(40, 4), hdr.tags_addr);
        // page_size(4) @44
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(44, 4), hdr.page_size);
        // name(24) @48
        Array.Copy(hdr.name, 0, headerBytes, 48, Math.Min(hdr.name.Length, 24));
        // cmdline(512) @72
        Array.Copy(hdr.cmdline, 0, headerBytes, 72, Math.Min(hdr.cmdline.Length, 512));
        // id(32) @584
        Array.Copy(hdr.id, 0, headerBytes, 584, Math.Min(hdr.id.Length, 32));
        // extra_cmdline(1024) @616
        Array.Copy(hdr.extra_cmdline, 0, headerBytes, 616, Math.Min(hdr.extra_cmdline.Length, 1024));
        ms.Write(headerBytes, 0, headerBytes.Length);
    }

    private void WriteVendorHeader(Stream ms, BootImage img, uint kernelSize, uint ramdiskSize)
    {
        var headerSize = GetHeaderSize(img);
        var headerBytes = new byte[headerSize];

        if (img.HeaderVersion == 4)
        {
            var hdr4 = img.HeaderV4Vendor;
            // magic(8) @0
            Array.Copy(hdr4.magic, 0, headerBytes, 0, 8);
            // header_version(4) @8
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(8, 4), hdr4.header_version);
            // page_size(4) @12
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(12, 4), hdr4.page_size);
            // kernel_addr(4) @16
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(16, 4), hdr4.kernel_addr);
            // ramdisk_addr(4) @20
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(20, 4), hdr4.ramdisk_addr);
            // ramdisk_size(4) @24
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(24, 4), ramdiskSize);
            // cmdline(2048) @28
            Array.Copy(hdr4.cmdline, 0, headerBytes, 28, Math.Min(hdr4.cmdline.Length, 2048));
            // tags_addr(4) @2076
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(2076, 4), hdr4.tags_addr);
            // name(16) @2080
            Array.Copy(hdr4.name, 0, headerBytes, 2080, Math.Min(hdr4.name.Length, 16));
            // header_size(4) @2096
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(2096, 4), hdr4.header_size);
            // dtb_size(4) @2100
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(2100, 4), hdr4.dtb_size);
            // dtb_addr(8) @2104
            BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(2104, 8), hdr4.dtb_addr);
        }
        else
        {
            var hdr3 = img.HeaderV3Vendor;
            // magic(8) @0
            Array.Copy(hdr3.magic, 0, headerBytes, 0, 8);
            // header_version(4) @8
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(8, 4), hdr3.header_version);
            // page_size(4) @12
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(12, 4), hdr3.page_size);
            // kernel_addr(4) @16
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(16, 4), hdr3.kernel_addr);
            // ramdisk_addr(4) @20
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(20, 4), hdr3.ramdisk_addr);
            // ramdisk_size(4) @24
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(24, 4), ramdiskSize);
            // cmdline(2048) @28
            Array.Copy(hdr3.cmdline, 0, headerBytes, 28, Math.Min(hdr3.cmdline.Length, 2048));
            // tags_addr(4) @2076
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(2076, 4), hdr3.tags_addr);
            // name(16) @2080
            Array.Copy(hdr3.name, 0, headerBytes, 2080, Math.Min(hdr3.name.Length, 16));
            // header_size(4) @2096
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(2096, 4), hdr3.header_size);
            // dtb_size(4) @2100
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(2100, 4), hdr3.dtb_size);
            // dtb_addr(8) @2104
            BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(2104, 8), hdr3.dtb_addr);
        }

        ms.Write(headerBytes, 0, headerBytes.Length);
    }

    #endregion

    #region Helper Methods

    private uint GetHeaderSize(BootImage img)
    {
        if (img.IsVendor)
        {
            return img.HeaderVersion == 4 
                ? img.HeaderV4Vendor.header_size 
                : img.HeaderV3Vendor.header_size;
        }
        return img.HeaderVersion switch
        {
            0 => 512,
            1 => 512,
            2 => 512,
            3 => 4096,
            4 => 4096,
            _ => 512
        };
    }

    private uint GetPageSize(BootImage img)
    {
        if (img.IsVendor)
        {
            return img.HeaderVersion == 4 
                ? img.HeaderV4Vendor.page_size 
                : img.HeaderV3Vendor.page_size;
        }
        if (img.Flags.IsPxa)
        {
            return img.HeaderPxa.page_size;
        }
        return img.HeaderVersion >= 3 ? 4096u : img.HeaderV0.page_size;
    }

    private static void PadTo(Stream ms, long position, uint alignment)
    {
        var aligned = (position + alignment - 1) / alignment * alignment;
        var padding = aligned - position;
        if (padding > 0)
        {
            ms.Write(new byte[padding], 0, (int)padding);
        }
    }

    private long FindAvbFooter(byte[] data)
    {
        var magic = "AVB0"u8.ToArray();
        for (long i = data.Length - Marshal.SizeOf<AvbFooter>(); i >= 0; i--)
        {
            if (data.AsSpan((int)i, 4).SequenceEqual(magic))
            {
                return i;
            }
        }
        return -1;
    }

    #endregion
}