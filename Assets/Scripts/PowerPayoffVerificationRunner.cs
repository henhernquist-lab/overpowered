#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PowerPayoffVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;
    const string Folder="Verification/Payoff/";
    readonly List<string> lines=new List<string>();
    WorldSession W=>WorldSession.Instance;
    PowerUser U=>W.Powers;
    Camera Cam=>Camera.main;
    Vector3 stage;
    string error;
    void Awake(){Application.logMessageReceived+=Observe;}
    void OnDestroy(){Application.logMessageReceived-=Observe;}
    void Observe(string message,string trace,LogType type){if((type==LogType.Exception||type==LogType.Error)&&trace.Contains("Assets/Scripts/"))error=message;}
    void Log(string s){lines.Add(s);Debug.Log(s);File.WriteAllLines(Folder+"results.txt",lines);}
    void Check(bool ok,string s){if(!ok)throw new Exception(s);Log("PASS "+s);}
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            object next=null;bool more=false;
            try{if(error!=null)throw new Exception(error);more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Finished(1);yield break;}
            if(!more){stack.Pop();continue;}if(next is IEnumerator child)stack.Push(child);else yield return next;
        }
        Finished(0);
    }
    IEnumerator Scene(string name)
    {
        float end=Time.realtimeSinceStartup+60;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>end)throw new Exception("Scene timeout");yield return null;}
        yield return new WaitForSeconds(.3f);
    }
    PowerDefinition Def(string id)=>Resources.Load<PowerDefinition>("Powers/"+id);
    PowerRuntime Power(string id)=>U.Powers.Find(p=>p.Definition.Id==id);
    IEnumerator Enter(string a,string b)
    {
        yield return Scene(GameFlow.HomeScene);
        var profile=FindAnyObjectByType<ModeScreens>().Profile;
        Check(profile.SetLoadout(Resources.Load<ForgeCatalog>("ForgeCatalog").Heroes[0],Def(a),Def(b),CityColor.Blue,CityColor.Cyan),"Equip "+a+" + "+b);
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        stage=W.City.Spawn+Vector3.up*200;
        U.Hero.enabled=false;var cc=U.GetComponent<CharacterController>();cc.enabled=false;U.transform.SetPositionAndRotation(stage,Quaternion.identity);cc.enabled=true;U.Hero.ResetMotion();
        Cam.GetComponent<ThirdPersonCamera>().enabled=false;
        Physics.SyncTransforms();
    }
    void Aim(Vector3 point,bool first=false)
    {
        var rig=Cam.GetComponent<ThirdPersonCamera>();
        if(rig.FirstPerson!=first){rig.enabled=true;rig.ToggleView();rig.enabled=false;}
        Cam.transform.position=stage+(first?Vector3.up*1.62f:new Vector3(0,2.8f,-7.6f));Cam.transform.LookAt(point);
    }
    CityNpc Npc(Vector3 at)
    {
        var npc=CityNpc.Spawn(W,W.City.NearestSidewalk(W.City.Spawn),NpcRole.Criminal);
        npc.enabled=false;npc.Agent.enabled=false;npc.transform.position=at;npc.SetCombatStats(1000,0);
        Physics.SyncTransforms();return npc;
    }
    Rigidbody Crate(Vector3 at,float mass=45)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Payoff test prop";go.transform.position=at;go.transform.localScale=Vector3.one*.5f;
        go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);
        var body=go.AddComponent<Rigidbody>();body.mass=mass;body.useGravity=false;Physics.SyncTransforms();return body;
    }
    IEnumerator Run()
    {
        yield return Enter("ice","strength");
        foreach(bool kick in new[]{false,true})foreach(bool frozen in new[]{false,true})yield return Melee(kick,frozen);
        yield return PropShatter();
        yield return LethalShatter();
        yield return NonMelee();
        GameFlow.Instance.Home();yield return Enter("ice","telekinesis");
        foreach(bool first in new[]{false,true})foreach(float mass in new[]{45f,400f})yield return Throw(first,mass);
        yield return Throw(false,400,true);
        yield return ThrowWall();
        GameFlow.Instance.Home();yield return Enter("flight","telekinesis");
        yield return Orbit();
        Log("LIMIT: real gameplay entry points and PhysX steps, not hardware input or human feel acceptance. FPS is in the separate PowerPayoffBenchmark run.");
    }
    IEnumerator Melee(bool kick,bool frozen)
    {
        yield return new WaitForSeconds(.7f);U.Tick(100,true);
        var npc=Npc(stage+Vector3.forward*2.7f);
        U.Select(Power("ice"));Aim(npc.transform.position+Vector3.up);
        if(frozen)Check(U.Use(Power("ice"))&&npc.Frozen,"Real paid Ice freeze before melee");
        float hp=npc.Health;int particles=FeelDirector.Instance.Particles.ParticlesEmitted;
        float before=kick?U.Hero.LastKickImpactTime:U.Hero.LastImpactTime;
        Check(kick?U.Hero.TryHurricaneKick():U.Hero.TryPunch(),(kick?"Kick":"Punch")+" accepted");
        float until=Time.time+2;
        while((kick?U.Hero.LastKickImpactTime:U.Hero.LastImpactTime)==before&&Time.time<until)yield return null;
        Check((kick?U.Hero.LastKickImpactTime:U.Hero.LastImpactTime)>before,"Waited for actual melee impact after windup");
        var rb=npc.GetComponent<Rigidbody>();yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
        float expected=U.Stats(U.Strength).Damage*(kick?HeroAbilityTuning.KickDamageMultiplier:1)+(frozen?30:0);
        float speed=rb!=null?rb.linearVelocity.magnitude:0;
        Check(Mathf.Abs(hp-npc.Health-expected)<.01f,$"{(kick?"KICK":"PUNCH")} frozen={frozen}: damage={hp-npc.Health:F2}, expected={expected:F2}, speed={speed:F3}m/s, base impulse={(kick?U.Hero.LastKickForce:U.Hero.LastForce):F0}, shatter impulse={(frozen?1800:0)} N.s");
        Check(!npc.Frozen&& (frozen?speed>1:rb==null),"Frozen-state CONTROL: only frozen melee launches, freeze consumed");
        int count=FeelDirector.Instance.Particles.ParticlesEmitted-particles;
        Check(count==(frozen?28:0)+FeelDirector.Instance.Settings.HeavyHitParticles,$"Shatter particle CONTROL: total={count}, ice-only bonus={(frozen?28:0)}");
        if(frozen)
        {
            Check(npc.GetComponent<FrozenLook>().Shown==false&&npc.GetComponent<HumanoidPresentation>().Animator.speed>0,"Frozen look and pose hold clear immediately");
            var suspension=npc.GetComponent<SynergySuspension>();suspension.Finish();npc.transform.position=stage+Vector3.forward*2.7f;Physics.SyncTransforms();
            yield return new WaitForSeconds(.7f);U.Tick(100,true);hp=npc.Health;
            Check(U.Hero.TryPunch(),"Second punch on consumed freeze accepted");yield return new WaitForSeconds(.3f);
            Check(Mathf.Abs(hp-npc.Health-U.Stats(U.Strength).Damage)<.01f,"Consumed freeze CONTROL: next hit has no second bonus");
        }
        Destroy(npc.gameObject);yield return null;
    }
    IEnumerator PropShatter()
    {
        U.Tick(100,true);var body=Crate(stage+Vector3.up+Vector3.forward*2.7f);
        U.Select(Power("ice"));Aim(body.position);Check(U.Use(Power("ice")),"Real Ice freezes prop");
        yield return new WaitForFixedUpdate();Check(body.constraints==RigidbodyConstraints.FreezeAll,"Frozen prop constraints control");
        Check(U.Hero.TryPunch(),"Punch frozen prop accepted");yield return new WaitForSeconds(.35f);
        Check(body.constraints!=RigidbodyConstraints.FreezeAll&&body.linearVelocity.magnitude>1,$"Prop shatter restores physics: speed={body.linearVelocity.magnitude:F2}m/s");Destroy(body.gameObject);
    }
    IEnumerator NonMelee()
    {
        U.Tick(100,true);var npc=Npc(stage+Vector3.forward*2.7f);U.Select(Power("ice"));Aim(npc.transform.position+Vector3.up);
        Check(U.Use(Power("ice")),"Freeze before non-melee CONTROL");float hp=npc.Health;
        CombatImpact.Blast(U,stage+Vector3.up+Vector3.forward*1.7f,3,100,7,.2f);
        Check(npc.Frozen&&Mathf.Abs(hp-npc.Health-7)<.01f&&npc.GetComponent<Rigidbody>()==null,"Non-melee blast CONTROL: 7 damage, still frozen, no shatter");
        npc.Thaw();hp=npc.Health;U.Tick(100,true);yield return new WaitForSeconds(.7f);Check(U.Hero.TryPunch(),"Thawed target punch");yield return new WaitForSeconds(.3f);
        Check(Mathf.Abs(hp-npc.Health-U.Stats(U.Strength).Damage)<.01f,"Thawed CONTROL has no bonus");Destroy(npc.gameObject);
    }
    IEnumerator LethalShatter()
    {
        yield return new WaitForSeconds(.7f);U.Tick(100,true);
        var npc=Npc(stage+Vector3.forward*2.7f);npc.SetCombatStats(65,0);
        U.Select(Power("ice"));Aim(npc.transform.position+Vector3.up);Check(U.Use(Power("ice")),"Freeze normal 65HP enemy");
        Check(U.Hero.TryPunch(),"Lethal shatter punch");yield return new WaitForSeconds(.4f);
        var body=npc.GetComponent<Rigidbody>();
        Check(npc.Dead&&body!=null&&!body.isKinematic&&body.linearVelocity.magnitude>1,$"Lethal shatter CONTROL: dead body still launches at {body?.linearVelocity.magnitude:F2}m/s, uses existing death lifetime");
        Destroy(npc.gameObject);
    }
    IEnumerator Throw(bool first,float mass,bool lethal=false)
    {
        var tk=Power("telekinesis");U.Tick(100,true);U.Select(tk);
        var body=Crate(stage+new Vector3(1,1.5f,4),mass);body.useGravity=true;Aim(body.position,first);
        Check(U.Use(tk)&&U.HeldBody==body,$"Grab {mass}kg in {(first?"first":"third")} person");
        yield return new WaitForSeconds(.25f);
        var target=Npc(stage+new Vector3(2,0,18));var control=Npc(stage+new Vector3(6,0,18));
        if(lethal)target.SetCombatStats(65,0);
        Aim(target.transform.position+Vector3.up*.9f,first);Physics.SyncTransforms();
        Vector3 aim=U.AimPoint(U.Stats(tk).Range,body),start=body.position,velocity=body.linearVelocity;
        int charges=tk.Charges;float energy=U.Energy,hp=target.Health;tk.Cooldown=1;tk.Charges=0;
        Check(U.Use(tk)&&U.HeldBody==null,"Second click throws despite zero charges/cooldown: already paid grab");
        Check(tk.Charges==0&&U.Energy==energy,"Release costs no extra charges or energy");tk.Charges=charges;
        var impact=body.GetComponent<ThrownProp>();float until=Time.time+2;
        yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
        float launchImpulse=(body.linearVelocity-velocity).magnitude*mass;
        while(!impact.Spent&&Time.time<until)yield return null;
        float miss=Vector3.Distance(aim,impact.LastContact);
        Check(impact.Spent,$"Throw contacts aimed NPC: {mass}kg, first={first}");
        Check(miss<.5f,$"Throw MISS={miss:F4}m, contact={impact.LastContact:F3}, crosshair={aim:F3}, AddForce impulse={impact.LaunchImpulse.magnitude:F1}N.s, measured velocity delta~{launchImpulse:F1}N.s, displacement={Vector3.Distance(start,body.position):F2}m");
        Check(hp-target.Health>0&&impact.LastImpulse>0&&control.Health==1000,$"Impact damage={hp-target.Health:F2}, transferred impulse={impact.LastImpulse:F1}N.s; off-axis CONTROL 0 damage");
        yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
        Check(target.GetComponent<Rigidbody>().linearVelocity.magnitude>0,$"NPC launch speed={target.GetComponent<Rigidbody>().linearVelocity.magnitude:F3}m/s");
        if(lethal)Check(target.Dead&&!target.GetComponent<Rigidbody>().isKinematic,"Lethal throw preserves physical launch through death animation");
        float after=target.Health;yield return new WaitForSeconds(.1f);Check(target.Health==after,"Collision pays damage once");
        Destroy(body.gameObject);Destroy(target.gameObject);Destroy(control.gameObject);yield return null;
    }
    IEnumerator ThrowWall()
    {
        var tk=Power("telekinesis");U.Tick(100,true);U.Select(tk);
        var body=Crate(stage+new Vector3(0,1.5f,3));Aim(body.position);Check(U.Use(tk),"Wall CONTROL grab");
        var target=Npc(stage+new Vector3(0,0,12));
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=stage+new Vector3(0,1,7);wall.transform.localScale=new Vector3(6,5,.5f);
        var probe=body.gameObject.AddComponent<PayoffContactProbe>();Aim(target.transform.position+Vector3.up);Physics.SyncTransforms();
        Check(U.Use(tk),"Wall CONTROL throw");yield return new WaitForSeconds(1);
        Check(probe.Hit==wall.GetComponent<Collider>()&&target.Health==1000,"Real wall collision blocks throw; NPC behind wall unharmed");
        Destroy(body.gameObject);Destroy(target.gameObject);Destroy(wall);yield return null;
    }
    /// Orbit Throw (Flight + Telekinesis) was removed by the five-synergy cap. What this section protected, the Telekinesis LMB
    /// path while a synergy is equipped, is now checked as: Flight + Telekinesis resolves no synergy, C is refused with no
    /// cooldown, and LMB Telekinesis still performs its own paid grab (not a synergy release).
    IEnumerator Orbit()
    {
        var body=Crate(stage+new Vector3(0,1.5f,4));
        U.Select(Power("telekinesis"));Aim(body.worldCenterOfMass);
        var runner=U.SynergyRunner;
        Check(U.Synergy==null&&!runner.TryActivate()&&runner.Cooldown==0&&runner.Feedback=="No synergy for this pair","Flight + Telekinesis: no synergy in the capped set; C refused with no cooldown.");
        var tk=U.Powers.Find(p=>p.Definition.Id=="telekinesis");int charges=tk.Charges;
        Check(U.Use(tk)&&U.HeldBody==body&&tk.Charges==charges-1,"LMB Telekinesis performs its own paid grab.");
        yield return new WaitForSeconds(.3f);
        Check(U.Use(tk)&&U.HeldBody==null&&body.GetComponent<ThrownProp>()!=null,"Second LMB throws the held prop (no orbit release path).");
        Destroy(body.gameObject);yield return null;
    }
}
public sealed class PayoffContactProbe : MonoBehaviour
{
    public Collider Hit;
    void OnCollisionEnter(Collision c){if(Hit==null)Hit=c.collider;}
}
#endif
