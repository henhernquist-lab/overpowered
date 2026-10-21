#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Rooftop Parkour & Time Trial verification entry point.
/// Exercises both shipping courses (Downtown Dash and Vertical Ascent), course placement resolution,
/// intentional start/cancel/retry, ordered checkpoint enforcement, personal best & XP rewards,
/// save/load persistence across a separate process (Run + Reload), HUD overlay & camera compatibility,
/// and pooled marker/VFX invariants.
///   Unity -batchmode -projectPath <copy> -executeMethod ParkourVerification.Run
///   Unity -batchmode -projectPath <copy> -executeMethod ParkourVerification.Reload
[InitializeOnLoad]
public static class ParkourVerification
{
    const string Key = "Overpowered.ParkourVerification";
    const string Folder = "Verification/Parkour/";
    static double deadline;

    static ParkourVerification()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline)
            {
                Debug.LogError("Parkour verification timeout");
                Finish(1);
            }
        };
    }

    public static void Run() => Begin("run");
    public static void Reload() => Begin("reload");

    static void Begin(string mode)
    {
        Directory.CreateDirectory(Folder + "saves");
        SessionState.SetString(Key, mode);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode = SessionState.GetString(Key, "");
        if (mode == "") return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + 900;
        GameFlow.VerificationSandbox = false;
        string pointer = Folder + "save-path.txt";
        string path = mode == "reload"
            ? File.ReadAllText(pointer).Trim()
            : Path.GetFullPath(Folder + "saves/parkour-" + Guid.NewGuid().ToString("N") + ".json");
        WorldSession.VerificationSavePath = path;
        if (mode == "run") File.WriteAllText(pointer, path);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode = SessionState.GetString(Key, "");
        if (mode == "") return;
        var go = new GameObject("Parkour verifier");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<ParkourVerificationRunner>();
        runner.Reload = mode == "reload";
        runner.Finished = Finish;
    }

    static void Finish(int code)
    {
        Time.captureDeltaTime = 0f;
        SessionState.EraseString(Key);
        deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}
#endif
