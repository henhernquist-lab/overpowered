using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// The nine staged missions (StagedMissionLibrary -> StagedMissionSetup.Create, which only creates MISSING assets): data rules,
/// one failure path with mechanic controls and one full solve each, in real Hero / Villain sessions. Never changes the modes.
/// Output: Verification/StagedMissions/results.txt.   Unity -batchmode -projectPath <copy> -executeMethod StagedMissionVerification.Run
[InitializeOnLoad]
public static class StagedMissionVerification
{
    const string Key = "Overpowered.StagedMissionVerification"; const string Folder = "Verification/StagedMissions/"; static double deadline;
    static StagedMissionVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Staged mission verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); StagedMissionSetup.CreateRotations(); Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 2400; GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Staged mission verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<StagedMissionVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
