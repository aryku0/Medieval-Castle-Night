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
        // ...or while resting/asleep or at the gate desk (those systems handle their own keys).
        if ((NightManager.Instance && !NightManager.Instance.IsRunning) || InputBlock.PlayerFrozen)
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

    // ---------------------------------------------------------------- on-screen prompt

    static GUIStyle promptStyle;

    void OnGUI()
    {
        UIScale.Apply(); // scale the whole UI with the screen
        var target = Interactable.Focused;
        if (!target || !Camera.main) return;
        string text = target.Prompt;
        if (string.IsNullOrEmpty(text)) return;

        if (!UIScale.WorldToGui(Camera.main, target.PromptPosition, out Vector2 point)) return;

        if (promptStyle == null)
            promptStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter, richText = true };

        Vector2 size = promptStyle.CalcSize(new GUIContent(text)) + new Vector2(14f, 6f);
        float x = Mathf.Clamp(point.x - size.x / 2f, 4f, UIScale.Width - size.x - 4f);
        float y = Mathf.Clamp(point.y - target.PromptLiftPixels - size.y, 4f, UIScale.Height - size.y - 4f);
        GUI.Box(new Rect(x, y, size.x, size.y), text, promptStyle);
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
