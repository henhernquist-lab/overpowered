using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Ice on REAL targets (a live, approaching Rusher NPC and a sliding physics crate), with off-axis and out-of-range
/// CONTROLs, in third and first person. Before / After write Verification/Ice/results-before.txt / results-after.txt.
[InitializeOnLoad]
public static class IceVerification
{
    const string Key="Overpowered.IceVerification";const string Saves="Verification/Ice/saves/";static double deadline;
    static IceVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Ice verification timeout");Finish(1);}};}
    public static void Before(){Begin("before");}
    public static void After(){Begin("after");}
    static void Begin(string tag){Directory.CreateDirectory(Saves);SessionState.SetString(Key,tag);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+900;GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=Path.GetFullPath(Saves+"save-"+Guid.NewGuid().ToString("N")+".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(SessionState.GetString(Key,"")=="")return;
        var go=new GameObject("Ice verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<IceVerificationRunner>();runner.Tag=SessionState.GetString(Key,"");runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
