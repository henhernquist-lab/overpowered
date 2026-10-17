using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Projectile")]
public sealed class FireBlastEffect : PowerEffect
{
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var d = power.Definition;
        // Resolve before creating the sphere (otherwise the aim query can hit its own projectile).
        Vector3 direction=user.AimDirection,origin=user.AimOrigin;
        float radius=d.ProjectileSize*.5f;
        foreach(var hit in Physics.OverlapSphere(origin,radius,~0,QueryTriggerInteraction.Ignore))
            if(hit.transform.root!=user.transform)return false;
        float offset=d.OriginOffset;
        foreach(var hit in Physics.SphereCastAll(origin,radius,direction,offset,~0,QueryTriggerInteraction.Ignore))
            if(hit.transform.root!=user.transform)offset=Mathf.Min(offset,Mathf.Max(0,hit.distance-.01f));
        var pool=FireProjectilePool.Instance??FireProjectilePool.Create(WorldSession.Instance);
        return pool.Launch(user,power,origin+direction*offset,direction);
    }
}
public sealed class PowerProjectile : MonoBehaviour
{
    PowerUser owner; PowerDefinition definition; PowerStats stats; FireProjectilePool pool;
    Rigidbody body; SphereCollider sphere; FireProjectileVisual visual;
    bool exploded,returnPending;float expires,emberAt,launchedAt;string credit;
    readonly RaycastHit[] visualHits=new RaycastHit[8];
    public FireProjectileVisual Visual=>visual;
    public Vector3 LaunchDirection {get;private set;}
    public Vector3 LastImpactPoint {get;private set;}
    public bool Exploded=>exploded;
    public void Bind(FireProjectilePool parent,FireProjectileVisual presentation)
    {pool=parent;visual=presentation;body=GetComponent<Rigidbody>();sphere=GetComponent<SphereCollider>();}
    public void Initialize(PowerUser user,PowerRuntime power,Vector3 direction)
    {
        owner=user;definition=power.Definition;stats=user.Stats(power);stats.Damage=user.RollDamage(stats.Damage);credit=user.Crediting;
        LaunchDirection=direction;exploded=false;returnPending=false;
        expires=Time.time+stats.Duration;emberAt=Time.time+.1f;launchedAt=Time.time;
        sphere.radius=definition.ProjectileSize*.5f;sphere.enabled=true;
        visual.SetSize(definition.ProjectileSize);
        var camera=Camera.main;var rig=camera!=null?camera.GetComponent<ThirdPersonCamera>():null;
        visual.SetFirstPerson(rig!=null&&rig.FirstPerson);
        body.linearVelocity=direction*definition.ProjectileSpeed;
        UpdatePresentation();
    }
    void Update()
    {
        if(returnPending){Return();return;}
        if(Time.time>=expires){Return();return;}
        if(Time.time<emberAt)return;
        emberAt=Time.time+.14f;
        FeelDirector.Instance?.Particles?.Burst(transform.position-transform.forward*.18f,CityColor.Red,1);
    }
    void FixedUpdate()
    {
        if(!exploded&&body.linearVelocity.sqrMagnitude>.01f)
        {
            body.MoveRotation(Quaternion.LookRotation(body.linearVelocity.normalized));
            UpdatePresentation();
        }
    }
    void UpdatePresentation()
    {
        float clearance=.55f;
        int count=Physics.RaycastNonAlloc(transform.position,body.linearVelocity.normalized,visualHits,.55f,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
            if(visualHits[i].collider!=sphere&&visualHits[i].collider.transform.root!=owner.transform)
                clearance=Mathf.Min(clearance,visualHits[i].distance);
        visual.Present(Time.time-launchedAt,Mathf.Clamp((clearance-.02f)/.45f,.3f,1f));
    }
    void OnCollisionEnter(Collision collision)
    {
        if (exploded) return; exploded = true;
        using (owner.Credit(credit)) CombatImpact.Blast(owner, transform.position, stats.Radius, stats.Force, stats.Damage, definition.UpwardForce,stats.Duration);
        LastImpactPoint=collision.contactCount>0?collision.GetContact(0).point:transform.position;
        Vector3 normal=collision.contactCount>0?collision.GetContact(0).normal:-transform.forward;
        pool.Impact(LastImpactPoint,normal,definition.Light);
        // Defer deactivation until Update so collision probes on the target and projectile see this same physics event.
        sphere.enabled=false;body.linearVelocity=Vector3.zero;visual.gameObject.SetActive(false);returnPending=true;
    }
    void Return()
    {
        returnPending=false;WorldLightingDirector.Instance?.ReleasePower(transform);
        owner=null;credit=null;body.linearVelocity=Vector3.zero;gameObject.SetActive(false);pool.Recycled++;
    }
}
