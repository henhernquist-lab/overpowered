using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// District diagnostics: off by default, interval sampling (not per frame), per-district attribution of time / hits /
/// civilian outcomes / XP / Heat / defeats / mission outcomes, CSV + JSON round-trip and the session-end auto write.
/// Output: Verification/DistrictDiagnostics/results.txt (+ out/ and auto/ file sets).
///   Unity -batchmode -projectPath <copy> -executeMethod DistrictDiagnosticsVerification.Run
[InitializeOnLoad]
public static class DistrictDiagnosticsVerification
{
    const string Key = "Overpowered.DistrictDiagnosticsVerification"; const string Folder = "Verification/DistrictDiagnostics/"; static double deadline;
    static DistrictDiagnosticsVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("District diagnostics verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 600; GameFlow.VerificationSandbox = false; DistrictDiagnostics.Enabled = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("District diagnostics verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<DistrictDiagnosticsVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
