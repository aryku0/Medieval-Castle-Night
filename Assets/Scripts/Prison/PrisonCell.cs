using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One barred cell in the cellar (put on Cell 1-4 by Tools > Nightguard > Set Up Prisoners).
/// E: give bread if you carry some and they're hungry, otherwise talk.
/// When the prisoner forces the lock, the door creeps open like a window shutter: hold E to bar it.
/// Shows a placeholder figure (same size as the guard) while someone is inside.
/// </summary>
public class PrisonCell : Interactable
{
    [Header("Cell")]
    [Min(1)] public int cellNumber = 1;

    [Header("Placeholders")]
    [Tooltip("The prisoner figure, shown while the cell is occupied.")]
    public SpriteRenderer prisonerSprite;
    [Tooltip("The cell door; slides sideways as the lock is forced.")]
    public Transform door;
    public float doorTravel = 0.9f;
    public Color talkerColor = new Color(0.55f, 0.5f, 0.42f);
    public Color grunterColor = new Color(0.45f, 0.25f, 0.2f);

    static readonly List<PrisonCell> cells = new List<PrisonCell>();
    public static IReadOnlyList<PrisonCell> Cells => cells;

    public Prisoner Occupant { get; private set; }
    public bool IsForcing { get; private set; }
    /// <summary>0 = locked, 1 = forced open.</summary>
    public float Opening { get; private set; }

    float waitTimer, rollTimer;
    bool heldThisFrame;
    Vector3 doorHome, figureHome;
    bool homesKnown;

    PrisonSystem Sys => PrisonSystem.Instance;
    bool Starving => Occupant != null && Sys && Occupant.hunger < Sys.starvingBelow;
    bool NightRunning => !NightManager.Instance || NightManager.Instance.IsRunning;

    // ---------------------------------------------------------------- setup

    protected override void Awake()
    {
        base.Awake();
        BuildPlaceholders();
        RememberHomes();
        UpdateVisuals();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!cells.Contains(this)) cells.Add(this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        cells.Remove(this);
    }

    void RememberHomes()
    {
        if (door) doorHome = door.position;
        if (prisonerSprite) figureHome = prisonerSprite.transform.position;
        homesKnown = true;
    }

    /// <summary>
    /// Makes the placeholder prisoner and door if they're missing, using the cell's own sprite.
    /// Called by the editor tool (so you can see them) and at runtime as a fallback.
    /// Returns the objects it created.
    /// </summary>
    public List<GameObject> BuildPlaceholders()
    {
        var made = new List<GameObject>();
        var cellSr = GetComponent<SpriteRenderer>();
        if (!cellSr || !cellSr.sprite) return made;
        Sprite sprite = cellSr.sprite;
        Material material = cellSr.sharedMaterial;
        Transform parent = transform.parent; // the Cellar (unscaled), so sizes stay in metres
        Vector3 c = transform.position;
        Vector3 size = transform.lossyScale; // 2.6 x 3.2
        float floor = c.y - size.y * 0.5f;

        if (!prisonerSprite)
        {
            // Same size as the guard: 97 x 195 cm.
            var fig = Block($"Prisoner ({name})", sprite, material, parent, new Vector3(c.x, floor + 0.975f, c.z), new Vector2(0.97f, 1.95f), talkerColor, -2);
            prisonerSprite = fig.GetComponent<SpriteRenderer>();
            prisonerSprite.enabled = false;
            made.Add(fig);
        }

        if (!door)
        {
            var root = new GameObject($"CellDoor ({name})");
            root.transform.position = new Vector3(c.x, c.y, c.z);
            if (parent) root.transform.SetParent(parent, true);
            var iron = new Color(0.36f, 0.36f, 0.4f);
            const float w = 0.9f;
            for (int i = 0; i < 3; i++)
                Block("Bar", sprite, material, root.transform, new Vector3(c.x - w * 0.5f + 0.04f + i * (w - 0.08f) / 2f, c.y, c.z), new Vector2(0.07f, size.y), iron, -1);
            Block("Rail", sprite, material, root.transform, new Vector3(c.x, c.y + size.y * 0.38f, c.z), new Vector2(w, 0.07f), iron, -1);
            Block("Rail", sprite, material, root.transform, new Vector3(c.x, c.y - size.y * 0.38f, c.z), new Vector2(w, 0.07f), iron, -1);
            Block("Lock", sprite, material, root.transform, new Vector3(c.x + w * 0.5f - 0.04f, c.y - 0.1f, c.z - 0.01f), new Vector2(0.16f, 0.22f), new Color(0.7f, 0.55f, 0.2f), -1);
            door = root.transform;
            made.Add(root);
        }

        if (homesKnown) RememberHomes();
        return made;
    }

    static GameObject Block(string n, Sprite sprite, Material material, Transform parent, Vector3 pos, Vector2 size, Color color, int order)
    {
        var go = new GameObject(n);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        if (parent) go.transform.SetParent(parent, true);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (material) sr.sharedMaterial = material;
        sr.color = color;
        sr.sortingOrder = order;
        return go;
    }

    // ---------------------------------------------------------------- occupancy (used by PrisonSystem)

    public void ResetForNight(Prisoner p, float quiet)
    {
        Occupant = p;
        IsForcing = false;
        Opening = 0f;
        waitTimer = quiet;
        rollTimer = Random.Range(0f, Sys ? Sys.rollInterval : 10f); // cells don't all roll on the same beat
        UpdateVisuals();
    }

    public void LockUp(Prisoner p, float quiet) => ResetForNight(p, quiet);

    public void Release() => ResetForNight(null, 0f);

    public void StopForcing()
    {
        IsForcing = false;
        Opening = 0f;
        UpdateVisuals();
    }

    public void StartForcing()
    {
        if (Occupant == null || IsForcing) return;
        IsForcing = true;
        if (Sys) Sys.NoteAttempt();
        SleepSystem.Announce("A cell lock rattles", transform.position);
    }

    // ---------------------------------------------------------------- interaction

    public override bool CanInteract => base.CanInteract && Occupant != null && Sys;

    public override string Prompt
    {
        get
        {
            if (Occupant == null || !Sys) return null;
            if (IsForcing) return "[Hold E] Bar the cell door";
            if (Sys.Bread > 0 && Occupant.hunger < 0.9f) return $"[E] Give bread to {Occupant.name}";
            return Occupant.talkCooldown > 0f ? Occupant.name : $"[E] Talk to {Occupant.name}";
        }
    }

    public override Vector3 PromptPosition => TopOfCell;
    public override float PromptLiftPixels => 58f; // above the name and bars

    Vector3 TopOfCell => transform.position + Vector3.up * (transform.lossyScale.y * 0.5f);

    public override void Interact(GameObject player)
    {
        if (Occupant == null || !Sys || IsForcing) return; // while forced, holding E bars the door
        if (Sys.Bread > 0 && Occupant.hunger < 0.9f)
        {
            Sys.UseBread();
            SleepSystem.Announce(Sys.Feed(Occupant));
        }
        else if (Occupant.talkCooldown <= 0f)
        {
            SleepSystem.Announce(Sys.Talk(Occupant));
        }
    }

    public override void InteractHeld(GameObject player, float deltaTime) { heldThisFrame = true; }

    // ---------------------------------------------------------------- simulation

    void Update()
    {
        if (Occupant != null && Sys && NightRunning) Tick(Time.deltaTime);
        heldThisFrame = false;
        UpdateVisuals();
    }

    void Tick(float dt)
    {
        var p = Occupant;
        var sys = Sys;

        p.hunger = Mathf.Max(0f, p.hunger - dt / sys.secondsToStarve);
        p.mood = Mathf.Max(0f, p.mood - dt / sys.secondsMoodDrains * (Starving ? sys.starvingMoodMultiplier : 1f));
        if (p.talkCooldown > 0f) p.talkCooldown -= dt;

        // Anyone may try the lock now and then; the unhappier they are, the likelier.
        if (!IsForcing)
        {
            if (waitTimer > 0f) waitTimer -= dt;
            else
            {
                rollTimer -= dt;
                if (rollTimer <= 0f)
                {
                    rollTimer += sys.rollInterval;
                    if (sys.MayStartAttempt && Random.value < sys.EscapeChance(p.mood, transform.position))
                        StartForcing();
                }
            }
        }

        if (!IsForcing) return;

        bool wasWide = Opening >= 0.65f;
        Opening = Mathf.Clamp01(Opening + (heldThisFrame ? -dt / Mathf.Max(0.1f, sys.barSeconds) : dt / Mathf.Max(0.1f, sys.lockSeconds)));
        if (!wasWide && Opening >= 0.65f)
            SleepSystem.Announce("A cell door is giving way", transform.position);

        if (heldThisFrame && Opening <= 0f)
        {
            IsForcing = false;
            waitTimer = sys.cooldownAfterBarred;
            rollTimer = sys.rollInterval;
            SleepSystem.Announce($"You bar the door. {p.name} backs off.");
        }
        else if (Opening >= 1f)
        {
            Occupant = null;
            IsForcing = false;
            Opening = 0f;
            sys.ReportEscape(p, transform.position);
        }
    }

    void UpdateVisuals()
    {
        if (!homesKnown) RememberHomes();
        if (door) door.position = doorHome + Vector3.right * (Opening * doorTravel);
        if (!prisonerSprite) return;

        prisonerSprite.enabled = Occupant != null;
        if (Occupant == null) return;
        Color col = Occupant.grunter ? grunterColor : talkerColor;
        if (Starving) col = new Color(col.r * 0.6f, col.g * 0.6f, col.b * 0.6f, col.a);
        prisonerSprite.color = col;
        float shake = IsForcing ? Mathf.Sin(Time.time * 40f) * 0.05f : 0f;
        prisonerSprite.transform.position = figureHome + Vector3.right * shake;
    }

    // ---------------------------------------------------------------- name and bars (placeholder IMGUI)

    static GUIStyle nameStyle;

    void OnGUI()
    {
        if (!Camera.main || !NightRunning) return;
        UIScale.Apply();
        if (!UIScale.WorldToGui(Camera.main, TopOfCell, out Vector2 point)) return;
        if (point.x < 0 || point.x > UIScale.Width || point.y < 0 || point.y > UIScale.Height) return;
        if (nameStyle == null) nameStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, alignment = TextAnchor.MiddleCenter };

        const float w = 150f;
        if (Occupant == null)
        {
            GUI.Label(new Rect(point.x - w / 2f, point.y - 22f, w, 18f), "<color=#999999>Empty cell</color>", nameStyle);
            return;
        }

        var box = new Rect(point.x - w / 2f, point.y - 50f, w, 46f);
        GUI.Box(box, "");
        string label = IsForcing ? $"<color=#ff8866><b>{Occupant.name}</b> forcing the lock {Mathf.RoundToInt(Opening * 100)}%</color>" : $"<b>{Occupant.name}</b>";
        GUI.Label(new Rect(box.x, box.y + 1f, w, 16f), label, nameStyle);
        Bar(new Rect(box.x + 8f, box.y + 20f, w - 16f, 9f), Occupant.hunger, new Color(0.85f, 0.6f, 0.25f), "Food");
        Bar(new Rect(box.x + 8f, box.y + 33f, w - 16f, 9f), Occupant.mood, new Color(0.4f, 0.7f, 0.9f), "Mood");
    }

    static void Bar(Rect r, float value, Color color, string label)
    {
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = value < 0.2f ? new Color(0.9f, 0.3f, 0.25f) : color;
        GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(value), r.height - 2f), Texture2D.whiteTexture);
        GUI.color = old;
    }
}
