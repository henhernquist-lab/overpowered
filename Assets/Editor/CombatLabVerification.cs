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
    public static void Run()
    {
        CombatLabScene.Ensure();SessionState.SetBool(Key,true);EditorSceneManager.OpenScene(CombatLabScene.Path);EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        if(!SessionState.GetBool(Key,false))return;SessionState.EraseBool(Key);
        Directory.CreateDirectory("Verification/CombatLab/saves");
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/CombatLab/saves/source-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(WorldSession.VerificationSavePath,JsonUtility.ToJson(new ProgressSave()));
        deadline=EditorApplication.timeSinceStartup+900;
        var root=new GameObject("Combat Lab verifier");UnityEngine.Object.DontDestroyOnLoad(root);
        root.AddComponent<CombatLabVerificationRunner>().Finished=Finish;
    }
    static void Finish(int code)
    {
        deadline=0;WorldSession.VerificationSavePath=null;EditorApplication.isPlaying=false;if(Application.isBatchMode)EditorApplication.Exit(code);
    }
}
