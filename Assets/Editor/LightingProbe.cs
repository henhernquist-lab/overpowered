using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class LightingProbe
{
    const string Key="Overpowered.LightingProbe";
    public static void Run(){Directory.CreateDirectory("Verification/Lighting");SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){if(!SessionState.GetBool(Key,false))return;GameFlow.VerificationSandbox=false;Directory.CreateDirectory("Verification/Lighting/saves");WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Lighting/saves/probe-"+Guid.NewGuid().ToString("N")+".json");}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(!SessionState.GetBool(Key,false))return;var r=new GameObject("Lighting probe").AddComponent<LightingProbeRunner>();UnityEngine.Object.DontDestroyOnLoad(r.gameObject);r.Finished=c=>{SessionState.EraseBool(Key);EditorApplication.Exit(c);};}
}
