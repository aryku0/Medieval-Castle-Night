using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Letters: messengers' letters go into the letter box; you may (illegally) open them over the
/// table's candle: warm the wax, slide the knife under it, read, then ALWAYS reseal by tracing the
/// crest with your cursor. How well you opened it decides how wide (forgiving) the line is.
/// At dawn the post is handed over: bonus per clean letter, fine per burned one, and any letter
/// left open or resealed sloppily is spotted (1 = fine + warning, 2 = dismissed).
/// The night keeps running while you do this. Placeholder IMGUI for now.
/// </summary>
[DefaultExecutionOrder(-20)]
public class LetterSystem : MonoBehaviour
{
    public enum Step { Closed, Box, Warm, Knife, Read, Trace }

    public static LetterSystem Instance { get; private set; }

    [Header("Pay at dawn (coins)")]
    public int deliveredBonus = 2;
    public int missingFine = 3;
    public int tamperFine = 5;

    [Header("Warming the wax")]
    [Tooltip("Heat gained per second while holding the mouse button.")]
    public float heatPerSecond = 0.3f;
    [Tooltip("Height of the green band (0.04 = 4% of the bar).")]
    [Range(0.01f, 0.2f)] public float greenSize = 0.04f;
    [Tooltip("The green band appears somewhere between these heights, different every time.")]
    [Range(0f, 1f)] public float greenLowest = 0.5f;
    [Range(0f, 1f)] public float greenHighest = 0.88f;
    [Tooltip("Reach this and the wax smears (seal broken).")]
    [Range(0f, 1f)] public float smearAt = 0.97f;

    [Header("Sliding the knife")]
    [Tooltip("Safe knife speed (track lengths per second). Faster builds strain.")]
    public float safeSpeed = 0.25f;
    [Tooltip("Safe speed if the wax wasn't perfect (too cold or runny).")]
    public float brittleSafeSpeed = 0.14f;
    public float strainRate = 4f;
    public float strainRecovery = 0.35f;

    [Header("Resealing (tracing the crest with your cursor)")]
    [Tooltip("Half-width of the line in pixels when the old seal came off in one piece.")]
    public float wideHalfWidth = 14f;
    [Tooltip("Half-width of the line in pixels with fresh wax (seal broke).")]
    public float thinHalfWidth = 9f;
    [Tooltip("How far past the line's edge (pixels) before you slip off it. Slipping off ruins the reseal.")]
    public float blotMargin = 4f;
    [Tooltip("How far ahead along the line your cursor can pull the wax (pixels). Stops cutting corners.")]
    public float lookahead = 60f;
    [Tooltip("Blots allowed for the reseal to still pass.")]
    [Min(0)] public int maxBlots = 0;
    [Tooltip("Average wobble allowed, as a fraction of the line's half-width.")]
    [Range(0f, 1.5f)] public float passDeviation = 0.45f;

    public Step CurrentStep { get; private set; }
    public bool IsPanelOpen => CurrentStep != Step.Closed;
    public IReadOnlyList<Letter> Letters => letters;
    public int LettersInBox
    {
        get { int n = 0; foreach (var l in letters) if (!l.burned) n++; return n; }
    }

    int Night => NightManager.Instance ? NightManager.Instance.NightNumber : 1;
    bool NightRunning => !NightManager.Instance || NightManager.Instance.IsRunning;
    bool CandleLit => LetterBox.Instance && LetterBox.Instance.CandleLit;

    readonly List<Letter> letters = new List<Letter>();
    Letter active;
    bool poxWarningGiven;
    int openedFrame;
    string notice;

    // warming
    float heat;
    float softFrom, dangerFrom; // this attempt's green band
    bool warming, brittle;
    // knife
    float knife, strain;
    bool knifeHeld;
    // tracing
    float traceProgress, traceDeviation;
    int blots;
    bool traceStarted, traceGrabbed;
    bool traceReverse, traceDirectionKnown; // you may go round the crest either way
    readonly List<Vector2> trail = new List<Vector2>();
    readonly List<Vector2> blotMarks = new List<Vector2>();

    float HalfWidth => active != null && active.liftedInOnePiece ? wideHalfWidth : thinHalfWidth;

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

    void OnNightStarted()
    {
        letters.Clear();
        poxWarningGiven = false;
        Close();
    }

    void OnNightEnded(NightManager.NightState result) { Close(); }

    // ---------------------------------------------------------------- receiving

    /// <summary>A messenger hands over a letter.</summary>
    public void Receive(Letter letter)
    {
        letters.Add(letter);
        SleepSystem.Announce("The messenger hands you a sealed letter. It goes in your letter box.");
    }

    public Letter CreateLetter()
    {
        int night = Night;
        if (night == 2 && !poxWarningGiven)
        {
            poxWarningGiven = true;
            return new Letter
            {
                kind = LetterKind.PoxWarning,
                sender = "Dr. Edmund Whitlock, physician",
                recipient = "The Steward of the Castle",
                body = "Sir,\n\nThe pox has broken out in the lower town. Three households struck down this week alone. "
                     + "I beg you: by tomorrow night let no one through your gates but those I have certified, "
                     + "and see that your men take the tonic, for it is sold at any apothecary.\n\nYour servant,\nE. Whitlock",
            };
        }

        var smuggler = GateSystem.Instance ? GateSystem.Instance.PeekUpcoming(VisitorKind.Smuggler) : null;
        float r = Random.value;
        if (smuggler != null && r < 0.45f)
        {
            string desc = smuggler.description.TrimEnd('.');
            desc = char.ToLower(desc[0]) + desc.Substring(1);
            return new Letter
            {
                kind = LetterKind.GateIntel,
                sender = "Captain of the Town Watch",
                recipient = "Lord Ashby",
                body = $"My lord,\n\nBe warned: a man we have long sought, {desc}, means to pass your gate tonight. "
                     + "His papers are forged. Have your guard seize him.\n\nCapt. R. Hollis",
            };
        }
        if (r < 0.82f) return Pick(Ordinary);
        return Pick(Rebel);
    }

    static Letter Pick(Letter[] pool)
    {
        var src = pool[Random.Range(0, pool.Length)];
        return new Letter { kind = src.kind, sender = src.sender, recipient = src.recipient, body = src.body };
    }

    static readonly Letter[] Ordinary =
    {
        new Letter { kind = LetterKind.Ordinary, sender = "Wm. Barrow, wool merchant", recipient = "The Steward",
            body = "Sir,\n\nForty bales delivered, forty-two paid for. I trust the difference will be settled by Martinmas.\n\nWm. Barrow" },
        new Letter { kind = LetterKind.Ordinary, sender = "Your loving mother", recipient = "Master Thomas, kitchens",
            body = "My dear son,\n\nThe hens are laying again and your sister is to be married in spring. Do eat properly.\n\nYour loving mother" },
        new Letter { kind = LetterKind.Ordinary, sender = "An admirer", recipient = "Mistress Anne, lady's maid",
            body = "Fair Anne,\n\nI have watched you cross the courtyard every morning. Meet me by the herb garden at dusk. "
                 + "Mind the herbalist, she misses nothing.\n\nOne who adores you" },
        new Letter { kind = LetterKind.Ordinary, sender = "J. Pell, tailor", recipient = "Lord Ashby",
            body = "My lord,\n\nYour new doublet is ready. The bill, I regret, is also ready.\n\nJ. Pell" },
    };

    static readonly Letter[] Rebel =
    {
        new Letter { kind = LetterKind.Rebel, sender = "A friend", recipient = "(no name)",
            body = "The wolf's hour draws near. When the chapel bell rings thrice, friends will come to the cellar door. "
                 + "Leave it unbarred.\n\nBurn this." },
        new Letter { kind = LetterKind.Rebel, sender = "(unsigned)", recipient = "To the guard at the gate",
            body = "Not all who wear the Crown's colours love the Crown. We are many, and we remember who helps us.\n\nBurn this." },
    };

    // ---------------------------------------------------------------- dawn

    /// <summary>
    /// Hand over the night's post. Adds bonuses / fines to the pay slip.
    /// Returns true if tampering got you dismissed (the night is failed).
    /// </summary>
    public bool DawnHandover(NightManager night)
    {
        Close();
        int spotted = 0;
        foreach (var l in letters)
        {
            if (l.burned) { night.AddDeduction("Letter missing", missingFine); continue; }
            if (l.WillBeSpotted) { spotted++; continue; }
            night.AddBonus("Letter delivered", deliveredBonus);
        }
        letters.Clear();

        if (spotted >= 2)
        {
            night.FailNight("The post was checked at dawn: two letters had been tampered with. You are dismissed.");
            return true;
        }
        if (spotted == 1)
            night.AddDeduction("Tampered letter (final warning)", tamperFine);
        return false;
    }

    // ---------------------------------------------------------------- panel flow

    public void OpenBox()
    {
        if (!NightRunning) return;
        CurrentStep = Step.Box;
        openedFrame = Time.frameCount;
        active = null;
    }

    void Close()
    {
        // Dropping a letter with the knife half under the seal tears it open.
        if (active != null && CurrentStep == Step.Knife && knife > 0.1f)
        {
            active.seal = SealState.Unsealed;
            active.liftedInOnePiece = false;
            SleepSystem.Announce("You drop the letter in a hurry and tear the half-lifted seal.");
        }
        active = null;
        CurrentStep = Step.Closed;
    }

    void BackToBox()
    {
        active = null;
        CurrentStep = Step.Box;
    }

    void StartOpening(Letter letter)
    {
        if (!CandleLit) { notice = "You need the candle to soften the wax."; return; }
        active = letter;
        heat = 0f;
        softFrom = Random.Range(greenLowest, Mathf.Max(greenLowest, greenHighest - greenSize));
        dangerFrom = softFrom + greenSize;
        warming = false;
        brittle = false;
        notice = null;
        CurrentStep = Step.Warm;
    }

    void StartTrace(Letter letter)
    {
        active = letter;
        traceProgress = 0f;
        traceDeviation = 0f;
        blots = 0;
        traceStarted = false;
        traceGrabbed = false;
        traceReverse = false;
        traceDirectionKnown = false;
        trail.Clear();
        blotMarks.Clear();
        notice = null;
        CurrentStep = Step.Trace;
    }

    void Burn(Letter letter)
    {
        if (!CandleLit) { notice = "You need the candle to burn it."; return; }
        letter.burned = true;
        notice = $"The letter from {letter.sender} curls up in the flame. It will be missed at dawn.";
        if (active == letter) BackToBox();
    }

    void OpenedUp(bool onePiece, string message)
    {
        active.seal = SealState.Unsealed;
        active.liftedInOnePiece = onePiece;
        notice = message;
        CurrentStep = Step.Read;
    }

    // ---------------------------------------------------------------- minigames

    void Update()
    {
        if (!IsPanelOpen) return;
        if (!NightRunning) { Close(); return; }
        if (SleepSystem.Instance && SleepSystem.Instance.IsResting) { Close(); return; }

        var kb = Keyboard.current;
        if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) && Time.frameCount != openedFrame)
        {
            Close();
            return;
        }

        float dt = Time.unscaledDeltaTime; // the minigames feel the same at any debug game speed
        var mouse = Mouse.current;
        if (mouse == null) return;
        bool held = mouse.leftButton.isPressed;
        Vector2 m = mouse.position.ReadValue();
        Vector2 gui = UIScale.MouseToGui(m);

        switch (CurrentStep)
        {
            case Step.Warm: UpdateWarm(held, dt); break;
            case Step.Knife: UpdateKnife(held, gui, dt); break;
            case Step.Trace: UpdateTrace(held, gui); break;
        }
    }

    void UpdateWarm(bool held, float dt)
    {
        if (held)
        {
            warming = true;
            heat += heatPerSecond * dt;
            if (heat >= smearAt) OpenedUp(false, "Too hot! The wax runs and smears. The seal is ruined.");
        }
        else if (warming)
        {
            warming = false;
            // Only a release inside the thin green zone gives perfectly soft wax.
            bool cold = heat < softFrom, runny = heat >= dangerFrom;
            brittle = cold || runny;
            notice = cold ? "The wax is still stiff... careful, it may crack."
                   : runny ? "The wax has gone runny... careful, it may tear."
                   : "Perfect. The wax is soft. Now the knife.";
            knife = 0f;
            strain = 0f;
            knifeHeld = false;
            CurrentStep = Step.Knife;
        }
    }

    void UpdateKnife(bool held, Vector2 mouse, float dt)
    {
        Rect track = KnifeTrack();
        float knifeX = track.x + knife * track.width;

        if (held && !knifeHeld && Mathf.Abs(mouse.x - knifeX) < 40f && Mathf.Abs(mouse.y - track.center.y) < 60f)
            knifeHeld = true;
        if (!held) knifeHeld = false;

        float moved = 0f;
        if (knifeHeld)
        {
            float target = Mathf.Clamp01((mouse.x - track.x) / track.width);
            if (target > knife) { moved = target - knife; knife = target; }
        }

        float safe = (brittle ? brittleSafeSpeed : safeSpeed) * dt;
        if (moved > safe) strain += (moved - safe) * strainRate;
        else strain = Mathf.Max(0f, strain - strainRecovery * dt);

        if (strain >= 1f) OpenedUp(false, "Crack! The seal splits. You'll need fresh wax to reseal it.");
        else if (knife >= 0.999f) OpenedUp(true, "The seal lifts off in one piece. It'll be easier to put back.");
    }

    void UpdateTrace(bool held, Vector2 mouse)
    {
        var path = CrestPoints(CrestRect());
        float length = PathLength(path);
        float half = HalfWidth;

        if (!held)
        {
            // Lifting the pen before the end ruins the reseal.
            if (traceStarted) FinishTrace(path, length, completed: false);
            return;
        }

        if (!traceStarted)
        {
            if (Vector2.Distance(mouse, path[0]) <= half + 12f) { traceStarted = true; traceGrabbed = true; }
            else return;
        }

        if (!traceGrabbed)
        {
            // After a blot: bring the pen back to where you left off (without letting go).
            if (Vector2.Distance(mouse, PointAt(path, ToPath(traceProgress, length))) <= half + 10f) traceGrabbed = true;
            else return;
        }

        // Clockwise or anticlockwise: decided by which way you first move away from the dot.
        if (!traceDirectionKnown)
        {
            if (Vector2.Distance(mouse, path[0]) < 12f) return; // not moved yet
            float sF = ClosestAlong(path, mouse, 0f, lookahead);
            float sB = ClosestAlong(path, mouse, length - lookahead, length);
            traceReverse = Vector2.Distance(mouse, PointAt(path, sB)) < Vector2.Distance(mouse, PointAt(path, sF));
            traceDirectionKnown = true;
        }

        // Progress is measured in your direction of travel; convert the search window to the path.
        float from = Mathf.Max(0f, traceProgress - 5f), to = Mathf.Min(length, traceProgress + lookahead);
        float sPath = traceReverse
            ? ClosestAlong(path, mouse, length - to, length - from)
            : ClosestAlong(path, mouse, from, to);
        float s = traceReverse ? length - sPath : sPath;
        float d = Vector2.Distance(mouse, PointAt(path, sPath));

        if (d > half + blotMargin)
        {
            // Off the line = the crest is ruined. No second chances.
            blots++;
            blotMarks.Add(mouse);
            active.seal = SealState.SealedBadly;
            notice = $"<color=#ff8866>You slipped off the line at {Mathf.RoundToInt(traceProgress / length * 100f)}%. The crest is ruined. Someone will notice.</color>";
            traceStarted = false;
            BackToBox();
            return;
        }

        if (trail.Count < 4000) trail.Add(mouse);
        if (s > traceProgress)
        {
            traceDeviation += (d / half) * (s - traceProgress);
            traceProgress = s;
        }
        if (traceProgress >= length - 12f) FinishTrace(path, length, completed: true); // back at the dot
    }

    void FinishTrace(Vector2[] path, float length, bool completed)
    {
        float wobble = traceDeviation / Mathf.Max(1f, traceProgress);
        bool pass = completed && blots <= maxBlots && wobble <= passDeviation;
        active.seal = pass ? SealState.SealedWell : SealState.SealedBadly;
        int done = Mathf.RoundToInt(traceProgress / length * 100f);
        notice = pass
            ? $"A neat reseal. It should pass. (wobble {wobble:0.00}, blots {blots})"
            : !completed
                ? $"<color=#ff8866>You lifted the pen at {done}%. The crest is unfinished. Someone will notice.</color>"
                : $"<color=#ff8866>The crest looks wrong. Someone will notice. (wobble {wobble:0.00}, blots {blots})</color>";
        traceStarted = false;
        BackToBox();
    }

    // ---------------------------------------------------------------- layout + path maths (shared by Update and OnGUI)

    const float PanelW = 760f, PanelH = 700f;
    static Rect Panel() => new Rect((UIScale.Width - PanelW) / 2f, (UIScale.Height - PanelH) / 2f, PanelW, PanelH);
    static Rect KnifeTrack() { var p = Panel(); return new Rect(p.x + 80f, p.y + 250f, PanelW - 160f, 18f); }
    static Rect CrestRect() { var p = Panel(); return new Rect(p.center.x - 190f, p.y + 130f, 380f, 380f); }

    // Lord Ashby's crest: a shield outline, traced in one stroke. Normalised 0..1 in the crest rect.
    static readonly Vector2[] CrestShape =
    {
        new Vector2(0.14f, 0.1f), new Vector2(0.86f, 0.1f), new Vector2(0.86f, 0.55f),
        new Vector2(0.5f, 0.9f), new Vector2(0.14f, 0.55f), new Vector2(0.14f, 0.1f),
    };

    static Vector2[] CrestPoints(Rect r)
    {
        var pts = new Vector2[CrestShape.Length];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Vector2(r.x + CrestShape[i].x * r.width, r.y + CrestShape[i].y * r.height);
        return pts;
    }

    static float PathLength(Vector2[] path)
    {
        float total = 0f;
        for (int i = 1; i < path.Length; i++) total += Vector2.Distance(path[i - 1], path[i]);
        return total;
    }

    /// <summary>The point at distance s along the path.</summary>
    static Vector2 PointAt(Vector2[] path, float s)
    {
        for (int i = 1; i < path.Length; i++)
        {
            float seg = Vector2.Distance(path[i - 1], path[i]);
            if (s <= seg || i == path.Length - 1)
                return Vector2.Lerp(path[i - 1], path[i], seg > 0f ? Mathf.Clamp01(s / seg) : 0f);
            s -= seg;
        }
        return path[path.Length - 1];
    }

    /// <summary>The distance along the path (between sFrom and sTo) closest to point p.</summary>
    static float ClosestAlong(Vector2[] path, Vector2 p, float sFrom, float sTo)
    {
        float bestS = sFrom, bestD = float.PositiveInfinity, start = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 a = path[i - 1], b = path[i];
            float seg = Vector2.Distance(a, b);
            float end = start + seg;
            if (end >= sFrom && start <= sTo && seg > 0f)
            {
                float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (seg * seg));
                float s = Mathf.Clamp(start + t * seg, sFrom, sTo);
                float d = Vector2.Distance(p, PointAt(path, s));
                if (d < bestD) { bestD = d; bestS = s; }
            }
            start = end;
        }
        return bestS;
    }

    // ---------------------------------------------------------------- drawing

    static GUIStyle rich, title, letterStyle, button;

    void Styles()
    {
        if (rich != null) return;
        rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 14 };
        title = new GUIStyle(rich) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        letterStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 16, alignment = TextAnchor.UpperLeft, padding = new RectOffset(18, 18, 16, 16) };
        var ink = new Color(0.16f, 0.1f, 0.05f); // dark ink, in every state (hover used to turn it white)
        letterStyle.normal.textColor = letterStyle.hover.textColor = letterStyle.active.textColor = letterStyle.focused.textColor = ink;
        letterStyle.onNormal.textColor = letterStyle.onHover.textColor = letterStyle.onActive.textColor = letterStyle.onFocused.textColor = ink;
        button = new GUIStyle(GUI.skin.button) { richText = true, fontSize = 14 };
    }

    void OnGUI()
    {
        UIScale.Apply(); // scale the whole UI with the screen
        if (!IsPanelOpen) return;
        Styles();
        GUI.depth = -50;

        var p = Panel();
        Fill(p, new Color(0.14f, 0.11f, 0.08f, 0.97f));
        Fill(new Rect(p.x, p.y, p.width, 3), new Color(0.75f, 0.6f, 0.3f));

        switch (CurrentStep)
        {
            case Step.Box: DrawBox(p); break;
            case Step.Warm: DrawWarm(p); break;
            case Step.Knife: DrawKnife(p); break;
            case Step.Read: DrawRead(p); break;
            case Step.Trace: DrawTrace(p); break;
        }
    }

    void Header(Rect p, string text)
    {
        GUI.Label(new Rect(p.x, p.y + 10, p.width, 28), text, title);
    }

    void Footer(Rect p, string hint)
    {
        if (!string.IsNullOrEmpty(notice))
            GUI.Label(new Rect(p.x + 20, p.yMax - 78, p.width - 40, 40), $"<i>{notice}</i>", rich);
        GUI.Label(new Rect(p.x + 20, p.yMax - 32, p.width - 40, 24), $"<color=#aaaaaa>{hint}</color>", rich);
    }

    void DrawBox(Rect p)
    {
        Header(p, "Letter box");
        GUILayout.BeginArea(new Rect(p.x + 20, p.y + 48, p.width - 40, p.height - 140));
        GUILayout.Label("A candle burns on the table beside you.", rich);
        GUILayout.Label("<color=#aaaaaa>Opening the post is forbidden. Every letter is handed over at dawn, so anything you open must be resealed.</color>", rich);
        GUILayout.Space(6);

        if (LettersInBox == 0) GUILayout.Label("<i>No letters yet. Messengers bring them to the gate.</i>", rich);
        foreach (var l in letters)
        {
            if (l.burned) continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>From:</b> {l.sender}   <b>To:</b> {l.recipient}\n<color=#cccccc>{l.SealLabel}</color>", rich, GUILayout.Width(360));
            if (l.seal == SealState.Intact && GUILayout.Button("Open it", button, GUILayout.Width(80), GUILayout.Height(34))) StartOpening(l);
            if (l.HasBeenOpened && GUILayout.Button("Read", button, GUILayout.Width(60), GUILayout.Height(34))) { active = l; CurrentStep = Step.Read; }
            if (l.seal == SealState.Unsealed && GUILayout.Button("Reseal", button, GUILayout.Width(66), GUILayout.Height(34))) StartTrace(l);
            if (GUILayout.Button("Burn", button, GUILayout.Width(56), GUILayout.Height(34))) Burn(l);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }
        GUILayout.EndArea();
        Footer(p, "E / Esc: close   ·   The night goes on while you're here.");
    }

    void DrawWarm(Rect p)
    {
        Header(p, "Soften the wax");
        GUI.Label(new Rect(p.x + 40, p.y + 48, p.width - 80, 64),
            "<b>Hold the left mouse button</b> to warm the seal over the flame. The wax rises up the bar: "
            + "<b>let go exactly in the thin <color=#8fd18f>green</color> notch</b>. It moves every time. "
            + "Too early = stiff, too late = runny, all the way up = smeared.", rich);

        // A tall vertical bar that fills from the bottom, over a little flame.
        var bar = new Rect(p.center.x - 22f, p.y + 124f, 44f, 300f);
        Fill(bar, new Color(0.1f, 0.1f, 0.12f));
        Fill(VZone(bar, softFrom, dangerFrom), new Color(0.3f, 0.75f, 0.3f));
        Fill(VZone(bar, dangerFrom, smearAt), new Color(0.6f, 0.25f, 0.2f, 0.8f));
        Fill(VZone(bar, smearAt, 1f), new Color(0.35f, 0.05f, 0.05f));
        Fill(VZone(bar, 0f, Mathf.Clamp01(heat)), new Color(1f, 0.7f, 0.3f, 0.75f));
        float level = bar.yMax - Mathf.Clamp01(heat) * bar.height;
        Fill(new Rect(bar.x - 10f, level - 1.5f, bar.width + 20f, 3f), Color.white); // the wax level

        float flicker = Mathf.Sin(Time.unscaledTime * 17f) * 3f;
        Fill(new Rect(p.center.x - 7f, bar.yMax + 10f - flicker, 14f, 22f + flicker), new Color(1f, 0.65f, 0.2f));
        Fill(new Rect(p.center.x - 3f, bar.yMax + 18f - flicker, 6f, 12f + flicker), new Color(1f, 0.95f, 0.6f));

        string state = heat < softFrom ? "cold" : heat < dangerFrom ? "<color=#8fd18f><b>NOW</b></color>" : "<color=#ff8866>runny!</color>";
        GUI.Label(new Rect(bar.xMax + 20f, level - 11f, 120f, 22f), state, rich);
        Footer(p, "E / Esc: put the letter down");
    }

    /// <summary>A vertical slice of a bar, from 'from' to 'to' (0 = bottom, 1 = top).</summary>
    static Rect VZone(Rect bar, float from, float to) =>
        new Rect(bar.x, bar.yMax - bar.height * to, bar.width, bar.height * Mathf.Max(0f, to - from));

    void DrawKnife(Rect p)
    {
        Header(p, "Slide the knife under the seal");
        GUI.Label(new Rect(p.x + 40, p.y + 60, p.width - 80, 70),
            "<b>Grab the knife</b> (hold the left mouse button on it) and drag it slowly to the right. "
            + "Slow and steady lifts the seal in one piece. Too fast and it cracks." + (brittle ? " <color=#ffcc66>The wax isn't right: go extra slowly.</color>" : ""), rich);

        var track = KnifeTrack();
        Fill(new Rect(track.x - 10, track.y - 30, track.width + 20, 78), new Color(0.55f, 0.1f, 0.1f, 0.35f)); // the seal
        Fill(track, new Color(0.1f, 0.1f, 0.12f));
        Fill(Zone(track, 0f, knife), new Color(0.75f, 0.75f, 0.8f));
        float kx = track.x + knife * track.width;
        Fill(new Rect(kx - 4, track.y - 16, 8, 50), knifeHeld ? new Color(1f, 1f, 1f) : new Color(0.8f, 0.8f, 0.85f));

        var strainBar = new Rect(track.x, track.y + 80, track.width, 14);
        Fill(strainBar, new Color(0.1f, 0.1f, 0.12f));
        Fill(Zone(strainBar, 0f, Mathf.Clamp01(strain)), Color.Lerp(new Color(0.9f, 0.8f, 0.3f), new Color(0.9f, 0.2f, 0.2f), strain));
        GUI.Label(new Rect(strainBar.x, strainBar.yMax + 4, strainBar.width, 22), "strain", rich);
        Footer(p, "E / Esc: put the letter down (a half-lifted seal will tear)");
    }

    void DrawRead(Rect p)
    {
        Header(p, "The letter");
        var sheet = new Rect(p.x + 30, p.y + 48, p.width - 60, p.height - 200);
        Fill(sheet, new Color(0.93f, 0.87f, 0.72f));                                   // parchment
        Fill(new Rect(sheet.x, sheet.y, sheet.width, 4), new Color(0.78f, 0.68f, 0.5f)); // aged edge
        GUI.Label(sheet, $"<b>From:</b> {active.sender}\n<b>To:</b> {active.recipient}\n\n{active.body}", letterStyle);

        float by = p.yMax - 136;
        if (active.seal == SealState.Unsealed)
        {
            if (GUI.Button(new Rect(p.x + 30, by, 170, 34), "<b>Reseal it</b>", button)) { StartTrace(active); return; }
            if (GUI.Button(new Rect(p.x + 210, by, 130, 34), "Burn it", button)) { Burn(active); return; }
            if (GUI.Button(new Rect(p.x + 350, by, 280, 34), "Put it away unsealed (risky)", button)) { BackToBox(); return; }
        }
        else if (GUI.Button(new Rect(p.x + 30, by, 200, 34), "Put it away", button)) { BackToBox(); return; }
        Footer(p, "E / Esc: put it away");
    }

    void DrawTrace(Rect p)
    {
        Header(p, "Reseal: trace Lord Ashby's crest");
        bool wide = active.liftedInOnePiece;
        GUI.Label(new Rect(p.x + 40, p.y + 42, p.width - 80, 64),
            "Press and hold on the <b>glowing dot</b>, then trace the whole crest <b>in one stroke</b>, either way round. Your cursor is the wax. <b>Slip off the line once and the reseal is ruined.</b> "
            + (wide ? "<color=#8fd18f>You kept the old seal whole, so the wax is forgiving.</color>" : "<color=#ffcc66>Fresh wax: a very narrow line.</color>"), rich);

        var r = CrestRect();
        var path = CrestPoints(r);
        float length = PathLength(path);
        float half = HalfWidth;

        // One solid, thick stroke to follow (opaque, so it stays clean); the traced part turns gold.
        Stroke(path, 0f, length, half, new Color(0.42f, 0.3f, 0.18f));
        if (traceProgress > 0f)
        {
            if (traceReverse) Stroke(path, length - traceProgress, length, half, new Color(0.95f, 0.75f, 0.3f));
            else Stroke(path, 0f, traceProgress, half, new Color(0.95f, 0.75f, 0.3f));
        }
        Stroke(path, 0f, length, 1.5f, new Color(0.2f, 0.12f, 0.06f)); // thin centre guide

        // What you've drawn, and any blots.
        foreach (var t in trail) Fill(new Rect(t.x - 3f, t.y - 3f, 6, 6), new Color(0.75f, 0.08f, 0.1f));
        foreach (var b in blotMarks) Fill(new Rect(b.x - 10, b.y - 10, 20, 20), new Color(0.4f, 0f, 0f));

        // Where to put the pen.
        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f);
        if (!traceStarted) Marker(path[0], pulse);
        else if (!traceGrabbed) Marker(PointAt(path, ToPath(traceProgress, length)), pulse);

        GUI.Label(new Rect(p.xMax - 230, p.y + 12, 210, 22), $"Traced {Mathf.RoundToInt(traceProgress / length * 100f)}%   ·   Blots {blots}", rich);
        Footer(p, "Don't lift the pen until the end   ·   E / Esc: give up (the letter stays open)");
    }

    /// <summary>Distance travelled in your direction -> distance along the path.</summary>
    float ToPath(float travelled, float length) => traceReverse ? length - travelled : travelled;

    /// <summary>Draws the path from s0 to s1 as a solid stroke of the given half-width.</summary>
    static void Stroke(Vector2[] path, float s0, float s1, float half, Color color)
    {
        float step = Mathf.Max(1f, half * 0.5f);
        for (float s = s0; s <= s1; s += step)
        {
            var q = PointAt(path, s);
            Fill(new Rect(q.x - half, q.y - half, half * 2f, half * 2f), color);
        }
        var end = PointAt(path, s1);
        Fill(new Rect(end.x - half, end.y - half, half * 2f, half * 2f), color);
    }

    static void Marker(Vector2 at, float pulse)
    {
        Fill(new Rect(at.x - 14, at.y - 14, 28, 28), new Color(1f, 0.95f, 0.5f, pulse));
    }

    static Rect Zone(Rect bar, float from, float to) => new Rect(bar.x + bar.width * from, bar.y, bar.width * Mathf.Max(0f, to - from), bar.height);

    static void Fill(Rect r, Color color)
    {
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    // ---------------------------------------------------------------- debug hooks (used by DebugMenu)

    public void DebugGiveLetter() { Receive(CreateLetter()); }
}
