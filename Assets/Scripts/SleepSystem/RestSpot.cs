using UnityEngine;

/// <summary>
/// A stool, chair or bed the guard can rest on. Press E to rest (screen goes dark),
/// E again to get up. Rest Quality is how fast tiredness drops: this is what
/// bed upgrades will improve later.
/// </summary>
public class RestSpot : Interactable
{
    [Tooltip("1 = plain stool. Higher = better bed, you recover faster so you spend less time resting.")]
    [Min(0.1f)] public float restQuality = 1f;

    public override string Prompt => GameState.HasStool ? "[E] Rest (cushioned)" : "[E] Rest";

    /// <summary>Quality tonight, including the cushioned stool upgrade.</summary>
    public float EffectiveQuality => restQuality * (GameState.HasStool ? GameState.StoolRestMultiplier : 1f);

    public override void Interact(GameObject player)
    {
        if (SleepSystem.Instance) SleepSystem.Instance.StartRest(EffectiveQuality);
    }
}
