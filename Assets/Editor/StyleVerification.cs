using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// StyleScoreTracker: exact arithmetic, every anti-exploit rule with a farming attempt and a control, the session tracker on
/// a real Ice kill, BestStyle saved per mode. Output: Verification/Style/results.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod StyleVerification.Run
[InitializeOnLoad]
public static class StyleVerification
{
    const string Key = "Overpowered.StyleVerification"; const string Folder = "Verification/Style/"; static double deadline;
    static StyleVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Style verification timeout"); Finish(1); } }; }
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
        var go = new GameObject("Style verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<StyleVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
