using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Seeded scenario fuzzer over the staged missions (StagedMissionSetup.Create first - creates only missing assets; the modes are
/// not changed). Run = the default seed list; Replay = the seeds given with -fuzzSeed <n>[,<n>...] on the command line.
/// Output: Verification/Fuzz/results.txt + actions.txt (every action per seed).
///   Unity -batchmode -projectPath <copy> -executeMethod ScenarioFuzzVerification.Run
///   Unity -batchmode -projectPath <copy> -executeMethod ScenarioFuzzVerification.Replay -fuzzSeed 404
[InitializeOnLoad]
public static class ScenarioFuzzVerification
{
    const string Key = "Overpowered.ScenarioFuzzVerification"; const string Folder = "Verification/Fuzz/"; static double deadline;
    static ScenarioFuzzVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Scenario fuzz verification timeout"); Finish(1); } }; }
    public static void Replay() { SessionState.SetString(Key + ".seeds", Arg("-fuzzSeed")); Run(); }
    static string Arg(string name) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : ""; }
    public static void Run() { RosterSetup.Create(); StagedMissionSetup.Create(); Directory.CreateDirectory(Folder + "saves"); SessionState.SetString(Key, "run"); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 2400; GameFlow.VerificationSandbox = false;
        WorldSession.VerificationSavePath = Path.GetFullPath(Folder + "saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Scenario fuzz verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<ScenarioFuzzVerificationRunner>(); runner.Finished = Finish;
        string seeds = SessionState.GetString(Key + ".seeds", "");
        if (seeds != "") runner.Seeds = Array.ConvertAll(seeds.Split(','), int.Parse);
    }
    static void Finish(int code) { SessionState.EraseString(Key); SessionState.EraseString(Key + ".seeds"); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
