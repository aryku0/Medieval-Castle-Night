using UnityEngine;

/// <summary>
/// The front gate in the scene. Put this on an object at the far left of the corridor.
/// When a visitor is waiting, [E] opens the desk view (handled by GateSystem).
/// </summary>
public class Gate : Interactable
{
    public static Gate Instance { get; private set; }

    protected override void OnEnable()
    {
        base.OnEnable();
        Instance = this;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (Instance == this) Instance = null;
    }

    public override bool CanInteract =>
        base.CanInteract && GateSystem.Instance && GateSystem.Instance.VisitorWaiting && !GateSystem.Instance.IsPanelOpen;

    public override string Prompt => "[E] Answer the gate";

    public override void Interact(GameObject player)
    {
        if (GateSystem.Instance) GateSystem.Instance.OpenDesk();
    }
}
