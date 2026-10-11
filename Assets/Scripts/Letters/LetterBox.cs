using UnityEngine;

/// <summary>
/// Put this on any object (a table, a desk, a shelf) to make it the letter box.
/// Letters from messengers go in here, and you open / burn them here.
/// The candle on this table is treated as always lit.
/// A Collider2D on the object is optional; it just makes the "in range" distance more accurate.
/// </summary>
public class LetterBox : Interactable
{
    public static LetterBox Instance { get; private set; }

    /// <summary>The table's candle is always lit, so letters can always be opened here.</summary>
    public bool CandleLit => true;

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

    public override string Prompt
    {
        get
        {
            int n = LetterSystem.Instance ? LetterSystem.Instance.LettersInBox : 0;
            return n == 0 ? "[E] Letter box (empty)" : $"[E] Letter box ({n})";
        }
    }

    public override void Interact(GameObject player)
    {
        if (LetterSystem.Instance) LetterSystem.Instance.OpenBox();
    }
}
