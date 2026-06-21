using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BACKRabbit.Protocol.Firehose;

public class LoaderEntry
{
    public string FilePath { get; set; } = "";
    public uint MsmId { get; set; }
    public byte[]? PkHash { get; set; }
    public string? Description { get; set; }

    public static LoaderEntry? FromFile(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var parts = name.Split("_");
        if (parts.Length < 1) return null;
        if (!uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out var msmId))
            return null;
        byte[]? pkHash = null;
        if (parts.Length >= 2 && parts[1].Length == 16)
            pkHash = Convert.FromHexString(parts[1]);
        return new LoaderEntry { FilePath = filePath, MsmId = msmId, PkHash = pkHash, Description = parts.Length >= 3 ? string.Join("_", parts[2..]) : null };
    }

    public override string ToString() => $"Loader(MSM=0x{MsmId:X8}, Path={FilePath})";
}

public class LoaderDatabase
{
    private readonly List<LoaderEntry> _entries = new();

    public static LoaderDatabase FromDirectory(string loadersDir)
    {
        var db = new LoaderDatabase();
        foreach (var file in Directory.GetFiles(loadersDir, "*.*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".bin" or ".elf" or ".mbn" or ".melf")
            {
                var entry = LoaderEntry.FromFile(file);
                if (entry != null) db._entries.Add(entry);
            }
        }
        return db;
    }

    public LoaderEntry? FindLoader(SaharaChipInfo chipInfo)
    {
        var exact = _entries.FirstOrDefault(e => e.MsmId == chipInfo.MsmId && e.PkHash != null && chipInfo.PkHash != null && e.PkHash.SequenceEqual(chipInfo.PkHash));
        if (exact != null) return exact;
        var msmMatch = _entries.FirstOrDefault(e => e.MsmId == chipInfo.MsmId);
        if (msmMatch != null) return msmMatch;
        if (!chipInfo.IsFused) return _entries.FirstOrDefault();
        return null;
    }

    public IReadOnlyList<LoaderEntry> Entries => _entries.AsReadOnly();
}
