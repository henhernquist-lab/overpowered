using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run in an isolated project copy, without -quit. Run Reload in a SECOND Unity process.
[InitializeOnLoad]
public static class ModeVerification
{
    const string Key="Overpowered.ModeVerification";
    const string TestAsset="Assets/Resources/Modes/verification-third.asset";
    static double deadline;
    static ModeVerification() { EditorApplication.update+=Watchdog; }
    public static void Run()
    {
        ModeDataSetup.Create();
        if(File.Exists(TestAsset)) throw new InvalidOperationException("Refusing to overwrite existing test definition.");
        var mode=UnityEngine.Object.Instantiate(Resources.Load<GameModeDefinition>("Modes/hero"));
        mode.Id="verification-third";mode.DisplayName="Data-only test mode";mode.SuccessGoal=1;mode.MenuOrder=99;
        AssetDatabase.CreateAsset(mode,TestAsset);AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Verification/Modes");File.Copy(TestAsset,"Verification/Modes/third-mode-definition.asset",true);
        SessionState.SetString(Key,"run");SessionState.SetBool(Key+"owns",true);
        SessionState.SetString(Key+"save",Path.GetFullPath("Verification/Modes/save-"+Guid.NewGuid().ToString("N")+".json"));
        File.WriteAllText("Verification/Modes/save-path.txt",SessionState.GetString(Key+"save",""));
        Start();
    }
    public static void Reload()
    {SessionState.SetString(Key,"reload");SessionState.SetString(Key+"save",File.ReadAllText("Verification/Modes/save-path.txt"));Start();}
    static void Start() {EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;
        GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=SessionState.GetString(Key+"save","");
        deadline=EditorApplication.timeSinceStartup+240;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string task=SessionState.GetString(Key,"");if(task=="")return;
        var runner=new GameObject("Mode verification").AddComponent<ModeVerificationRunner>();
        UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);runner.Reload=task=="reload";runner.Finished=Finish;
    }
    static void Watchdog() {if(EditorApplication.isPlaying&&deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("Mode verification timed out.");Finish(1);}}
    static void Finish(int code)
    {
        SessionState.EraseString(Key);deadline=0;
        EditorApplication.isPlaying=false;
        EditorApplication.delayCall+=()=>{
            if(SessionState.GetBool(Key+"owns",false)){AssetDatabase.DeleteAsset(TestAsset);SessionState.EraseBool(Key+"owns");}
            EditorApplication.Exit(code);
        };
    }
}
