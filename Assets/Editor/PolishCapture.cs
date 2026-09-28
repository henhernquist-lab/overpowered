using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Visual polish evidence (captures + perf, no asset changes). Before = VisualPreset forced OFF, After = forced ON, for this
/// process only; the asset is never written. Output: Verification/Polish/<Before|After>/ (PNGs, perf.csv, results.txt).
///   Unity -batchmode -projectPath <project> -executeMethod PolishCapture.RunBefore -logFile <log>
///   Unity -batchmode -projectPath <project> -executeMethod PolishCapture.RunAfter  -logFile <log>
///   PolishCapture.RunCurrent = the preset asset as it is (tag from -polishTag, default "Current").
/// Batch mode needs a GPU (no -nographics). Restore Side_Kick_Data.db afterwards (AGENTS.md).
[InitializeOnLoad]
public static class PolishCapture
{
    const string Key = "Overpowered.PolishCapture", PresetKey = "Overpowered.PolishCapture.Preset";
    static double deadline;
    static PolishCapture() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Polish capture timeout"); Finish(1); } }; }
    [MenuItem("Overpowered/Visual/Capture BEFORE (preset off)")] public static void RunBefore() => Run("Before", "off");
    [MenuItem("Overpowered/Visual/Capture AFTER (preset on)")] public static void RunAfter() => Run("After", "on");
    public static void RunCurrent() => Run(Arg("-polishTag") ?? "Current", "");
    static string Arg(string name) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    static void Run(string tag, string preset)
    {
        VisualPresetSetup.Create();   // create-missing only: the asset stays disabled; After forces it on for this process
        SessionState.SetString(Key, tag); SessionState.SetString(PresetKey, preset);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 1800; GameFlow.VerificationSandbox = false;
        string preset = SessionState.GetString(PresetKey, "");
        VisualPreset.ForceEnabled = preset == "on" ? true : preset == "off" ? false : (bool?)null;
        Directory.CreateDirectory("Verification/Polish/saves");
        WorldSession.VerificationSavePath = Path.GetFullPath("Verification/Polish/saves/save-" + Guid.NewGuid().ToString("N") + ".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string tag = SessionState.GetString(Key, ""); if (tag == "") return;
        var go = new GameObject("Polish capture"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<PolishCaptureRunner>(); runner.Tag = tag; runner.Finished = Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key); SessionState.EraseString(PresetKey); VisualPreset.ForceEnabled = null; deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        if (Application.isBatchMode) EditorApplication.Exit(code); else EditorApplication.isPlaying = false;
    }
}
