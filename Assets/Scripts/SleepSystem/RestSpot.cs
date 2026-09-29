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

    public override string Prompt => "[E] Rest";

    public override void Interact(GameObject player)
    {
        if (SleepSystem.Instance) SleepSystem.Instance.StartRest(restQuality);
    }
}
