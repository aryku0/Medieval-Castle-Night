using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CandleInteraction : Interactable
{
    [Header("Candle")]
    public GameObject candleFlame;
    public GameObject candleLight;

    [Header("Timer")]
    public Image timerBar;

    [Tooltip("Burn time in seconds. Beeswax candles (safehouse upgrade) make it 1.5x longer.")]
    public float totalTime = 60f;

    /// <summary>Actual burn time tonight, including the beeswax upgrade.</summary>
    public float BurnTime => totalTime * (GameState.HasBeeswax ? GameState.BeeswaxBurnMultiplier : 1f);
    public float timePerChunk = 4f;

    private bool isLit = false;
    private float timeRemaining;
    private float chunkTimer;

    public bool IsLit => isLit;
    public override string Prompt => isLit ? "[E] Tend candle" : "[E] Light candle";

    void Start()
    {
        ResetForNight();
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

    /// <summary>
    /// Every night starts with the candles lit, except the one nearest where you start (your first job).
    /// The lit ones start part-burned at staggered levels, nearest first, so they go out one by one.
    /// </summary>
    void ResetForNight()
    {
        var hero = GameObject.FindGameObjectWithTag("Player");
        Vector3 start = hero ? hero.transform.position : transform.position;

        var candles = new List<CandleInteraction>();
        foreach (var item in All) if (item is CandleInteraction c) candles.Add(c);
        candles.Sort((a, b) => Vector2.Distance(a.transform.position, start).CompareTo(Vector2.Distance(b.transform.position, start)));
        int rank = candles.IndexOf(this);

        if (rank <= 0) // the starting candle (or the only one): out
        {
            isLit = false;
            SetLitVisuals(false);
            SetBar(0f, false);
            return;
        }

        // Spread the others between 40% and 100% burn left, the nearest ones lowest.
        int others = candles.Count - 1;
        float level = Mathf.Lerp(startLitMin, 1f, (rank - 0.5f) / others) + Random.Range(-0.05f, 0.05f);
        isLit = true;
        SetLitVisuals(true);
        timeRemaining = BurnTime * Mathf.Clamp(level, 0.1f, 1f);
        chunkTimer = timePerChunk;
        SetBar(timeRemaining / BurnTime, true);
    }

    [Tooltip("Lowest burn level (0-1) a candle can start the night at.")]
    [Range(0.1f, 1f)] public float startLitMin = 0.4f;

    /// <summary>The candle closest to a point (null if there are none).</summary>
    public static CandleInteraction Nearest(Vector3 point)
    {
        CandleInteraction best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var item in All)
        {
            if (!(item is CandleInteraction c)) continue;
            float d = Vector2.Distance(c.transform.position, point);
            if (d < bestDistance) { bestDistance = d; best = c; }
        }
        return best;
    }

    /// <summary>Is the candle nearest this point out? Dark spots are where intruders and prisoners try their luck.</summary>
    public static bool IsDark(Vector3 point)
    {
        var c = Nearest(point);
        return c != null && !c.IsLit;
    }

    void Update()
    {
        if (!isLit)
            return;

        chunkTimer -= Time.deltaTime;

        if (chunkTimer <= 0f)
        {
            timeRemaining -= timePerChunk;
            chunkTimer = timePerChunk;

            SetBar(timeRemaining / BurnTime, true);

            // Last chunk left: the flame starts to gutter.
            if (timeRemaining > 0f && timeRemaining <= timePerChunk)
                SleepSystem.Cue("a candle flame gutters", transform.position);

            if (timeRemaining <= 0f)
            {
                ExtinguishCandle();
            }
        }
    }

    public override void Interact(GameObject player)
    {
        if (!isLit)
            LightCandle();
        else
            Refill();
    }

    // ---- debug hooks (used by DebugMenu)
    public void DebugLight() { if (!isLit) LightCandle(); else Refill(); }
    public void DebugBurnOut() { if (isLit) ExtinguishCandle(); }

    void LightCandle()
    {
        isLit = true;
        SetLitVisuals(true);
        Refill();
        Debug.Log("Candle lit!");
    }

    void Refill()
    {
        timeRemaining = BurnTime;
        chunkTimer = timePerChunk;
        SetBar(1f, true);
    }

    void ExtinguishCandle()
    {
        isLit = false;
        SetLitVisuals(false);
        SetBar(0f, false);
        Debug.Log("Candle went out!");
        if (NightManager.Instance) NightManager.Instance.ReportCandleOut(name);
        SleepSystem.Cue("a candle hisses out", transform.position);
    }

    void SetLitVisuals(bool lit)
    {
        if (candleFlame != null) candleFlame.SetActive(lit);
        if (candleLight != null) candleLight.SetActive(lit);
    }

    void SetBar(float fill, bool visible)
    {
        if (timerBar == null) return;
        timerBar.fillAmount = Mathf.Clamp01(fill);
        timerBar.gameObject.SetActive(visible);
    }
}
