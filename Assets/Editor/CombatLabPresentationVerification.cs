using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CombatLabPresentationVerification
{
    const string Key="Overpowered.CombatLabPresentationVerification";static double deadline;
    static CombatLabPresentationVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("FAIL Lab presentation timeout");Finish(1);}};}
    public static void Run(){CombatLabScene.Ensure();SessionState.SetBool(Key,true);EditorSceneManager.OpenScene(CombatLabScene.Path);EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        if(!SessionState.GetBool(Key,false))return;SessionState.EraseBool(Key);
        Directory.CreateDirectory("Verification/CombatLab/saves");WorldSession.VerificationSavePath=Path.GetFullPath("Verification/CombatLab/saves/presentation-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(WorldSession.VerificationSavePath,JsonUtility.ToJson(new ProgressSave()));deadline=EditorApplication.timeSinceStartup+900;
        var go=new GameObject("Lab presentation verifier");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CombatLabPresentationRunner>().Finished=Finish;
    }
    static void Finish(int code){deadline=0;WorldSession.VerificationSavePath=null;EditorApplication.isPlaying=false;if(Application.isBatchMode)EditorApplication.Exit(code);}
}
