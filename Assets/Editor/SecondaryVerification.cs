using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SecondaryVerification
{
    const string Key="Overpowered.SecondaryVerification";
    const string Folder="Verification/Secondary/";
    static double deadline;
    static SecondaryVerification() { EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Secondary verification timeout");Finish(1);}}; }
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
            Path.GetFullPath(Folder+"saves/secondary-"+Guid.NewGuid().ToString("N")+".json");
        WorldSession.VerificationSavePath=path;GameFlow.VerificationSandbox=false;
        if(stage=="run")File.WriteAllText(Folder+"save-path.txt",path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var stage=SessionState.GetString(Key,"");if(stage=="")return;
        var go=new GameObject("Secondary verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<SecondaryVerificationRunner>();runner.Reload=stage=="reload";runner.Finished=Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);
    }
}
