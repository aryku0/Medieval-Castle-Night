using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes sure the castle's managers (NightManager, SleepSystem, GateSystem) exist
/// every time a castle scene loads - including when it reloads for the next night.
/// The safehouse scene has its own SafehouseManager and skips these.
/// </summary>
public static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook() { SceneManager.sceneLoaded += OnSceneLoaded; }

    // Belt and braces for the very first scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FirstScene() { OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single); }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Object.FindAnyObjectByType<SafehouseManager>()) return; // safehouse: no night systems
        Ensure<NightManager>("NightManager");
        Ensure<SleepSystem>("SleepSystem");
        Ensure<GateSystem>("GateSystem");
    }

    static void Ensure<T>(string name) where T : Component
    {
        if (!Object.FindAnyObjectByType<T>())
            new GameObject(name).AddComponent<T>();
    }
}
