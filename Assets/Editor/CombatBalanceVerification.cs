using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CombatBalanceVerification
{
    const string Key="Overpowered.CombatBalanceVerification";
    static double deadline;
    static CombatBalanceVerification()
    { EditorApplication.update+=()=>{if(deadline>0 && EditorApplication.timeSinceStartup>deadline){Debug.LogError("Combat balance verification timeout");Finish(1);}}; }
    public static void Run()
    {
        Directory.CreateDirectory("Verification/CombatBalance/saves");
        SessionState.SetBool(Key,true);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1800;
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/CombatBalance/saves/save-"+Guid.NewGuid().ToString("N")+".json");
        GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var go=new GameObject("Combat balance verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<CombatBalanceVerificationRunner>().Finished=Finish;
    }
    static void Finish(int code)
    { SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code); }
}
