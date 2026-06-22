using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace BACKRabbit.Protocol.Firehose;

/// <summary>
/// Parses the mixed XML + log packet stream that a Firehose programmer returns.
/// Firehose responses are NOT a single clean XML document — they are a series of
/// XML fragments (each starting with <?xml ...>) interspersed with log packets.
/// </summary>
public class FirehoseResponse
{
    public List<FirehoseResponseFragment> Fragments { get; } = new();

    public bool IsAck => Fragments.Count > 0 && Fragments[^1].IsAck;
    public bool IsNak => Fragments.Count > 0 && Fragments[^1].IsNak;
    public string? LastRawValue => Fragments.Count > 0 ? Fragments[^1].RawValue : null;
    public string? LastLogValue => Fragments.Count > 0 ? Fragments[^1].LogValue : null;

    public static FirehoseResponse Parse(byte[] rawData)
    {
        var text = Encoding.UTF8.GetString(rawData).Trim('\0', '\r', '\n', ' ');
        var response = new FirehoseResponse();

        // Split on XML declaration boundaries — each fragment begins with <?xml
        var parts = text.Split("<?xml", StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var xmlText = "<?xml" + part;
            // Trim anything after the last '>' to avoid trailing garbage that breaks XDocument.Parse
            var lastClose = xmlText.LastIndexOf('>');
            if (lastClose > 0) xmlText = xmlText[..(lastClose + 1)];

            try
            {
                var doc = XDocument.Parse(xmlText);
                var dataElement = doc.Root;
                if (dataElement == null) continue;

                foreach (var child in dataElement.Elements())
                {
                    var fragment = new FirehoseResponseFragment
                    {
                        TagName = child.Name.LocalName,
                        RawValue = child.Attribute("value")?.Value,
                    };

                    if (child.Name.LocalName == "response")
                    {
                        fragment.IsAck = fragment.RawValue == "ACK";
                        fragment.IsNak = fragment.RawValue == "NAK";
                    }
                    else if (child.Name.LocalName == "log")
                    {
                        fragment.LogValue = fragment.RawValue;
                    }
                    else if (child.Name.LocalName == "partition")
                    {
                        // Real Firehose GPT dumps expose partition metadata as attributes.
                        // Different loaders use different attribute names, so we probe the
                        // most common ones and never leave offsets/GUIDs at default values.
                        fragment.RawValue = GetAttribute(child,
                            "name",
                            "partition_name",
                            "label",
                            "value");

                        if (ulong.TryParse(GetAttribute(child, "start_sector", "start_lba") ?? "", out var startSector))
                            fragment.StartSector = startSector;

                        fragment.Sectors = ParsePartitionSectors(child, fragment.StartSector);

                        fragment.PartitionGuid = GetAttribute(child,
                            "partition_guid",
                            "type",
                            "guid");
                    }

                    response.Fragments.Add(fragment);
                }
            }
            catch
            {
                // Non-XML garbage — store as a raw log fragment so callers can inspect it
                response.Fragments.Add(new FirehoseResponseFragment
                {
                    TagName = "raw",
                    LogValue = part.Trim(),
                });
            }
        }

        return response;
    }

    /// <summary>
    /// Case-insensitive attribute lookup that tries several common names.
    /// Returns the first non-empty value, or null if none are present.
    /// </summary>
    private static string? GetAttribute(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var attr = element.Attribute(name)
                ?? element.Attributes().FirstOrDefault(a =>
                    string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            if (attr != null && !string.IsNullOrEmpty(attr.Value))
                return attr.Value;
        }
        return null;
    }

    /// <summary>
    /// Resolve a partition's sector count from the most common Firehose attribute forms.
    /// </summary>
    private static ulong ParsePartitionSectors(XElement partition, ulong startSector)
    {
        // Preferred explicit sector count.
        var sectorValue = GetAttribute(partition, "num_partition_sectors", "sectors", "size_in_sector");
        if (ulong.TryParse(sectorValue ?? "", out var sectors) && sectors > 0)
            return sectors;

        // Some loaders report last_sector inclusive.
        if (ulong.TryParse(GetAttribute(partition, "last_sector", "end_sector") ?? "", out var lastSector)
            && lastSector >= startSector)
        {
            return lastSector - startSector + 1;
        }

        // size_in_kb is sometimes the only size hint (1KB = 2 sectors at 512B).
        if (ulong.TryParse(GetAttribute(partition, "size_in_kb") ?? "", out var sizeInKb) && sizeInKb > 0)
            return sizeInKb * 2;

        return 0;
    }

    public override string ToString() =>
        $"FirehoseResponse(Ack={IsAck}, Nak={IsNak}, Fragments={Fragments.Count})";
}

public class FirehoseResponseFragment
{
    public string TagName { get; set; } = "";
    public string? RawValue { get; set; }
    public string? LogValue { get; set; }
    public bool IsAck { get; set; }
    public bool IsNak { get; set; }

    // GPT partition attributes (populated for <partition> fragments)
    public ulong StartSector { get; set; }
    public ulong Sectors { get; set; }
    public string? PartitionGuid { get; set; }

    public override string ToString() =>
        $"<{TagName}> value='{RawValue ?? LogValue}' ack={IsAck} nak={IsNak}";
}
