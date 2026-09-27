using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// VOID GRASP (Darkness + Telekinesis): opens a dark singularity at the crosshair point and pulls up to MaxTargets hostile NPCs
/// within Radius into it through the existing SynergySuspension physics handoff (spring force, like Orbit Throw), for Duration
/// seconds; then it collapses — the shared SynergyRunner.Impact blast — and every survivor is left rooted (CityNpc.Root) for
/// FreezeSeconds. Needs at least one enemy in reach (a refused cast costs no cooldown).
[CreateAssetMenu(menuName = "Overpowered/Forge/Effects/Void grasp")]
public sealed class VoidGraspEffect : SynergyEffect
{
    public float CoreHeight = 1.2f, TetherWidth = .06f, TetherInterval = .12f;
    /// Enemies pulled by the most recent cast (tests and presentation read it).
    public static readonly List<CityNpc> LastPulled = new List<CityNpc>();
    public override bool CanBegin(SynergyRunner r)
    {
        if (!r.User.FindTarget(r.Definition.Range, out var hit)) return false;
        r.Target = hit.point + Vector3.up * CoreHeight;
        return Gather(r, r.Target, null) > 0;
    }
    int Gather(SynergyRunner r, Vector3 core, List<CityNpc> into)
    {
        int count = Physics.OverlapSphereNonAlloc(core, r.Definition.Radius, r.Hits), found = 0;
        var seen = new HashSet<CityNpc>();
        for (int i = 0; i < count && found < Mathf.Max(1, r.Definition.MaxTargets); i++)
        {
            var npc = r.Hits[i].GetComponentInParent<CityNpc>();
            if (npc == null || npc.Dead || !npc.Hostile || !seen.Add(npc)) continue;
            into?.Add(npc); found++;
        }
        return found;
    }
    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition; var vfx = PowerVfx.Get(); Vector3 core = r.Target;
        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        LastPulled.Clear(); Gather(r, core, LastPulled);
        var bodies = new List<Rigidbody>(); var holds = new List<SynergySuspension>();
        foreach (var npc in LastPulled)
        {
            var suspension = npc.GetComponent<SynergySuspension>() ?? npc.gameObject.AddComponent<SynergySuspension>();
            bodies.Add(suspension.Begin(npc)); holds.Add(suspension);
        }
        float elapsed = 0, nextFx = 0;
        while (elapsed < d.Duration)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i]; if (body == null) continue;
                body.AddForce((core - body.position) * d.Spring - body.linearVelocity * d.Damping, ForceMode.Acceleration);
            }
            if (elapsed >= nextFx)
            {
                r.Vfx.Burst(core, d);
                for (int i = 0; i < bodies.Count; i++) if (bodies[i] != null) vfx.Arc(bodies[i].position, core, 4, .2f, d.Primary, TetherWidth, TetherInterval + .04f);
                nextFx = elapsed + TetherInterval;
            }
            elapsed += Time.fixedDeltaTime; yield return SynergyRunner.FixedStep;
        }
        // Collapse: one shared blast at the core, then survivors fall back to navigation, rooted in place.
        for (int i = 0; i < holds.Count; i++) if (holds[i] != null) { if (bodies[i] != null) bodies[i].useGravity = true; holds[i].Release(); }
        r.Impact(core);
        foreach (var npc in LastPulled) if (npc != null && !npc.Dead) npc.Root(d.FreezeSeconds);
    }
}
