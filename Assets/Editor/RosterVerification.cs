using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// The six new powers (Darkness, Laser Eyes, Lightning, Force Field, Speed, Poison) and the three capped new synergies, each
/// equipped through the saved Hero Forge loadout and exercised through PowerUser.Use / SynergyRunner.TryActivate (what the
/// mouse button and C key call), with positive and negative controls. Run creates the roster data first (RosterSetup).
/// Output: Verification/Roster/results.txt (+ reload.txt for the separate-process Reload).
///   Unity -batchmode -projectPath <copy> -executeMethod RosterVerification.Run      (omit -quit)
///   Unity -batchmode -projectPath <copy> -executeMethod RosterVerification.Reload   (second process)
[InitializeOnLoad]
public static class RosterVerification
{
    const string Key = "Overpowered.RosterVerification"; const string Folder = "Verification/Roster/"; static double deadline;
    static RosterVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Roster verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); Begin("run"); }
    public static void Reload() { Begin("reload"); }
    static void Begin(string mode) { Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, mode); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode = SessionState.GetString(Key, ""); if (mode == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 1200; GameFlow.VerificationSandbox = false;
        string pointer = Folder + "save-path.txt";
        string path = mode == "reload" ? File.ReadAllText(pointer) : Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
        WorldSession.VerificationSavePath = path; if (mode == "run") File.WriteAllText(pointer, path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode = SessionState.GetString(Key, ""); if (mode == "") return;
        var go = new GameObject("Roster verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<RosterVerificationRunner>(); runner.Reload = mode == "reload"; runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
