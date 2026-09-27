using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PowerPayoffVerification
{
    const string Key="Overpowered.PowerPayoffVerification";
    static double deadline;
    static PowerPayoffVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline)Finish(1);};}
    public static void Run(){SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+600;
        GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=Path.Combine(Path.GetTempPath(),"op-payoff-"+Guid.NewGuid()+".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var go=new GameObject("Power payoff verification");UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<PowerPayoffVerificationRunner>().Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
