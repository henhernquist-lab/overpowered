using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FinalSignaturesVerification
{
    const string Key = "Overpowered.FinalSignaturesVerification";
    static double deadline;
    static FinalSignaturesVerification()
    {
        EditorApplication.update += () => { if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) { Debug.LogError("FAIL Final signatures timeout"); Finish(1); } };
    }
    public static void Run() => Begin("run");
    public static void Reload() => Begin("reload");
    static void Begin(string mode)
    {
        Directory.CreateDirectory("Verification/FinalSignatures/saves");
        SessionState.SetString(Key, mode);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        string mode = SessionState.GetString(Key, ""); if (mode == "") return;
        SessionState.EraseString(Key); deadline = EditorApplication.timeSinceStartup + 360;
        string pointer = "Verification/FinalSignatures/saves/reload-path.txt";
        WorldSession.VerificationSavePath = mode == "reload" ? File.ReadAllText(pointer).Trim() : Path.GetFullPath("Verification/FinalSignatures/saves/" + Guid.NewGuid().ToString("N") + ".json");
        var go = new GameObject("Final signature verifier"); UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<FinalSignaturesVerificationRunner>(); runner.Reload = mode == "reload"; runner.Finished = Finish;
    }
    static void Finish(int code)
    {
        deadline = 0; SignatureCatalog.VerificationPreview = false; WorldSession.VerificationSavePath = null;
        EditorApplication.isPlaying = false; if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
