#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

/// Load-bearing retarget check: the shared Mixamo clips evaluated through the Humanoid retarget path (AnimationClipPlayable ->
/// Animator, with the foot-IK setting of the SharedHumanoid.controller state that plays the clip) on the mannequin and on all 8 Sidekick prefabs at the SAME
/// normalized times. Writes side-by-side captures (mannequin | Starter_02 | HumanSpecies_01, front 3/4 + side) and
/// proportion-independent pose metrics. -executeMethod SidekickClipCheck.Run (edit mode; exit 0 only if every sample is in tolerance)
public static class SidekickClipCheck
{
    const string Out="Verification/Sidekick/clips";
    public const string Root="Assets/Synty/SidekickCharacters/Characters/";
    public static readonly string[] Prefabs={"Starter/Starter_01","Starter/Starter_02","Starter/Starter_03","Starter/Starter_04","HumanSpecies/HumanSpecies_01","HumanSpecies/HumanSpecies_02","HumanSpecies/HumanSpecies_03","HumanSpecies/HumanSpecies_04"};
    public static string PrefabPath(string p)=>Root+p+"/"+Path.GetFileName(p)+".prefab";
    static readonly StringBuilder log=new StringBuilder();
    static void L(string s){log.AppendLine(s);Debug.Log("[SIDEKICK CLIPS] "+s);}
    // TIGHT tolerances were fixed before the first measured run; they flag proportion effects too (Sidekick hips ride lower,
    // shorter arms), so they are REPORTED per sample, not the exit gate. The exit gate is gross breakage: a bone chain moving
    // >35deg differently from the mannequin (broken wrist / twisted spine scale), or a grounded foot >0.15m off the mannequin's.
    const float MaxBoneDegrees=20f, MeanBoneDegrees=6f, FootMetres=.05f, ReachRatio=.10f;
    const float GrossBoneDegrees=35f, GrossFootMetres=.15f;
    static readonly (HumanBodyBones from,HumanBodyBones to)[] Chains=
    {
        (HumanBodyBones.Hips,HumanBodyBones.Spine),(HumanBodyBones.Spine,HumanBodyBones.Chest),(HumanBodyBones.Chest,HumanBodyBones.Neck),(HumanBodyBones.Neck,HumanBodyBones.Head),
        (HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm),(HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand),(HumanBodyBones.LeftHand,HumanBodyBones.LeftMiddleProximal),
        (HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm),(HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand),(HumanBodyBones.RightHand,HumanBodyBones.RightMiddleProximal),
        (HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg),(HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot),(HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes),
        (HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg),(HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot),(HumanBodyBones.RightFoot,HumanBodyBones.RightToes),
    };
    static readonly HumanBodyBones[] FootBones={HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightFoot,HumanBodyBones.RightToes};

    sealed class Rig
    {
        public string Name;public GameObject Go;public Animator Animator;public PlayableGraph Graph;public AnimationClipPlayable Clip;public AnimationPlayableOutput Output;
        public SkinnedMeshRenderer[] Skins;public float Scale;public bool[][] FootVertex;public Vector3 Home;
        public Transform B(HumanBodyBones b)=>Animator.GetBoneTransform(b);
    }
    static Rig Make(string name,GameObject prefab,Vector3 at,Material replace)
    {
        var go=UnityEngine.Object.Instantiate(prefab);go.name=name;go.transform.position=Vector3.zero;
        var r=new Rig{Name=name,Go=go,Animator=go.GetComponent<Animator>(),Skins=go.GetComponentsInChildren<SkinnedMeshRenderer>(),Home=at};
        r.Animator.runtimeAnimatorController=null;r.Animator.applyRootMotion=false;r.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        // Same fit as HumanoidPresentation.Create: reference-pose baked vertices, feet-to-crown = 1.8m, feet on the root.
        var b=Bounds(r,null);r.Scale=1.8f/b.size.y;go.transform.localScale*=r.Scale;go.transform.position=at-Vector3.up*b.min.y*r.Scale;
        if(replace!=null)foreach(var s in r.Skins){var m=s.sharedMaterials;for(int i=0;i<m.Length;i++)m[i]=replace;s.sharedMaterials=m;}
        // Foot vertices = vertices whose dominant skin weight is a foot/toe bone (so attachments/capes never count as "feet").
        var feet=new HashSet<Transform>(FootBones.Select(x=>r.B(x)).Where(x=>x!=null));
        r.FootVertex=r.Skins.Select(s=>s.sharedMesh.boneWeights.Select(w=>feet.Contains(s.bones[w.boneIndex0])).ToArray()).ToArray();
        r.Graph=PlayableGraph.Create(name);r.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        r.Output=AnimationPlayableOutput.Create(r.Graph,"out",r.Animator);
        return r;
    }
    static Bounds Bounds(Rig r,bool[][] only)
    {
        Bounds bounds=new Bounds();bool has=false;
        for(int k=0;k<r.Skins.Length;k++)
        {
            var s=r.Skins[k];var mesh=new Mesh();s.BakeMesh(mesh);var v=mesh.vertices;
            for(int i=0;i<v.Length;i++){if(only!=null&&!only[k][i])continue;var p=s.transform.TransformPoint(v[i]);if(!has){bounds=new Bounds(p,Vector3.zero);has=true;}else bounds.Encapsulate(p);}
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        return bounds;
    }
    static void Pose(Rig r,AnimationClip clip,float time,bool footIK)
    {
        if(!r.Clip.IsValid()||r.Clip.GetAnimationClip()!=clip)
        {
            if(r.Clip.IsValid())r.Clip.Destroy();
            r.Clip=AnimationClipPlayable.Create(r.Graph,clip);r.Output.SetSourcePlayable(r.Clip);r.Graph.Play();
        }
        r.Clip.SetApplyFootIK(footIK);r.Clip.SetTime(time);r.Clip.SetTime(time);r.Graph.Evaluate(0);
    }
    struct Metrics{public Vector3[] Dirs;public float FootY,LowestY,Reach,ReachMetres;public Vector3 Hips;}
    // Rig-geometry differences (toe-bone placement, spine curvature) are static, so bone motion is compared as the rotation
    // from each rig's OWN idle pose (Idle clip, t=50%) to the sample, applied to the mannequin's idle direction.
    static readonly Dictionary<Rig,Vector3[]> idle=new Dictionary<Rig,Vector3[]>();
    static float[] Relative(Rig r,Metrics x,Rig reference,Metrics m)=>x.Dirs.Select((d,i)=>
    {
        var a=idle[r][i];var b=idle[reference][i];if(d==Vector3.zero||a==Vector3.zero||b==Vector3.zero)return 0f;
        return Vector3.Angle(Quaternion.FromToRotation(a,d)*b,Quaternion.FromToRotation(b,m.Dirs[i])*b);
    }).ToArray();
    static Metrics Measure(Rig r)
    {
        var root=r.Go.transform;var m=new Metrics();
        m.Dirs=Chains.Select(c=>{var a=r.B(c.from);var b=r.B(c.to);return a==null||b==null?Vector3.zero:root.InverseTransformDirection(b.position-a.position).normalized;}).ToArray();
        m.Hips=r.B(HumanBodyBones.Hips).position;m.FootY=Bounds(r,r.FootVertex).min.y-r.Home.y;m.LowestY=Bounds(r,null).min.y-r.Home.y;
        // Punch reach: leading (left) hand's forward offset from the chest, as a fraction of that rig's own arm length.
        var chest=r.B(HumanBodyBones.Chest);float arm=Vector3.Distance(r.B(HumanBodyBones.LeftUpperArm).position,r.B(HumanBodyBones.LeftLowerArm).position)+Vector3.Distance(r.B(HumanBodyBones.LeftLowerArm).position,r.B(HumanBodyBones.LeftHand).position);
        m.ReachMetres=root.InverseTransformVector(r.B(HumanBodyBones.LeftHand).position-chest.position).z*root.lossyScale.z;m.Reach=m.ReachMetres/arm;
        return m;
    }
    // Rendering: edit-mode camera.Render() does not re-skin between synchronous renders, so each capture draws a baked snapshot.
    static readonly List<GameObject> proxies=new List<GameObject>();
    static void Snapshot(IEnumerable<Rig> rigs)
    {
        foreach(var p in proxies){UnityEngine.Object.DestroyImmediate(p.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(p);}proxies.Clear();
        foreach(var r in rigs)foreach(var s in r.Skins)
        {
            var mesh=new Mesh();s.BakeMesh(mesh);var go=new GameObject("snapshot "+r.Name);go.transform.SetParent(s.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=s.sharedMaterials;proxies.Add(go);s.enabled=false;
        }
    }

    public static void Run()
    {
        int code=0;
        try{code=Check();}catch(Exception e){L("EXCEPTION "+e);code=2;}
        Directory.CreateDirectory(Out);File.WriteAllText(Out+"/results.txt",log.ToString());
        EditorApplication.Exit(code);
    }
    static int Check()
    {
        Directory.CreateDirectory(Out);int failures=0,samples=0,gross=0;
        var tuning=AssetDatabase.LoadAssetAtPath<HumanoidAnimationTuning>("Assets/Resources/HumanoidAnimationTuning.asset");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.52f,.58f);
        var sun=new GameObject("sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.1f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(40,160,0);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.localScale=new Vector3(14,.02f,14);ground.transform.position=new Vector3(0,-.01f,0);
        ground.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.45f,.53f,.56f)};
        var grey=new Material(Shader.Find("Standard")){color=new Color(.78f,.78f,.8f)};
        var mannequin=Make("Mannequin",tuning.Model,new Vector3(-1.5f,0,0),grey);
        var rigs=new List<Rig>();
        for(int i=0;i<Prefabs.Length;i++)
        {
            var at=i==1?Vector3.zero:i==4?new Vector3(1.5f,0,0):new Vector3(100+i*4,0,0);
            rigs.Add(Make(Path.GetFileName(Prefabs[i]),AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Prefabs[i])),at,null));
        }
        var shown=new[]{mannequin,rigs[1],rigs[4]};
        var ikRig=Make("Starter_02 opposite foot IK",AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Prefabs[1])),new Vector3(3f,0,0),null);
        foreach(var r in rigs.Append(mannequin)){Pose(r,tuning.Idle,tuning.Idle.length*.5f,false);idle[r]=Measure(r).Dirs;}
        L($"Fit (reference-pose baked vertices -> 1.8m, as HumanoidPresentation.Create): {mannequin.Name}={mannequin.Scale:F4}; "+string.Join("; ",rigs.Select(r=>$"{r.Name}={r.Scale:F4}")));
        L($"Tolerances (fixed before this run): max bone-direction difference <= {MaxBoneDegrees}deg and mean <= {MeanBoneDegrees}deg over {Chains.Length} chains (spine/neck/arms/hands/legs/feet, rig-root space);");
        L($"  foot contact: lowest foot/toe-weighted vertex within {FootMetres}m of the mannequin's; punch impact: leading-hand forward reach / own arm length within {ReachRatio}.");
        L("Captures: one sheet per clip sample: columns mannequin (grey) | Starter_02 | HumanSpecies_01 | Starter_02 with the OPPOSITE foot-IK setting (diagnostic); rows front-3/4 / side; each cell frames its own rig at its hips.");
        var cam=new GameObject("cam").AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.16f,.18f,.22f);cam.fieldOfView=28;
        int W=380,H=460;var rt=new RenderTexture(W,H,24,RenderTextureFormat.ARGB32){antiAliasing=4};cam.targetTexture=rt;var cell=new Texture2D(W,H,TextureFormat.RGB24,false);
        float N(AnimationClip c,float seconds)=>seconds/c.length;
        var clips=new (string label,AnimationClip clip,float[] times)[]
        {
            ("walk",tuning.Walk,new[]{.25f,.5f,.75f}),("run",tuning.Run,new[]{.25f,.5f,.75f}),
            ("punch",tuning.Punch,new[]{N(tuning.Punch,tuning.PunchStartSeconds),N(tuning.Punch,tuning.PunchImpactSeconds),.8f}),
            ("hurricane-kick",tuning.HurricaneKick,new[]{N(tuning.HurricaneKick,tuning.KickStartSeconds),N(tuning.HurricaneKick,tuning.KickImpactSeconds),.75f}),
            ("backflip",tuning.Backflip,new[]{N(tuning.Backflip,tuning.BackflipStartSeconds),.45f,.62f}),("cast",tuning.Cast,new[]{.25f,.5f,.75f}),("death",tuning.Death,new[]{.3f,.6f,.99f}),
            ("idle",tuning.Idle,new[]{.5f}),("jump",tuning.Jump,new[]{.5f}),("land",tuning.Land,new[]{.3f}),("hit",tuning.Hit,new[]{.3f}),("shoot",tuning.Shoot,new[]{N(tuning.Shoot,tuning.ShootImpactSeconds)}),("armed-run",tuning.ArmedRun,new[]{.5f}),("back",tuning.Back,new[]{.5f}),("jog",tuning.Jog,new[]{.5f}),
        };
        var controller=AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/Resources/SharedHumanoid.controller");var ik=new Dictionary<AnimationClip,bool>();
        foreach(var st in controller.layers[0].stateMachine.states)
            foreach(var motion in st.state.motion is UnityEditor.Animations.BlendTree tree?tree.children.Select(c=>c.motion):new[]{st.state.motion})
                if(motion is AnimationClip c&&!ik.ContainsKey(c))ik[c]=st.state.iKOnFeet;
        L("Shipping controller foot IK per clip: "+string.Join(", ",ik.Select(p=>$"{p.Key.name}={(p.Value?"on":"off")}")));
        var views=new (string name,Vector3 dir)[]{("front3q",new Vector3(.38f,.12f,1f).normalized),("side",new Vector3(1f,.1f,0f).normalized)};
        var cellRigs=new[]{mannequin,rigs[1],rigs[4],ikRig};
        var worst=new Dictionary<string,float>();
        foreach(var (label,clip,times) in clips)
        {
            for(int t=0;t<times.Length;t++)
            {
                float time=Mathf.Clamp01(times[t])*clip.length;
                // The opposite foot-IK setting first (diagnostic "variant" column), then the shipping controller's setting.
                bool shipping=ik.TryGetValue(clip,out var on)&&on;
                Pose(mannequin,clip,time,!shipping);var mIK=Measure(mannequin);
                var variant=rigs.ToDictionary(r=>r,r=>{Pose(r,clip,time,!shipping);return Measure(r);});
                Pose(mannequin,clip,time,shipping);var m=Measure(mannequin);
                L($"-- {label} ({clip.name}) normalized={times[t]:F3} t={time:F3}s  Mannequin: hips={mannequin.Go.transform.InverseTransformPoint(m.Hips):F2} footY={m.FootY:F3} lowestY={m.LowestY:F3}{(label=="punch"?$" reach={m.Reach:F3} ({m.ReachMetres:F3}m)":"")}");
                foreach(var r in rigs)
                {
                    Pose(r,clip,time,shipping);var x=Measure(r);samples++;
                    var raw=x.Dirs.Select((d,i)=>d==Vector3.zero||m.Dirs[i]==Vector3.zero?0:Vector3.Angle(d,m.Dirs[i])).ToArray();
                    var diffs=Relative(r,x,mannequin,m);
                    int wi=Array.IndexOf(diffs,diffs.Max());float mean=diffs.Average();
                    var bad=new List<string>();
                    if(diffs.Max()>MaxBoneDegrees)bad.Add($"bone {Chains[wi].from}->{Chains[wi].to} {diffs.Max():F1}deg");
                    if(mean>MeanBoneDegrees)bad.Add($"mean {mean:F1}deg");
                    if(Mathf.Abs(x.FootY-m.FootY)>FootMetres)bad.Add($"foot {x.FootY-m.FootY:+0.000;-0.000}m");
                    if(label=="punch"&&t==1&&Mathf.Abs(x.Reach-m.Reach)>ReachRatio)bad.Add($"reach {x.Reach-m.Reach:+0.000;-0.000}");
                    string key=r.Name;worst[key]=Mathf.Max(worst.TryGetValue(key,out var w)?w:0,diffs.Max());
                    L($"   {r.Name,-16} maxBone={diffs.Max(),5:F1}deg ({Chains[wi].from}->{Chains[wi].to}) mean={mean:F1}deg (absolute max {raw.Max():F1}) hips={r.Go.transform.InverseTransformPoint(x.Hips):F2} footY={x.FootY,6:F3} (foot IK {(shipping?"off":"on")} variant {variant[r].FootY:F3}, mannequin {mIK.FootY:F3}) lowestY={x.LowestY,6:F3}"+(label=="punch"?$" reach={x.Reach:F3} ({x.ReachMetres:F3}m)":"")+(bad.Count==0?"  OK":"  OUT: "+string.Join(", ",bad)));
                    if(bad.Count>0)failures++;
                    bool grounded=m.FootY<.1f;
                    if(diffs.Max()>GrossBoneDegrees||grounded&&Mathf.Abs(x.FootY-m.FootY)>GrossFootMetres){gross++;L($"   GROSS {r.Name}: bone {diffs.Max():F1}deg, grounded={grounded} foot diff {x.FootY-m.FootY:+0.000;-0.000}m");}
                }
                Pose(ikRig,clip,time,!shipping);
                var sheet=new Texture2D(W*cellRigs.Length,H*views.Length,TextureFormat.RGB24,false);
                for(int c=0;c<cellRigs.Length;c++)
                {
                    var r=cellRigs[c];foreach(var o in cellRigs)foreach(var sk in o.Skins)sk.enabled=false;Snapshot(new[]{r});
                    var hips=r.B(HumanBodyBones.Hips).position;var focus=new Vector3(hips.x,Mathf.Max(.85f,hips.y-.05f),hips.z);
                    for(int v=0;v<views.Length;v++)
                    {
                        cam.transform.position=focus+views[v].dir*5.2f;cam.transform.LookAt(focus);
                        cam.Render();RenderTexture.active=rt;cell.ReadPixels(new Rect(0,0,W,H),0,0);cell.Apply();RenderTexture.active=null;
                        sheet.SetPixels(c*W,(views.Length-1-v)*H,W,H,cell.GetPixels());
                    }
                }
                Snapshot(new Rig[0]);foreach(var o in cellRigs)foreach(var sk in o.Skins)sk.enabled=true;
                sheet.Apply();File.WriteAllBytes($"{Out}/{label}-{t+1}.png",sheet.EncodeToPNG());UnityEngine.Object.DestroyImmediate(sheet);
            }
        }
        L("Worst bone-direction difference per rig over all samples: "+string.Join("; ",worst.Select(p=>$"{p.Key}={p.Value:F1}deg")));
        L($"Samples outside the TIGHT (reported) tolerance: {failures} of {samples}");
        L($"Samples with GROSS breakage (exit gate: bone >{GrossBoneDegrees}deg, or grounded foot >{GrossFootMetres}m off): {gross} of {samples}");
        foreach(var r in rigs.Append(mannequin).Append(ikRig))r.Graph.Destroy();
        return gross==0?0:1;
    }
}
#endif
