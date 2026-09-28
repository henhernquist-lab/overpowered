using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// EncounterSelection (optional weighted / anti-repeat / banded / district / seeded encounter picks): rule checks on
/// in-memory options, then real sessions proving the shipping modes are unchanged and a Selection drives SpawnNext.
/// Output: Verification/EncounterSelection/results.txt.   Unity -batchmode -projectPath <copy> -executeMethod EncounterSelectionVerification.Run
[InitializeOnLoad]
public static class EncounterSelectionVerification
{
    const string Key = "Overpowered.EncounterSelectionVerification"; const string Folder = "Verification/EncounterSelection/"; static double deadline;
    static EncounterSelectionVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Encounter selection verification timeout"); Finish(1); } }; }
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
        var go = new GameObject("Encounter selection verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<EncounterSelectionVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
