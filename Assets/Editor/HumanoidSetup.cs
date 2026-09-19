#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class HumanoidSetup
{
    public static readonly string[] Names={"Backflip","Casting Spell","Death","Fall A Land To Run Forward","Hit Reaction","Hurricane Kick","Idle","Jog Forward","Jump","Pistol Run","Punching","Shooting Gun","Standing Run Back","Standing Run Forward","Walking"};
    static bool Loops(string name)=>new[]{"Idle","Walking","Jog Forward","Standing Run Forward","Standing Run Back","Pistol Run"}.Contains(name);
    [MenuItem("Overpowered/Animation/Batch import Mixamo Humanoids")]
    public static void Import()
    {
        Directory.CreateDirectory("Verification/Humanoid");var report=new List<string>();
        foreach(string name in Names)
        {
            string path="Assets/Animations/"+name+".fbx";
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(importer==null)throw new Exception("Missing supplied FBX: "+path);
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation=true;importer.optimizeGameObjects=false;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
            var clips=importer.defaultClipAnimations;
            foreach(var c in clips)
            {
                c.name=name;c.loopTime=Loops(name);c.loopPose=Loops(name);
                c.lockRootRotation=true;c.keepOriginalOrientation=true;
                c.lockRootPositionXZ=true;c.keepOriginalPositionXZ=true;
                c.lockRootHeightY=true;c.keepOriginalPositionY=true;
            }
            importer.clipAnimations=clips;importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);var avatar=model.GetComponent<Animator>().avatar;
            if(!avatar.isValid||!avatar.isHuman)throw new Exception("Invalid Humanoid: "+path);
            var clip=Clip(name);var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();
            report.Add($"{name}: Humanoid valid={avatar.isValid}, length={clip.length:F4}s, rate={clip.frameRate}, skins={skins.Length}, vertices={skins.Sum(s=>s.sharedMesh.vertexCount)}, loop={clip.isLooping}");
        }
        var sample=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animations/Idle.fbx"));
        var animator=sample.GetComponent<Animator>();
        foreach(var s in sample.GetComponentsInChildren<SkinnedMeshRenderer>())report.Add($"Mesh {s.name}: world bounds={s.bounds}; local={s.localBounds}");
        var punch=Clip("Punching");
        for(float t=0;t<punch.length;t+=1f/30)
        {
            punch.SampleAnimation(sample,t);
            var right=sample.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position);
            var left=sample.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
            report.Add($"PUNCH SAMPLE t={t:F4}, right={right:F4}, left={left:F4}");
        }
        var shoot=Clip("Shooting Gun");
        for(float time=0;time<shoot.length;time+=.25f){shoot.SampleAnimation(sample,time);report.Add($"SHOOT SAMPLE t={time:F2} right={sample.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position):F3} left={sample.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position):F3}");}
        UnityEngine.Object.DestroyImmediate(sample);AssetDatabase.SaveAssets();File.WriteAllLines("Verification/Humanoid/import.txt",report);Debug.Log(string.Join("\n",report));
    }
    public static AnimationClip Clip(string name)=>AssetDatabase.LoadAllAssetsAtPath("Assets/Animations/"+name+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
    [MenuItem("Overpowered/Animation/Build shared humanoid presentation")]
    public static void Build()
    {
        Import();
        const string path="Assets/Resources/HumanoidAnimationTuning.asset";
        var t=AssetDatabase.LoadAssetAtPath<HumanoidAnimationTuning>(path);
        if(t==null){t=ScriptableObject.CreateInstance<HumanoidAnimationTuning>();AssetDatabase.CreateAsset(t,path);}
        t.Model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animations/Idle.fbx");
        t.Idle=Clip("Idle");t.Walk=Clip("Walking");t.Jog=Clip("Jog Forward");t.Run=Clip("Standing Run Forward");t.Back=Clip("Standing Run Back");
        t.Jump=Clip("Jump");t.Land=Clip("Fall A Land To Run Forward");t.Punch=Clip("Punching");t.Hit=Clip("Hit Reaction");t.Death=Clip("Death");t.Cast=Clip("Casting Spell");
        t.ArmedRun=Clip("Pistol Run");t.Shoot=Clip("Shooting Gun");t.Backflip=Clip("Backflip");t.HurricaneKick=Clip("Hurricane Kick");
        const string controllerPath="Assets/Resources/SharedHumanoid.controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        // Explicit menu rebuild, never an automatic import callback; controller GUID stays stable.
        foreach(var item in AssetDatabase.LoadAllAssetsAtPath(controllerPath))if(item!=controller)UnityEngine.Object.DestroyImmediate(item,true);
        var machine=new AnimatorStateMachine{name="Humanoid Base"};AssetDatabase.AddObjectToAsset(machine,controller);
        controller.layers=new[]{new AnimatorControllerLayer{name="Humanoid Base",defaultWeight=1,stateMachine=machine}};
        controller.parameters=new[]{new AnimatorControllerParameter{name="Speed",type=AnimatorControllerParameterType.Float},new AnimatorControllerParameter{name="Blend",type=AnimatorControllerParameterType.Float},new AnimatorControllerParameter{name="GaitRate",type=AnimatorControllerParameterType.Float,defaultFloat=1},new AnimatorControllerParameter{name="ActionRate",type=AnimatorControllerParameterType.Float,defaultFloat=1}};
        var tree=new BlendTree{name="Continuous actual speed",blendType=BlendTreeType.Simple1D,blendParameter="Blend",useAutomaticThresholds=false};AssetDatabase.AddObjectToAsset(tree,controller);
        tree.AddChild(t.Idle,0);tree.AddChild(t.Walk,1);tree.AddChild(t.Jog,2);tree.AddChild(t.Run,3);
        AnimatorState Add(string name,Motion clip,string rate)
        {var state=machine.AddState(name);state.motion=clip;state.writeDefaultValues=true;state.speedParameterActive=true;state.speedParameter=rate;return state;}
        machine.defaultState=Add("Locomotion",tree,"GaitRate");
        Add("Back",t.Back,"GaitRate");Add("Armed",t.ArmedRun,"GaitRate");Add("Panic",t.Run,"GaitRate");
        Add("Air",t.Jump,"GaitRate");Add("Fly",t.Idle,"GaitRate");
        Add("Jump",t.Jump,"ActionRate");Add("Land",t.Land,"ActionRate");Add("Punch",t.Punch,"ActionRate");Add("Cast",t.Cast,"ActionRate");Add("Hit",t.Hit,"ActionRate");Add("Death",t.Death,"ActionRate");Add("Shoot",t.Shoot,"ActionRate");
        Add("Unwired Backflip",t.Backflip,"ActionRate");Add("Unwired Hurricane Kick",t.HurricaneKick,"ActionRate");
        t.Controller=controller;EditorUtility.SetDirty(t);EditorUtility.SetDirty(controller);
        foreach(var power in Resources.LoadAll<PowerDefinition>("Powers"))if(power.Effect is FireBlastEffect||power.Effect is IceEffect){power.CastingPresentation=true;EditorUtility.SetDirty(power);}
        AssetDatabase.SaveAssets();
    }
    public static void BuildBatch(){try{Build();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    public static void ImportBatch(){try{Import();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
#endif
