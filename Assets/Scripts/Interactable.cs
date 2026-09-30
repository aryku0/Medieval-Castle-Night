using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for anything the player can use with E (candles, windows, and later
/// letters, prisoners, beds...). PlayerInteraction picks the nearest one in range.
/// Tap E calls Interact(); holding E calls InteractHeld() every frame.
/// </summary>
public abstract class Interactable : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("How close the player must be. Measured to this object's Collider2D if it has one, otherwise to its position.")]
    public float interactionRange = 1.5f;

    static readonly List<Interactable> all = new List<Interactable>();
    public static IReadOnlyList<Interactable> All => all;

    /// <summary>The interactable the player is currently closest to (null if none).</summary>
    public static Interactable Focused { get; internal set; }

    Collider2D ownCollider;

    protected virtual void Awake() { ownCollider = GetComponent<Collider2D>(); }
    protected virtual void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    protected virtual void OnDisable()
    {
        all.Remove(this);
        if (Focused == this) Focused = null;
    }

    /// <summary>Distance from a point to this object, or infinity if it can't be used right now.</summary>
    public float DistanceFrom(Vector2 point)
    {
        if (!CanInteract) return float.PositiveInfinity;
        Vector2 target = ownCollider && ownCollider.enabled
            ? ownCollider.ClosestPoint(point)
            : (Vector2)transform.position;
        return Vector2.Distance(point, target);
    }

    public virtual bool CanInteract => isActiveAndEnabled;
    public virtual string Prompt => "[E] Use";

    /// <summary>World point the on-screen prompt sits above: top of the collider, or just above the object.</summary>
    public virtual Vector3 PromptPosition
    {
        get
        {
            if (ownCollider && ownCollider.enabled)
            {
                var b = ownCollider.bounds;
                return new Vector3(b.center.x, b.max.y, transform.position.z);
            }
            return transform.position + Vector3.up * 0.8f;
        }
    }

    /// <summary>Extra screen pixels to lift the prompt (e.g. to clear a status label).</summary>
    public virtual float PromptLiftPixels => 8f;

    /// <summary>Called once when E is pressed.</summary>
    public virtual void Interact(GameObject player) { }

    /// <summary>Called every frame while E is held.</summary>
    public virtual void InteractHeld(GameObject player, float deltaTime) { }
}
