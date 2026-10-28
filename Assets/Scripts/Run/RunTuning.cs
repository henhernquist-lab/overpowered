/// RUN PROGRESSION — the numbers. One place to tune Endless Fight; nothing here is saved.
/// The curve is deliberately geometric (the same shape as PlayerProgression.RequiredXp, which is already overflow-guarded)
/// but with its own constants, so persistent balance can never leak into a run.
public static class RunTuning
{
    /// XP needed for run level 1 -> 2. Round 1 pays 30 (three Rushers), so the first pick lands in round 2.
    public const int BaseXp = 120;
    /// Each level costs this much more than the last: 120 / 144 / 173 / 207 / 249 ...
    public const float XpGrowth = 1.20f;
    /// Levels are clamped here so a corrupt XP stream can never spin forever. Requirement saturates at int.MaxValue.
    public const int MaxLevel = 999;

    /// A Pulsebreaker pays this; it is ~2 waves of regular XP and arrives on every 5th round, worth 2-3 levels early and
    /// (with the extra Epic pick in RunProgress) the clearest "the boss made me stronger" moment of the run.
    public const int BossXp = 300;
    /// Unknown/new archetypes (a future enemy) still pay something instead of nothing.
    public const int UnknownEnemyXp = 12;

    /// Per-archetype kill XP, keyed by EnemyArchetype.DisplayName (see Assets/Resources/Enemies).
    /// The spread follows real time-to-kill: Rusher 0.6 HP, Gunner 1.0, Skirmisher 0.75 + evasion, Brute 3.0,
    /// Bulwark 1.8 + a directional guard that demands positioning.
    public static int XpFor(string archetype)
    {
        switch (archetype)
        {
            case "Rusher": return 10;
            case "Gunner": return 14;
            case "Skirmisher": return 18;
            case "Brute": return 34;
            case "Bulwark": return 40;
            case "Pulsebreaker": return BossXp;
            default: return UnknownEnemyXp;
        }
    }

    /// Rarity odds by run level. Early picks are mostly flat improvements; the pool opens up as the run goes deep.
    /// (Common, Rare, Epic) — always sums to 1.
    public static void RarityWeights(int level, out float common, out float rare, out float epic)
    {
        if (level >= 14) { common = .40f; rare = .38f; epic = .22f; }
        else if (level >= 8) { common = .50f; rare = .35f; epic = .15f; }
        else { common = .62f; rare = .30f; epic = .08f; }
    }

    /// How much a primary power card outranks a secondary/signature/hero card in the draw.
    public const float PrimaryWeight = 1f, SecondaryWeight = .55f, SignatureWeight = .6f, HeroWeight = .75f;

    /// Signature cards unlock at this run level (the signature is a mid-run payoff, not a level-1 choice).
    public const int SignatureUnlockLevel = 4;

    /// Safety floors. These are asserted by RunProgressionVerification so no card can ever break them.
    public const float MinPowerCooldown = .15f, MinSignatureCooldown = 12f, MaxFreezeSeconds = 6.5f;
    public const int MaxChainTargets = 7, MaxPoisonSpreadTargets = 5;
}
