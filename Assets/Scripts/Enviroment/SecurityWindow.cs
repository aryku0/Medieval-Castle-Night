using UnityEngine;

public sealed class SecurityWindow : Interactable
{
    public Transform shutter;
    public GameObject intruder;
    [Header("Random attempts")]
    [Tooltip("How often (seconds) this window rolls the dice for an intruder attempt.")]
    [Min(0.5f)] public float rollInterval = 10f;
    [Tooltip("Chance per roll that an attempt starts (0.15 = 15%).")]
    [Range(0f, 1f)] public float attemptChance = 0.15f;
    [Tooltip("No attempts during the first seconds of the night.")]
    [Min(0f)] public float quietStart = 30f;
    [Tooltip("After being latched or breached, wait this long before rolling again.")]
    [Min(0f)] public float cooldownSeconds = 20f;
    [Tooltip("An attempt can't start while this many windows are already being forced.")]
    [Min(1)] public int maxWindowsAtOnce = 2;

    [Tooltip("Attempts are this many times more likely while this window's nearest candle is out.")]
    [Min(1f)] public float darkMultiplier = 3f;

    /// <summary>Global difficulty hook for later (e.g. rises through the night or on later nights).</summary>
    public static float ChanceMultiplier = 1f;

    [Header("Shutter")]
    public float openingSeconds = 20f;
    public float closingSeconds = 1.5f;
    public float shutterTravel = 1.2f;
    // interactionRange now lives on Interactable (same name, so existing Inspector values carry over).
    public int Breaches { get; private set; }
    public float Opening { get; private set; }
    Vector3 closedPosition;
    float waitTimer, rollTimer, breachedUntil;
    float scheduledIn; // a prisoner's tip: this window will be tried in this many seconds
    bool attacking;
    public bool IsUnderAttack => attacking;
    bool heldThisFrame;

    public override string Prompt => "[Hold E] Latch";

    // Sit the prompt just above this window's status label.
    public override Vector3 PromptPosition => transform.position + Vector3.up * 1.25f;
    public override float PromptLiftPixels => 18f;

    protected override void Awake()
    {
        base.Awake();
        closedPosition = shutter ? shutter.localPosition : Vector3.zero;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        NightManager.NightStarted += ResetForNight;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        NightManager.NightStarted -= ResetForNight;
    }

    void Start() { ResetForNight(); }

    /// <summary>Back to a latched window, quiet for the start of the night.</summary>
    void ResetForNight()
    {
        Breaches = 0;
        breachedUntil = 0f;
        attacking = false;
        Opening = 0f;
        waitTimer = quietStart;
        rollTimer = Random.Range(0f, rollInterval); // windows don't all roll on the same beat
        scheduledIn = 0f;
        if (shutter) shutter.localPosition = closedPosition;
        if (intruder) intruder.SetActive(false);
    }

    public override void InteractHeld(GameObject player, float deltaTime) { heldThisFrame = true; }

    void Update()
    {
        Advance(Time.deltaTime, heldThisFrame);
        heldThisFrame = false;
    }

    // Separate simulation step also permits deterministic verification without keyboard input.
    public void Advance(float dt, bool closing)
    {
        // A tipped-off attack comes when the prisoner said it would (or as soon as the window is free).
        if (scheduledIn > 0f)
        {
            scheduledIn -= dt;
            if (scheduledIn <= 0f && !ForceAttack()) scheduledIn = 1f;
        }
        if (breachedUntil > 0f) { breachedUntil = Mathf.Max(0f, breachedUntil - dt); return; }
        if (!attacking)
        {
            if (waitTimer > 0f) waitTimer -= dt; // quiet start or cooldown
            else
            {
                rollTimer -= dt;
                if (rollTimer <= 0f)
                {
                    rollTimer += rollInterval;
                    if (DebugRandomAttacks && Random.value < AttemptChanceNow && WindowsUnderAttack() < maxWindowsAtOnce)
                    {
                        attacking = true;
                        SleepSystem.Cue("something scrapes at a window shutter", transform.position);
                    }
                }
            }
        }
        if (attacking)
        {
            bool wasWide = Opening >= 0.65f;
            Opening = Mathf.Clamp01(Opening + (closing ? -dt / Mathf.Max(0.1f, closingSeconds) : dt / Mathf.Max(0.1f, openingSeconds)));
            if (!wasWide && Opening >= 0.65f)
                SleepSystem.Cue("a shutter creaks wide open", transform.position);
            if (closing && Opening <= 0f) ResetAttempt();
            else if (Opening >= 1f)
            {
                Breaches++;
                breachedUntil = 3f;
                if (NightManager.Instance) NightManager.Instance.ReportBreach(name);
                SleepSystem.Cue("footsteps inside the castle", transform.position);
                ResetAttempt();
            }
        }
        if (shutter) shutter.localPosition = closedPosition + Vector3.up * (Opening * shutterTravel);
        if (intruder) intruder.SetActive(Opening >= 0.65f);
    }

    void ResetAttempt()
    {
        attacking = false;
        Opening = 0f;
        waitTimer = cooldownSeconds;
        rollTimer = rollInterval;
    }

    /// <summary>Start an attempt right now (used by decoys and people you wrongly let in).</summary>
    public bool ForceAttack()
    {
        if (attacking || breachedUntil > 0f || !isActiveAndEnabled) return false;
        attacking = true;
        waitTimer = 0f;
        SleepSystem.Cue("something scrapes at a window shutter", transform.position);
        return true;
    }

    // ---- debug hooks (used by DebugMenu)
    public static bool DebugRandomAttacks = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetDebug() { DebugRandomAttacks = true; ChanceMultiplier = 1f; }

    public static int DebugForceAll()
    {
        int n = 0;
        foreach (var item in All)
            if (item is SecurityWindow w && w.ForceAttack()) n++;
        return n;
    }

    public string DebugStatus =>
        scheduledIn > 0f && !attacking ? $"tipped off: attack in {Mathf.CeilToInt(scheduledIn)}s" :
        (breachedUntil > 0f ? "BREACHED" : attacking ? $"opening {Mathf.RoundToInt(Opening * 100)}%" : waitTimer > 0f ? $"quiet {Mathf.CeilToInt(waitTimer)}s" : "latched")
        + (CandleInteraction.IsDark(transform.position) ? " (dark)" : "");

    /// <summary>Force an attempt on a random window that isn't already under attack.</summary>
    public static bool ForceRandomAttack()
    {
        var free = new System.Collections.Generic.List<SecurityWindow>();
        foreach (var item in All)
            if (item is SecurityWindow w && !w.attacking && w.breachedUntil <= 0f && w.isActiveAndEnabled) free.Add(w);
        if (free.Count == 0) return false;
        return free[Random.Range(0, free.Count)].ForceAttack();
    }

    /// <summary>Chance per roll right now: tripled while the nearest candle is out.</summary>
    public float AttemptChanceNow =>
        Mathf.Clamp01(attemptChance * ChanceMultiplier * (CandleInteraction.IsDark(transform.position) ? darkMultiplier : 1f));

    bool IsFree => !attacking && breachedUntil <= 0f && scheduledIn <= 0f && isActiveAndEnabled;

    public static int FreeWindowCount()
    {
        int n = 0;
        foreach (var item in All) if (item is SecurityWindow w && w.IsFree) n++;
        return n;
    }

    /// <summary>Pick a quiet window and have intruders try it after this many seconds (a prisoner's tip).</summary>
    public static SecurityWindow ScheduleRandomAttack(float delay)
    {
        var free = new System.Collections.Generic.List<SecurityWindow>();
        foreach (var item in All) if (item is SecurityWindow w && w.IsFree) free.Add(w);
        if (free.Count == 0) return null;
        var chosen = free[Random.Range(0, free.Count)];
        chosen.scheduledIn = Mathf.Max(0.01f, delay);
        return chosen;
    }

    /// <summary>"the far-left window", "the middle window"... by where it sits among the windows.</summary>
    public static string Describe(SecurityWindow window)
    {
        var windows = new System.Collections.Generic.List<SecurityWindow>();
        foreach (var item in All) if (item is SecurityWindow w) windows.Add(w);
        windows.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        int i = windows.IndexOf(window);
        if (windows.Count <= 1) return "the window";
        if (i == 0) return "the far-left window";
        if (i == windows.Count - 1) return "the far-right window";
        if (windows.Count == 3) return "the middle window";
        return $"window {i + 1} from the left";
    }

    static int WindowsUnderAttack()
    {
        int count = 0;
        foreach (var item in All)
            if (item is SecurityWindow w && w.attacking) count++;
        return count;
    }

    void OnGUI()
    {
        if (!Camera.main) return;
        UIScale.Apply(); // scale the whole UI with the screen
        if (!UIScale.WorldToGui(Camera.main, transform.position + Vector3.up * 1.25f, out Vector2 point)) return;
        if (point.x < 0 || point.x > UIScale.Width) return;
        string status = breachedUntil > 0f ? "BREACH!" : Opening >= 0.65f ? "INTRUDER!" : attacking ? "Window opening..." : "Latched";
        if (Breaches > 0) status += "  Breaches: " + Breaches;
        GUI.Box(new Rect(point.x - 135f, point.y - 15f, 270f, 28f), status);
    }
}
