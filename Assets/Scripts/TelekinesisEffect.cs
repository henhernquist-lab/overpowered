using UnityEngine;
using UnityEngine.Rendering;
[CreateAssetMenu(menuName = "Overpowered/Effects/Telekinesis")]
public sealed class TelekinesisEffect : PowerEffect
{
    [Header("Directed throw")]
    public float MinimumThrowSpeed = 22f, MaximumThrowSpeed = 40f;
    [Tooltip("Applied after existing mass scaling, before one impact variance roll.")]
    public float ImpactDamageMultiplier = 5f / 7f;
    public float DamageReferenceMass = 45f, MinimumDamageScale = .5f, MaximumDamageScale = 3f;
    public float MomentumTransfer = .65f, MaxImpactImpulse = 2400f, ImpactLift = .3f;
    public float ImpactSeparationSeconds = .2f;
    public void Throw(PowerUser user, PowerRuntime power, Rigidbody body)
    {
        var stats=user.Stats(power);
        Vector3 destination=user.AimPoint(stats.Range,body);
        // Same crosshair target as Fire, converged from the actual prop. Compensate gravity and existing orbit velocity
        // with an impulse, never a teleport or a per-frame homing correction. Heavy props get a usable launch speed.
        float speed=Mathf.Clamp(stats.Force/body.mass,MinimumThrowSpeed,MaximumThrowSpeed);
        float seconds=Mathf.Max(Time.fixedDeltaTime,Vector3.Distance(body.worldCenterOfMass,destination)/speed);
        Vector3 velocity=(destination-body.worldCenterOfMass)/seconds;
        if(body.useGravity)velocity-=Physics.gravity*(seconds+Time.fixedDeltaTime)*.5f;
        Vector3 impulse=(velocity-body.linearVelocity)*body.mass;
        body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        body.AddForce(impulse,ForceMode.Impulse);
        var impact=body.GetComponent<ThrownProp>();if(impact==null)impact=body.gameObject.AddComponent<ThrownProp>();
        float scale=Mathf.Clamp(Mathf.Sqrt(body.mass/Mathf.Max(1,DamageReferenceMass)),MinimumDamageScale,MaximumDamageScale);
        impact.Initialize(user,stats.Damage*scale*ImpactDamageMultiplier,stats.Duration,this,impulse);
    }
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        if (!user.FindTarget(user.Stats(power).Range, out RaycastHit hit)) return false;
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic || body.mass > power.Definition.HoldMaxMass) { user.Message = "Aim at a movable prop."; return false; }
        user.Grab(body, power);
        return true;
    }
}

/// A fixed, session-local visual lease for the REAL held/thrown Rigidbody. Never moves the body or creates
/// materials/geometry per cast; PowerUser owns the physics and the release/impact transitions.
public sealed class TelekinesisPresentation : MonoBehaviour
{
    const int RingPoints=13, TrailPoints=8;
    readonly LineRenderer[] rings=new LineRenderer[2];
    readonly Vector3[] trailPoints=new Vector3[TrailPoints];
    LineRenderer tether,pulse,trail;
    Rigidbody held,thrown;
    float radius,nextTrail,trailUntil,impactUntil;
    CityColor color;
    int trailCount;
    public bool Holding=>held!=null;
    public bool Throwing=>thrown!=null;
    public int RendererCount=>tether==null?0:5;
    public int ImpactCount {get;private set;}
    public Vector3 LastImpactPoint {get;private set;}

    void Awake()
    {
        rings[0]=Line("Telekinesis orbit horizontal",RingPoints);
        rings[1]=Line("Telekinesis orbit vertical",RingPoints);
        tether=Line("Telekinesis tether",3);
        pulse=Line("Telekinesis connection pulse",2);
        trail=Line("Telekinesis real-body throw trail",TrailPoints);
    }
    LineRenderer Line(string name,int points)
    {
        var go=new GameObject(name);go.transform.SetParent(transform,false);
        var line=go.AddComponent<LineRenderer>();line.useWorldSpace=true;line.positionCount=points;
        line.numCapVertices=2;line.widthMultiplier=.025f;
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
        line.enabled=false;return line;
    }
    public void Grab(Rigidbody body,CityColor swatch)
    {
        held=body;thrown=null;color=swatch;trailCount=0;impactUntil=0;
        var collider=body.GetComponent<Collider>();
        radius=collider!=null?Mathf.Clamp(collider.bounds.extents.magnitude*.72f,.42f,1.15f):.45f;
        var material=CityMaterials.GetVfx(color);
        foreach(var ring in rings){ring.sharedMaterial=material;ring.widthMultiplier=.032f;ring.enabled=true;}
        tether.sharedMaterial=CityMaterials.GetVfx(CityColor.Cyan);
        pulse.sharedMaterial=CityMaterials.GetVfx(CityColor.Cream);
        tether.enabled=pulse.enabled=true;trail.enabled=false;
        FeelDirector.Instance?.Particles.Burst(body.worldCenterOfMass,color,5);
    }
    public void Release()
    {
        held=null;thrown=null;
        foreach(var ring in rings)ring.enabled=false;
        tether.enabled=pulse.enabled=trail.enabled=false;
    }
    public void Throw(Rigidbody body)
    {
        if(held!=body)return;
        Vector3 center=body.worldCenterOfMass;
        held=null;thrown=body;trailCount=0;nextTrail=0;trailUntil=Time.time+.65f;
        for(int i=0;i<TrailPoints;i++)trailPoints[i]=center;
        foreach(var ring in rings)ring.enabled=false;
        tether.enabled=pulse.enabled=false;
        trail.sharedMaterial=CityMaterials.GetVfx(color);trail.enabled=true;
        FeelDirector.Instance?.Particles.Burst(center,color,8);
    }
    public void Impact(Rigidbody body,Vector3 point)
    {
        if(thrown!=body)return;
        thrown=null;impactUntil=Time.time+.12f;
        ImpactCount++;LastImpactPoint=point;
        FeelDirector.Instance?.Particles.Burst(point,color,12);
        // A short cross at the real collision point, reusing the same pooled line renderers.
        rings[0].positionCount=2;rings[1].positionCount=2;
        rings[0].SetPosition(0,point+Vector3.left*.35f);rings[0].SetPosition(1,point+Vector3.right*.35f);
        rings[1].SetPosition(0,point+Vector3.up*.35f);rings[1].SetPosition(1,point+Vector3.down*.35f);
        rings[0].enabled=rings[1].enabled=true;
    }
    void LateUpdate()
    {
        if(held!=null)
        {
            Vector3 center=held.worldCenterOfMass;
            float a=Time.time*2.8f;
            float size=radius*(1f+.07f*Mathf.Sin(Time.time*10f));
            rings[0].positionCount=rings[1].positionCount=RingPoints;
            for(int i=0;i<RingPoints;i++)
            {
                float angle=a+i*(Mathf.PI*2f/(RingPoints-1));
                float c=Mathf.Cos(angle)*size,s=Mathf.Sin(angle)*size;
                rings[0].SetPosition(i,center+new Vector3(c,.12f,s));
                rings[1].SetPosition(i,center+new Vector3(c,s*.65f,s*.35f));
            }
            var camera=Camera.main;var rig=camera!=null?camera.GetComponent<ThirdPersonCamera>():null;
            bool first=rig!=null&&rig.isActiveAndEnabled&&rig.FirstPerson;
            Vector3 start=first?camera.transform.position+camera.transform.right*.3f-camera.transform.up*.2f+camera.transform.forward*.3f:
                transform.position+Vector3.up*1.25f+transform.right*.35f;
            Vector3 end=center+(first?camera.transform.right:transform.right)*(size*.65f);
            Vector3 middle=Vector3.Lerp(start,end,.5f)+Vector3.up*(.09f*Mathf.Sin(Time.time*7f));
            tether.SetPosition(0,start);tether.SetPosition(1,middle);tether.SetPosition(2,end);
            tether.widthMultiplier=first?.0045f:.018f;
            float t=Mathf.Repeat(Time.time*1.8f,1f);
            Vector3 p= t<.5f?Vector3.Lerp(start,middle,t*2f):Vector3.Lerp(middle,end,(t-.5f)*2f);
            Vector3 direction=(end-start).normalized;
            pulse.SetPosition(0,p-direction*.055f);pulse.SetPosition(1,p+direction*.055f);
            pulse.widthMultiplier=first?.012f:.028f;
        }
        if(thrown!=null)
        {
            if(Time.time>trailUntil){thrown=null;trail.enabled=false;}
            else if(Time.time>=nextTrail)
            {
                for(int i=TrailPoints-1;i>0;i--)trailPoints[i]=trailPoints[i-1];
                trailPoints[0]=thrown.worldCenterOfMass;trailCount=Mathf.Min(TrailPoints,trailCount+1);
                trail.positionCount=Mathf.Max(2,trailCount);
                for(int i=0;i<trail.positionCount;i++)trail.SetPosition(i,trailPoints[i]);
                trail.widthMultiplier=.11f*Mathf.Clamp01((trailUntil-Time.time)/.65f);
                nextTrail=Time.time+.025f;
            }
        }
        if(thrown==null&&held==null)
        {
            if(Time.time>=impactUntil){rings[0].enabled=rings[1].enabled=false;trail.enabled=false;}
        }
    }
    void OnDisable(){Release();}
}
