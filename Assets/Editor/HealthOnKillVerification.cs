using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Health on kill: the shipping formula table, then real Free Play kills through the shared CityNpc.Damage death funnel
/// (per-archetype values, caps, one claim per life, ineligible targets, the miniboss formula, the HUD number, a second hero).
/// Output: Verification/HealthOnKill/results.txt.   Unity -batchmode -projectPath <copy> -executeMethod HealthOnKillVerification.Run
[InitializeOnLoad]
public static class HealthOnKillVerification
{
    const string Key = "Overpowered.HealthOnKillVerification"; const string Folder = "Verification/HealthOnKill/"; static double deadline;
    static HealthOnKillVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Health-on-kill verification timeout"); Finish(1); } }; }
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
        var go = new GameObject("Health on kill verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<HealthOnKillVerificationRunner>(); runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
