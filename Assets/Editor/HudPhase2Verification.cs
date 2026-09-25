using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// HUD Phase 2 ("make the goal obvious") verification: mission briefing, data-driven objective line, waypoint, encounter
/// alerts and first-time prompts, in real GameFlow sessions with composited captures at 1280x720, 1920x1080 and 2560x1080.
///   Unity -batchmode -projectPath <project> -executeMethod HudPhase2Verification.Run    -logFile <log>   (omit -quit)
///   Unity -batchmode -projectPath <project> -executeMethod HudPhase2Verification.Reload -logFile <log>   (SEPARATE process,
///   after Run: the prompt save written by Run must come back with its seen prompts; plus the legacy-save fixture)
[InitializeOnLoad]
public static class HudPhase2Verification
{
    const string Key = "Overpowered.HudPhase2Verification", ModeKey = "Overpowered.HudPhase2Verification.Mode";
    static double deadline;
    public static string Folder => Path.GetFullPath("Verification/Hud/phase2");
    /// Isolated saves next to the evidence (Unity empties Temp/ when the Editor exits, so a reload save cannot live
    /// there). Run deletes all of them first; Reload deletes none except the legacy copy (it must read the prompt save
    /// Run left behind).
    public static string SaveFolder => Path.Combine(Folder, "saves");
    public static string MainSave => Path.Combine(SaveFolder, "save-main.json");
    public static string PromptSave => Path.Combine(SaveFolder, "save-prompts.json");
    public static string FreshSave => Path.Combine(SaveFolder, "save-fresh.json");
    public static string LegacySave => Path.Combine(SaveFolder, "save-legacy.json");
    public static string LegacyFixture => Path.GetFullPath("Verification/ModeExpansion/legacy-save-fixture.json");
    static HudPhase2Verification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("HUD phase 2 verification timeout"); Finish(1); } }; }
    static void Delete(string path) { foreach (var f in new[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(f)) File.Delete(f); }
    public static void Run()
    {
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(SaveFolder);
        foreach (var path in new[] { MainSave, PromptSave, FreshSave, LegacySave }) Delete(path);
        Start("run");
    }
    public static void Reload()
    {
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(SaveFolder);
        Delete(LegacySave); File.Copy(LegacyFixture, LegacySave);
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
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 700;
        WorldSession.VerificationSavePath = SessionState.GetString(ModeKey, "run") == "reload" ? PromptSave : MainSave; GameFlow.VerificationSandbox = false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!SessionState.GetBool(Key, false)) return;
        var go = new GameObject("HUD phase 2 verification"); Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<HudPhase2VerificationRunner>(); runner.Folder = Folder; runner.Finished = Finish;
        runner.ReloadMode = SessionState.GetString(ModeKey, "run") == "reload";
        runner.MainSave = MainSave; runner.PromptSave = PromptSave; runner.FreshSave = FreshSave; runner.LegacySave = LegacySave;
    }
    static void Finish(int code) { SessionState.EraseBool(Key); SessionState.EraseString(ModeKey); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
