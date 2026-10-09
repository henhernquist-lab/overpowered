using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PhaseShiftVerification
{
    const string Key="Overpowered.PhaseShiftVerification";
    const string Folder="Verification/PhaseShift/";
    static double deadline;
    static PhaseShiftVerification() { EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Secondary verification timeout");Finish(1);}}; }
    public static void Baseline()=>Begin("baseline");
    public static void Run()=>Begin("run");
    public static void Reload()=>Begin("reload");
    static void Begin(string stage)
    {
        Directory.CreateDirectory(Folder+"saves");
        SessionState.SetString(Key,stage);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        var stage=SessionState.GetString(Key,"");if(stage=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1200;
        var path=stage=="reload"?File.ReadAllText(Folder+"save-path.txt"):
            Path.GetFullPath(Folder+"saves/phase-"+Guid.NewGuid().ToString("N")+".json");
        WorldSession.VerificationSavePath=path;GameFlow.VerificationSandbox=false;
        if(stage=="run")File.WriteAllText(Folder+"save-path.txt",path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var stage=SessionState.GetString(Key,"");if(stage=="")return;
        var go=new GameObject("Secondary verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<PhaseShiftVerificationRunner>();runner.Stage=stage;runner.Finished=Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);
    }
}
