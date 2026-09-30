/// <summary>
/// One place to ask "should the player be frozen right now?"
/// (resting/asleep, a gate panel like the desk view or Captain's orders,
/// or a safehouse panel like the bills or the bed).
/// </summary>
public static class InputBlock
{
    public static bool PlayerFrozen =>
        (SleepSystem.Instance && SleepSystem.Instance.IsResting)
        || (GateSystem.Instance && GateSystem.Instance.IsPanelOpen)
        || (SafehouseManager.Instance && SafehouseManager.Instance.IsPanelOpen);
}
