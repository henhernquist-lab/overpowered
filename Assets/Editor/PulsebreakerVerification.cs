using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// PULSEBREAKER miniboss verification. Live batch Play Mode: the shipped mode data, an in-memory Endless clone for the
/// round-5 spawn, and controlled combat fixtures for the three attacks, the phase shift, the boss bar and the ordinary
/// enemy regression controls. Output: Verification/Pulsebreaker/results.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod PulsebreakerVerification.Run
[InitializeOnLoad]
public static class PulsebreakerVerification
{
    const string Key = "Overpowered.PulsebreakerVerification"; const string Folder = "Verification/Pulsebreaker/"; static double deadline;
    static PulsebreakerVerification()
    {
        EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Pulsebreaker verification timeout"); Finish(1); } };
    }
    public static void Run()
    {
        Directory.CreateDirectory(Folder + "saves");
        SessionState.SetString(Key, "run");
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }
    /// Separate process: reopens the save the Run wrote (no new save file, no asset writes).
    public static void Reload()
    {
        if (!File.Exists(Path.GetFullPath(Folder + "save-path.txt"))) { Debug.LogError("No save path from a Pulsebreaker Run"); EditorApplication.Exit(1); return; }
        SessionState.SetString(Key, "reload");
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + 900;
        GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = SessionState.GetString(Key, "") == "reload"
            ? File.ReadAllText(Path.GetFullPath(Folder + "save-path.txt")).Trim()
            : Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Pulsebreaker verifier");
        UnityEngine.Object.DontDestroyOnLoad(go);
        if (SessionState.GetString(Key, "") == "reload") { var reload = go.AddComponent<PulsebreakerReloadRunner>(); reload.Finished = Finish; return; }
        var runner = go.AddComponent<PulsebreakerVerificationRunner>();
        runner.Finished = Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key); deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
