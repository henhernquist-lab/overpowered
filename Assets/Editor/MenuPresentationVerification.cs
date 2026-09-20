using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MenuPresentationVerification
{
    const string Key="Overpowered.MenuVerification";static double deadline;
    static MenuPresentationVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Menu verification timeout");Finish(1);}};}
    public static void Run(){MenuPresentationSetup.Create();SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){if(!SessionState.GetBool(Key,false))return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+300;WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Menus/save-"+Guid.NewGuid().ToString("N")+".json");GameFlow.VerificationSandbox=false;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(!SessionState.GetBool(Key,false))return;var go=new GameObject("Menu verification");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<MenuPresentationVerificationRunner>().Finished=Finish;}
    static void Finish(int code){SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
