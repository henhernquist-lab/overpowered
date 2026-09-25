using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// HUD Phase 3 ("connect the dots") verification: honest floating +XP popups (merge + controls), Heat flash on discrete
/// changes only, non-pausing level-up burst + banner, queued objective-complete / failed / wave-clear banners before the
/// next alert, and the enlarged waypoint. Real GameFlow sessions; composited captures at 1280x720, 1920x1080, 2560x1080.
///   Unity -batchmode -projectPath <project> -executeMethod HudPhase3Verification.Run    -logFile <log>   (omit -quit)
///   Unity -batchmode -projectPath <project> -executeMethod HudPhase3Verification.Reload -logFile <log>   (SEPARATE process,
///   after Run: the progression Run earned must come back from its save, and loading it must not replay level-up/popups)
[InitializeOnLoad]
public static class HudPhase3Verification
{
    const string Key = "Overpowered.HudPhase3Verification", ModeKey = "Overpowered.HudPhase3Verification.Mode";
    static double deadline;
    public static string Folder => Path.GetFullPath("Verification/Hud/phase3");
    /// Isolated saves next to the evidence (git-ignored; Unity empties Temp/ on exit, so the reload save cannot live there).
    public static string SaveFolder => Path.Combine(Folder, "saves");
    public static string MainSave => Path.Combine(SaveFolder, "save-main.json");
    public static string ExpectedFile => Path.Combine(SaveFolder, "expected-after-run.txt");
    static HudPhase3Verification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("HUD phase 3 verification timeout"); Finish(1); } }; }
    static void Delete(string path) { foreach (var f in new[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(f)) File.Delete(f); }
    public static void Run()
    {
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(SaveFolder);
        Delete(MainSave); Delete(ExpectedFile);
        Start("run");
    }
    public static void Reload()
    {
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(SaveFolder);
        Start("reload");
    }
    static void Start(string mode)
    {
        SessionState.SetBool(Key, true); SessionState.SetString(ModeKey, mode);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 1000;
        WorldSession.VerificationSavePath = MainSave; GameFlow.VerificationSandbox = false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!SessionState.GetBool(Key, false)) return;
        var go = new GameObject("HUD phase 3 verification"); Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<HudPhase3VerificationRunner>(); runner.Folder = Folder; runner.Finished = Finish;
        runner.ReloadMode = SessionState.GetString(ModeKey, "run") == "reload"; runner.MainSave = MainSave; runner.ExpectedFile = ExpectedFile;
    }
    static void Finish(int code) { SessionState.EraseBool(Key); SessionState.EraseString(ModeKey); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
