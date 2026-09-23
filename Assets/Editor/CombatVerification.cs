using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Telegraphed combat verification (real GameFlow/scenes, real NPC AI, real WorldSession.DamagePlayer, isolated save).
/// Unity -batchmode -projectPath <project> -executeMethod CombatVerification.Run -logFile <log>     (omit -quit)
/// Optional: -combatOnly archetypes|tokens|sessions|endless runs one part (iteration only; evidence = the full run).
[InitializeOnLoad]
public static class CombatVerification
{
    const string Key="Overpowered.CombatVerification";
    static double deadline;
    public static string Folder=>Path.GetFullPath("Verification/Combat");
    public static string SavePath=>Path.Combine(Folder,"save-run.json");
    static CombatVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Combat verification timeout");Finish(1);}};}
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        foreach(var file in new[]{SavePath,SavePath+".bak",SavePath+".tmp"}) if(File.Exists(file)) File.Delete(file);
        SessionState.SetString(Key,"run");EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;
        EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+900;
        WorldSession.VerificationSavePath=SavePath;GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(SessionState.GetString(Key,"")=="")return;
        var go=new GameObject("Combat verification");Object.DontDestroyOnLoad(go);
        var runner=go.AddComponent<CombatVerificationRunner>();runner.Folder=Folder;runner.Finished=Finish;
    }
    static void Finish(int code){SessionState.EraseString(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
