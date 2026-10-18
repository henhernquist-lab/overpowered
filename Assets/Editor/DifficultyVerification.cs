using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DifficultyVerification
{
    const string Key="Overpowered.DifficultyVerification"; static double deadline;
    static DifficultyVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline)Finish(1);};}
    public static void Run(){Begin("run");}
    public static void Reload(){Begin("reload");}
    static void Begin(string task)
    {Directory.CreateDirectory("Verification/Difficulty/saves");SessionState.SetString(Key,task);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string task=SessionState.GetString(Key,"");if(task=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1800;
        GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=task=="reload"?File.ReadAllText("Verification/Difficulty/saves/save-path.txt"):
            Path.GetFullPath("Verification/Difficulty/saves/save-"+Guid.NewGuid()+".json");
        File.WriteAllText("Verification/Difficulty/saves/save-path.txt",WorldSession.VerificationSavePath);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(SessionState.GetString(Key,"")=="")return;
        var go=new GameObject("Difficulty verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<DifficultyVerificationRunner>();runner.Reload=SessionState.GetString(Key,"")=="reload";runner.Finished=Finish;
    }
    static void Finish(int result){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(result);}
}
