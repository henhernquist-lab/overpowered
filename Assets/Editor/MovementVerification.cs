#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class MovementVerification
{
    const string Key="Overpowered.MovementVerification";static double deadline;
    static MovementVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Movement verification timeout");Finish(1);}};}
    public static void Baseline()=>Start("baseline");
    public static void Camera()=>Start("camera");
    public static void Run()=>Start("movement");
    static void Start(string stage){SessionState.SetString(Key,stage);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){if(SessionState.GetString(Key,"")=="")return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+600;Directory.CreateDirectory("Verification/Movement/saves");WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Movement/saves/"+Guid.NewGuid()+".json");GameFlow.VerificationSandbox=false;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){var stage=SessionState.GetString(Key,"");if(stage=="")return;var go=new GameObject("Movement verifier");UnityEngine.Object.DontDestroyOnLoad(go);var runner=go.AddComponent<MovementVerificationRunner>();runner.Stage=stage;runner.Finished=Finish;}
    static void Finish(int code){Time.captureDeltaTime=0;SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
