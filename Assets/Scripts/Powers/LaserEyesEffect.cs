using System.Collections.Generic;
using UnityEngine;

/// LASER EYES — a held beam (PowerActivation.Channeled). While the fire button is held, PowerUser drains DrainPerSecond energy
/// and calls Sustain every frame. The beam follows the crosshair (PowerUser.FindTarget, the same aim as Fire/Ice) and deals the
/// power's Damage as DAMAGE PER SECOND to whatever it touches, applied in TickSeconds steps. Only the first tick on each target
/// per channel counts as an assault (Heat); later ticks do not. Props take Force as impulse per second and breakable damage.
[CreateAssetMenu(menuName = "Overpowered/Effects/Channeled beam")]
public sealed class LaserEyesEffect : ChanneledEffect
{
    [Header("Beam")]
    public float TickSeconds = .1f, BeamWidth = .024f, MuzzleForward = .8f;
    public float EyeSeparation = .12f, HaloWidth = .045f, ImpactRadius = .12f, SparkInterval = .14f;
    public int HitParticles = 2;
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var state = BeamState.For(user); state.Begin(user);
        state.DamagePerSecond = user.RollDamage(user.Stats(power).Damage);
        return true;
    }
    public override void Sustain(PowerUser user, PowerRuntime power, float dt)
    {
        var state = BeamState.For(user); var stats = user.Stats(power);
        Vector3 origin=user.AimOrigin,direction=user.AimDirection;
        bool hit = user.FindTarget(stats.Range, out RaycastHit target);
        Vector3 to = hit ? target.point : origin + direction * stats.Range;
        var camera=Camera.main;var rig=camera!=null?camera.GetComponent<ThirdPersonCamera>():null;
        bool firstPerson=rig!=null&&rig.isActiveAndEnabled&&rig.FirstPerson;
        Vector3 left,right,merge;float nearScale=1f;
        if(firstPerson)
        {
            float distance=Vector3.Distance(origin,to);
            nearScale=Mathf.Clamp(distance/2f,.2f,1f);
            float ahead=Mathf.Min(MuzzleForward,distance*.2f);
            float drop=Mathf.Min(.18f,distance*.12f);
            left=origin+direction*ahead-camera.transform.up*drop+camera.transform.right*Mathf.Min(.12f,distance*.08f);
            merge=origin+direction*Mathf.Min(MuzzleForward*2f,distance*.75f);
            right=left;
        }
        else
        {
            Vector3 head=state.Head!=null?state.Head.position:user.transform.position+Vector3.up*1.62f;
            Vector3 center=head+user.transform.up*.08f+direction*.18f;
            Vector3 eyeOffset=user.transform.right*(EyeSeparation*.5f);
            left=center-eyeOffset;right=center+eyeOffset;merge=to;
        }
        var viewRight=camera!=null?camera.transform.right:user.transform.right;
        var viewUp=camera!=null?camera.transform.up:user.transform.up;
        float impactRadius=firstPerson?Mathf.Min(ImpactRadius,Vector3.Distance(origin,to)*.12f):ImpactRadius;
        PowerVfx.Get().ShowLaser(left,right,merge,to,firstPerson,hit,BeamWidth*nearScale,HaloWidth*nearScale,impactRadius,viewRight,viewUp);
        state.LastHitPoint=to;state.HasHit=hit;state.VisualLeft=left;state.VisualRight=right;
        if(hit&&Time.time>=state.NextSparkAt)
        {
            FeelDirector.Instance?.Particles.Burst(target.point,power.Definition.PaletteColor,HitParticles);
            state.NextSparkAt=Time.time+SparkInterval;state.ImpactBursts++;
        }
        state.Held += dt; state.Pending += dt;
        if (state.Pending < TickSeconds) return;
        float seconds = state.Pending; state.Pending = 0f; state.Ticks++;
        if (!hit) { state.LastTarget = null; return; }
        // RUN EVOLUTION: OVERLOAD ramps the beam up to +40% over the first 3 seconds of a continuous channel.
        float dps = state.DamagePerSecond;
        if (RunMechanics.Has(RunMechanic.LaserOverload)) dps *= 1f + .4f * Mathf.Clamp01(state.Held / 3f);
        var npc = target.collider.GetComponentInParent<CityNpc>();
        if (npc != null)
        {
            state.LastTarget = npc;
            if (npc.Dead) return;
            float damage = dps * seconds;
            npc.Damage(damage, user, state.Struck.Add(npc));
            state.Dealt += damage;
            // RUN EVOLUTION: PIERCING GAZE continues through the first target to one hostile behind it.
            if (RunMechanics.Has(RunMechanic.LaserPierce))
            {
                Vector3 behind = target.point + direction * .15f;
                if (Physics.Raycast(behind, direction, out RaycastHit second, stats.Range, ~0, QueryTriggerInteraction.Ignore))
                {
                    var other = second.collider.GetComponentInParent<CityNpc>();
                    if (other != null && other != npc && !other.Dead && other.Hostile)
                    {
                        float pierced = dps * seconds * .6f;
                        other.Damage(pierced, user, state.Struck.Add(other));
                        state.Dealt += pierced;
                    }
                }
            }
            return;
        }
        state.LastTarget = null;
        target.collider.GetComponentInParent<MissionTarget>()?.Hit(state.DamagePerSecond * seconds, stats.Force * seconds, user);
        var body = target.rigidbody;
        if (body != null && !body.isKinematic) body.AddForceAtPosition(user.AimDirection * stats.Force * seconds, target.point, ForceMode.Impulse);
        var prop = target.collider.GetComponentInParent<BreakableProp>();
        if (prop != null) { prop.TakeDamage(state.DamagePerSecond * seconds, user); state.Dealt += state.DamagePerSecond * seconds; }
    }
    public override void Stop(PowerUser user, PowerRuntime power)
    {
        PowerVfx.Get().HideBeam();
        var state=BeamState.For(user);state.Pending = 0f;state.HasHit=false;
    }
}
/// Per-player beam bookkeeping (the effect asset is shared data, so live state lives on the player). Also read by tests.
public sealed class BeamState : MonoBehaviour
{
    public float DamagePerSecond;
    public float Held, Pending, Dealt, NextSparkAt; public int Ticks, Channels, ImpactBursts;
    public CityNpc LastTarget;
    public Transform Head;
    public Vector3 LastHitPoint, VisualLeft, VisualRight;
    public bool HasHit;
    public readonly HashSet<CityNpc> Struck = new HashSet<CityNpc>();
    public static BeamState For(Component owner)
    {
        var s = owner.GetComponent<BeamState>(); if (s == null) s = owner.gameObject.AddComponent<BeamState>(); return s;
    }
    public void Begin(PowerUser user)
    {
        Held = Pending = Dealt = NextSparkAt = 0f;ImpactBursts=0;HasHit=false;
        Struck.Clear(); LastTarget = null; Channels++;
        var presentation=user.Hero!=null?user.Hero.GetComponent<HumanoidPresentation>():null;
        Head=presentation!=null&&presentation.Animator!=null?presentation.Animator.GetBoneTransform(HumanBodyBones.Head):null;
    }
}
