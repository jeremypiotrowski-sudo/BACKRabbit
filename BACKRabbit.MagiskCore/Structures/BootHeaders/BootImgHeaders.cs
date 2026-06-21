using System.Runtime.InteropServices;

namespace BACKRabbit.MagiskCore.Structures.BootHeaders;

/// <summary>
/// AOSP Boot Header V0 - Original Android boot image format
/// Matches: struct boot_img_hdr_v0 from Magisk bootimg.hpp
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct BootImgHdrV0
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;           // "ANDROID!"
    public uint kernel_size;      // size in bytes
    public uint kernel_addr;      // physical load addr
    public uint ramdisk_size;     // size in bytes
    public uint ramdisk_addr;     // physical load addr
    public uint second_size;      // size in bytes
    public uint second_addr;      // physical load addr
    public uint tags_addr;        // physical addr for kernel tags
    public uint page_size;        // flash page size we assume
    public uint header_version;   // Union with extra_size - header version (0)
    public uint os_version;       // OS version encoding
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] name;  // asciiz product name
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public byte[] cmdline;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] id;  // timestamp/checksum/sha1
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] extra_cmdline;

    public const string MAGIC = "ANDROID!";
    public const int HEADER_SIZE = 1632;
}

/// <summary>
/// AOSP Boot Header V1 - Added recovery_dtbo support
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct BootImgHdrV1
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;
    public uint kernel_size;
    public uint kernel_addr;
    public uint ramdisk_size;
    public uint ramdisk_addr;
    public uint second_size;
    public uint second_addr;
    public uint tags_addr;
    public uint page_size;
    public uint header_version;   // = 1
    public uint os_version;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] name;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public byte[] cmdline;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] id;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] extra_cmdline;
    public uint recovery_dtbo_size;     // size in bytes for recovery DTBO/ACPIO image
    public ulong recovery_dtbo_offset;  // offset to recovery dtbo/acpio in boot image
    public uint header_size;

    public const int HEADER_SIZE = 1664;
}

/// <summary>
/// AOSP Boot Header V2 - Added DTB support
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct BootImgHdrV2
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;
    public uint kernel_size;
    public uint kernel_addr;
    public uint ramdisk_size;
    public uint ramdisk_addr;
    public uint second_size;
    public uint second_addr;
    public uint tags_addr;
    public uint page_size;
    public uint header_version;   // = 2
    public uint os_version;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] name;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public byte[] cmdline;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] id;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] extra_cmdline;
    public uint recovery_dtbo_size;
    public ulong recovery_dtbo_offset;
    public uint header_size;
    public uint dtb_size;   // size in bytes for DTB image
    public ulong dtb_addr;  // physical load address for DTB image

    public const int HEADER_SIZE = 1696;
}

/// <summary>
/// AOSP Boot Header V3 - New format with fixed 4096 header size, no page_size field
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct BootImgHdrV3
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;
    public uint kernel_size;
    public uint ramdisk_size;
    public uint os_version;
    public uint header_size;      // = 2112
    public uint reserved_0;
    public uint reserved_1;
    public uint reserved_2;
    public uint reserved_3;
    public uint header_version;   // = 3
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1536)] public byte[] cmdline;  // BOOT_ARGS + EXTRA_ARGS

    public const int HEADER_SIZE = 2112;
    public const uint FIXED_PAGE_SIZE = 4096;
}

/// <summary>
/// AOSP Boot Header V4 - Added signature support (GKI 2.0)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct BootImgHdrV4
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;
    public uint kernel_size;
    public uint ramdisk_size;
    public uint os_version;
    public uint header_size;      // = 2112
    public uint reserved_0;
    public uint reserved_1;
    public uint reserved_2;
    public uint reserved_3;
    public uint header_version;   // = 4
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1536)] public byte[] cmdline;
    public uint signature_size;   // size in bytes for boot signature

    public const int HEADER_SIZE = 2116;
    public const uint FIXED_PAGE_SIZE = 4096;
}

/// <summary>
/// Samsung PXA Boot Header - Special Samsung header detected by page_size >= 0x02000000
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct BootImgHdrPxa
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;
    public uint kernel_size;
    public uint kernel_addr;
    public uint ramdisk_size;
    public uint ramdisk_addr;
    public uint second_size;
    public uint second_addr;
    public uint extra_size;       // Instead of tags_addr
    public uint unknown;
    public uint tags_addr;
    public uint page_size;        // >= 0x02000000 for PXA
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)] public byte[] name;  // 24 bytes, not 16
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public byte[] cmdline;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] id;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] extra_cmdline;

    public const uint PXA_PAGE_SIZE_THRESHOLD = 0x02000000;
}

/// <summary>
/// Vendor Boot Header V3 (GKI 2.0 - init_boot partition)
/// Magic: "VNDRBOOT"
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct VendorBootImgHdrV3
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;  // "VNDRBOOT"
    public uint header_version;   // = 3
    public uint page_size;
    public uint kernel_addr;
    public uint ramdisk_addr;
    public uint ramdisk_size;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2048)] public byte[] cmdline;  // VENDOR_BOOT_ARGS_SIZE
    public uint tags_addr;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] name;
    public uint header_size;
    public uint dtb_size;
    public ulong dtb_addr;

    public const string MAGIC = "VNDRBOOT";
}

/// <summary>
/// Vendor Boot Header V4 (GKI 2.0 - init_boot partition with multiple ramdisks)
/// Adds vendor_ramdisk_table for multiple ramdisk support
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct VendorBootImgHdrV4
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] magic;  // "VNDRBOOT"
    public uint header_version;   // = 4
    public uint page_size;
    public uint kernel_addr;
    public uint ramdisk_addr;
    public uint ramdisk_size;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2048)] public byte[] cmdline;
    public uint tags_addr;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] name;
    public uint header_size;
    public uint dtb_size;
    public ulong dtb_addr;
    public uint vendor_ramdisk_table_size;        // size in bytes for the vendor ramdisk table
    public uint vendor_ramdisk_table_entry_num;   // number of entries in the vendor ramdisk table
    public uint vendor_ramdisk_table_entry_size;  // size in bytes for a vendor ramdisk table entry
    public uint bootconfig_size;  // size in bytes for the bootconfig section

    public const string MAGIC = "VNDRBOOT";
}

/// <summary>
/// Vendor Ramdisk Table Entry V4 - Used for multiple ramdisks in vendor boot v4
/// Each entry describes a ramdisk with type, name, and board ID
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct VendorRamdiskTableEntryV4
{
    public uint ramdisk_size;   // size in bytes for the ramdisk image
    public uint ramdisk_offset; // offset to the ramdisk image in vendor ramdisk section
    public uint ramdisk_type;   // 0=none, 1=platform, 2=recovery, 3=dlkm
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] ramdisk_name;  // asciiz ramdisk name
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public uint[] board_id;  // hardware identifiers

    public const int SIZE = 116;  // 4+4+4+32+64 = 108... actually 4*29=116
    public const uint TYPE_NONE = 0;
    public const uint TYPE_PLATFORM = 1;
    public const uint TYPE_RECOVERY = 2;
    public const uint TYPE_DLKM = 3;
}