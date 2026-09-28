using System;
using UnityEngine;

/// Gameplay character of a district (data only). Every default is neutral (1 / 0 / empty), and a neutral profile changes
/// nothing: the systems that read it multiply by 1, add 0, or skip their filter. Gameplay code never looks at district
/// NAMES; the name -> profile mapping lives in the DistrictProfileSet asset.
[CreateAssetMenu(menuName = "Overpowered/Districts/Gameplay profile")]
public sealed class DistrictGameplayProfile : ScriptableObject
{
    [Serializable] public sealed class CategoryWeight { public string Category; [Min(0)] public float Weight = 1f; }
    [Header("Encounters")]
    [Tooltip("x the weight of EncounterSelection options whose Category matches (unlisted categories x1).")] public CategoryWeight[] CategoryWeights = new CategoryWeight[0];
    [Tooltip("Tags this district offers; EncounterSelection options with Tags need one of them. Empty = offers none (tagged options skip it).")] public string[] MissionTags = new string[0];
    [Tooltip("Added to the difficulty band of encounters placed here (staged missions, EncounterSelection).")] public int DifficultyOffset;
    [Header("Population and response")]
    [Tooltip("x the civilians spawned on this district's sidewalks at session start.")] [Min(0)] public float CivilianDensity = 1f;
    [Tooltip("x the Heat police count while the player is in this district.")] [Min(0)] public float PoliceResponse = 1f;
    [Tooltip("x Heat the player earns from crimes / destruction / assaults committed here.")] [Min(0)] public float HeatResponse = 1f;
    [Tooltip("x villain destruction XP for props broken here.")] [Min(0)] public float DestructionReward = 1f;
    [Header("Tags (hooks)")]
    [Tooltip("Preferred enemy archetypes (EnemyArchetype.Tags) for Heat police spawned here. Empty = the roster default.")] public string[] EnemyTags = new string[0];
    [Tooltip("Traversal / activity tags for future systems (read-only here).")] public string[] ActivityTags = new string[0];
    public float CategoryWeightOf(string category)
    {
        if (string.IsNullOrEmpty(category) || CategoryWeights == null) return 1f;
        foreach (var c in CategoryWeights) if (c != null && string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase)) return c.Weight;
        return 1f;
    }
    public bool HasMissionTag(string tag)
    {
        if (MissionTags == null) return false;
        foreach (var t in MissionTags) if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    /// True when every value is the neutral default (the old behaviour).
    public bool IsNeutral => (CategoryWeights == null || CategoryWeights.Length == 0) && (MissionTags == null || MissionTags.Length == 0) && DifficultyOffset == 0 &&
        CivilianDensity == 1f && PoliceResponse == 1f && HeatResponse == 1f && DestructionReward == 1f && (EnemyTags == null || EnemyTags.Length == 0);
    static DistrictGameplayProfile neutral;
    /// Shared all-defaults profile for districts with no mapping (never saved, never edited).
    public static DistrictGameplayProfile Neutral { get { if (neutral == null) { neutral = CreateInstance<DistrictGameplayProfile>(); neutral.name = "Neutral district profile"; neutral.hideFlags = HideFlags.HideAndDontSave; } return neutral; } }
}
