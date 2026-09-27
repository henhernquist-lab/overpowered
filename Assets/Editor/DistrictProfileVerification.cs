using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// District gameplay profiles: unset = old numbers, one-value clones, boundary crossing, no name literals, seed determinism.
/// Uses in-memory profile sets only (never enables the shipped asset). Output: Verification/DistrictProfiles/results.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod DistrictProfileVerification.Run
[InitializeOnLoad]
public static class DistrictProfileVerification
{
    const string Key = "Overpowered.DistrictProfileVerification"; const string Folder = "Verification/DistrictProfiles/"; static double deadline;
    static DistrictProfileVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("District profile verification timeout"); Finish(1); } }; }
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
        var go = new GameObject("District profile verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<DistrictProfileVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
