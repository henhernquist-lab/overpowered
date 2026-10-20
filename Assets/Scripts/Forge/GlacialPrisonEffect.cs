using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

/// GLACIAL PRISON (Primary: Ice) — localized battlefield lockdown. Erupts a ring of jagged ice pillars around
/// a targeted enemy or area, trapping up to MaxTargets nearby hostiles inside the formation for Duration before
/// shattering for a second damage burst. Uses NavMesh-safe movement control (Root + initial Freeze + Slow) and
/// collider-free pooled pillars so it never modifies NavMeshes, traps the player, or softlocks missions.
[CreateAssetMenu(menuName = "Overpowered/Forge/Signature Effects/Glacial Prison")]
public sealed class GlacialPrisonEffect : SynergyEffect
{
    public float RingRadius = 3.2f;
    public float RiseSeconds = .22f;
    public float EruptionDamageFraction = .45f;
    public float ShatterDamageFraction = .55f;
    public float PostSlowFactor = .25f;
    public float PostSlowSeconds = 1.2f;

    public static readonly List<CityNpc> LastImprisoned = new List<CityNpc>();
    public static int LastShatterHits { get; private set; }
    static readonly RaycastHit[] barriers = new RaycastHit[16];

    public override bool CanBegin(SynergyRunner r)
    {
        if (r.User == null) return false;
        Vector3 point;
        if (r.User.FindTarget(r.Definition.Range, out var hit))
        {
            var npc = hit.collider.GetComponentInParent<CityNpc>();
            point = npc != null ? npc.transform.position : hit.point;
        }
        else if (!r.GroundTarget(true, out point))
        {
            return false;
        }

        if (Physics.Raycast(point + Vector3.up * 2.5f, Vector3.down, out var groundHit, 12f, ~0, QueryTriggerInteraction.Ignore))
            point = groundHit.point;
        if (NavMesh.SamplePosition(point, out var navHit, 4f, NavMesh.AllAreas))
            point = navHit.position;

        r.Target = point;
        return GatherHostiles(r, point, null) > 0;
    }

    int GatherHostiles(SynergyRunner r, Vector3 center, List<CityNpc> results)
    {
        var d = r.Definition;
        int count = Physics.OverlapSphereNonAlloc(center + Vector3.up * .8f, d.Radius, r.Hits, ~0, QueryTriggerInteraction.Ignore);
        int max = Mathf.Max(1, d.MaxTargets);
        var candidates = ListPool<CityNpc>.Get();
        var seen = HashSetPool<CityNpc>.Get();
        try
        {
            for (int i = 0; i < count; i++)
            {
                var npc = r.Hits[i].GetComponentInParent<CityNpc>();
                if (npc == null || npc.Dead || !npc.Hostile || !seen.Add(npc)) continue;
                if (Mathf.Abs(npc.transform.position.y - center.y) > 3f) continue;
                if (!ClearLine(center + Vector3.up * 1f, npc.transform.position + Vector3.up * 1f, r.transform)) continue;
                candidates.Add(npc);
            }
            candidates.Sort((a, b) =>
                (a.transform.position - center).sqrMagnitude.CompareTo((b.transform.position - center).sqrMagnitude));
            int take = Mathf.Min(max, candidates.Count);
            for (int i = 0; i < take; i++) results?.Add(candidates[i]);
            return take;
        }
        finally
        {
            ListPool<CityNpc>.Release(candidates);
            HashSetPool<CityNpc>.Release(seen);
        }
    }

    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition;
        Vector3 center = r.Target;
        LastImprisoned.Clear();
        LastShatterHits = 0;

        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        r.KickCamera();
        GatherHostiles(r, center, LastImprisoned);

        var sigVfx = SignatureVfx.Get();
        var powerVfx = PowerVfx.Get();
        float duration = Mathf.Max(.5f, d.Duration);
        sigVfx.ShowGlacialPrison(center, RingRadius, duration);
        r.Vfx.Burst(center + Vector3.up * .2f, d);
        FeelDirector.Instance?.Particles.Burst(center + Vector3.up * .6f, CityColor.Cyan, 16);
        AudioDirector.Instance?.Play(AudioCue.Destruction, center);

        float eruptionDamage = r.ActivationDamage * EruptionDamageFraction;
        float shatterDamage = r.ActivationDamage * ShatterDamageFraction;
        float maxContainRadius = RingRadius * .72f;

        for (int i = 0; i < LastImprisoned.Count; i++)
        {
            var npc = LastImprisoned[i];
            if (npc == null || npc.Dead) continue;

            ClampInsideRing(npc, center, maxContainRadius);
            npc.Freeze(Mathf.Max(.4f, d.FreezeSeconds), true);
            npc.Root(duration);
            npc.Slow(PostSlowFactor, duration + PostSlowSeconds);
            npc.Damage(eruptionDamage, r.User, true, d.Force * .5f);
        }

        try
        {
            float elapsed = 0f;
            float nextArc = 0f;
            while (elapsed < duration)
            {
                elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
                sigVfx.UpdateGlacialPrison(center, RingRadius, elapsed / Mathf.Max(.05f, RiseSeconds));

                for (int i = 0; i < LastImprisoned.Count; i++)
                {
                    var npc = LastImprisoned[i];
                    if (npc == null || npc.Dead) continue;
                    ClampInsideRing(npc, center, maxContainRadius);
                }

                if (elapsed >= nextArc)
                {
                    for (int i = 0; i < LastImprisoned.Count; i++)
                    {
                        var npc = LastImprisoned[i];
                        if (npc == null || npc.Dead) continue;
                        float angle = (i * Mathf.PI * 2f / Mathf.Max(1, LastImprisoned.Count)) + elapsed * 2f;
                        Vector3 rim = center + new Vector3(Mathf.Cos(angle) * RingRadius, 1.1f, Mathf.Sin(angle) * RingRadius);
                        powerVfx.Arc(rim, npc.transform.position + Vector3.up * 1f, 3, .12f, CityColor.Cyan, .05f, .18f);
                    }
                    nextArc = elapsed + .22f;
                }

                if (elapsed >= duration) break;
                yield return null;
            }

            r.Vfx.Burst(center + Vector3.up * .5f, d);
            FeelDirector.Instance?.Particles.Burst(center + Vector3.up * .9f, CityColor.Cyan, 24);
            AudioDirector.Instance?.Play(AudioCue.Destruction, center);
            r.KickCamera();

            for (int i = 0; i < LastImprisoned.Count; i++)
            {
                var npc = LastImprisoned[i];
                if (npc == null || npc.Dead) continue;
                LastShatterHits++;
                npc.Thaw();
                npc.Unroot();
                npc.Damage(shatterDamage, r.User, false, d.Force);
                FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * .9f, CityColor.Blue, 8);
            }
        }
        finally
        {
            sigVfx.HideGlacialPrison();
        }
    }

    static void ClampInsideRing(CityNpc npc, Vector3 center, float maxRadius)
    {
        Vector3 delta = npc.transform.position - center;
        Vector2 flat = new Vector2(delta.x, delta.z);
        if (flat.magnitude <= maxRadius) return;
        Vector2 clamped = flat.normalized * maxRadius;
        Vector3 desired = new Vector3(center.x + clamped.x, center.y, center.z + clamped.y);
        if (NavMesh.SamplePosition(desired, out var navHit, 2.5f, NavMesh.AllAreas))
        {
            npc.transform.position = navHit.position;
            if (npc.Agent != null && npc.Agent.enabled && npc.Agent.isOnNavMesh)
                npc.Agent.Warp(navHit.position);
        }
        else if (npc.Agent == null || !npc.Agent.enabled)
        {
            npc.transform.position = desired;
        }
    }

    static bool ClearLine(Vector3 from, Vector3 to, Transform ignoreRoot)
    {
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= .05f) return true;
        int hits = Physics.RaycastNonAlloc(from, delta / dist, barriers, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            var c = barriers[i].collider;
            if (c.attachedRigidbody != null || c.GetComponentInParent<CityNpc>() != null || c.transform.root == ignoreRoot)
                continue;
            return false;
        }
        return true;
    }
}
