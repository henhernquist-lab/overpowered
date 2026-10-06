using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LaserBalanceVerification
{
    const string Key="Overpowered.LaserBalanceVerification";
    static double deadline;
    static LaserBalanceVerification()
    {EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Laser balance timeout");Finish(1);}};}
    public static void Run()
    {SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1200;
        Directory.CreateDirectory("Verification/LaserPresentation/saves");
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/LaserPresentation/saves/balance-"+Guid.NewGuid().ToString("N")+".json");
        GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var go=new GameObject("Laser balance verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<LaserBalanceRunner>().Finished=Finish;
    }
    static void Finish(int code)
    {SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
