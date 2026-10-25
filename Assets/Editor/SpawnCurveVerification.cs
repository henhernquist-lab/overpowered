using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// ENEMY SPAWN DIRECTOR 2.0 verification. Live batch Play Mode against the shipping mode data: both wave curves, the
/// deterministic drawn mix over them, the boss round budget, one-time difficulty application, a live Endless run through
/// the round-5 Pulsebreaker and Free Play isolation. Output: Verification/SpawnCurve/results.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod SpawnCurveVerification.Run
[InitializeOnLoad]
public static class SpawnCurveVerification
{
    const string Key = "Overpowered.SpawnCurveVerification"; const string Folder = "Verification/SpawnCurve/"; static double deadline;
    static SpawnCurveVerification()
    {
        EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Spawn curve verification timeout"); Finish(1); } };
    }
    public static void Run()
    {
        Directory.CreateDirectory(Folder + "saves");
        SessionState.SetString(Key, "run");
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + 600;
        GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Spawn curve verifier");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<SpawnCurveVerificationRunner>();
        runner.Finished = Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key); deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
