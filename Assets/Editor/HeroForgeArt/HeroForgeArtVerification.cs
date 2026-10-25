#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HeroForgeArtVerification
{
    const string Key="Overpowered.HeroForgeArtVerification";static double deadline;
    public const string Dir="Verification/HeroForgeArt";
    static HeroForgeArtVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("FAIL Hero Forge art timeout");Finish(1);}};}
    public static void Run()=>Begin("run");
    public static void Reload()=>Begin("reload");
    static void Begin(string mode){Directory.CreateDirectory(Dir+"/saves");SessionState.SetString(Key,mode);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1800;GameFlow.VerificationSandbox=false;
        string pointer=Dir+"/saves/save-path.txt";
        WorldSession.VerificationSavePath=mode=="reload"?File.ReadAllText(pointer):Path.GetFullPath(Dir+"/saves/save-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(pointer,WorldSession.VerificationSavePath);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        var go=new GameObject("Hero Forge art verifier");UnityEngine.Object.DontDestroyOnLoad(go);var runner=go.AddComponent<HeroForgeArtVerificationRunner>();runner.Reload=mode=="reload";runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
