namespace BACKRabbit.Protocol.Firehose.Rescue;

/// <summary>
/// Result of a --wipe-data operation — block-level erase of the userdata partition.
/// The wipe goes through THREE safety gates before any erase call:
/// 1. CLI flag --wipe-data must be present
/// 2. Typed confirmation must match (CLI: "WIPE-USERDATA"; orchestrator: device serial or "CONFIRM-WIPE-DATA")
/// 3. --dry-run forces ZERO erase calls regardless
/// After erase: readback verification samples first 1000 + last 100 sectors for any non-zero byte.
/// Any non-zero byte found ABORTS the rescue (verdict = PermanentDamage).
/// </summary>
public class WipeDataResult
{
    public WipeDataStatus Status { get; set; }
    public bool DryRun { get; set; }
    public long SectorsChecked { get; set; }
    public long? FirstNonZeroSector { get; set; }  // null if all zeros, sector number if verification failed
    public string? ErrorMessage { get; set; }
    public string ConfirmationRequested { get; set; } = "";  // what the operator was asked to type
    public bool ConfirmationProvided { get; set; }
}

public enum WipeDataStatus
{
    WipedAndVerified,
    WipeNotAuthorized,    // --wipe-data not set
    ConfirmationFailed,   // wrong confirmation string
    VerificationFailed,   // erase happened but readback showed non-zero data
    DryRunLogged          // dry-run mode, no actual erase
}