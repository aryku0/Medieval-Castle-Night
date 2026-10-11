using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs one night shift: the clock from dusk to dawn, failure events reported by
/// windows/candles, and the end-of-night pay slip.
/// You don't have to place this in the scene: one is created automatically if missing.
/// Add it to a GameObject yourself if you want to tune the values in the Inspector.
/// </summary>
[DefaultExecutionOrder(-100)]
public class NightManager : MonoBehaviour
{
    public enum NightState { Running, Survived, Failed }

    public static NightManager Instance { get; private set; }

    /// <summary>Raised whenever a night (re)starts. Windows, candles etc. reset themselves.</summary>
    public static event Action NightStarted;
    /// <summary>Raised when the night ends, with the result.</summary>
    public static event Action<NightState> NightEnded;

    [Header("Clock")]
    [Range(0, 23)] public int startHour = 22;
    [Range(0, 23)] public int endHour = 6;
    [Min(0.25f)] public float realMinutesPerNight = 10f;
    [Tooltip("Other systems (e.g. sleeping) can raise this to make the night pass faster.")]
    [Min(0f)] public float timeMultiplier = 1f;

    [Header("Rules")]
    [Min(1)] public int maxBreaches = 3;

    [Header("Pay (coins)")]
    public int nightlyWage = 20;
    public int breachDeduction = 5;
    public int candleOutDeduction = 1;

    [Header("Debug HUD")]
    public bool showHud = true;

    // Night number and purse live in GameState so they survive trips to the safehouse.
    public int NightNumber => GameState.Night;
    public int Purse => GameState.Purse;
    public NightState State { get; private set; }
    public int Breaches { get; private set; }
    public string FailReason { get; private set; }
    public bool IsRunning => State == NightState.Running;

    /// <summary>0 at dusk, 1 at dawn.</summary>
    public float Progress => Mathf.Clamp01(elapsedHours / NightLengthHours);

    /// <summary>Current in-game time as fractional hours on a 24h clock (e.g. 23.5 = 11:30 pm).</summary>
    public float ClockHours => (startHour + elapsedHours) % 24f;

    float NightLengthHours
    {
        get
        {
            int length = (endHour - startHour + 24) % 24;
            return length == 0 ? 24f : length;
        }
    }

    // Deductions grouped by label, in the order they first happened.
    readonly List<string> deductionOrder = new List<string>();
    readonly Dictionary<string, int> deductionCount = new Dictionary<string, int>();
    readonly Dictionary<string, int> deductionEach = new Dictionary<string, int>();
    // Bonuses (e.g. letters delivered), same grouping.
    readonly List<string> bonusOrder = new List<string>();
    readonly Dictionary<string, int> bonusCount = new Dictionary<string, int>();
    readonly Dictionary<string, int> bonusEach = new Dictionary<string, int>();

    float elapsedHours;
    int lastNetPay;
    bool demoOver;
    Transform player;
    Vector3 playerStart;


    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        GameState.CastleScene = gameObject.scene.name;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            GameTime.Resume();
        }
    }

    void Start()
    {
        var hero = GameObject.FindGameObjectWithTag("Player");
        if (hero)
        {
            player = hero.transform;
            playerStart = player.position;
        }
        StartNight();
    }

    void Update()
    {
        if (State != NightState.Running) return;

        float realSecondsPerHour = realMinutesPerNight * 60f / NightLengthHours;
        elapsedHours += Time.deltaTime * timeMultiplier / realSecondsPerHour;

        if (elapsedHours >= NightLengthHours)
        {
            elapsedHours = NightLengthHours;
            // Hand over the night's post first: tampering can still cost you the night.
            if (LetterSystem.Instance && LetterSystem.Instance.DawnHandover(this)) return;
            EndNight(NightState.Survived, null);
        }
    }

    // ---------------------------------------------------------------- reporting

    /// <summary>Call when an intruder gets in.</summary>
    public void ReportBreach(string where)
    {
        if (!IsRunning) return;
        Breaches++;
        AddDeduction("Window breach", breachDeduction);
        Debug.Log($"[Night] Breach at {where} ({Breaches}/{maxBreaches})");
        if (Breaches >= maxBreaches)
            EndNight(NightState.Failed, $"Intruders got in {Breaches} times.");
    }

    /// <summary>Call when a lit candle burns out.</summary>
    public void ReportCandleOut(string which)
    {
        if (!IsRunning) return;
        AddDeduction("Candle burned out", candleOutDeduction);
        Debug.Log($"[Night] Candle out: {which}");
    }

    /// <summary>General-purpose deduction for future systems (inspections, broken seals...).</summary>
    public void AddDeduction(string label, int amount)
    {
        if (!IsRunning) return;
        if (!deductionCount.ContainsKey(label))
        {
            deductionOrder.Add(label);
            deductionCount[label] = 0;
        }
        deductionCount[label]++;
        deductionEach[label] = amount;
    }

    /// <summary>Extra pay on the pay slip (e.g. letters delivered).</summary>
    public void AddBonus(string label, int amount)
    {
        if (!IsRunning) return;
        if (!bonusCount.ContainsKey(label))
        {
            bonusOrder.Add(label);
            bonusCount[label] = 0;
        }
        bonusCount[label]++;
        bonusEach[label] = amount;
    }

    int TotalBonuses()
    {
        int total = 0;
        foreach (var label in bonusOrder) total += bonusCount[label] * bonusEach[label];
        return total;
    }

    // ---------------------------------------------------------------- debug hooks (used by DebugMenu)

    public void DebugSkipHours(float hours)
    {
        if (IsRunning) elapsedHours = Mathf.Min(elapsedHours + hours, NightLengthHours - 0.01f);
    }

    public void DebugSkipToDawn()
    {
        if (IsRunning) elapsedHours = NightLengthHours; // Update ends the night next frame
    }

    public void DebugStartNight(int night)
    {
        GameState.Night = Mathf.Max(1, night);
        StartNight();
    }

    public void DebugAddCoins(int coins) { GameState.Purse += coins; }

    /// <summary>End the night early as a failure (e.g. overslept, prisoner escaped).</summary>
    public void FailNight(string reason)
    {
        if (IsRunning) EndNight(NightState.Failed, reason);
    }

    // ---------------------------------------------------------------- flow

    void StartNight()
    {
        State = NightState.Running;
        demoOver = false;
        GameState.SickTonight = false;
        elapsedHours = 0f;
        Breaches = 0;
        FailReason = null;
        deductionOrder.Clear();
        deductionCount.Clear();
        deductionEach.Clear();
        bonusOrder.Clear();
        bonusCount.Clear();
        bonusEach.Clear();
        GameTime.Resume();

        if (player)
        {
            player.position = playerStart;
            if (player.TryGetComponent<Rigidbody2D>(out var body))
            {
                body.position = playerStart;
                body.linearVelocity = Vector2.zero;
            }
        }

        // If the night restarts while you're in the cellar, bring the camera back up with you.
        var cam = Camera.main ? Camera.main.GetComponent<CameraXYFollow>() : null;
        if (cam) cam.ResetBounds();

        NightStarted?.Invoke();
        Debug.Log($"[Night] Night {NightNumber} begins.");
    }

    void EndNight(NightState result, string reason)
    {
        // Owing money costs interest (added while the night still counts as running).
        if (result == NightState.Survived && GameState.InDebt)
            AddDeduction("Debt interest", GameState.DebtInterest);

        State = result;
        FailReason = reason;

        lastNetPay = 0;
        if (result == NightState.Survived)
        {
            lastNetPay = nightlyWage + TotalBonuses() - TotalDeductions();
            GameState.Purse += lastNetPay; // pays off any debt first, since debt is a negative purse
        }

        Time.timeScale = 0f;
        NightEnded?.Invoke(result);
        Debug.Log($"[Night] Night {NightNumber} ended: {result}. Net pay {lastNetPay}.");
    }

    int TotalDeductions()
    {
        int total = 0;
        foreach (var label in deductionOrder)
            total += deductionCount[label] * deductionEach[label];
        return total;
    }

    // ---------------------------------------------------------------- display

    public static string FormatClock(float hours24)
    {
        int h = Mathf.FloorToInt(hours24) % 24;
        int m = Mathf.FloorToInt((hours24 - Mathf.Floor(hours24)) * 60f);
        m = m / 10 * 10; // tick in 10-minute steps, like a watchman's glass
        string suffix = h < 12 ? "am" : "pm";
        int h12 = h % 12 == 0 ? 12 : h % 12;
        return $"{h12}:{m:00} {suffix}";
    }

    void OnGUI()
    {
        UIScale.Apply(); // scale the whole UI with the screen
        if (!showHud) return;

        // Top-left status
        int lit = 0, candles = 0;
        foreach (var item in Interactable.All)
            if (item is CandleInteraction c) { candles++; if (c.IsLit) lit++; }

        string hud = $"Night {NightNumber}   {FormatClock(ClockHours)}\n"
                   + $"Breaches {Breaches}/{maxBreaches}   Candles lit {lit}/{candles}\n"
                   + $"Purse: {Purse} coins" + (GameState.InDebt ? " (in debt)" : "")
                   + (GameState.SickTonight ? "   <color=#ff9a7a>Feverish</color>" : "");
        GUI.Box(new Rect(10, 10, 260, 60), "");
        GUI.Label(new Rect(16, 12, 252, 58), hud, RichLabel());

        if (State == NightState.Running) return;

        // End-of-night pay slip
        const float w = 380f, h = 400f;
        var area = new Rect((UIScale.Width - w) / 2f, (UIScale.Height - h) / 2f, w, h);
        GUI.Box(area, "");
        GUILayout.BeginArea(new Rect(area.x + 16, area.y + 12, w - 32, h - 24));

        if (demoOver)
        {
            GUILayout.Label("<b>End of the demo</b>", RichLabel());
            GUILayout.Space(8);
            GUILayout.Label($"You survived all {GameState.LastDemoNight} nights of the watch.", RichLabel());
            GUILayout.Label($"Final purse: {Purse} coins. Prisoners taken: {GameState.TotalArrests}.", RichLabel());
            GUILayout.Label("Thanks for playing!", RichLabel());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Start over", GUILayout.Height(32))) GameState.StartOver();
        }
        else if (State == NightState.Survived)
        {
            GUILayout.Label($"<b>Dawn breaks. Night {NightNumber} survived.</b>", RichLabel());
            GUILayout.Space(8);
            GUILayout.Label($"Wage: +{nightlyWage}");
            foreach (var label in bonusOrder)
            {
                int count = bonusCount[label], each = bonusEach[label];
                GUILayout.Label($"{label} x{count}: +{count * each}");
            }
            foreach (var label in deductionOrder)
            {
                int count = deductionCount[label], each = deductionEach[label];
                GUILayout.Label($"{label} x{count}: -{count * each}");
            }
            if (deductionOrder.Count == 0) GUILayout.Label("No deductions. The captain is pleased.");
            if (PrisonSystem.Instance && !string.IsNullOrEmpty(PrisonSystem.Instance.DawnReport))
            {
                GUILayout.Space(4);
                GUILayout.Label($"<i>{PrisonSystem.Instance.DawnReport}</i>", RichLabel());
            }
            GUILayout.Space(6);
            GUILayout.Label($"<b>Take-home: {lastNetPay}</b>   Purse: {Purse}" + (GameState.InDebt ? " <color=#ff9a7a>(still in debt)</color>" : ""), RichLabel());
            GUILayout.FlexibleSpace();
            if (NightNumber >= GameState.LastDemoNight)
            {
                if (GUILayout.Button("Finish", GUILayout.Height(32))) demoOver = true;
            }
            else if (GUILayout.Button("Go home", GUILayout.Height(32)))
            {
                // No safehouse scene yet? Carry straight on to the next night here.
                if (!GameState.GoHome())
                {
                    GameState.Night++;
                    StartNight();
                }
            }
        }
        else
        {
            GUILayout.Label($"<b>You have failed the watch.</b>", RichLabel());
            GUILayout.Space(8);
            GUILayout.Label(FailReason ?? "");
            GUILayout.Label($"Fell at {FormatClock(ClockHours)}. No wage tonight.");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Try the night again", GUILayout.Height(32)))
                StartNight();
        }

        GUILayout.EndArea();
    }

    static GUIStyle richLabel;
    static GUIStyle RichLabel()
    {
        if (richLabel == null) richLabel = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        return richLabel;
    }
}
