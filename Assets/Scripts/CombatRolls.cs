using System;
using UnityEngine;

/// Small symmetric ranges around authored means. Scaling a mean scales both bounds equally.
[Serializable]
public sealed class CombatRangeSettings
{
    [Range(0f, .5f)] public float EnemyHealthVariance = .1f;
    [Range(0f, .5f)] public float PlayerDamageVariance = .1f;
    public static float Resolve(float mean, float variance, float unit)
        => Mathf.Max(0f, mean) * (1f + Mathf.Clamp(variance, 0f, .5f) * (2f * Mathf.Clamp01(unit) - 1f));
}

/// Session-owned streams: combat never consumes UnityEngine.Random or the city/layout RNG.
/// An explicit fixed percentile supports exact regression controls; seeded streams test real variation.
public sealed class CombatRolls
{
    uint healthState, damageState;
    public float? FixedUnit { get; set; }
    public int HealthSamples { get; private set; }
    public int DamageSamples { get; private set; }
    public CombatRolls(int seed) { Seed(seed); }
    public void Seed(int seed)
    {
        healthState = unchecked((uint)seed) ^ 0xA341316Cu;
        damageState = unchecked((uint)seed) ^ 0xC8013EA4u;
        if (healthState == 0) healthState = 1;
        if (damageState == 0) damageState = 1;
        HealthSamples = DamageSamples = 0;
    }
    static float Next(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return (state >> 8) * (1f / 16777216f);
    }
    public float Health(float mean, CombatRangeSettings settings)
    { HealthSamples++; return CombatRangeSettings.Resolve(mean, settings.EnemyHealthVariance, FixedUnit ?? Next(ref healthState)); }
    public float Damage(float mean, CombatRangeSettings settings)
    { DamageSamples++; return CombatRangeSettings.Resolve(mean, settings.PlayerDamageVariance, FixedUnit ?? Next(ref damageState)); }
    public static CombatRolls Create()
    {
        var rolls = new CombatRolls(Environment.TickCount);
#if UNITY_EDITOR
        // Explicit test-process injection only; never enabled implicitly in ordinary batch or player sessions.
        if (float.TryParse(Environment.GetEnvironmentVariable("OP_VERIFY_COMBAT_ROLL"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float unit))
            rolls.FixedUnit = Mathf.Clamp01(unit);
#endif
        return rolls;
    }
}
