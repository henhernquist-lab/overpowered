#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class PowerExpansionVerification
{
    const string Key="Overpowered.PowerExpansion2";static double deadline;
    static PowerExpansionVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Power Expansion verification timeout");Finish(1);}};}
    public static void PhaseB()=>Begin("b");
    public static void PhaseC()=>Begin("c");
    public static void PhaseD()=>Begin("d");
    public static void PhaseE()=>Begin("e");
    public static void Run()=>Begin("final");
    public static void Reload()=>Begin("reload");
    static void Begin(string phase){Directory.CreateDirectory("Verification/PowerExpansion2/saves");SessionState.SetString(Key,phase);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){if(SessionState.GetString(Key,"")=="")return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+900;GameFlow.VerificationSandbox=false;WorldSession.VerificationSavePath=SessionState.GetString(Key,"")=="reload"?File.ReadAllText("Verification/PowerExpansion2/saves/reload-path.txt"):Path.GetFullPath("Verification/PowerExpansion2/saves/"+Guid.NewGuid().ToString("N")+".json");}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){var phase=SessionState.GetString(Key,"");if(phase=="")return;var r=new GameObject("Power Expansion verifier").AddComponent<PowerExpansionVerificationRunner>();UnityEngine.Object.DontDestroyOnLoad(r.gameObject);r.Phase=phase;r.Finished=Finish;}
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
