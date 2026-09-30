using UnityEngine;

/// <summary>
/// Something in your lodgings you can use with E: the bills table, the upgrade chest,
/// the apothecary jar or the bed. Pick which in the Inspector.
/// </summary>
public class SafehouseObject : Interactable
{
    public SafehouseManager.Panel opens = SafehouseManager.Panel.Bills;

    public override string Prompt
    {
        get
        {
            switch (opens)
            {
                case SafehouseManager.Panel.Bills: return GameState.BillsPaidToday ? "[E] Bills (paid)" : "[E] Pay bills";
                case SafehouseManager.Panel.Upgrades: return "[E] Open chest";
                case SafehouseManager.Panel.Medicine: return "[E] Apothecary jar";
                case SafehouseManager.Panel.Bed: return "[E] Go to bed";
                default: return "[E] Use";
            }
        }
    }

    public override bool CanInteract =>
        base.CanInteract && SafehouseManager.Instance && !SafehouseManager.Instance.IsPanelOpen;

    public override void Interact(GameObject player)
    {
        if (SafehouseManager.Instance) SafehouseManager.Instance.OpenPanel(opens);
    }
}
