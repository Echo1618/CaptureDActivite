using System;

public static class OrchestraEventBus
{
    public static event Action<OrchestraCommand> OnCommand;
    public static event Action<InstrumentGroupId, bool> OnGroupSelectionChanged;

    public static void SendCommand(OrchestraCommand command)
    {
        OnCommand?.Invoke(command);
    }

    public static void SendSelectionChanged(InstrumentGroupId group, bool selected)
    {
        OnGroupSelectionChanged?.Invoke(group, selected);
    }
}