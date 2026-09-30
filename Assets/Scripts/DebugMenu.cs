#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F1 debug panel for testing each system without waiting around.
/// Only compiled into the Unity editor and development builds; players never see it.
/// Every new feature should get its own section here.
/// </summary>
public class DebugMenu : MonoBehaviour
{
    bool open;
    Vector2 scroll;
    static GUIStyle header, info;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateIfMissing()
    {
        if (!FindAnyObjectByType<DebugMenu>())
            new GameObject("DebugMenu").AddComponent<DebugMenu>();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) open = !open;
    }

    void OnGUI()
    {
        if (!open) return;
        GUI.depth = -200; // on top of everything
        if (header == null)
        {
            header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13, richText = true };
            info = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, wordWrap = true };
        }

        var area = new Rect(10, 115, 320, Screen.height - 125);
        GUI.Box(area, "");
        GUI.Box(area, "");
        GUILayout.BeginArea(new Rect(area.x + 6, area.y + 4, area.width - 12, area.height - 8));
        GUILayout.Label("<b>DEBUG</b>   (F1 to close)", header);
        scroll = GUILayout.BeginScrollView(scroll);

        TimeSection();
        GateSection();
        WindowSection();
        CandleSection();
        SleepSection();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // ---------------------------------------------------------------- sections

    void TimeSection()
    {
        var night = NightManager.Instance;
        Section("Time & nights");
        if (night)
            GUILayout.Label($"Night {night.NightNumber} · {NightManager.FormatClock(night.ClockHours)} · {night.State} · speed x{GameTime.Speed} · purse {night.Purse}", info);

        GUILayout.BeginHorizontal();
        foreach (float s in new[] { 1f, 2f, 5f, 10f })
            if (GUILayout.Button($"x{s}")) SetSpeed(s);
        GUILayout.EndHorizontal();

        if (!night) return;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+1 hour")) night.DebugSkipHours(1f);
        if (GUILayout.Button("Skip to dawn")) night.DebugSkipToDawn();
        if (GUILayout.Button("Fail night")) night.FailNight("Failed from the debug menu.");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        for (int n = 1; n <= 3; n++)
            if (GUILayout.Button($"Night {n}")) night.DebugStartNight(n);
        if (GUILayout.Button("+50 coins")) night.DebugAddCoins(50);
        GUILayout.EndHorizontal();
    }

    void GateSection()
    {
        var gate = GateSystem.Instance;
        Section("Gate");
        if (!gate) { GUILayout.Label("GateSystem not running.", info); return; }
        GUILayout.Label("Right call: " + gate.DebugCorrectCall(), info);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Traveller")) gate.DebugSpawn(VisitorKind.Traveller);
        if (GUILayout.Button("Merchant")) gate.DebugSpawn(VisitorKind.Merchant);
        if (GUILayout.Button("Smuggler")) gate.DebugSpawn(VisitorKind.Smuggler);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Messenger")) gate.DebugSpawn(VisitorKind.Messenger);
        if (GUILayout.Button("Lady")) gate.DebugSpawn(VisitorKind.Lady);
        if (GUILayout.Button("Decoy lady")) gate.DebugSpawn(VisitorKind.Lady, forceDecoy: true);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GateSystem.DebugInfinitePatience = GUILayout.Toggle(GateSystem.DebugInfinitePatience, " Infinite patience");
        if (GUILayout.Button("Show orders")) gate.DebugShowOrders();
        GUILayout.EndHorizontal();
    }

    void WindowSection()
    {
        Section("Windows");
        foreach (var item in Interactable.All)
            if (item is SecurityWindow w) GUILayout.Label($"{w.name}: {w.DebugStatus}", info);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Attack random")) SecurityWindow.ForceRandomAttack();
        if (GUILayout.Button("Attack all")) SecurityWindow.DebugForceAll();
        GUILayout.EndHorizontal();
        SecurityWindow.DebugRandomAttacks = GUILayout.Toggle(SecurityWindow.DebugRandomAttacks, " Random attacks on");
    }

    void CandleSection()
    {
        Section("Candles");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Light / refill all"))
            foreach (var item in Interactable.All.ToArraySafe()) if (item is CandleInteraction c) c.DebugLight();
        if (GUILayout.Button("Burn out all"))
            foreach (var item in Interactable.All.ToArraySafe()) if (item is CandleInteraction c) c.DebugBurnOut();
        GUILayout.EndHorizontal();
    }

    void SleepSection()
    {
        var sleep = SleepSystem.Instance;
        Section("Sleep");
        if (!sleep) { GUILayout.Label("SleepSystem not running.", info); return; }
        GUILayout.Label($"{sleep.State} · tiredness {Mathf.RoundToInt(sleep.Tiredness * 100)}% · doze {sleep.Doze:0.00}", info);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Tired 0%")) sleep.DebugSetTiredness(0f);
        if (GUILayout.Button("70%")) sleep.DebugSetTiredness(0.7f);
        if (GUILayout.Button("100%")) sleep.DebugSetTiredness(1f);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Fall asleep now")) sleep.DebugFallAsleep();

        GUILayout.Label("Caught when asleep:", info);
        int mode = GUILayout.Toolbar((int)SleepSystem.DebugCatch, new[] { "Normal (1 in 3)", "Always", "Never" });
        SleepSystem.DebugCatch = (SleepSystem.DebugCatchMode)mode;

        if (GUILayout.Button("Reset tutorial pop-ups")) SleepSystem.DebugResetTips();
    }

    // ---------------------------------------------------------------- helpers

    static void Section(string title)
    {
        GUILayout.Space(8);
        GUILayout.Label($"<b>{title}</b>", header);
    }

    static void SetSpeed(float speed)
    {
        GameTime.Speed = speed;
        if (!GameTime.IsPaused) Time.timeScale = speed;
    }
}

static class DebugListExtensions
{
    /// <summary>Copy first, so buttons that change the list while we loop don't break it.</summary>
    public static Interactable[] ToArraySafe(this System.Collections.Generic.IReadOnlyList<Interactable> list)
    {
        var copy = new Interactable[list.Count];
        for (int i = 0; i < list.Count; i++) copy[i] = list[i];
        return copy;
    }
}
#endif
