using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Finds the nearest Interactable in range and sends it E presses/holds.
/// Runs early so interactables see this frame's input in their own Update.
/// </summary>
[DefaultExecutionOrder(-50)]
public class PlayerInteraction : MonoBehaviour
{
    void Update()
    {
        // No interacting while the pay slip / failure screen is up.
        // ...or while resting/asleep (SleepSystem handles E to get up).
        if ((NightManager.Instance && !NightManager.Instance.IsRunning)
            || (SleepSystem.Instance && SleepSystem.Instance.IsResting))
        {
            Interactable.Focused = null;
            return;
        }

        Interactable nearest = FindNearest();
        Interactable.Focused = nearest;
        if (nearest == null || Keyboard.current == null) return;

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            Debug.Log("Interacted with: " + nearest.gameObject.name);
            nearest.Interact(gameObject);
        }
        if (Keyboard.current.eKey.isPressed)
            nearest.InteractHeld(gameObject, Time.deltaTime);
    }

    Interactable FindNearest()
    {
        Vector2 here = transform.position;
        Interactable best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var item in Interactable.All)
        {
            if (!item) continue;
            float d = item.DistanceFrom(here);
            if (d <= item.interactionRange && d < bestDistance)
            {
                bestDistance = d;
                best = item;
            }
        }
        return best;
    }

    void OnDrawGizmosSelected()
    {
        if (Interactable.Focused)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, Interactable.Focused.transform.position);
        }
    }
}
