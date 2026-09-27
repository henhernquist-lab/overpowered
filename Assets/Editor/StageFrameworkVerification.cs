using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// The staged-mission layer (StagedScenario) with in-memory definitions in a real Hero session: completion, failure, timeout,
/// wrong-order controls, exactly-once transitions, no success after failure, one reward. Creates / saves no assets.
/// Output: Verification/StageFramework/results.txt.   Unity -batchmode -projectPath <copy> -executeMethod StageFrameworkVerification.Run
[InitializeOnLoad]
public static class StageFrameworkVerification
{
    const string Key = "Overpowered.StageFrameworkVerification"; const string Folder = "Verification/StageFramework/"; static double deadline;
    static StageFrameworkVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Stage framework verification timeout"); Finish(1); } }; }
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
        var go = new GameObject("Stage framework verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<StageFrameworkVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
