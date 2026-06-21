using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System;
using BACKRabbit.MagiskCore.Structures.Avb;

namespace BACKRabbit.MagiskCore.AvbRestorer;

/// <summary>
/// AVB/VBMeta restoration service
/// Restores Android Verified Boot flags from Magisk-patched state (3) to stock (0)
/// 
/// From Magisk source: When Magisk patches boot images, it sets:
/// - flags = 3 (disable-verity | disable-verification)
/// 
/// To uninstall Magisk properly, we must restore:
/// - flags = 0 (verification enabled)
/// 
/// Note: This does NOT restore tripped Knox eFuse - that is permanent on Samsung devices
/// </summary>
public class AvbRestorer
{
    // AVB flags: 3 = disable-verity + disable-verification (Magisk patched)
    //            0 = verification enabled (stock)
    private const uint AVB_FLAGS_DISABLED = 3;
    private const uint AVB_FLAGS_ENABLED = 0;
    private const int FLAGS_OFFSET = 88;  // Offset of flags field in AvbVBMetaImageHeader

    /// <summary>
    /// Restore AVB verification flags in a boot image
    /// </summary>
    /// <param name="bootImage">Raw boot image data</param>
    /// <returns>AvbRestoreResult with patched image if successful</returns>
    public AvbRestoreResult RestoreVerificationFlags(byte[] bootImage)
    {
        var result = new AvbRestoreResult();

        // Find AVB footer at end of image
        var footerOffset = FindAvbFooter(bootImage);
        if (footerOffset < 0)
        {
            result.Success = false;
            result.Message = "No AVB footer found - image may not be AVB signed";
            return result;
        }

        result.FooterFound = true;
        result.FooterOffset = footerOffset;

        // Read footer manually (AvbFooter contains byte[] arrays, incompatible with MemoryMarshal)
        var footerSpan = new ReadOnlySpan<byte>(bootImage, (int)footerOffset, checked((int)(bootImage.Length - footerOffset)));
        // AvbFooter layout: magic(4)@0 + version_major(4)@4 + version_minor(4)@8
        //   + original_image_size(8)@12 + vbmeta_offset(8)@20 + vbmeta_size(8)@28 + reserved(28)@36 = 64 bytes
        var originalImageSize = BinaryPrimitives.ReadUInt64LittleEndian(footerSpan.Slice(12, 8));
        var vbmetaOffset = BinaryPrimitives.ReadUInt64LittleEndian(footerSpan.Slice(20, 8));
        var vbmetaSize = BinaryPrimitives.ReadUInt64LittleEndian(footerSpan.Slice(28, 8));
        result.OriginalImageSize = originalImageSize;

        // vbmeta_offset is ABSOLUTE from partition start (per AOSP avb_footer.h spec)
        // Reference: Magisk commit c11ccba — void *meta = hdr_addr + __builtin_bswap64(avb_footer->vbmeta_offset)
        var vbmetaStart = (long)vbmetaOffset;
        if (vbmetaStart < 0 || vbmetaStart >= bootImage.Length)
        {
            result.Success = false;
            result.Message = $"Invalid vbmeta offset: {vbmetaOffset} (image size: {bootImage.Length})";
            return result;
        }

        result.VbmetaOffset = (ulong)vbmetaStart;
        result.VbmetaSize = vbmetaSize;

        // Read vbmeta header manually (AvbVBMetaImageHeader contains byte[] arrays)
        var vbmetaSpan = new ReadOnlySpan<byte>(bootImage, (int)vbmetaStart, Math.Min(256, bootImage.Length - (int)vbmetaStart));
        // flags field is at offset 88 in AvbVBMetaImageHeader (after magic(4) + 10 uint64 fields + 2 uint32 fields)
        var currentFlags = BinaryPrimitives.ReadUInt32LittleEndian(vbmetaSpan.Slice(88, 4));
        result.CurrentFlags = currentFlags;

        // Patch flags if disabled
        if (currentFlags == AVB_FLAGS_DISABLED)
        {
            var mutable = new byte[bootImage.Length];
            Array.Copy(bootImage, 0, mutable, 0, bootImage.Length);

            // Write new flags (0 = enabled) at offset 88 (flags field position)
            BinaryPrimitives.WriteUInt32LittleEndian(
                mutable.AsSpan((int)vbmetaStart + FLAGS_OFFSET), 
                AVB_FLAGS_ENABLED);

            result.Success = true;
            result.Message = "AVB flags restored from 3 (disabled) to 0 (enabled)";
            result.PatchedImage = mutable;
            result.FlagsChanged = true;

            return result;
        }
        else if (currentFlags == AVB_FLAGS_ENABLED)
        {
            result.Success = true;
            result.Message = "AVB flags already enabled (0) - no patching needed";
            result.PatchedImage = bootImage;
            result.FlagsChanged = false;
            return result;
        }
        else
        {
            result.Success = false;
            result.Message = $"Unknown AVB flags value: {currentFlags} (expected 0 or 3)";
            return result;
        }
    }

    /// <summary>
    /// Find AVB footer by searching for "AVB0" magic near end of image
    /// </summary>
    private long FindAvbFooter(byte[] data)
    {
        var magic = "AVB0"u8.ToArray();
        var footerSize = Marshal.SizeOf<AvbFooter>();
        var minOffset = Math.Max(0, data.Length - 1024);  // Footer within last 1KB

        for (int i = data.Length - footerSize; i >= minOffset; i--)
        {
            if (new ReadOnlySpan<byte>(data, i, 4).SequenceEqual(magic))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Patch vbmeta partition directly (for separate vbmeta partition)
    /// </summary>
    public AvbRestoreResult PatchVbmetaPartition(byte[] vbmetaData)
    {
        var result = new AvbRestoreResult();

        if (vbmetaData.Length < AvbVBMetaImageHeader.SIZE)
        {
            result.Success = false;
            result.Message = "vbmeta data too small";
            return result;
        }

        // Check magic
        var magic = new ReadOnlySpan<byte>(vbmetaData, 0, 4);
        if (!magic.SequenceEqual("AVB0"u8.ToArray()))
        {
            result.Success = false;
            result.Message = "Invalid vbmeta magic";
            return result;
        }

        // Read flags at offset 88
        var currentFlags = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(vbmetaData, FLAGS_OFFSET, 4));
        result.CurrentFlags = currentFlags;

        if (currentFlags == AVB_FLAGS_DISABLED)
        {
            var mutable = new byte[vbmetaData.Length];
            Array.Copy(vbmetaData, 0, mutable, 0, vbmetaData.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(mutable, FLAGS_OFFSET, 4), AVB_FLAGS_ENABLED);
            
            result.Success = true;
            result.Message = "vbmeta flags patched";
            result.PatchedImage = mutable;
            result.FlagsChanged = true;
            return result;
        }

        result.Success = true;
        result.Message = "vbmeta flags already enabled";
        result.PatchedImage = vbmetaData;
        result.FlagsChanged = false;
        return result;
    }
}

/// <summary>
/// Result of AVB restoration operation
/// </summary>
public class AvbRestoreResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool FooterFound { get; set; }
    public long FooterOffset { get; set; }
    public ulong OriginalImageSize { get; set; }
    public ulong VbmetaOffset { get; set; }
    public ulong VbmetaSize { get; set; }
    public uint CurrentFlags { get; set; }
    public bool FlagsChanged { get; set; }
    public byte[]? PatchedImage { get; set; }
}