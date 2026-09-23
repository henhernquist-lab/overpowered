using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Free Play + Endless Fight verification (real GameFlow, real scenes, isolated save).
/// Unity -batchmode -projectPath <project> -executeMethod ModeExpansionVerification.Run -logFile <log>     (omit -quit)
/// then in a SEPARATE Unity process:  -executeMethod ModeExpansionVerification.Reload
/// Optional: -modeExpansionOnly freeplay|endless runs one half (iteration only; evidence comes from the full run).
[InitializeOnLoad]
public static class ModeExpansionVerification
{
    const string Key="Overpowered.ModeExpansionVerification";
    static double deadline;
    public static string Folder=>Path.GetFullPath("Verification/ModeExpansion");
    /// Fixed, git-ignored (save-*.json) path so the Reload process finds it; Run deletes it first, so every run starts fresh.
    public static string SavePath=>Path.Combine(Folder,"save-run.json");
    static ModeExpansionVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Mode expansion verification timeout");Finish(1);}};}
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        foreach(var file in new[]{SavePath,SavePath+".bak",SavePath+".tmp"}) if(File.Exists(file)) File.Delete(file);
        Start("run");
    }
    public static void Reload(){Start("reload");}
    static void Start(string task){SessionState.SetString(Key,task);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+900;
        WorldSession.VerificationSavePath=SavePath;GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string task=SessionState.GetString(Key,"");if(task=="")return;
        var go=new GameObject("Mode expansion verification");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<ModeExpansionVerificationRunner>();runner.Reload=task=="reload";runner.Folder=Folder;runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
