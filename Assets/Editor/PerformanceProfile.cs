using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Batch entry point for the FPS regression diagnosis. Diagnostic only: changes nothing.
/// Unity -batchmode -projectPath <project> -executeMethod PerformanceProfile.Run -logFile <log>
/// (omit -quit; the runner exits with its own code).
[InitializeOnLoad]
public static class PerformanceProfile
{
    const string Key = "Overpowered.PerformanceProfile";
    static double deadline;

    static PerformanceProfile()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline)
            { Debug.LogError("Performance profile timeout"); Finish(1); }
        };
    }

    public static void Run()
    {
        Directory.CreateDirectory("Verification/Performance");
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + 600;
        WorldSession.VerificationSavePath = Path.GetFullPath("Verification/Performance/save-" + Guid.NewGuid().ToString("N") + ".json");
        GameFlow.VerificationSandbox = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!SessionState.GetBool(Key, false)) return;
        var go = new GameObject("Performance profile");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<PerformanceProfileRunner>().Finished = Finish;
    }

    static void Finish(int code)
    {
        SessionState.EraseBool(Key);
        deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
