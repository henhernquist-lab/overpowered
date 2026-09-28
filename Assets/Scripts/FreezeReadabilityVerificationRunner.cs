#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FreezeReadabilityVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;
    const string Folder="Verification/FreezeReadability/";
    readonly List<string> output=new List<string>();string failure;
    WorldSession W=>WorldSession.Instance;
    FreezeIndicatorPool Pool=>FreezeIndicatorPool.Current;
    Camera Cam=>Camera.main;
    void Awake(){Application.logMessageReceived+=Observe;}
    void OnDestroy(){Application.logMessageReceived-=Observe;}
    void Observe(string message,string trace,LogType type){if((type==LogType.Exception||type==LogType.Error)&&trace.Contains("Assets/Scripts/"))failure=message;}
    void Log(string s){output.Add(s);Debug.Log(s);}
    void Check(bool yes,string s){if(!yes)throw new Exception(s);Log("PASS "+s);}
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0){object next=null;bool moved=false;try{if(failure!=null)throw new Exception(failure);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}catch(Exception e){Log("FAIL "+e);File.WriteAllLines(Folder+"results.txt",output);Finished(1);yield break;}if(!moved){stack.Pop();continue;}if(next is IEnumerator child)stack.Push(child);else yield return next;}
        File.WriteAllLines(Folder+"results.txt",output);Finished(0);
    }
    IEnumerator Scene(string name){float end=Time.realtimeSinceStartup+45;while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null)){if(Time.realtimeSinceStartup>end)throw new Exception("Scene timeout "+name);yield return null;}yield return new WaitForSecondsRealtime(.5f);}
    void Aim(Vector3 target){Cam.transform.position=W.Hero.transform.position+new Vector3(0,1.7f,-1);Cam.transform.LookAt(target);Physics.SyncTransforms();}
    CityNpc Actor(Vector3 at,NpcRole role=NpcRole.Criminal){var npc=CityNpc.Spawn(W,W.City.Sidewalks[0],role);npc.enabled=false;npc.Agent.enabled=false;npc.transform.position=at;npc.SetCombatStats(1000,0);Physics.SyncTransforms();return npc;}
    Renderer Glyph(GameObject target){var look=target.GetComponent<FrozenLook>();return look!=null?look.Indicator:null;}
    void Shape(GameObject target,string label)
    {
        var glyph=Glyph(target);Check(glyph!=null&&glyph.gameObject.activeInHierarchy,label+": visible frost silhouette exists.");
        var mesh=glyph.GetComponent<MeshFilter>().sharedMesh;
        Check(mesh.subMeshCount==2&&mesh.vertexCount==144,label+": six-ray branched shape with border is shared geometry.");
        Check(glyph.sharedMaterials[0]==CityMaterials.Get(CityColor.Cream)&&glyph.sharedMaterials[1]==CityMaterials.Get(CityColor.UiNavy),label+": cream shape/navy outline use shared palette materials.");
        Check(glyph.GetComponent<Collider>()==null&&glyph.transform.parent==Pool.transform,label+": glyph does not affect target physics or its renderer snapshot.");
    }
    IEnumerator Capture(GameObject target,string file)
    {
        Cam.transform.position=target.transform.position+new Vector3(3,2.2f,-5);Cam.transform.LookAt(target.transform.position+Vector3.up*1.5f);yield return null;
        var rt=new RenderTexture(960,540,24);rt.Create();var prior=Cam.targetTexture;Cam.targetTexture=rt;Cam.Render();Cam.targetTexture=prior;var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes(Folder+file+".png",image.EncodeToPNG());RenderTexture.active=old;Destroy(image);rt.Release();Destroy(rt);
    }
    Texture2D Render(){var rt=new RenderTexture(960,540,24);rt.Create();var prior=Cam.targetTexture;Cam.targetTexture=rt;Cam.Render();Cam.targetTexture=prior;var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;rt.Release();Destroy(rt);return image;}
    static float Luma(Color c)=>.2126f*c.r+.7152f*c.g+.0722f*c.b;
    static bool Wears(CityNpc n,CityColor c)=>n.GetComponentsInChildren<SkinnedMeshRenderer>().Any(x=>x.sharedMaterials.Contains(CityMaterials.Get(c)));
    static string Mats(CityNpc n)=>string.Join(",",n.GetComponentsInChildren<Renderer>(true).Select(x=>x.GetType().Name+":"+x.name+"="+string.Join("/",x.sharedMaterials.Select(m=>m!=null?m.name:"null"))));
    static bool AllIce(CityNpc n)=>n.GetComponentsInChildren<SkinnedMeshRenderer>().All(x=>x.sharedMaterials.All(m=>m==CityMaterials.Get(CityColor.Cyan)));
    /// Rendered visibility of the SAME glyph in front of a wall of the given palette colour: glyph ON vs OFF frames.
    IEnumerator Visibility(CityNpc target,CityColor wallColor,string file)
    {
        var glyph=Glyph(target.gameObject);Vector3 g=glyph.transform.position;
        Cam.transform.position=g+new Vector3(-1.5f,-.4f,-4.5f);Cam.transform.LookAt(g);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);DestroyImmediate(wall.GetComponent<Collider>());wall.transform.rotation=Cam.transform.rotation;wall.transform.position=g+Cam.transform.forward*3f;wall.transform.localScale=new Vector3(10,10,.1f);wall.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(wallColor);
        yield return null;
        var on=Render();glyph.enabled=false;var off=Render();glyph.enabled=true;
        Vector3 screen=Cam.WorldToViewportPoint(glyph.transform.position);int cx=(int)(screen.x*960),cy=(int)(screen.y*540);
        Color reference=off.GetPixel(Mathf.Clamp(cx-150,0,959),cy);int changed=0,strong=0,darker=0,lighter=0;Color background=Color.black;
        var onPixels=on.GetPixels32();var offPixels=off.GetPixels32();
        for(int i=0;i<onPixels.Length;i++)
        {
            Color a=onPixels[i],b=offPixels[i];if(Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b))<8/255f)continue;
            changed++;background+=b;float d=Luma(a)-Luma(b);if(d<=-.25f){strong++;darker++;}else if(d>=.25f){strong++;lighter++;}
        }
        background/=Mathf.Max(1,changed);
        File.WriteAllBytes(Folder+file+".png",on.EncodeToPNG());Destroy(on);Destroy(off);Destroy(wall);
        Log("MEASURED "+wallColor+" wall: glyph pixels="+changed+", |dLuma|>=0.25: "+strong+" ("+(100f*strong/Mathf.Max(1,changed)).ToString("F0")+"%; navy-darker "+darker+", cream-lighter "+lighter+"); mean backdrop under glyph "+background+" vs wall sample "+reference);
        Check(Mathf.Max(Mathf.Abs(background.r-reference.r),Mathf.Abs(background.g-reference.g),Mathf.Abs(background.b-reference.b))<.12f,"Glyph is actually composited over the "+wallColor+" wall.");
        Check(strong>=300&&strong>=.25f*changed,"Frost silhouette reads by shape over a "+wallColor+" backdrop (>=300 px and >=25% of glyph pixels at >=0.25 luma contrast).");
    }
    /// Palette-level WCAG contrast: at least one of the glyph's two colours must reach 3:1 against the backdrop colour.
    void Contrast(CityColor backdrop)
    {
        float L(CityColor c){Color v=CityMaterials.Current.Palette.Colors[(int)c].linear;return .2126f*v.r+.7152f*v.g+.0722f*v.b;}
        float Ratio(CityColor a,CityColor b){float x=L(a),y=L(b);return (Mathf.Max(x,y)+.05f)/(Mathf.Min(x,y)+.05f);}
        float cream=Ratio(CityColor.Cream,backdrop),navy=Ratio(CityColor.UiNavy,backdrop);
        Log("MEASURED palette contrast vs "+backdrop+": cream "+cream.ToString("F2")+":1, navy outline "+navy.ToString("F2")+":1");
        Check(Mathf.Max(cream,navy)>=3f,"Palette glyph colours reach 3:1 against "+backdrop+".");
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);var f=Resources.Load<ForgeCatalog>("ForgeCatalog");var p=FindAnyObjectByType<ModeScreens>().Profile;var ice=Resources.Load<PowerDefinition>("Powers/ice");var fire=Resources.Load<PowerDefinition>("Powers/fire");
        Check(p.SetLoadout(f.Heroes[0],fire,ice,CityColor.Teal,CityColor.Cyan),"Save Fire/Ice pair through ownership/equip gates.");GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));yield return Scene(GameFlow.CityScene);
        W.Hero.enabled=false;var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=new Vector3(0,150,0);cc.enabled=true;Cam.GetComponent<ThirdPersonCamera>().enabled=false;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,149.5f,0);floor.transform.localScale=new Vector3(40,1,40);floor.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Teal);
        var target=Actor(new Vector3(0,150,9));var control=Actor(new Vector3(4,150,9),NpcRole.Cop);yield return null;int baseline=Pool.ActiveCount,count=Pool.PoolCount,materials=Resources.FindObjectsOfTypeAll<Material>().Length;var originalMaterials=new HashSet<Material>(Resources.FindObjectsOfTypeAll<Material>());
        W.Powers.Tick(100,true);var rt=W.Powers.Powers.Find(x=>x.Definition==ice);W.Powers.Select(rt);Aim(target.transform.position+Vector3.up*1.2f);
        Check(W.Powers.Use(rt),"Direct Ice uses the real equipped power.");yield return null;yield return null;yield return null;Shape(target.gameObject,"Direct Ice NPC");var reused=Glyph(target.gameObject);
        Check(Glyph(control.gameObject)==null&&!control.Frozen,"Off-axis CONTROL has no glyph or freeze.");
        var presentation=target.GetComponent<HumanoidPresentation>();var head=presentation.Animator.GetBoneTransform(HumanBodyBones.Head);Quaternion pose=head.localRotation;yield return new WaitForSeconds(.25f);
        Check(presentation.FrozenOverlay&&presentation.Animator.speed==0&&Quaternion.Angle(pose,head.localRotation)<.1f,"Frozen Animator and presentation overlays both hold the head pose.");string controlLook=Mats(control);Check(Wears(control,CityColor.Teal)&&!AllIce(control),"Unfrozen control is a Teal-bodied cop beside the frozen all-Cyan target (its own accent: "+controlLook+")");
        yield return Capture(target.gameObject,"direct-ice-cyan-on-teal");
        yield return Visibility(target,CityColor.Cyan,"glyph-over-cyan-wall");yield return Visibility(target,CityColor.Teal,"glyph-over-teal-wall");
        Contrast(CityColor.Cyan);Contrast(CityColor.Teal);
        target.Thaw();yield return null;
        Check(Glyph(target.gameObject)==null&&!presentation.FrozenOverlay&&presentation.Animator.speed==1,"Explicit thaw clears silhouette and restores presentation.");
        control.Freeze(.6f,ice:false);yield return null;
        Check(control.Frozen&&!control.IceFrozen&&Glyph(control.gameObject)==null&&Mats(control)==controlLook&&control.GetComponent<HumanoidPresentation>().Animator.speed==1,"Non-ice hold CONTROL (Freeze(0.6, ice:false), the call CrimeEncounter police suppression makes) stops the NPC without frost glyph or material swap.");
        yield return new WaitForSeconds(.7f);Check(!control.Frozen&&Glyph(control.gameObject)==null,"Suppression CONTROL ends without leaving presentation behind.");
        // Root CONTROL on the thawed criminal target (the Cop control can be despawned by the periodic police reconcile).
        string targetLook=Mats(target);target.Root(.6f);yield return null;Check(target.Rooted&&!target.Frozen&&!target.IceFrozen&&Glyph(target.gameObject)==null&&Mats(target)==targetLook&&presentation.Animator.speed==1,"Root CONTROL (CityNpc.Root, the hold Darkness, Void Grasp and Eclipse Beam apply) is not a freeze: no frost glyph, material swap or held pose.");
        yield return new WaitForSeconds(.7f);Check(!target.Rooted&&Glyph(target.gameObject)==null&&Mats(target)==targetLook,"Root CONTROL ends without leaving presentation behind.");
        var prop=GameObject.CreatePrimitive(PrimitiveType.Cube);prop.transform.position=new Vector3(0,151,8);prop.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Teal);var body=prop.AddComponent<Rigidbody>();body.useGravity=false;body.constraints=RigidbodyConstraints.FreezeRotation;Physics.SyncTransforms();W.Powers.Tick(100,true);Aim(prop.transform.position);
        Check(W.Powers.Use(rt),"Direct Ice uses the real power on a Teal physics prop.");yield return null;Shape(prop,"Direct Ice prop");Check(Glyph(prop)==reused,"Pool reuses the released NPC marker for a prop.");yield return Capture(prop,"direct-ice-prop");
        prop.SetActive(false);yield return null;Check(Pool.ActiveCount==baseline,"Disabling frozen prop releases its glyph.");prop.SetActive(true);yield return null;Shape(prop,"Re-enabled frozen prop");
        prop.GetComponent<FrozenBody>().Thaw();yield return null;yield return null;Check(Glyph(prop)==null&&body.constraints==RigidbodyConstraints.FreezeRotation&&prop.GetComponent<Renderer>().sharedMaterial==CityMaterials.Get(CityColor.Teal),"Prop thaw restores exact constraints/material and releases glyph.");Destroy(prop);yield return null;
        target.MarkBurn(4);Aim(target.transform.position+Vector3.up*1.2f);var synergy=W.Powers.GetComponent<SynergyRunner>();Check(synergy.Definition.Effect is ThermalShockEffect&&synergy.TryActivate(),"Shipping Thermal Shock activated through real synergy gate on burning target.");float until=Time.time+3;while(!target.Frozen&&Time.time<until)yield return null;yield return null;Shape(target.gameObject,"Thermal Shock NPC");Check(target.IceFrozen&&AllIce(target)&&presentation.FrozenOverlay,"Thermal Shock uses the shared FrozenLook (Cyan palette material, held presentation) through CityNpc.Freeze.");yield return Capture(target.gameObject,"thermal-shock");
        target.Damage(100000,W.Powers);yield return null;yield return null;Check(target.Dead&&Glyph(target.gameObject)==null&&Pool.ActiveCount==baseline,"Frozen NPC death releases silhouette.");
        var doomed=GameObject.CreatePrimitive(PrimitiveType.Cube);doomed.AddComponent<Rigidbody>().useGravity=false;doomed.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Teal);doomed.AddComponent<FrozenBody>().Apply(2,CityColor.Cyan);yield return null;Shape(doomed,"Destruction control");Destroy(doomed);yield return null;yield return null;Check(Pool.ActiveCount==baseline,"Destroying frozen prop releases silhouette.");
        var timed=GameObject.CreatePrimitive(PrimitiveType.Cube);timed.AddComponent<Rigidbody>().useGravity=false;timed.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Teal);timed.AddComponent<FrozenBody>().Apply(.2f,CityColor.Cyan);yield return new WaitForSeconds(.4f);yield return null;Check(Glyph(timed)==null&&Pool.ActiveCount==baseline,"Timed prop thaw clears silhouette.");Destroy(timed);
        Log("MEASURED markers "+count+"->"+Pool.PoolCount+", growth="+Pool.Growths+", materials "+materials+"->"+Resources.FindObjectsOfTypeAll<Material>().Length+"; newly loaded: "+string.Join(",",Resources.FindObjectsOfTypeAll<Material>().Where(m=>!originalMaterials.Contains(m)).Select(m=>m.name)));
        Check(Pool.PoolCount==count&&Pool.Growths==0&&Resources.FindObjectsOfTypeAll<Material>().Length==materials,"Repeated freezes reuse preallocated markers without material growth.");GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);Check(Pool==null,"Session transition destroys its marker pool.");
        var catalog=Resources.Load<ForgeCatalog>("ForgeCatalog");
        Log("MEASURED current catalog synergies="+catalog.Synergies.Length+": "+string.Join(", ",catalog.Synergies.Select(d=>d.Id+"="+(d.Effect!=null?d.Effect.GetType().Name:"null"))));
        Check(catalog.Synergies.Select(d=>d.Id).OrderBy(n=>n).SequenceEqual(new[]{"eclipse-beam","solar-flare","sonic-slam","thermal-shock","void-grasp"}),"Catalog holds exactly the five shipping synergies.");
        // Only ThermalShockEffect calls CityNpc.Freeze (SynergyRunner.AffectNearby); Void Grasp and Eclipse Beam hold with CityNpc.Root
        // (checked above as a non-freeze CONTROL); Sonic Slam and Solar Flare neither freeze nor root.
        var freezing=catalog.Synergies.Where(d=>d.Effect is ThermalShockEffect).Select(d=>d.DisplayName).OrderBy(n=>n).ToArray();
        Log("MEASURED freezing synergies="+string.Join(", ",freezing)+"; rooting (no ice look by design)="+string.Join(", ",catalog.Synergies.Where(d=>d.Effect is VoidGraspEffect||d.Effect is EclipseBeamEffect).Select(d=>d.DisplayName)));
        Check(freezing.SequenceEqual(new[]{"Thermal Shock"}),"Every freezing synergy in the current catalog is exercised here (Thermal Shock above).");
        Log("LIMIT: batch entry-point tests and rendered screenshots; human silhouette readability and dense freeze performance still need review.");
    }
}
#endif
