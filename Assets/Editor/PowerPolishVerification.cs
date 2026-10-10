using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Focused capture and bounded-resource controls for the four presentation changes.
[InitializeOnLoad]
public static class PowerPolishVerification
{
    const string Key="Overpowered.PowerPolishVerification";
    static double deadline;
    static PowerPolishVerification()
    {EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Power polish timeout");Finish(1);}};}
    public static void Run()
    {
        Directory.CreateDirectory("Verification/PowerPolish/saves");
        SessionState.SetBool(Key,true);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1200;
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/PowerPolish/saves/save-"+Guid.NewGuid().ToString("N")+".json");
        GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var go=new GameObject("Power polish verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<PowerPolishVerificationRunner>();runner.Finished=Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseBool(Key);deadline=0;
        EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);
    }
}
