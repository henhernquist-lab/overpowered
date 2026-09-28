using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class IntroTimelineVerification
{
    const string Key="Overpowered.IntroTimelineVerification";
    const string Folder="Verification/Intro/";
    static double deadline;
    [InitializeOnLoadMethod] static void Watch(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Intro verification timed out");Finish(1);}};}
    public static void Run()
    {
        Directory.CreateDirectory(Folder);Directory.CreateDirectory(Folder+"saves/");
        File.WriteAllText(Folder+"saves/legacy.json","{\"Version\":1,\"Level\":1,\"Xp\":0,\"Points\":0,\"Powers\":[],\"Rooftops\":[]}");
        SessionState.SetString(Key,"run");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    public static void Reload(){SessionState.SetString(Key,"reload");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        var mode=SessionState.GetString(Key,"");if(mode=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+180;
        FirstLaunchIntro.AllowBatchVerification=true;
        // Run starts from a Version 1 save written without IntroSeen; Reload re-reads the same file in a separate process.
        WorldSession.VerificationSavePath=Path.GetFullPath(Folder+"saves/legacy.json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var mode=SessionState.GetString(Key,"");if(mode=="")return;
        var go=new GameObject("Intro verification");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<IntroTimelineVerificationRunner>();runner.Reload=mode=="reload";runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;FirstLaunchIntro.AllowBatchVerification=false;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
