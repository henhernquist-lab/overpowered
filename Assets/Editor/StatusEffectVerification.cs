using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// NPC status audit (Freeze / Root / Poison; Lightning instant; Force Field is the player's shield, not a status): expiry,
/// overlap, death / despawn cleanup, pause, stacking, and the dash-shatter base interaction with controls.
/// Output: Verification/StatusEffects/results.txt.   Unity -batchmode -projectPath <copy> -executeMethod StatusEffectVerification.Run
[InitializeOnLoad]
public static class StatusEffectVerification
{
    const string Key = "Overpowered.StatusEffectVerification"; const string Folder = "Verification/StatusEffects/"; static double deadline;
    static StatusEffectVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Status effect verification timeout"); Finish(1); } }; }
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
        var go = new GameObject("Status effect verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<StatusEffectVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
