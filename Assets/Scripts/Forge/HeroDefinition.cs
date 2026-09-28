using System;
using UnityEngine;

/// Per-hero archetype, as MULTIPLIERS of the shared baseline (GameTuning.Movement and each power's data), so 1 = baseline
/// and every value is tunable on the hero asset. Applied by WorldSession (health), PowerUser (energy, power damage/cooldown,
/// melee via Super Strength / basic stats), SuperHeroController (move speed, basic melee) and CityNpc (incoming knockback).
[Serializable] public sealed class HeroStats
{
    [Tooltip("x GameTuning.Movement.Health")] public float MaxHealth = 1f;
    [Tooltip("x GameTuning.Movement.Energy")] public float MaxEnergy = 1f;
    [Tooltip("x GameTuning.Movement.EnergyRecharge")] public float EnergyRegen = 1f;
    [Tooltip("x walk / run speed")] public float MoveSpeed = 1f;
    [Tooltip("x melee damage AND knockback force dealt (Strength punch/kick, combo, heavy, ground pound, basic melee)")] public float MeleeDamage = 1f;
    [Tooltip("x damage of every non-melee power")] public float PowerDamage = 1f;
    [Tooltip("x cooldown of every non-melee power (0.8 = 20% shorter); synergy cooldowns are not affected")] public float CooldownMultiplier = 1f;
    [Tooltip("Share of incoming knockback distance ignored (0 = full knockback, 1 = immovable)")] [Range(0f, 1f)] public float KnockbackResistance;
    public static readonly HeroStats Baseline = new HeroStats();
    public bool IsBaseline => MaxHealth == 1f && MaxEnergy == 1f && EnergyRegen == 1f && MoveSpeed == 1f && MeleeDamage == 1f && PowerDamage == 1f && CooldownMultiplier == 1f && KnockbackResistance == 0f;
}
[CreateAssetMenu(menuName="Overpowered/Forge/Hero")]
public sealed class HeroDefinition : ScriptableObject
{
    public string Id, DisplayName;
    public GameObject CharacterPrefab;
    [Tooltip("Sidekick characters: recolours the prefab's colour map from the loadout's Primary/Secondary palette colours. Empty = palette materials replace every slot (mannequin).")]
    public SidekickSuit Suit;
    public Sprite Portrait;
    public CityColor Primary=CityColor.Blue, Secondary=CityColor.Cyan;
    public Vector3 VisualScale=Vector3.one;
    public PowerDefinition[] AvailablePowers;
    public PowerDefinition DefaultA, DefaultB;
    public HumanoidAnimationTuning Animation;
    public HeroStats Stats = new HeroStats();
}
