#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// Verification for the two additive gestures: Q backflip and right-mouse Hurricane Kick.
/// Every assertion drives the real production paths (TryBackflip / TryHurricaneKick -> the
/// existing Strength charge pool and CombatImpact.Blast) and pairs each with a control.
public sealed class BackflipHurricaneVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;readonly List<string> lines=new List<string>();WorldSession W=>WorldSession.Instance;
    HumanoidPresentation P=>W.Hero.GetComponent<HumanoidPresentation>();
    void Log(string text){lines.Add(text);Debug.Log("[ABILITIES VERIFY] "+text);File.WriteAllLines("Verification/Abilities/results.txt",lines);}
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
    void MoveTo(Vector3 p,Quaternion rotation){var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=p;W.Hero.transform.rotation=rotation;cc.enabled=true;W.Hero.ResetMotion();Physics.SyncTransforms();}
    void Move(Vector3 p)=>MoveTo(p,Quaternion.identity);
    IEnumerator Settle(){var cc=W.Hero.GetComponent<CharacterController>();for(int i=0;i<120&&!cc.isGrounded;i++)yield return null;yield return new WaitForSeconds(.3f);Check(cc.isGrounded,"CONTROL: hero reached real ground contact.");}
    IEnumerator WaitFor(Func<bool> condition,float seconds,string label){float deadline=Time.time+seconds;while(!condition()&&Time.time<deadline)yield return null;Check(condition(),$"CONTROL: {label} resolved within {seconds:F1}s.");}
    bool Blocked(Vector3 origin,Vector3 target)
    {
        // Mirrors CombatImpact.Blast: only static non-NPC geometry shields a target; physics props do not.
        Vector3 delta=target-origin;
        foreach(var ray in Physics.RaycastAll(origin,delta.normalized,delta.magnitude))
            if(ray.rigidbody==null&&ray.collider.GetComponentInParent<CityNpc>()==null&&ray.collider.GetComponentInParent<PowerUser>()==null&&!ray.collider.isTrigger)return true;
        return false;
    }
    bool TryAimAt(Transform target,out Vector3 position,out Quaternion rotation)
    {
        foreach(var dir in new[]{Vector3.forward,Vector3.right,Vector3.back,Vector3.left})
        {
            var candidate=target.position-dir*3f;
            if(!NavMesh.SamplePosition(candidate,out var hit,2f,NavMesh.AllAreas))continue;
            Vector3 origin=hit.position+Vector3.up*1f+dir*W.Powers.Strength.Definition.OriginOffset;
            if(Blocked(origin,target.position+Vector3.up*.9f))continue;
            position=hit.position+Vector3.up*.2f;rotation=Quaternion.LookRotation(dir);return true;
        }
        position=Vector3.zero;rotation=Quaternion.identity;return false;
    }
    Rigidbody Target(float forward)
    {
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name=$"Ability target {forward:0.0}m";
        cube.transform.localScale=Vector3.one*.5f;
        cube.transform.position=W.Hero.transform.position+Vector3.up+W.Hero.transform.forward*forward;
        cube.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);
        var body=cube.AddComponent<Rigidbody>();body.mass=45;body.useGravity=false;return body;
    }
    void Place(Rigidbody body,float forward){
        body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
        body.position=W.Hero.transform.position+Vector3.up+W.Hero.transform.forward*forward;
        body.rotation=Quaternion.identity;Physics.SyncTransforms();
    }
    IEnumerator Run()
    {
        yield return Scene("Home");Check(Camera.main!=null,"CONTROL: Home camera retained.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene("Prototype");
        W.MenuOpen=true; // Verification drives the abilities directly; the hero keeps its normal physics.
        Check(P.Animator!=null&&P.Animator.runtimeAnimatorController!=null,"CONTROL: shared humanoid Animator is live before the gestures.");
        Log($"CONFIG backflip: cooldown={HeroAbilityTuning.BackflipCooldown:F2}s dash={HeroAbilityTuning.BackflipSeconds:F2}s distance={HeroAbilityTuning.BackflipDistance:F1}m hop={HeroAbilityTuning.BackflipHopSpeed:F2}m/s; clip start={P.Tuning.BackflipStartSeconds:F3}s length={P.Tuning.Backflip.length:F4}s fitted playback={((P.Tuning.Backflip.length-P.Tuning.BackflipStartSeconds)/HeroAbilityTuning.BackflipSeconds):F2}x");
        Log($"CONFIG hurricane kick: start={P.Tuning.KickStartSeconds:F3}s impact={P.Tuning.KickImpactSeconds:F3}s playback={P.Tuning.KickPlayback:F2}x -> configured windup={P.Tuning.KickWindup*1000:F1}ms; force x{HeroAbilityTuning.KickForceMultiplier:F2} damage x{HeroAbilityTuning.KickDamageMultiplier:F2} radius={HeroAbilityTuning.KickRadius:F2}m vs punch radius {W.Powers.Stats(W.Powers.Strength).Radius:F2}m");
        yield return Backflip();
        yield return ChargePool();
        yield return Melee();
        yield return NpcDamage();
        yield return Regressions();
        W.MenuOpen=false;
        GameFlow.Instance.Home();yield return Scene("Home");
        Check(Camera.main!=null,"Hero mode returns Home without camera regression.");
    }
    IEnumerator Backflip()
    {
        Move(W.City.Spawn+Vector3.up*2);yield return Settle();
        Check(W.Hero.LastBackflipResult=="Ready"&&W.Hero.BackflipCount==0,"CONTROL: backflip surface starts Ready with zero uses.");
        int events=0;System.Action count=()=>events++;W.Hero.BackflipStarted+=count;
        Quaternion facing=W.Hero.transform.rotation;Vector3 start=W.Hero.transform.position;float peak=start.y;
        Check(W.Hero.TryBackflip(),"Backflip accepted from the ground.");
        yield return null;
        Check(P.Animator.GetCurrentAnimatorStateInfo(0).IsName("Backflip"),"Backflip dispatches the shared humanoid's Backflip state (previously named Unwired Backflip).");
        Check(P.State=="Backflip"&&events==1,"Presentation reports the backflip as the active action from one real event.");
        Check(W.Hero.BackflipActive&&W.Hero.BackflipCount==1,"Accepted backflip opens its dash window and counts one use.");
        float until=Time.time+HeroAbilityTuning.BackflipSeconds+.15f;
        while(Time.time<until){peak=Mathf.Max(peak,W.Hero.transform.position.y);yield return null;}
        Vector3 travel=W.Hero.transform.position-start;Vector3 flat=new Vector3(travel.x,0,travel.z);
        float backward=Vector3.Dot(flat,W.Hero.transform.forward);
        Check(backward<-HeroAbilityTuning.BackflipDistance*.5f,$"Backflip repositioned the hero {backward:F2}m backward against a configured {HeroAbilityTuning.BackflipDistance:F1}m.");
        Check(peak-start.y>.2f,$"Backflip hop actually left the ground: peak rise {peak-start.y:F3}m at {HeroAbilityTuning.BackflipHopSpeed:F2}m/s launch, brought back down by the existing gravity.");
        Check(Quaternion.Angle(facing,W.Hero.transform.rotation)<1f,"Backflip does not rotate the physics root or turn the hero.");
        Check(!W.Hero.BackflipActive,"Backflip dash window closes when its configured window ends.");
        Check(!W.Hero.TryBackflip(),"Cooldown CONTROL refuses an immediate second backflip.");
        Check(W.Hero.LastBackflipResult=="Blocked: cooldown"&&W.Hero.BackflipCooldown>0f,$"Refusal is the backflip cooldown ('{W.Hero.LastBackflipResult}') with {W.Hero.BackflipCooldown:F2}s of {HeroAbilityTuning.BackflipCooldown:F2}s still remaining.");
        Check(W.Hero.BackflipCount==1&&events==1,"Refused backflip neither counted a use nor emitted a presentation event.");
        Move(W.City.Spawn+Vector3.up*8);
        yield return null;yield return null; // Let the controller recompute ground contact after the teleport.
        Check(!W.Hero.GetComponent<CharacterController>().isGrounded,"Airborne CONTROL is genuinely off the ground.");
        Check(!W.Hero.TryBackflip(),$"Airborne CONTROL refuses the grounded-only backflip ('{W.Hero.LastBackflipResult}').");
        Move(W.City.Spawn+Vector3.up*2);yield return Settle();
        float remaining=W.Hero.BackflipCooldown;yield return new WaitForSeconds(remaining+.15f);
        Check(W.Hero.BackflipCooldown==0f,"CONTROL: backflip cooldown counts down to exactly zero.");
        Check(W.Hero.TryBackflip()&&W.Hero.BackflipCount==2&&events==2,"Recharged backflip fires again (use 2), so the cooldown is a real gate rather than a permanent block.");
        yield return new WaitForSeconds(HeroAbilityTuning.BackflipSeconds+.45f);
        Check(P.State=="Locomotion","Backflip returns the presentation to locomotion once the dash ends.");
        Check(W.Hero.TryJump(),"CONTROL: existing grounded jump still accepted after the backflip.");
        yield return null;Check(P.State=="Jump","CONTROL: jump still dispatches its existing presentation event.");
        yield return new WaitForSeconds(1.6f);
        Check(P.State=="Locomotion"||P.State=="Air","CONTROL: movement presentation still recovers after a jump.");
        W.Hero.BackflipStarted-=count;
    }
    IEnumerator ChargePool()
    {
        Move(W.City.Spawn+Vector3.up*15);yield return new WaitForSeconds(.3f);
        W.Hero.DebugSetResources(6,3);
        Check(W.Hero.Charges==3,"CONTROL: three Super Strength charges available.");
        Check(W.Hero.TryPunch(),"CONTROL: standard punch still accepted.");
        Check(W.Hero.Charges==2,"Punch spends one charge from the shared pool (3 -> 2).");
        Check(!W.Hero.TryHurricaneKick(),"Cooldown CONTROL refuses the kick immediately after the punch.");
        Check(W.Hero.LastKickResult=="Blocked: cooldown",$"Kick refusal is the shared Super Strength cooldown ('{W.Hero.LastKickResult}'), not a separate kick resource.");
        W.Hero.DebugSetResources(6,2,0f);
        Check(W.Hero.TryHurricaneKick(),"Hurricane Kick accepted once the shared cooldown clears.");
        Check(W.Hero.Charges==1,"Kick spends from the same charge pool (2 -> 1).");
        W.Hero.DebugSetResources(6,0);
        Check(!W.Hero.TryHurricaneKick(),"Zero-charge CONTROL refuses the kick.");
        Check(W.Hero.LastKickResult=="Blocked: 0 charges",$"Kick zero-charge refusal reuses the existing message ('{W.Hero.LastKickResult}').");
        W.Hero.DebugSimulateGround(1.25f);
        Check(W.Hero.Charges==1,"Existing 1.25 s charge recharge still restores the pool after kicks (0 -> 1).");
        yield return new WaitForSeconds(P.Tuning.KickWindup+.4f);
    }
    IEnumerator Melee()
    {
        // Clean-air geometry: 200 m up there is no city geometry or loose prop to confound the two
        // blasts, and the hero's free fall during the comparison stays clear of every building.
        float punchReach=W.Powers.Strength.Definition.OriginOffset+W.Powers.Stats(W.Powers.Strength).Radius;
        float kickReach=HeroAbilityTuning.KickOriginOffset+HeroAbilityTuning.KickRadius;
        var near=Target(2.8f);var far=Target(5.6f); // far sits beyond the punch's reach and inside the kick's
        Log($"REACH punch={punchReach:F2}m kick={kickReach:F2}m; targets at 2.8m (inside both) and 5.6m (outside the punch, inside the kick)");
        // Both measurements start from the same freshly reset airborne position, so the only
        // difference between them is the gesture's own windup.
        Move(W.City.Spawn+Vector3.up*200);Place(near,2.8f);Place(far,5.6f);yield return null;
        W.Hero.DebugSetResources(6,3);
        float prior=W.Hero.LastImpactTime;
        Check(W.Hero.TryPunch(),"CONTROL: charged punch accepted with a fresh pool.");
        yield return WaitFor(()=>W.Hero.LastImpactTime!=prior,2f,"punch force");
        float punchForce=W.Hero.LastForce;int punchBodies=W.Hero.LastAffectedBodies;
        Log($"PUNCH geometry at impact: origin={W.Hero.transform.position+Vector3.up*1f+W.Hero.transform.forward*W.Powers.Strength.Definition.OriginOffset:F2}; near target {Vector3.Distance(W.Hero.transform.position+Vector3.up*1f+W.Hero.transform.forward*W.Powers.Strength.Definition.OriginOffset,near.position):F2}m, far target {Vector3.Distance(W.Hero.transform.position+Vector3.up*1f+W.Hero.transform.forward*W.Powers.Strength.Definition.OriginOffset,far.position):F2}m from it");
        yield return new WaitForSeconds(.25f);
        float punchNear=near.linearVelocity.magnitude,punchFar=far.linearVelocity.magnitude;
        Log($"PUNCH force={punchForce:F0} N·s bodies={punchBodies} target(2.8m)={punchNear:F3}m/s target(5.6m)={punchFar:F3}m/s | origin {W.Powers.Strength.Definition.OriginOffset:F2}m forward, radius {W.Powers.Stats(W.Powers.Strength).Radius:F2}m");
        Check(punchBodies==1&&punchFar<.01f,$"CONTROL: the punch's {punchReach:F2}m reach touched only the near target ({punchBodies} body; the 5.6m target stayed at {punchFar:F3}m/s).");
        Move(W.City.Spawn+Vector3.up*200);Place(near,2.8f);Place(far,5.6f);yield return null;
        W.Hero.DebugSetResources(6,3);
        prior=W.Hero.LastKickImpactTime;
        float priorMarker=P.LastKickAnimationImpactTime;
        Check(W.Hero.TryHurricaneKick(),"Hurricane Kick accepted through the Super Strength runtime.");
        yield return null;
        Check(P.Animator.GetCurrentAnimatorStateInfo(0).IsName("Hurricane Kick"),"Hurricane Kick dispatches the shared humanoid's Hurricane Kick state (previously Unwired Hurricane Kick).");
        yield return WaitFor(()=>W.Hero.LastKickImpactTime!=prior,2f,"kick force");
        Log($"KICK geometry at impact: origin={W.Hero.transform.position+Vector3.up*HeroAbilityTuning.KickOriginHeight+W.Hero.transform.forward*HeroAbilityTuning.KickOriginOffset:F2}; near target {Vector3.Distance(W.Hero.transform.position+Vector3.up*HeroAbilityTuning.KickOriginHeight+W.Hero.transform.forward*HeroAbilityTuning.KickOriginOffset,near.position):F2}m, far target {Vector3.Distance(W.Hero.transform.position+Vector3.up*HeroAbilityTuning.KickOriginHeight+W.Hero.transform.forward*HeroAbilityTuning.KickOriginOffset,far.position):F2}m from it");
        yield return WaitFor(()=>P.LastKickAnimationImpactTime!=priorMarker,1f,"kick animation impact marker");
        float kickImpact=W.Hero.LastKickImpactTime,kickForce=W.Hero.LastKickForce;int kickBodies=W.Hero.LastKickAffectedBodies;
        float offset=(kickImpact-P.LastKickAnimationImpactTime)*1000;
        Log($"KICK animation marker t={P.LastKickAnimationImpactTime:F5} frame={P.LastKickAnimationImpactFrame}; real force t={kickImpact:F5} frame={W.Hero.LastKickImpactFrame}; offset={offset:F2}ms across {W.Hero.LastKickImpactFrame-P.LastKickAnimationImpactFrame} frames.");
        Check(P.LastKickAnimationImpactTime>0f&&Mathf.Abs(W.Hero.LastKickImpactFrame-P.LastKickAnimationImpactFrame)<=2,"Kick force and its evaluated clip impact marker align within two rendered frames, exactly like the punch.");
        yield return new WaitForSeconds(.25f);
        float kickNear=near.linearVelocity.magnitude,kickFar=far.linearVelocity.magnitude;
        Log($"HURRICANE KICK force={kickForce:F0} N·s bodies={kickBodies} target(2.8m)={kickNear:F3}m/s target(5.6m)={kickFar:F3}m/s | origin {HeroAbilityTuning.KickOriginOffset:F2}m forward, radius {HeroAbilityTuning.KickRadius:F2}m");
        Check(kickForce>punchForce,$"Kick applies more force than the punch: {kickForce:F0} N·s vs {punchForce:F0} N·s (x{HeroAbilityTuning.KickForceMultiplier:F2}).");
        Check(kickNear>punchNear,$"Kick throws the same 45kg mass harder than the punch: {kickNear:F3}m/s vs {punchNear:F3}m/s.");
        Check(kickBodies==2&&kickFar>1f,$"Kick is wider than the punch: the same two targets give {kickBodies} bodies hit and {kickFar:F3}m/s on the 5.6m one, versus {punchBodies} body and {punchFar:F3}m/s for the punch ({punchReach:F2}m reach vs {kickReach:F2}m).");
        Destroy(near.gameObject);Destroy(far.gameObject);
    }
    IEnumerator NpcDamage()
    {
        var punchCop=CityNpc.Spawn(W,W.City.Spawn+Vector3.up*.1f,NpcRole.Cop);
        Check(punchCop!=null,"CONTROL: a cop spawns on the NavMesh as a damage target.");
        punchCop.Freeze(90);
        Check(TryAimAt(punchCop.transform,out var position,out var rotation),$"CONTROL: a clear ground position faces the {punchCop.Role} target.");
        MoveTo(position,rotation);yield return Settle();
        float punchBefore=punchCop.Health;
        W.Hero.DebugSetResources(6,3);
        float prior=W.Hero.LastImpactTime;
        Check(W.Hero.TryPunch(),"CONTROL: punch accepted against a live NPC.");
        yield return WaitFor(()=>W.Hero.LastImpactTime!=prior,2f,"punch damage");
        yield return new WaitForSeconds(.25f);
        float punchDamage=punchBefore-punchCop.Health;
        Check(punchDamage>0f,$"CONTROL: the punch really damaged the cop ({punchDamage:F1} of {punchBefore:F0} health); no geometry shielded it.");
        var kickCop=CityNpc.Spawn(W,W.City.Spawn+Vector3.up*.1f,NpcRole.Cop);
        Check(kickCop!=null,"CONTROL: a second identical cop spawns for the kick comparison.");
        kickCop.Freeze(90);
        Check(TryAimAt(kickCop.transform,out position,out rotation),$"CONTROL: a clear ground position faces the second {kickCop.Role} target.");
        MoveTo(position,rotation);yield return Settle();
        float kickBefore=kickCop.Health;
        W.Hero.DebugSetResources(6,3);
        prior=W.Hero.LastKickImpactTime;
        Check(W.Hero.TryHurricaneKick(),"Hurricane Kick accepted against a live NPC.");
        yield return WaitFor(()=>W.Hero.LastKickImpactTime!=prior,2f,"kick damage");
        yield return new WaitForSeconds(.25f);
        float kickDamage=kickBefore-kickCop.Health;
        Log($"NPC DAMAGE on same-role cops (punch target {punchBefore:F0} health, kick target {kickBefore:F0} health): punch {punchDamage:F1} -> {punchCop.Health:F1} left; hurricane kick {kickDamage:F1} -> {kickCop.Health:F1} left; ratio {kickDamage/Mathf.Max(.01f,punchDamage):F2}x");
        Check(kickDamage>punchDamage,$"Kick deals more real NPC damage than the punch: {kickDamage:F1} vs {punchDamage:F1} on same-role cops.");
        Destroy(punchCop.gameObject);Destroy(kickCop.gameObject);
    }
    IEnumerator Regressions()
    {
        Check(W.Tuning.Movement.WalkSpeed==5.2f&&W.Tuning.Movement.RunSpeed==9f&&W.Tuning.Movement.JumpSpeed==8.5f,"CONTROL: movement constants unchanged (walk 5.2 / run 9 / jump 8.5 m/s).");
        var stats=W.Powers.Stats(W.Powers.Strength);
        Check(stats.Force==1350f&&stats.Radius==3.3f&&stats.Damage==35f&&stats.Charges==3&&Mathf.Approximately(stats.Cooldown,.45f),"CONTROL: Super Strength tier-0 punch stats unchanged (1350 N·s / 3.3 m / 35 dmg / 3 charges / 0.45 s).");
        Move(W.City.Spawn+Vector3.up*200);yield return new WaitForSeconds(.3f);
        W.Hero.DebugSetResources(6,3);
        float prior=W.Hero.LastImpactTime;
        Check(W.Hero.TryPunch(),"CONTROL: punch still fires after a backflip and two kicks in the same session.");
        yield return WaitFor(()=>W.Hero.LastImpactTime!=prior,2f,"punch regression force");
        Check(Mathf.Approximately(W.Hero.LastForce,stats.Force),$"Punch force is still exactly the configured strength value ({W.Hero.LastForce:F0} N·s); the kick multipliers did not leak into it.");
    }
}
#endif
