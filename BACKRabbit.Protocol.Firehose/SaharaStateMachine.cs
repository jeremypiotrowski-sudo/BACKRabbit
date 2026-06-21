namespace BACKRabbit.Protocol.Firehose;

public enum SaharaState
{
    Disconnected,
    HelloReceived,
    HelloSent,
    ImageUploading,
    ImageUploadComplete,
    CommandMode,
    MemoryDebugMode,
    Done,
    Error,
}

public class SaharaStateMachine
{
    public SaharaState CurrentState { get; private set; } = SaharaState.Disconnected;
    public SaharaChipInfo? ChipInfo { get; private set; }
    public SaharaError LastError { get; private set; } = SaharaError.Success;

    public event Action<SaharaState, SaharaState>? StateChanged;

    public void TransitionTo(SaharaState newState)
    {
        if (!IsValidTransition(CurrentState, newState))
            throw new SaharaProtocolException($"Invalid state transition: {CurrentState} -> {newState}");

        var oldState = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(oldState, newState);
    }

    public void SetChipInfo(SaharaChipInfo info)
    {
        ChipInfo = info;
        TransitionTo(SaharaState.HelloReceived);
    }

    public void SetError(SaharaError error)
    {
        LastError = error;
        CurrentState = SaharaState.Error;
    }

    private static bool IsValidTransition(SaharaState from, SaharaState to) => (from, to) switch
    {
        (SaharaState.Disconnected, SaharaState.HelloReceived) => true,
        (SaharaState.HelloReceived, SaharaState.HelloSent) => true,
        (SaharaState.HelloSent, SaharaState.ImageUploading) => true,
        (SaharaState.ImageUploading, SaharaState.ImageUploading) => true,
        (SaharaState.ImageUploading, SaharaState.ImageUploadComplete) => true,
        (SaharaState.ImageUploadComplete, SaharaState.CommandMode) => true,
        (SaharaState.HelloSent, SaharaState.MemoryDebugMode) => true,
        (SaharaState.ImageUploadComplete, SaharaState.MemoryDebugMode) => true,
        (SaharaState.CommandMode, SaharaState.Done) => true,
        (SaharaState.MemoryDebugMode, SaharaState.Done) => true,
        (SaharaState.ImageUploadComplete, SaharaState.Done) => true,
        (_, SaharaState.Error) => true,
        (SaharaState.Error, SaharaState.Disconnected) => true,
        _ => false,
    };

    public override string ToString() => $"SaharaSM(State={CurrentState})";
}
