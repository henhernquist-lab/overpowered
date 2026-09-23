using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HeroForgeVerification
{
    const string Key="Overpowered.ForgeVerification";static double deadline;
    static HeroForgeVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Forge timeout");Finish(1);}};}
    public static void Run(){HeroForgeSetup.Create();Directory.CreateDirectory("Verification/Forge");SessionState.SetString(Key,"run");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    public static void Reload(){SessionState.SetString(Key,"reload");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+900;GameFlow.VerificationSandbox=false;
        string path=SessionState.GetString(Key,"")=="reload"?File.ReadAllText("Verification/Forge/save-path.txt"):Path.GetFullPath("Verification/Forge/save-"+Guid.NewGuid().ToString("N")+".json");
        WorldSession.VerificationSavePath=path;File.WriteAllText("Verification/Forge/save-path.txt",path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(SessionState.GetString(Key,"")=="")return;var go=new GameObject("Forge verifier");UnityEngine.Object.DontDestroyOnLoad(go);var runner=go.AddComponent<HeroForgeVerificationRunner>();runner.Reload=SessionState.GetString(Key,"")=="reload";runner.Finished=Finish;}
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
