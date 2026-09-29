#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Nightguard > Apply Default Timings
/// Pushes the agreed timings onto every candle, window, NightManager and SleepSystem
/// already placed in the open scene(s). Undoable with Ctrl+Z.
/// </summary>
public static class NightguardTimings
{
    const float NightMinutes = 10f;
    const float ExhaustionMinutes = 5f;
    const float CandleBurnSeconds = 60f;
    const float WindowRollInterval = 10f;
    const float WindowAttemptChance = 0.15f;
    const float WindowQuietStart = 30f;
    const float WindowCooldown = 20f;
    const int MaxWindowsAtOnce = 2;
    const float WindowOpeningSeconds = 20f;

    [MenuItem("Tools/Nightguard/Apply Default Timings")]
    static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Nightguard", "Exit Play mode first.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply Nightguard timings");

        int candles = 0, windows = 0, managers = 0;

        foreach (var c in Find<CandleInteraction>())
        {
            Record(c);
            c.totalTime = CandleBurnSeconds;
            Done(c);
            candles++;
        }

        foreach (var w in Find<SecurityWindow>())
        {
            Record(w);
            w.rollInterval = WindowRollInterval;
            w.attemptChance = WindowAttemptChance;
            w.quietStart = WindowQuietStart;
            w.cooldownSeconds = WindowCooldown;
            w.maxWindowsAtOnce = MaxWindowsAtOnce;
            w.openingSeconds = WindowOpeningSeconds;
            Done(w);
            windows++;
        }

        foreach (var n in Find<NightManager>())
        {
            Record(n);
            n.realMinutesPerNight = NightMinutes;
            Done(n);
            managers++;
        }

        foreach (var s in Find<SleepSystem>())
        {
            Record(s);
            s.minutesToExhaustion = ExhaustionMinutes;
            Done(s);
            managers++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkAllScenesDirty();

        string msg = $"Updated {candles} candle(s), {windows} window(s) and {managers} manager(s).\nSave the scene (Ctrl+S) to keep the changes.";
        Debug.Log("[Nightguard] " + msg);
        EditorUtility.DisplayDialog("Nightguard", msg, "OK");
    }

    static T[] Find<T>() where T : Object =>
        Object.FindObjectsByType<T>(FindObjectsInactive.Include);

    static void Record(Object o) => Undo.RecordObject(o, "Apply Nightguard timings");

    static void Done(Object o)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        EditorUtility.SetDirty(o);
    }
}
#endif
