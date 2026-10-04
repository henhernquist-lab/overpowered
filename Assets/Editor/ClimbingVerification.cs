#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ClimbingVerification
{
    const string Key="Overpowered.ClimbingVerification";
    static double deadline;
    static ClimbingVerification()
    {EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Climbing verification timeout");Finish(1);}};}
    public static void Run()=>Start("fixtures");
    public static void City()=>Start("city");
    static void Start(string stage)
    {SessionState.SetString(Key,stage);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1200;
        Directory.CreateDirectory("Verification/Climbing/saves");
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Climbing/saves/"+Guid.NewGuid()+".json");
        GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var stage=SessionState.GetString(Key,"");if(stage=="")return;
        var go=new GameObject("Climbing verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<ClimbingVerificationRunner>();runner.Stage=stage;runner.Finished=Finish;
    }
    static void Finish(int code)
    {Time.captureDeltaTime=0;SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
