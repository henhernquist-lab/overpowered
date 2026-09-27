using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Per-power stats (instrumentation): two real sessions record ice / strength / poison / speed stats against the hits that
/// caused them, with uncredited-damage controls; Reload (a second Unity process) reads the sandbox save back.
/// Output: Verification/PowerStats/results.txt, reload.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod PowerStatsVerification.Run   then   PowerStatsVerification.Reload
[InitializeOnLoad]
public static class PowerStatsVerification
{
    const string Key = "Overpowered.PowerStatsVerification"; const string Folder = "Verification/PowerStats/"; static double deadline;
    static PowerStatsVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Power stats verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); Directory.CreateDirectory(Folder); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    public static void Reload() { SessionState.SetString(Key, "reload"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode = SessionState.GetString(Key, ""); if (mode == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 600; GameFlow.VerificationSandbox = false;
        string path = mode == "reload" ? File.ReadAllText(Folder + "save-path.txt").Trim() : Path.GetFullPath(Folder + "save-" + Guid.NewGuid().ToString("N") + ".json");
        WorldSession.VerificationSavePath = path; File.WriteAllText(Folder + "save-path.txt", path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode = SessionState.GetString(Key, ""); if (mode == "") return;
        var go = new GameObject("Power stats verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<PowerStatsVerificationRunner>(); runner.Reload = mode == "reload"; runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
