using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using BACKRabbit.MagiskCore.Structures.BootHeaders;
using BACKRabbit.MagiskCore.Structures.Avb;
using BACKRabbit.MagiskCore.FormatDetection;
using BACKRabbit.MagiskCore.RamdiskEditor;
using BACKRabbit.MagiskCore.Compression;

namespace BACKRabbit.MagiskCore.Parser;

/// <summary>
/// Boot image parser - port of Magisk's boot_img class from bootimg.cpp
/// Handles all Android boot image formats (v0-v4, vendor v3-v4, PXA, MTK, etc.)
/// </summary>
public class BootImageParser
{
    private const int PageAlign = 4096;

    /// <summary>
    /// Parse boot image from file
    /// </summary>
    public BootImage Parse(string path)
    {
        var data = File.ReadAllBytes(path);
        return Parse(data);
    }

    /// <summary>
    /// Parse boot image from byte array
    /// </summary>
    public BootImage Parse(byte[] data)
    {
        var img = new BootImage { RawData = data };

        // Scan for special headers (DHTB, BLOB, CHROMEOS, etc.)
        for (int i = 0; i < data.Length; i++)
        {
            var fmt = FormatDetector.CheckFmt(data.AsSpan(i));
            switch (fmt)
            {
                case FileFormat.DHTB:
                    img.Flags.HasDhtb = true;
                    img.Flags.HasSeandroid = true;
                    i += Marshal.SizeOf<DhtbHdr>() - 1;
                    break;
                case FileFormat.BLOB:
                    img.Flags.HasBlob = true;
                    i += Marshal.SizeOf<BlobHdr>() - 1;
                    break;
                case FileFormat.CHROMEOS:
                    img.Flags.HasChromeos = true;
                    i += 65535;
                    break;
                case FileFormat.AOSP:
                case FileFormat.AOSP_VENDOR:
                    if (ParseHeader(data.AsSpan(i), fmt, img))
                    {
                        ParseSections(data, img);
                        ParseTail(data, img);
                        return img;
                    }
                    break;
            }
        }

        throw new InvalidDataException("No valid boot image header found");
    }

    private bool ParseHeader(ReadOnlySpan<byte> data, FileFormat type, BootImage img)
    {
        if (data.Length < 8) return false;

        // Check for vendor boot
        if (type == FileFormat.AOSP_VENDOR)
        {
            var magic = Encoding.ASCII.GetString(data.Slice(0, 8)).TrimEnd('\0');
            if (magic == "VNDRBOOT")
            {
                img.IsVendor = true;
                // Vendor boot header: magic(8) + header_version(4) at offset 8
                var vendorHeaderVersion = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
                img.HeaderVersion = vendorHeaderVersion;

                if (vendorHeaderVersion == 4)
                {
                    // Read VendorBootImgHdrV4 fields manually
                    var hdr4 = new VendorBootImgHdrV4();
                    hdr4.magic = data.Slice(0, 8).ToArray();
                    hdr4.header_version = vendorHeaderVersion;
                    hdr4.page_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
                    hdr4.kernel_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));
                    hdr4.ramdisk_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
                    hdr4.ramdisk_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
                    hdr4.cmdline = data.Slice(28, 2048).ToArray();
                    hdr4.tags_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2076, 4));
                    hdr4.name = data.Slice(2080, 16).ToArray();
                    hdr4.header_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2096, 4));
                    hdr4.dtb_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2100, 4));
                    hdr4.dtb_addr = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(2104, 8));
                    img.HeaderV4Vendor = hdr4;
                }
                else
                {
                    // Read VendorBootImgHdrV3 fields manually
                    var hdr3 = new VendorBootImgHdrV3();
                    hdr3.magic = data.Slice(0, 8).ToArray();
                    hdr3.header_version = vendorHeaderVersion;
                    hdr3.page_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
                    hdr3.kernel_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));
                    hdr3.ramdisk_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
                    hdr3.ramdisk_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
                    hdr3.cmdline = data.Slice(28, 2048).ToArray();
                    hdr3.tags_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2076, 4));
                    hdr3.name = data.Slice(2080, 16).ToArray();
                    hdr3.header_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2096, 4));
                    hdr3.dtb_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2100, 4));
                    hdr3.dtb_addr = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(2104, 8));
                    img.HeaderV3Vendor = hdr3;
                }

                return true;
            }
        }

        // Standard AOSP boot — read key fields manually (structs contain byte[] arrays, incompatible with MemoryMarshal)
        // V0/V1/V2 header layout: magic(8) + kernel_size(4)@8 + kernel_addr(4)@12 + ramdisk_size(4)@16 + ramdisk_addr(4)@20
        //   + second_size(4)@24 + second_addr(4)@28 + tags_addr(4)@32 + page_size(4)@36 + header_version(4)@40
        // V3/V4 header layout: magic(8) + kernel_size(4)@8 + ramdisk_size(4)@12 (no kernel_addr, different layout)
        var kernelSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
        var pageSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(36, 4));
        var headerVersion = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(40, 4));
        // ramdisk_size is at offset 16 for V0-V2, offset 12 for V3-V4
        var ramdiskSize = (headerVersion >= 3)
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4))
            : BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));

        // Check for Samsung PXA (page_size >= 0x02000000)
        if (pageSize >= 0x02000000)
        {
            var pxa = new BootImgHdrPxa();
            pxa.magic = data.Slice(0, 8).ToArray();
            pxa.kernel_size = kernelSize;
            pxa.kernel_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
            pxa.ramdisk_size = ramdiskSize;
            pxa.ramdisk_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
            pxa.second_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
            pxa.second_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(28, 4));
            pxa.extra_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4));
            pxa.unknown = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(36, 4));
            pxa.tags_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(40, 4));
            pxa.page_size = pageSize;
            pxa.name = data.Slice(48, 24).ToArray();
            pxa.cmdline = data.Slice(72, 512).ToArray();
            pxa.id = data.Slice(584, 32).ToArray();
            pxa.extra_cmdline = data.Slice(616, 1024).ToArray();
            img.HeaderPxa = pxa;
            img.Flags.IsPxa = true;
            img.HeaderVersion = headerVersion;
            return true;
        }

        // Standard AOSP headers v0-v4
        img.HeaderVersion = headerVersion;
        switch (headerVersion)
        {
            case 0:
                var hdr0 = new BootImgHdrV0();
                hdr0.magic = data.Slice(0, 8).ToArray();
                hdr0.kernel_size = kernelSize;
                hdr0.kernel_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
                hdr0.ramdisk_size = ramdiskSize;
                hdr0.ramdisk_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
                hdr0.second_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
                hdr0.second_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(28, 4));
                hdr0.tags_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4));
                hdr0.page_size = pageSize;
                hdr0.header_version = headerVersion;
                hdr0.os_version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(44, 4));
                hdr0.name = data.Slice(48, 16).ToArray();
                hdr0.cmdline = data.Slice(64, 512).ToArray();
                hdr0.id = data.Slice(576, 32).ToArray();
                hdr0.extra_cmdline = data.Slice(608, 1024).ToArray();
                img.HeaderV0 = hdr0;
                break;
            case 1:
                var hdr1 = new BootImgHdrV1();
                hdr1.magic = data.Slice(0, 8).ToArray();
                hdr1.kernel_size = kernelSize;
                hdr1.kernel_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
                hdr1.ramdisk_size = ramdiskSize;
                hdr1.ramdisk_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
                hdr1.second_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
                hdr1.second_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(28, 4));
                hdr1.tags_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4));
                hdr1.page_size = pageSize;
                hdr1.header_version = headerVersion;
                hdr1.os_version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(44, 4));
                hdr1.name = data.Slice(48, 16).ToArray();
                hdr1.cmdline = data.Slice(64, 512).ToArray();
                hdr1.id = data.Slice(576, 32).ToArray();
                hdr1.extra_cmdline = data.Slice(608, 1024).ToArray();
                hdr1.recovery_dtbo_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1632, 4));
                hdr1.recovery_dtbo_offset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(1636, 8));
                hdr1.header_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1644, 4));
                img.HeaderV1 = hdr1;
                break;
            case 2:
                var hdr2 = new BootImgHdrV2();
                hdr2.magic = data.Slice(0, 8).ToArray();
                hdr2.kernel_size = kernelSize;
                hdr2.kernel_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
                hdr2.ramdisk_size = ramdiskSize;
                hdr2.ramdisk_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
                hdr2.second_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
                hdr2.second_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(28, 4));
                hdr2.tags_addr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4));
                hdr2.page_size = pageSize;
                hdr2.header_version = headerVersion;
                hdr2.os_version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(44, 4));
                hdr2.name = data.Slice(48, 16).ToArray();
                hdr2.cmdline = data.Slice(64, 512).ToArray();
                hdr2.id = data.Slice(576, 32).ToArray();
                hdr2.extra_cmdline = data.Slice(608, 1024).ToArray();
                hdr2.recovery_dtbo_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1632, 4));
                hdr2.recovery_dtbo_offset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(1636, 8));
                hdr2.header_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1644, 4));
                hdr2.dtb_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1648, 4));
                hdr2.dtb_addr = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(1652, 8));
                img.HeaderV2 = hdr2;
                break;
            case 3:
                var hdr3 = new BootImgHdrV3();
                hdr3.magic = data.Slice(0, 8).ToArray();
                hdr3.kernel_size = kernelSize;
                hdr3.ramdisk_size = ramdiskSize;
                hdr3.os_version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
                hdr3.header_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));
                hdr3.reserved_0 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
                hdr3.reserved_1 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
                hdr3.reserved_2 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(28, 4));
                hdr3.reserved_3 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4));
                hdr3.header_version = headerVersion;
                hdr3.cmdline = data.Slice(40, 1536).ToArray();
                img.HeaderV3 = hdr3;
                break;
            case 4:
                var hdr4 = new BootImgHdrV4();
                hdr4.magic = data.Slice(0, 8).ToArray();
                hdr4.kernel_size = kernelSize;
                hdr4.ramdisk_size = ramdiskSize;
                hdr4.os_version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
                hdr4.header_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));
                hdr4.reserved_0 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
                hdr4.reserved_1 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
                hdr4.reserved_2 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(28, 4));
                hdr4.reserved_3 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4));
                hdr4.header_version = headerVersion;
                hdr4.cmdline = data.Slice(40, 1536).ToArray();
                hdr4.signature_size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1576, 4));
                img.HeaderV4 = hdr4;
                break;
            default:
                // Fallback to V0
                var hdrFallback = new BootImgHdrV0();
                hdrFallback.magic = data.Slice(0, 8).ToArray();
                hdrFallback.kernel_size = kernelSize;
                hdrFallback.ramdisk_size = ramdiskSize;
                hdrFallback.page_size = pageSize;
                hdrFallback.header_version = headerVersion;
                img.HeaderV0 = hdrFallback;
                break;
        }

        return true;
    }

    private void ParseSections(byte[] data, BootImage img)
    {
        var offset = GetHeaderSize(img);
        var pageSize = GetPageSize(img);

        // Kernel
        img.KernelOffset = offset;
        img.KernelSize = GetKernelSize(img);
        offset += Align(img.KernelSize, pageSize);

        // Ramdisk
        img.RamdiskOffset = offset;
        img.RamdiskSize = GetRamdiskSize(img);
        offset += Align(img.RamdiskSize, pageSize);

        // Second stage
        img.SecondOffset = offset;
        img.SecondSize = GetSecondSize(img);
        offset += Align(img.SecondSize, pageSize);

        // Extra (v0 only)
        if (img.HeaderVersion == 0)
        {
            img.ExtraOffset = offset;
            img.ExtraSize = GetExtraSize(img);
            offset += Align(img.ExtraSize, pageSize);
        }

        // Recovery DTBO (v1-v2)
        if (img.HeaderVersion is 1 or 2)
        {
            img.RecoveryDtboOffset = GetRecoveryDtboOffset(img);
            img.RecoveryDtboSize = GetRecoveryDtboSize(img);
            offset += Align(img.RecoveryDtboSize, pageSize);
        }

        // DTB (v2+, vendor)
        if (img.HeaderVersion >= 2 || img.IsVendor)
        {
            img.DtbOffset = offset;  // File offset, not memory address (dtb_addr)
            img.DtbSize = GetDtbSize(img);
            offset += Align(img.DtbSize, pageSize);
        }

        // Signature (v4)
        if (img.HeaderVersion == 4 && !img.IsVendor)
        {
            img.SignatureOffset = offset;
            img.SignatureSize = GetSignatureSize(img);
            offset += Align(img.SignatureSize, pageSize);
        }

        // Vendor ramdisk table (vendor v4)
        if (img.IsVendor && img.HeaderVersion == 4)
        {
            ParseVendorRamdiskTable(data, img, offset);
        }

        // Payload ends here
        img.PayloadSize = offset;
    }

    private void ParseTail(byte[] data, BootImage img)
    {
        // Tail is everything after payload
        if (data.Length > img.PayloadSize)
        {
            img.TailOffset = img.PayloadSize;
            img.TailSize = (uint)(data.Length - img.PayloadSize);
            img.TailData = data.AsSpan((int)img.TailOffset, (int)img.TailSize).ToArray();

            // Check for SEANDROID
            if (img.TailSize >= 16)
            {
                var seandroidMagic = "SEANDROIDENFORCE"u8.ToArray();
                if (img.TailData.AsSpan(0, 16).SequenceEqual(seandroidMagic))
                {
                    img.Flags.HasSeandroid = true;
                }
            }

            // Check for AVB footer
            FindAvbFooter(img);
        }
    }

    private void FindAvbFooter(BootImage img)
    {
        // AVB footer is at the end of the image
        var magic = "AVB0"u8.ToArray();
        var minOffset = Math.Max(0, img.TailOffset + img.TailSize - 1024);

        for (long i = img.TailOffset + img.TailSize - Marshal.SizeOf<AvbFooter>(); 
             i >= minOffset; i--)
        {
            if (img.RawData.AsSpan((int)i, 4).SequenceEqual(magic))
            {
                img.AvbFooterOffset = i;
                img.AvbFooter = MemoryMarshal.Read<AvbFooter>(
                    img.RawData.AsSpan((int)i));
                
                // Read vbmeta
                var vbmetaStart = img.AvbFooterOffset - (long)img.AvbFooter.vbmeta_offset;
                if (vbmetaStart >= 0)
                {
                    img.VbmetaOffset = (ulong)vbmetaStart;
                    img.Vbmeta = MemoryMarshal.Read<AvbVBMetaImageHeader>(
                        img.RawData.AsSpan((int)vbmetaStart));
                    img.Flags.HasAvb = true;
                }
                break;
            }
        }
    }

    private void ParseVendorRamdiskTable(byte[] data, BootImage img, long offset)
    {
        if (img.HeaderV4Vendor.vendor_ramdisk_table_size == 0) return;

        var tableOffset = offset;
        var entrySize = img.HeaderV4Vendor.vendor_ramdisk_table_entry_size;
        var entryCount = img.HeaderV4Vendor.vendor_ramdisk_table_entry_num;

for (int i = 0; i < entryCount; i++)
            {
                var entry = MemoryMarshal.Read<VendorRamdiskTableEntryV4>(
                    new ReadOnlySpan<byte>(data, checked((int)(tableOffset + i * entrySize)), checked((int)entrySize)));
                img.VendorRamdiskEntries.Add(entry);
            }
    }

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

    private uint GetKernelSize(BootImage img)
    {
        if (img.IsVendor) return 0; // Vendor boot doesn't have separate kernel
        if (img.Flags.IsPxa) return img.HeaderPxa.kernel_size;
        return img.HeaderVersion switch
        {
            0 => img.HeaderV0.kernel_size,
            1 => img.HeaderV1.kernel_size,
            2 => img.HeaderV2.kernel_size,
            3 => img.HeaderV3.kernel_size,
            4 => img.HeaderV4.kernel_size,
            _ => 0
        };
    }

    private uint GetRamdiskSize(BootImage img)
    {
        if (img.IsVendor)
        {
            return img.HeaderVersion == 4 
                ? img.HeaderV4Vendor.ramdisk_size 
                : img.HeaderV3Vendor.ramdisk_size;
        }
        if (img.Flags.IsPxa) return img.HeaderPxa.ramdisk_size;
        return img.HeaderVersion switch
        {
            0 => img.HeaderV0.ramdisk_size,
            1 => img.HeaderV1.ramdisk_size,
            2 => img.HeaderV2.ramdisk_size,
            3 => img.HeaderV3.ramdisk_size,
            4 => img.HeaderV4.ramdisk_size,
            _ => 0
        };
    }

    private uint GetSecondSize(BootImage img)
    {
        if (img.IsVendor || img.HeaderVersion >= 3) return 0;
        if (img.Flags.IsPxa) return img.HeaderPxa.second_size;
        return img.HeaderV0.second_size;
    }

    private uint GetExtraSize(BootImage img)
    {
        if (img.IsVendor || img.HeaderVersion != 0) return 0;
        return img.HeaderV0.header_version; // Union field
    }

    private ulong GetRecoveryDtboOffset(BootImage img)
    {
        if (img.HeaderVersion == 1) return img.HeaderV1.recovery_dtbo_offset;
        if (img.HeaderVersion == 2) return img.HeaderV2.recovery_dtbo_offset;
        return 0;
    }

    private uint GetRecoveryDtboSize(BootImage img)
    {
        if (img.HeaderVersion == 1) return img.HeaderV1.recovery_dtbo_size;
        if (img.HeaderVersion == 2) return img.HeaderV2.recovery_dtbo_size;
        return 0;
    }

    private ulong GetDtbOffset(BootImage img)
    {
        if (img.IsVendor)
        {
            return img.HeaderVersion == 4 
                ? img.HeaderV4Vendor.dtb_addr 
                : img.HeaderV3Vendor.dtb_addr;
        }
        if (img.HeaderVersion == 2) return img.HeaderV2.dtb_addr;
        return 0;
    }

    private uint GetDtbSize(BootImage img)
    {
        if (img.IsVendor)
        {
            return img.HeaderVersion == 4 
                ? img.HeaderV4Vendor.dtb_size 
                : img.HeaderV3Vendor.dtb_size;
        }
        if (img.HeaderVersion == 2) return img.HeaderV2.dtb_size;
        return 0;
    }

    private uint GetSignatureSize(BootImage img)
    {
        if (img.HeaderVersion == 4 && !img.IsVendor)
        {
            return img.HeaderV4.signature_size;
        }
        return 0;
    }

    private static uint Align(uint value, uint alignment)
    {
        if (alignment == 0) return value;
        return (value + alignment - 1) / alignment * alignment;
    }

    #endregion

    #region Extraction Methods

    /// <summary>
    /// Extract kernel from boot image
    /// </summary>
    public byte[] ExtractKernel(BootImage img)
    {
        return img.RawData.AsSpan((int)img.KernelOffset, (int)img.KernelSize).ToArray();
    }

    /// <summary>
    /// Extract ramdisk from boot image
    /// </summary>
    public byte[] ExtractRamdisk(BootImage img)
    {
        return img.RawData.AsSpan((int)img.RamdiskOffset, (int)img.RamdiskSize).ToArray();
    }

/// <summary>
    /// Extract and parse ramdisk as CPIO
    /// </summary>
    public CpioArchive ExtractRamdiskArchive(BootImage img)
    {
        var ramdiskData = ExtractRamdisk(img);
        using var compression = new CompressionEngine();
        var decompressed = compression.Decompress(ramdiskData);
        return CpioArchive.Parse(decompressed);
    }

    /// <summary>
    /// Extract DTB
    /// </summary>
    public byte[] ExtractDtb(BootImage img)
    {
        if (img.DtbSize == 0) return Array.Empty<byte>();
        return img.RawData.AsSpan((int)img.DtbOffset, (int)img.DtbSize).ToArray();
    }

    #endregion
}

/// <summary>
/// DHTB header (MediaTek)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DhtbHdr
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)] public byte[] checksum;
    public uint size;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 460)] public byte[] padding;
}

/// <summary>
/// BLOB header (LG/Qualcomm)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlobHdr
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)] public byte[] secure_magic;
    public uint datalen;
    public uint signature;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] magic;
    public uint hdr_version;
    public uint hdr_size;
    public uint part_offset;
    public uint num_parts;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 28)] public byte[] unknown;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] name;
    public uint offset;
    public uint size;
    public uint version;
}

/// <summary>
/// Parsed boot image representation
/// </summary>
public class BootImage
{
    public byte[] RawData { get; set; } = Array.Empty<byte>();
    public bool IsVendor { get; set; }
    public uint HeaderVersion { get; set; }

    // Headers (only one will be populated based on version/type)
    public BootImgHdrV0 HeaderV0 { get; set; }
    public BootImgHdrV1 HeaderV1 { get; set; }
    public BootImgHdrV2 HeaderV2 { get; set; }
    public BootImgHdrV3 HeaderV3 { get; set; }
    public BootImgHdrV4 HeaderV4 { get; set; }
    public BootImgHdrPxa HeaderPxa { get; set; }
    public VendorBootImgHdrV3 HeaderV3Vendor { get; set; }
    public VendorBootImgHdrV4 HeaderV4Vendor { get; set; }

    // Section offsets and sizes
    public long KernelOffset { get; set; }
    public uint KernelSize { get; set; }
    public long RamdiskOffset { get; set; }
    public uint RamdiskSize { get; set; }
    public long SecondOffset { get; set; }
    public uint SecondSize { get; set; }
    public long ExtraOffset { get; set; }
    public uint ExtraSize { get; set; }
    public ulong RecoveryDtboOffset { get; set; }
    public uint RecoveryDtboSize { get; set; }
    public ulong DtbOffset { get; set; }
    public uint DtbSize { get; set; }
    public long SignatureOffset { get; set; }
    public uint SignatureSize { get; set; }
    public long PayloadSize { get; set; }

    // Tail section
    public long TailOffset { get; set; }
    public uint TailSize { get; set; }
    public byte[] TailData { get; set; } = Array.Empty<byte>();

    // AVB
    public long AvbFooterOffset { get; set; }
    public AvbFooter AvbFooter { get; set; }
    public ulong VbmetaOffset { get; set; }
    public AvbVBMetaImageHeader Vbmeta { get; set; }

    // Vendor ramdisk (v4)
    public List<VendorRamdiskTableEntryV4> VendorRamdiskEntries { get; set; } = new();

    // Flags
    public BootImageFlags Flags { get; set; } = new();
}

/// <summary>
/// Boot image flags
/// </summary>
public class BootImageFlags
{
    public bool HasDhtb { get; set; }
    public bool HasBlob { get; set; }
    public bool HasChromeos { get; set; }
    public bool HasSeandroid { get; set; }
    public bool HasAvb { get; set; }
    public bool IsPxa { get; set; }
}
