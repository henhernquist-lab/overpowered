using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DarknessVerification
{
    const string Key="Overpowered.DarknessVerification";
    const string Folder="Verification/Darkness/";
    static double deadline;
    static DarknessVerification()
    {EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline)Finish(1);};}
    public static void Run(){Begin("run");}
    public static void Reload(){Begin("reload");}
    static void Begin(string mode)
    {Directory.CreateDirectory(Folder+"saves");SessionState.SetString(Key,mode);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1800;
        GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=mode=="reload"?File.ReadAllText(Folder+"saves/save-path.txt"):
            Path.GetFullPath(Folder+"saves/save-"+Guid.NewGuid().ToString("N")+".json");
        if(mode=="run")File.WriteAllText(Folder+"saves/save-path.txt",WorldSession.VerificationSavePath);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        var go=new GameObject("Darkness verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<DarknessVerificationRunner>();runner.Reload=mode=="reload";runner.Finished=Finish;
    }
    static void Finish(int code)
    {SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
