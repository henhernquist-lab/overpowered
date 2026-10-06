using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Real Laser Eyes aim and pooled-presentation controls in both camera views.
[InitializeOnLoad]
public static class LaserPresentationVerification
{
    const string Key="Overpowered.LaserPresentationVerification";
    static double deadline;
    static LaserPresentationVerification()
    {
        EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Laser presentation timeout");Finish(1);}};
    }
    public static void Run()
    {
        SessionState.SetBool(Key,true);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1200;
        Directory.CreateDirectory("Verification/LaserPresentation/saves");
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/LaserPresentation/saves/save-"+Guid.NewGuid().ToString("N")+".json");
        GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var go=new GameObject("Laser presentation verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<LaserPresentationRunner>().Finished=Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);
    }
}
