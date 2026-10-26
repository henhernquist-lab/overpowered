using UnityEngine;

/// CENTRALISED HEALTH ON KILL. One funnel: CityNpc.Damage is the only place an NPC's health reaches zero, and it calls
/// OnEnemyDefeated for every death. Nothing else in the game heals the player for a defeat, so a new power can never
/// double-pay and can never forget to pay.
///   Eligible:  a real combat enemy (has an EnemyArchetype), not a civilian, HOSTILE to the player at the moment of death,
///              killed by the PLAYER's own damage, while the player is alive and the session is running.
///   Ineligible: civilians, friendly/neutral NPCs, environmental props, despawns, already-dead bodies (one claim per
///              enemy life), NPC-on-NPC kills, a player who is already dead, and anything killed by a non-player source.
/// Coefficients live on Resources/HealthOnKill (HealthOnKillTuning); the per-enemy weight is EnemyArchetype.HealOnKillMultiplier,
/// so a new enemy type is data. The Pulsebreaker (any CityNpc.IsBoss body) uses the separate boss formula.
/// Max health is read from WorldSession.MaxHealth at the moment of the kill, so run upgrades that raise it are picked up
/// by the next heal without touching this code.
public static class HealthOnKill
{
    public const string ResourcePath = "HealthOnKill";

    static HealthOnKillTuning tuning;
    /// The active coefficients (null only when the asset is missing: healing then does nothing rather than guessing).
    public static HealthOnKillTuning Tuning => tuning != null ? tuning : tuning = Resources.Load<HealthOnKillTuning>(ResourcePath);

    /// Credited defeats that paid healing (verification/diagnostics; gameplay never reads these).
    public static int Kills { get; private set; }
    public static float TotalHealed { get; private set; }
    public static float LastHeal { get; private set; }
    public static NpcRole LastRole { get; private set; }
    /// Why the most recent death paid nothing ("civilian", "not hostile", "player dead", "already claimed", "not player-owned", ...).
    public static string LastBlocked { get; private set; }

    /// Drop the cached asset and the telemetry (verification only).
    public static void ResetTelemetry()
    {
        tuning = null; Kills = 0; TotalHealed = 0f; LastHeal = 0f; LastBlocked = null; LastRole = NpcRole.Civilian;
    }

    /// The resolved heal for one credited defeat. Pure: never touches health. Exposed so tests can table-drive it.
    public static float Resolve(float maxHealth, float currentHealth, float archetypeMultiplier, bool boss)
    {
        var asset = Tuning;
        return asset == null ? 0f : asset.Resolve(maxHealth, currentHealth, archetypeMultiplier, boss);
    }

    /// Called by CityNpc.Damage for every death, exactly once per enemy life. Returns the health actually restored.
    public static float OnEnemyDefeated(WorldSession world, CityNpc victim, PowerUser source)
    {
        if (world == null || victim == null) { LastBlocked = "no world or victim"; return 0f; }
        if (!PlayerOwned(world, source)) { LastBlocked = "not player-owned"; return 0f; }
        if (victim.KillRewardClaimed) { LastBlocked = "already claimed"; return 0f; }
        if (!victim.Dead) { LastBlocked = "not dead"; return 0f; }
        if (victim.Role == NpcRole.Civilian) { LastBlocked = "civilian"; return 0f; }
        if (victim.Archetype == null) { LastBlocked = "not a combat enemy"; return 0f; }
        if (!victim.Hostile) { LastBlocked = "not hostile"; return 0f; }
        if (world.PlayerDead) { LastBlocked = "player dead"; return 0f; }
        var asset = Tuning;
        if (asset == null) { LastBlocked = "missing " + ResourcePath; return 0f; }
        // Claim even when the heal resolves to 0 (killing at full health): one reward per enemy life, never a saved-up one.
        victim.ClaimKillReward();
        LastBlocked = null; LastRole = victim.Role;
        float heal = asset.Resolve(world.MaxHealth, world.Health, victim.Archetype.HealOnKillMultiplier, victim.IsBoss);
        float applied = world.HealPlayer(heal);
        LastHeal = applied;
        if (applied > 0f) { Kills++; TotalHealed += applied; }
        return applied;
    }

    /// The player's own damage. PowerUser lives on the hero, so this stays true for every direct, channelled and delayed
    /// (poison burn, held beam, synergy tick) player-owned effect.
    static bool PlayerOwned(WorldSession world, PowerUser source)
    {
        if (source == null) return false;
        if (source == world.Powers) return true;
        return world.Hero != null && source.gameObject == world.Hero.gameObject;
    }
}

/// Once-per-enemy-life claim. CityNpc bodies are created per spawn (never pooled), so an instance flag is exact: a reused
/// body still needs a fresh NPC, and a doubled death notification pays at most once.
public sealed partial class CityNpc
{
    /// True once this body's death has consumed its health-on-kill reward (or been refused one).
    public bool KillRewardClaimed { get; private set; }

    internal void ClaimKillReward() { KillRewardClaimed = true; }
}
