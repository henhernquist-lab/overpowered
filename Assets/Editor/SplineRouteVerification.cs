using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Phase 8 spline pedestrian / patrol routes: real Play Mode verification with controls, captures, NPC LOD and a
/// separate-process determinism reload.
/// Unity -batchmode -projectPath <project> -executeMethod SplineRouteVerification.Run -logFile <log>     (then .Reload)
/// Writes Verification/Splines/verify/*. Omit -quit; the runner exits with its own code.
[InitializeOnLoad]
public static class SplineRouteVerification
{
    const string Key = "Overpowered.SplineRouteVerification";
    static double deadline;
    static string Folder => Path.GetFullPath("Verification/Splines/verify");

    static SplineRouteVerification()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("Spline route verification timeout"); Finish(1); }
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
        deadline = EditorApplication.timeSinceStartup + 3000;
        string saves = Path.Combine(Folder, "saves"); Directory.CreateDirectory(saves);
        string pathFile = Path.Combine(saves, "save-path.txt");
        if (mode == "run") { WorldSession.VerificationSavePath = Path.Combine(saves, "splines-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(pathFile, WorldSession.VerificationSavePath); }
        else WorldSession.VerificationSavePath = File.ReadAllText(pathFile).Trim();
        GameFlow.VerificationSandbox = false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode = SessionState.GetString(Key, ""); if (string.IsNullOrEmpty(mode)) return;
        var go = new GameObject("Spline route verification"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<SplineRouteVerificationRunner>(); runner.Mode = mode; runner.Folder = Folder; runner.Finished = Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key); deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}

/// Brings CityLayout.asset / GameTuning.asset up to the route data: serializes the new route fields with their defaults,
/// adds the authored Docks "Quay promenade" Route feature (to the district whose region contains it) if no feature of
/// that name exists, and marks the lighthouse platform deck as no route (its centre line runs through the Harbour Light).
/// Idempotent. Unity -batchmode -projectPath <project> -executeMethod SplineRouteSetup.Apply -quit
public static class SplineRouteSetup
{
    [MenuItem("Overpowered/World/Apply spline route defaults to assets")]
    public static void Apply()
    {
        var layout = AssetDatabase.LoadAssetAtPath<CityLayout>("Assets/Resources/CityLayout.asset");
        var quay = CityLayout.QuayPromenade(); int added = 0, cleared = 0;
        if (!layout.Districts.Exists(d => d.Features.Exists(f => f.Name == quay.Name)))
        {
            var owner = layout.Districts.Find(d => d.Region.Contains(quay.Points[0]));
            if (owner != null) { owner.Features.Add(quay); added++; }
        }
        foreach (var d in layout.Districts) foreach (var f in d.Features)
            if (f.Name == "Lighthouse platform" && f.Walkers != RouteUse.None) { f.Walkers = RouteUse.None; cleared++; }
        if (layout.Routes == null) layout.Routes = new RouteSettings();
        EditorUtility.SetDirty(layout);
        var tuning = AssetDatabase.LoadAssetAtPath<GameTuning>("Assets/Resources/GameTuning.asset");
        EditorUtility.SetDirty(tuning);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SPLINE SETUP] quay promenade added {added}, lighthouse platform route cleared {cleared}, routes enabled {layout.Routes.Enabled}, patrol speed {tuning.Npcs.PatrolSpeed}");
    }
}
