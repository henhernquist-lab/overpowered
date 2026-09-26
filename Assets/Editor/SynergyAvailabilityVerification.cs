using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Synergies are available the moment their pair is equipped (no unlock/purchase), gated by long per-synergy cooldowns.
/// Run: fresh 0-point profile, Forge UI, real Play Mode activation / mid-cooldown rejection / re-fire after the cooldown,
/// refusal CONTROLs, tier purchases, and writes an OLD save without Fire. Reload (separate process): that old save migrates.
[InitializeOnLoad]
public static class SynergyAvailabilityVerification
{
    const string Key="Overpowered.SynergyAvailabilityVerification";const string Saves="Verification/Synergy/saves/";static double deadline;
    static SynergyAvailabilityVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Synergy availability timeout");Finish(1);}};}
    public static void Run(){Begin("run");}
    public static void Reload(){Begin("reload");}
    static void Begin(string task){Directory.CreateDirectory(Saves);SessionState.SetString(Key,task);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string task=SessionState.GetString(Key,"");if(task=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+900;GameFlow.VerificationSandbox=false;
        // Reload opens the OLD save the first process wrote (Fire/Ice/Telekinesis absent from its Powers list).
        WorldSession.VerificationSavePath=task=="reload"?File.ReadAllText(Saves+"old-save-path.txt").Trim():Path.GetFullPath(Saves+"save-"+Guid.NewGuid().ToString("N")+".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(SessionState.GetString(Key,"")=="")return;
        var go=new GameObject("Synergy availability verifier");UnityEngine.Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<SynergyAvailabilityVerificationRunner>();runner.Reload=SessionState.GetString(Key,"")=="reload";runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
