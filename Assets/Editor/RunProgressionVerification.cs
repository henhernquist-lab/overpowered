using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Run progression (Endless Fight roguelite): catalog/formula table, then a real City session that starts at level 1 on a
/// levelled account, earns kill XP, pauses on a level-up, applies a picked card to the real power stats, pays the
/// Pulsebreaker reward through the boss bar/HUD, resets for the next run and leaves Free Play untouched.
/// Output: Verification/RunProgression/results.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod RunProgressionVerification.Run
[InitializeOnLoad]
public static class RunProgressionVerification
{
    const string Key = "Overpowered.RunProgressionVerification"; const string Folder = "Verification/RunProgression/"; static double deadline;
    static RunProgressionVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Run progression verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 900; GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Run progression verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<RunProgressionVerificationRunner>(); runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
