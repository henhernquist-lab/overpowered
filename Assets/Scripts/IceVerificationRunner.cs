#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// Ice on REAL targets in the live city: an approaching, AI-driven Rusher (NavMeshAgent locomotion) and a falling crate.
/// Measures speed before / during / after the freeze, damage, freeze duration, the Frozen flag and the visible state, with
/// an off-axis CONTROL and an out-of-range CONTROL, in third person and first person. Tag "before" only records what the
/// code does; tag "after" also asserts the fixed behaviour.
public sealed class IceVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;public string Tag="before";
    const string Folder="Verification/Ice/";
    readonly List<string> output=new List<string>();string runtimeFailure;
    WorldSession W=>WorldSession.Instance;
    bool After=>Tag=="after";
    Camera Cam=>Camera.main;
    ThirdPersonCamera Rig=>Cam.GetComponent<ThirdPersonCamera>();
    PowerRuntime Ice=>W.Powers.Powers.Find(p=>p.Definition.Id=="ice");
    PowerDefinition Power(string id)=>Resources.Load<PowerDefinition>("Powers/"+id);
    void Awake(){Application.logMessageReceived+=ObserveLog;}
    void OnDestroy(){Application.logMessageReceived-=ObserveLog;}
    void ObserveLog(string message,string trace,LogType type)
    {if((type==LogType.Exception||type==LogType.Error||type==LogType.Assert)&&trace.Contains("Assets/Scripts/"))runtimeFailure=message;}
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{if(runtimeFailure!=null)throw new Exception("Gameplay Console error: "+runtimeFailure);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Write();Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Write();Finished(0);
    }
    void Log(string line){output.Add(line);Debug.Log(line);}
    void Check(bool ok,string line){if(!ok)throw new Exception(line);Log("PASS "+line);}
    /// Before: record. After: assert.
    void Expect(bool ok,string line){if(After)Check(ok,line);else Log((ok?"OBSERVED yes: ":"OBSERVED NO: ")+line);}
    void Write(){File.WriteAllLines(Folder+"results-"+Tag+".txt",output);}
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+60;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout");yield return null;}
        yield return new WaitForSecondsRealtime(.5f);
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);var profile=FindAnyObjectByType<ModeScreens>().Profile;
        var ice=Power("ice");
        Log($"DATA ice: damage={ice.Damage}, freeze duration={ice.Duration}s, range={ice.Range}m, charges={ice.Charges}, cooldown={ice.Cooldown}s, energy={ice.ResourceCost}, palette colour={ice.PaletteColor}.");
        Check(profile.SetLoadout(Resources.Load<ForgeCatalog>("ForgeCatalog").Heroes[0],ice,Power("strength"),CityColor.Blue,CityColor.Cyan),"Equip Ice + Strength through the saved loadout.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        foreach(bool first in new[]{false,true})yield return NpcCase(first);
        yield return PropCase();
        yield return OutOfRange();
        Log("LIMIT: casts go through PowerUser.Use (the method the mouse button calls) with the crosshair aimed by the test; no hardware input or human feel test.");
    }
    // ---------------------------------------------------------------- aiming
    void Hero(Vector3 at){W.Hero.enabled=false;var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=at;cc.enabled=true;W.Hero.ResetMotion();Physics.SyncTransforms();}
    void View(bool first)
    {
        if(first){Rig.enabled=true;if(!Rig.FirstPerson)Rig.ToggleView();}
        else{if(Rig.FirstPerson)Rig.ToggleView();Rig.enabled=false;}
    }
    /// Third person: a shoulder camera 1 m behind the hero's head. First person: the real rig's eye, via SetLook.
    void AimAt(Vector3 point,bool first)
    {
        if(first)
        {
            Vector3 eye=Cam.transform.position,d=point-eye;float flat=new Vector2(d.x,d.z).magnitude;
            Rig.SetLook(Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg,-Mathf.Atan2(d.y,flat)*Mathf.Rad2Deg);
        }
        else
        {
            Vector3 head=W.Hero.transform.position+Vector3.up*1.7f,dir=(point-head).normalized;
            Cam.transform.position=head-dir*1f+Vector3.up*.2f;Cam.transform.LookAt(point);
        }
    }
    Vector3 Chest(CityNpc npc)=>npc.transform.position+Vector3.up*1.2f;
    bool Clear(Vector3 from,Vector3 to)
    {
        foreach(var h in Physics.RaycastAll(from,(to-from).normalized,Vector3.Distance(from,to),~0,QueryTriggerInteraction.Ignore))
            if(h.transform.root!=W.Hero.transform&&h.collider.GetComponentInParent<CityNpc>()==null)return false;
        return true;
    }
    /// A sidewalk point at [min,max] m from the hero with a clear sight line from the hero's head.
    Vector3 Point(float min,float max,Vector3 avoid)
    {
        var head=W.Hero.transform.position+Vector3.up*1.7f;
        foreach(var p in W.City.Sidewalks.OrderBy(p=>Mathf.Abs(Vector3.Distance(p,W.Hero.transform.position)-(min+max)*.5f)))
        {
            float d=Vector3.Distance(p,W.Hero.transform.position);
            if(d<min||d>max||Vector3.Distance(p,avoid)<6||!Clear(head,p+Vector3.up*1.2f))continue;return p;
        }
        throw new Exception($"No clear sidewalk {min}-{max} m from the hero");
    }
    float speed;
    IEnumerator Speed(CityNpc npc,float seconds)
    {
        float t=0,dist=0;Vector3 last=npc.transform.position;
        while(t<seconds){yield return null;Vector3 now=npc.transform.position;Vector3 d=now-last;d.y=0;dist+=d.magnitude;last=now;t+=Time.deltaTime;}
        speed=dist/Mathf.Max(.001f,t);
    }
    int FrozenLookRenderers(CityNpc npc)
    {
        var cyan=CityMaterials.Get(CityColor.Cyan);
        return npc.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r=>r.sharedMaterials.All(m=>m==cyan));
    }
    // ---------------------------------------------------------------- live NPC
    IEnumerator NpcCase(bool first)
    {
        string view=first?"first-person":"third-person";
        Hero(W.City.Spawn+Vector3.up*.2f);View(first);yield return null;yield return null;
        // Keep ambient police/civilians from blocking the line: move them far away is not possible, so pick a clear lane.
        var lane=Point(13,19,Vector3.one*9999);
        var target=CityNpc.Spawn(W,lane,NpcRole.Criminal);target.AlwaysAggro=true;target.SetCombatStats(500,target.ContactDamage);
        Vector3 side=Vector3.Cross(Vector3.up,(lane-W.Hero.transform.position).normalized);
        var control=CityNpc.Spawn(W,W.City.NearestSidewalk(W.Hero.transform.position+side*14),NpcRole.Criminal);control.AlwaysAggro=true;control.SetCombatStats(500,control.ContactDamage);
        Check(target!=null&&control!=null,view+": live Rusher target and off-axis CONTROL spawned on the NavMesh.");
        yield return new WaitForSeconds(.6f);
        yield return Speed(target,.5f);float before=speed;yield return Speed(control,.2f);float controlBefore=speed;
        float distance=Vector3.Distance(target.transform.position,W.Hero.transform.position);
        Log($"MEASURED {view} target approach speed BEFORE={before:0.00} m/s at {distance:0.0} m; control speed={controlBefore:0.00} m/s.");
        Check(before>2f,$"{view}: target is genuinely moving under its own AI before the cast ({before:0.00} m/s).");
        for(int i=0;i<3;i++){AimAt(Chest(target),first);yield return null;}
        W.Powers.Tick(100,true);W.Powers.Select(Ice);AimAt(Chest(target),first);
        // The target runs through a live street: wait (bounded) for a moment when nothing (a parked car) is in the line.
        RaycastHit probe=default;float clearBy=Time.time+4;
        while(Time.time<clearBy&&!(W.Powers.FindTarget(W.Powers.Stats(Ice).Range,out probe)&&probe.collider.GetComponentInParent<CityNpc>()==target)){yield return null;AimAt(Chest(target),first);}
        Check(W.Powers.FindTarget(W.Powers.Stats(Ice).Range,out var aimed)&&aimed.collider.GetComponentInParent<CityNpc>()==target,$"{view}: crosshair ray selects the live target ({aimed.collider?.name}).");
        float hp=target.Health,controlHp=control.Health;int charges=Ice.Charges;
        int bursts=FeelDirector.Instance.Particles.Bursts;
        bool used=W.Powers.Use(Ice);float cast=Time.time;int castBursts=FeelDirector.Instance.Particles.Bursts-bursts;
        Check(used&&Ice.Charges==charges-1,$"{view}: real Ice cast accepted and paid ({W.Powers.Message}).");
        float damage=hp-target.Health;
        Log($"MEASURED {view} damage dealt={damage:0.##} (health {hp:0.#} -> {target.Health:0.#}); Frozen flag={target.Frozen}; agent.isStopped={target.Agent.isStopped}.");
        Check(target.Frozen&&!control.Frozen,$"{view}: aimed NPC Frozen flag set; off-axis CONTROL not frozen.");
        yield return null;yield return null;
        int iced=FrozenLookRenderers(target),renderers=target.GetComponentsInChildren<SkinnedMeshRenderer>().Length;
        float animSpeed=target.GetComponent<HumanoidPresentation>().Animator.speed;
        Log($"MEASURED {view} visible state during freeze: {iced}/{renderers} body renderers use the shared Cyan palette material; Animator.speed={animSpeed:0.##}.");
        yield return CaptureNpc(target,$"{Tag}-{view}-npc-frozen.png",first);
        yield return new WaitForSeconds(.3f);
        float windowEnd=cast+W.Powers.Stats(Ice).Duration-.15f;
        yield return Speed(target,Mathf.Max(.2f,windowEnd-Time.time));float during=speed;
        Log($"MEASURED {view} target speed DURING freeze={during:0.00} m/s; attack phase={target.Phase}.");
        float until=Time.time+W.Powers.Stats(Ice).Duration+2;while(target.Frozen&&Time.time<until)yield return null;
        float duration=Time.time-cast;
        Log($"MEASURED {view} freeze lasted {duration:0.00}s (configured {W.Powers.Stats(Ice).Duration:0.##}s).");
        // After the thaw the AI resumes its own cycle: with the Robber archetype (Phase 2) it may reach the 4.5 m wait ring and
        // circle at 1.2 m/s or commit a 0.45 s windup, so "resumes" is measured as mean speed over 1.5 s.
        yield return new WaitForSeconds(.2f);
        yield return Speed(target,1.5f);float after=speed;
        int icedAfter=FrozenLookRenderers(target);float animAfter=target.GetComponent<HumanoidPresentation>().Animator.speed;
        Log($"MEASURED {view} target speed AFTER thaw={after:0.00} m/s; cyan renderers after thaw={icedAfter}; Animator.speed={animAfter:0.##}.");
        Check(control.Health==controlHp&&!control.Frozen,$"{view}: off-axis CONTROL untouched (health {controlHp:0.#}, never frozen).");
        Check(during<.3f,$"{view}: NPC locomotion halted during the freeze ({before:0.00} -> {during:0.00} m/s).");
        Check(Mathf.Abs(duration-W.Powers.Stats(Ice).Duration)<.2f,$"{view}: freeze duration {duration:0.00}s matches Stats.Duration.");
        Check(after>.4f,$"{view}: NPC resumes moving after thaw ({after:0.00} m/s mean over 1.5 s).");
        Check(Mathf.Abs(damage-W.Powers.Stats(Ice).Damage)<.01f,$"{view}: Ice dealt its configured damage ({damage:0.##} = Stats.Damage {W.Powers.Stats(Ice).Damage:0.##}).");
        Log($"MEASURED {view} cast VFX bursts from the shared Feel pool: {castBursts}.");
        Expect(castBursts==2,$"{view}: the cast shows pooled ice bursts at the hand and at the target ({castBursts}).");
        Expect(iced==renderers&&renderers>0&&animSpeed==0f,$"{view}: frozen NPC is VISIBLY frozen (shared Cyan palette material on all {renderers} renderers, pose held).");
        Expect(icedAfter==0&&animAfter==1f,$"{view}: visible state clears on thaw (original materials, Animator.speed 1).");
        Destroy(target.gameObject);Destroy(control.gameObject);yield return new WaitForSeconds(.2f);
        if(first)View(false);
    }
    // ---------------------------------------------------------------- physics prop
    IEnumerator PropCase()
    {
        Hero(W.City.Spawn+Vector3.up*.2f);View(false);yield return null;
        var head=W.Hero.transform.position+Vector3.up*1.7f;Vector3 dir=Vector3.zero;
        // A direction whose drop column (14 m -> 4 m above the street, 9 m out, and 4 m to the side) is in clear view.
        for(int a=0;a<360&&dir==Vector3.zero;a+=15)
        {
            Vector3 d=Quaternion.Euler(0,a,0)*Vector3.forward,s2=Vector3.Cross(Vector3.up,d),c=W.Hero.transform.position+d*9;
            if(Clear(head,c+Vector3.up*14)&&Clear(head,c+Vector3.up*4)&&Clear(head,c+s2*4+Vector3.up*14)&&Clear(head,c+s2*4+Vector3.up*4))dir=d;
        }
        Check(dir!=Vector3.zero,"Prop: found a clear drop column 9 m from the hero.");Vector3 side=Vector3.Cross(Vector3.up,dir);
        Rigidbody Crate(Vector3 at){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Ice falling crate";go.transform.position=at;go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);var rb=go.AddComponent<Rigidbody>();rb.mass=45;return rb;}
        var crate=Crate(W.Hero.transform.position+dir*9+Vector3.up*14);var control=Crate(W.Hero.transform.position+dir*9+side*4+Vector3.up*14);
        Physics.SyncTransforms();yield return new WaitForSeconds(.45f);
        float before=crate.linearVelocity.magnitude;
        W.Powers.Tick(100,true);W.Powers.Select(Ice);for(int i=0;i<2;i++){AimAt(crate.position,false);yield return new WaitForFixedUpdate();}
        AimAt(crate.position,false);
        Check(W.Powers.Use(Ice),"Prop: Ice cast on a falling 45 kg crate accepted.");
        Vector3 held=crate.position;float cast=Time.time;
        yield return new WaitForSeconds(1f);
        float during=crate.linearVelocity.magnitude,drift=Vector3.Distance(crate.position,held);
        var cyan=CityMaterials.Get(CityColor.Cyan);var wood=CityMaterials.Get(CityColor.Wood);bool icedProp=crate.GetComponent<Renderer>().sharedMaterial==cyan;
        float until=Time.time+W.Powers.Stats(Ice).Duration+2;while(crate.GetComponent<FrozenBody>()!=null&&Time.time<until)yield return null;
        float duration=Time.time-cast;yield return new WaitForSeconds(.3f);float after=crate.linearVelocity.magnitude;
        Log($"MEASURED prop speed BEFORE={before:0.00} m/s, DURING={during:0.00} m/s (drift {drift:0.000} m), freeze {duration:0.00}s, AFTER={after:0.00} m/s; control crate speed={control.linearVelocity.magnitude:0.00} m/s.");
        Check(before>3&&during<.01f&&drift<.01f&&after>1,"Prop: falling crate stops in mid-air for the freeze and falls again after.");
        Check(control.constraints!=RigidbodyConstraints.FreezeAll&&control.GetComponent<FrozenBody>()==null,"Prop: off-axis CONTROL crate never frozen.");
        bool restored=crate.GetComponent<Renderer>().sharedMaterial==wood,controlWood=control.GetComponent<Renderer>().sharedMaterial==wood;
        Log($"MEASURED prop visible state: frozen crate shared Cyan material={icedProp}; restored to shared Wood after thaw={restored}; control crate Wood={controlWood}.");
        Expect(icedProp&&restored&&controlWood,"Prop: frozen crate shows the shared Cyan palette material and returns to its own shared material after thaw; control untouched.");
        Destroy(crate.gameObject);Destroy(control.gameObject);
    }
    // ---------------------------------------------------------------- out of range
    IEnumerator OutOfRange()
    {
        Hero(W.City.Spawn+Vector3.up*.2f);View(false);yield return null;
        float range=W.Powers.Stats(Ice).Range;var head=W.Hero.transform.position+Vector3.up*1.7f;
        W.Powers.Tick(100,true);W.Powers.Select(Ice);CityNpc far=null;float distance=0;string blocker="";
        // TEST HARNESS: every other NPC (encounter robbers/civilians, patrols) is deactivated for this control only and restored
        // after, so the only thing on the crosshair is the out-of-range target.
        var parked=W.Npcs.Where(n=>n!=null&&n.gameObject.activeSelf).ToList();foreach(var n in parked)n.gameObject.SetActive(false);
        // A lane where the crosshair ray reaches the NPC (beyond range) and nothing else lies within Ice's range.
        foreach(var p in W.City.Sidewalks.Where(p=>{float d=Vector3.Distance(p,W.Hero.transform.position);return d>range+4&&d<range+12;}).Where(p=>Clear(head,p+Vector3.up*1.2f)).Take(12))
        {
            far=CityNpc.Spawn(W,p,NpcRole.Criminal);if(far==null)continue;far.enabled=false;far.Agent.enabled=false;far.SetCombatStats(500,0);Physics.SyncTransforms();
            AimAt(Chest(far),false);yield return null;AimAt(Chest(far),false);Physics.SyncTransforms();
            bool nothingInRange=!W.Powers.FindTarget(range,out var near);if(!nothingInRange)blocker=near.collider.name;
            bool onRay=Physics.Raycast(W.Powers.AimOrigin,W.Powers.AimDirection,out var line,range+20,~0,QueryTriggerInteraction.Ignore)&&line.collider.GetComponentInParent<CityNpc>()==far;
            // AimDirection converges on the first hit within reach; beyond reach it points along the crosshair ray.
            if(nothingInRange&&(onRay||Vector3.Angle(W.Powers.AimDirection,Chest(far)-W.Powers.AimOrigin)<1.5f)){distance=Vector3.Distance(far.transform.position,W.Hero.transform.position);break;}
            Destroy(far.gameObject);far=null;yield return null;
        }
        Check(far!=null,"Out-of-range CONTROL setup: an NPC on the crosshair beyond range with nothing else inside range (last blocker: "+blocker+").");
        int charges=Ice.Charges;float hp=far.Health;
        bool used=W.Powers.Use(Ice);
        Check(!used&&!far.Frozen&&far.Health==hp&&Ice.Charges==charges,$"Out-of-range CONTROL: NPC {distance:0.0} m away (range {range:0.#} m) on the crosshair -> refused (\"{W.Powers.Message}\"), not frozen, not damaged, no charge spent.");
        Destroy(far.gameObject);foreach(var n in parked)if(n!=null)n.gameObject.SetActive(true);
    }
    IEnumerator CaptureNpc(CityNpc npc,string file,bool first)
    {
        var texture=new RenderTexture(960,540,24);texture.Create();var old=Cam.targetTexture;
        Vector3 pos=Cam.transform.position;Quaternion rot=Cam.transform.rotation;bool rig=Rig.enabled;Rig.enabled=false;
        Cam.transform.position=npc.transform.position+(W.Hero.transform.position-npc.transform.position).normalized*3.2f+Vector3.up*1.6f;Cam.transform.LookAt(npc.transform.position+Vector3.up*1f);
        Cam.targetTexture=texture;Cam.Render();
        var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
        File.WriteAllBytes(Folder+file,image.EncodeToPNG());Destroy(image);RenderTexture.active=previous;Cam.targetTexture=old;texture.Release();Destroy(texture);
        Cam.transform.SetPositionAndRotation(pos,rot);Rig.enabled=rig;yield return null;
    }
}
#endif
