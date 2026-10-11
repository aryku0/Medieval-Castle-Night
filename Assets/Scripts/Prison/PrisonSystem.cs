using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the cellar: puts people arrested at the gate into free cells, keeps the bread you carry,
/// handles escapes (first one is a deduction, the second fails the night), tips from happy
/// prisoners, and the dawn collection. Prisoners still held at dawn are kept in GameState
/// for the next night. Created automatically; does nothing until the cells have PrisonCell
/// on them (Tools > Nightguard > Set Up Prisoners).
/// </summary>
[DefaultExecutionOrder(-25)]
public class PrisonSystem : MonoBehaviour
{
    public static PrisonSystem Instance { get; private set; }

    [Header("Needs")]
    [Tooltip("Seconds for a full stomach to empty.")]
    public float secondsToStarve = 240f;
    [Tooltip("Seconds for full mood to drain to nothing (when not starving).")]
    public float secondsMoodDrains = 400f;
    [Tooltip("Below this hunger a prisoner counts as starving.")]
    [Range(0f, 1f)] public float starvingBelow = 0.2f;
    public float starvingMoodMultiplier = 2f;

    [Header("Bread and talking")]
    [Min(1)] public int maxBread = 4;
    [Tooltip("Mood gained from a loaf (this is the only thing that calms a grunter).")]
    [Range(0f, 1f)] public float breadMood = 0.4f;
    [Range(0f, 1f)] public float talkMood = 0.3f;
    public float talkCooldown = 20f;
    [Tooltip("Talkers at or above this mood may share a tip (once each per night).")]
    [Range(0f, 1f)] public float tipMood = 0.7f;
    [Range(0f, 1f)] public float tipChance = 0.5f;

    [Header("Escapes")]
    public float rollInterval = 10f;
    [Tooltip("Chance per roll that a content prisoner (full mood) starts forcing the lock.")]
    [Range(0f, 1f)] public float chanceAtFullMood = 0.005f;
    [Tooltip("Chance per roll for a desperate prisoner (zero mood). In between it rises steeply as mood falls.")]
    [Range(0f, 1f)] public float chanceAtZeroMood = 0.15f;
    [Tooltip("Seconds after one escape attempt starts before any other prisoner can start one (cellar-wide).")]
    public float gapBetweenAttempts = 45f;
    [Tooltip("Escape attempts are this many times more likely while the cell's nearest candle is out.")]
    [Min(1f)] public float darkMultiplier = 3f;
    [Tooltip("No escape attempts during the first seconds of the night (or after a new arrest).")]
    public float quietStart = 30f;
    public float cooldownAfterBarred = 20f;
    [Min(1)] public int maxLocksAtOnce = 2;
    [Tooltip("Seconds for a prisoner to force their lock fully open.")]
    public float lockSeconds = 25f;
    [Tooltip("Seconds of holding E to bar a fully forced door again.")]
    public float barSeconds = 1.5f;
    public int escapeDeduction = 12;
    [Min(1)] public int escapesToFail = 2;

    [Header("Dawn")]
    [Tooltip("How many prisoners are taken away at dawn (random in this range).")]
    public Vector2Int collectedAtDawn = new Vector2Int(1, 2);
    [Tooltip("Hunger and mood of prisoners kept for the next night.")]
    [Range(0f, 1f)] public float carriedOverNeeds = 0.5f;

    // ---- public state
    public int Bread { get; private set; }
    public int EscapesTonight { get; private set; }
    public int ArrestsTonight { get; private set; }
    /// <summary>Line for the pay slip: who was taken away at dawn.</summary>
    public string DawnReport { get; private set; }

    /// <summary>True once the cellar's cells are set up in this scene.</summary>
    public bool Ready => PrisonCell.Cells.Count > 0;
    /// <summary>Can someone else be arrested? (Always true if there's no cellar yet.)</summary>
    public bool HasRoom => !Ready || FreeCell() != null;
    public int Occupied { get { int n = 0; foreach (var c in PrisonCell.Cells) if (c.Occupant != null) n++; return n; } }

    // ---- debug hooks (used by DebugMenu)
    public static bool DebugRandomEscapes = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetDebug() { DebugRandomEscapes = true; }

    bool NightRunning => !NightManager.Instance || NightManager.Instance.IsRunning;
    float lastAttemptTime = -999f;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnEnable()
    {
        NightManager.NightStarted += OnNightStarted;
        NightManager.NightEnded += OnNightEnded;
    }

    void OnDisable()
    {
        NightManager.NightStarted -= OnNightStarted;
        NightManager.NightEnded -= OnNightEnded;
    }

    // ---------------------------------------------------------------- night flow

    /// <summary>Dusk (or a retry): the cellar goes back to how it was at the end of last night.</summary>
    void OnNightStarted()
    {
        Bread = 0;
        EscapesTonight = 0;
        ArrestsTonight = 0;
        DawnReport = null;
        lastAttemptTime = -999f;

        foreach (var cell in PrisonCell.Cells) cell.ResetForNight(null, quietStart);
        foreach (var kept in GameState.Cellar)
        {
            var p = kept.Clone(); // work on a copy, so a failed night can be retried from dusk
            p.tipGiven = false;
            p.talkCooldown = 0f;
            var cell = CellNumber(p.cell);
            if (cell == null) cell = FreeCell();
            if (cell == null) continue;
            p.cell = cell.cellNumber;
            cell.ResetForNight(p, quietStart);
        }
    }

    void OnNightEnded(NightManager.NightState result)
    {
        Bread = 0;
        foreach (var cell in PrisonCell.Cells) cell.StopForcing();
        if (result != NightManager.NightState.Survived) return;

        GameState.TotalArrests += ArrestsTonight;
        if (!Ready) return;

        // A cart comes for some of them at dawn.
        var held = new List<PrisonCell>();
        foreach (var cell in PrisonCell.Cells) if (cell.Occupant != null) held.Add(cell);
        int take = Mathf.Min(held.Count, Random.Range(collectedAtDawn.x, collectedAtDawn.y + 1));
        Shuffle(held);
        var names = new List<string>();
        for (int i = 0; i < take; i++)
        {
            names.Add(held[i].Occupant.name);
            held[i].Release();
        }

        // Whoever's left stays for tomorrow night.
        GameState.Cellar.Clear();
        foreach (var cell in PrisonCell.Cells)
        {
            if (cell.Occupant == null) continue;
            var p = cell.Occupant.Clone();
            p.hunger = p.mood = carriedOverNeeds;
            p.tipGiven = false;
            p.talkCooldown = 0f;
            GameState.Cellar.Add(p);
        }

        if (take > 0)
            DawnReport = $"Taken away at dawn: {string.Join(", ", names)}. Still in the cellar: {GameState.Cellar.Count}.";
        else if (GameState.Cellar.Count == 0)
            DawnReport = "The cellar is empty.";
        else
            DawnReport = $"Still in the cellar: {GameState.Cellar.Count}.";
    }

    // ---------------------------------------------------------------- arrests

    /// <summary>Lock up someone arrested at the gate. Returns false if the cellar is full.</summary>
    public bool Arrest(Visitor v)
    {
        if (!Ready) { ArrestsTonight++; return true; } // no cellar in this scene yet: just counted
        var cell = FreeCell();
        if (cell == null) return false;
        ArrestsTonight++;
        cell.LockUp(new Prisoner
        {
            name = v.spokenName,
            grunter = v.kind == VisitorKind.Smuggler,
            cell = cell.cellNumber,
            hunger = 0.7f,
            mood = 0.6f,
        }, quietStart);
        return true;
    }

    PrisonCell FreeCell()
    {
        PrisonCell best = null;
        foreach (var c in PrisonCell.Cells)
            if (c.Occupant == null && (best == null || c.cellNumber < best.cellNumber)) best = c;
        return best;
    }

    PrisonCell CellNumber(int number)
    {
        foreach (var c in PrisonCell.Cells) if (c.cellNumber == number && c.Occupant == null) return c;
        return null;
    }

    // ---------------------------------------------------------------- bread

    /// <summary>Fill your arms at the ration shelf. Returns how many loaves you picked up.</summary>
    public int TakeBread()
    {
        int taken = maxBread - Bread;
        Bread = maxBread;
        return taken;
    }

    public bool UseBread()
    {
        if (Bread <= 0) return false;
        Bread--;
        return true;
    }

    // ---------------------------------------------------------------- escapes

    /// <summary>Chance per roll that a prisoner starts forcing the lock: tiny when content, 15% when desperate.</summary>
    public float EscapeChance(float mood)
    {
        float unhappy = 1f - Mathf.Clamp01(mood);
        return chanceAtFullMood + (chanceAtZeroMood - chanceAtFullMood) * unhappy * unhappy * unhappy;
    }

    /// <summary>Escape chance for a prisoner in a cell at this point: tripled while its nearest candle is out.</summary>
    public float EscapeChance(float mood, Vector3 cellPosition) =>
        Mathf.Clamp01(EscapeChance(mood) * (CandleInteraction.IsDark(cellPosition) ? darkMultiplier : 1f));

    /// <summary>Can a new random escape attempt start right now? (At most 2 at once, and a breather between them.)</summary>
    public bool MayStartAttempt =>
        DebugRandomEscapes && LocksBeingForced() < maxLocksAtOnce && Time.time - lastAttemptTime >= gapBetweenAttempts;

    public void NoteAttempt() { lastAttemptTime = Time.time; }

    public static int LocksBeingForced()
    {
        int n = 0;
        foreach (var c in PrisonCell.Cells) if (c.IsForcing) n++;
        return n;
    }

    public void ReportEscape(Prisoner p, Vector3 where)
    {
        EscapesTonight++;
        SleepSystem.Announce($"<color=#ff9a7a>{p.name} has broken out of the cellar!</color>");
        var night = NightManager.Instance;
        if (!night) return;
        if (EscapesTonight >= escapesToFail)
            night.FailNight($"{EscapesTonight} prisoners escaped from the cellar.");
        else
            night.AddDeduction("Prisoner escaped", escapeDeduction);
    }

    // ---------------------------------------------------------------- talking and tips

    static readonly string[] Grunts =
    {
        "{0} grunts.", "{0} spits on the straw and turns away.", "\"Hmph.\"", "{0} stares straight through you.",
        "{0} mutters something you'd rather not hear.",
    };

    static readonly string[] Chatter =
    {
        "\"Cold as the grave down here.\"", "\"How long do they keep a man in this hole?\"",
        "\"Tell my wife I'm sorry, if you ever see her.\"", "\"You're kinder than the last fellow on the night watch.\"",
        "\"I paid good money for that pass. Swore it was proper, the man did.\"", "\"Rats had a go at my boots last night.\"",
    };

    static readonly string[] HungryChatter =
    {
        "\"Is there no bread in this whole castle?\"", "\"My belly thinks my throat's been cut.\"",
    };

    static readonly string[] Whispers =
    {
        "\"Carts have been rolling barrels down to the river at night. Not wine, by the smell of it.\"",
        "\"There's a tall fellow calls himself John Johnson. Servant, he says. No servant I ever met.\"",
        "\"Parliament sits on the fifth. Some say the King won't see the end of that day.\"",
        "\"Lord Monteagle had a letter he was never meant to read, so the taverns say.\"",
        "\"Catesby's lot drink at the Duck and Drake. Gone very quiet lately, they have.\"",
    };

    static string Pick(string[] pool) => pool[Random.Range(0, pool.Length)];

    /// <summary>What happens when you talk to a prisoner. Returns the caption to show.</summary>
    public string Talk(Prisoner p)
    {
        if (p.grunter)
        {
            p.talkCooldown = 4f; // just to stop the captions piling up
            return string.Format(Pick(Grunts), p.name);
        }

        p.mood = Mathf.Clamp01(p.mood + talkMood);
        p.talkCooldown = talkCooldown;

        if (!p.tipGiven && p.mood >= tipMood && Random.value < tipChance)
        {
            string tip = Tip(p);
            if (tip != null) { p.tipGiven = true; return tip; }
        }
        return $"{p.name}: " + (p.hunger < 0.3f ? Pick(HungryChatter) : Pick(Chatter));
    }

    string Tip(Prisoner p)
    {
        var options = new List<int> { 2 }; // story whispers are always possible
        var smuggler = GateSystem.Instance ? GateSystem.Instance.PeekUpcoming(VisitorKind.Smuggler) : null;
        if (smuggler != null) options.Add(0);
        if (SecurityWindow.FreeWindowCount() > 0) options.Add(1);

        switch (options[Random.Range(0, options.Count)])
        {
            case 0:
                string desc = smuggler.description.TrimEnd('.');
                desc = char.ToLower(desc[0]) + desc.Substring(1);
                return $"{p.name} leans on the bars: \"Word in the cells is {desc} means to come through your gate tonight. Forged papers, the lot.\"";
            case 1:
                var window = SecurityWindow.ScheduleRandomAttack(Random.Range(30f, 60f));
                if (window == null) goto default;
                return $"{p.name} whispers: \"I heard them talking. They'll try {SecurityWindow.Describe(window)} before long.\"";
            default:
                return $"{p.name} whispers: " + Pick(Whispers);
        }
    }

    public string Feed(Prisoner p)
    {
        p.hunger = 1f;
        p.mood = Mathf.Clamp01(p.mood + breadMood);
        return p.grunter ? $"{p.name} snatches the bread without a word."
                         : $"{p.name} wolfs down the bread. \"Bless you.\"";
    }

    // ---------------------------------------------------------------- debug hooks

    public void DebugAddPrisoner(bool grunter)
    {
        var cell = FreeCell();
        if (cell == null) return;
        var v = VisitorGenerator.Create(grunter ? VisitorKind.Smuggler : VisitorKind.Traveller, GameState.Night, 0f);
        Arrest(v);
    }

    public void DebugSetAll(float? hunger, float? mood)
    {
        foreach (var c in PrisonCell.Cells)
        {
            if (c.Occupant == null) continue;
            if (hunger.HasValue) c.Occupant.hunger = hunger.Value;
            if (mood.HasValue) c.Occupant.mood = mood.Value;
        }
    }

    public void DebugForceLock()
    {
        var free = new List<PrisonCell>();
        foreach (var c in PrisonCell.Cells) if (c.Occupant != null && !c.IsForcing) free.Add(c);
        if (free.Count > 0) free[Random.Range(0, free.Count)].StartForcing();
    }

    public void DebugFillBread() { Bread = maxBread; }

    public void DebugEmptyCellar()
    {
        foreach (var c in PrisonCell.Cells) c.Release();
    }

    // ---------------------------------------------------------------- HUD

    static GUIStyle hudStyle;

    void OnGUI()
    {
        UIScale.Apply();
        if (!Ready || !NightRunning) return;
        if (hudStyle == null) hudStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 };
        int forcing = LocksBeingForced();
        string text = $"Bread {Bread}/{maxBread}   Cellar {Occupied}/{PrisonCell.Cells.Count}";
        if (forcing > 0) text += "   <color=#ff8866><b>Lock!</b></color>";
        GUI.Box(new Rect(UIScale.Width - 250, 62, 240, 24), "");
        GUI.Label(new Rect(UIScale.Width - 244, 64, 232, 22), text, hudStyle);
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
