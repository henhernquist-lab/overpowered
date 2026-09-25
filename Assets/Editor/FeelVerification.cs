using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// HUD Phase 4 (game feel) verification in real Play Mode: hit pause through TimeArbiter (with pause-menu, light-hit and
/// rate-limit CONTROLS), physics / audio / HUD-tween behaviour through the pause, camera impulse + FOV kick (hard vs soft
/// landing CONTROL), pooled palette-material impact particles (+ interleaved FPS with/without), and aim accuracy of Fire
/// Blast / Ice / Telekinesis with the new camera framing (incl. the shoulder-parallax obstacle case), with captures.
///   Unity -batchmode -projectPath <project> -executeMethod FeelVerification.Run -logFile <log>   (omit -quit)
[InitializeOnLoad]
public static class FeelVerification
{
    const string Key = "Overpowered.FeelVerification";
    static double deadline;
    public static string Folder => Path.GetFullPath("Verification/Feel");
    /// Isolated save next to the evidence (git-ignored); deleted before every run.
    public static string SaveFolder => Path.Combine(Folder, "saves");
    public static string SavePath => Path.Combine(SaveFolder, "save-feel.json");
    static FeelVerification() { EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Feel verification timeout"); Finish(1); } }; }
    public static void Run()
    {
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(SaveFolder);
        foreach (var f in new[] { SavePath, SavePath + ".bak", SavePath + ".tmp" }) if (File.Exists(f)) File.Delete(f);
        SessionState.SetBool(Key, true); EditorSceneManager.OpenScene("Assets/Scenes/Home.unity"); EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.LockReloadAssemblies(); deadline = EditorApplication.timeSinceStartup + 800;
        WorldSession.VerificationSavePath = SavePath; GameFlow.VerificationSandbox = false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!SessionState.GetBool(Key, false)) return;
        var go = new GameObject("Feel verification"); Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<FeelVerificationRunner>(); runner.Folder = Folder; runner.Finished = Finish;
    }
    static void Finish(int code) { SessionState.EraseBool(Key); deadline = 0; EditorApplication.UnlockReloadAssemblies(); EditorApplication.Exit(code); }
}
