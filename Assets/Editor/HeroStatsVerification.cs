using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Hero archetypes (VECTOR baseline, TITAN tank, NOVA energy): every HeroStats value is shown to change real gameplay
/// against the VECTOR control, plus the Hero Forge comparison bars. Applies HeroArchetypeSetup first.
/// Output: Verification/HeroStats/results.txt.   Unity -batchmode -projectPath <copy> -executeMethod HeroStatsVerification.Run
[InitializeOnLoad]
public static class HeroStatsVerification
{
    const string Key = "Overpowered.HeroStatsVerification"; const string Folder = "Verification/HeroStats/"; static double deadline;
    static HeroStatsVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Hero stats verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 1200; GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Hero stats verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<HeroStatsVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { HeroStatsVerificationRunner.RestoreCatalog(); SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
