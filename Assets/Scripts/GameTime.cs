using UnityEngine;

/// <summary>
/// The game's normal running speed. Pause screens set Time.timeScale to 0 and,
/// when they close, restore it to GameTime.Speed (1 normally; the debug menu can raise it).
/// </summary>
public static class GameTime
{
    public static float Speed = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() { Speed = 1f; }

    /// <summary>Resume play at the chosen speed.</summary>
    public static void Resume() { Time.timeScale = Speed; }

    public static bool IsPaused => Time.timeScale == 0f;
}
