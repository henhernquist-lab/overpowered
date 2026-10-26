using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// TWO MODES + GUARANTEED PULSEBREAKER ROUNDS. Live batch Play Mode: the mode catalog and the real Home cards, real card
/// submits into Endless Fight and Free Play, the boss schedule for rounds 1-30 as data, live boss rounds 5 and 10 with the
/// boss bar, and the save the finished run leaves behind. Free Play is held for three seconds to prove no boss appears.
/// Output: Verification/TwoModes/results.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod TwoModesVerification.Run
[InitializeOnLoad]
public static class TwoModesVerification
{
    const string Key = "Overpowered.TwoModesVerification"; const string Folder = "Verification/TwoModes/"; static double deadline;
    static TwoModesVerification()
    {
        EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Two-mode verification timeout"); Finish(1); } };
    }
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        SessionState.SetString(Key, "run");
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
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Two-mode verifier");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<TwoModesVerificationRunner>();
        runner.Finished = Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key); deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
