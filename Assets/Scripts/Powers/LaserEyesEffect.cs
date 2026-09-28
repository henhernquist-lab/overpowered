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
    public float TickSeconds = .1f, BeamWidth = .07f, MuzzleForward = .3f;
    public int HitParticles = 3;
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        BeamState.For(user).Begin();
        return true;
    }
    public override void Sustain(PowerUser user, PowerRuntime power, float dt)
    {
        var state = BeamState.For(user); var stats = user.Stats(power);
        Vector3 from = user.AimOrigin + user.AimDirection * MuzzleForward;
        bool hit = user.FindTarget(stats.Range, out RaycastHit target);
        Vector3 to = hit ? target.point : user.AimOrigin + user.AimDirection * stats.Range;
        PowerVfx.Get().ShowBeam(from, to, power.Definition.PaletteColor, BeamWidth);
        state.Held += dt; state.Pending += dt;
        if (state.Pending < TickSeconds) return;
        float seconds = state.Pending; state.Pending = 0f; state.Ticks++;
        if (!hit) { state.LastTarget = null; return; }
        var npc = target.collider.GetComponentInParent<CityNpc>();
        if (npc != null)
        {
            state.LastTarget = npc;
            if (npc.Dead) return;
            float damage = stats.Damage * seconds;
            npc.Damage(damage, user, state.Struck.Add(npc));
            state.Dealt += damage;
            FeelDirector.Instance?.Particles.Burst(target.point, power.Definition.PaletteColor, HitParticles);
            return;
        }
        state.LastTarget = null;
        target.collider.GetComponentInParent<MissionTarget>()?.Hit(stats.Damage * seconds, stats.Force * seconds, user);
        var body = target.rigidbody;
        if (body != null && !body.isKinematic) body.AddForceAtPosition(user.AimDirection * stats.Force * seconds, target.point, ForceMode.Impulse);
        var prop = target.collider.GetComponentInParent<BreakableProp>();
        if (prop != null) { prop.TakeDamage(stats.Damage * seconds, user); state.Dealt += stats.Damage * seconds; }
    }
    public override void Stop(PowerUser user, PowerRuntime power)
    {
        PowerVfx.Get().HideBeam();
        BeamState.For(user).Pending = 0f;
    }
}
/// Per-player beam bookkeeping (the effect asset is shared data, so live state lives on the player). Also read by tests.
public sealed class BeamState : MonoBehaviour
{
    public float Held, Pending, Dealt; public int Ticks, Channels;
    public CityNpc LastTarget;
    public readonly HashSet<CityNpc> Struck = new HashSet<CityNpc>();
    public static BeamState For(Component owner)
    {
        var s = owner.GetComponent<BeamState>(); if (s == null) s = owner.gameObject.AddComponent<BeamState>(); return s;
    }
    public void Begin() { Held = Pending = Dealt = 0f; Struck.Clear(); LastTarget = null; Channels++; }
}
