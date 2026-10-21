using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CombatLabVerification
{
    const string Key="Overpowered.CombatLabVerification";
    static double deadline;
    static CombatLabVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("FAIL Combat Lab timeout");Finish(1);}};}
    public static void Run() => Begin(false);
    public static void Reload() => Begin(false,true);
    public static void Matrix() => Begin(true);
    static void Begin(bool matrix,bool reload=false)
    {
        SessionState.SetBool(Key+".Matrix",matrix);SessionState.SetBool(Key+".Reload",reload);
        CombatLabScene.Ensure();SessionState.SetBool(Key,true);EditorSceneManager.OpenScene(CombatLabScene.Path);EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        if(!SessionState.GetBool(Key,false))return;SessionState.EraseBool(Key);
        Directory.CreateDirectory("Verification/CombatLab/saves");
        bool reload=SessionState.GetBool(Key+".Reload",false);
        string pointer="Verification/CombatLab/saves/source-path.txt";
        WorldSession.VerificationSavePath=reload?File.ReadAllText(pointer).Trim():Path.GetFullPath("Verification/CombatLab/saves/source-"+Guid.NewGuid().ToString("N")+".json");
        if(!reload){File.WriteAllText(WorldSession.VerificationSavePath,JsonUtility.ToJson(new ProgressSave()));File.WriteAllText(pointer,WorldSession.VerificationSavePath);}
        deadline=EditorApplication.timeSinceStartup+1800;
        var root=new GameObject("Combat Lab verifier");UnityEngine.Object.DontDestroyOnLoad(root);
        var runner=root.AddComponent<CombatLabVerificationRunner>();runner.Matrix=SessionState.GetBool(Key+".Matrix",false);runner.Reload=reload;runner.Finished=Finish;
    }
    static void Finish(int code)
    {
        deadline=0;WorldSession.VerificationSavePath=null;EditorApplication.isPlaying=false;if(Application.isBatchMode)EditorApplication.Exit(code);
    }
}
