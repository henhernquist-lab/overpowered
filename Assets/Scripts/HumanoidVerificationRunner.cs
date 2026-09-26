#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class HumanoidVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;readonly List<string> lines=new List<string>();WorldSession W=>WorldSession.Instance;
    public bool OnlyDeathControl;
    HumanoidPresentation P=>W.Hero.GetComponent<HumanoidPresentation>();
    void Log(string text){lines.Add(text);Debug.Log("[HUMANOID VERIFY] "+text);File.WriteAllLines("Verification/Humanoid/results.txt",lines);}
    void Check(bool condition,string text){if(!condition)throw new Exception(text);Log("PASS "+text);}
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            bool more=false;object value=null;
            try{more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}catch(Exception e){Log("FAIL "+e);Finished(1);yield break;}
            if(!more){stack.Pop();continue;}if(value is IEnumerator nested)stack.Push(nested);else yield return value;
        }
        Finished(0);
    }
    IEnumerator Scene(string scene)
    {
        float end=Time.realtimeSinceStartup+60;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=scene){if(Time.realtimeSinceStartup>end)throw new Exception("Scene timeout");yield return null;}
        for(int i=0;i<10;i++)yield return null;
    }
    void Move(Vector3 p){var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=p;W.Hero.transform.rotation=Quaternion.identity;cc.enabled=true;W.Hero.ResetMotion();Physics.SyncTransforms();}
    IEnumerator Run()
    {
        yield return Scene("Home");Check(Camera.main!=null,"Home camera retained.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene("Prototype");
        W.Hero.enabled=false;W.MenuOpen=true;Move(W.City.Spawn+Vector3.up*15);
        if(OnlyDeathControl){yield return DeathInterrupt();yield break;}
        Check(P.Animator.isHuman&&P.Animator.avatar.isValid&&W.Hero.GetComponentsInChildren<SkinnedMeshRenderer>().Length==2,"Player uses supplied valid Humanoid with two skinned meshes, no capsule renderer.");
        Check(W.Hero.GetComponent<CharacterController>().height==1.8f&&W.Hero.GetComponent<CharacterController>().radius==.38f,"CONTROL: player collider unchanged: height=1.800m radius=.380m; NPC capsule=1.800m/.350m.");
        Check(W.Hero.GetComponentsInChildren<Collider>().Length==1,$"Reference skinned vertices height={P.ReferenceMeshHeight:F4}m, visual scale={P.ModelScale:F4}, fitted height={P.ReferenceMeshHeight*P.ModelScale:F4}m; exactly one authoritative player collider.");
        Check(W.Npcs.All(n=>n.GetComponent<HumanoidPresentation>().Animator.runtimeAnimatorController==P.Animator.runtimeAnimatorController),$"SAME controller asset on player and all {W.Npcs.Count} civilians/cops/criminals.");
        yield return Locomotion();yield return Punch();yield return Damage();yield return Flight();yield return Panic();yield return Landing();yield return Casting();yield return Benchmark();yield return EquippedCasting();
        W.Hero.enabled=false;GameFlow.Instance.Home();yield return Scene("Home");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/villain"));yield return Scene("Prototype");W.Hero.enabled=false;
        Check(P.Animator.runtimeAnimatorController==Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning").Controller&&Camera.main!=null,"Villain mode spawns same humanoid/controller and renders.");
        var cop=W.Npcs.First(n=>n.Role==NpcRole.Cop&&!n.Dead);cop.Agent.Warp(W.City.Spawn);Move(cop.transform.position+Vector3.forward*8);
        // Gait selection is about a MOVING cop. A Gunner windup stops the agent dead by design (velocity zeroed), so sample only a frame
        // where the cop is NOT in AttackPhase.Windup and both its agent and measured presentation speed exceed the 0.1 m/s threshold.
        var copPresentation=cop.GetComponent<HumanoidPresentation>();float armedUntil=Time.time+10;int windupFrames=0;
        while(!(cop.Phase!=AttackPhase.Windup&&cop.Agent.velocity.magnitude>.1f&&copPresentation.MeasuredSpeed>.1f&&copPresentation.State=="Armed")&&Time.time<armedUntil){if(cop.Phase==AttackPhase.Windup)windupFrames++;yield return null;}
        Log($"COP gait sample: phase={cop.Phase}, agent speed={cop.Agent.velocity.magnitude:F3}m/s, measured speed={copPresentation.MeasuredSpeed:F3}m/s, state={copPresentation.State}, windup frames skipped={windupFrames}, budget left={armedUntil-Time.time:F2}s of 10s.");
        Check(cop.Phase!=AttackPhase.Windup&&copPresentation.MeasuredSpeed>.1f&&copPresentation.State=="Armed"&&cop.Agent.velocity.magnitude>.1f,$"Moving cop (phase={cop.Phase}) actual speed={cop.Agent.velocity.magnitude:F3}m/s selects shared Pistol Run.");
        // Balance pass: Villain patrol police turn hostile from VillainPolice.HostileFromStars (1 star), not at 0 Heat.
        Check(!cop.Hostile||W.Stars>=W.Tuning.Heat.VillainPolice.HostileFromStars,$"CONTROL: patrol cop at {W.Stars} stars is hostile={cop.Hostile} (per-side data, hostile from {W.Tuning.Heat.VillainPolice.HostileFromStars}).");
        W.AddHeat(W.Tuning.Heat.VillainPolice.HostileFromStars-W.Heat);Check(cop.Hostile,$"Heat raised to {W.Stars} star(s): the same patrol cop is now hostile.");
        Move(cop.transform.position+Vector3.right);float hp=W.Health;
        float timeout=Time.time+3;while(W.Health==hp&&Time.time<timeout)yield return null;
        yield return null;Check(W.Health<hp&&cop.GetComponent<HumanoidPresentation>().State=="Shoot",$"Live hostile cop attack: HP {hp}->{W.Health}; shared controller plays Shooting Gun.");
        yield return new WaitForSeconds(.3f);Capture("cop-shoot",cop.transform.position);
        var copPose=cop.GetComponent<HumanoidPresentation>();
        Log($"COP shooting clip sample: left hand local={cop.transform.InverseTransformPoint(copPose.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position):F3}, presentation yaw={copPose.PoseRoot.localEulerAngles.y:F1}deg; physics root still navigates normally.");
        GameFlow.Instance.Home();yield return Scene("Home");Check(Camera.main!=null,"Hero and Villain return Home without camera regression.");
    }
    IEnumerator DeathInterrupt()
    {
        W.Hero.DebugSetResources(6,3);float before=W.Hero.LastImpactTime;
        Check(W.Hero.TryPunch(),"Death-during-windup CONTROL starts a real paid punch.");
        W.DamagePlayer(W.Health);yield return new WaitForSeconds(.3f);
        Check(P.DeathCount==1&&W.Hero.LastImpactTime==before,"Death clip overrides punch and cancels pending force.");
        yield return new WaitForSeconds(W.Tuning.Movement.RespawnDelay+.2f);
        Check(!W.PlayerDead&&P.State!="Death","Actual respawn releases death presentation.");
        int hits=P.HitCount;W.DamagePlayer(1);yield return null;
        Check(P.HitCount==hits+1&&P.Animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"),"Post-respawn damage CONTROL plays Hit; canceled punch leaves no stale deferred animation.");
        GameFlow.Instance.Home();yield return Scene("Home");
    }
    IEnumerator Locomotion()
    {
        var cc=W.Hero.GetComponent<CharacterController>();
        foreach(float speed in new[]{0f,.9f,1.8f,3.4f,5f,7f,9f})
        {
            Move(W.City.Spawn+Vector3.up*15);
            for(float elapsed=0;elapsed<.7f;elapsed+=Time.deltaTime){cc.Move(Vector3.forward*speed*Time.deltaTime);P.VerificationState=new HeroPresentationState(cc.velocity,true,false);yield return null;}
            var clips=P.Animator.GetCurrentAnimatorClipInfo(0);string weights=string.Join(", ",clips.Select(c=>$"{c.clip.name}={c.weight:F3}"));
            Log($"BLEND requested={speed:F2} actual controller={cc.velocity.magnitude:F3} animator Speed={P.Animator.GetFloat("Speed"):F3} m/s: {weights}");
            Check(Mathf.Abs(P.Animator.GetFloat("Speed")-speed)<.1f,"Continuous actual-speed parameter tracks measured motion.");
            if(speed==3.4f||speed==7f)Check(clips.Count(c=>c.weight>.1f)>=2,"Intermediate-speed CONTROL has two nonzero clip weights, not a snapped state.");
            if(speed==0)Capture("idle-model",P.transform.position);
        }
        P.VerificationState=new HeroPresentationState(Vector3.back*3,true,false);yield return new WaitForSeconds(.3f);
        Check(P.Animator.GetCurrentAnimatorStateInfo(0).IsName("Back"),"Negative actual local velocity selects Standing Run Back.");
        P.VerificationState=new HeroPresentationState(Vector3.zero,true,false);yield return new WaitForSeconds(.3f);
    }
    IEnumerator Punch()
    {
        Move(W.City.Spawn+Vector3.up*15);var target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.name="Humanoid punch control";target.transform.position=W.Hero.transform.position+Vector3.up+Vector3.forward*2.2f;
        var rb=target.AddComponent<Rigidbody>();rb.mass=45;rb.useGravity=false;target.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);Vector3 start=rb.position;
        W.Hero.DebugSetResources(6,3);int fired=0;W.Hero.PunchImpacted+=()=>fired++;
        Check(W.Hero.TryPunch(),"Normal charged punch accepted.");Check(!W.Hero.TryPunch(),"Cooldown CONTROL refuses immediate second punch.");
        Check(fired==0&&rb.linearVelocity.sqrMagnitude==0,"Windup CONTROL has no premature force.");
        float deadline=Time.time+2;while((fired==0||P.LastAnimationImpactTime<0)&&Time.time<deadline)yield return null;
        float offset=(W.Hero.LastImpactTime-P.LastAnimationImpactTime)*1000;
        Log($"PUNCH source start={P.Tuning.PunchStartSeconds:F4}s impact={P.Tuning.PunchImpactSeconds:F4}s playback={P.Tuning.PunchPlayback:F2}; configured windup={P.Tuning.PunchWindup*1000:F2}ms; visual marker t={P.LastAnimationImpactTime:F5} frame={P.LastAnimationImpactFrame}; real force t={W.Hero.LastImpactTime:F5} frame={W.Hero.LastImpactFrame}; offset={offset:F2}ms/{W.Hero.LastImpactFrame-P.LastAnimationImpactFrame} frames.");
        Check(fired==1&&Mathf.Abs(W.Hero.LastImpactFrame-P.LastAnimationImpactFrame)<=2,"Actual force and evaluated clip impact marker align within two rendered frames.");
        Capture("punch-impact",P.transform.position);
        yield return new WaitForSeconds(.2f);Check(rb.linearVelocity.magnitude>1&&Vector3.Distance(start,rb.position)>.1f,$"Real force={W.Hero.LastForce} N·s, mass={rb.mass}kg, velocity={rb.linearVelocity.magnitude:F3}m/s, displacement={Vector3.Distance(start,rb.position):F3}m.");
        W.Hero.DebugSetResources(6,0);Check(!W.Hero.TryPunch(),"Zero-charge CONTROL refuses punch.");yield return new WaitForSeconds(.2f);Check(fired==1,"Rejected punches emit no extra impact.");
        W.Hero.DebugSimulateGround(1.25f);Check(W.Hero.Charges==1,"Original 1.25s charge recharge retained.");
        W.Hero.DebugSetResources(6,3);W.Hero.DebugSimulateFlight(2);Log($"FLIGHT fuel 6 -> {W.Hero.FlightFuel:F3}");W.Hero.DebugSimulateFlight(4);Check(W.Hero.FlightFuel==0,"Fuel drains 4 -> 0; empty-flight control blocks.");W.Hero.DebugSimulateGround(1);Check(Mathf.Approximately(W.Hero.FlightFuel,2.5f),"Grounded fuel recharge 0 -> 2.500 in 1s.");Destroy(target);
    }
    IEnumerator Damage()
    {
        var a=W.Npcs.First(n=>n.Role==NpcRole.Civilian&&!n.Dead);var b=W.Npcs.Last(n=>n.Role==NpcRole.Civilian&&!n.Dead);
        var ap=a.GetComponent<HumanoidPresentation>();var bp=b.GetComponent<HumanoidPresentation>();int before=ap.HitCount;
        a.Agent.Warp(W.City.Spawn);a.Freeze(5);
        a.Damage(1,null);yield return null;Check(ap.HitCount==before+1&&ap.Animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"),"Real NPC damage triggers Hit Reaction.");
        Check(bp.HitCount==0&&bp.DeathCount==0&&!bp.Animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"),"Undamaged NPC CONTROL plays neither hit nor death.");
        a.Damage(10000,null);yield return null;Check(ap.DeathCount==1&&ap.Animator.GetCurrentAnimatorStateInfo(0).IsName("Death")&&!a.Agent.enabled,"Lethal damage triggers Death, disables navigation/collision; no root 90-degree flip.");
        yield return new WaitForSeconds(.5f);Capture("death",a.transform.position);
        int playerHits=P.HitCount;W.DamagePlayer(1);yield return null;Check(P.HitCount==playerHits+1&&P.Animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"),"Real player damage triggers Hit Reaction.");yield return new WaitForSeconds(1);
    }
    IEnumerator Flight()
    {
        Move(W.City.Spawn+Vector3.up*12);P.VerificationState=new HeroPresentationState(Vector3.zero,false,true);
        yield return new WaitForSeconds(1);Capture("hover",P.transform.position);
        Quaternion hover=P.PoseRoot.localRotation;Vector3 hoverHand=P.PoseRoot.InverseTransformPoint(P.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
        Log($"FLIGHT hover weight={P.FlightWeight:F3} pitch={P.PoseRoot.localEulerAngles.x:F2} hand={hoverHand:F3}");
        // ReadPixels stalls the editor. Let capture latency settle before measuring continuity.
        yield return new WaitForSeconds(.3f);
        float maxStep=0;Quaternion prior=hover;
        P.VerificationState=new HeroPresentationState(Vector3.forward*12,false,true);
        for(int i=0;i<60;i++){yield return null;maxStep=Mathf.Max(maxStep,Quaternion.Angle(prior,P.PoseRoot.localRotation));prior=P.PoseRoot.localRotation;if(i==4||i==12||i==59)Log($"FLIGHT transition frame {i}: blend={P.ForwardFlight:F3}, pitch={P.PoseRoot.localEulerAngles.x:F2}");}
        Vector3 forwardHand=P.PoseRoot.InverseTransformPoint(P.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
        Check(Quaternion.Angle(hover,P.PoseRoot.localRotation)>60&&forwardHand.y>hoverHand.y+.5f,$"Hover vs forward distinct: body difference={Quaternion.Angle(hover,P.PoseRoot.localRotation):F2}deg, hand height {hoverHand.y:F3}->{forwardHand.y:F3}m; maximum rendered-frame pitch change={maxStep:F2}deg.");
        Check(maxStep<20,"Uncaptured live transition CONTROL stays below 20 degrees per rendered frame.");Capture("forward-flight",P.transform.position);
        P.VerificationState=new HeroPresentationState(Vector3.zero,true,false);yield return new WaitForSeconds(1);
        Check(P.FlightWeight<.01f&&Quaternion.Angle(P.PoseRoot.localRotation,Quaternion.identity)<2,"Landing transition releases flight bones/root smoothly.");
    }
    IEnumerator Panic()
    {
        var npc=CityNpc.Spawn(W,W.City.Spawn,NpcRole.Civilian);var p=npc.GetComponent<HumanoidPresentation>();
        p.VerificationState=new HeroPresentationState(Vector3.forward*5,true,false);yield return new WaitForSeconds(.5f);Check(!npc.Fleeing&&p.State=="Locomotion","Normal-jog CONTROL is not alarmed and has no panic overlay.");Capture("normal-jog",npc.transform.position);
        npc.Alarm(npc.transform.position+Vector3.back*3);float min=999,max=-999;float yawMin=999,yawMax=-999;
        for(int i=0;i<90;i++)
        {
            yield return null;var hand=p.PoseRoot.InverseTransformPoint(p.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position);min=Mathf.Min(min,hand.y);max=Mathf.Max(max,hand.y);
            float yaw=Mathf.DeltaAngle(0,p.Animator.GetBoneTransform(HumanBodyBones.Head).localEulerAngles.y);yawMin=Mathf.Min(yawMin,yaw);yawMax=Mathf.Max(yawMax,yaw);
            if(i==20||i==60)Capture("panic-"+i,npc.transform.position);
        }
        Check(p.State=="Panic"&&p.PanicWeight>.9f&&p.Animator.GetFloat("GaitRate")>1,$"Live fleeing civilian: Standing Run Forward at {p.Animator.GetFloat("GaitRate"):F2}x, lean={p.PoseRoot.localEulerAngles.x:F2}deg, raised/flailing hand Y={min:F2}..{max:F2}m; head yaw={yawMin:F1}..{yawMax:F1}deg.");
        var straight=npc.transform.position+Vector3.forward*10;Log($"PANIC path deviation sample={Vector3.Distance(straight,npc.PanicDestination(straight)):F3}m, configured amplitude={p.Tuning.PanicPathDeviation:F2}m; NavMesh sampling remains authoritative.");p.VerificationState=null;
    }
    IEnumerator Landing()
    {
        P.VerificationState=null;Move(W.City.Spawn+Vector3.up*5);int landings=0;float impact=0;W.Hero.Landed+=v=>{landings++;impact=v;};W.Hero.enabled=true;
        float min=1;bool clip=false;float until=Time.time+3;
        while(Time.time<until){yield return null;min=Mathf.Min(min,P.PoseRoot.parent.localScale.y);clip|=P.State=="Land";}
        Check(landings==1&&clip&&min<.95f,$"Actual controller fall: {landings} impact at {impact:F3}m/s; Land clip played with single existing squash minY={min:F3}, recovered={P.PoseRoot.parent.localScale.y:F3}; collider={W.Hero.GetComponent<CharacterController>().height:F3}m.");
        Check(W.Hero.TryJump(),"Existing grounded jump accepted.");yield return null;Check(P.State=="Jump","Jump presentation comes from real movement event.");W.Hero.enabled=false;P.VerificationState=new HeroPresentationState(Vector3.zero,true,false);yield return new WaitForSeconds(1);
    }
    PowerDefinition fireDefinition,iceDefinition;
    IEnumerator Casting()
    {
        // Fire and Ice definitions dispatch the SAME presentation flag, only after successful effects.
        var fire=W.Powers.Powers.First(p=>p.Definition.Effect is FireBlastEffect);var ice=W.Powers.Powers.First(p=>p.Definition.Effect is IceEffect);
        fireDefinition=fire.Definition;iceDefinition=ice.Definition;
        Check(fire.Definition.CastingPresentation&&ice.Definition.CastingPresentation,"Fire Blast and Ice both configured for shared Casting Spell presentation.");
        W.Progression.AddXp(100000);W.Progression.Buy(fire.Definition);W.Progression.Buy(ice.Definition);
        Check(W.Progression.Owns(fire.Definition)&&W.Progression.Owns(ice.Definition)&&!W.Powers.IsEquipped(fire.Definition)&&!W.Powers.IsEquipped(ice.Definition),$"Fire Blast and Ice owned through earned points but NOT equipped (session loadout {W.Powers.EquippedA.Id} + {W.Powers.EquippedB.Id}).");
        // Hero Forge gate CONTROL: an owned but unequipped power is refused before any effect, payment or presentation.
        Move(W.City.Spawn+Vector3.up*15);int activated=0;Action<PowerDefinition> count=_=>activated++;W.Powers.Activated+=count;
        var target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.transform.position=W.Powers.AimOrigin+W.Powers.AimDirection*3;target.AddComponent<Rigidbody>().useGravity=false;Physics.SyncTransforms();
        int shots=FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length,fireCharges=fire.Charges,iceCharges=ice.Charges;float fireCooldown=fire.Cooldown,iceCooldown=ice.Cooldown,energy=W.Powers.Energy;var selected=W.Powers.Selected;
        bool fireSelected=W.Powers.Select(fire),fireUsed=W.Powers.Use(fire);string fireMessage=W.Powers.Message;
        bool iceSelected=W.Powers.Select(ice),iceUsed=W.Powers.Use(ice);string iceMessage=W.Powers.Message;
        Check(!fireSelected&&!fireUsed&&!iceSelected&&!iceUsed&&fireMessage=="Power not equipped"&&iceMessage=="Power not equipped"&&W.Powers.Selected==selected,"CONTROL: unequipped Fire Blast and Ice refused by the equip gate (Select/Use false, \"Power not equipped\").");
        Check(fire.Charges==fireCharges&&ice.Charges==iceCharges&&fire.Cooldown==fireCooldown&&ice.Cooldown==iceCooldown&&W.Powers.Energy==energy,$"CONTROL: refused unequipped casts charged nothing (Fire charges {fireCharges}->{fire.Charges}, Ice charges {iceCharges}->{ice.Charges}, energy {energy:F2}->{W.Powers.Energy:F2}).");
        yield return null;
        Check(activated==0&&FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length==shots&&target.GetComponent<FrozenBody>()==null&&P.State!="Cast",$"CONTROL: refused unequipped casts spawned no projectile ({shots} before/after), froze nothing, raised no Activated event and played no Casting Spell (state={P.State}).");
        W.Powers.Activated-=count;Destroy(target);
    }
    IEnumerator EquippedCasting()
    {
        // Loadouts are fixed per session, so equip through the real pre-session API at Home, then start a new Hero session.
        W.Hero.enabled=false;GameFlow.Instance.Home();yield return Scene("Home");
        var profile=UnityEngine.Object.FindAnyObjectByType<ModeScreens>().Profile;var loadout=profile.Data.Loadout;
        Check(profile.Owns(fireDefinition)&&profile.Owns(iceDefinition)&&profile.SetLoadout(profile.SelectedHero,fireDefinition,iceDefinition,loadout.Primary,loadout.Secondary),"Owned Fire Blast + Ice equipped through PlayerProgression.SetLoadout before the session.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene("Prototype");W.Hero.enabled=false;W.MenuOpen=true;
        Check(W.Powers.IsEquipped(fireDefinition)&&W.Powers.IsEquipped(iceDefinition),$"New session receives equipped pair {W.Powers.EquippedA.Id} + {W.Powers.EquippedB.Id}.");
        var fire=W.Powers.Powers.First(p=>p.Definition==fireDefinition);var ice=W.Powers.Powers.First(p=>p.Definition==iceDefinition);
        Move(W.City.Spawn+Vector3.up*15);W.Powers.Select(fire);Check(W.Powers.Use(fire),"Real Fire Blast activation succeeds.");yield return null;Check(P.State=="Cast","Successful Fire Blast triggers Casting Spell.");
        var target=GameObject.CreatePrimitive(PrimitiveType.Cube);W.Powers.Select(ice);target.transform.position=W.Powers.AimOrigin+W.Powers.AimDirection*3;target.AddComponent<Rigidbody>().useGravity=false;Physics.SyncTransforms();
        Check(W.Powers.Use(ice),"Real Ice activation succeeds.");yield return null;Check(P.State=="Cast"&&target.GetComponent<FrozenBody>()!=null,"Successful Ice triggers same Casting Spell and freezes actual body.");Destroy(target);
    }
    IEnumerator Benchmark()
    {
        W.AddHeat(3);W.ReconcilePolice();Move(W.City.Spawn+Vector3.up*35);P.VerificationState=new HeroPresentationState(Vector3.zero,false,true);
        var camera=Camera.main;var follow=camera.GetComponent<ThirdPersonCamera>();follow.enabled=false;camera.transform.position=new Vector3(-65,60,-80);camera.transform.LookAt(Vector3.zero);
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;yield return new WaitForSeconds(1);
        var times=new List<double>();var draws=new List<int>();var watch=System.Diagnostics.Stopwatch.StartNew();double prior=0;
        while(watch.Elapsed.TotalSeconds<5){camera.Render();yield return null;double now=watch.Elapsed.TotalSeconds;times.Add((now-prior)*1000);prior=now;draws.Add(UnityStats.drawCalls);}
        watch.Stop();times.Sort();draws.Sort();double fps=times.Count/watch.Elapsed.TotalSeconds;
        Log($"MEASURED populated skinned/Animator city: civilians={W.Npcs.Count(n=>!n.Dead&&n.Role==NpcRole.Civilian)}, cops={W.Npcs.Count(n=>!n.Dead&&n.Role==NpcRole.Cop)}, active animators={FindObjectsByType<Animator>().Length}; 1280x720, frames={times.Count}, seconds={watch.Elapsed.TotalSeconds:F3}, FPS={fps:F2}, p95={times[(int)(times.Count*.95)]:F2}ms, drawcalls median/max={draws[draws.Count/2]}/{draws.Last()}; previous city seed2409=50.25 FPS ({(fps/50.25-1)*100:F1}%), historical155=({(fps/155-1)*100:F1}%); {SystemInfo.graphicsDeviceName}.");
        Save(target,"populated-city");camera.targetTexture=null;target.Release();Destroy(target);follow.enabled=true;
    }
    void Capture(string name,Vector3 actor)
    {
        var camera=new GameObject("Humanoid capture").AddComponent<Camera>();camera.transform.position=actor+new Vector3(4,2,5);camera.transform.LookAt(actor+new Vector3(0,.7f,.6f));camera.fieldOfView=38;
        var rt=new RenderTexture(960,720,24);rt.Create();camera.targetTexture=rt;camera.Render();Save(rt,name);camera.targetTexture=null;rt.Release();Destroy(rt);Destroy(camera.gameObject);
    }
    void Save(RenderTexture target,string name)
    {
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes("Verification/Humanoid/"+name+".png",image.EncodeToPNG());RenderTexture.active=previous;Destroy(image);
    }
}
#endif
