using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Endless wave events live: shipping director unchanged (control), then an in-memory events clone - alive budget, per-slot
/// enemy stats, every score event against the session score, flawless and its hit control. Saves no asset.
/// Output: Verification/EndlessEvents/results.txt.   Unity -batchmode -projectPath <copy> -executeMethod EndlessEventsVerification.Run
[InitializeOnLoad]
public static class EndlessEventsVerification
{
    const string Key = "Overpowered.EndlessEventsVerification"; const string Folder = "Verification/EndlessEvents/"; static double deadline;
    static EndlessEventsVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Endless events verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 600; GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Endless events verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<EndlessEventsVerificationRunner>(); runner.Recipe = EndlessEventsSetup.Build(); runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
