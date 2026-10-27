#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

/// See Assets/Editor/HealthOnKillVerification.cs. Proves the health-on-kill formula against the SHIPPING coefficients, then
/// pays it through the real damage funnel in a real Free Play session: per-archetype values, the missing-health cap, a
/// full-health zero, one payment per enemy life, civilians / neutrals / non-player kills / despawns / a dead player paying
/// nothing, the miniboss formula, the HUD number from the authoritative heal, and a second hero with a different MaxHealth.
public sealed class HealthOnKillVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/HealthOnKill/";
    protected override string ResultFile => "results.txt";
    GameHud Hud => FindAnyObjectByType<GameHud>();

    protected override IEnumerator Checks()
    {
        HealthOnKill.ResetTelemetry();
        yield return Scene(GameFlow.HomeScene);
        Table();
        yield return Live();
        yield return RealTimeHud();
        yield return SecondHero();
        Log("LIMIT: no hardware keyboard/mouse in batch mode. Healing is measured at the shared CityNpc.Damage death funnel (the boundary every power and delayed effect uses) in a real Home -> Free Play session, with one REAL poison cast as the power-owned control: signatures, telekinesis impacts and synergy ticks reach the same call but were not cast here. Human feel/pacing of sustain is UNVERIFIED. The 0.18/0.35 caps never bind at the current coefficients (raw <= 0.168M / 0.35M); they are asserted as the guard they are.");
    }

    // ------------------------------------------------------------------ pure formula
    void Table()
    {
        Log("---- FORMULA (Resources/HealthOnKill, M=100 H=50)");
        var asset = HealthOnKill.Tuning;
        Check(asset != null, "HealthOnKill asset loads.");
        Check(Mathf.Abs(asset.BaseFraction - .04f) < 1e-6f && Mathf.Abs(asset.MissingFraction - .08f) < 1e-6f && Mathf.Abs(asset.CapFraction - .18f) < 1e-6f &&
              Mathf.Abs(asset.BossBaseFraction - .2f) < 1e-6f && Mathf.Abs(asset.BossMissingFraction - .15f) < 1e-6f && Mathf.Abs(asset.BossCapFraction - .35f) < 1e-6f,
            $"Shipping coefficients: 0.04 M + 0.08 missing, cap 0.18 M; boss 0.20 M + 0.15 missing, cap 0.35 M.");
        void Row(string name, float multiplier, float expected) =>
            Check(Mathf.Abs(HealthOnKill.Resolve(100f, 50f, multiplier, false) - expected) < 1e-4f, $"{name} x{multiplier}: {HealthOnKill.Resolve(100f, 50f, multiplier, false):F2} = {expected:F2} at 50/100 HP.");
        Row("Rusher", Archetype("Rusher").HealOnKillMultiplier, 8f);
        Row("Gunner", Archetype("Gunner").HealOnKillMultiplier, 8f);
        Row("Skirmisher", Archetype("Skirmisher").HealOnKillMultiplier, 9.2f);
        Row("Brute", Archetype("Brute").HealOnKillMultiplier, 10.4f);
        Row("Bulwark", Archetype("Bulwark").HealOnKillMultiplier, 11.2f);
        Check(Mathf.Abs(HealthOnKill.Resolve(100f, 50f, Archetype("Pulsebreaker").HealOnKillMultiplier, true) - 27.5f) < 1e-4f,
            $"Pulsebreaker (boss formula, its own multiplier ignored): {HealthOnKill.Resolve(100f, 50f, 1f, true):F2} = 27.50 at 50/100 HP.");
        Check(HealthOnKill.Resolve(100f, 100f, 1.4f, false) == 0f && HealthOnKill.Resolve(100f, 100f, 1f, true) == 0f, "Full health -> 0 for a normal enemy and the boss.");
        Check(Mathf.Abs(HealthOnKill.Resolve(100f, 99f, 1f, false) - 1f) < 1e-4f, $"Near full (99/100): bounded by the missing 1 HP -> {HealthOnKill.Resolve(100f, 99f, 1f, false):F3}.");
        Check(Mathf.Abs(HealthOnKill.Resolve(100f, 0f, 1.4f, false) - 16.8f) < 1e-4f && Mathf.Abs(HealthOnKill.Resolve(100f, 0f, 1f, true) - 35f) < 1e-4f,
            "Caps: normal 16.80 <= 0.18 M, boss 35.00 = 0.35 M at 0 health.");
        Check(Mathf.Abs(HealthOnKill.Resolve(140f, 70f, 1f, false) - 11.2f) < 1e-4f && Mathf.Abs(HealthOnKill.Resolve(140f, 70f, 1f, true) - 38.5f) < 1e-4f,
            "Max-health scaling (TITAN 140): normal 11.20, boss 38.50 at half health.");
        Check(HealthOnKill.Resolve(0f, 0f, 1f, false) == 0f && HealthOnKill.Resolve(100f, 50f, 0f, false) == 0f, "Zero max health and a zero archetype multiplier both resolve to 0.");
    }

    static EnemyArchetype Archetype(string name) => Resources.Load<EnemyArchetype>("Enemies/" + name);

    // ------------------------------------------------------------------ live session
    IEnumerator Live()
    {
        Log("---- LIVE (real Free Play session, real CityNpc.Damage death funnel)");
        yield return Enter(F.Heroes[0], "poison", "strength", "free-play");
        Isolate();
        for (int i = 0; i < 4; i++) yield return null;
        var hud = Hud;
        Check(hud != null, "GameHud present in the live session.");
        float max = W.MaxHealth;
        Log($"MEASURED hero {W.Powers.HeroDefinition.Id}: MaxHealth {max:F2} = Movement.Health {W.Tuning.Movement.Health:F1} x archetype {W.Powers.HeroStats.MaxHealth:F2} x difficulty {W.Difficulty.PlayerHealth:F2}.");
        Check(Mathf.Abs(max - W.Tuning.Movement.Health * W.Powers.HeroStats.MaxHealth * W.Difficulty.PlayerHealth) < 1e-3f,
            "Healing reads the SAME authoritative maximum (hero archetype x difficulty) the HUD health bar uses.");

        // Every regular archetype pays exactly its multiplier at half health.
        foreach (var name in new[] { "Rusher", "Gunner", "Skirmisher", "Brute", "Bulwark" })
        {
            SetTo(max * .5f);
            var enemy = Enemy(name);
            float before = W.Health, expected = HealthOnKill.Resolve(max, before, enemy.Archetype.HealOnKillMultiplier, false);
            float applied = Kill(enemy, W.Powers);
            Check(Mathf.Abs(applied - expected) < 1e-3f && Mathf.Abs(W.Health - (before + expected)) < 1e-3f && W.Health <= max,
                $"Live {name} kill at {before:F1}/{max:F1}: +{applied:F2} HP (expected {expected:F2} = multiplier {enemy.Archetype.HealOnKillMultiplier:F2}); health now {W.Health:F2} <= {max:F2}.");
            Check(enemy.KillRewardClaimed && HealthOnKill.LastBlocked == null, $"{name} death consumed its one reward claim.");
        }

        // A REAL poison cast: the kill comes from the power's own damage-over-time ticks, not from a test damage call.
        SetTo(max * .5f);
        var poisoned = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Criminal, Archetype("Rusher"), 50f, 0f);
        poisoned.enabled = false; poisoned.Agent.enabled = false;
        poisoned.transform.position = new Vector3(0f, 150f, 7f); poisoned.SetCombatStats(3f, 0f); Physics.SyncTransforms();
        Cam.transform.position = W.Hero.transform.position + new Vector3(0f, 1.7f, -1f);
        Cam.transform.LookAt(Chest(poisoned)); Physics.SyncTransforms();
        var poison = Runtime("poison");
        Check(poison != null && W.Powers.Select(poison), "poison selected for the real-power kill control.");
        W.Powers.Tick(100f, true);
        int poisonCharges = poison.Charges;
        Check(W.Powers.Use(poison) && poison.Charges == poisonCharges - 1, "Real poison cast accepted and one charge paid.");
        if (W.Powers.Channeling == poison)
        {
            float hold = Time.time + .45f;
            while (Time.time < hold) { W.Powers.Channel(true, Time.deltaTime); yield return null; }
            W.Powers.Channel(false, Time.deltaTime);
        }
        string lastSource = null; float poisonBefore = W.Health;
        System.Action<NpcDamageResult> watch = r => { if (r.Target == poisoned) lastSource = r.SourceId; };
        CityNpc.DamageApplied += watch;
        float poisonUntil = Time.time + 8f;
        while (poisoned != null && !poisoned.Dead && Time.time < poisonUntil) yield return null;
        CityNpc.DamageApplied -= watch;
        Check(poisoned == null || poisoned.Dead, $"A real poison application killed the target over time ({(poisoned == null ? "destroyed" : "dead")}).");
        Check(lastSource == "poison", $"The killing damage was credited to the poison power ('{lastSource}').");
        Check(Mathf.Abs(W.Health - (poisonBefore + HealthOnKill.Resolve(max, poisonBefore, 1f, false))) < 1e-3f,
            $"Poison kill healed {W.Health - poisonBefore:F2} at {poisonBefore:F1}/{max:F1} (the same funnel pays delayed power damage).");

        // Full health pays nothing, but the claim is still spent.
        W.HealPlayer(max);
        Check(Mathf.Abs(W.Health - max) < 1e-3f, "HealPlayer to full: health equals MaxHealth.");
        var fullKill = Enemy("Rusher");
        int shownAtFull = hud.HealNumbersShown;
        float fullApplied = Kill(fullKill, W.Powers);
        Check(fullApplied == 0f && Mathf.Abs(W.Health - max) < 1e-3f && fullKill.KillRewardClaimed && HealthOnKill.LastHeal == 0f && hud.HealNumbersShown == shownAtFull,
            $"At full health a kill heals {fullApplied:F2} (claim spent, no HUD number, health stays {W.Health:F2}).");

        // Near full: the missing-health bound, not the raw value (Brute raw ~5.3 at 1 missing).
        SetTo(max - 1f);
        var nearKill = Enemy("Brute");
        float nearApplied = Kill(nearKill, W.Powers);
        Check(Mathf.Abs(nearApplied - 1f) < 1e-3f && Mathf.Abs(W.Health - max) < 1e-3f, $"At {max - 1f:F1}/{max:F1} a Brute kill heals exactly the missing 1 HP ({nearApplied:F3}).");

        // Three kills in one frame each pay at the state the previous one left.
        SetTo(max * .5f);
        var crowd = new[] { Enemy("Rusher"), Enemy("Rusher"), Enemy("Gunner") };
        float crowdBefore = W.Health, expectedTotal = 0f;
        foreach (var npc in crowd)
        {
            expectedTotal += HealthOnKill.Resolve(max, W.Health, npc.Archetype.HealOnKillMultiplier, false);
            Kill(npc, W.Powers);
        }
        Check(Mathf.Abs(W.Health - Mathf.Min(max, crowdBefore + expectedTotal)) < 1e-2f && W.Health <= max,
            $"Same-frame multi-kill: {crowdBefore:F2} -> {W.Health:F2} (+{W.Health - crowdBefore:F2}, expected +{expectedTotal:F2}); capped by MaxHealth.");
        Check(hud.HealNumbersShown >= shownAtFull + crowd.Length, $"HUD started a number per applied heal ({shownAtFull} -> {hud.HealNumbersShown}).");

        // One payment per enemy life: a second notification and further damage pay nothing.
        var claimed = Enemy("Rusher");
        Kill(claimed, W.Powers);
        float settled = W.Health;
        float again = HealthOnKill.OnEnemyDefeated(W, claimed, W.Powers);
        claimed.Damage(5f, W.Powers);
        Check(again == 0f && HealthOnKill.LastBlocked == "already claimed" && W.Health == settled, "A repeated death notification pays 0 ('already claimed') and later damage cannot pay twice.");

        // Neutrals and civilians pay nothing. The cop is checked FIRST: its death raises Heat, which would make the next
        // patrol cop hostile and invalidate the neutral control.
        Check(W.Stars == 0, $"Neutral CONTROL: 0 Heat stars ({W.Stars}) before the friendly-cop check.");
        var cop = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Cop);
        cop.enabled = false; cop.Agent.enabled = false; cop.transform.position = new Vector3(-1f, 150f, 0f); cop.SetCombatStats(10f, 0f); Physics.SyncTransforms();
        Check(!cop.Hostile, "The Heat-0 patrol cop is not hostile (Hero side).");
        float hp = W.Health; Kill(cop, W.Powers);
        Check(W.Health == hp && HealthOnKill.LastBlocked == "not hostile", "Neutral cop death: 0 healing ('not hostile').");
        var civilian = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Civilian);
        civilian.enabled = false; civilian.Agent.enabled = false; civilian.transform.position = new Vector3(1f, 150f, 0f); civilian.SetCombatStats(10f, 0f); Physics.SyncTransforms();
        hp = W.Health;
        Kill(civilian, W.Powers);
        Check(W.Health == hp && HealthOnKill.LastBlocked == "civilian", "Civilian death: 0 healing ('civilian').");
        var orphan = Enemy("Rusher");
        hp = W.Health; orphan.Damage(99999f, null);
        Check(orphan.Dead && W.Health == hp && HealthOnKill.LastBlocked == "not player-owned", "A death with no player source (NPC-on-NPC equivalent): 0 healing ('not player-owned').");
        var despawned = Enemy("Rusher");
        hp = W.Health; Destroy(despawned.gameObject);
        for (int i = 0; i < 3; i++) yield return null;
        Check(W.Health == hp, "Despawning an enemy pays nothing (no death notification).");

        // A dead player cannot be healed by a late kill.
        var late = Enemy("Brute");
        W.DamagePlayer(W.Health, true);
        Check(W.PlayerDead && W.Health == 0f, "Player killed (unblockable) for the same-frame control.");
        float deadHeal = Kill(late, W.Powers);
        Check(deadHeal == 0f && W.Health == 0f && HealthOnKill.LastBlocked == "player dead", $"Kill while dead: 0 healing ('{HealthOnKill.LastBlocked}'), player stays dead.");
        float until = Time.realtimeSinceStartup + W.Tuning.Movement.RespawnDelay + 5f;
        while (W.PlayerDead && Time.realtimeSinceStartup < until) yield return null;
        Check(!W.PlayerDead && Mathf.Abs(W.Health - W.MaxHealth) < 1e-3f, "Existing respawn restored full health; healing did not interfere.");

        // The miniboss formula, through a real Pulsebreaker body.
        SetTo(max * .5f);
        var bossNpc = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Criminal, Archetype("Pulsebreaker"), 400f, 0f);
        bossNpc.enabled = false; bossNpc.Agent.enabled = false; bossNpc.transform.position = new Vector3(3f, 150f, 0f); Physics.SyncTransforms();
        Check(PulsebreakerBoss.AttachForVerification(bossNpc) != null && bossNpc.IsBoss, "Pulsebreaker body attached outside the wave director (verification entry point).");
        float bossBefore = W.Health, bossExpected = HealthOnKill.Resolve(max, bossBefore, bossNpc.Archetype.HealOnKillMultiplier, true);
        float bossApplied = Kill(bossNpc, W.Powers);
        Check(Mathf.Abs(bossApplied - bossExpected) < 1e-3f && Mathf.Abs(bossApplied - (.2f * max + .15f * (max - bossBefore))) < 1e-3f && bossApplied > 3f * HealthOnKill.Resolve(max, bossBefore, 1f, false),
            $"Pulsebreaker kill at {bossBefore:F1}/{max:F1}: +{bossApplied:F2} HP = 0.20 M + 0.15 missing (normal formula would be {HealthOnKill.Resolve(max, bossBefore, 1f, false):F2}).");
        until = Time.realtimeSinceStartup + 15f;
        while (PulsebreakerBoss.Active != null && Time.realtimeSinceStartup < until) yield return null;
        Check(PulsebreakerBoss.Active == null, "Boss body cleaned up after death (no lingering boss).");

        Check(hud.LastHealShown > 0f && hud.HealNumbersShown > 0, $"HUD feedback came from real heals: {hud.HealNumbersShown} numbers, last '+{hud.LastHealShown:F2} HP'.");
        Check(HealthOnKill.Kills > 0 && HealthOnKill.TotalHealed > 0f, $"Telemetry: {HealthOnKill.Kills} credited kills paid {HealthOnKill.TotalHealed:F2} HP in total.");
    }

    // ------------------------------------------------------------------ real time
    /// The user-facing question: does the SHIPPING HUD show the heal? A real Endless wave (the mode healing is for), the
    /// real GameHud, one kill through the player's own PowerUser, then the bar sampled on the next frame — plus the same
    /// sample for damage as a control.
    IEnumerator RealTimeHud()
    {
        Log("---- REAL TIME (real Endless wave, shipping GameHud)");
        yield return Enter(F.Heroes[0], "ice", "strength", "endless-fight");
        var state = W.Mode.Director as EndlessWaveState;
        Check(state != null, "Endless Fight is running its wave director.");
        float until = Time.time + 30f;
        while ((state.Intermission || state.Wave < 1 || state.Alive.Count < 1) && Time.time < until) yield return null;
        Check(!state.Intermission && state.Wave >= 1 && state.Alive.Count >= 1, $"Wave {state.Wave} is live with {state.Alive.Count} enemy(ies).");
        var hud = Hud;
        Check(hud != null, "GameHud present in the live Endless session.");
        float max = W.MaxHealth;
        W.DamagePlayer(max * .5f, true);
        yield return null;
        float damageFill = FillPercent(hud);
        Check(Mathf.Abs(damageFill - W.Health / max * 100f) < .1f,
            $"CONTROL: damage reaches the bar on the next frame — fill {damageFill:F1}% = health {W.Health:F1}/{max:F1} ({W.Health / max * 100f:F1}%).");
        var victim = state.Alive.FirstOrDefault(n => n != null && !n.Dead && n.Archetype != null);
        Check(victim != null, "A live wave enemy is available to defeat.");
        int numbers = hud.HealNumbersShown;
        float before = W.Health, expected = HealthOnKill.Resolve(max, before, victim.Archetype.HealOnKillMultiplier, victim.IsBoss);
        victim.Damage(99999f, W.Powers);
        Check(Mathf.Abs(W.Health - Mathf.Min(max, before + expected)) < .01f,
            $"The kill raised health at once: {before:F2} -> {W.Health:F2} (+{W.Health - before:F2}, expected +{expected:F2}).");
        yield return null;
        float healFill = FillPercent(hud);
        Check(Mathf.Abs(healFill - W.Health / max * 100f) < .1f,
            $"The bar shows the heal on the next frame: {damageFill:F1}% -> {healFill:F1}% = health {W.Health:F1}/{max:F1} ({W.Health / max * 100f:F1}%).");
        Check(hud.HealNumbersShown == numbers + 1, $"One '+N HP' number started for the heal ({numbers} -> {hud.HealNumbersShown}).");
        Check(hud.HealNumbers.Any(d => d.Active && GameHud.Shown(d.Element)), "The heal number is visible on screen (not just counted).");
        Check(hud.HealFlashStrength > 0f, $"The health bar flashes on the heal (strength {hud.HealFlashStrength:F2}), so a small jump still reads.");
        yield return new WaitForSecondsRealtime(GameHud.HealFlashSeconds + .1f);
        Check(hud.HealFlashStrength == 0f, "The flash ends on its own and the bar returns to its normal colour.");
    }

    static float FillPercent(GameHud hud) => hud.HealthFill.style.width.value.value;

    // ------------------------------------------------------------------ second hero
    IEnumerator SecondHero()
    {
        Log("---- SECOND HERO (a different MaxHealth uses the same formula)");
        var titan = F.Hero("titan");
        Check(titan != null && titan.Stats.MaxHealth > 1f, $"TITAN max-health multiplier {titan?.Stats.MaxHealth:F2} (> 1).");
        yield return Enter(titan, "ice", "strength", "free-play");
        Isolate();
        for (int i = 0; i < 4; i++) yield return null;
        float max = W.MaxHealth;
        Check(Mathf.Abs(max - W.Tuning.Movement.Health * titan.Stats.MaxHealth * W.Difficulty.PlayerHealth) < 1e-3f && max > W.Tuning.Movement.Health * 1.2f,
            $"TITAN session MaxHealth {max:F2} is larger than the baseline health pool.");
        SetTo(max * .5f);
        var enemy = Enemy("Rusher");
        float before = W.Health, expected = HealthOnKill.Resolve(max, before, enemy.Archetype.HealOnKillMultiplier, false);
        float applied = Kill(enemy, W.Powers);
        Check(Mathf.Abs(applied - expected) < 1e-3f && applied > 8f,
            $"TITAN {enemy.Archetype.DisplayName} kill at {before:F1}/{max:F1}: +{applied:F2} HP (same percentages, bigger pool; Vector would have paid 8.00).");
    }

    // ------------------------------------------------------------------ helpers
    float SetTo(float target)
    {
        if (W.Health > target) W.DamagePlayer(W.Health - target);
        else W.HealPlayer(target - W.Health);
        return W.Health;
    }

    float Kill(CityNpc npc, PowerUser source)
    {
        float before = W.Health;
        npc.Damage(99999f, source);
        return W.Health - before;
    }

    CityNpc Enemy(string archetype)
    {
        var npc = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Criminal, Archetype(archetype), 50f, 0f);
        if (npc == null) throw new System.Exception("Enemy spawn failed: " + archetype);
        npc.enabled = false; npc.Agent.enabled = false;
        npc.transform.position = new Vector3(2f, 150f, 0f); Physics.SyncTransforms();
        if (!npc.Hostile) throw new System.Exception(archetype + " spawned non-hostile");
        return npc;
    }
}
#endif
