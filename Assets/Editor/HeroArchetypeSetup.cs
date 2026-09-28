using System;
using UnityEditor;
using UnityEngine;

/// Writes the three archetypes into the hero assets (multipliers of the shared baseline; see HeroStats). Only a hero whose
/// Stats are still all-baseline is written, so inspector tuning is never overwritten. VECTOR stays the baseline.
/// Menu: Overpowered/Forge/Apply hero archetype stats. Batch: -executeMethod HeroArchetypeSetup.Batch
public static class HeroArchetypeSetup
{
    [MenuItem("Overpowered/Forge/Apply hero archetype stats")]
    public static void Apply()
    {
        // TITAN, tank: +40% health, +25% melee damage and knockback, -15% move speed, -20% max energy, resists knockback.
        Set("titan", s => { s.MaxHealth = 1.4f; s.MeleeDamage = 1.25f; s.MoveSpeed = .85f; s.MaxEnergy = .8f; s.KnockbackResistance = .6f; });
        // NOVA, energy: +40% max energy and regen, -20% power cooldowns, +10% move speed, -25% health.
        Set("nova", s => { s.MaxEnergy = 1.4f; s.EnergyRegen = 1.4f; s.CooldownMultiplier = .8f; s.MoveSpeed = 1.1f; s.MaxHealth = .75f; });
        AssetDatabase.SaveAssets();
    }
    public static void Batch() { Apply(); EditorApplication.Exit(0); }
    static void Set(string id, Action<HeroStats> configure)
    {
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Forge/Heroes/" + id + ".asset");
        if (hero == null) { Debug.LogWarning("Hero asset missing: " + id); return; }
        if (hero.Stats == null) hero.Stats = new HeroStats();
        if (!hero.Stats.IsBaseline) return;
        configure(hero.Stats); EditorUtility.SetDirty(hero);
    }
}
