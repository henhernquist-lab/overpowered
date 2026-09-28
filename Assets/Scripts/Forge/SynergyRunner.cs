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
    public CityNpc Captive;
    /// NPC physics handoffs an effect started (Void Grasp); Cancel finishes any still held so a cut-short synergy never leaves
    /// an NPC suspended until the handoff's own safety deadline.
    readonly List<SynergySuspension> suspended=new List<SynergySuspension>();
    public void Track(SynergySuspension suspension){if(suspension!=null&&!suspended.Contains(suspension))suspended.Add(suspension);}
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
    }
    public bool TryActivate()
    {
        if(!Live){Feedback="Unavailable while paused, in menu or defeated";return false;}
        if(Busy&&Definition.Effect.Repeat(this))return false;
        if(Busy||User.Hero.BackflipActive){Feedback="Finish the current move";return false;}
        if(Cooldown>0){Feedback="Synergy cooling down";return false;}
        if(Definition==null||Definition.Effect==null){Feedback="No synergy for this pair";return false;}
        if(!User.IsEquipped(Definition.PowerA)||!User.IsEquipped(Definition.PowerB)){Feedback="Equip both powers";return false;}
        Captive=null;
        if(!Definition.Effect.CanBegin(this)){Feedback="No valid target";return false;}
        User.Release(false);
        affected.Clear();suspended.Clear();
        Cooldown=Definition.Cooldown;Busy=true;deadline=Time.time+Mathf.Max(8,Definition.Duration+4);
        Feedback=Definition.DisplayName;
        action=StartCoroutine(Perform(Definition.Effect));
        User.RecordUse(CreditId);
        return true;
    }
    /// Per-power stats id for this synergy's hits and activations.
    public string CreditId{get{if(creditFor!=Definition){creditFor=Definition;creditId=Definition!=null?"synergy:"+Definition.Id:null;}return creditId;}}
    PowerSynergyDefinition creditFor;string creditId;
    IEnumerator Perform(SynergyEffect effect)
    {
        var steps=new Stack<IEnumerator>();steps.Push(effect.Execute(this));
        while(steps.Count>0)
        {
            if(Time.timeScale==0){yield return null;continue;}
            object next=null;bool moved=false;Exception error=null;
            try{using(User.Credit(CreditId)){moved=steps.Peek().MoveNext();if(moved)next=steps.Peek().Current;}}
            catch(Exception e){error=e;}
            if(error!=null)
            {
                Feedback="Synergy interrupted";Debug.LogException(error);Cancel();yield break;
            }
            if(!moved){(steps.Pop() as IDisposable)?.Dispose();continue;}
            if(next is IEnumerator nested)steps.Push(nested);else yield return next;
        }
        suspended.Clear();
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
    public void Cancel()
    {
        if(action!=null)StopCoroutine(action);
        action=null;Busy=false;DrivesMotion=false;
        foreach(var hold in suspended)if(hold!=null&&hold.enabled)hold.Finish();
        suspended.Clear();Captive=null;
        if(User!=null)User.Hero.ResetMotion();
        if(view!=null&&fovUntil>0)view.fieldOfView=baseFov;
        PowerVfx.Instance?.HideBeam();   // a beam synergy (Solar Flare / Eclipse Beam) cut short must not leave its beam drawn
        fovUntil=0;
    }
    void OnDisable(){Cancel();}
    void OnDestroy(){if(WorldSession.Instance!=null)WorldSession.Instance.PlayerDamaged-=OnDamage;}
}
