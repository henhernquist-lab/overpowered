using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Signatures2Verification
{
    const string Key = "Overpowered.Signatures2Verification";
    const string Folder = "Verification/Synergies2/";
    const string Saves = Folder + "saves/";
    static double deadline;

    static Signatures2Verification()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline)
            {
                Debug.LogError("Signatures2Verification timeout");
                Finish(1);
            }
        };
    }

    public static void Run() => Begin("run");
    public static void Reload() => Begin("reload");

    static void Begin(string mode)
    {
        Directory.CreateDirectory(Saves);
        SessionState.SetString(Key, mode);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        string mode = SessionState.GetString(Key, "");
        if (string.IsNullOrEmpty(mode)) return;
        SessionState.EraseString(Key);
        deadline = EditorApplication.timeSinceStartup + 240;
        bool reload = mode == "reload";
        string pointer = Saves + "reload-save-path.txt";
        WorldSession.VerificationSavePath = reload && File.Exists(pointer)
            ? File.ReadAllText(pointer).Trim()
            : Path.GetFullPath(Saves + "save-" + Guid.NewGuid().ToString("N") + ".json");
        var go = new GameObject("Signatures 2 verifier");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<Signatures2VerificationRunner>();
        runner.Reload = reload;
        runner.Finished = Finish;
    }

    static void Finish(int code)
    {
        deadline = 0;
        WorldSession.VerificationSavePath = null;
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
