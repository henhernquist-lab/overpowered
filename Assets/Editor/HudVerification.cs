using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// In-game HUD (GameHud) verification: real GameFlow scene loads for Hero, Villain, Free Play and Endless Fight,
/// composited captures (gameplay camera + HUD panel in one render target) at 1280x720, 1920x1080 and 2560x1080,
/// programmatic no-clip / centre-clear / crosshair assertions, data-driven visibility controls, the F3 debug toggle,
/// a loadout swap, a Heat-star pop and an interleaved FPS A/B against the legacy IMGUI panel.
/// Unity -batchmode -projectPath <project> -executeMethod HudVerification.Run -logFile <log>     (omit -quit)
[InitializeOnLoad]
public static class HudVerification
{
    const string Key = "Overpowered.HudVerification";
    static double deadline;
    public static string Folder => Path.GetFullPath("Verification/Hud");
    /// Isolated save under the git-ignored Temp folder; deleted before every run so each run starts fresh.
    public static string SavePath => Path.GetFullPath("Temp/HudVerification/save-run.json");
    static HudVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("HUD verification timeout"); Finish(1); } }; }
    public static void Run()
    {
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
        foreach (var file in new[] { SavePath, SavePath + ".bak", SavePath + ".tmp" }) if (File.Exists(file)) File.Delete(file);
        SessionState.SetBool(Key, true); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 900;
        WorldSession.VerificationSavePath = SavePath; GameFlow.VerificationSandbox = false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!SessionState.GetBool(Key, false)) return;
        var go = new GameObject("HUD verification"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<HudVerificationRunner>(); runner.Folder = Folder; runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseBool(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
