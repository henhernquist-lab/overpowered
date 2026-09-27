#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Play Mode: Sidekick heroes through the real Hero Forge -> save -> session path, recolour/skin controls, Ice restore,
/// first-person hiding, teardown, and a separate-process Reload. -executeMethod SidekickVerification.Run / .Reload
[InitializeOnLoad]
public static class SidekickVerification
{
    const string Key="Overpowered.SidekickVerification";static double deadline;
    public const string Dir="Verification/Sidekick";
    static SidekickVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("SIDEKICK verification timeout");Finish(1);}};}
    public static void Run(){Directory.CreateDirectory(Dir+"/saves");SessionState.SetString(Key,"run");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    public static void Reload(){SessionState.SetString(Key,"reload");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+600;GameFlow.VerificationSandbox=false;
        string path=mode=="reload"?File.ReadAllText(Dir+"/saves/save-path.txt"):Path.GetFullPath(Dir+"/saves/save-"+Guid.NewGuid().ToString("N")+".json");
        WorldSession.VerificationSavePath=path;File.WriteAllText(Dir+"/saves/save-path.txt",path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        var go=new GameObject("Sidekick verifier");UnityEngine.Object.DontDestroyOnLoad(go);var runner=go.AddComponent<SidekickVerificationRunner>();runner.Reload=mode=="reload";runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
