#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class NovaCustomizationVerification
{
    const string Key="Overpowered.NovaCustomizationVerification";static double deadline;static IDisposable registration;
    public const string Dir="Verification/NovaCustomization";
    static NovaCustomizationVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("FAIL Nova customization timeout");Finish(1);}};}
    public static void Run()=>Begin("run");
    public static void Reload()=>Begin("reload");
    static void Begin(string mode){Directory.CreateDirectory(Dir+"/saves");SessionState.SetString(Key,mode);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        EditorApplication.LockReloadAssemblies();registration=NovaCosmeticContent.RegisterForVerification();deadline=EditorApplication.timeSinceStartup+900;GameFlow.VerificationSandbox=false;
        string pointer=Dir+"/saves/save-path.txt";
        WorldSession.VerificationSavePath=mode=="reload"?File.ReadAllText(pointer):Path.GetFullPath(Dir+"/saves/save-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(pointer,WorldSession.VerificationSavePath);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        var go=new GameObject("Nova customization verifier");UnityEngine.Object.DontDestroyOnLoad(go);var runner=go.AddComponent<NovaCustomizationVerificationRunner>();runner.Reload=mode=="reload";runner.Finished=Finish;
    }
    static void Finish(int code){registration?.Dispose();registration=null;SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
