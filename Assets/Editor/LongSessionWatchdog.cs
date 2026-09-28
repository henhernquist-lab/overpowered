#if UNITY_EDITOR
// VERIFICATION GAUNTLET — editor entry point for the long-session watchdog (item 10, opt-in).
// Unity -batchmode -projectPath <project> -executeMethod LongSessionWatchdog.Run -logFile <log>   (no -quit)
// Optional: OP_WATCHDOG_MINUTES (default 3), OP_WATCHDOG_CONTROL (baseline census.csv for comparison).
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LongSessionWatchdog
{
    [MenuItem("Overpowered/Verification/Long Session Watchdog (5 game-minutes)")]
    public static void Run()
    {
        SessionState.SetString(Key, "run");
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }

    const string Key = "Overpowered.LongSessionWatchdog";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies();
        WorldSession.VerificationSavePath = Path.GetFullPath(Path.Combine(GauntletVerificationSupport.EvidenceRoot, "watchdog", "save-watchdog.json"));
        GameFlow.VerificationSandbox = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Long-session watchdog");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<LongSessionWatchdogRunner>();
        string minutes = Environment.GetEnvironmentVariable("OP_WATCHDOG_MINUTES");
        if (!string.IsNullOrEmpty(minutes) && float.TryParse(minutes, out var m) && m > 0) runner.Minutes = m;
    }
}
#endif
