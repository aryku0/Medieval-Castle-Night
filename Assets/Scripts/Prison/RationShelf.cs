using UnityEngine;

/// <summary>
/// The free ration shelf in the cellar. E fills your arms with bread (up to 4 loaves),
/// which you give to prisoners at their cells.
/// </summary>
public class RationShelf : Interactable
{
    PrisonSystem Sys => PrisonSystem.Instance;

    public override bool CanInteract => base.CanInteract && Sys;

    public override string Prompt
    {
        get
        {
            if (!Sys) return null;
            return Sys.Bread >= Sys.maxBread ? $"Your arms are full ({Sys.Bread} loaves)" : $"[E] Take bread ({Sys.Bread}/{Sys.maxBread})";
        }
    }

    public override void Interact(GameObject player)
    {
        if (!Sys || Sys.Bread >= Sys.maxBread) return;
        int taken = Sys.TakeBread();
        SleepSystem.Announce(taken == 1 ? "You take a loaf from the ration shelf." : $"You take {taken} loaves from the ration shelf.");
    }
}
