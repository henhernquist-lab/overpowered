using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Melee depth (combo string, charged heavy, ground pound) through the controller entry points the E key calls, in a real
/// Hero session with and without Super Strength equipped. Output: Verification/Melee/results.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod MeleeVerification.Run   (omit -quit)
[InitializeOnLoad]
public static class MeleeVerification
{
    const string Key = "Overpowered.MeleeVerification"; const string Folder = "Verification/Melee/"; static double deadline;
    static MeleeVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Melee verification timeout"); Finish(1); } }; }
    public static void Run() { Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 900; GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Melee verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<MeleeVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
