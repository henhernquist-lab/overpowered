using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CityArtVerification
{
    const string Key="Overpowered.CityArtVerification";
    public static void Run()
    {
        CityArtSetup.Create();Directory.CreateDirectory("Verification/Art");
        SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        GameFlow.VerificationSandbox=false;WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Art/save-"+Guid.NewGuid().ToString("N")+".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var runner=new GameObject("City art verification").AddComponent<CityArtVerificationRunner>();
        UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);
        runner.Finished=code=>{SessionState.EraseBool(Key);EditorApplication.Exit(code);};
    }
}
