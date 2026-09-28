using System;
using System.Collections.Generic;
using UnityEngine;

/// OPTIONAL encounter-selection strategy for a mode (GameModeDefinition.Selection). Null keeps the original behaviour exactly:
/// round robin over GameModeDefinition.Encounters. With a selection, each spawn draws one Option by weight among those
/// eligible now: inside its difficulty band, allowed in the chosen site's district, and not among the last AntiRepeatWindow
/// picks (the window relaxes rather than stall when every eligible option was picked recently). Seeded = the same seed
/// gives the same sequence of picks for the same inputs (replays, tests). Data only - no mode or encounter ID switches.
[CreateAssetMenu(menuName = "Overpowered/Modes/Encounter selection")]
public sealed class EncounterSelection : ScriptableObject
{
    [Serializable] public sealed class Option
    {
        public EncounterDefinition Encounter;
        [Min(0)] public float Weight = 1f;
        [Tooltip("Eligible while the session difficulty band is within [Min, Max].")] public int MinDifficulty = 0, MaxDifficulty = 999;
        [Tooltip("District names (CityLayout district Name, case-insensitive) this option may spawn in. Empty = any district.")] public string[] Districts = new string[0];
        [Tooltip("District gameplay tags (DistrictGameplayProfile.MissionTags) this option needs, any one of them. Empty = any. Checked with the district filter (so DistrictFallback also relaxes it).")] public string[] Tags = new string[0];
        [Tooltip("Category for district weighting (DistrictGameplayProfile.CategoryWeights). Empty = x1.")] public string Category;
    }
    public enum DifficultySource { ResolvedEncounters, Successes, PlayerLevel, HeatStars }
    public Option[] Options = new Option[0];
    [Tooltip("An encounter picked within the last N picks is skipped while another eligible option exists. 0 = off.")] [Min(0)] public int AntiRepeatWindow = 1;
    public DifficultySource Difficulty = DifficultySource.ResolvedEncounters;
    [Tooltip("The band is the source value divided by this (1 = every encounter is a step, 2 = every second one...).")] [Min(1)] public int DifficultyStep = 1;
    [Tooltip("When no option is allowed in the chosen site's district, ignore district filters for this pick (false = no spawn this cycle).")] public bool DistrictFallback = true;
    [Tooltip("Deterministic picks from Seed (otherwise seeded from the clock per session).")] public bool Seeded;
    public int Seed = 1337;
    public EncounterSelectionState Begin() => new EncounterSelectionState(this, Seeded ? Seed : Environment.TickCount);
    /// Pure selection rule (no state): the index of the chosen option, or -1. `roll` in [0,1). `scratch` is reused.
    public static int Choose(IReadOnlyList<Option> options, int band, string district, IReadOnlyList<EncounterDefinition> recent, int window, bool districtFallback, double roll, List<int> scratch)
        => Choose(options, band, district, recent, window, districtFallback, roll, scratch, null);
    /// Same rule with the site district's gameplay profile: option Tags must meet its MissionTags, and weights are scaled by its
    /// CategoryWeights. A null profile (no district layer) ignores Tags / Category exactly as before.
    public static int Choose(IReadOnlyList<Option> options, int band, string district, IReadOnlyList<EncounterDefinition> recent, int window, bool districtFallback, double roll, List<int> scratch, DistrictGameplayProfile profile)
    {
        scratch.Clear();
        for (int pass = 0; pass < 4 && scratch.Count == 0; pass++)
        {
            bool ignoreDistrict = pass >= 2, ignoreRecent = pass % 2 == 1;
            if (ignoreDistrict && !districtFallback) break;
            for (int i = 0; i < options.Count; i++)
            {
                var o = options[i];
                if (o == null || o.Encounter == null || !(o.Weight > 0f) || band < o.MinDifficulty || band > o.MaxDifficulty) continue;
                if (!ignoreDistrict && (!Allowed(o, district) || !Tagged(o, profile))) continue;
                if (!ignoreRecent && Recent(o.Encounter, recent, window)) continue;
                scratch.Add(i);
            }
        }
        if (scratch.Count == 0) return -1;
        double total = 0; foreach (int i in scratch) total += WeightOf(options[i], profile);
        if (!(total > 0)) return scratch[0];
        double at = Math.Min(Math.Max(roll, 0d), .999999999d) * total;
        foreach (int i in scratch) { at -= WeightOf(options[i], profile); if (at < 0) return i; }
        return scratch[scratch.Count - 1];
    }
    static double WeightOf(Option o, DistrictGameplayProfile profile) => profile == null ? o.Weight : o.Weight * profile.CategoryWeightOf(o.Category);
    static bool Tagged(Option o, DistrictGameplayProfile profile)
    {
        if (profile == null || o.Tags == null || o.Tags.Length == 0) return true;
        foreach (var t in o.Tags) if (profile.HasMissionTag(t)) return true;
        return false;
    }
    static bool Allowed(Option o, string district)
    {
        if (o.Districts == null || o.Districts.Length == 0) return true;
        foreach (var d in o.Districts) if (!string.IsNullOrEmpty(d) && string.Equals(d.Trim(), district, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    static bool Recent(EncounterDefinition e, IReadOnlyList<EncounterDefinition> recent, int window)
    {
        for (int i = recent.Count - 1, seen = 0; i >= 0 && seen < window; i--, seen++) if (recent[i] == e) return true;
        return false;
    }
}
/// Per-session state of an EncounterSelection (plain object owned by GameModeSession).
public sealed class EncounterSelectionState
{
    readonly EncounterSelection data; readonly System.Random random; readonly List<int> scratch = new List<int>();
    public readonly List<EncounterDefinition> History = new List<EncounterDefinition>();
    public EncounterSelectionState(EncounterSelection selection, int seed) { data = selection; random = new System.Random(seed); }
    public int Band(int value) => Mathf.Max(0, value) / Mathf.Max(1, data.DifficultyStep);
    public EncounterDefinition Pick(int difficultyValue, string district) => Pick(difficultyValue, district, null, 0);
    /// With the site district's profile (null = none) and its difficulty band offset.
    public EncounterDefinition Pick(int difficultyValue, string district, DistrictGameplayProfile profile, int bandOffset)
    {
        int index = EncounterSelection.Choose(data.Options, Mathf.Max(0, Band(difficultyValue) + bandOffset), district ?? "", History, data.AntiRepeatWindow, data.DistrictFallback, random.NextDouble(), scratch, profile);
        if (index < 0) return null;
        var chosen = data.Options[index].Encounter; History.Add(chosen);
        if (History.Count > 64) History.RemoveAt(0);
        return chosen;
    }
}
