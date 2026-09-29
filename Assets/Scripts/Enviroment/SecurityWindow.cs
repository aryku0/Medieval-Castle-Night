using UnityEngine;

public sealed class SecurityWindow : Interactable
{
    public Transform shutter;
    public GameObject intruder;
    public float firstAttemptDelay = 20f;
    public Vector2 attemptInterval = new Vector2(30f, 50f);
    public float openingSeconds = 14f;
    public float closingSeconds = 1.5f;
    public float shutterTravel = 1.2f;
    // interactionRange now lives on Interactable (same name, so existing Inspector values carry over).
    public int Breaches { get; private set; }
    public float Opening { get; private set; }
    Vector3 closedPosition;
    float countdown, breachedUntil;
    bool attacking;
    bool heldThisFrame;

    public override string Prompt => "[Hold E] Latch";

    void Start()
    {
        closedPosition = shutter ? shutter.localPosition : Vector3.zero;
        countdown = firstAttemptDelay;
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
            countdown -= dt;
            if (countdown <= 0f) attacking = true;
        }
        if (attacking)
        {
            Opening = Mathf.Clamp01(Opening + (closing ? -dt / Mathf.Max(0.1f, closingSeconds) : dt / Mathf.Max(0.1f, openingSeconds)));
            if (closing && Opening <= 0f) ResetAttempt();
            else if (Opening >= 1f)
            {
                Breaches++;
                breachedUntil = 3f;
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
        countdown = Random.Range(Mathf.Max(1f, attemptInterval.x), Mathf.Max(attemptInterval.x, attemptInterval.y));
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
