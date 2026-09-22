using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AudioVerification
{
    const string Key="Overpowered.AudioVerification";static double deadline;
    static AudioVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Audio verification timeout");Finish(1);}};}
    public static void Run(){AudioSetup.Create();Directory.CreateDirectory("Verification/Audio");SessionState.SetString(Key,"run");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    public static void Reload(){SessionState.SetString(Key,"reload");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+360;
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Audio/save-"+Guid.NewGuid().ToString("N")+".json");GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(SessionState.GetString(Key,"")=="")return;var go=new GameObject("Audio verification");UnityEngine.Object.DontDestroyOnLoad(go);var runner=go.AddComponent<AudioVerificationRunner>();runner.Reload=SessionState.GetString(Key,"")=="reload";runner.Finished=Finish;}
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
