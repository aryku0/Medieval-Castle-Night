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

    /// <summary>Every night starts with the candles out.</summary>
    void ResetForNight()
    {
        isLit = false;
        SetLitVisuals(false);
        SetBar(0f, false);
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
