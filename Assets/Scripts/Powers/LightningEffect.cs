using System.Collections.Generic;
using UnityEngine;

/// LIGHTNING — chain attack. The first target is the aimed NPC; if the crosshair lands on anything else, the bolt jumps from
/// that point to the nearest eligible NPC within ArcRadius. From each struck NPC it arcs to the nearest not-yet-struck ENEMY
/// (CityNpc.Hostile, so a hero never chains into civilians or friendly police) within ArcRadius and with clear line of sight,
/// up to MaxArcs jumps. Damage starts at the power's Damage and is multiplied by Falloff per jump.
[CreateAssetMenu(menuName = "Overpowered/Effects/Chain lightning")]
public sealed class LightningEffect : PowerEffect
{
    [Header("Chain")]
    public int MaxArcs = 5;
    public float ArcRadius = 8f, Falloff = .85f;
    [Tooltip("Arcs only jump to NPCs hostile to the player.")] public bool EnemiesOnly = true;
    [Header("Presentation")]
    public float ArcSeconds = .22f, ArcWidth = .08f, ArcJitter = .35f;
    public int ArcSegments = 6, HitParticles = 8;
    static readonly Collider[] nearby = new Collider[64];
    static readonly RaycastHit[] sight = new RaycastHit[16];
    /// Targets struck by the most recent cast, in chain order (tests and presentation read it).
    public static readonly List<CityNpc> LastChain = new List<CityNpc>();
    public static readonly List<float> LastDamages = new List<float>();
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var stats = user.Stats(power);
        if (!user.FindTarget(stats.Range, out RaycastHit hit)) return false;
        var first = hit.collider.GetComponentInParent<CityNpc>();
        if (first != null && first.Dead) first = null;
        if (first == null) first = Next(hit.point, null, null, user);
        if (first == null) { user.Message = "No one in reach of the bolt."; return false; }
        LastChain.Clear(); LastDamages.Clear();
        var struck = new HashSet<CityNpc>();
        var vfx = PowerVfx.Get(); var color = power.Definition.PaletteColor;
        Vector3 from = user.AimOrigin + user.AimDirection * .5f;
        float damage = stats.Damage;
        var current = first;
        for (int jump = 0; current != null && jump <= Mathf.Max(0, MaxArcs); jump++)
        {
            Vector3 chest = current.transform.position + Vector3.up * 1.1f;
            vfx.Arc(from, chest, ArcSegments, ArcJitter, color, ArcWidth, ArcSeconds);
            struck.Add(current); LastChain.Add(current); LastDamages.Add(damage);
            current.Damage(damage, user);
            FeelDirector.Instance?.Particles.Burst(chest, color, HitParticles);
            from = chest; damage *= Falloff;
            current = jump < MaxArcs ? Next(current.transform.position, current, struck, user) : null;
        }
        FeelDirector.Impact(first.transform.position + Vector3.up, stats.Force, stats.Damage, LastChain.Count);
        user.Message = "Lightning: " + LastChain.Count + " struck";
        return true;
    }
    CityNpc Next(Vector3 from, CityNpc exclude, HashSet<CityNpc> struck, PowerUser user)
    {
        int count = Physics.OverlapSphereNonAlloc(from, ArcRadius, nearby, ~0, QueryTriggerInteraction.Ignore);
        CityNpc best = null; float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var npc = nearby[i].GetComponentInParent<CityNpc>();
            if (npc == null || npc == exclude || npc.Dead || (struck != null && struck.Contains(npc))) continue;
            if (EnemiesOnly && !npc.Hostile) continue;
            float d = Vector3.Distance(from, npc.transform.position);
            if (d >= bestDistance || !Clear(from + Vector3.up * 1.1f, npc.transform.position + Vector3.up * 1.1f, user)) continue;
            best = npc; bestDistance = d;
        }
        return best;
    }
    /// Solid static geometry blocks an arc; NPCs, props and the player do not (same rule as CombatImpact's barrier test).
    static bool Clear(Vector3 a, Vector3 b, PowerUser user)
    {
        Vector3 delta = b - a; float length = delta.magnitude; if (length < .01f) return true;
        int count = Physics.RaycastNonAlloc(a, delta / length, sight, length, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = sight[i].collider;
            if (c.attachedRigidbody != null || c.GetComponentInParent<CityNpc>() != null || c.transform.root == user.transform) continue;
            return false;
        }
        return true;
    }
}
