#if UNITY_EDITOR
// VERIFICATION GAUNTLET — editor entry point for the static-state audit + game-flow matrix (items 8, 9).
// Unity -batchmode -projectPath <project> -executeMethod StaticStateVerification.Run -logFile <log>   (no -quit)
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class StaticStateVerification
{
    const string Key = "Overpowered.StaticStateVerification";
    static double deadline;

    static StaticStateVerification()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Static-state verification timeout"); Finish(1); }
        };
    }

    public static void Run()
    {
        SessionState.SetString(Key, "run");
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + 3600;
        // Sentinel-armed sandbox saves; the runner sets per-leg paths itself.
        WorldSession.VerificationSavePath = Path.GetFullPath(Path.Combine(GauntletVerificationSupport.EvidenceRoot, "static-state", "save-default.json"));
        GameFlow.VerificationSandbox = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Static-state verification");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<StaticStateVerificationRunner>();
        runner.IncludeFlowMatrix = true;
        runner.Finished = Finish;
    }

    static void Finish(int code)
    {
        SessionState.EraseString(Key);
        deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
#endif
