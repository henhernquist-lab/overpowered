using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// The four mission scenarios (robbery getaway, hostage rescue, building fire, vault heist) spawned at real city encounter
/// sites in real Hero / Villain sessions: objective line text, the mechanics that make each one more than "hold R", the win
/// path and the fail path of each. Creates the mission assets first (MissionSetup.Create); never switches the shipping modes.
/// Output: Verification/Missions/results.txt.   Unity -batchmode -projectPath <copy> -executeMethod MissionVerification.Run
[InitializeOnLoad]
public static class MissionVerification
{
    const string Key = "Overpowered.MissionVerification"; const string Folder = "Verification/Missions/"; static double deadline;
    static MissionVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Mission verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); MissionSetup.Create(); Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 1500; GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Mission verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<MissionVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
