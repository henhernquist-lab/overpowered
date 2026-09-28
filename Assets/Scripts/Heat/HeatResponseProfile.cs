using System;
using UnityEngine;

/// OPTIONAL Heat response quality (Resources/HeatResponse). Enabled=false (setup default) or no asset: the police respond
/// exactly as before. Each tier starts at MinStars; the highest reached tier applies, and dropping below it removes it.
/// Only Heat-spawned police (WorldSession.ReconcilePolice) are affected - never encounter responders or Endless waves, whose
/// stats stay explicit. Hostility is never changed here: it stays GameTuning's per-side police rule.
[CreateAssetMenu(menuName = "Overpowered/Heat response profile")]
public sealed class HeatResponseProfile : ScriptableObject
{
    [Serializable] public sealed class Tier
    {
        public string Label = "TIER";
        [Min(1)] public int MinStars = 3;
        [Tooltip("x GameTuning Heat.ResponseInterval (0.5 = police arrive twice as often).")] [Min(.05f)] public float ArrivalIntervalMultiplier = 1f;
        [Tooltip("x the Heat police count.")] [Min(0)] public float ResponseCountMultiplier = 1f;
        [Tooltip("A harder archetype for a share of the police spawned at this tier (null = the roster default).")] public EnemyArchetype HostileArchetype;
        [Range(0, 1)] public float ArchetypeShare;
        [Tooltip("Elite responders: police spawned at this tier get x this health (1 = normal).")] [Min(1)] public float EliteHealthMultiplier = 1f;
        [Tooltip("Police spawned at this tier keep chasing at any distance (AlwaysAggro) - pursuit persistence.")] public bool Persistent;
        [Tooltip("x Heat decay while the player is Pursued / Searching (0.5 = half; 1 = normal).")] [Min(0)] public float PursuedDecayMultiplier = 1f;
        [Tooltip("Hook only: raises HeatResponse.RoadblockEligible when this tier starts. Nothing is spawned by cloud code.")] public bool RoadblockEligible;
    }
    public bool Enabled;
    [Tooltip("Apply to the Hero side too (counts / arrival only matter there: hero police stay non-hostile by GameTuning).")] public bool ApplyToHero;
    public Tier[] Tiers = new Tier[0];
    /// The highest tier with MinStars <= stars, or null.
    public Tier TierFor(int stars)
    {
        Tier best = null; if (Tiers == null) return null;
        foreach (var t in Tiers) if (t != null && stars >= t.MinStars && (best == null || t.MinStars > best.MinStars)) best = t;
        return best;
    }
    public static HeatResponseProfile Current => Resources.Load<HeatResponseProfile>("HeatResponse");
}
