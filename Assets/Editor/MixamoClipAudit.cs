#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// Explicit authoring audit for the nine newly supplied Mixamo clips. It never edits an importer,
/// a prefab, or the shared controller. Batch runs create only Verification/AnimationIntegration output.
public static class MixamoClipAudit
{
    public static readonly string[] Names={
        "X Bot@Head Hit", "X Bot@Taunt", "X Bot@Martelo 2", "X Bot@Falling",
        "X Bot@Terrified", "X Bot@Shoved Reaction With Spin", "X Bot@Jumping Down",
        "X Bot@Climbing Ladder", "X Bot@Running Forward Flip"
    };
    static readonly HumanBodyBones[] SampleBones={
        HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,
        HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg
    };
    static AnimationClip MovingClip(string path)
    {
        var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Where(c=>!c.name.StartsWith("__preview__")).ToArray();
        return clips.OrderByDescending(c=>c.length).FirstOrDefault();
    }
    [MenuItem("Overpowered/Animation/Audit new Mixamo clips")]
    public static void Run()
    {
        const string folder="Verification/AnimationIntegration";
        Directory.CreateDirectory(folder+"/Preview");
        var report=new List<string>();
        var catalog=Resources.Load<ForgeCatalog>("ForgeCatalog");
        var hero=catalog.Heroes.First(h=>h.CharacterPrefab!=null);
        var heroAvatar=hero.CharacterPrefab.GetComponent<Animator>().avatar;
        report.Add($"SIDEKICK {hero.DisplayName}: avatar valid={heroAvatar.isValid} human={heroAvatar.isHuman}; shared controller={Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning").Controller.name}");
        var cameraObject=new GameObject("Mixamo audit camera");var camera=cameraObject.AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.23f);camera.fieldOfView=37;
        camera.transform.position=new Vector3(3f,1.6f,5.4f);camera.transform.LookAt(new Vector3(0,1,0));
        var lightObject=new GameObject("Mixamo audit light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;
        light.intensity=1.4f;lightObject.transform.rotation=Quaternion.Euler(40,-35,0);
        try
        {
            foreach(var name in Names)
            {
                var path="Assets/Animations/"+name+".fbx";
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                if(importer==null)throw new Exception("Missing FBX: "+path);
                // Animation-only Mixamo FBXs may have no Animator component on the model root.
                // Their imported Avatar is a subasset, so inspect it directly.
                var sourceAvatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                var clip=MovingClip(path);
                var all=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(c=>!c.name.StartsWith("__preview__")).Select(c=>$"{c.name}:{c.length:F3}s");
                report.Add($"ASSET {path}: type={importer.animationType}, avatarSetup={importer.avatarSetup}, sourceAvatarValid={sourceAvatar!=null&&sourceAvatar.isValid}, sourceAvatarHuman={sourceAvatar!=null&&sourceAvatar.isHuman}, clips=[{string.Join(", ",all)}]");
                if(clip==null)throw new Exception("No imported AnimationClip: "+path);
                report.Add($"CLIP {name}: chosen={clip.name} duration={clip.length:F4}s frameRate={clip.frameRate:F1} looping={clip.isLooping} humanMotion={clip.humanMotion} averageSpeed={clip.averageSpeed} averageAngularSpeed={clip.averageAngularSpeed}");
                var clone=UnityEngine.Object.Instantiate(hero.CharacterPrefab);clone.name="Mixamo Sidekick retarget sample";
                clone.transform.position=Vector3.zero;clone.transform.rotation=Quaternion.identity;
                var animator=clone.GetComponent<Animator>();animator.enabled=true;animator.applyRootMotion=false;
                animator.runtimeAnimatorController=null;
                var graph=PlayableGraph.Create("Mixamo Sidekick retarget audit");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable=AnimationClipPlayable.Create(graph,clip);
                var output=AnimationPlayableOutput.Create(graph,"Sidekick",animator);
                output.SetSourcePlayable(playable);graph.Play();
                try
                {
                    Vector3 firstHip=Vector3.zero,lastHip=Vector3.zero,firstRoot=Vector3.zero,lastRoot=Vector3.zero;
                    float maxHipTravel=0f;
                    Quaternion firstRotation=Quaternion.identity,lastRotation=Quaternion.identity;
                    var firstPose=new Quaternion[SampleBones.Length];var lastPose=new Quaternion[SampleBones.Length];
                    for(int i=0;i<=8;i++)
                    {
                        float time=clip.length*i/8f;
                        playable.SetTime(time);graph.Evaluate(.0001f);
                        var hip=animator.GetBoneTransform(HumanBodyBones.Hips);
                        if(i==0){firstHip=hip.position;firstRoot=clone.transform.position;firstRotation=clone.transform.rotation;}
                        if(i==8){lastHip=hip.position;lastRoot=clone.transform.position;lastRotation=clone.transform.rotation;}
                        maxHipTravel=Mathf.Max(maxHipTravel,Vector3.Distance(firstHip,hip.position));
                        var pose=i==0?firstPose:i==8?lastPose:null;
                        for(int bone=0;bone<SampleBones.Length;bone++)
                        {
                            var t=animator.GetBoneTransform(SampleBones[bone]);
                            if(t==null||!Finite(t.position))throw new Exception($"Invalid {SampleBones[bone]} at {time:F3}s in {name}");
                            if(pose!=null)pose[bone]=t.localRotation;
                        }
                        if(i==0||i==2||i==4||i==6||i==8)
                            report.Add($"SAMPLE {name} t={time:F3}s root={clone.transform.position:F3} yaw={clone.transform.eulerAngles.y:F1} hip={hip.position:F3} leftHand={animator.GetBoneTransform(HumanBodyBones.LeftHand).position:F3} leftFoot={animator.GetBoneTransform(HumanBodyBones.LeftFoot).position:F3} rightFoot={animator.GetBoneTransform(HumanBodyBones.RightFoot).position:F3}");
                        if(i==0||i==2||i==4||i==6||i==8)
                        {
                            // Follow animated hips for the preview only. The report still records
                            // their displacement from the fixed gameplay/model root.
                            var focus=hip.position+Vector3.up*.25f;
                            camera.transform.position=focus+new Vector3(3f,1.6f,5.4f);
                            camera.transform.LookAt(focus);
                            CapturePose(camera,clone,folder+"/Preview/"+name.Replace(' ','-').Replace('@','-')+"-"+i+".png");
                        }
                    }
                    if(name=="X Bot@Martelo 2")
                    {
                        for(int frame=0;frame<=Mathf.RoundToInt(clip.length*clip.frameRate);frame++)
                        {
                            float time=Mathf.Min(clip.length,frame/clip.frameRate);
                            playable.SetTime(time);graph.Evaluate(.0001f);
                            var hip=animator.GetBoneTransform(HumanBodyBones.Hips);
                            var left=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                            var right=animator.GetBoneTransform(HumanBodyBones.RightFoot);
                            report.Add($"MARTELO frame={frame} t={time:F3} hip={hip.position:F3} left={left.position:F3} right={right.position:F3} leftFromHip={left.position-hip.position:F3} rightFromHip={right.position-hip.position:F3}");
                        }
                    }
                    float poseGap=0;for(int i=0;i<SampleBones.Length;i++)poseGap+=Quaternion.Angle(firstPose[i],lastPose[i]);
                    report.Add($"ROOT {name}: sampled-root-translation={Vector3.Distance(firstRoot,lastRoot):F3}m root-rotation={Quaternion.Angle(firstRotation,lastRotation):F1}deg hip-endpoint={Vector3.Distance(firstHip,lastHip):F3}m hip-max-from-start={maxHipTravel:F3}m mean-endpoint-bone-angle={poseGap/SampleBones.Length:F1}deg (small endpoint gap helps looping)");
                }
                finally{graph.Destroy();UnityEngine.Object.DestroyImmediate(clone);}
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);}
        File.WriteAllLines(folder+"/clip-audit.txt",report);
        Debug.Log("Mixamo clip audit wrote "+report.Count+" lines to "+folder+"/clip-audit.txt");
    }
    static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&
        !float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);
    // Batch Edit Mode updates humanoid bones, but its skinned draw can lag behind the manually
    // evaluated PlayableGraph. Bake the posed skin for each still so the preview shows the pose
    // actually measured above, then dispose every temporary renderer and mesh.
    static void CapturePose(Camera camera,GameObject model,string path)
    {
        var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var snapshots=new List<GameObject>(skins.Length);
        var meshes=new List<Mesh>(skins.Length);
        var hiddenSkins=new List<SkinnedMeshRenderer>(skins.Length);
        try
        {
            foreach(var skin in skins)
            {
                if(!skin.enabled)continue;
                var mesh=new Mesh();skin.BakeMesh(mesh);
                var snapshot=new GameObject("Audit posed skin");snapshot.transform.SetParent(skin.transform,false);
                snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;
                snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                skin.enabled=false;hiddenSkins.Add(skin);snapshots.Add(snapshot);meshes.Add(mesh);
            }
            Capture(camera,path);
        }
        finally
        {
            foreach(var skin in hiddenSkins)skin.enabled=true;
            foreach(var snapshot in snapshots)UnityEngine.Object.DestroyImmediate(snapshot);
            foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
    static void Capture(Camera camera,string path)
    {
        var target=new RenderTexture(640,480,24);target.Create();camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;target.Release();
        UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
    }
    public static void RunBatch()
    {
        try{Run();EditorApplication.Exit(0);}
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
#endif
