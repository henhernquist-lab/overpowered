using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

/// SEISMIC SLAM (Primary: Super Strength) — close-range ground smash that releases an expanding radial shockwave.
/// Requires grounded (or near-ground) activation over valid terrain. As the shockwave expands from 0 to Radius,
/// hostile enemies inside take distance-scaled damage once, cancel active windups, and suffer archetype-scaled
/// stagger and NavMesh-bounded radial knockback (Brutes resist stagger/push more than Rushers/Gunners).
[CreateAssetMenu(menuName = "Overpowered/Forge/Signature Effects/Seismic Slam")]
public sealed class SeismicSlamEffect : SynergyEffect
{
    public float MaxStartHeight = 2.2f;
    public float WaveSeconds = .38f;
    public float InnerFullDamageFraction = .5f;
    public float OuterDamageFraction = .68f;
    public float MaxVerticalOffset = 3f;
    public float StandardStaggerSeconds = 1.15f;
    public float BruteStaggerSeconds = .55f;
    public float StandardPushDistance = 2.6f;
    public float BrutePushDistance = 1.05f;

    public static readonly List<CityNpc> LastStruck = new List<CityNpc>();
    public static readonly List<float> LastDamages = new List<float>();
    public static float LastWaveRadius { get; private set; }
    static readonly RaycastHit[] barriers = new RaycastHit[16];

    public override bool CanBegin(SynergyRunner r)
    {
        var hero = r.User != null ? r.User.Hero : null;
        if (hero == null) return false;
        if (!hero.PresentationState.Grounded && hero.HeightAboveGround() > MaxStartHeight) return false;
        if (!r.GroundTarget(false, out r.Target)) return false;
        return Mathf.Abs(r.transform.position.y - r.Target.y) <= MaxStartHeight + 1.25f;
    }

    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition;
        var hero = r.User.Hero;
        LastStruck.Clear();
        LastDamages.Clear();
        LastWaveRadius = 0f;

        if (!hero.PresentationState.Grounded)
        {
            r.DrivesMotion = true;
            float dropElapsed = 0f;
            while (!hero.PresentationState.Grounded && dropElapsed < .18f)
            {
                var flags = r.Move(Vector3.down * d.DiveSpeed, false);
                if ((flags & CollisionFlags.Below) != 0) break;
                dropElapsed += Time.deltaTime;
                yield return null;
            }
            r.DrivesMotion = false;
        }

        Vector3 origin = r.Target;
        if (r.GroundTarget(false, out Vector3 freshGround)) origin = freshGround;
        Vector3 waveOrigin = origin + Vector3.up * .15f;

        hero.PresentPunch();
        r.KickCamera();
        r.Vfx.Burst(waveOrigin, d);
        var sigVfx = SignatureVfx.Get();
        sigVfx.ShowSeismicSlam(origin, d.Radius, WaveSeconds);
        FeelDirector.Impact(waveOrigin, d.Force, r.ActivationDamage, 1);
        AudioDirector.Instance?.Play(AudioCue.Destruction, waveOrigin);

        var struckNpcs = HashSetPool<CityNpc>.Get();
        var struckBodies = HashSetPool<Rigidbody>.Get();
        try
        {
            float duration = Mathf.Max(.12f, WaveSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
                float progress = elapsed / duration;
                float currentRadius = Mathf.Lerp(.9f, d.Radius, progress);
                LastWaveRadius = currentRadius;
                sigVfx.UpdateSeismicSlam(origin, currentRadius, progress);
                SweepWave(r, origin, waveOrigin, currentRadius, d.Radius, struckNpcs, struckBodies);
                if (elapsed >= duration) break;
                yield return null;
            }
            LastWaveRadius = d.Radius;
            SweepWave(r, origin, waveOrigin, d.Radius, d.Radius, struckNpcs, struckBodies);
            yield return new WaitForSeconds(.12f);
        }
        finally
        {
            sigVfx.HideSeismicSlam();
            HashSetPool<CityNpc>.Release(struckNpcs);
            HashSetPool<Rigidbody>.Release(struckBodies);
        }
    }

    void SweepWave(SynergyRunner r, Vector3 groundOrigin, Vector3 waveOrigin, float currentRadius, float maxRadius,
        HashSet<CityNpc> struckNpcs, HashSet<Rigidbody> struckBodies)
    {
        var d = r.Definition;
        int count = Physics.OverlapSphereNonAlloc(waveOrigin, currentRadius, r.Hits, ~0, QueryTriggerInteraction.Ignore);
        float innerRadius = maxRadius * Mathf.Clamp(InnerFullDamageFraction, .1f, .9f);

        for (int i = 0; i < count; i++)
        {
            var col = r.Hits[i];
            if (col == null || col.transform.root == r.transform) continue;

            var npc = col.GetComponentInParent<CityNpc>();
            if (npc != null)
            {
                if (npc.Dead || !npc.Hostile || struckNpcs.Contains(npc)) continue;
                Vector3 toNpc = npc.transform.position - groundOrigin;
                if (Mathf.Abs(toNpc.y) > MaxVerticalOffset) continue;
                float flatDist = new Vector2(toNpc.x, toNpc.z).magnitude;
                if (flatDist > currentRadius + .35f) continue;
                if (!ClearLine(waveOrigin, npc.transform.position + Vector3.up * .9f, r.transform)) continue;

                struckNpcs.Add(npc);
                float falloff = flatDist <= innerRadius
                    ? 1f
                    : Mathf.Lerp(1f, OuterDamageFraction, Mathf.InverseLerp(innerRadius, maxRadius, flatDist));
                float damage = r.ActivationDamage * falloff;

                bool isBrute = npc.Archetype != null &&
                    (npc.Archetype.Kind == AttackKind.Slam || npc.Archetype.HealthMultiplier >= 2.5f);
                float stagger = isBrute ? BruteStaggerSeconds : StandardStaggerSeconds;
                float push = (isBrute ? BrutePushDistance : StandardPushDistance) * falloff;

                Vector3 pushDir = new Vector3(toNpc.x, 0f, toNpc.z);
                pushDir = pushDir.sqrMagnitude > .001f ? pushDir.normalized : r.transform.forward;

                npc.Freeze(Mathf.Min(.3f, stagger), false);
                npc.Root(stagger);
                npc.Damage(damage, r.User, true, d.Force * (isBrute ? .5f : 1f));

                if (!npc.Dead)
                {
                    Vector3 desired = npc.transform.position + pushDir * push;
                    if (NavMesh.SamplePosition(desired, out var navHit, 3.5f, NavMesh.AllAreas))
                    {
                        npc.transform.position = navHit.position;
                        if (npc.Agent != null && npc.Agent.enabled && npc.Agent.isOnNavMesh)
                            npc.Agent.Warp(navHit.position);
                    }
                    else if ((npc.Agent == null || !npc.Agent.enabled) &&
                             Physics.Raycast(desired + Vector3.up * 1.5f, Vector3.down, out var groundHit, 4f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        npc.transform.position = new Vector3(desired.x, groundHit.point.y, desired.z);
                    }
                }
                else
                {
                    var suspension = npc.GetComponent<SynergySuspension>() ?? npc.gameObject.AddComponent<SynergySuspension>();
                    var rb = suspension.Begin(npc);
                    rb.useGravity = true;
                    suspension.Release();
                    rb.AddForce((pushDir + Vector3.up * .45f).normalized * Mathf.Min(d.Force, 1200f), ForceMode.Impulse);
                    CombatImpact.PreserveDeathLaunch(npc);
                }

                FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * .2f, CityColor.Amber, 6);
                LastStruck.Add(npc);
                LastDamages.Add(damage);
                continue;
            }

            var body = col.attachedRigidbody;
            if (body != null && !struckBodies.Contains(body))
            {
                if (!ClearLine(waveOrigin, col.bounds.center, r.transform)) continue;
                struckBodies.Add(body);
                if (!body.isKinematic)
                    body.AddExplosionForce(Mathf.Min(d.Force, 1400f), waveOrigin, maxRadius, .55f, ForceMode.Impulse);
                body.GetComponent<BreakableProp>()?.TakeDamage(r.ActivationDamage, r.User);
            }
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
