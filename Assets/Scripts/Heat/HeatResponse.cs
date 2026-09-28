using System;
using UnityEngine;

/// Applies the optional HeatResponseProfile (WorldSession.Response). No profile / disabled / side excluded -> Tier is null
/// and every value below is neutral, so WorldSession computes exactly what it did before.
public sealed class HeatResponse : MonoBehaviour
{
    WorldSession world; int spawnedAtTier;
    public HeatResponseProfile Profile { get; private set; }
    /// The active tier (null = none), re-evaluated from the current Heat stars on every read.
    public HeatResponseProfile.Tier Tier
    {
        get
        {
            if (Profile == null || !Profile.Enabled || world == null) return null;
            if (world.Progression.Data.Side == PlayerSide.Hero && !Profile.ApplyToHero) return null;
            return Profile.TierFor(world.Stars);
        }
    }
    public HeatResponseProfile.Tier LastTier { get; private set; }
    /// (tier or null) when the active tier changes; RoadblockEligible (the hook for LOCAL) when a tier asking for it starts.
    public event Action<HeatResponseProfile.Tier> TierChanged, RoadblockEligible;
    public int TierChanges { get; private set; }
    public void Initialize(WorldSession owner, HeatResponseProfile profile = null) { world = owner; Profile = profile != null ? profile : HeatResponseProfile.Current; LastTier = Tier; }
    /// Verification swaps profiles in memory (null = none).
    public void Use(HeatResponseProfile profile) { Profile = profile; Evaluate(); }
    public float ArrivalMultiplier { get { var t = Tier; return t != null ? t.ArrivalIntervalMultiplier : 1f; } }
    public float CountMultiplier { get { var t = Tier; return t != null ? t.ResponseCountMultiplier : 1f; } }
    public float DecayMultiplier
    {
        get
        {
            var t = Tier; if (t == null || world.Pursuit == null) return 1f;
            var s = world.Pursuit.State; return s == PursuitState.Pursued || s == PursuitState.Searching ? t.PursuedDecayMultiplier : 1f;
        }
    }
    /// The archetype for the next Heat police spawn, or null for the roster default (deterministic share by spawn order).
    public EnemyArchetype NextArchetype()
    {
        var t = Tier; if (t == null || t.HostileArchetype == null || t.ArchetypeShare <= 0f) return null;
        spawnedAtTier++;
        return Mathf.FloorToInt(spawnedAtTier * t.ArchetypeShare) > Mathf.FloorToInt((spawnedAtTier - 1) * t.ArchetypeShare) ? t.HostileArchetype : null;
    }
    /// Marks a freshly spawned Heat police NPC with this tier's quality (elite health, persistence); removed on tier drop.
    public void Apply(CityNpc npc)
    {
        var t = Tier; if (t == null || npc == null) return;
        if (t.EliteHealthMultiplier == 1f && !t.Persistent) return;
        var tag = npc.gameObject.AddComponent<HeatTierTag>(); tag.MinStars = t.MinStars; tag.HealthMultiplier = t.EliteHealthMultiplier; tag.Persistent = t.Persistent;
        if (t.EliteHealthMultiplier != 1f) npc.SetCombatStats(npc.MaxHealth * t.EliteHealthMultiplier, npc.ContactDamage);
        if (t.Persistent) npc.AlwaysAggro = true;
    }
    void Update() { if (world != null) Evaluate(); }
    void Evaluate()
    {
        var now = Tier; if (now == LastTier) return;
        LastTier = now; TierChanges++; spawnedAtTier = 0; TierChanged?.Invoke(now);
        if (now != null && now.RoadblockEligible) RoadblockEligible?.Invoke(now);
        // Dropping Heat removes tier modifiers from the police the higher tier spawned.
        int stars = world.Stars;
        foreach (var npc in world.Npcs)
        {
            if (npc == null || npc.Dead) continue; var tag = npc.GetComponent<HeatTierTag>(); if (tag == null || tag.Removed || (now != null && now.MinStars >= tag.MinStars) || stars >= tag.MinStars) continue;
            tag.Removed = true;
            if (tag.HealthMultiplier != 1f) npc.SetCombatStats(Mathf.Max(1f, npc.Health / tag.HealthMultiplier), npc.ContactDamage);
            if (tag.Persistent) npc.AlwaysAggro = false;
        }
    }
}
/// Tier quality given to one Heat police NPC (see HeatResponse.Apply).
public sealed class HeatTierTag : MonoBehaviour { public int MinStars; public float HealthMultiplier = 1f; public bool Persistent, Removed; }
