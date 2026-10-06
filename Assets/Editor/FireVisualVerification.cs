using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FireVisualVerification
{
    const string Key="Overpowered.FireVisualVerification";
    static double deadline;
    static FireVisualVerification()
    {EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Fire visual verification timeout");Finish(1);}};}
    public static void Baseline()=>Begin(false);
    public static void Run()=>Begin(true);
    static void Begin(bool final)
    {
        SessionState.SetBool(Key,true);SessionState.SetBool(Key+"final",final);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1200;
        Directory.CreateDirectory("Verification/FireRedesign/saves");
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/FireRedesign/saves/save-"+Guid.NewGuid().ToString("N")+".json");
        GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var go=new GameObject("Fire visual verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<FireVisualVerificationRunner>();
        runner.Final=SessionState.GetBool(Key+"final",false);runner.Finished=Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseBool(Key);SessionState.EraseBool(Key+"final");deadline=0;
        EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);
    }
}
