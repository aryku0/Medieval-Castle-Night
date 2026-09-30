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

    public int NightNumber { get; private set; } = 1;
    public int Purse { get; private set; }
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

    float elapsedHours;
    int lastNetPay;
    Transform player;
    Vector3 playerStart;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateIfMissing()
    {
        if (!FindAnyObjectByType<NightManager>())
            new GameObject("NightManager").AddComponent<NightManager>();
    }

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Time.timeScale = 1f;
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
        NightNumber = Mathf.Max(1, night);
        StartNight();
    }

    public void DebugAddCoins(int coins) { Purse += coins; }

    /// <summary>End the night early as a failure (e.g. overslept, prisoner escaped).</summary>
    public void FailNight(string reason)
    {
        if (IsRunning) EndNight(NightState.Failed, reason);
    }

    // ---------------------------------------------------------------- flow

    void StartNight()
    {
        State = NightState.Running;
        elapsedHours = 0f;
        Breaches = 0;
        FailReason = null;
        deductionOrder.Clear();
        deductionCount.Clear();
        deductionEach.Clear();
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

        NightStarted?.Invoke();
        Debug.Log($"[Night] Night {NightNumber} begins.");
    }

    void EndNight(NightState result, string reason)
    {
        State = result;
        FailReason = reason;

        lastNetPay = 0;
        if (result == NightState.Survived)
        {
            lastNetPay = nightlyWage - TotalDeductions();
            Purse += lastNetPay;
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
        if (!showHud) return;

        // Top-left status
        int lit = 0, candles = 0;
        foreach (var item in Interactable.All)
            if (item is CandleInteraction c) { candles++; if (c.IsLit) lit++; }

        string hud = $"Night {NightNumber}   {FormatClock(ClockHours)}\n"
                   + $"Breaches {Breaches}/{maxBreaches}   Candles lit {lit}/{candles}\n"
                   + $"Purse: {Purse} coins";
        GUI.Box(new Rect(10, 10, 260, 60), hud);

        if (State == NightState.Running) return;

        // End-of-night pay slip
        const float w = 360f, h = 300f;
        var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.Box(area, "");
        GUILayout.BeginArea(new Rect(area.x + 16, area.y + 12, w - 32, h - 24));

        if (State == NightState.Survived)
        {
            GUILayout.Label($"<b>Dawn breaks. Night {NightNumber} survived.</b>", RichLabel());
            GUILayout.Space(8);
            GUILayout.Label($"Wage: +{nightlyWage}");
            foreach (var label in deductionOrder)
            {
                int count = deductionCount[label], each = deductionEach[label];
                GUILayout.Label($"{label} x{count}: -{count * each}");
            }
            if (deductionOrder.Count == 0) GUILayout.Label("No deductions. The captain is pleased.");
            GUILayout.Space(6);
            GUILayout.Label($"<b>Take-home: {lastNetPay}</b>   Purse: {Purse}", RichLabel());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Next night", GUILayout.Height(32)))
            {
                NightNumber++;
                StartNight();
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
