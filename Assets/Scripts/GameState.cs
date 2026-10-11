using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Everything that has to survive moving between the castle and the safehouse:
/// night number, purse (negative = in debt), upgrades, medicine, prisoners.
/// Also holds the economy numbers so they're all in one place.
/// </summary>
public static class GameState
{
    // ---- economy numbers
    public const int Taxes = 6;
    public const int ArmsUpkeep = 4;
    public static int Bills => Taxes + ArmsUpkeep;
    public const int DebtInterest = 2;
    public const int DaysInDebtToBeDismissed = 2;

    public const int StoolPrice = 15;
    public const int BeeswaxPrice = 12;
    public const int MedicinePrice = 10;
    public const float BeeswaxBurnMultiplier = 1.5f;  // candles 60s -> 90s
    public const float StoolRestMultiplier = 1.6f;    // rest ~60% faster
    public const float InfectionChance = 0.3f;        // per visitor admitted on smallpox night without medicine

    public const int LastDemoNight = 3;
    public const string SafehouseSceneName = "Safehouse";

    // ---- state
    public static int Night = 1;
    public static int Purse;
    public static int DaysInDebt;
    public static bool BillsPaidToday;
    public static bool HasStool, HasBeeswax, HasMedicine;
    /// <summary>Prisoners still in the cellar at the end of the last night (kept for the next one).</summary>
    public static List<Prisoner> Cellar = new List<Prisoner>();
    /// <summary>Everyone arrested over the whole game (counted at each dawn).</summary>
    public static int TotalArrests;
    public static bool SickTonight;

    /// <summary>Name of the castle scene, recorded by NightManager when it starts.</summary>
    public static string CastleScene;

    public static bool InDebt => Purse < 0;
    public static bool SmallpoxNight => Night >= 3;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void NewGame()
    {
        Night = 1;
        Purse = 0;
        DaysInDebt = 0;
        BillsPaidToday = false;
        HasStool = HasBeeswax = HasMedicine = false;
        Cellar = new List<Prisoner>();
        TotalArrests = 0;
        SickTonight = false;
    }

    // ---- scene travel

    /// <summary>Load the safehouse. Returns false if that scene isn't in the build list yet.</summary>
    public static bool GoHome()
    {
        if (!Application.CanStreamedLevelBeLoaded(SafehouseSceneName))
        {
            Debug.LogWarning("[GameState] No 'Safehouse' scene in the build list yet. Use Tools > Nightguard > Create Safehouse Scene.");
            return false;
        }
        BillsPaidToday = false;
        GameTime.Resume();
        SceneManager.LoadScene(SafehouseSceneName);
        return true;
    }

    /// <summary>Load the castle for tonight (Night should already be set).</summary>
    public static void GoToWork()
    {
        GameTime.Resume();
        if (!string.IsNullOrEmpty(CastleScene) && Application.CanStreamedLevelBeLoaded(CastleScene))
            SceneManager.LoadScene(CastleScene);
        else
            SceneManager.LoadScene(0); // first scene in the build list
    }

    public static void StartOver()
    {
        NewGame();
        GoToWork();
    }
}
