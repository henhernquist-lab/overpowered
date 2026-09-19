using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CameraVerification
{
    const string Key="Overpowered.CameraVerification";
    public static void Baseline() { Run("baseline"); }
    public static void Verify() { Run("fixed"); }
    public static void DirectPrototype() { Run("direct-prototype"); }
    static void Run(string task)
    {
        Directory.CreateDirectory("Verification/Cameras");
        var report=new System.Collections.Generic.List<string>();
        foreach(string name in new[]{"Home","Prototype","Results"})
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity");
            var cameras=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Camera>(true)).ToArray();
            report.Add($"EDIT {name}: roots={scene.rootCount}, cameras={cameras.Length}, enabled={cameras.Count(c=>c.isActiveAndEnabled)}.");
            if(task!="baseline"&&(cameras.Length!=1||!cameras[0].isActiveAndEnabled))throw new Exception("Missing enabled serialized camera: "+name);
        }
        File.WriteAllLines("Verification/Cameras/"+task+"-edit.txt",report);
        SessionState.SetString(Key,task);
        EditorSceneManager.OpenScene("Assets/Scenes/"+(task=="direct-prototype"?"Prototype":"Home")+".unity");
        EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(SessionState.GetString(Key,"")=="")return;
        GameFlow.VerificationSandbox=false;
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Cameras/save-"+Guid.NewGuid().ToString("N")+".json");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string task=SessionState.GetString(Key,"");if(task=="")return;
        var runner=new GameObject("Camera verification").AddComponent<CameraVerificationRunner>();
        UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);runner.Task=task;
        runner.Finished=code=>{SessionState.EraseString(Key);EditorApplication.Exit(code);};
    }
}
