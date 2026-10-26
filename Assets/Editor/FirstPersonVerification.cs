using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FirstPersonVerification
{
    const string Key="Overpowered.FirstPersonVerification"; static double deadline;
    static FirstPersonVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline)Finish(1);};}
    public static void Run(){Begin("run");}
    public static void Reload(){Begin("reload");}
    static void Begin(string task)
    {Directory.CreateDirectory("Verification/FirstPerson");SessionState.SetString(Key,task);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string task=SessionState.GetString(Key,"");if(task=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+600;
        GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=task=="reload"?File.ReadAllText("Verification/FirstPerson/save-path.txt"):
            Path.GetFullPath("Verification/FirstPerson/save-"+Guid.NewGuid()+".json");
        File.WriteAllText("Verification/FirstPerson/save-path.txt",WorldSession.VerificationSavePath);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(SessionState.GetString(Key,"")=="")return;
        var go=new GameObject("First person verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<FirstPersonVerificationRunner>();runner.Reload=SessionState.GetString(Key,"")=="reload";runner.Finished=Finish;
    }
    static void Finish(int result){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(result);}
}
