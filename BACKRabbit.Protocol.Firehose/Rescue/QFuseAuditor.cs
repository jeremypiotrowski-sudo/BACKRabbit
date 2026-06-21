using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose.Rescue;

public class QFuseDefinition
{
    public string FuseName { get; set; } = "";
    public uint Address { get; set; }
    public int BitNumber { get; set; }
    public string Description { get; set; } = "";
    public string ImplicationIfBlown { get; set; } = "";
}

public static class QFuseDatabase
{
    // SoC model -> list of fuse definitions
    // Addresses sourced from Qualcomm Linux Security Guide (80-70020-11) and
    // public Qualcomm reference manuals. These are QFPROM register offsets
    // relative to the QFPROM base (typically 0x00780000 on SDM845, varies by SoC).
    // The actual base address is SoC-specific; PeekAsync uses absolute addresses.

    private static readonly Dictionary<string, List<QFuseDefinition>> _db = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SDM845"] = new()
        {
            new() { FuseName = "OEM_SECURE_BOOT1_AUTH_EN", Address = 0x00780020, BitNumber = 0, Description = "Secure boot authentication enabled", ImplicationIfBlown = "Device will only boot signed images. Cannot load unsigned code." },
            new() { FuseName = "OEM_SECURE_BOOT1_PK_HASH_IN_FUSE", Address = 0x00780020, BitNumber = 1, Description = "Root certificate hash stored in fuses", ImplicationIfBlown = "Root of trust permanently set. Cannot change signing authority." },
            new() { FuseName = "ANTI_ROLLBACK_FEATURE_EN[0]", Address = 0x00780024, BitNumber = 0, Description = "Boot image anti-rollback enabled", ImplicationIfBlown = "Cannot downgrade to older firmware versions. Attacker may have incremented rollback index." },
            new() { FuseName = "ANTI_ROLLBACK_FEATURE_EN[1]", Address = 0x00780024, BitNumber = 1, Description = "TrustZone anti-rollback enabled", ImplicationIfBlown = "Cannot downgrade TZ. Permanent version lock." },
            new() { FuseName = "APPS_DBGEN_DISABLE", Address = 0x00780028, BitNumber = 0, Description = "JTAG debug disabled", ImplicationIfBlown = "Hardware JTAG permanently disabled. Cannot debug at hardware level." },
            new() { FuseName = "APPS_NIDEN_DISABLE", Address = 0x00780028, BitNumber = 1, Description = "Non-invasive debug disabled", ImplicationIfBlown = "Trace and performance monitoring disabled." },
            new() { FuseName = "SHARED_QSEE_SPIDEN_DISABLE", Address = 0x0078002C, BitNumber = 0, Description = "TrustZone secure invasive debug disabled", ImplicationIfBlown = "Cannot debug TrustZone. Hardware key extraction blocked." },
            new() { FuseName = "SKDK_READ_DISABLE", Address = 0x00780030, BitNumber = 0, Description = "Secure Key Derivation Key read disabled", ImplicationIfBlown = "Hardware encryption key permanently locked. Cannot extract or change." },
            new() { FuseName = "WDOG_EN", Address = 0x00780034, BitNumber = 0, Description = "Watchdog disable GPIO locked", ImplicationIfBlown = "Watchdog timer configuration locked. Cannot disable hardware watchdog." },
            new() { FuseName = "RPMB_KEY_PROVISIONED", Address = 0x00780038, BitNumber = 0, Description = "RPMB key provisioned", ImplicationIfBlown = "Replay Protected Memory Block key set. Storage authentication active." },
        },
        ["SM8550"] = new()
        {
            new() { FuseName = "OEM_SECURE_BOOT1_AUTH_EN", Address = 0x007C0020, BitNumber = 0, Description = "Secure boot authentication enabled", ImplicationIfBlown = "Device will only boot signed images." },
            new() { FuseName = "OEM_SECURE_BOOT1_PK_HASH_IN_FUSE", Address = 0x007C0020, BitNumber = 1, Description = "Root certificate hash stored in fuses", ImplicationIfBlown = "Root of trust permanently set." },
            new() { FuseName = "ANTI_ROLLBACK_FEATURE_EN[0]", Address = 0x007C0024, BitNumber = 0, Description = "Boot image anti-rollback enabled", ImplicationIfBlown = "Cannot downgrade firmware." },
            new() { FuseName = "APPS_DBGEN_DISABLE", Address = 0x007C0028, BitNumber = 0, Description = "JTAG debug disabled", ImplicationIfBlown = "Hardware JTAG permanently disabled." },
            new() { FuseName = "APPS_NIDEN_DISABLE", Address = 0x007C0028, BitNumber = 1, Description = "Non-invasive debug disabled", ImplicationIfBlown = "Trace disabled." },
            new() { FuseName = "SHARED_QSEE_SPIDEN_DISABLE", Address = 0x007C002C, BitNumber = 0, Description = "TrustZone secure invasive debug disabled", ImplicationIfBlown = "Cannot debug TrustZone." },
            new() { FuseName = "SKDK_READ_DISABLE", Address = 0x007C0030, BitNumber = 0, Description = "Secure Key Derivation Key read disabled", ImplicationIfBlown = "Hardware encryption key locked." },
            new() { FuseName = "WDOG_EN", Address = 0x007C0034, BitNumber = 0, Description = "Watchdog disable GPIO locked", ImplicationIfBlown = "Watchdog configuration locked." },
        },
        ["SM8650"] = new()
        {
            new() { FuseName = "OEM_SECURE_BOOT1_AUTH_EN", Address = 0x00800020, BitNumber = 0, Description = "Secure boot authentication enabled", ImplicationIfBlown = "Device will only boot signed images." },
            new() { FuseName = "OEM_SECURE_BOOT1_PK_HASH_IN_FUSE", Address = 0x00800020, BitNumber = 1, Description = "Root certificate hash stored in fuses", ImplicationIfBlown = "Root of trust permanently set." },
            new() { FuseName = "ANTI_ROLLBACK_FEATURE_EN[0]", Address = 0x00800024, BitNumber = 0, Description = "Boot image anti-rollback enabled", ImplicationIfBlown = "Cannot downgrade firmware." },
            new() { FuseName = "APPS_DBGEN_DISABLE", Address = 0x00800028, BitNumber = 0, Description = "JTAG debug disabled", ImplicationIfBlown = "Hardware JTAG permanently disabled." },
            new() { FuseName = "SHARED_QSEE_SPIDEN_DISABLE", Address = 0x0080002C, BitNumber = 0, Description = "TrustZone secure invasive debug disabled", ImplicationIfBlown = "Cannot debug TrustZone." },
            new() { FuseName = "SKDK_READ_DISABLE", Address = 0x00800030, BitNumber = 0, Description = "Secure Key Derivation Key read disabled", ImplicationIfBlown = "Hardware encryption key locked." },
        },
        ["MSM8998"] = new()
        {
            new() { FuseName = "OEM_SECURE_BOOT1_AUTH_EN", Address = 0x00740020, BitNumber = 0, Description = "Secure boot authentication enabled", ImplicationIfBlown = "Device will only boot signed images." },
            new() { FuseName = "OEM_SECURE_BOOT1_PK_HASH_IN_FUSE", Address = 0x00740020, BitNumber = 1, Description = "Root certificate hash stored in fuses", ImplicationIfBlown = "Root of trust permanently set." },
            new() { FuseName = "ANTI_ROLLBACK_FEATURE_EN[0]", Address = 0x00740024, BitNumber = 0, Description = "Boot image anti-rollback enabled", ImplicationIfBlown = "Cannot downgrade firmware." },
            new() { FuseName = "APPS_DBGEN_DISABLE", Address = 0x00740028, BitNumber = 0, Description = "JTAG debug disabled", ImplicationIfBlown = "Hardware JTAG permanently disabled." },
            new() { FuseName = "SHARED_QSEE_SPIDEN_DISABLE", Address = 0x0074002C, BitNumber = 0, Description = "TrustZone secure invasive debug disabled", ImplicationIfBlown = "Cannot debug TrustZone." },
            new() { FuseName = "SKDK_READ_DISABLE", Address = 0x00740030, BitNumber = 0, Description = "Secure Key Derivation Key read disabled", ImplicationIfBlown = "Hardware encryption key locked." },
        },
        ["MSM8937"] = new()
        {
            new() { FuseName = "OEM_SECURE_BOOT1_AUTH_EN", Address = 0x00700020, BitNumber = 0, Description = "Secure boot authentication enabled", ImplicationIfBlown = "Device will only boot signed images." },
            new() { FuseName = "OEM_SECURE_BOOT1_PK_HASH_IN_FUSE", Address = 0x00700020, BitNumber = 1, Description = "Root certificate hash stored in fuses", ImplicationIfBlown = "Root of trust permanently set." },
            new() { FuseName = "ANTI_ROLLBACK_FEATURE_EN[0]", Address = 0x00700024, BitNumber = 0, Description = "Boot image anti-rollback enabled", ImplicationIfBlown = "Cannot downgrade firmware." },
            new() { FuseName = "APPS_DBGEN_DISABLE", Address = 0x00700028, BitNumber = 0, Description = "JTAG debug disabled", ImplicationIfBlown = "Hardware JTAG permanently disabled." },
            new() { FuseName = "SKDK_READ_DISABLE", Address = 0x00700030, BitNumber = 0, Description = "Secure Key Derivation Key read disabled", ImplicationIfBlown = "Hardware encryption key locked." },
        },
    };

    // Generic fallback — common QFPROM register offsets used across many SoCs
    private static readonly List<QFuseDefinition> _generic = new()
    {
        new() { FuseName = "OEM_SECURE_BOOT1_AUTH_EN", Address = 0x00780020, BitNumber = 0, Description = "Secure boot authentication enabled", ImplicationIfBlown = "Device will only boot signed images." },
        new() { FuseName = "OEM_SECURE_BOOT1_PK_HASH_IN_FUSE", Address = 0x00780020, BitNumber = 1, Description = "Root certificate hash stored in fuses", ImplicationIfBlown = "Root of trust permanently set." },
        new() { FuseName = "ANTI_ROLLBACK_FEATURE_EN[0]", Address = 0x00780024, BitNumber = 0, Description = "Boot image anti-rollback enabled", ImplicationIfBlown = "Cannot downgrade firmware." },
        new() { FuseName = "APPS_DBGEN_DISABLE", Address = 0x00780028, BitNumber = 0, Description = "JTAG debug disabled", ImplicationIfBlown = "Hardware JTAG permanently disabled." },
        new() { FuseName = "APPS_NIDEN_DISABLE", Address = 0x00780028, BitNumber = 1, Description = "Non-invasive debug disabled", ImplicationIfBlown = "Trace disabled." },
        new() { FuseName = "SHARED_QSEE_SPIDEN_DISABLE", Address = 0x0078002C, BitNumber = 0, Description = "TrustZone secure invasive debug disabled", ImplicationIfBlown = "Cannot debug TrustZone." },
        new() { FuseName = "SKDK_READ_DISABLE", Address = 0x00780030, BitNumber = 0, Description = "Secure Key Derivation Key read disabled", ImplicationIfBlown = "Hardware encryption key locked." },
        new() { FuseName = "WDOG_EN", Address = 0x00780034, BitNumber = 0, Description = "Watchdog disable GPIO locked", ImplicationIfBlown = "Watchdog configuration locked." },
    };

    public static List<QFuseDefinition> GetFuses(string? socModel)
    {
        if (socModel != null && _db.TryGetValue(socModel, out var fuses))
            return fuses;
        return _generic;
    }

    public static string? GetSocModel(uint msmId) => msmId switch
    {
        0x008600E1 => "SDM845",
        0x008700E1 => "SM8550",
        0x008800E1 => "SM8650",
        0x007000E1 => "MSM8998",
        0x006900E1 => "MSM8937",
        _ => null,
    };
}

public class QFuseAuditor
{
    private readonly FirehoseClient _client;
    private readonly string? _socModel;

    public QFuseAuditor(FirehoseClient client, string? socModel = null)
    {
        _client = client;
        _socModel = socModel ?? QFuseDatabase.GetSocModel(client.ChipInfo?.MsmId ?? 0);
    }

    public async Task<QFuseAuditResult> AuditAsync(CancellationToken ct = default)
    {
        var result = new QFuseAuditResult();
        var fuses = QFuseDatabase.GetFuses(_socModel);
        result.TotalAvailable = fuses.Count;

        foreach (var def in fuses)
        {
            var status = new QFuseStatus
            {
                FuseName = def.FuseName,
                Address = def.Address,
                BitNumber = def.BitNumber,
                Description = def.Description,
                Implication = def.ImplicationIfBlown,
            };

            try
            {
                var raw = await _client.PeekAsync(def.Address, 4, ct);
                if (raw.Length >= 4)
                {
                    uint val = BitConverter.ToUInt32(raw, 0);
                    status.IsBlown = ((val >> def.BitNumber) & 1) == 1;
                }
            }
            catch (FirehoseException)
            {
                // Address not readable — mark as unknown (not blown)
                status.IsBlown = false;
            }

            result.Fuses.Add(status);
            if (status.IsBlown)
            {
                result.TotalBlown++;
                result.PermanentDamageWarnings.Add($"{def.FuseName}: {def.ImplicationIfBlown}");
            }
        }

        return result;
    }
}
