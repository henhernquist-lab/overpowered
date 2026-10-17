using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EnemyHealthFeedbackVerification
{
    const string Key="Overpowered.EnemyHealthFeedbackVerification";
    static double deadline;
    static EnemyHealthFeedbackVerification()
    { EditorApplication.update+=()=>{if(deadline>0 && EditorApplication.timeSinceStartup>deadline){Debug.LogError("Enemy health feedback verification timeout");Finish(1);}}; }
    public static void Run()
    {
        Directory.CreateDirectory("Verification/EnemyHealthFeedback/saves");
        SessionState.SetBool(Key,true);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+1800;
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/EnemyHealthFeedback/saves/save-"+Guid.NewGuid().ToString("N")+".json");
        GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!SessionState.GetBool(Key,false))return;
        var go=new GameObject("Enemy health feedback verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<EnemyHealthFeedbackVerificationRunner>().Finished=Finish;
    }
    static void Finish(int code)
    { SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code); }
}
