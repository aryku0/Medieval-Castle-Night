/// <summary>
/// One person locked in the cellar. Plain data so it can be kept in GameState between nights.
/// </summary>
[System.Serializable]
public class Prisoner
{
    public string name;
    /// <summary>Smugglers won't talk to you: talking just gets a grunt. Only bread calms them.</summary>
    public bool grunter;
    /// <summary>Which cell they're in (1-4).</summary>
    public int cell;

    /// <summary>1 = just fed, 0 = starving.</summary>
    public float hunger = 0.7f;
    /// <summary>1 = content, 0 = desperate. Low mood makes escape attempts more likely.</summary>
    public float mood = 0.6f;

    // Per-night state (reset at dusk).
    public bool tipGiven;
    public float talkCooldown;

    public Prisoner Clone() => (Prisoner)MemberwiseClone();
}
