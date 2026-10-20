using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

/// SOLAR FLARE (Primary: Fire Blast) — unleashes a broad expanding wave of superheated flame in the player's
/// aim direction, scorching enemies across a wide frontal arc once and leaving a short-lived burning heat wake.
/// Distinct from ordinary Fire Blast (not a point projectile or sphere); spawns ahead of the eye/body so it
/// never obscures the first-person camera.
[CreateAssetMenu(menuName = "Overpowered/Forge/Signature Effects/Solar Flare Signature")]
public sealed class SolarFlareSignatureEffect : SynergyEffect
{
    public float ForwardStartOffset = 1.35f;
    public float WaveTravelSeconds = .42f;
    public float StartHalfWidth = 1.6f;
    public float SlabDepth = 2.4f;
    public float ResidualDuration = 2f;
    public float ResidualTickSeconds = .5f;
    public float ResidualTickFraction = .12f;
    public float WakeRadius = 4.2f;

    public static readonly List<CityNpc> LastWaveStruck = new List<CityNpc>();
    public static readonly List<CityNpc> LastResidualStruck = new List<CityNpc>();
    public static int LastResidualTicks { get; private set; }
    static readonly RaycastHit[] barriers = new RaycastHit[16];

    public override bool CanBegin(SynergyRunner r)
    {
        if (r.User == null) return false;
        Vector3 dir = ForwardAim(r);
        r.Target = r.transform.position + dir * r.Definition.Range;
        return true;
    }

    static Vector3 ForwardAim(SynergyRunner r)
    {
        Vector3 raw = r.User.AimDirection;
        Vector3 flat = new Vector3(raw.x, 0f, raw.z);
        if (flat.sqrMagnitude < .001f) flat = r.transform.forward;
        return flat.normalized;
    }

    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition;
        LastWaveStruck.Clear();
        LastResidualStruck.Clear();
        LastResidualTicks = 0;

        Vector3 forward = ForwardAim(r);
        Vector3 origin = r.transform.position;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        r.KickCamera();
        var sigVfx = SignatureVfx.Get();
        AudioDirector.Instance?.Play(AudioCue.Destruction, origin + forward * 2f);

        var struckWave = HashSetPool<CityNpc>.Get();
        var struckProps = HashSetPool<BreakableProp>.Get();
        try
        {
            float travelTime = Mathf.Max(.15f, WaveTravelSeconds);
            float elapsed = 0f;
            float nextSpark = 0f;
            while (elapsed < travelTime)
            {
                elapsed = Mathf.Min(travelTime, elapsed + Time.deltaTime);
                float progress = elapsed / travelTime;
                float dist = Mathf.Lerp(ForwardStartOffset, d.Range, progress);
                float halfWidth = Mathf.Lerp(StartHalfWidth, d.Radius, progress);

                sigVfx.ShowSolarWave(origin, forward, dist, halfWidth, progress, .15f);
                if (elapsed >= nextSpark)
                {
                    Vector3 centerFront = origin + forward * dist + Vector3.up * .85f;
                    FeelDirector.Instance?.Particles.Burst(centerFront, CityColor.Fire, 6);
                    FeelDirector.Instance?.Particles.Burst(centerFront + right * (halfWidth * .55f), CityColor.Amber, 4);
                    FeelDirector.Instance?.Particles.Burst(centerFront - right * (halfWidth * .55f), CityColor.Amber, 4);
                    nextSpark = elapsed + .08f;
                }

                SweepWaveFront(r, origin, forward, right, dist, halfWidth, struckWave, struckProps);
                if (elapsed >= travelTime) break;
                yield return null;
            }
        }
        finally
        {
            sigVfx.HideSolarWave();
            HashSetPool<CityNpc>.Release(struckWave);
            HashSetPool<BreakableProp>.Release(struckProps);
        }

        Vector3 wakeCenter = origin + forward * (d.Range * .52f);
        if (NavMesh.SamplePosition(wakeCenter, out var navHit, 4f, NavMesh.AllAreas))
            wakeCenter = navHit.position;

        float wakeTime = Mathf.Max(0f, ResidualDuration);
        if (wakeTime > 0f && ResidualTickSeconds > 0f)
        {
            sigVfx.ShowSolarWake(wakeCenter, WakeRadius, wakeTime);
            float tickDamage = r.ActivationDamage * ResidualTickFraction;
            int ticks = Mathf.Max(1, Mathf.RoundToInt(wakeTime / ResidualTickSeconds));
            try
            {
                for (int t = 0; t < ticks; t++)
                {
                    yield return new WaitForSeconds(ResidualTickSeconds);
                    LastResidualTicks++;
                    TickResidualWake(r, wakeCenter, WakeRadius, tickDamage);
                }
            }
            finally
            {
                sigVfx.HideSolarWake();
            }
        }
    }

    void SweepWaveFront(SynergyRunner r, Vector3 origin, Vector3 forward, Vector3 right,
        float currentDist, float halfWidth, HashSet<CityNpc> struckWave, HashSet<BreakableProp> struckProps)
    {
        var d = r.Definition;
        Vector3 slabCenter = origin + forward * currentDist + Vector3.up * .9f;
        float searchRadius = Mathf.Sqrt(halfWidth * halfWidth + SlabDepth * SlabDepth) + .6f;
        int count = Physics.OverlapSphereNonAlloc(slabCenter, searchRadius, r.Hits, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var col = r.Hits[i];
            if (col == null || col.transform.root == r.transform) continue;

            var npc = col.GetComponentInParent<CityNpc>();
            if (npc != null)
            {
                if (npc.Dead || !npc.Hostile || struckWave.Contains(npc)) continue;
                Vector3 rel = npc.transform.position - origin;
                if (Mathf.Abs(rel.y) > 3f) continue;
                float forwardProj = Vector3.Dot(rel, forward);
                float lateralProj = Mathf.Abs(Vector3.Dot(rel, right));
                if (forwardProj < ForwardStartOffset - .4f || forwardProj > currentDist + SlabDepth * .65f) continue;
                float allowedHalfWidth = Mathf.Lerp(StartHalfWidth, d.Radius, Mathf.Clamp01(forwardProj / Mathf.Max(1f, d.Range))) + .45f;
                if (lateralProj > allowedHalfWidth) continue;
                if (!ClearLine(origin + Vector3.up * 1.1f, npc.transform.position + Vector3.up * 1.1f, r.transform)) continue;

                struckWave.Add(npc);
                if (d.BurnSeconds > 0f) npc.MarkBurn(d.BurnSeconds);
                npc.Damage(r.ActivationDamage, r.User, true, d.Force);

                if (!npc.Dead)
                {
                    Vector3 push = npc.transform.position + forward * 1.35f;
                    if (NavMesh.SamplePosition(push, out var navHit, 2.5f, NavMesh.AllAreas))
                    {
                        npc.transform.position = navHit.position;
                        if (npc.Agent != null && npc.Agent.enabled && npc.Agent.isOnNavMesh)
                            npc.Agent.Warp(navHit.position);
                    }
                }

                FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * .9f, CityColor.Fire, 8);
                LastWaveStruck.Add(npc);
                continue;
            }

            var prop = col.GetComponentInParent<BreakableProp>();
            if (prop != null && !struckProps.Contains(prop))
            {
                if (!ClearLine(origin + Vector3.up * 1.1f, col.bounds.center, r.transform)) continue;
                struckProps.Add(prop);
                prop.TakeDamage(r.ActivationDamage, r.User);
            }
        }
    }

    static void TickResidualWake(SynergyRunner r, Vector3 center, float radius, float tickDamage)
    {
        int count = Physics.OverlapSphereNonAlloc(center, radius, r.Hits, ~0, QueryTriggerInteraction.Ignore);
        var seen = HashSetPool<CityNpc>.Get();
        try
        {
            for (int i = 0; i < count; i++)
            {
                var npc = r.Hits[i].GetComponentInParent<CityNpc>();
                if (npc == null || npc.Dead || !npc.Hostile || !seen.Add(npc)) continue;
                if (Mathf.Abs(npc.transform.position.y - center.y) > 2.5f) continue;
                npc.MarkBurn(2f);
                npc.Damage(tickDamage, r.User, false);
                if (!LastResidualStruck.Contains(npc)) LastResidualStruck.Add(npc);
                FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * .5f, CityColor.Amber, 3);
            }
        }
        finally
        {
            HashSetPool<CityNpc>.Release(seen);
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
