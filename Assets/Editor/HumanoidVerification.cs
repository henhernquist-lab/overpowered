#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class HumanoidVerification
{
    const string Key="Overpowered.HumanoidVerification";static double deadline;
    static HumanoidVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("HUMANOID verification timeout");Finish(1);}};}
    public static void Run()
    {
        Directory.CreateDirectory("Verification/Humanoid");SessionState.SetBool(Key,true);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    public static void DeathControl(){SessionState.SetBool(Key+"death",true);Run();}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+240;
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Humanoid/save-"+Guid.NewGuid().ToString("N")+".json");GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(!SessionState.GetBool(Key,false))return;var obj=new GameObject("Humanoid verification");UnityEngine.Object.DontDestroyOnLoad(obj);var runner=obj.AddComponent<HumanoidVerificationRunner>();runner.OnlyDeathControl=SessionState.GetBool(Key+"death",false);runner.Finished=Finish;}
    static void Finish(int code){SessionState.EraseBool(Key);SessionState.EraseBool(Key+"death");deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
