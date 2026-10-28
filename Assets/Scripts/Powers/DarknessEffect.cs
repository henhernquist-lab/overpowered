using UnityEngine;
using System.Collections.Generic;

/// DARKNESS — one aimed hostile, plus the nearest visible hostile near it. The status owns control and aura pulses.
[CreateAssetMenu(menuName = "Overpowered/Effects/Shadow tendrils")]
public sealed class DarknessEffect : PowerEffect
{
    [Header("Targeting")]
    public float SecondaryRadius = 7f;
    [Header("Movement control")]
    public float InitialRootSeconds = 1.1f;
    [Range(0f,1f)] public float SlowSpeedFactor = .4f;
    [Header("Shadow aura")]
    public float AuraRadius = 4.5f, AuraTickSeconds = .75f, AuraTickDamage = 5f;
    static readonly Collider[] nearby = new Collider[64];
    static readonly RaycastHit[] sight = new RaycastHit[32];
    public static CityNpc LastPrimary { get; private set; }
    public static CityNpc LastSecondary { get; private set; }
    public static Vector3 LastPrimaryVisualOrigin { get; private set; }
    public static bool LastPrimaryWasFirstPerson { get; private set; }
    [Header("Shadow tendrils (presentation)")]
    public int Tendrils = 4;
    public float TendrilHeight = 1.3f, TendrilSpread = .75f, TendrilWidth = .07f, TendrilJitter = .12f;
    [Tooltip("Tendrils are redrawn from the pooled lines this often while the root holds.")]
    public float RedrawSeconds = .3f;
    public int CastParticles = 10;
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var stats = user.Stats(power);
        if (!user.FindTarget(stats.Range, out RaycastHit hit)) return false;
        var npc = hit.collider.GetComponentInParent<CityNpc>();
        if (npc == null || npc.Dead || !npc.Hostile) { user.Message = "Aim at a hostile enemy."; return false; }
        LastPrimary=npc;
        LastSecondary=Secondary(user,npc,stats.Range);
        Apply(user,power,npc,stats);
        if(LastSecondary!=null)Apply(user,power,LastSecondary,stats);
        return true;
    }
    CityNpc Secondary(PowerUser user,CityNpc primary,float range)
    {
        Vector3 from=primary.transform.position+Vector3.up*1.1f;
        int count=Physics.OverlapSphereNonAlloc(from,SecondaryRadius,nearby,~0,QueryTriggerInteraction.Ignore);
        CityNpc best=null;float bestDistance=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            var candidate=nearby[i].GetComponentInParent<CityNpc>();
            if(candidate==null||candidate==primary||candidate.Dead||!candidate.Hostile)continue;
            Vector3 chest=candidate.transform.position+Vector3.up*1.1f;
            float distance=Vector3.Distance(from,chest);
            if(distance>=bestDistance||Vector3.Distance(user.AimOrigin,chest)>range||
               !Clear(from,chest,user)||!Clear(user.AimOrigin,chest,user))continue;
            best=candidate;bestDistance=distance;
        }
        return best;
    }
    static bool Clear(Vector3 a,Vector3 b,PowerUser user)
    {
        Vector3 delta=b-a;float distance=delta.magnitude;if(distance<.01f)return true;
        int count=Physics.RaycastNonAlloc(a,delta/distance,sight,distance,~0,QueryTriggerInteraction.Ignore);
        if(count==sight.Length)return false; // Saturated queries fail closed rather than seeing through a wall.
        for(int i=0;i<count;i++)
        {
            var collider=sight[i].collider;
            if(collider.GetComponentInParent<CityNpc>()!=null||collider.transform.root==user.transform)continue;
            return false;
        }
        return true;
    }
    void Apply(PowerUser user,PowerRuntime power,CityNpc npc,PowerStats stats)
    {
        // RUN EVOLUTION: UMBRAL GRIP lengthens the initial root and deepens the slow (never a permanent lock: both are
        // still clipped by the curse duration, and the slow floor leaves the enemy moving).
        int grip = RunMechanics.Stacks(RunMechanic.DarknessRoot);
        npc.Root(Mathf.Min(InitialRootSeconds + .5f * grip, stats.Duration));
        npc.Slow(Mathf.Max(.15f, SlowSpeedFactor - .08f * grip), stats.Duration);
        npc.Damage(user.RollDamage(stats.Damage),user);
        var color = power.Definition.PaletteColor;
        if (!npc.Dead)
        {
            ShadowCursed.Apply(npc,user,this,stats.Duration,stats.Damage/Mathf.Max(.01f,power.Definition.Damage),user.Crediting);
            RootedLook.Show(npc, this, color);
            // A brief aimed snap establishes the target; the leased body bands carry the held state.
            var camera=Camera.main;var rig=camera!=null?camera.GetComponent<ThirdPersonCamera>():null;
            bool first=rig!=null&&rig.isActiveAndEnabled&&rig.FirstPerson;
            // In first person, keep the activation snap local to the victim instead of drawing across the view.
            Vector3 origin=first?npc.transform.position+Vector3.up*.65f+camera.transform.right*.7f:user.AimOrigin;
            if(npc==LastSecondary&&LastPrimary!=null)origin=LastPrimary.transform.position+Vector3.up;
            if(npc==LastPrimary){LastPrimaryVisualOrigin=origin;LastPrimaryWasFirstPerson=first;}
            var snap=PowerVfx.Get().Arc(origin,npc.transform.position+Vector3.up,
                first?2:4,first?.015f:.045f,CityColor.UiMuted,first?.012f:.035f,.12f);
            if(snap!=null)snap.sharedMaterial=CityMaterials.GetVfx(CityColor.UiMuted);
        }
        FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * .15f, color, CastParticles);
    }
}
/// One timed aura per cursed NPC. Every pulse queries only the nearby hostile bodies, then uses the normal damage pipeline.
public sealed class ShadowCursed : MonoBehaviour
{
    CityNpc npc;PowerUser source;DarknessEffect settings;string credit;
    float endsAt,nextTick,damageScale;
    readonly HashSet<CityNpc> hitThisTick=new HashSet<CityNpc>();
    readonly Collider[] nearby=new Collider[128];
    public bool Active=>enabled&&npc!=null&&!npc.Dead&&Time.time<endsAt;
    public int Pulses { get; private set; }
    public int DamageEvents { get; private set; }
    public static ShadowCursed Apply(CityNpc target,PowerUser user,DarknessEffect effect,float duration,float scale,string sourceId)
    {
        var curse=target.GetComponent<ShadowCursed>();
        if(curse==null)curse=target.gameObject.AddComponent<ShadowCursed>();
        curse.npc=target;curse.source=user;curse.settings=effect;curse.credit=sourceId;
        curse.endsAt=Time.time+duration;curse.nextTick=Time.time+effect.AuraTickSeconds;
        curse.damageScale=scale;curse.enabled=true;
        return curse;
    }
    void Update()
    {
        if(!Active){enabled=false;return;}
        var world=WorldSession.Instance;
        if(world!=null&&world.Mode!=null&&world.Mode.Ended)return;
        if(Time.time<nextTick)return;
        nextTick=Time.time+Mathf.Max(.05f,settings.AuraTickSeconds);
        Pulses++;hitThisTick.Clear();
        StatusPowerVfx.Existing?.PulseShadow(npc,settings.AuraRadius);
        int count=Physics.OverlapSphereNonAlloc(npc.transform.position,settings.AuraRadius,nearby,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
        {
            var other=nearby[i].GetComponentInParent<CityNpc>();
            if(other==null||other==npc||other.Dead||!other.Hostile||!hitThisTick.Add(other))continue;
            // Damage is credited to Darkness; CityNpc applies the global difficulty multiplier once.
            float amount=source!=null?source.RollDamage(settings.AuraTickDamage*damageScale):settings.AuraTickDamage*damageScale;
            using(source!=null?source.Credit(credit):default)other.Damage(amount,source,false);
            DamageEvents++;
            Vector3 from=npc.transform.position+Vector3.up*.8f,to=other.transform.position+Vector3.up*.8f;
            PowerVfx.Get().Arc(from,to,2,.025f,CityColor.UiMuted,.025f,.15f);
        }
    }
    void OnDisable(){if(npc!=null&&StatusPowerVfx.Existing!=null)StatusPowerVfx.Existing.ReleaseRoot(npc);}
}
/// Visible curse: four closely wrapped, leased tendril bands continue through the slow portion.
public sealed class RootedLook : MonoBehaviour
{
    CityNpc npc; int slot=-1;
    public int Draws { get; private set; }
    public bool Showing => enabled && npc != null && !npc.Dead && (npc.GetComponent<ShadowCursed>()?.Active==true||npc.Rooted) && slot>=0;
    public static RootedLook Show(CityNpc target, DarknessEffect settings, CityColor color)
    {
        var look=target.GetComponent<RootedLook>();if(look==null)look=target.gameObject.AddComponent<RootedLook>();
        look.npc=target;look.enabled=true;
        if(look.slot<0){look.slot=StatusPowerVfx.Get().AcquireRoot(target);if(look.slot>=0)look.Draws++;}
        return look;
    }
    void Update()
    {
        if(npc==null||npc.Dead||npc.GetComponent<ShadowCursed>()?.Active!=true){enabled=false;return;}
        if(slot<0){slot=StatusPowerVfx.Get().AcquireRoot(npc);if(slot>=0)Draws++;}
    }
    void OnDisable()
    {
        if(slot>=0&&npc!=null&&StatusPowerVfx.Existing!=null)StatusPowerVfx.Existing.ReleaseRoot(npc,slot);
        slot=-1;
    }
}
