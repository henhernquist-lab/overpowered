using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FreezeReadabilityVerification
{
    const string Key="Overpowered.FreezeReadability";static double deadline;
    static FreezeReadabilityVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Freeze readability timeout");Finish(1);}};}
    public static void Run(){SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){if(!SessionState.GetBool(Key,false))return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+300;Directory.CreateDirectory("Verification/FreezeReadability/saves");WorldSession.VerificationSavePath=Path.GetFullPath("Verification/FreezeReadability/saves/"+Guid.NewGuid().ToString("N")+".json");GameFlow.VerificationSandbox=false;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(!SessionState.GetBool(Key,false))return;var go=new GameObject("Freeze readability verification");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<FreezeReadabilityVerificationRunner>().Finished=Finish;}
    static void Finish(int code){SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
