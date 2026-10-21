using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[CreateAssetMenu(menuName = "Overpowered/Forge/Signature Effects/Plague Bloom")]
public sealed class PlagueBloomEffect : SynergyEffect
{
    public const int PoolLimit = 4;
    public PoisonEffect Poison;
    public int PulseCount = 6, MaxBlooms = 4;
    public float SecondaryRadius = 2.5f, SecondarySeconds = 2f, SecondaryDamageScale = .5f;
    public static int LastBloomCount { get; private set; }
    public static int LastApplications { get; private set; }
    public override bool CanBegin(SynergyRunner r) => Poison != null && SignatureTargeting.Ground(r);
    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition; var fx = SignatureVfx.Get();
        var targets = ListPool<CityNpc>.Get(); var exposed = HashSetPool<CityNpc>.Get();
        var centers = new Vector3[PoolLimit]; var ends = new float[PoolLimit];
        centers[0] = r.Target;
        float duration = Mathf.Clamp(d.Duration, 1f, 12f), start = Time.time, end = start + duration;
        float interval = duration / Mathf.Clamp(PulseCount, 1, 12);
        int count = 1, max = Mathf.Clamp(MaxBlooms, 1, PoolLimit);
        ends[0] = end; LastBloomCount = 1; LastApplications = 0;
        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        // Observe the authoritative killing result. Only main-bloom exposures may seed one secondary each;
        // secondary-only victims never enter exposed, and the lifetime total cannot exceed the fixed pool.
        System.Action<NpcDamageResult> death = result =>
        {
            if (result.HealthAfter > 0 || Time.time >= end || count >= max || !exposed.Remove(result.Target)) return;
            Vector3 point = result.Target.transform.position;
            if (!Physics.Raycast(point + Vector3.up * .4f, Vector3.down, out var ground, 2f, ~0, QueryTriggerInteraction.Ignore) ||
                ground.normal.y < .65f || ground.rigidbody != null) return;
            centers[count] = ground.point; ends[count] = Mathf.Min(end, Time.time + Mathf.Clamp(SecondarySeconds, .5f, 4f));
            count++; LastBloomCount = count;
        };
        CityNpc.DamageApplied += death;
        try
        {
            float nextPulse = start;
            while (Time.time < end)
            {
                for (int b = 0; b < count; b++)
                    if (Time.time < ends[b]) fx.ShowBloom(b, centers[b], b == 0 ? d.Radius : SecondaryRadius, (Time.time - start) / interval, ends[b] - Time.time);
                    else fx.HideBloom(b);
                if (Time.time >= nextPulse)
                {
                    nextPulse += interval;
                    int activeAtPulse = count;
                    for (int b = 0; b < activeAtPulse; b++)
                    {
                        if (Time.time >= ends[b]) continue;
                        float radius = b == 0 ? d.Radius : Mathf.Clamp(SecondaryRadius, .5f, d.Radius);
                        SignatureTargeting.Gather(r, centers[b], radius, targets, d.MaxTargets);
                        for (int t = 0; t < targets.Count; t++)
                        {
                            var npc = targets[t];
                            if (b == 0) exposed.Add(npc);
                            // Existing poison owns refreshing, tick totals, one variance roll/application and bounded death spreading.
                            // A bloom contributes at most one interval of poison per pulse; overlap refreshes, never stacks components.
                            float scale = b == 0 ? 1f : Mathf.Clamp01(SecondaryDamageScale);
                            float seconds = Mathf.Min(interval, ends[b] - Time.time);
                            if (seconds < .02f) continue;
                            Poisoned.Apply(npc, r.User, Poison, d.Damage * r.User.HeroStats.PowerDamage / duration * scale,
                                seconds, 0, CityColor.Leaf, r.ActivationDamage / duration * scale);
                            LastApplications++;
                        }
                        FeelDirector.Instance?.Particles.Burst(centers[b] + Vector3.up * .5f, CityColor.Leaf, 8);
                    }
                }
                yield return null;
            }
        }
        finally
        {
            CityNpc.DamageApplied -= death; fx.HideBlooms();
            ListPool<CityNpc>.Release(targets); HashSetPool<CityNpc>.Release(exposed);
        }
    }
}
