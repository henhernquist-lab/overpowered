#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// NPC body cost A/B: mannequin NPCs (A) vs Sidekick NpcLooks (B), interleaved Free Play sessions at 3 stars in the densest
/// street, real gameplay camera single-render. Diagnostic: the tuning toggle is restored at exit.
/// -executeMethod SidekickNpcProfile.Run [-npcRounds 3] [-npcSeconds 5]
[InitializeOnLoad]
public static class SidekickNpcProfile
{
    const string Key="Overpowered.SidekickNpcProfile";static double deadline;
    static SidekickNpcProfile(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("NPC profile timeout");Finish(1);}};}
    public static void Run(){SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+5400;
        Directory.CreateDirectory("Verification/Sidekick/saves");WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Sidekick/saves/npc-profile-"+Guid.NewGuid().ToString("N")+".json");GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(!SessionState.GetBool(Key,false))return;var go=new GameObject("Sidekick NPC profile");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<SidekickNpcProfileRunner>().Finished=Finish;}
    static void Finish(int code){SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
