using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LightingVerification
{
    const string Key="Overpowered.LightingVerification";static double deadline;
    static LightingVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Lighting verification timeout");Finish(1);}};}
    public static void Run(){Directory.CreateDirectory("Verification/Lighting/saves");SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){if(!SessionState.GetBool(Key,false))return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+600;GameFlow.VerificationSandbox=false;WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Lighting/saves/save-"+Guid.NewGuid().ToString("N")+".json");}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(!SessionState.GetBool(Key,false))return;var go=new GameObject("Lighting verification");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<LightingVerificationRunner>().Finished=Finish;}
    static void Finish(int code){SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
