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
    bool attacking;
    public bool IsUnderAttack => attacking;
    bool heldThisFrame;

    public override string Prompt => "[Hold E] Latch";

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
                    if (Random.value < attemptChance * ChanceMultiplier && WindowsUnderAttack() < maxWindowsAtOnce)
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
        Vector3 point = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.25f);
        if (point.z <= 0 || point.x < 0 || point.x > Screen.width) return;
        string status = breachedUntil > 0f ? "BREACH!" : Opening >= 0.65f ? "INTRUDER!" : attacking ? "Window opening..." : "Latched";
        if (Focused == this) status += "  " + Prompt;
        if (Breaches > 0) status += "  Breaches: " + Breaches;
        GUI.Box(new Rect(point.x - 135f, Screen.height - point.y - 15f, 270f, 28f), status);
    }
}
