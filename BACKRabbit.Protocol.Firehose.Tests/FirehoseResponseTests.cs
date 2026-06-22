using BACKRabbit.Protocol.Firehose;
using System.Linq;
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

    [Fact]
    public void Parse_GptDump_WithNameAttribute_ExtractsOffsets()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?><data>
  <partition name=""boot_a"" start_sector=""2048"" num_partition_sectors=""65536"" partition_guid=""12345678-1234-1234-1234-123456789abc""/>
  <partition name=""system_a"" start_sector=""67584"" num_partition_sectors=""1048576""/>
  <response value=""ACK"" rawmode=""false""/>
</data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        Assert.True(response.IsAck);
        var partitions = response.Fragments.Where(f => f.TagName == "partition").ToList();
        Assert.Equal(2, partitions.Count);

        Assert.Equal("boot_a", partitions[0].RawValue);
        Assert.Equal(2048ul, partitions[0].StartSector);
        Assert.Equal(65536ul, partitions[0].Sectors);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", partitions[0].PartitionGuid);

        Assert.Equal("system_a", partitions[1].RawValue);
        Assert.Equal(67584ul, partitions[1].StartSector);
        Assert.Equal(1048576ul, partitions[1].Sectors);
    }

    [Fact]
    public void Parse_GptDump_WithPartitionNameAttribute_ExtractsOffsets()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?><data>
  <partition partition_name=""boot_b"" start_sector=""69632"" num_partition_sectors=""65536"" type=""aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee""/>
  <response value=""ACK"" rawmode=""false""/>
</data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        var part = response.Fragments.Single(f => f.TagName == "partition");
        Assert.Equal("boot_b", part.RawValue);
        Assert.Equal(69632ul, part.StartSector);
        Assert.Equal(65536ul, part.Sectors);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", part.PartitionGuid);
        Assert.True(response.IsAck);
    }

    [Fact]
    public void Parse_GptDump_LastSector_ComputesSectors()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?><data>
  <partition name=""userdata"" start_sector=""2097152"" last_sector=""8388607""/>
  <response value=""ACK"" rawmode=""false""/>
</data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        var part = response.Fragments.Single(f => f.TagName == "partition");
        Assert.Equal("userdata", part.RawValue);
        Assert.Equal(2097152ul, part.StartSector);
        Assert.Equal(6291456ul, part.Sectors); // 8388607 - 2097152 + 1
    }

    [Fact]
    public void Parse_GptDump_SizeInKb_ConvertsToSectors()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?><data>
  <partition name=""modem_a"" start_sector=""0"" size_in_kb=""262144""/>
  <response value=""ACK"" rawmode=""false""/>
</data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        var part = response.Fragments.Single(f => f.TagName == "partition");
        Assert.Equal("modem_a", part.RawValue);
        Assert.Equal(524288ul, part.Sectors); // 262144 KB * 2 sectors/KB at 512B sectors
    }

    [Fact]
    public void Parse_GptDump_CaseInsensitiveAttributes_Accepted()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?><data>
  <partition NAME=""recovery"" START_SECTOR=""4096"" NUM_PARTITION_SECTORS=""32768"" PARTITION_GUID=""ffffffff-ffff-ffff-ffff-ffffffffffff""/>
  <response value=""ACK"" rawmode=""false""/>
</data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        var part = response.Fragments.Single(f => f.TagName == "partition");
        Assert.Equal("recovery", part.RawValue);
        Assert.Equal(4096ul, part.StartSector);
        Assert.Equal(32768ul, part.Sectors);
        Assert.Equal("ffffffff-ffff-ffff-ffff-ffffffffffff", part.PartitionGuid);
    }

    [Fact]
    public void Parse_GptDump_MixedFragments_PreservesAck()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?><data><log value=""Reading GPT header""/></data>
<?xml version=""1.0"" encoding=""UTF-8""?><data>
  <partition name=""boot_a"" start_sector=""2048"" num_partition_sectors=""65536""/>
</data>
<?xml version=""1.0"" encoding=""UTF-8""?><data><response value=""ACK"" rawmode=""false""/></data>";
        var response = FirehoseResponse.Parse(Encoding.UTF8.GetBytes(xml));

        Assert.True(response.IsAck);
        Assert.Equal(3, response.Fragments.Count);
        Assert.Equal("Reading GPT header", response.Fragments[0].LogValue);
        Assert.Equal("boot_a", response.Fragments[1].RawValue);
        Assert.Equal(2048ul, response.Fragments[1].StartSector);
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