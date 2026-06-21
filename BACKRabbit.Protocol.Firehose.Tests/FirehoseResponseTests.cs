using BACKRabbit.Protocol.Firehose;
using System.Text;
using Xunit;

namespace BACKRabbit.Protocol.Firehose.Tests;

public class FirehoseResponseTests
{
    [Fact]
    public void Parse_AckResponse_DetectsAck()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><data><response value=\"ACK\" rawmode=\"false\"/></data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        Assert.True(response.IsAck);
        Assert.False(response.IsNak);
        Assert.Single(response.Fragments);
        Assert.Equal("ACK", response.Fragments[0].RawValue);
    }

    [Fact]
    public void Parse_NakResponse_DetectsNak()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><data><response value=\"NAK\" rawmode=\"false\"/></data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        Assert.False(response.IsAck);
        Assert.True(response.IsNak);
    }

    [Fact]
    public void Parse_MixedLogAndResponse_ExtractsAll()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?><data><log value=""Programming sector 0""/></data>
<?xml version=""1.0"" encoding=""UTF-8""?><data><response value=""ACK"" rawmode=""false""/></data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        Assert.Equal(2, response.Fragments.Count);
        Assert.Equal("Programming sector 0", response.Fragments[0].LogValue);
        Assert.True(response.Fragments[1].IsAck);
        Assert.True(response.IsAck); // Last fragment is ACK
    }

    [Fact]
    public void Parse_RawGarbage_StoredAsLog()
    {
        var garbage = "INFO: Some random UART output\r\n";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(garbage));

        Assert.Single(response.Fragments);
        Assert.Equal("raw", response.Fragments[0].TagName);
        Assert.Contains("INFO:", response.Fragments[0].LogValue);
    }
}

public class FirehoseConfigurationTests
{
    [Fact]
    public void DefaultsToUfs()
    {
        var config = new FirehoseConfiguration();
        Assert.Equal("ufs", config.MemoryName);
    }

    [Fact]
    public void ToXml_GeneratesValidXml()
    {
        var config = new FirehoseConfiguration
        {
            MemoryName = "emmc",
            ZlpAwareHost = "1",
            MaxPayloadSizeToTargetInBytes = "32768",
        };

        var xml = config.ToXml();
        Assert.Contains("MemoryName=\"emmc\"", xml);
        Assert.Contains("ZlpAwareHost=\"1\"", xml);
        Assert.Contains("<configure", xml);
        Assert.Contains("<?xml", xml);
    }
}

public class GptPartitionEntryTests
{
    [Fact]
    public void ToString_ReturnsFormatted()
    {
        var entry = new GptPartitionEntry
        {
            Name = "boot_a",
            StartSector = 2048,
            Sectors = 65536,
        };
        var str = entry.ToString();
        Assert.Contains("boot_a", str);
        Assert.Contains("2048", str);
        Assert.Contains("65536", str);
    }
}