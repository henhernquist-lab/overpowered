using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(10)]
public sealed class SynergyRunner : MonoBehaviour
{
    public PowerUser User {get;private set;}
    public PowerSynergyDefinition Definition=>User.Synergy;
    public ForgeCatalog Catalog=>User.Forge;
    public bool Busy {get;private set;}
    public bool DrivesMotion {get;set;}
    public float Cooldown {get;private set;}
    public string Feedback {get;private set;}="Ready";
    public Vector3 Target;
    public int Impacts {get;private set;}
    public int AffectedBodies {get;private set;}
    public float LastForce {get;private set;}
    public SynergyVfx Vfx {get;private set;}
    public readonly Collider[] Hits=new Collider[128];
    readonly RaycastHit[] rays=new RaycastHit[64];
    Coroutine action;
    public static readonly WaitForFixedUpdate FixedStep=new WaitForFixedUpdate();
    float deadline,fovUntil,baseFov;
    Camera view;
    readonly HashSet<CityNpc> affected=new HashSet<CityNpc>();
    readonly Rigidbody[] held=new Rigidbody[8];
    readonly bool[] gravity=new bool[8];
    public int HeldCount {get;private set;}
    public bool ReleaseRequested {get;private set;}
    public CityNpc Captive;
    public Rigidbody CaptiveBody;
    public SynergySuspension Suspension;
    float glacierUntil,nextGlacierFx;
    readonly Collider[] ignored=new Collider[128];int ignoredCount;
    public bool GlacierActive=>Time.time<glacierUntil;
    public void Initialize(PowerUser user)
    {
        User=user;Vfx=gameObject.AddComponent<SynergyVfx>();Vfx.Initialize(Catalog);
        WorldSession.Instance.PlayerDamaged+=OnDamage;
    }
    bool Live=>WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&!WorldSession.Instance.MenuOpen&&
        (WorldSession.Instance.Mode==null||(!WorldSession.Instance.Mode.Paused&&!WorldSession.Instance.Mode.Ended));
    void Update()
    {
        if(User==null)return;
        Cooldown=Mathf.Max(0,Cooldown-Time.deltaTime);
        if(Busy&&(WorldSession.Instance.PlayerDead||Time.time>deadline))Cancel();
        if(Live&&(Input.GetKeyDown(Catalog.SynergyKey)||Input.GetKeyDown(Catalog.SynergyGamepadButton)))TryActivate();
        if(GlacierActive&&Live&&Time.time>=nextGlacierFx)
        {
            var animator=GetComponent<HumanoidPresentation>().Animator;
            Vfx.Burst(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,Definition);
            Vfx.Burst(animator.GetBoneTransform(HumanBodyBones.RightHand).position,Definition);
            nextGlacierFx=Time.time+.25f;
        }
    }
    public bool TryActivate()
    {
        if(!Live){Feedback="Unavailable while paused, in menu or defeated";return false;}
        if(Busy&&Definition.Effect.Repeat(this))return false;
        if(Busy||User.Hero.BackflipActive){Feedback="Finish the current move";return false;}
        if(Cooldown>0){Feedback="Synergy cooling down";return false;}
        if(Definition==null||Definition.Effect==null){Feedback="No synergy for this pair";return false;}
        if(!User.IsEquipped(Definition.PowerA)||!User.IsEquipped(Definition.PowerB)){Feedback="Equip both powers";return false;}
        Captive=null;CaptiveBody=null;
        if(!Definition.Effect.CanBegin(this)){Feedback="No valid target";return false;}
        User.Release(false);
        affected.Clear();ReleaseRequested=false;
        Cooldown=Definition.Cooldown;Busy=true;deadline=Time.time+Mathf.Max(8,Definition.Duration+4);
        Feedback=Definition.DisplayName;
        action=StartCoroutine(Perform(Definition.Effect));
        return true;
    }
    IEnumerator Perform(SynergyEffect effect)
    {
        var steps=new Stack<IEnumerator>();steps.Push(effect.Execute(this));
        while(steps.Count>0)
        {
            if(Time.timeScale==0){yield return null;continue;}
            object next=null;bool moved=false;Exception error=null;
            try{moved=steps.Peek().MoveNext();if(moved)next=steps.Peek().Current;}
            catch(Exception e){error=e;}
            if(error!=null)
            {
                Feedback="Synergy interrupted";Debug.LogException(error);Cancel();yield break;
            }
            if(!moved){(steps.Pop() as IDisposable)?.Dispose();continue;}
            if(next is IEnumerator nested)steps.Push(nested);else yield return next;
        }
        RestoreActorCollisions();
        bool droveMotion=DrivesMotion;DrivesMotion=false;Busy=false;if(droveMotion)User.Hero.ResetMotion();action=null;
    }
    public CollisionFlags Move(Vector3 velocity,bool flying)=>User.Hero.MoveAbility(velocity,flying);
    public bool GroundTarget(bool aimed,out Vector3 point)
    {
        if(aimed&&Camera.main!=null)
        {
            var aim=Camera.main.ViewportPointToRay(new Vector3(.5f,.5f,0));
            int hits=Physics.RaycastNonAlloc(aim,rays,Definition.Range);
            float nearest=float.PositiveInfinity;point=default;
            for(int i=0;i<hits;i++)
            {
                var hit=rays[i];
                if(hit.transform.root==transform||hit.collider.isTrigger||hit.rigidbody!=null||hit.normal.y<.6f)continue;
                if(hit.distance<nearest){nearest=hit.distance;point=hit.point;}
            }
            if(nearest<float.PositiveInfinity)return true;
        }
        Vector3 start=transform.position+Vector3.up*.4f;
        if(aimed)start+=Vector3.Scale(User.AimDirection,new Vector3(1,0,1)).normalized*Mathf.Min(12,Definition.Range);
        int count=Physics.RaycastNonAlloc(start+Vector3.up*2,Vector3.down,rays,Definition.Range+60);
        float best=float.PositiveInfinity;point=default;
        for(int i=0;i<count;i++)
        {
            var hit=rays[i];
            if(hit.transform.root==transform||hit.collider.isTrigger||hit.rigidbody!=null||hit.normal.y<.6f)continue;
            if(hit.distance<best){best=hit.distance;point=hit.point;}
        }
        return best<float.PositiveInfinity;
    }
    public void Impact(Vector3 position,float multiplier=1)
    {
        var d=Definition;LastForce=d.Force*multiplier;
        AffectedBodies=CombatImpact.Blast(User,position,d.Radius,LastForce,d.Damage*multiplier,.65f,d.BurnSeconds,true);
        if(d.BurnSeconds>0)
        {
            int count=Physics.OverlapSphereNonAlloc(position,d.Radius,Hits);
            for(int i=0;i<count;i++)Hits[i].GetComponentInParent<CityNpc>()?.MarkBurn(d.BurnSeconds);
        }
        Impacts++;Vfx.Burst(position,d);Feedback=d.DisplayName+" / impact";
        AudioDirector.Instance?.Play(AudioCue.Destruction,position);
        KickCamera();
    }
    public void KickCamera()
    {
        if(view==null)view=Camera.main;
        if(view==null)return;
        var rig=view.GetComponent<ThirdPersonCamera>();
        if(rig!=null&&rig.isActiveAndEnabled){rig.KickFov(Catalog.FovKick,Catalog.FovSeconds);return;}
        if(fovUntil<=Time.unscaledTime)baseFov=view.fieldOfView;
        fovUntil=Time.unscaledTime+Catalog.FovSeconds;
    }
    void LateUpdate()
    {
        if(view==null||fovUntil<=0)return;
        float left=Mathf.Clamp01((fovUntil-Time.unscaledTime)/Mathf.Max(.01f,Catalog.FovSeconds));
        view.fieldOfView=baseFov+Catalog.FovKick*left;
        if(left==0)fovUntil=0;
    }
    void OnDamage(bool died){if(died)Cancel();}
    public void AffectNearby(Vector3 position,float radius,float damage,float freeze,bool conditionedOnly)
    {
        int count=Physics.OverlapSphereNonAlloc(position,radius,Hits);
        for(int i=0;i<count;i++)
        {
            var npc=Hits[i].GetComponentInParent<CityNpc>();
            if(npc==null||npc.Dead||affected.Contains(npc)||conditionedOnly&&!npc.Frozen&&!npc.Burning)continue;
            if(!Visible(position,npc.transform.position+Vector3.up))continue;
            affected.Add(npc);npc.Freeze(freeze);npc.Damage(damage,User);
        }
    }
    bool Visible(Vector3 from,Vector3 to)
    {
        Vector3 delta=to-from;int count=Physics.RaycastNonAlloc(from,delta.normalized,rays,delta.magnitude);
        for(int i=0;i<count;i++)if(!rays[i].collider.isTrigger&&rays[i].rigidbody==null&&rays[i].collider.GetComponentInParent<CityNpc>()==null&&rays[i].transform.root!=transform)return false;
        return true;
    }
    public void EnableGlacier(){glacierUntil=Time.time+Definition.Duration;Feedback="Glacier Fist empowered melee";}
    public void RequestRelease(){ReleaseRequested=true;Feedback="Release orbit";}
    public void PassActors()
    {
        var controller=GetComponent<CharacterController>();
        foreach(var npc in WorldSession.Instance.Npcs)
        {
            if(npc==null||ignoredCount>=ignored.Length)continue;
            var collider=npc.GetComponent<Collider>();if(collider==null)continue;
            ignored[ignoredCount++]=collider;Physics.IgnoreCollision(controller,collider,true);
        }
    }
    void RestoreActorCollisions()
    {
        var controller=GetComponent<CharacterController>();
        for(int i=0;i<ignoredCount;i++){if(ignored[i]!=null&&controller!=null)Physics.IgnoreCollision(controller,ignored[i],false);ignored[i]=null;}ignoredCount=0;
    }
    public PowerStats ModifyMelee(PowerStats stats,Vector3 origin)
    {
        if(!GlacierActive)return stats;
        stats.Force*=Definition.MeleeMultiplier;
        affected.Clear();AffectNearby(origin,stats.Radius,0,Definition.FreezeSeconds,false);
        Vfx.Burst(origin,Definition);return stats;
    }
    public int GatherBodies()
    {
        ReleaseHeld();int count=Physics.OverlapSphereNonAlloc(transform.position,Definition.Range,Hits);
        for(int i=0;i<count&&HeldCount<Mathf.Clamp(Definition.MaxTargets,1,held.Length);i++)
        {
            var body=Hits[i].attachedRigidbody;
            if(body==null||body.isKinematic||body.mass>Definition.MaxMass||body.transform.root==transform||body.constraints==RigidbodyConstraints.FreezeAll)continue;
            bool duplicate=false;for(int j=0;j<HeldCount;j++)if(held[j]==body)duplicate=true;
            if(duplicate||!Visible(User.AimOrigin,body.position))continue;
            held[HeldCount]=body;gravity[HeldCount]=body.useGravity;HeldCount++;
        }
        return HeldCount;
    }
    public void HoldGathered(){for(int i=0;i<HeldCount;i++)if(held[i]!=null)held[i].useGravity=false;}
    public void HoldOne(Rigidbody body){ReleaseHeld();held[0]=body;gravity[0]=body.useGravity;HeldCount=1;HoldGathered();}
    public void Orbit(float elapsed)
    {
        for(int i=0;i<HeldCount;i++)
        {
            var body=held[i];if(body==null)continue;
            float angle=elapsed*2+i*Mathf.PI*2/HeldCount;
            Vector3 target=transform.position+Vector3.up*2+new Vector3(Mathf.Cos(angle),.2f*Mathf.Sin(angle*2),Mathf.Sin(angle))*Definition.OrbitRadius;
            body.AddForce((target-body.position)*Definition.Spring-body.linearVelocity*Definition.Damping,ForceMode.Acceleration);
        }
    }
    public void OrbitVfx(){for(int i=0;i<HeldCount;i++)if(held[i]!=null)Vfx.Burst(held[i].position,Definition);}
    public void LaunchHeld(int index,bool ignite)
    {
        var body=held[index];if(body==null)return;
        body.useGravity=gravity[index];body.AddForce(User.AimDirection*Definition.Force,ForceMode.Impulse);
        var thrown=body.GetComponent<ThrownProp>()??body.gameObject.AddComponent<ThrownProp>();thrown.Initialize(User,Definition.Damage,Definition.Duration+3);
        var payload=body.GetComponent<SynergyPayload>()??body.gameObject.AddComponent<SynergyPayload>();payload.Arm(this,ignite,ignite);
        held[index]=null;
    }
    public void ReleaseHeld()
    {
        for(int i=0;i<HeldCount;i++){if(held[i]!=null)held[i].useGravity=gravity[i];held[i]=null;}
        HeldCount=0;
    }
    public void Cancel()
    {
        if(action!=null)StopCoroutine(action);
        action=null;Busy=false;DrivesMotion=false;
        ReleaseHeld();if(Suspension!=null)Suspension.Finish();Suspension=null;Captive=null;CaptiveBody=null;glacierUntil=0;
        RestoreActorCollisions();
        if(User!=null)User.Hero.ResetMotion();
        if(view!=null&&fovUntil>0)view.fieldOfView=baseFov;
        fovUntil=0;
    }
    void OnDisable(){Cancel();}
    void OnDestroy(){if(WorldSession.Instance!=null)WorldSession.Instance.PlayerDamaged-=OnDamage;}
}
