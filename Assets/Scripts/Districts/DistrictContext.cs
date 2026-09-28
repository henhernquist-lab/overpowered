using System;
using System.Collections.Generic;
using UnityEngine;

/// The district layer gameplay reads (WorldSession.Districts): which district a point or the player is in, and that
/// district's gameplay profile (DistrictProfileSet mapping; neutral when unset or disabled). No gameplay code branches on
/// a district name or index: they ask for a profile value here.
public sealed class DistrictContext : MonoBehaviour
{
    WorldSession world; DistrictGameplayProfile[] profiles = new DistrictGameplayProfile[0]; float nextCheck;
    /// The mapping in use (Resources/DistrictProfiles unless a verification suite supplies one); null = none.
    public DistrictProfileSet Set { get; private set; }
    /// True when at least one district resolves to a non-neutral profile. False = every hook takes its old path.
    public bool Active { get; private set; }
    public int Count => profiles.Length;
    /// District index the player is in (-1 before the first check), and its profile.
    public int PlayerDistrict { get; private set; } = -1;
    public DistrictGameplayProfile PlayerProfile => ProfileOf(PlayerDistrict);
    /// (from, to) whenever the player crosses into another district (from = -1 on the first check).
    public event Action<int, int> DistrictChanged;
    public int Changes { get; private set; }
    public void Initialize(WorldSession owner, DistrictProfileSet set = null)
    {
        world = owner; Use(set ?? DistrictProfileSet.Current);
    }
    /// Re-resolves every district against `set` (verification swaps in an in-memory set; null = none).
    public void Use(DistrictProfileSet set)
    {
        Set = set; var defs = world.City.DistrictDefinitions; profiles = new DistrictGameplayProfile[defs.Count]; Active = false;
        for (int i = 0; i < defs.Count; i++)
        {
            var p = set != null ? set.For(defs[i].Name) : null;
            profiles[i] = p != null ? p : DistrictGameplayProfile.Neutral;
            if (!profiles[i].IsNeutral) Active = true;
        }
    }
    public int DistrictAt(Vector3 position) => world.City.DistrictAt(position);
    public string NameOf(int district) => district >= 0 && district < world.City.DistrictDefinitions.Count ? world.City.DistrictDefinitions[district].Name : "";
    public DistrictGameplayProfile ProfileOf(int district) => district >= 0 && district < profiles.Length ? profiles[district] : DistrictGameplayProfile.Neutral;
    public DistrictGameplayProfile ProfileAt(Vector3 position) => ProfileOf(DistrictAt(position));
    // ---- the values the hooks read (exactly 1 / 0 for neutral, so a multiplication or addition leaves the old number)
    public float HeatFactorAt(Vector3 position) => Active ? ProfileAt(position).HeatResponse : 1f;
    public float DestructionFactorAt(Vector3 position) => Active ? ProfileAt(position).DestructionReward : 1f;
    public int DifficultyOffsetAt(Vector3 position) => Active ? ProfileAt(position).DifficultyOffset : 0;
    public float PoliceFactor => Active ? PlayerProfile.PoliceResponse : 1f;
    /// An archetype for `role` preferred by the district at `position` (first EnemyArchetype under Resources/Enemies whose
    /// Tags meet the profile's EnemyTags), or null = the roster default.
    public EnemyArchetype PreferredArchetype(NpcRole role, Vector3 position)
    {
        if (!Active) return null; var tags = ProfileAt(position).EnemyTags; if (tags == null || tags.Length == 0) return null;
        foreach (var a in Archetypes()) if (a != null && a.Tags != null) foreach (var t in a.Tags) foreach (var want in tags) if (string.Equals(t, want, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }
    static EnemyArchetype[] archetypes;
    static EnemyArchetype[] Archetypes() { if (archetypes == null) { archetypes = Resources.LoadAll<EnemyArchetype>("Enemies"); Array.Sort(archetypes, (a, b) => string.CompareOrdinal(a.name, b.name)); } return archetypes; }
    /// Civilian counts per district for `total` civilians spawned on the sidewalks in the original order (i % sidewalks),
    /// scaled by each district's CivilianDensity. Only used when Active.
    public int[] CivilianCounts(int total)
    {
        var city = world.City; var counts = new int[profiles.Length];
        for (int i = 0; i < total; i++) counts[city.SidewalkDistrict[i % city.Sidewalks.Count]]++;
        for (int d = 0; d < counts.Length; d++) counts[d] = Mathf.RoundToInt(counts[d] * profiles[d].CivilianDensity);
        return counts;
    }
    void Update()
    {
        if (world == null || world.Hero == null || Time.time < nextCheck) return;
        nextCheck = Time.time + .25f;
        int d = DistrictAt(world.Hero.transform.position);
        if (d == PlayerDistrict) return;
        int from = PlayerDistrict; PlayerDistrict = d; Changes++; DistrictChanged?.Invoke(from, d);
    }
    /// Verification: evaluate now instead of on the next 0.25 s check.
    public void Refresh() { nextCheck = 0f; Update(); }
}
