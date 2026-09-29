using UnityEngine;
using UnityEngine.UI;

public class CandleInteraction : Interactable
{
    [Header("Candle")]
    public GameObject candleFlame;
    public GameObject candleLight;

    [Header("Timer")]
    public Image timerBar;

    public float totalTime = 20f;
    public float timePerChunk = 4f;

    private bool isLit = false;
    private float timeRemaining;
    private float chunkTimer;

    public bool IsLit => isLit;
    public override string Prompt => isLit ? "[E] Tend candle" : "[E] Light candle";

    void Start()
    {
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

            SetBar(timeRemaining / totalTime, true);

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

    void LightCandle()
    {
        isLit = true;
        SetLitVisuals(true);
        Refill();
        Debug.Log("Candle lit!");
    }

    void Refill()
    {
        timeRemaining = totalTime;
        chunkTimer = timePerChunk;
        SetBar(1f, true);
    }

    void ExtinguishCandle()
    {
        isLit = false;
        SetLitVisuals(false);
        SetBar(0f, false);
        Debug.Log("Candle went out!");
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
