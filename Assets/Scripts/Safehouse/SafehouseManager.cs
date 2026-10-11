using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runs your lodgings between nights: paying bills, buying upgrades and medicine,
/// and going to bed (which checks debt and starts the next night).
/// Lives in the Safehouse scene (the Create Safehouse Scene tool adds it).
/// Placeholder IMGUI for now.
/// </summary>
public class SafehouseManager : MonoBehaviour
{
    public enum Panel { None, Bills, Upgrades, Medicine, Bed, Dismissed }

    public static SafehouseManager Instance { get; private set; }

    public Panel Open { get; private set; }
    public bool IsPanelOpen => Open != Panel.None;

    int openedFrame;
    string message;
    float messageUntil;
    static GUIStyle rich, big, buttonRich;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        GameTime.Resume();
        Say(GameState.InDebt
            ? "Home at last. You still owe money, and taxes and upkeep are due before you sleep."
            : "Home at last. Taxes and upkeep are due before you sleep.");
    }

    void Update()
    {
        if (Open == Panel.None || Open == Panel.Dismissed) return;
        var kb = Keyboard.current;
        if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) && Time.frameCount != openedFrame)
            Open = Panel.None;
    }

    public void OpenPanel(Panel panel)
    {
        Open = panel;
        openedFrame = Time.frameCount;
    }

    void Say(string text, float seconds = 5f)
    {
        message = text;
        messageUntil = Time.time + seconds;
    }

    // ---------------------------------------------------------------- actions

    void PayBills()
    {
        if (GameState.BillsPaidToday) return;
        if (GameState.Purse < GameState.Bills) { Say("You can't afford the bills."); return; }
        GameState.Purse -= GameState.Bills;
        GameState.BillsPaidToday = true;
        Say($"Bills paid. {GameState.Purse} coins left.");
    }

    void BuyStool()
    {
        if (GameState.HasStool || GameState.Purse < GameState.StoolPrice) return;
        GameState.Purse -= GameState.StoolPrice;
        GameState.HasStool = true;
        Say("A cushion for your stool at the post. You'll rest faster.");
    }

    void BuyBeeswax()
    {
        if (GameState.HasBeeswax || GameState.Purse < GameState.BeeswaxPrice) return;
        GameState.Purse -= GameState.BeeswaxPrice;
        GameState.HasBeeswax = true;
        Say("Beeswax candles. They'll burn half as long again.");
    }

    void BuyMedicine()
    {
        if (GameState.HasMedicine || GameState.Purse < GameState.MedicinePrice) return;
        GameState.Purse -= GameState.MedicinePrice;
        GameState.HasMedicine = true;
        Say("A bitter tonic from the apothecary. It should keep the pox away.");
    }

    void GoToSleep()
    {
        // Unpaid bills don't disappear: they become debt.
        if (!GameState.BillsPaidToday)
        {
            GameState.Purse -= GameState.Bills;
            GameState.BillsPaidToday = true;
        }

        if (GameState.InDebt) GameState.DaysInDebt++;
        else GameState.DaysInDebt = 0;

        if (GameState.DaysInDebt >= GameState.DaysInDebtToBeDismissed)
        {
            OpenPanel(Panel.Dismissed);
            return;
        }

        GameState.Night++;
        GameState.GoToWork();
    }

    // ---------------------------------------------------------------- drawing

    void Styles()
    {
        if (rich != null) return;
        rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 14 };
        big = new GUIStyle(rich) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        buttonRich = new GUIStyle(GUI.skin.button) { richText = true, fontSize = 14 };
    }

    void OnGUI()
    {
        UIScale.Apply(); // scale the whole UI with the screen
        Styles();
        GUI.depth = -50;
        DrawHud();

        if (!string.IsNullOrEmpty(message) && Time.time < messageUntil)
            GUI.Label(new Rect(UIScale.Width / 2f - 320, UIScale.Height * 0.82f, 640, 30), $"<i>{message}</i>",
                new GUIStyle(rich) { alignment = TextAnchor.MiddleCenter });

        switch (Open)
        {
            case Panel.Bills: DrawBills(); break;
            case Panel.Upgrades: DrawUpgrades(); break;
            case Panel.Medicine: DrawMedicine(); break;
            case Panel.Bed: DrawBed(); break;
            case Panel.Dismissed: DrawDismissed(); break;
        }
    }

    void DrawHud()
    {
        string purse = GameState.InDebt ? $"<color=#ff8866>{GameState.Purse} coins (in debt)</color>" : $"{GameState.Purse} coins";
        string bills = GameState.BillsPaidToday ? "paid" : $"<color=#ffcc66>{GameState.Bills} due</color>";
        string owned = "";
        if (GameState.HasStool) owned += "cushioned stool  ";
        if (GameState.HasBeeswax) owned += "beeswax candles  ";
        if (GameState.HasMedicine) owned += "medicine";
        if (owned == "") owned = "nothing yet";

        string text = $"<b>Your lodgings</b>   Day after night {GameState.Night}\n"
            + $"Purse: {purse}   Bills: {bills}\n"
            + $"Owned: {owned}";
        if (GameState.DaysInDebt > 0)
            text += "\n<color=#ff8866>You were in debt yesterday. End today in debt and you're dismissed!</color>";

        float h = GameState.DaysInDebt > 0 ? 84 : 64;
        GUI.Box(new Rect(10, 10, 380, h), "");
        GUI.Label(new Rect(16, 12, 368, h), text, rich);
    }

    Rect BeginPanel(string title, float h = 300f)
    {
        const float w = 440f;
        var r = new Rect((UIScale.Width - w) / 2f, (UIScale.Height - h) / 2f, w, h);
        var old = GUI.color;
        GUI.color = new Color(0.14f, 0.11f, 0.08f, 0.97f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.75f, 0.6f, 0.3f);
        GUI.DrawTexture(new Rect(r.x, r.y, w, 3), Texture2D.whiteTexture);
        GUI.color = old;
        GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, w - 40, h - 28));
        GUILayout.Label(title, big);
        GUILayout.Space(10);
        return r;
    }

    void EndPanel(bool closeButton = true)
    {
        GUILayout.FlexibleSpace();
        if (closeButton && GUILayout.Button("Close (E)", GUILayout.Height(30))) Open = Panel.None;
        GUILayout.EndArea();
    }

    void DrawBills()
    {
        BeginPanel("Bills on the table");
        GUILayout.Label($"Crown taxes: <b>{GameState.Taxes}</b>\nArms upkeep: <b>{GameState.ArmsUpkeep}</b>\nTotal due today: <b>{GameState.Bills}</b>", rich);
        GUILayout.Space(8);
        if (GameState.BillsPaidToday)
            GUILayout.Label("Paid for today.", rich);
        else if (GameState.Purse >= GameState.Bills)
        {
            if (GUILayout.Button($"Pay {GameState.Bills} coins", buttonRich, GUILayout.Height(34))) PayBills();
        }
        else
            GUILayout.Label($"<color=#ff8866>You only have {GameState.Purse}. If you sleep without paying, it becomes debt (+{GameState.DebtInterest} interest from your next wage).</color>", rich);
        EndPanel();
    }

    void DrawUpgrades()
    {
        BeginPanel("Your chest", 320f);
        GUILayout.Label($"Purse: {GameState.Purse} coins", rich);
        GUILayout.Space(6);
        ShopRow("Cushioned stool", $"Rest at your post recovers tiredness about {Mathf.RoundToInt((GameState.StoolRestMultiplier - 1f) * 100)}% faster.",
            GameState.StoolPrice, GameState.HasStool, BuyStool);
        GUILayout.Space(6);
        ShopRow("Beeswax candles", $"Candles burn {GameState.BeeswaxBurnMultiplier:0.#}x as long.",
            GameState.BeeswaxPrice, GameState.HasBeeswax, BuyBeeswax);
        EndPanel();
    }

    void DrawMedicine()
    {
        BeginPanel("Apothecary jar");
        if (GameState.Night >= 2)
            GUILayout.Label("<i>There's talk of pox in the lower town...</i>", rich);
        ShopRow("Pox tonic", $"Protects you from catching the pox from people you let in. Without it, each one has a {Mathf.RoundToInt(GameState.InfectionChance * 100)}% chance of giving you a fever.",
            GameState.MedicinePrice, GameState.HasMedicine, BuyMedicine);
        EndPanel();
    }

    void ShopRow(string name, string description, int price, bool owned, System.Action buy)
    {
        GUILayout.Label($"<b>{name}</b>  ({price} coins)\n{description}", rich);
        if (owned) GUILayout.Label("<color=#9fd69f>Owned</color>", rich);
        else if (GameState.Purse >= price)
        {
            if (GUILayout.Button($"Buy for {price}", buttonRich, GUILayout.Height(30))) buy();
        }
        else GUILayout.Label("<color=#aaaaaa>Can't afford it yet.</color>", rich);
    }

    void DrawBed()
    {
        BeginPanel("Your bed");
        GUILayout.Label($"Sleep through the day. Night {GameState.Night + 1} begins when you wake.", rich);
        if (!GameState.BillsPaidToday)
        {
            int after = GameState.Purse - GameState.Bills;
            GUILayout.Label($"<color=#ffcc66>Your bills ({GameState.Bills}) aren't paid. They'll be added as debt.</color>", rich);
            if (after < 0 && GameState.DaysInDebt >= GameState.DaysInDebtToBeDismissed - 1)
                GUILayout.Label("<color=#ff8866><b>You were in debt yesterday too. The Captain will dismiss you!</b></color>", rich);
        }
        else if (GameState.InDebt && GameState.DaysInDebt >= GameState.DaysInDebtToBeDismissed - 1)
            GUILayout.Label("<color=#ff8866><b>You're still in debt from yesterday. The Captain will dismiss you!</b></color>", rich);

        GUILayout.Space(8);
        if (GUILayout.Button("Sleep until nightfall", buttonRich, GUILayout.Height(34))) GoToSleep();
        EndPanel();
    }

    void DrawDismissed()
    {
        BeginPanel("Dismissed");
        GUILayout.Label("A letter under the door bears the Captain's seal:\n\n<i>\"You have failed to settle your debts two days running. Hand in your halberd. You are relieved of the watch.\"</i>", rich);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Start over", buttonRich, GUILayout.Height(34))) GameState.StartOver();
        EndPanel(closeButton: false);
    }
}
