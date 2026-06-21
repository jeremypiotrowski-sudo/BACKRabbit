using System.Runtime.InteropServices;

namespace BACKRabbit.MagiskCore.Structures.Avb;

/// <summary>
/// AVB Footer - Found at the end of boot images with AVB
/// Matches: struct AvbFooter from Magisk bootimg.hpp
/// Source: https://android.googlesource.com/platform/external/avb/+/refs/heads/android11-release/libavb/avb_footer.h
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AvbFooter
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] magic;           // "AVB0"
    public uint version_major;
    public uint version_minor;
    public ulong original_image_size;     // Size of the original boot image
    public ulong vbmeta_offset;           // Offset to vbmeta header
    public ulong vbmeta_size;             // Size of vbmeta data
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 28)] public byte[] reserved;

    public const string MAGIC = "AVB0";
    public const int FOOTER_MAGIC_LEN = 4;
    public const int SIZE = 64;  // 4+4+4+8+8+8+28 = 64 bytes
}

/// <summary>
/// AVB VBMeta Image Header - Contains verification metadata
/// Matches: struct AvbVBMetaImageHeader from Magisk bootimg.hpp
/// Source: https://android.googlesource.com/platform/external/avb/+/refs/heads/android11-release/libavb/avb_vbmeta_image.h
/// 
/// Critical field: flags
/// - 0 = verification enabled (stock)
/// - 3 = disable-verity + disable-verification (Magisk patched)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AvbVBMetaImageHeader
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] magic;                        // "AVB0"
    public uint required_libavb_version_major;
    public uint required_libavb_version_minor;
    public ulong authentication_data_block_size;
    public ulong auxiliary_data_block_size;
    public uint algorithm_type;
    public ulong hash_offset;
    public ulong hash_size;
    public ulong signature_offset;
    public ulong signature_size;
    public ulong public_key_offset;
    public ulong public_key_size;
    public ulong public_key_metadata_offset;
    public ulong public_key_metadata_size;
    public ulong descriptors_offset;
    public ulong descriptors_size;
    public ulong rollback_index;
    public uint flags;                        // CRITICAL: 0=enabled, 3=disabled (Magisk)
    public uint rollback_index_location;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] release_string;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)] public byte[] reserved;

    public const string MAGIC = "AVB0";
    public const int AVB_MAGIC_LEN = 4;
    public const int RELEASE_STRING_SIZE = 48;
    public const int SIZE = 256;  // Total header size
    
    // AVB Flags values
    public const uint FLAGS_ENABLED = 0;              // Stock - verification enabled
    public const uint FLAGS_DISABLE_VERITY = 1;       // Magisk patched
    public const uint FLAGS_DISABLE_VERIFICATION = 2; // Magisk patched  
    public const uint FLAGS_DISABLED = 3;             // Magisk patched (1|2)
}

/// <summary>
/// AVB Algorithm Types - Used in vbmeta header
/// </summary>
public enum AvbAlgorithmType : uint
{
    AVB_ALGORITHM_TYPE_NONE = 0,
    AVB_ALGORITHM_TYPE_SHA256_RSA2048 = 1,
    AVB_ALGORITHM_TYPE_SHA512_RSA2048 = 2,
    AVB_ALGORITHM_TYPE_SHA256_RSA4096 = 3,
    AVB_ALGORITHM_TYPE_SHA512_RSA4096 = 4,
    AVB_ALGORITHM_TYPE_SHA256_RSA8192 = 5,
    AVB_ALGORITHM_TYPE_SHA512_RSA8192 = 6,
}

/// <summary>
/// AVB Descriptor Types - Found in descriptors section
/// </summary>
public enum AvbDescriptorType : ulong
{
    AVB_DESCRIPTOR_TYPE_PROPERTY = 0,
    AVB_DESCRIPTOR_TYPE_HASH = 1,
}

/// <summary>
/// AVB Property Descriptor - Describes a partition property
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AvbPropertyDescriptor
{
    public ulong type;                    // AVB_DESCRIPTOR_TYPE_PROPERTY
    public ulong partition_name_len;
    public ulong key_len;
    public ulong value_len;
    public ulong flags;
    // Followed by: partition_name (null-terminated), key (null-terminated), value (null-terminated)
}

/// <summary>
/// AVB Hash Descriptor - Describes a partition hash for verification
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AvbHashDescriptor
{
    public ulong type;                    // AVB_DESCRIPTOR_TYPE_HASH
    public ulong partition_name_len;
    public ulong hash_offset;
    public ulong hash_size;
    public ulong image_size;
    public ulong flags;
    // Followed by: partition_name (null-terminated), hash data
}