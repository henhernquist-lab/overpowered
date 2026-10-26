using UnityEngine;

/// Health restored when the PLAYER defeats a hostile enemy. Every coefficient lives on this asset
/// (Resources/HealthOnKill) and every archetype carries its own multiplier (EnemyArchetype.HealOnKillMultiplier), so a new
/// enemy type is data, never a code change.
///   Normal: min(W * (Base*Max + Missing*MissingHP), Cap*Max, MissingHP)
///   Boss:   min(BossBase*Max + BossMissing*MissingHP, BossCap*Max, MissingHP)
/// Max is the player's CURRENT authoritative maximum (hero archetype x difficulty), never a constant, so a future upgrade
/// that raises max health is picked up by the next kill automatically.
[CreateAssetMenu(menuName = "Overpowered/Health On Kill")]
public sealed class HealthOnKillTuning : ScriptableObject
{
    [Header("Normal enemies")]
    [Tooltip("Fraction of maximum health restored by one kill, before the archetype multiplier.")] public float BaseFraction = .04f;
    [Tooltip("Fraction of MISSING health restored by one kill, before the archetype multiplier.")] public float MissingFraction = .08f;
    [Tooltip("Ceiling of one normal kill's heal, as a fraction of maximum health.")] public float CapFraction = .18f;
    [Header("Miniboss (Pulsebreaker and any future boss)")]
    [Tooltip("Fraction of maximum health restored by a boss kill (the archetype multiplier does not apply).")] public float BossBaseFraction = .20f;
    [Tooltip("Fraction of MISSING health restored by a boss kill.")] public float BossMissingFraction = .15f;
    [Tooltip("Ceiling of one boss kill's heal, as a fraction of maximum health.")] public float BossCapFraction = .35f;

    /// Resolved heal for one credited defeat. Never negative, never more than the missing health.
    public float Resolve(float maxHealth, float currentHealth, float archetypeMultiplier, bool boss)
    {
        if (maxHealth <= 0f) return 0f;
        float missing = Mathf.Max(0f, maxHealth - currentHealth);
        if (missing <= 0f) return 0f;
        float raw = (boss ? BossBaseFraction : BaseFraction) * maxHealth + (boss ? BossMissingFraction : MissingFraction) * missing;
        if (!boss) raw *= Mathf.Max(0f, archetypeMultiplier);
        float cap = (boss ? BossCapFraction : CapFraction) * maxHealth;
        return Mathf.Clamp(Mathf.Min(raw, cap, missing), 0f, missing);
    }

    void OnValidate()
    {
        BaseFraction = Mathf.Max(0f, BaseFraction); MissingFraction = Mathf.Max(0f, MissingFraction); CapFraction = Mathf.Clamp01(CapFraction);
        BossBaseFraction = Mathf.Max(0f, BossBaseFraction); BossMissingFraction = Mathf.Max(0f, BossMissingFraction); BossCapFraction = Mathf.Clamp01(BossCapFraction);
    }
}
