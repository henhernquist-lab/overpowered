using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// All 55 two-power loadouts in real sessions (equip, synergy resolution, generic lifecycle of all 11 powers, synergy attempt,
/// teardown), each save snapshotted; Reload re-reads all 55 snapshots in a second process.
/// Output: Verification/LoadoutMatrix/results.txt, reload.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod LoadoutMatrixVerification.Run   then   LoadoutMatrixVerification.Reload
[InitializeOnLoad]
public static class LoadoutMatrixVerification
{
    const string Key = "Overpowered.LoadoutMatrixVerification"; const string Folder = "Verification/LoadoutMatrix/"; static double deadline;
    static LoadoutMatrixVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Loadout matrix verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); Directory.CreateDirectory(Folder); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    public static void Reload() { SessionState.SetString(Key, "reload"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode = SessionState.GetString(Key, ""); if (mode == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 3600; GameFlow.VerificationSandbox = false;
        string path = mode == "reload" ? File.ReadAllText(Folder + "save-path.txt").Trim() : Path.GetFullPath(Folder + "save-" + Guid.NewGuid().ToString("N") + ".json");
        WorldSession.VerificationSavePath = path; File.WriteAllText(Folder + "save-path.txt", path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode = SessionState.GetString(Key, ""); if (mode == "") return;
        var go = new GameObject("Loadout matrix verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<LoadoutMatrixVerificationRunner>(); runner.Reload = mode == "reload"; runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
