/// <summary>
/// One place to ask "should the player be frozen right now?"
/// (resting/asleep, or a gate panel like the desk view or Captain's orders is open).
/// </summary>
public static class InputBlock
{
    public static bool PlayerFrozen =>
        (SleepSystem.Instance && SleepSystem.Instance.IsResting)
        || (GateSystem.Instance && GateSystem.Instance.IsPanelOpen);
}
