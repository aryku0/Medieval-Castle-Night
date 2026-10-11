/// <summary>What a letter is about.</summary>
public enum LetterKind { GateIntel, PoxWarning, Ordinary, Rebel }

/// <summary>The state of a letter's wax seal.</summary>
public enum SealState
{
    Intact,       // never opened
    Unsealed,     // opened and not resealed yet: obvious at dawn
    SealedWell,   // resealed neatly: passes at dawn
    SealedBadly,  // resealed sloppily: spotted at dawn
}

/// <summary>One letter in your letter box. Plain data.</summary>
public class Letter
{
    public LetterKind kind;
    public string sender;
    public string recipient;
    public string body;

    public SealState seal = SealState.Intact;
    /// <summary>The old seal came off in one piece, so resealing is easier (wider line to trace).</summary>
    public bool liftedInOnePiece;
    public bool burned;

    public bool HasBeenOpened => seal != SealState.Intact;
    public bool WillBeSpotted => seal == SealState.Unsealed || seal == SealState.SealedBadly;

    public string SealLabel
    {
        get
        {
            if (burned) return "burned";
            switch (seal)
            {
                case SealState.Intact: return "sealed";
                case SealState.Unsealed: return "<color=#ff8866>OPEN, not resealed</color>" + (liftedInOnePiece ? " (old seal kept whole)" : " (seal broken)");
                case SealState.SealedWell: return "read, resealed neatly";
                default: return "<color=#ff8866>resealed sloppily</color>";
            }
        }
    }
}
