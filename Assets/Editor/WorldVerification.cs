using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// District world verification (positive + negative controls, captures, NPC LOD, separate-process save reload).
/// Unity -batchmode -projectPath <project> -executeMethod WorldVerification.Run -logFile <log>     (then .Reload)
/// Writes Verification/World/verify/*. Omit -quit; the runner exits with its own code.
[InitializeOnLoad]
public static class WorldVerification
{
    const string Key = "Overpowered.WorldVerification";
    static double deadline;
    static string Folder => Path.GetFullPath("Verification/World/verify");

    static WorldVerification()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("World verification timeout"); Finish(1); }
        };
    }
    public static void Run() => Start("run");
    public static void Reload() => Start("reload");
    static void Start(string mode)
    {
        SessionState.SetString(Key, mode);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode = SessionState.GetString(Key, ""); if (string.IsNullOrEmpty(mode)) return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + 2400;
        Directory.CreateDirectory(Path.Combine(Folder, "saves"));
        string pathFile = Path.Combine(Folder, "saves", "save-path.txt");
        if (mode == "run") { WorldSession.VerificationSavePath = Path.Combine(Folder, "saves", "world-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(pathFile, WorldSession.VerificationSavePath); }
        else WorldSession.VerificationSavePath = File.ReadAllText(pathFile);
        GameFlow.VerificationSandbox = false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode = SessionState.GetString(Key, ""); if (string.IsNullOrEmpty(mode)) return;
        var go = new GameObject("World verification"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<WorldVerificationRunner>(); runner.Mode = mode; runner.Folder = Folder; runner.Finished = Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key); deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
