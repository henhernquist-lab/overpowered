using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// World-scale measurement (diagnostic only; changes nothing it measures).
/// Unity -batchmode -projectPath <project> -executeMethod WorldProfile.Run -logFile <log>
///   [-worldTag baseline] [-worldRounds 3] [-worldGens 3] [-worldCommit <hash>]
/// Writes Verification/World/<tag>/results.txt plus captures. Omit -quit; the runner exits with its own code.
[InitializeOnLoad]
public static class WorldProfile
{
    const string Key = "Overpowered.WorldProfile";
    static double deadline;

    static WorldProfile()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline)
            { Debug.LogError("World profile timeout"); Finish(1); }
        };
    }

    public static void Run()
    {
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + 5400;
        Directory.CreateDirectory("Verification/World/saves");
        WorldSession.VerificationSavePath = Path.GetFullPath("Verification/World/saves/profile-" + Guid.NewGuid().ToString("N") + ".json");
        GameFlow.VerificationSandbox = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!SessionState.GetBool(Key, false)) return;
        var go = new GameObject("World profile");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<WorldProfileRunner>().Finished = Finish;
    }

    static void Finish(int code)
    {
        SessionState.EraseBool(Key);
        deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
