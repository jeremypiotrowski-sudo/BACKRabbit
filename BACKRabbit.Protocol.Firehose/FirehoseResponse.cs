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

    public override string ToString() =>
        $"<{TagName}> value='{RawValue ?? LogValue}' ack={IsAck} nak={IsNak}";
}