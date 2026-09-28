using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Challenges: asset rules, in-memory test challenges paid exactly once in a real session (controls for parameter, side,
/// below-target, re-pay, removed ids), and a SEPARATE-PROCESS Reload that re-reads the save and proves no re-pay.
/// Creates missing challenge assets first (ChallengeSetup.Create). Output: Verification/Challenges/results.txt, reload.txt.
///   Unity -batchmode -projectPath <copy> -executeMethod ChallengeVerification.Run   then   ChallengeVerification.Reload
[InitializeOnLoad]
public static class ChallengeVerification
{
    const string Key = "Overpowered.ChallengeVerification"; const string Folder = "Verification/Challenges/"; static double deadline;
    static ChallengeVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Challenge verification timeout"); Finish(1); } }; }
    public static void Run() { RosterSetup.Create(); ChallengeSetup.Create(); Directory.CreateDirectory(Folder); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
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
        var go = new GameObject("Challenge verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<ChallengeVerificationRunner>(); runner.Reload = mode == "reload"; runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseString(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
