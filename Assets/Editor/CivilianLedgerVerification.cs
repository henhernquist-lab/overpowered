using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Session civilian outcomes: honest attribution, counted once, mission loss vs despawn / cleanup, legacy rescue, staged
/// escort, summary on the result, nothing in the save. Output: Verification/CivilianLedger/results.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod CivilianLedgerVerification.Run
[InitializeOnLoad]
public static class CivilianLedgerVerification
{
    const string Key = "Overpowered.CivilianLedgerVerification"; const string Folder = "Verification/CivilianLedger/"; static double deadline;
    static CivilianLedgerVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Civilian ledger verification timeout"); Finish(1); } }; }
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
        var go = new GameObject("Civilian ledger verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<CivilianLedgerVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
