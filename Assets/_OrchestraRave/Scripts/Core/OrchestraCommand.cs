public enum OrchestraCommandType
{
    SetGlobalVolume,
    SetGlobalFilter,
    SetGroupVolume,
    SetGroupMute,
    ChangePhase
}

public readonly struct OrchestraCommand
{
    public readonly OrchestraCommandType Type;
    public readonly InstrumentGroupId Target;
    public readonly float Value;
    public readonly bool IsMuted;
    public readonly MusicPhase Phase;

    public OrchestraCommand(
        OrchestraCommandType type,
        InstrumentGroupId target = InstrumentGroupId.None,
        float value = 0f,
        bool isMuted = false,
        MusicPhase phase = MusicPhase.Classical)
    {
        Type = type;
        Target = target;
        Value = value;
        IsMuted = isMuted;
        Phase = phase;
    }
}