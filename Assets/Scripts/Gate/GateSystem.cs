using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runs the front gate: Captain's orders at the start of each night, visitors arriving
/// and ringing the bell, patience, the desk view (papers + rulebook), Admit / Deny / Arrest,
/// the ladies' hearts minigame, and penalties on the pay slip.
/// Created automatically; does nothing unless a Gate is placed in the scene.
/// The night keeps running while the desk is open. Placeholder IMGUI for now.
/// </summary>
[DefaultExecutionOrder(-30)]
public class GateSystem : MonoBehaviour
{
    public static GateSystem Instance { get; private set; }

    [Header("Visitors")]
    [Min(1)] public int visitorsPerNight = 6;
    [Tooltip("Seconds into the night before the first visitor rings.")]
    public float firstVisitorDelay = 25f;
    [Tooltip("Seconds a visitor waits at the gate (while you're not at the desk) before leaving.")]
    public float patienceSeconds = 40f;
    [Range(0f, 1f)] public float ladyDecoyChance = 0.5f;
    [Min(1)] public int heartsToClear = 8;

    [Header("Deductions (coins)")]
    public int honestTurnedAway = 2;
    public int badPapersAdmitted = 3;
    public int forgerAdmitted = 5;
    public int innocentArrested = 8;
    public int forgerLetGo = 1;
    public int visitorLeftWaiting = 1;
    public int messengerLeftWaiting = 3;

    // ---- public state
    public bool VisitorWaiting => current != null;
    public bool IsPanelOpen => deskOpen || ordersOpen;
    public int ArrestedTonight { get; private set; }
    /// <summary>Everyone arrested so far. Becomes real prisoners when the cellar is built.</summary>
    public int PrisonersHeld => GameState.Prisoners;
    public int LettersTonight { get; private set; }

    // ---- debug hooks (used by DebugMenu)
    public static bool DebugInfinitePatience;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetDebug() { DebugInfinitePatience = false; }

    /// <summary>Replace whoever is at the gate with a new visitor of this kind, right now.</summary>
    public void DebugSpawn(VisitorKind kind, bool forceDecoy = false)
    {
        if (!Active || !NightRunning) return;
        current = VisitorGenerator.Create(kind, Night, ladyDecoyChance);
        if (kind == VisitorKind.Lady && forceDecoy) current.isDecoy = true;
        patience = patienceSeconds;
        deskOpen = false;
        heartsStarted = false;
        hearts.Clear();
        SleepSystem.Announce("The gate bell rings", Gate.Instance.transform.position);
    }

    public void DebugShowOrders()
    {
        if (!Active || !NightRunning) return;
        deskOpen = false;
        ordersOpen = true;
        Time.timeScale = 0f;
    }

    /// <summary>What the right call is for the current visitor, for the debug readout.</summary>
    public string DebugCorrectCall()
    {
        if (!Active) return "no Gate in scene";
        if (current == null) return "nobody at the gate";
        if (current.IsLady) return "lady" + (current.isDecoy ? " (DECOY)" : "") + " - pop the hearts";
        var verdict = GateRules.Evaluate(current, Night, out string reason);
        return $"{current.kind}: {verdict.ToString().ToUpper()} ({reason})";
    }

    int Night => NightManager.Instance ? NightManager.Instance.NightNumber : 1;
    bool NightRunning => !NightManager.Instance || NightManager.Instance.IsRunning;
    bool Active => Gate.Instance != null;

    // ---- internal state
    readonly List<VisitorKind> lineup = new List<VisitorKind>();
    Visitor current;
    float patience;
    float arrivalTimer;
    float spacing;
    bool deskOpen, ordersOpen, showRulebook;
    int deskOpenedFrame;
    readonly List<Vector2> hearts = new List<Vector2>();
    bool heartsStarted;


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

    void OnNightStarted()
    {
        current = null;
        deskOpen = false;
        ArrestedTonight = 0;
        LettersTonight = 0;
        lineup.Clear();
        lineup.AddRange(VisitorGenerator.Lineup(Night, visitorsPerNight));

        float nightSeconds = NightManager.Instance ? NightManager.Instance.realMinutesPerNight * 60f : 600f;
        spacing = Mathf.Max(15f, (nightSeconds - firstVisitorDelay - 45f) / visitorsPerNight);
        arrivalTimer = firstVisitorDelay;

        if (Active)
        {
            ordersOpen = true;
            Time.timeScale = 0f; // read the orders before the night starts ticking
        }
    }

    void OnNightEnded(NightManager.NightState result)
    {
        deskOpen = false;
        ordersOpen = false;
        current = null;
    }

    void CloseOrders()
    {
        ordersOpen = false;
        if (NightRunning) GameTime.Resume();
    }

    void Update()
    {
        if (!Active || !NightRunning) return;
        var kb = Keyboard.current;

        if (ordersOpen)
        {
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) CloseOrders();
            return;
        }

        if (deskOpen)
        {
            bool leave = kb != null && (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame);
            if ((leave && Time.frameCount != deskOpenedFrame) || (SleepSystem.Instance && SleepSystem.Instance.IsResting))
                deskOpen = false;
        }

        float dt = Time.deltaTime;

        // Arrivals: one visitor at the gate at a time.
        arrivalTimer -= dt;
        if (current == null && arrivalTimer <= 0f && lineup.Count > 0)
        {
            var kind = lineup[0];
            lineup.RemoveAt(0);
            current = VisitorGenerator.Create(kind, Night, ladyDecoyChance);
            patience = patienceSeconds;
            heartsStarted = false;
            hearts.Clear();
            arrivalTimer = spacing * Random.Range(0.7f, 1.3f);
            SleepSystem.Announce("The gate bell rings", Gate.Instance.transform.position);
        }

        // Patience only runs down while nobody's at the desk.
        if (current != null && !deskOpen)
        {
            if (!DebugInfinitePatience) patience -= dt;
            if (patience <= 0f) VisitorGivesUp();
        }
    }

    // ---------------------------------------------------------------- desk

    public void OpenDesk()
    {
        if (current == null || !NightRunning) return;
        deskOpen = true;
        deskOpenedFrame = Time.frameCount;
        showRulebook = false;

        if (current.IsLady && !heartsStarted)
        {
            heartsStarted = true;
            hearts.Clear();
            for (int i = 0; i < heartsToClear; i++)
                hearts.Add(new Vector2(Random.Range(0.08f, 0.92f), Random.Range(0.1f, 0.85f)));
            if (current.isDecoy && SecurityWindow.ForceRandomAttack())
                SleepSystem.Announce("Something scrapes at a window while you're distracted...");
        }
    }

    void Decide(GateVerdict chosen)
    {
        var v = current;
        GateVerdict correct = GateRules.Evaluate(v, Night, out string reason);
        var night = NightManager.Instance;

        if (chosen == GateVerdict.Arrest) { ArrestedTonight++; GameState.Prisoners++; }

        // Smallpox night: every person you let in might carry the pox, unless you bought medicine.
        if (chosen == GateVerdict.Admit && GateRules.HealthCertRequired(Night) && !GameState.HasMedicine
            && !GameState.SickTonight && Random.value < GameState.InfectionChance)
        {
            GameState.SickTonight = true;
            SleepSystem.Announce("<color=#ff9a7a>You feel feverish... You'll tire much faster tonight.</color>");
        }
        if (chosen == GateVerdict.Admit && v.kind == VisitorKind.Messenger && correct == GateVerdict.Admit) LettersTonight++;

        if (chosen == correct)
        {
            SleepSystem.Announce(chosen == GateVerdict.Arrest ? $"{v.spokenName} is dragged down to the cellar."
                : chosen == GateVerdict.Deny ? $"You turn {v.spokenName} away. Correct: {reason}."
                : $"{v.spokenName} passes through the gate.");
        }
        else
        {
            string label; int amount;
            if (correct == GateVerdict.Admit)
            {
                if (chosen == GateVerdict.Deny) { label = "Honest visitor turned away"; amount = honestTurnedAway; }
                else { label = "Innocent visitor arrested"; amount = innocentArrested; }
            }
            else if (correct == GateVerdict.Deny)
            {
                if (chosen == GateVerdict.Admit) { label = "Admitted with bad papers"; amount = badPapersAdmitted; }
                else { label = "Innocent visitor arrested"; amount = innocentArrested; }
            }
            else // should have arrested
            {
                if (chosen == GateVerdict.Admit)
                {
                    label = "Forger let inside"; amount = forgerAdmitted;
                    if (SecurityWindow.ForceRandomAttack())
                        SleepSystem.Announce("The stranger you let in is prising at a window from inside!");
                }
                else { label = "Forger let go"; amount = forgerLetGo; }
            }
            if (night) night.AddDeduction(label, amount);
            SleepSystem.Announce($"<color=#ff9a7a>Mistake: {reason}. (-{amount})</color>");
        }

        FinishVisitor();
    }

    void VisitorGivesUp()
    {
        var night = NightManager.Instance;
        if (current.kind == VisitorKind.Messenger)
        {
            if (night) night.AddDeduction("Messenger left waiting", messengerLeftWaiting);
            SleepSystem.Announce("The messenger rides off with his letters", Gate.Instance.transform.position);
        }
        else if (current.IsLady)
        {
            SleepSystem.Announce("The lady at the gate loses interest and wanders off", Gate.Instance.transform.position);
        }
        else
        {
            if (night) night.AddDeduction("Visitor left waiting", visitorLeftWaiting);
            SleepSystem.Announce("The visitor at the gate gives up and leaves", Gate.Instance.transform.position);
        }
        FinishVisitor();
    }

    void FinishVisitor()
    {
        current = null;
        deskOpen = false;
        hearts.Clear();
        arrivalTimer = Mathf.Max(arrivalTimer, 5f);
    }

    // ---------------------------------------------------------------- drawing (placeholder IMGUI)

    static GUIStyle rich, richCentered, title, heartStyle, docStyle;

    void Styles()
    {
        if (rich != null) return;
        rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 14 };
        richCentered = new GUIStyle(rich) { alignment = TextAnchor.MiddleCenter };
        title = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        docStyle = new GUIStyle(GUI.skin.box) { richText = true, wordWrap = true, fontSize = 14, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
        heartStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
        heartStyle.normal.textColor = new Color(0.9f, 0.2f, 0.3f);
        heartStyle.hover.textColor = new Color(1f, 0.55f, 0.65f);
    }

    void OnGUI()
    {
        if (!Active || !NightRunning) return;
        Styles();
        GUI.depth = -50;

        if (ordersOpen) { DrawOrders(); return; }
        DrawHud();
        if (deskOpen && current != null) DrawDesk();
    }

    void DrawHud()
    {
        string gate = current == null ? "Gate: quiet"
            : deskOpen ? "Gate: at the desk"
            : $"Gate: <b>visitor waiting</b> ({Mathf.CeilToInt(patience)}s)";
        if (current != null && !deskOpen && patience < 10f) gate = $"<color=#ff8866>{gate}</color>";
        string text = gate + $"\nArrested tonight: {ArrestedTonight}";
        if (GateRules.MessengersNeedSeal(Night)) text += $"   Letters: {LettersTonight}";
        GUI.Box(new Rect(Screen.width - 250, 10, 240, 48), "");
        GUI.Label(new Rect(Screen.width - 244, 12, 232, 46), text, rich);
    }

    void DrawOrders()
    {
        const float w = 560f, h = 380f;
        var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.6f));
        Fill(r, new Color(0.16f, 0.13f, 0.1f, 0.98f));
        Fill(new Rect(r.x, r.y, w, 3), new Color(0.75f, 0.6f, 0.3f));
        GUILayout.BeginArea(new Rect(r.x + 22, r.y + 16, w - 44, h - 32));
        GUILayout.Label(GateRules.Orders(Night), rich);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Understood (Enter)", GUILayout.Height(34))) CloseOrders();
        GUILayout.EndArea();
    }

    void DrawDesk()
    {
        const float w = 780f, h = 470f;
        var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f - 20f, w, h);
        Fill(r, new Color(0.14f, 0.11f, 0.08f, 0.97f));
        Fill(new Rect(r.x, r.y, w, 3), new Color(0.75f, 0.6f, 0.3f));

        int night = Night;
        GUI.Label(new Rect(r.x, r.y + 8, w, 26), $"The Gate   ·   {GateRules.DateText(GateRules.Day(night))}", title);

        // Left: the visitor
        var left = new Rect(r.x + 18, r.y + 46, 250, h - 110);
        Fill(left, new Color(0, 0, 0, 0.25f));
        GUILayout.BeginArea(new Rect(left.x + 10, left.y + 8, left.width - 20, left.height - 16));
        GUILayout.Label("<b>At the hatch</b>", rich);
        GUILayout.Label(current.description, rich);
        GUILayout.Space(8);
        GUILayout.Label($"<i>\"{current.greeting}\"</i>", rich);
        if (!current.IsLady)
        {
            GUILayout.Space(8);
            GUILayout.Label($"\"The name's <b>{current.spokenName}</b>.\"", rich);
        }
        GUILayout.EndArea();

        // Right: papers / rulebook / hearts
        var right = new Rect(r.x + 284, r.y + 46, w - 302, h - 110);
        if (current.IsLady) DrawHearts(right);
        else if (showRulebook) GUI.Box(right, GateRules.Rulebook(night), docStyle);
        else DrawPapers(right, night);
        if (current == null) return; // the lady just left (all hearts cleared)

        // Bottom buttons
        float by = r.yMax - 52;
        if (current.IsLady)
        {
            GUI.Label(new Rect(r.x, by, w - 180, 40), $"Click the hearts to get back to work!  ({hearts.Count} left)", richCentered);
        }
        else
        {
            if (GUI.Button(new Rect(r.x + 18, by, 120, 38), "<b>Admit</b>", ButtonRich())) Decide(GateVerdict.Admit);
            if (GUI.Button(new Rect(r.x + 146, by, 120, 38), "<b>Deny</b>", ButtonRich())) Decide(GateVerdict.Deny);
            if (GUI.Button(new Rect(r.x + 274, by, 120, 38), "<b>Arrest</b>", ButtonRich())) Decide(GateVerdict.Arrest);
            if (GUI.Button(new Rect(r.x + 420, by, 150, 38), showRulebook ? "Show papers" : "Open rulebook")) showRulebook = !showRulebook;
        }
        if (GUI.Button(new Rect(r.xMax - 168, by, 150, 38), "Step away (E)")) deskOpen = false;
    }

    static GUIStyle buttonRich;
    static GUIStyle ButtonRich()
    {
        if (buttonRich == null) buttonRich = new GUIStyle(GUI.skin.button) { richText = true, fontSize = 15 };
        return buttonRich;
    }

    void DrawPapers(Rect area, int night)
    {
        var v = current;
        float y = area.y;
        float docH = 108f;

        if (v.hasPass)
        {
            string pass = "<b>TRAVEL PASS</b>\n"
                + $"Name: {v.passName}\n"
                + $"Town of origin: {v.passTown}\n"
                + $"Valid until: {GateRules.DateText(v.passExpiryDay)}\n"
                + $"Stamp: <i>{v.passStamp}</i>";
            GUI.Box(new Rect(area.x, y, area.width, docH), pass, docStyle);
            y += docH + 8;
        }
        else
        {
            GUI.Box(new Rect(area.x, y, area.width, 36), "<i>No travel pass.</i>", docStyle);
            y += 44;
        }

        if (v.kind == VisitorKind.Messenger && GateRules.MessengersNeedSeal(night))
        {
            string seal = v.hasLordsSeal
                ? $"<b>LETTER CASE</b>\nSealed with <i>{v.lordsSeal}</i>"
                : "<b>LETTER CASE</b>\n<i>No seal.</i>";
            GUI.Box(new Rect(area.x, y, area.width, 56), seal, docStyle);
            y += 64;
        }

        if (GateRules.HealthCertRequired(night))
        {
            string cert = v.hasHealthCert
                ? $"<b>HEALTH CERTIFICATE</b>\nThis is to certify that <b>{v.healthCertName}</b> is free of the pox.\nSigned: <i>{v.healthCertSignature}</i>"
                : "<i>No health certificate.</i>";
            GUI.Box(new Rect(area.x, y, area.width, v.hasHealthCert ? 80 : 36), cert, docStyle);
        }
    }

    void DrawHearts(Rect area)
    {
        Fill(area, new Color(0.25f, 0.08f, 0.12f, 0.35f));
        for (int i = hearts.Count - 1; i >= 0; i--)
        {
            Vector2 p = hearts[i];
            float bob = Mathf.Sin(Time.unscaledTime * 2.2f + i * 1.3f) * 5f;
            var hr = new Rect(area.x + p.x * area.width - 22, area.y + p.y * area.height - 22 + bob, 44, 44);
            if (GUI.Button(hr, "♥", heartStyle)) hearts.RemoveAt(i);
        }
        if (heartsStarted && hearts.Count == 0)
        {
            SleepSystem.Announce("The lady sighs and wanders off into the night.");
            FinishVisitor();
        }
    }

    static void Fill(Rect r, Color color)
    {
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }
}
