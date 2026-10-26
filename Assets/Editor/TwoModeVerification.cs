#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TwoModeVerification
{
    const string Key="Overpowered.TwoModeVerification";
    public const string Dir="Verification/TwoModes/";
    static double deadline;
    static TwoModeVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Two-mode verification timeout");Finish(1);}};}
    public static void Run(){Directory.CreateDirectory(Dir+"saves");SessionState.SetString(Key,"run");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    public static void Reload(){SessionState.SetString(Key,"reload");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1200;GameFlow.VerificationSandbox=false;
        string path=mode=="reload"?File.ReadAllText(Dir+"saves/path.txt"):Path.GetFullPath(Dir+"saves/save-"+Guid.NewGuid().ToString("N")+".json");
        WorldSession.VerificationSavePath=path;File.WriteAllText(Dir+"saves/path.txt",path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode=SessionState.GetString(Key,"");if(mode=="")return;
        var runner=new GameObject("Two-mode shipping-path verifier").AddComponent<TwoModeVerificationRunner>();UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);runner.Reload=mode=="reload";runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
    [MenuItem("Overpowered/Combat/Endless/Jump to round 5")]
    static void Five()=>Jump(5);
    [MenuItem("Overpowered/Combat/Endless/Jump to round 10")]
    static void Ten()=>Jump(10);
    [MenuItem("Overpowered/Combat/Endless/Jump to round 15")]
    static void Fifteen()=>Jump(15);
    static void Jump(int round)
    {
        var state=WorldSession.Instance!=null?WorldSession.Instance.Mode?.Director as EndlessWaveState:null;
        if(!Application.isPlaying||state==null||!state.JumpToRoundForDevelopment(round))Debug.LogWarning("Start Endless Fight in Play Mode before using the development round jump.");
    }
}
#endif
