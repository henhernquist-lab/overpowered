using System;
using System.Collections.Generic;

/// One upgrade the run owns. Stacks are the only run state that affects gameplay.
[Serializable] public struct RunUpgradeStack { public string Id; public int Count; }

/// Immutable snapshot of what the run's stacks do, rebuilt on every pick (never per frame).
/// Multiplicative stats compose as (1 + per)^stacks; additive stats (extra charges) sum.
public sealed class RunModifiers
{
    public static readonly RunModifiers Empty = new RunModifiers();

    readonly Dictionary<string, float> damage = new Dictionary<string, float>(8);
    readonly Dictionary<string, float> cooldown = new Dictionary<string, float>(8);
    readonly Dictionary<string, float> radius = new Dictionary<string, float>(8);
    readonly Dictionary<string, float> range = new Dictionary<string, float>(8);
    readonly Dictionary<string, float> duration = new Dictionary<string, float>(8);
    readonly Dictionary<string, float> energy = new Dictionary<string, float>(8);
    readonly Dictionary<string, int> charges = new Dictionary<string, int>(8);
    readonly Dictionary<RunMechanic, int> mechanics = new Dictionary<RunMechanic, int>(8);

    public float AllPowerDamage { get; private set; } = 1f;
    public float MeleeDamage { get; private set; } = 1f;
    public float MaxHealth { get; private set; } = 1f;
    public float HealOnKill { get; private set; } = 1f;
    public float MaxEnergy { get; private set; } = 1f;
    public float EnergyRegen { get; private set; } = 1f;

    public float SignatureDamage { get; private set; } = 1f;
    public float SignatureCooldown { get; private set; } = 1f;
    public float SignatureEnergy { get; private set; } = 1f;
    public float SignatureRadius { get; private set; } = 1f;

    public float Damage(string powerId) => Get(damage, powerId);
    public float Cooldown(string powerId) => Get(cooldown, powerId);
    public float Radius(string powerId) => Get(radius, powerId);
    public float Range(string powerId) => Get(range, powerId);
    public float Duration(string powerId) => Get(duration, powerId);
    public float EnergyCost(string powerId) => Get(energy, powerId);
    public int ExtraCharges(string powerId) => powerId != null && charges.TryGetValue(powerId, out var n) ? n : 0;
    public int Mechanic(RunMechanic mechanic) => mechanics.TryGetValue(mechanic, out var n) ? n : 0;

    static float Get(Dictionary<string, float> map, string id) => id != null && map.TryGetValue(id, out var v) ? v : 1f;

    /// Rebuilt from the full stack list; O(cards), called only on a pick / run start.
    public static RunModifiers Build(IReadOnlyList<RunUpgradeStack> stacks)
    {
        var m = new RunModifiers();
        if (stacks == null) return m;
        for (int i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            var def = RunUpgradeLibrary.Find(stack.Id);
            if (def == null || stack.Count <= 0) continue;
            m.Apply(def, stack.Count);
        }
        return m;
    }

    void Apply(RunUpgradeDefinition def, int count)
    {
        float mult = (float)Math.Pow(1.0 + def.PerStack, count);
        // Cooldown and energy cards are REDUCTIONS: a card that says "-12%" must multiply the cost by 1/1.12, not 1.12.
        float reduced = 1f / mult;
        string id = def.PowerId;
        switch (def.Stat)
        {
            case RunStat.PowerDamage: Multiply(damage, id, mult); break;
            case RunStat.PowerCooldown: Multiply(cooldown, id, reduced); break;
            case RunStat.PowerRadius: Multiply(radius, id, mult); break;
            case RunStat.PowerRange: Multiply(range, id, mult); break;
            case RunStat.PowerDuration: Multiply(duration, id, mult); break;
            case RunStat.PowerEnergyCost: Multiply(energy, id, reduced); break;
            case RunStat.ExtraCharges: charges[id] = ExtraCharges(id) + (int)(def.PerStack * count); break;
            case RunStat.MeleeDamage: MeleeDamage *= mult; break;
            case RunStat.AllPowerDamage: AllPowerDamage *= mult; break;
            case RunStat.MaxHealth: MaxHealth *= mult; break;
            case RunStat.HealOnKill: HealOnKill *= mult; break;
            case RunStat.MaxEnergy: MaxEnergy *= mult; break;
            case RunStat.EnergyRegen: EnergyRegen *= mult; break;
            case RunStat.SignatureDamage: SignatureDamage *= mult; break;
            case RunStat.SignatureCooldown: SignatureCooldown *= reduced; break;
            case RunStat.SignatureEnergyCost: SignatureEnergy *= reduced; break;
            case RunStat.SignatureRadius: SignatureRadius *= mult; break;
        }
        if (def.Mechanic != RunMechanic.None) mechanics[def.Mechanic] = Mechanic(def.Mechanic) + def.MechanicPerStack * count;
    }

    static void Multiply(Dictionary<string, float> map, string id, float mult)
    {
        if (string.IsNullOrEmpty(id)) return;
        map.TryGetValue(id, out var current);
        map[id] = (current <= 0f ? 1f : current) * mult;
    }
}
