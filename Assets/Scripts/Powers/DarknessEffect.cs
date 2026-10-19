using UnityEngine;

/// DARKNESS — one aimed hostile, plus the nearest visible hostile near it. The status owns control and aura pulses.
[CreateAssetMenu(menuName = "Overpowered/Effects/Shadow tendrils")]
public sealed class DarknessEffect : PowerEffect
{
    [Header("Targeting")]
    public float SecondaryRadius = 7f;
    static readonly Collider[] nearby = new Collider[64];
    static readonly RaycastHit[] sight = new RaycastHit[32];
    public static CityNpc LastPrimary { get; private set; }
    public static CityNpc LastSecondary { get; private set; }
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
        npc.Root(stats.Duration);
        npc.Damage(user.RollDamage(stats.Damage),user);
        var color = power.Definition.PaletteColor;
        if (!npc.Dead)
        {
            RootedLook.Show(npc, this, color);
            // A brief aimed snap establishes the target; the leased body bands carry the held state.
            var camera=Camera.main;var rig=camera!=null?camera.GetComponent<ThirdPersonCamera>():null;
            bool first=rig!=null&&rig.isActiveAndEnabled&&rig.FirstPerson;
            Vector3 origin=first?camera.transform.position+camera.transform.forward*2f+
                camera.transform.right*.2f-camera.transform.up*.2f:user.AimOrigin;
            if(npc==LastSecondary&&LastPrimary!=null)origin=LastPrimary.transform.position+Vector3.up;
            var snap=PowerVfx.Get().Arc(origin,npc.transform.position+Vector3.up,
                first?2:4,first?.015f:.045f,CityColor.UiMuted,first?.012f:.035f,.12f);
            if(snap!=null)snap.sharedMaterial=CityMaterials.GetVfx(CityColor.UiMuted);
        }
        FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * .15f, color, CastParticles);
    }
}
/// Visible root: four closely wrapped, leased tendril bands. The gameplay root is unchanged.
public sealed class RootedLook : MonoBehaviour
{
    CityNpc npc; int slot=-1;
    public int Draws { get; private set; }
    public bool Showing => enabled && npc != null && !npc.Dead && npc.Rooted && slot>=0;
    public static RootedLook Show(CityNpc target, DarknessEffect settings, CityColor color)
    {
        var look=target.GetComponent<RootedLook>();if(look==null)look=target.gameObject.AddComponent<RootedLook>();
        look.npc=target;look.enabled=true;
        if(look.slot<0){look.slot=StatusPowerVfx.Get().AcquireRoot(target);if(look.slot>=0)look.Draws++;}
        return look;
    }
    void Update()
    {
        if(npc==null||npc.Dead||!npc.Rooted){enabled=false;return;}
        if(slot<0){slot=StatusPowerVfx.Get().AcquireRoot(npc);if(slot>=0)Draws++;}
    }
    void OnDisable()
    {
        if(slot>=0&&npc!=null&&StatusPowerVfx.Existing!=null)StatusPowerVfx.Existing.ReleaseRoot(npc,slot);
        slot=-1;
    }
}
