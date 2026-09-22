#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BackflipHurricaneVerification
{
    const string Key="Overpowered.BackflipHurricaneVerification";static double deadline;
    static BackflipHurricaneVerification(){EditorApplication.update+=()=>{if(deadline>0&&EditorApplication.timeSinceStartup>deadline){Debug.LogError("BACKFLIP/HURRICANE verification timeout");Finish(1);}};}
    public static void Run()
    {
        Directory.CreateDirectory("Verification/Abilities");SessionState.SetBool(Key,true);
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");EditorApplication.isPlaying=true;
    }
    // Edit-mode clip sampling: where in each source clip the gesture actually reads as a strike.
    // Same technique the punch timing was originally derived with (HumanoidSetup.Import).
    public static void Sample()
    {
        try
        {
            Directory.CreateDirectory("Verification/Abilities");
            var report=new List<string>();
            var sample=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animations/Idle.fbx"));
            var animator=sample.GetComponent<Animator>();
            foreach(string name in new[]{"Backflip","Hurricane Kick"})
            {
                var clip=HumanoidSetup.Clip(name);
                report.Add($"{name}: length={clip.length:F4}s rate={clip.frameRate} frames={Mathf.RoundToInt(clip.length*clip.frameRate)}");
                int peakForward=-1;float peak=float.MinValue;
                for(float t=0;t<clip.length;t+=1f/30)
                {
                    clip.SampleAnimation(sample,t);
                    var hips=sample.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
                    var right=sample.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightFoot).position);
                    var left=sample.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position);
                    int frame=Mathf.RoundToInt(t*30);
                    if(right.z>peak){peak=right.z;peakForward=frame;}
                    report.Add($"{name} frame={frame} t={t:F4} hipsY={hips.y:F4} rightFoot=({right.x:F3},{right.y:F3},{right.z:F3}) leftFoot=({left.x:F3},{left.y:F3},{left.z:F3})");
                }
                report.Add($"{name}: peak right-foot forward extension at frame {peakForward} (z={peak:F3}m)");
            }
            UnityEngine.Object.DestroyImmediate(sample);
            File.WriteAllLines("Verification/Abilities/clip-sample.txt",report);
            Debug.Log(string.Join("\n",report));
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if(!SessionState.GetBool(Key,false))return;EditorApplication.LockReloadAssemblies();deadline=EditorApplication.timeSinceStartup+180;
        WorldSession.VerificationSavePath=Path.GetFullPath("Verification/Abilities/save-"+Guid.NewGuid().ToString("N")+".json");GameFlow.VerificationSandbox=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(!SessionState.GetBool(Key,false))return;var obj=new GameObject("Backflip/hurricane verification");UnityEngine.Object.DontDestroyOnLoad(obj);var runner=obj.AddComponent<BackflipHurricaneVerificationRunner>();runner.Finished=Finish;}
    static void Finish(int code){SessionState.EraseBool(Key);deadline=0;EditorApplication.UnlockReloadAssemblies();EditorApplication.Exit(code);}
}
#endif
