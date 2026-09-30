using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tiredness, resting in the dark, the doze slider and falling asleep.
/// Created automatically if missing; add it to a GameObject yourself to tune values.
///
/// Up:      tiredness rises; past the warning level the screen dims and you walk slower.
///          At 100% you slump into a forced rest that starts close to sleep.
/// Resting: screen goes dark. Hold the stay-awake key to push the slider up, release to sink.
///          Tiredness only drops while the slider is inside the light-doze band.
///          E gets you up. Sounds around the castle appear as captions.
/// Asleep:  slider hits the bottom. You're out for 15-20 real seconds and can't act.
///          Each time there's a chance you're caught: first catch = deduction, second = night failed.
/// </summary>
[DefaultExecutionOrder(-40)]
public class SleepSystem : MonoBehaviour
{
    public enum SleepState { Up, Resting, Asleep }

    public static SleepSystem Instance { get; private set; }

    [Header("Tiredness while up")]
    [Tooltip("Real minutes on your feet before tiredness reaches 100%.")]
    [Min(0.1f)] public float minutesToExhaustion = 5f;
    [Range(0f, 1f)] public float tiredWarningLevel = 0.7f;
    [Tooltip("Walking speed multiplier at 100% tiredness.")]
    [Range(0.1f, 1f)] public float slowestSpeed = 0.6f;

    [Header("Doze slider (1 = wide awake, 0 = asleep)")]
    public Key stayAwakeKey = Key.Space;
    public float riseSpeed = 0.45f;
    public float sinkSpeed = 0.25f;
    [Tooltip("How much the slider drifts on its own. Lower = calmer (possible upgrade).")]
    public float wobble = 0.3f;
    [Range(0f, 1f)] public float bandLow = 0.10f;
    [Range(0f, 1f)] public float bandHigh = 0.35f;

    [Header("Recovery")]
    [Tooltip("Seconds inside the band to go from 100% to 0% on a Rest Quality 1 spot.")]
    [Min(1f)] public float secondsToFullRecovery = 30f;
    [Tooltip("Rest quality when you slump against a wall from exhaustion.")]
    public float forcedRestQuality = 0.5f;
    [Tooltip("Deep sleep recovers faster than dozing, but you can't act.")]
    public float asleepRecoveryMultiplier = 1.5f;

    [Header("Falling asleep")]
    public Vector2 asleepSeconds = new Vector2(15f, 20f);
    [Range(0f, 1f)] public float chanceOfBeingCaught = 1f / 3f;
    public int caughtDeduction = 10;

    public SleepState State { get; private set; }
    public float Tiredness { get; private set; }
    public float Doze { get; private set; }
    public bool IsResting => State != SleepState.Up;

    /// <summary>Multiplier for walking speed: 0 while resting, slower when very tired.</summary>
    public float SpeedMultiplier
    {
        get
        {
            if (IsResting) return 0f;
            float t = Mathf.InverseLerp(tiredWarningLevel, 1f, Tiredness);
            return Mathf.Lerp(1f, slowestSpeed, t);
        }
    }

    bool InBand => Doze >= bandLow && Doze <= bandHigh;
    bool NightRunning => !NightManager.Instance || NightManager.Instance.IsRunning;

    float restQuality = 1f;
    float asleepTimer;
    bool willBeCaught;
    int timesCaught;
    int restStartFrame;
    float noiseSeed;
    Transform player;

    // One-off explanation panels (once per play session). Game is paused while one is up.
    static bool shownRestTip, shownSleepTip;

    // ---- debug hooks (used by DebugMenu)
    public enum DebugCatchMode { Normal, Always, Never }
    public static DebugCatchMode DebugCatch = DebugCatchMode.Normal;
    public static void DebugResetTips() { shownRestTip = shownSleepTip = false; }
    public void DebugSetTiredness(float value) { Tiredness = Mathf.Clamp01(value); }
    public void DebugFallAsleep()
    {
        if (State == SleepState.Asleep) return;
        if (State == SleepState.Up) BeginRest(1f, forced: false);
        if (State == SleepState.Resting) FallAsleep();
    }
    string tipTitle, tipBody;
    public bool IsShowingTip => tipTitle != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSessionTips() { shownRestTip = shownSleepTip = false; DebugCatch = DebugCatchMode.Normal; }

    struct Caption { public string text; public float until; }
    readonly List<Caption> captions = new List<Caption>();
    const float CaptionSeconds = 4.5f;
    const int MaxCaptions = 4;


    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        noiseSeed = Random.value * 100f;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnEnable()
    {
        NightManager.NightStarted += ResetForNight;
        NightManager.NightEnded += OnNightEnded;
    }

    void OnDisable()
    {
        NightManager.NightStarted -= ResetForNight;
        NightManager.NightEnded -= OnNightEnded;
    }

    void Start()
    {
        var hero = GameObject.FindGameObjectWithTag("Player");
        player = hero ? hero.transform : null;
    }

    void ResetForNight()
    {
        State = SleepState.Up;
        Tiredness = 0f;
        Doze = 1f;
        timesCaught = 0;
        captions.Clear();
        tipTitle = tipBody = null;
    }

    void OnNightEnded(NightManager.NightState result)
    {
        State = SleepState.Up; // never leave the player stuck in the dark on the pay slip
        tipTitle = tipBody = null;
    }

    // ---------------------------------------------------------------- public API

    /// <summary>Lie down / sit down. Called by RestSpot.</summary>
    public void StartRest(float quality) => BeginRest(quality, forced: false);

    /// <summary>
    /// Something happened in the castle. Shown as a caption only while resting/asleep,
    /// because when you're up you can see it for yourself.
    /// </summary>
    public static void Cue(string text, Vector3 worldPosition)
    {
        if (Instance && Instance.IsResting)
            Instance.Say($"<i>...{text}{Instance.Direction(worldPosition)}</i>");
    }

    /// <summary>A caption that always shows (awake or resting), e.g. the gate bell, with a direction.</summary>
    public static void Announce(string text, Vector3 worldPosition)
    {
        if (Instance) Instance.Say(text + Instance.Direction(worldPosition) + ".");
    }

    /// <summary>A caption that always shows, no direction.</summary>
    public static void Announce(string text)
    {
        if (Instance) Instance.Say(text);
    }

    // ---------------------------------------------------------------- simulation

    void Update()
    {
        if (!NightRunning) return;
        if (IsShowingTip)
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) CloseTip();
            return;
        }
        float dt = Time.deltaTime;

        switch (State)
        {
            case SleepState.Up:
                // Smallpox fever (caught at the gate without medicine) doubles how fast you tire.
                Tiredness += dt / (minutesToExhaustion * 60f) * (GameState.SickTonight ? 2f : 1f);
                if (Tiredness >= 1f)
                {
                    Tiredness = 1f;
                    BeginRest(forcedRestQuality, forced: true);
                }
                break;

            case SleepState.Resting:
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.eKey.wasPressedThisFrame && Time.frameCount != restStartFrame)
                {
                    StandUp("You get back on your feet.");
                    break;
                }
                bool holding = keyboard != null && keyboard[stayAwakeKey].isPressed;
                float drift = (Mathf.PerlinNoise(Time.time * 0.8f, noiseSeed) - 0.5f) * 2f * wobble;
                float sink = sinkSpeed * (1f + Tiredness); // the more tired, the heavier your eyelids
                Doze = Mathf.Clamp01(Doze + ((holding ? riseSpeed : -sink) + drift) * dt);

                if (InBand) Tiredness -= dt / secondsToFullRecovery * restQuality;
                if (Doze <= 0f) FallAsleep();
                break;

            case SleepState.Asleep:
                Tiredness -= dt / secondsToFullRecovery * restQuality * asleepRecoveryMultiplier;
                asleepTimer -= dt;
                if (asleepTimer <= 0f) WakeUp();
                break;
        }

        Tiredness = Mathf.Clamp01(Tiredness);
        captions.RemoveAll(c => Time.time > c.until);
    }

    void BeginRest(float quality, bool forced)
    {
        if (IsResting || !NightRunning) return;
        State = SleepState.Resting;
        restQuality = Mathf.Max(0.05f, quality);
        restStartFrame = Time.frameCount;
        Doze = forced ? bandLow * 0.5f : Mathf.Min(1f, bandHigh + 0.15f);
        captions.Clear();
        Say(forced ? "Your legs give way. Your eyes close where you stand..."
                   : "You settle down and close your eyes...");

        if (!shownRestTip)
        {
            shownRestTip = true;
            ShowTip(forced ? "Exhausted" : "Resting",
                (forced ? "You were too tired to stay on your feet, so you've slumped into a rest. " : "")
                + $"Your eyes are closed: you can't see the castle, but you'll read what you hear.\n\n"
                + $"Hold {stayAwakeKey.ToString().ToUpper()} to push the slider up, let go to let it sink. "
                + "Tiredness only goes down while the slider sits inside the green band. "
                + "Too high and you're too alert to rest. Let it hit the bottom and you'll fall asleep.\n\n"
                + "Press E at any time to get back up.");
        }
    }

    void FallAsleep()
    {
        State = SleepState.Asleep;
        asleepTimer = Random.Range(asleepSeconds.x, Mathf.Max(asleepSeconds.x, asleepSeconds.y));
        willBeCaught = DebugCatch == DebugCatchMode.Always || (DebugCatch == DebugCatchMode.Normal && Random.value < chanceOfBeingCaught);
        Say("You sink into a deep sleep...");

        if (!shownSleepTip)
        {
            shownSleepTip = true;
            ShowTip("You've fallen asleep",
                $"For the next {Mathf.RoundToInt(asleepSeconds.x)}-{Mathf.RoundToInt(asleepSeconds.y)} seconds you can't move, "
                + "and the castle carries on without you: candles burn down and windows can be forced.\n\n"
                + "If the sergeant catches you asleep, your pay is docked. Caught a second time in one night and you're dismissed.\n\n"
                + "Keep the slider inside the green band to rest safely.");
        }
    }

    void WakeUp()
    {
        if (willBeCaught)
        {
            timesCaught++;
            if (timesCaught >= 2)
            {
                StandUp("The sergeant drags you up by the collar.");
                if (NightManager.Instance) NightManager.Instance.FailNight("Dismissed for sleeping on duty.");
                return;
            }
            if (NightManager.Instance) NightManager.Instance.AddDeduction("Caught asleep on duty", caughtDeduction);
            StandUp("The sergeant's boot wakes you. \"Once more and you're done.\"");
            return;
        }

        State = SleepState.Resting;
        Doze = bandLow + 0.05f;
        Say("You jolt awake. Nobody saw... you think.");
    }

    void StandUp(string message)
    {
        State = SleepState.Up;
        Say(message);
    }

    // ---------------------------------------------------------------- explanation panels

    void ShowTip(string title, string body)
    {
        tipTitle = title;
        tipBody = body;
        Time.timeScale = 0f;
    }

    void CloseTip()
    {
        tipTitle = tipBody = null;
        if (NightRunning) GameTime.Resume();
    }

    // ---------------------------------------------------------------- captions

    void Say(string text)
    {
        captions.Add(new Caption { text = text, until = Time.time + CaptionSeconds });
        while (captions.Count > MaxCaptions) captions.RemoveAt(0);
    }

    string Direction(Vector3 worldPosition)
    {
        if (!player) return "";
        float dy = worldPosition.y - player.position.y;
        if (dy > 6f) return " upstairs";      // you're down in the cellar
        if (dy < -6f) return " in the cellar"; // you're upstairs
        float dx = worldPosition.x - player.position.x;
        if (Mathf.Abs(dx) < 3f) return " close by";
        return dx < 0f ? " to your left" : " to your right";
    }

    // ---------------------------------------------------------------- drawing (placeholder IMGUI)

    static GUIStyle centered, small;

    void OnGUI()
    {
        if (!NightRunning) return;
        GUI.depth = -100; // draw above the other debug HUDs

        if (centered == null)
        {
            centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true, fontSize = 16 };
            small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, fontSize = 12 };
        }

        if (State == SleepState.Up)
        {
            // Tiredness dimming
            float dim = Mathf.InverseLerp(tiredWarningLevel, 1f, Tiredness) * 0.45f;
            if (dim > 0f) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, dim));

            // Small tiredness bar under the NightManager panel
            var bar = new Rect(10, 76, 260, 14);
            Fill(bar, new Color(0, 0, 0, 0.6f));
            Fill(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * Tiredness, bar.height - 4),
                 Tiredness >= tiredWarningLevel ? new Color(0.8f, 0.3f, 0.2f) : new Color(0.75f, 0.7f, 0.5f));
            GUI.Label(new Rect(bar.x, bar.y + 14, bar.width, 18), $"Tiredness {Mathf.RoundToInt(Tiredness * 100)}%", small);

            DrawCaptions(Screen.height * 0.8f);
            return;
        }

        // Eyes closed: near-black screen
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.97f));

        float cx = Screen.width / 2f;
        const float sliderH = 260f, sliderW = 26f;
        var slider = new Rect(cx - sliderW / 2f, Screen.height / 2f - sliderH / 2f - 40f, sliderW, sliderH);
        float Y(float value) => slider.yMax - value * slider.height;

        if (State == SleepState.Resting)
        {
            GUI.Label(new Rect(cx - 60, slider.y - 24, 120, 20), "AWAKE", small);
            GUI.Label(new Rect(cx - 60, slider.yMax + 4, 120, 20), "ASLEEP", small);

            Fill(slider, new Color(0.15f, 0.15f, 0.2f));
            var band = Rect.MinMaxRect(slider.x, Y(bandHigh), slider.xMax, Y(bandLow));
            Fill(band, InBand ? new Color(0.35f, 0.55f, 0.35f) : new Color(0.25f, 0.35f, 0.25f));
            Fill(new Rect(slider.x - 8, Y(Doze) - 2, sliderW + 16, 4), new Color(0.95f, 0.85f, 0.55f));

            string state = InBand ? "Light doze: resting" : Doze > bandHigh ? "Too alert to rest" : "Drifting off...";
            GUI.Label(new Rect(cx - 200, slider.yMax + 26, 400, 24), state, centered);
            GUI.Label(new Rect(cx - 200, slider.yMax + 50, 400, 20),
                $"Hold {stayAwakeKey.ToString().ToUpper()} to stay awake   ·   E to get up", small);
        }
        else
        {
            GUI.Label(new Rect(cx - 200, Screen.height / 2f - 60, 400, 30), "Zzz...", centered);
            GUI.Label(new Rect(cx - 200, Screen.height / 2f - 30, 400, 20), "You are fast asleep and cannot move.", small);
        }

        GUI.Label(new Rect(cx - 200, slider.yMax + 72, 400, 20), $"Tiredness {Mathf.RoundToInt(Tiredness * 100)}%", small);
        DrawCaptions(slider.yMax + 110);

        if (IsShowingTip) DrawTip();
    }

    static GUIStyle tipText, tipHeader;

    void DrawTip()
    {
        if (tipText == null)
        {
            tipHeader = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
            tipText = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 15, richText = true };
        }
        const float w = 480f, h = 320f;
        var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        Fill(panel, new Color(0.12f, 0.1f, 0.08f, 0.98f));
        Fill(new Rect(panel.x, panel.y, w, 3), new Color(0.75f, 0.6f, 0.3f));

        GUILayout.BeginArea(new Rect(panel.x + 20, panel.y + 14, w - 40, h - 28));
        GUILayout.Label(tipTitle, tipHeader);
        GUILayout.Space(10);
        GUILayout.Label(tipBody, tipText);
        GUILayout.FlexibleSpace();
        GUILayout.Label("(Game paused)", small);
        if (GUILayout.Button("Got it", GUILayout.Height(34))) CloseTip();
        GUILayout.EndArea();
    }

    void DrawCaptions(float top)
    {
        float y = top;
        foreach (var c in captions)
        {
            GUI.Label(new Rect(Screen.width / 2f - 320, y, 640, 26), c.text, centered);
            y += 26;
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
