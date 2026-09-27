using System.Collections.Generic;
using UnityEngine;

/// Adapter only. Animator owns the base skeleton; LateUpdate owns visual root lean and
/// weighted flight/panic bones. Physics roots, controller, resource accounting stay outside.
[DefaultExecutionOrder(50)]
public sealed class HumanoidPresentation : MonoBehaviour
{
    public Animator Animator { get; private set; }
    public HumanoidAnimationTuning Tuning { get; private set; }
    public Transform PoseRoot { get; private set; }
    public float FlightWeight { get; private set; }
    public float ForwardFlight { get; private set; }
    public float PanicWeight { get; private set; }
    public float MeasuredSpeed { get; private set; }
    public float ReferenceMeshHeight { get; private set; }
    public float ModelScale { get; private set; }
    public string State { get; private set; }
    public int HitCount { get; private set; }
    public int DeathCount { get; private set; }
    public float LastAnimationImpactTime { get; private set; }=-1;
    public int LastAnimationImpactFrame { get; private set; }=-1;
    public float LastKickAnimationImpactTime { get; private set; }=-1;
    public int LastKickAnimationImpactFrame { get; private set; }=-1;
    /// NPC telegraphed attack: clip state started at windup start, the fitted playback rate, and when the clip's
    /// impact marker was actually evaluated (compare with CityNpc.LastReleaseTime/Frame).
    public string AttackState { get; private set; }
    public float AttackFittedRate { get; private set; }
    public float LastAttackImpactTime { get; private set; }=-1;
    public int LastAttackImpactFrame { get; private set; }=-1;
    /// "Landing squash (visual only)": the visual root. Archetype silhouette scale lives here, never on the physics root.
    public Transform VisualRoot { get; private set; }
#if UNITY_EDITOR
    public HeroPresentationState? VerificationState;
#endif
    SuperHeroController hero;CityNpc npc;
    WorldSession world;PowerUser powers;Vector3 neutralHipsPosition;
    float actionUntil, phase, lean, actionYaw, attackImpactSeconds, attackEndSeconds, attackFollowRate;bool dead, punchMarkerPending, kickMarkerPending, attackMarkerPending, deferredHit;AnimationClip attackClip;string attackMarkerState;
    readonly Dictionary<Transform,Quaternion> neutral=new Dictionary<Transform,Quaternion>();
    public static HumanoidPresentation Create(GameObject owner,float height,SuperHeroController hero=null,CityNpc npc=null,HeroDefinition definition=null)
    {
        var tuning=definition!=null&&definition.Animation!=null?definition.Animation:Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning");
        if(tuning==null||tuning.Model==null||tuning.Controller==null)throw new System.InvalidOperationException("Run Overpowered > Animation > Build shared humanoid presentation first.");
        var squash=new GameObject("Landing squash (visual only)").transform;squash.SetParent(owner.transform,false);
        var pose=new GameObject("Humanoid pose (visual only)").transform;pose.SetParent(squash,false);
        // NPC look (data): the shared mannequin, or with SidekickNpcs a Sidekick body picked per NPC from NpcLooks.
        var look=npc!=null&&tuning.SidekickNpcs&&tuning.NpcLooks!=null&&tuning.NpcLooks.Length>0?tuning.NpcLooks[(owner.GetEntityId().GetHashCode()&int.MaxValue)%tuning.NpcLooks.Length]:null;
        var model=Instantiate(definition!=null&&definition.CharacterPrefab!=null?definition.CharacterPrefab:look!=null?look.Prefab:tuning.Model,pose);model.name=definition!=null?definition.DisplayName:look!=null?look.Prefab.name:"Shared Mixamo humanoid";
        var animator=model.GetComponent<Animator>();animator.enabled=false;
        var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();
        var suit=definition!=null?definition.Suit:look;
        if(suit!=null)foreach(var r in renderers)suit.ApplyBody(r);
        // Skin bounds are conservative animation envelopes, not actual character proportions.
        // Measure the imported reference pose's vertices once at spawn, then fit feet-to-crown.
        Bounds bounds=new Bounds();bool hasBounds=false;
        foreach(var r in renderers)
        {
            var mesh=new Mesh();r.BakeMesh(mesh);
            foreach(var vertex in mesh.vertices){var point=r.transform.TransformPoint(vertex);if(!hasBounds){bounds=new Bounds(point,Vector3.zero);hasBounds=true;}else bounds.Encapsulate(point);}
            Destroy(mesh);
        }
        float scale=height/bounds.size.y;model.transform.localScale*=scale;
        model.transform.localPosition-=Vector3.up*(bounds.min.y-pose.position.y)*scale;
        if(definition!=null)model.transform.localScale=Vector3.Scale(model.transform.localScale,definition.VisualScale);
        var loadout=definition!=null?WorldSession.Instance.Progression.Data.Loadout:null;
        // Player: Hero Forge loadout colours. NPCs: role sets the body colour, the archetype (if any) sets the joints accent.
        // A Sidekick suit recolours its colour map with the same two roles: one shared material per (suit, primary, secondary).
        var accent=npc!=null&&npc.Archetype!=null?npc.Archetype.Accent:CityColor.Metal;
        var body=npc==null?CityColor.Blue:npc.Role==NpcRole.Civilian?CityColor.Amber:npc.Role==NpcRole.Cop?CityColor.Teal:CityColor.Red;
        var primary=loadout!=null?loadout.Primary:body;var secondary=loadout!=null?loadout.Secondary:accent;
        foreach(var r in renderers)
        {
            var mats=r.sharedMaterials;
            for(int i=0;i<mats.Length;i++)mats[i]=suit!=null&&mats[i]==suit.Source?CityMaterials.Suit(suit,primary,secondary):CityMaterials.Get(r.name.Contains("Joints")?secondary:primary);
            r.sharedMaterials=mats;r.updateWhenOffscreen=true;
        }
        var presentation=owner.AddComponent<HumanoidPresentation>();presentation.hero=hero;presentation.npc=npc;presentation.Tuning=tuning;presentation.Animator=animator;presentation.PoseRoot=pose;
        presentation.ReferenceMeshHeight=bounds.size.y;presentation.ModelScale=scale;presentation.VisualRoot=squash;
        // Silhouette: scale the visual root about the feet (the fit above is unchanged). CityNpc.Spawn scales the collider/agent to match.
        if(npc!=null&&npc.Archetype!=null)squash.localScale=Vector3.one*npc.Archetype.VisualScale;
        for(int i=0;i<(int)HumanBodyBones.LastBone;i++){var bone=animator.GetBoneTransform((HumanBodyBones)i);if(bone!=null)presentation.neutral[bone]=bone.localRotation;}
        presentation.neutralHipsPosition=animator.GetBoneTransform(HumanBodyBones.Hips).localPosition;
        animator.runtimeAnimatorController=tuning.Controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;
        presentation.phase=(owner.GetEntityId().GetHashCode()%1000)*.013f;
        if(hero!=null)
        {
            hero.PunchWindupSeconds=tuning.PunchWindup;hero.KickWindupSeconds=tuning.KickWindup;
            hero.PunchStarted+=presentation.Punch;hero.Jumped+=presentation.Jump;hero.Landed+=presentation.Land;
            hero.BackflipStarted+=presentation.Backflip;hero.HurricaneKickStarted+=presentation.HurricaneKick;
            var procedural=owner.AddComponent<ProceduralHeroAnimation>();procedural.HumanoidSquashOnly=true;
            procedural.Initialize(hero,squash,null,Resources.Load<ProceduralAnimationTuning>("ProceduralAnimationTuning"));
        }
        if(npc!=null){npc.Damaged+=presentation.Damage;npc.Attacked+=presentation.Attack;npc.AttackWindupStarted+=presentation.AttackWindup;npc.AttackCanceled+=presentation.AttackCanceled;}
        presentation.Change("Locomotion",true);return presentation;
    }
    void OnDestroy()
    {
        if(world!=null){world.PlayerDamaged-=Damage;world.PlayerRespawned-=Revive;}
        if(powers!=null)powers.Activated-=PowerActivated;
        if(hero!=null){hero.PunchStarted-=Punch;hero.Jumped-=Jump;hero.Landed-=Land;hero.BackflipStarted-=Backflip;hero.HurricaneKickStarted-=HurricaneKick;}
        if(npc!=null){npc.Damaged-=Damage;npc.Attacked-=Attack;npc.AttackWindupStarted-=AttackWindup;npc.AttackCanceled-=AttackCanceled;}
    }
    void Start()
    {
        if(hero==null)return;world=WorldSession.Instance;powers=GetComponent<PowerUser>();
        world.PlayerDamaged+=Damage;world.PlayerRespawned+=Revive;powers.Activated+=PowerActivated;
    }
    void PowerActivated(PowerDefinition definition){if(definition.CastingPresentation)Cast();}
    void Change(string state,bool immediate=false,float offset=0)
    {
        if(State==state&&!immediate)return;State=state;
        if(immediate)Animator.Play(state,0,offset);else Animator.CrossFadeInFixedTime(state,Tuning.TransitionSeconds,0,offset);
    }
    void Action(string state,AnimationClip clip,float speed=1,float start=0,bool immediate=false)
    {
        if(dead)return;Animator.SetFloat("ActionRate",speed);actionUntil=Time.time+Mathf.Max(0,clip.length-start)/speed;
        State=null; // Repeated casts / cop shots restart even if the previous clip is still active.
        // Normalized clip offset is independent of playback multiplier; fixed-time offsets are not.
        Change(state,immediate,immediate?start/clip.length:start/speed);
    }
    void Punch(){punchMarkerPending=true;Action("Punch",Tuning.Punch,Tuning.PunchPlayback,Tuning.PunchStartSeconds,true);}
    void Jump(){Action("Jump",Tuning.Jump,Tuning.JumpPlayback);}
    // The backflip clip is fitted to the dash window exactly, so the two cannot drift apart.
    float BackflipRate=>Mathf.Max(.01f,(Tuning.Backflip.length-Tuning.BackflipStartSeconds)/Mathf.Max(.01f,HeroAbilityTuning.BackflipSeconds));
    void Backflip(){Action("Backflip",Tuning.Backflip,BackflipRate,Tuning.BackflipStartSeconds,true);}
    void HurricaneKick(){kickMarkerPending=true;Action("Hurricane Kick",Tuning.HurricaneKick,Tuning.KickPlayback,Tuning.KickStartSeconds,true);}
    void Land(float impact)
    {
        // A backflip owns the pose through its own landing; the shared Land clip is not layered over it.
        if(State=="Backflip"&&Time.time<actionUntil)return;
        if(impact>=Resources.Load<ProceduralAnimationTuning>("ProceduralAnimationTuning").LandMinimumImpactSpeed)Action("Land",Tuning.Land,Tuning.LandPlayback);
    }
    public void Cast(){Action("Cast",Tuning.Cast,Tuning.CastPlayback);}
    public void Damage(bool lethal)
    {
        if(dead)return;
        if(lethal){punchMarkerPending=false;attackMarkerPending=false;deferredHit=false;Action("Death",Tuning.Death,Tuning.ActionPlayback,0,true);dead=true;DeathCount++;}
        else{if(punchMarkerPending||attackMarkerPending)deferredHit=true;else Action("Hit",Tuning.Hit,Tuning.HitPlayback,0,true);HitCount++;}
    }
    public void Revive(){dead=false;punchMarkerPending=false;deferredHit=false;actionUntil=0;Change("Locomotion",true);}
    bool Ranged=>npc!=null&&(npc.Archetype!=null?npc.Archetype.Kind==AttackKind.Ranged:npc.Role==NpcRole.Cop);
    // Telegraphed NPC attack: the clip starts at WINDUP START and its playback is fitted so the clip's impact marker lands
    // exactly at release (same fitted-playback pattern as the Backflip). Rusher=Punch, Gunner=Shoot, Brute=Hurricane Kick.
    void AttackWindup()
    {
        if(dead||npc==null||npc.Archetype==null)return;
        var t=Tuning;
        switch(npc.Archetype.Kind)
        {
            case AttackKind.Ranged:FitAttack("Shoot",t.Shoot,t.ShootStartSeconds,t.ShootImpactSeconds,t.ShootEndSeconds,t.ShootPlayback);break;
            case AttackKind.Slam:FitAttack("Hurricane Kick",t.HurricaneKick,t.SlamStartSeconds,t.KickImpactSeconds,t.HurricaneKick.length,t.KickPlayback);break;
            default:FitAttack("Punch",t.Punch,t.EnemyPunchStartSeconds,t.PunchImpactSeconds,t.Punch.length,t.PunchPlayback);break;
        }
    }
    void FitAttack(string state,AnimationClip clip,float start,float impact,float end,float followRate)
    {
        float windup=Mathf.Max(.01f,npc.WindupSeconds);
        AttackFittedRate=Mathf.Max(.01f,(impact-start)/windup);
        attackClip=clip;attackImpactSeconds=impact;attackEndSeconds=Mathf.Max(impact,end);attackFollowRate=Mathf.Max(.01f,followRate);
        Action(state,clip,AttackFittedRate,start,true);AttackState=attackMarkerState=state;attackMarkerPending=true;
        // Hold the gesture through release; Attack() then plays the follow-through at the clip's normal playback.
        actionUntil=Time.time+windup+(attackEndSeconds-impact)/AttackFittedRate;
    }
    void AttackCanceled()
    {
        if(AttackState==null)return;
        attackMarkerPending=false;AttackState=null;if(!dead)actionUntil=0; // Update() returns to locomotion
    }
    void Attack()
    {
        if(dead)return;
        if(AttackState!=null&&State==AttackState)
        {
            // Release: the fitted clip has just reached its impact; finish the gesture at normal playback.
            Animator.SetFloat("ActionRate",attackFollowRate);actionUntil=Time.time+(attackEndSeconds-attackImpactSeconds)/attackFollowRate;AttackState=null;
            return;
        }
        // Legacy (no windup was presented): play the gesture at the strike.
        if(!Ranged){Action("Punch",Tuning.Punch,Tuning.PunchPlayback);return;}
        Action("Shoot",Tuning.Shoot,Tuning.ShootPlayback,Tuning.ShootStartSeconds);
        actionUntil=Time.time+Mathf.Max(.01f,Tuning.ShootEndSeconds-Tuning.ShootStartSeconds)/Tuning.ShootPlayback;
    }
    public void AnimationImpact(){LastAnimationImpactTime=Time.time;LastAnimationImpactFrame=Time.frameCount;}
    public void KickAnimationImpact(){LastKickAnimationImpactTime=Time.time;LastKickAnimationImpactFrame=Time.frameCount;}
    void Update()
    {
        if(Animator==null)return;
        if(deferredHit&&!punchMarkerPending&&!attackMarkerPending){deferredHit=false;Action("Hit",Tuning.Hit,Tuning.HitPlayback,0,true);}
        if(hero!=null){hero.PunchWindupSeconds=Tuning.PunchWindup;hero.KickWindupSeconds=Tuning.KickWindup;}
        HeroPresentationState state=ReadState();MeasuredSpeed=new Vector2(state.LocalVelocity.x,state.LocalVelocity.z).magnitude;
        Animator.SetFloat("Speed",MeasuredSpeed,Tuning.SpeedDamping,Time.deltaTime);
        float smooth=Animator.GetFloat("Speed");
        float blend=smooth<Tuning.WalkThreshold?Mathf.InverseLerp(0,Tuning.WalkThreshold,smooth):smooth<Tuning.JogThreshold?1+Mathf.InverseLerp(Tuning.WalkThreshold,Tuning.JogThreshold,smooth):2+Mathf.InverseLerp(Tuning.JogThreshold,Tuning.RunThreshold,smooth);
        Animator.SetFloat("Blend",blend);
        bool panic=npc!=null&&npc.Role==NpcRole.Civilian&&npc.Fleeing&&!npc.Dead;
        Animator.SetFloat("GaitRate",panic?Tuning.PanicPlayback:1);
        if(dead)return;
        if(Time.time>=actionUntil)Change(state.Flying?"Fly":!state.Grounded?"Air":panic?"Panic":state.LocalVelocity.z<Tuning.BackThreshold?"Back":Ranged&&MeasuredSpeed>.1f?"Armed":"Locomotion");
    }
    HeroPresentationState ReadState()
    {
#if UNITY_EDITOR
        if(VerificationState.HasValue)return VerificationState.Value;
#endif
        if(hero!=null)return hero.PresentationState;
        return new HeroPresentationState(npc!=null&&npc.Agent.enabled?transform.InverseTransformDirection(npc.Agent.velocity):Vector3.zero,true,false);
    }
    void LateUpdate()
    {
        if(Animator==null)return;
        if(punchMarkerPending&&Animator.GetCurrentAnimatorStateInfo(0).IsName("Punch")&&Animator.GetCurrentAnimatorStateInfo(0).normalizedTime*Tuning.Punch.length>=Tuning.PunchImpactSeconds)
        {AnimationImpact();punchMarkerPending=false;}
        if(kickMarkerPending&&Animator.GetCurrentAnimatorStateInfo(0).IsName("Hurricane Kick")&&Animator.GetCurrentAnimatorStateInfo(0).normalizedTime*Tuning.HurricaneKick.length>=Tuning.KickImpactSeconds)
        {KickAnimationImpact();kickMarkerPending=false;}
        if(attackMarkerPending&&attackClip!=null&&Animator.GetCurrentAnimatorStateInfo(0).IsName(attackMarkerState)&&Animator.GetCurrentAnimatorStateInfo(0).normalizedTime*attackClip.length>=attackImpactSeconds)
        {LastAttackImpactTime=Time.time;LastAttackImpactFrame=Time.frameCount;attackMarkerPending=false;}
        ApplyPose(ReadState(),npc!=null&&npc.Fleeing&&!npc.Dead,Time.deltaTime);
    }
    // Verification supplies recorded states through this same overlay path.
    public void ApplyPose(HeroPresentationState state,bool panic,float dt)
    {
        var t=Tuning;float response=1-Mathf.Exp(-t.PoseResponse*dt);
        FlightWeight=Mathf.Lerp(FlightWeight,state.Flying&&!dead?1:0,1-Mathf.Exp(-t.FlightBlendResponse*dt));
        float speed=new Vector2(state.LocalVelocity.x,state.LocalVelocity.z).magnitude;
        ForwardFlight=Mathf.Lerp(ForwardFlight,Mathf.Clamp01(speed/t.FlightFullSpeed),response);
        PanicWeight=Mathf.Lerp(PanicWeight,panic&&!dead?1:0,1-Mathf.Exp(-t.PanicResponse*dt));
        float ground=state.Grounded&&!dead?Mathf.Clamp(state.LocalVelocity.z/t.GroundLeanFullSpeed,-1,1)*t.GroundLean:0;
        lean=Mathf.Lerp(lean,Mathf.Lerp(ground,t.PanicLean,PanicWeight),response);
        actionYaw=Mathf.LerpAngle(actionYaw,State=="Shoot"&&Time.time<actionUntil?t.ShootVisualYaw:0,response);
        PoseRoot.localRotation=Quaternion.Euler(Mathf.Lerp(lean,t.FlightPitch*ForwardFlight,FlightWeight),actionYaw,0);
        PoseRoot.localPosition=Vector3.up*(Mathf.Sin(Time.time*t.HoverBobFrequency*Mathf.PI*2)*t.HoverBobAmplitude*FlightWeight*(1-ForwardFlight));
        // Restore/override from a neutral humanoid pose, not last frame's modified pose.
        // Active action clips retain upper-body authority, including aerial punches/casts.
        bool acting=Time.time<actionUntil;
        var hips=Animator.GetBoneTransform(HumanBodyBones.Hips);hips.localPosition=Vector3.Lerp(hips.localPosition,neutralHipsPosition,FlightWeight);
        foreach(var item in neutral)
        {
            bool upper=acting&&(item.Key.IsChildOf(Animator.GetBoneTransform(HumanBodyBones.Spine))||item.Key==Animator.GetBoneTransform(HumanBodyBones.Spine));
            item.Key.localRotation=Quaternion.Slerp(item.Key.localRotation,item.Value,upper?0:FlightWeight);
        }
        for(int side=-1;side<=1;side+=2)
        {
            var upper=Bone(side,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm);
            var lower=Bone(side,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm);
            var hand=Bone(side,HumanBodyBones.LeftHand,HumanBodyBones.RightHand);
            Vector3 hover=new Vector3(side*t.HoverArmOut,-1,t.HoverArmForward);
            Vector3 forward=new Vector3(side*t.FlightArmSpread,1,0);
            float armWeight=acting?0:FlightWeight;
            Aim(upper,lower,Vector3.Lerp(hover,forward,ForwardFlight),armWeight);
            Aim(lower,hand,Vector3.Lerp(new Vector3(side*t.HoverArmOut,-1,t.HoverElbowForward),forward,ForwardFlight),armWeight);
            float flail=Mathf.Sin((Time.time*t.PanicFlailFrequency+phase+side*t.PanicSidePhase)*Mathf.PI*2)*t.PanicFlailDegrees;
            Vector3 raised=Quaternion.Euler(flail,0,side*flail)*new Vector3(side*t.PanicArmOut,t.PanicArmRaise,0);
            float panicArms=acting?0:PanicWeight*(1-FlightWeight);
            Aim(upper,lower,raised,panicArms);Aim(lower,hand,new Vector3(side*t.PanicForearmOut,1,t.PanicForearmForward),panicArms);
            var leg=Bone(side,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg);
            var thigh=Bone(side,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg);
            var foot=Bone(side,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot);
            Aim(thigh,leg,Vector3.down,FlightWeight*ForwardFlight);
            Aim(leg,foot,Vector3.down,FlightWeight*ForwardFlight);
            leg.localRotation*=Quaternion.Euler(t.HoverKneeBend*(1-ForwardFlight)*FlightWeight,0,0);
        }
        var head=Animator.GetBoneTransform(HumanBodyBones.Head);
        if(!acting)head.localRotation*=Quaternion.Euler(-t.FlightHeadLift*ForwardFlight*FlightWeight,Mathf.Sin((Time.time*t.PanicLookBackFrequency+phase)*Mathf.PI*2)*t.PanicLookBackDegrees*PanicWeight*(1-FlightWeight),0);
    }
    Transform Bone(int side,HumanBodyBones left,HumanBodyBones right)=>Animator.GetBoneTransform(side<0?left:right);
    void Aim(Transform bone,Transform child,Vector3 localDirection,float weight)
    {
        if(weight<=0)return;Quaternion desired=Quaternion.FromToRotation(child.position-bone.position,PoseRoot.TransformDirection(localDirection))*bone.rotation;
        bone.rotation=Quaternion.Slerp(bone.rotation,desired,weight);
    }
}
