using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Batch entry points for the FPS regression work. Diagnostic only: changes nothing.
/// Unity -batchmode -projectPath <project> -executeMethod PerformanceProfile.Run -logFile <log>
///   the control table on the current CityArtSettings modes -> Verification/Performance/results.txt
/// Unity -batchmode -projectPath <project> -executeMethod PerformanceProfile.Compare -logFile <log>
///   interleaved LEGACY/candidate geometry modes + visual identity -> Verification/Performance/comparison.txt
/// (omit -quit; the runner exits with its own code).
[InitializeOnLoad]
public static class PerformanceProfile
{
    const string Key = "Overpowered.PerformanceProfile";
    const string CompareKey = "Overpowered.PerformanceProfile.Compare";
    static double deadline;

    static PerformanceProfile()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline)
            { Debug.LogError("Performance profile timeout"); Finish(1); }
        };
    }

    public static void Run() => Start(false);
    public static void Compare() => Start(true);

    static void Start(bool comparison)
    {
        Directory.CreateDirectory("Verification/Performance");
        SessionState.SetBool(Key, true);
        SessionState.SetBool(CompareKey, comparison);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + (SessionState.GetBool(CompareKey, false) ? 2400 : 600);
        WorldSession.VerificationSavePath = Path.GetFullPath("Verification/Performance/save-" + Guid.NewGuid().ToString("N") + ".json");
        GameFlow.VerificationSandbox = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!SessionState.GetBool(Key, false)) return;
        var go = new GameObject("Performance profile");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<PerformanceProfileRunner>();
        runner.Comparison = SessionState.GetBool(CompareKey, false);
        runner.Finished = Finish;
    }

    static void Finish(int code)
    {
        SessionState.EraseBool(Key);
        SessionState.EraseBool(CompareKey);
        deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
