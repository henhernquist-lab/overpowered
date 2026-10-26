using UnityEngine;

/// Which composition rule a wave follows.
/// WeightTable = the director's own Composition weights (the pre-2.0 behaviour, unchanged).
/// Standard / Endless = the wave curves on EnemyCompositionCurve.
public enum CompositionCurveKind { WeightTable = 0, Standard = 1, Endless = 2 }

/// The five regular combat archetypes with the round each one unlocks and a wave-dependent weight curve.
///
/// The curves make the enemy mix a function of the wave instead of one fixed ratio: early rounds are basic melee and
/// ranged pressure, the agile (Skirmisher) and shielded (Bulwark) archetypes ramp in later, and Standard converges on a
/// mixed late-game roster while Endless pushes variety harder. Weights are always finite, non-negative and normalised
/// over the archetypes that are actually unlocked in that wave, so a locked enemy can never be drawn, the sum is always
/// 1 (or 0 when nothing is available), and the director's deterministic weighted round-robin keeps an exact, repeatable
/// wave mix.
[System.Serializable]
public sealed class EnemyCompositionCurve
{
    [Header("Archetypes (an empty slot is never spawned by this curve)")]
    public EnemyArchetype Rusher, Gunner, Brute, Skirmisher, Bulwark;
    [Header("First round each archetype is allowed (the Rusher is always allowed)")]
    [Min(1)] public int GunnerFromWave = 2;
    [Min(1)] public int BruteFromWave = 3;
    [Min(1)] public int SkirmisherFromWave = 3;
    [Min(1)] public int BulwarkFromWave = 5;
    [Tooltip("Floor for the Rusher weight, so the basic archetype never disappears from the pool.")]
    [Range(0f, .5f)] public float MinRusherWeight = .05f;

    public const int Slots = 5;

    public EnemyArchetype At(int slot) =>
        slot == 0 ? Rusher : slot == 1 ? Gunner : slot == 2 ? Brute : slot == 3 ? Skirmisher : Bulwark;

    public int FromWave(int slot) =>
        slot == 0 ? 1 : slot == 1 ? GunnerFromWave : slot == 2 ? BruteFromWave : slot == 3 ? SkirmisherFromWave : BulwarkFromWave;

    public bool Unlocked(int slot, int wave) => At(slot) != null && wave >= Mathf.Max(1, FromWave(slot));

    public int UnlockedCount(int wave)
    {
        int count = 0;
        for (int slot = 0; slot < Slots; slot++) if (Unlocked(slot, wave)) count++;
        return count;
    }

    /// Normalised weight of one slot at `wave` for `kind`. 0 when the slot is empty or still locked.
    public float Weight(CompositionCurveKind kind, int slot, int wave)
    {
        var weights = new float[Slots];
        return Weights(kind, wave, weights) ? weights[slot] : 0f;
    }

    /// Fills `into` (length >= Slots) with normalised weights for `wave`. Returns false when nothing is available.
    /// Every early return leaves `into` fully written, so callers never read stale weights.
    public bool Weights(CompositionCurveKind kind, int wave, float[] into)
    {
        for (int slot = 0; slot < Slots; slot++) into[slot] = 0f;
        wave = Mathf.Max(1, wave);
        float total = 0f;
        for (int slot = 0; slot < Slots; slot++)
        {
            if (!Unlocked(slot, wave)) continue;
            float raw = Raw(kind, slot, wave);
            if (float.IsNaN(raw) || float.IsInfinity(raw) || raw < 0f) raw = 0f;
            into[slot] = raw; total += raw;
        }
        if (total > 1e-6f)
        {
            for (int slot = 0; slot < Slots; slot++) into[slot] /= total;
            return true;
        }
        // Degenerate configuration (no weight anywhere): fall back to an even share of what is unlocked, so the pool is
        // never silently empty while archetypes exist.
        int unlocked = UnlockedCount(wave);
        if (unlocked == 0) return false;
        float even = 1f / unlocked;
        for (int slot = 0; slot < Slots; slot++) into[slot] = Unlocked(slot, wave) ? even : 0f;
        return true;
    }

    /// The published curve, before normalisation.
    public float Raw(CompositionCurveKind kind, int slot, int wave)
    {
        if (kind == CompositionCurveKind.Standard) return Standard(slot, wave);
        if (kind == CompositionCurveKind.Endless) return Endless(slot, wave);
        return 0f;
    }

    /// Standard campaign-shaped curve: a slow, smooth hand-over from basic enemies to a mixed late roster.
    /// Rounds 1-2 are the teaching wave, 3-4 introduce the agile and heavy archetypes, and from round 5 the weights
    /// slide until round 20 (t = 1) and stay there.
    public float Standard(int slot, int wave)
    {
        if (wave <= 2) { if (slot == 0) return .48f; if (slot == 1) return .34f; if (slot == 2) return .18f; return 0f; }
        if (wave <= 4) { if (slot == 0) return .39f; if (slot == 1) return .29f; if (slot == 2) return .19f; if (slot == 3) return .13f; return 0f; }
        float t = Mathf.Clamp01((wave - 5f) / 15f);
        if (slot == 0) return .32f - .07f * t;
        if (slot == 1) return .29f - .03f * t;
        if (slot == 2) return .19f;
        if (slot == 3) return .12f + .05f * t;
        return .08f + .05f * t;
    }

    /// Endless curve: variety ramps harder. Gunners and Brutes hold a fixed share while Skirmishers and Bulwarks grow
    /// (capped) and the Rusher absorbs the remainder - never negative, never gone.
    public float Endless(int slot, int wave)
    {
        if (wave <= 2) { if (slot == 0) return .48f; if (slot == 1) return .34f; if (slot == 2) return .18f; return 0f; }
        if (wave <= 4) { if (slot == 0) return .38f; if (slot == 1) return .30f; if (slot == 2) return .20f; if (slot == 3) return .12f; return 0f; }
        float skirmisher = Mathf.Min(.22f, .12f + .005f * (wave - 5));
        float bulwark = Mathf.Min(.16f, .08f + .004f * (wave - 5));
        float gunner = .28f, brute = .20f;
        float rusher = Mathf.Max(Mathf.Max(0f, MinRusherWeight), 1f - (skirmisher + bulwark + gunner + brute));
        if (slot == 0) return rusher;
        if (slot == 1) return gunner;
        if (slot == 2) return brute;
        if (slot == 3) return skirmisher;
        return bulwark;
    }

    void OnValidate()
    {
        GunnerFromWave = Mathf.Max(1, GunnerFromWave); BruteFromWave = Mathf.Max(1, BruteFromWave);
        SkirmisherFromWave = Mathf.Max(1, SkirmisherFromWave); BulwarkFromWave = Mathf.Max(1, BulwarkFromWave);
    }
}
