using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PowerPayoffBenchmark
{
    const string Key="Overpowered.PayoffBenchmark";
    public static void Run(){SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        EditorApplication.LockReloadAssemblies();GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=Path.Combine(Path.GetTempPath(),"payoff-bench-"+Guid.NewGuid()+".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var go=new GameObject("Payoff benchmark");UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<PowerPayoffBenchmarkRunner>().Finished=code=>{SessionState.EraseBool(Key);EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);};
    }
}
