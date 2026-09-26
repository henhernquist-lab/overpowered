using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Measured difficulty sample per side and Heat level (idle player at the live encounter site), plus the Phase 2 controls.
/// Before / After write Verification/Balance/sample-before.txt / sample-after.txt with the SAME scenario code.
[InitializeOnLoad]
public static class BalanceVerification
{
    const string Key="Overpowered.BalanceVerification";const string Saves="Verification/Balance/saves/";static double deadline;
    static BalanceVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Balance sample timeout");Finish(1);}};}
    public static void Before(){Begin("before");}
    public static void After(){Begin("after");}
    static void Begin(string tag){Directory.CreateDirectory(Saves);SessionState.SetString(Key,tag);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1800;GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=Path.GetFullPath(Saves+"save-"+Guid.NewGuid().ToString("N")+".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(SessionState.GetString(Key,"")=="")return;
        var go=new GameObject("Balance sampler");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<BalanceVerificationRunner>();runner.Tag=SessionState.GetString(Key,"");runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
