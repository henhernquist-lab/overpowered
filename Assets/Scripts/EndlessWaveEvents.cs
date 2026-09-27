using System;
using System.Collections.Generic;
using UnityEngine;

/// OPTIONAL Endless milestones for EndlessWaveDirector.Events (null = the original waves exactly). Everything a wave
/// becomes is decided by Plan(wave), a pure deterministic function of the director's formulas, this asset and the wave
/// number, so any wave can be simulated without a scene (EndlessSimulation) and replays identically.
///  - Modifier waves: from ModifierFromWave, every ModifierEvery-th wave draws distinct weighted modifiers (more of them
///    deeper in the run); their multipliers COMPOSE (multiply) on health, damage, enemy count and score, and add elite share.
///  - Elites: from EliteFromWave a share of the regular enemies (spread evenly through the spawn order) are elites.
///  - Miniboss: every MinibossEvery-th wave the FIRST spawn is one extra heavy enemy.
///  - Alive budget: an elite / miniboss occupies EliteAliveCost / MinibossAliveCost of the MaxAlive slots, so the
///    simultaneous threat and the humanoid count both stay within MaxAlive.
///  - Scoring (x wave): kill (director KillScore x modifier score), elite kill, miniboss kill, wave clear (director
///    WaveClearBonus), flawless (no player damage taken during the wave).
[CreateAssetMenu(menuName = "Overpowered/Mode Director/Endless wave events")]
public sealed class EndlessWaveEvents : ScriptableObject
{
    [Header("Modifier waves")]
    public int ModifierFromWave = 4;
    [Min(1)] public int ModifierEvery = 2;
    [Tooltip("Modifiers on a modifier wave: 1 + (wave - ModifierFromWave) / ExtraModifierEvery, capped at MaxModifiersPerWave.")]
    [Min(1)] public int ExtraModifierEvery = 10;
    [Min(1)] public int MaxModifiersPerWave = 3;
    public WaveModifier[] Modifiers = new WaveModifier[0];
    [Header("Elites")]
    public int EliteFromWave = 3;
    [Range(0, 1)] public float EliteShare = .15f;
    public float EliteShareGrowthPerWave = .01f;
    [Range(0, 1)] public float MaxEliteShare = .4f;
    public float EliteHealthMultiplier = 2f, EliteDamageMultiplier = 1.4f;
    [Min(1)] public int EliteAliveCost = 2;
    [Header("Miniboss")]
    [Tooltip("0 = never.")] public int MinibossEvery = 5;
    [Tooltip("Null = the regular composition archetype.")] public EnemyArchetype MinibossArchetype;
    public float MinibossHealthMultiplier = 8f, MinibossDamageMultiplier = 1.6f;
    [Min(1)] public int MinibossAliveCost = 4;
    [Header("Scoring (each x wave)")]
    public int EliteKillScore = 25, MinibossKillScore = 150, FlawlessBonus = 40;
    [Tooltip("Modifier draws are seeded by (Seed, wave).")] public int Seed = 7331;

    public enum Slot { Regular, Elite, Miniboss }
    public WavePlan Plan(EndlessWaveDirector director, int wave)
    {
        var plan = new WavePlan { Wave = wave, Health = 1f, Damage = 1f, Count = 1f, Score = 1f, Modifiers = new List<WaveModifier>() };
        if (wave >= ModifierFromWave && (wave - ModifierFromWave) % ModifierEvery == 0 && Modifiers != null)
        {
            int want = Mathf.Min(MaxModifiersPerWave, 1 + (wave - ModifierFromWave) / ExtraModifierEvery);
            var pool = new List<WaveModifier>(); foreach (var m in Modifiers) if (m != null && m.Weight > 0f && wave >= m.FromWave) pool.Add(m);
            var random = new System.Random(unchecked(Seed * 7919 + wave * 104729));
            while (plan.Modifiers.Count < want && pool.Count > 0)
            {
                float total = 0f; foreach (var m in pool) total += m.Weight;
                double at = random.NextDouble() * total; int pick = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++) { at -= pool[i].Weight; if (at < 0) { pick = i; break; } }
                plan.Modifiers.Add(pool[pick]); pool.RemoveAt(pick);
            }
        }
        float eliteBonus = 0f;
        foreach (var m in plan.Modifiers) { plan.Health *= m.Health; plan.Damage *= m.Damage; plan.Count *= m.Count; plan.Score *= m.Score; eliteBonus += m.EliteShareBonus; }
        plan.Miniboss = MinibossEvery > 0 && wave > 0 && wave % MinibossEvery == 0;
        plan.Regulars = Mathf.Max(1, Mathf.RoundToInt(director.WaveSize(wave) * plan.Count));
        plan.Size = plan.Regulars + (plan.Miniboss ? 1 : 0);
        plan.EliteShare = wave >= EliteFromWave ? Mathf.Clamp01(Mathf.Min(MaxEliteShare, EliteShare + EliteShareGrowthPerWave * (wave - EliteFromWave)) + eliteBonus) : 0f;
        plan.Elites = Mathf.FloorToInt(plan.EliteShare * plan.Regulars + 1e-4f);
        return plan;
    }
    /// Which kind of enemy the 0-based spawn index of a wave is: the miniboss first, then elites spread evenly (exact count).
    public Slot SlotOf(WavePlan plan, int index)
    {
        if (plan.Miniboss) { if (index == 0) return Slot.Miniboss; index--; }
        if (plan.Elites <= 0 || index < 0 || index >= plan.Regulars) return Slot.Regular;
        return (long)(index + 1) * plan.Elites / plan.Regulars > (long)index * plan.Elites / plan.Regulars ? Slot.Elite : Slot.Regular;
    }
    public int CostOf(Slot slot) => slot == Slot.Miniboss ? MinibossAliveCost : slot == Slot.Elite ? EliteAliveCost : 1;
    /// Composition index among the regular / elite enemies (the miniboss does not shift the archetype rotation).
    public static int CompositionIndex(WavePlan plan, int index) => plan.Miniboss ? Mathf.Max(0, index - 1) : index;
}
[Serializable] public sealed class WaveModifier
{
    public string Id = "modifier", DisplayName = "MODIFIER";
    [Min(0)] public float Weight = 1f;
    [Min(1)] public int FromWave = 1;
    [Tooltip("Multipliers compose with the other modifiers of the same wave.")] public float Health = 1f, Damage = 1f, Count = 1f, Score = 1f;
    [Tooltip("Added to the wave's elite share.")] public float EliteShareBonus;
}
public sealed class WavePlan
{
    public int Wave, Size, Regulars, Elites; public bool Miniboss; public float Health, Damage, Count, Score, EliteShare;
    public List<WaveModifier> Modifiers;
    public string Names { get { var n = new List<string>(); foreach (var m in Modifiers) n.Add(m.DisplayName); return string.Join(" + ", n); } }
}
public enum EndlessScoreKind { Kill, EliteKill, MinibossKill, WaveClear, Flawless }
public readonly struct EndlessScoreEvent
{
    public readonly EndlessScoreKind Kind; public readonly int Amount, Wave;
    public EndlessScoreEvent(EndlessScoreKind kind, int amount, int wave) { Kind = kind; Amount = amount; Wave = wave; }
}
