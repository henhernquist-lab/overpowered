#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// See Assets/Editor/RunProgressionVerification.cs. Proves the run layer against the SHIPPING catalog and a real Endless
/// session: every run starts at level 1 even on a levelled account (and ignores persistent tiers), credited kills pay their
/// table XP exactly once, a level pauses the fight and offers three eligible cards, a pick changes the real power stats, a
/// Pulsebreaker pays its XP + an all-Epic milestone pick and its bar reads the authoritative health, the next run starts
/// clean, Free Play gets no run, and the save shape is untouched.
public sealed class RunProgressionVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/RunProgression/";
    protected override string ResultFile => "results.txt";
    GameHud Hud => FindAnyObjectByType<GameHud>();
    readonly List<PowerDefinition> combatPowers = new List<PowerDefinition>();
    RunProgress run;

    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        Catalogue();
        Maths();
        ShippingModel();
        yield return FreshRun();
        yield return LevelUps();
        yield return Boss();
        ResetAndSave();
        yield return FreePlay();
        yield return Heroes();
        yield return Difficulty();
        Perf();
        Limit();
    }

    // ------------------------------------------------------------------ catalog
    void Catalogue()
    {
        Log("---- CATALOG (Resources/Powers vs Assets/Scripts/Run/RunUpgradeDefinition.cs)");
        var powers = Resources.LoadAll<PowerDefinition>("Powers");
        combatPowers.Clear();
        foreach (var p in powers) if (p != null && p.Role == PowerRole.Combat) combatPowers.Add(p);
        var secondaries = powers.Where(p => p != null && p.Role == PowerRole.Secondary).ToList();
        Check(combatPowers.Count >= 8, $"{combatPowers.Count} combat powers ship: {string.Join(", ", combatPowers.Select(p => p.Id))}.");
        Check(secondaries.Count >= 5, $"{secondaries.Count} secondary powers ship: {string.Join(", ", secondaries.Select(p => p.Id))}.");

        foreach (var p in combatPowers)
        {
            int cards = RunUpgradeLibrary.All.Count(d => d.PowerId == p.Id);
            Check(cards >= 3, $"{p.DisplayName} has {cards} run cards (>= 3: damage, economy/area and a mechanical evolution).");
        }
        foreach (var p in secondaries)
        {
            int cards = RunUpgradeLibrary.All.Count(d => d.PowerId == p.Id);
            Check(cards >= 1, $"{p.DisplayName} (secondary) has {cards} run card(s).");
        }

        var ids = new HashSet<string>();
        var powerIds = new HashSet<string>(powers.Select(p => p.Id));
        foreach (var d in RunUpgradeLibrary.All)
        {
            Check(!string.IsNullOrEmpty(d.Id) && ids.Add(d.Id), $"Card id '{d.Id}' is unique.");
            Check(!string.IsNullOrEmpty(d.Name) && !string.IsNullOrEmpty(d.Description), $"Card '{d.Id}' has a name and a description.");
            Check(d.MaxStacks >= 1 && d.MaxStacks <= 999 && d.MinLevel >= 1, $"Card '{d.Id}' has sane stack ({d.MaxStacks}) and unlock ({d.MinLevel}) bounds.");
            if (d.IsHero) Check(d.PowerId == "", $"Card '{d.Id}' is a hero card.");
            else if (d.IsSignature) Check(d.Stat == RunStat.SignatureDamage || d.Stat == RunStat.SignatureCooldown ||
                d.Stat == RunStat.SignatureEnergyCost || d.Stat == RunStat.SignatureRadius, $"Signature card '{d.Id}' uses a signature stat.");
            else Check(powerIds.Contains(d.PowerId), $"Card '{d.Id}' targets a real power ('{d.PowerId}').");
            if (d.Stat != RunStat.None) Check(Mathf.Abs(d.PerStack) > 0f, $"Stat card '{d.Id}' has a per-stack value.");
            if (d.Mechanic == RunMechanic.None) Check(d.MechanicPerStack == 0, $"Non-mechanic card '{d.Id}' carries no mechanic stacks.");
            else Check(d.MechanicPerStack > 0, $"Mechanic card '{d.Id}' carries mechanic stacks.");
            if (!string.IsNullOrEmpty(d.RequiresId))
            {
                var required = RunUpgradeLibrary.Find(d.RequiresId);
                Check(required != null && required.Id != d.Id, $"Card '{d.Id}' requires a real card ('{d.RequiresId}').");
            }
            // Build a one-stack snapshot and check the direction of every effect.
            var mods = RunModifiers.Build(new[] { new RunUpgradeStack { Id = d.Id, Count = 1 } });
            float expected = 1f + d.PerStack;
            switch (d.Stat)
            {
                case RunStat.PowerDamage: Check(mods.Damage(d.PowerId) > 1.001f, $"'{d.Id}' raises damage ({mods.Damage(d.PowerId):F2}x)."); break;
                case RunStat.PowerRadius: Check(mods.Radius(d.PowerId) > 1.001f, $"'{d.Id}' raises radius ({mods.Radius(d.PowerId):F2}x)."); break;
                case RunStat.PowerRange: Check(mods.Range(d.PowerId) > 1.001f, $"'{d.Id}' raises range ({mods.Range(d.PowerId):F2}x)."); break;
                case RunStat.PowerDuration: Check(mods.Duration(d.PowerId) > 1.001f, $"'{d.Id}' raises duration ({mods.Duration(d.PowerId):F2}x)."); break;
                case RunStat.PowerCooldown: Check(mods.Cooldown(d.PowerId) < .999f, $"'{d.Id}' SHORTENS cooldown ({mods.Cooldown(d.PowerId):F3}x, never longer)."); break;
                case RunStat.PowerEnergyCost: Check(mods.EnergyCost(d.PowerId) < .999f, $"'{d.Id}' LOWERS energy cost ({mods.EnergyCost(d.PowerId):F3}x)."); break;
                case RunStat.ExtraCharges: Check(mods.ExtraCharges(d.PowerId) == 1, $"'{d.Id}' adds exactly +1 charge."); break;
                case RunStat.MeleeDamage: Check(mods.MeleeDamage > 1.001f, $"'{d.Id}' raises melee ({mods.MeleeDamage:F2}x)."); break;
                case RunStat.AllPowerDamage: Check(mods.AllPowerDamage > 1.001f, $"'{d.Id}' raises every power ({mods.AllPowerDamage:F3}x)."); break;
                case RunStat.MaxHealth: Check(mods.MaxHealth > 1.001f, $"'{d.Id}' raises max health ({mods.MaxHealth:F2}x)."); break;
                case RunStat.HealOnKill: Check(mods.HealOnKill > 1.001f, $"'{d.Id}' raises health on kill ({mods.HealOnKill:F2}x)."); break;
                case RunStat.MaxEnergy: Check(mods.MaxEnergy > 1.001f, $"'{d.Id}' raises max energy ({mods.MaxEnergy:F2}x)."); break;
                case RunStat.EnergyRegen: Check(mods.EnergyRegen > 1.001f, $"'{d.Id}' raises energy regen ({mods.EnergyRegen:F2}x)."); break;
                case RunStat.SignatureDamage: Check(mods.SignatureDamage > 1.001f, $"'{d.Id}' raises signature damage ({mods.SignatureDamage:F2}x)."); break;
                case RunStat.SignatureCooldown: Check(mods.SignatureCooldown < .999f, $"'{d.Id}' shortens the signature cooldown ({mods.SignatureCooldown:F3}x)."); break;
                case RunStat.SignatureEnergyCost: Check(mods.SignatureEnergy < .999f, $"'{d.Id}' lowers the signature cost ({mods.SignatureEnergy:F3}x)."); break;
                case RunStat.SignatureRadius: Check(mods.SignatureRadius > 1.001f, $"'{d.Id}' widens the signature ({mods.SignatureRadius:F2}x)."); break;
            }
            if (d.Mechanic != RunMechanic.None) Check(mods.Mechanic(d.Mechanic) == d.MechanicPerStack, $"'{d.Id}' grants {d.MechanicPerStack} {d.Mechanic} stack(s).");
        }
        Log($"MEASURED {RunUpgradeLibrary.All.Length} cards across {combatPowers.Count} primaries, {secondaries.Count} secondaries, the signature and the hero.");

        // Rarity tables stay valid probability distributions at every band.
        foreach (int level in new[] { 1, 7, 8, 13, 14, 30, 99 })
        {
            RunTuning.RarityWeights(level, out float common, out float rare, out float epic);
            Check(common >= 0f && rare >= 0f && epic >= 0f && Mathf.Abs(common + rare + epic - 1f) < 1e-5f,
                $"Rarity weights at level {level} are finite, non-negative and sum to 1 ({common:F2}/{rare:F2}/{epic:F2}).");
        }
        Check(RunTuning.MinPowerCooldown > 0f && RunTuning.MinSignatureCooldown > 0f && RunTuning.MaxChainTargets <= 7 && RunTuning.MaxPoisonSpreadTargets <= 5,
            $"Safety floors: power cooldown >= {RunTuning.MinPowerCooldown}s, signature >= {RunTuning.MinSignatureCooldown}s, chain <= {RunTuning.MaxChainTargets}, poison spread <= {RunTuning.MaxPoisonSpreadTargets}.");

        var fields = typeof(ProgressSave).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToArray();
        Check(!fields.Any(n => n.IndexOf("RunLevel", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("RunXp", StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("RunSeed", StringComparison.OrdinalIgnoreCase) >= 0),
            "ProgressSave has no run field: temporary progression cannot be persisted.");
    }

    // ------------------------------------------------------------------ maths and table
    void Maths()
    {
        Log("---- XP TABLE AND CURVE");
        Check(RunTuning.XpFor("Rusher") == 10 && RunTuning.XpFor("Gunner") == 14 && RunTuning.XpFor("Skirmisher") == 18 &&
              RunTuning.XpFor("Brute") == 34 && RunTuning.XpFor("Bulwark") == 40 && RunTuning.XpFor("Pulsebreaker") == RunTuning.BossXp,
            $"Shipping kill XP: 10 / 14 / 18 / 34 / 40, boss {RunTuning.BossXp}; heavier archetypes pay more.");
        Check(RunTuning.XpFor("FutureEnemy") == RunTuning.UnknownEnemyXp && RunTuning.XpFor(null) == RunTuning.UnknownEnemyXp,
            "Unknown/new archetypes still pay a fallback instead of nothing.");
        Check(RunMath.RequiredXp(1) == 120, $"Level 1 needs {RunMath.RequiredXp(1)} XP (= {RunMath.RequiredXp(1) / 10} Rusher kills).");
        for (int level = 1; level < 40; level++) Check(RunMath.RequiredXp(level) > 0 && RunMath.RequiredXp(level + 1) >= RunMath.RequiredXp(level), $"Requirement is positive and monotonic at level {level}.");
        Check(RunMath.RequiredXp(999) == int.MaxValue && RunMath.RequiredXp(5000) == int.MaxValue, "The curve saturates (no overflow, no negative requirement).");
        Check(RunMath.LevelFor(0) == 1 && RunMath.LevelFor(119) == 1 && RunMath.LevelFor(120) == 2 && RunMath.LevelFor(120 + 144) == 3,
            "LevelFor matches the curve at the exact boundaries.");
        Check(RunMath.TotalXpTo(1) == 0 && RunMath.TotalXpTo(3) == 120 + 144, "TotalXpTo sums the same requirements.");
        Log($"MEASURED levels 1-10 requirement: {string.Join(", ", Enumerable.Range(1, 10).Select(RunMath.RequiredXp))}.");
    }

    /// The shipping wave data (Endless curve + every-5th-round boss) run through the real curve and XP table, with the
    /// average enemy worth per wave. It is a model, not a playtest: it assumes every enemy is killed and averages the
    /// deterministic weighted mix rather than simulating a specific spawn order.
    void ShippingModel()
    {
        Log("---- MODEL (shipping EndlessWaves + EndlessWaveEvents + XP table; average mix, all kills)");
        var director = Resources.Load<EndlessWaveDirector>("ModeDirectors/EndlessWaves");
        Check(director != null && director.Events != null, "Shipping Endless director and its milestone asset load.");
        var events = director.Events;
        Check(events.MinibossEvery == 5 && events.MinibossArchetype != null && events.MinibossArchetype.DisplayName == "Pulsebreaker",
            $"Pulsebreaker is the miniboss every {events.MinibossEvery} rounds (rounds 5, 10, 15, ...), not a regular-pool weight.");
        var curve = events.EndlessCurve;
        var weights = new float[EnemyCompositionCurve.Slots];
        int LevelAt(int wave, out int xp, out int bossXp)
        {
            xp = 0; bossXp = 0;
            int size = director.WaveSize(wave);
            bool boss = events.MinibossEvery > 0 && wave % events.MinibossEvery == 0;
            int regulars = Mathf.Max(1, size - (boss ? events.MinibossRegularCost : 0));
            curve.Weights(CompositionCurveKind.Endless, wave, weights);
            float average = 0f, totalWeight = 0f;
            for (int slot = 0; slot < EnemyCompositionCurve.Slots; slot++)
            {
                var archetype = curve.At(slot);
                if (archetype == null || weights[slot] <= 0f) continue;
                average += weights[slot] * RunTuning.XpFor(archetype.DisplayName); totalWeight += weights[slot];
            }
            average = totalWeight > 0f ? average / totalWeight : 0f;
            xp = Mathf.RoundToInt(average * regulars);
            bossXp = boss ? RunTuning.BossXp : 0;
            long total = 0;
            for (int w = 1; w <= wave; w++)
            {
                int s = director.WaveSize(w); bool b = events.MinibossEvery > 0 && w % events.MinibossEvery == 0;
                int r = Mathf.Max(1, s - (b ? events.MinibossRegularCost : 0));
                curve.Weights(CompositionCurveKind.Endless, w, weights);
                float avg = 0f, sum = 0f;
                for (int slot = 0; slot < EnemyCompositionCurve.Slots; slot++)
                {
                    var a = curve.At(slot); if (a == null || weights[slot] <= 0f) continue;
                    avg += weights[slot] * RunTuning.XpFor(a.DisplayName); sum += weights[slot];
                }
                if (sum > 0f) total += Mathf.RoundToInt(avg / sum * r);
                if (b) total += RunTuning.BossXp;
            }
            return RunMath.LevelFor(total);
        }

        int l5 = LevelAt(5, out int e5, out int b5), l10 = LevelAt(10, out int e10, out int b10), l20 = LevelAt(20, out int e20, out int b20), l30 = LevelAt(30, out _, out _);
        Log($"MEASURED model: round 5 -> level {l5} (+{e5} regular, +{b5} boss XP), round 10 -> level {l10}, round 20 -> level {l20}, round 30 -> level {l30}.");
        Check(l5 >= 4 && l5 <= 8, $"Round 5 lands at level {l5} (band 4-8: the first boss pays off without skipping the early curve).");
        Check(l10 >= 7 && l10 <= 12, $"Round 10 lands at level {l10} (band 7-12).");
        Check(l20 >= 12 && l20 <= 19, $"Round 20 lands at level {l20} (band 12-19: ~14 picks by then).");
        Check(l30 > l20, $"Progression is still moving at round 30 (level {l30} > {l20}); the repeatable hero card carries deep runs.");
        Check(b5 == RunTuning.BossXp && b10 == RunTuning.BossXp, "Only every-5th round carries boss XP.");
    }

    // ------------------------------------------------------------------ a real run
    IEnumerator FreshRun()
    {
        Log("---- FRESH RUN (a maxed account still starts at level 1)");
        yield return Home();
        Profile.AddXp(6000);
        var fire = Power("fire");
        int bought = 0; while (Profile.Buy(fire)) bought++;
        int menuLevel = Profile.Data.Level;
        int fireTier = Profile.Tier(fire);
        Check(bought > 0 && fireTier >= 1, $"Saved account: level {menuLevel}, Fire Blast tier {fireTier} ({bought} purchases).");
        float tieredHeadStart = fire.GetStats(fireTier).Damage;

        yield return EnterMode(F.Heroes[0], "fire", "ice", "free-play");
        Isolate(); for (int i = 0; i < 3; i++) yield return null;
        var fireRuntime = Runtime("fire");
        float freePlayDamage = W.Powers.Stats(fireRuntime).Damage;
        Check(Mathf.Abs(freePlayDamage - tieredHeadStart * W.Powers.HeroStats.PowerDamage) < .01f && freePlayDamage > fire.GetStats(0).Damage * W.Powers.HeroStats.PowerDamage + .01f,
            $"CONTROL Free Play uses the saved tier: {freePlayDamage:F2} damage = tier {fireTier} ({tieredHeadStart:F1}) x hero {W.Powers.HeroStats.PowerDamage:F2}.");

        yield return Home();
        RunProgress.SeedOverride = 424242;
        yield return EnterMode(F.Heroes[0], "fire", "ice", "endless-fight");
        Isolate(); for (int i = 0; i < 3; i++) yield return null;
        run = W.Run;
        Check(run != null, "Entering Endless Fight creates the temporary run state.");
        Check(run.Seed == 424242, $"The run used the injected deterministic seed ({run.Seed}).");
        Check(run.Level == 1 && run.Xp == 0 && run.Kills == 0 && run.BossKills == 0 && run.UpgradePicks == 0 && run.Stacks.Count == 0,
            $"Fresh run: level {run.Level}, XP {run.Xp}, kills {run.Kills}, stacks {run.Stacks.Count} — the saved level {menuLevel} gave no head start.");
        Check(Mathf.Abs(run.Mods.MaxHealth - 1f) < 1e-5f && Mathf.Abs(run.Mods.AllPowerDamage - 1f) < 1e-5f, "No run modifiers before the first pick.");
        float baseDamage = fire.GetStats(0).Damage * W.Powers.HeroStats.PowerDamage;
        float runDamage = W.Powers.Stats(Runtime("fire")).Damage;
        Check(Mathf.Abs(runDamage - baseDamage) < .01f && runDamage < freePlayDamage,
            $"CONTROL the run uses tier-0 base damage: {runDamage:F2} = {baseDamage:F2} (Free Play was {freePlayDamage:F2}); a maxed account has no advantage.");
        Check(W.MaxHealth > 0f && Mathf.Abs(W.MaxHealth - W.Tuning.Movement.Health * W.Powers.HeroStats.MaxHealth * W.Difficulty.PlayerHealth) < .01f,
            $"Run max health is the same authoritative pool ({W.MaxHealth:F1}) with no run multiplier yet.");

        var hud = Hud;
        Check(hud != null && hud.LevelCaptionText == "RUN" && hud.LevelValue.text == "1", $"HUD row shows RUN / 1 (caption '{hud.LevelCaptionText}', value '{hud.LevelValue.text}').");
        Check(hud.ExperienceText != null && hud.ExperienceText.Contains("0 / "), $"HUD XP reads '{hud.ExperienceText}'.");
        Check(!hud.RunModalVisible, "No level-up modal before the first level.");

        Log("---- KILL CREDIT (the health-on-kill gate pays XP exactly once)");
        int xpBefore = run.Xp;
        var rusher = Enemy("Rusher");
        int popups = hud.XpGrantsReceived;
        rusher.Damage(99999f, W.Powers);
        Check(run.Kills == 1 && run.Xp == xpBefore + 10, $"Rusher kill: kills {run.Kills}, run XP {run.Xp} (+10).");
        Check(hud.XpGrantsReceived == popups + 1 && hud.PopupXpShown >= 10, $"The HUD started one floating run-XP number ({popups} -> {hud.XpGrantsReceived}).");
        Check(HealthOnKill.LastBlocked == null, "The same kill also passed the health-on-kill gate (shared kill credit).");
        rusher.Damage(5f, W.Powers);
        Check(run.Kills == 1 && run.Xp == xpBefore + 10, "Further damage to a dead enemy pays no second XP.");

        int kills = run.Kills;
        var brute = Enemy("Brute");
        brute.Damage(99999f, W.Powers);
        Check(run.Kills == kills + 1 && run.Xp == xpBefore + 44, $"Brute pays 34 (total {run.Xp} = 10 + 34).");

        var civilian = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Civilian);
        civilian.enabled = false; civilian.Agent.enabled = false; civilian.transform.position = new Vector3(1f, 150f, 0f); civilian.SetCombatStats(10f, 0f); Physics.SyncTransforms();
        int xpAtCivilian = run.Xp; kills = run.Kills;
        civilian.Damage(99999f, W.Powers);
        Check(run.Xp == xpAtCivilian && run.Kills == kills, "Civilian death pays no run XP and no run kill.");

        var orphan = Enemy("Rusher");
        xpAtCivilian = run.Xp; kills = run.Kills;
        orphan.Damage(99999f, null);
        Check(orphan.Dead && run.Xp == xpAtCivilian && run.Kills == kills, "A death with no player source (NPC-on-NPC) pays no run XP.");
    }

    // ------------------------------------------------------------------ level-up flow
    IEnumerator LevelUps()
    {
        Log("---- LEVEL UP (pause, three cards, one real stat change)");
        var hud = Hud;
        int persistentLevel = W.Progression.Data.Level;
        run.GrantXp(run.RequiredXp - run.Xp, W.Hero.transform.position);
        yield return null;
        Check(run.Level == 2 && run.UpgradeOpen && run.PendingPicks == 1, $"Enough XP raises level 2 and opens one pick (pending {run.PendingPicks}).");
        Check(W.Mode.Paused && Time.timeScale == 0f && W.MenuOpen, $"The fight is paused for the choice: Paused {W.Mode.Paused}, timeScale {Time.timeScale}, menu {W.MenuOpen}.");
        Check(hud.RunModalVisible && hud.RunOfferCount == 3 && hud.RunModalLevelShown == 2, $"The modal is visible with {hud.RunOfferCount} cards at level {hud.RunModalLevelShown}.");
        Check(W.Progression.Data.Level == persistentLevel, "Opening a run level-up does not touch the saved level.");

        AssertOffer(run, "first offer");
        Check(run.Offer.Select(d => d.Id).Distinct().Count() == run.Offer.Count, "The three cards are distinct.");
        var def = PickPrimaryCard(run);
        float before = StatValue(def), beforeEnergy = EnergyValue(def);
        float chargesBefore = ChargesValue(def);
        int index = run.Offer.ToList().IndexOf(def);
        Check(run.Choose(index), $"Chose '{def.Name}' ({def.Id}) at index {index}.");
        Check(!run.UpgradeOpen && !W.Mode.Paused && Mathf.Abs(Time.timeScale - 1f) < 1e-4f, "Picking the only card closes the modal and resumes the fight (timeScale 1).");
        yield return null;
        Check(run.UpgradePicks == 1 && run.StacksOf(def.Id) == 1 && run.Mods != null, $"The stack was recorded once ({run.StacksOf(def.Id)} of {def.MaxStacks}).");
        Check(W.Progression.Data.Level == persistentLevel, "The pick does not write to persistent progression.");

        if (def.Stat == RunStat.ExtraCharges)
            Check(ChargesValue(def) == chargesBefore + 1, $"Charge card refilled the target ({chargesBefore} -> {ChargesValue(def)}).");
        else if (def.Stat == RunStat.PowerEnergyCost)
            Check(Mathf.Abs(EnergyValue(def) - beforeEnergy / (1f + def.PerStack)) < .01f, $"Energy cost fell {beforeEnergy:F2} -> {EnergyValue(def):F2} (1/{1f + def.PerStack:F2}).");
        else if (def.Stat == RunStat.PowerCooldown)
            Check(StatValue(def) < before, $"Cooldown fell {before:F3} -> {StatValue(def):F3} (a reduction, never a longer cooldown).");
        else if (def.Stat == RunStat.MaxHealth)
            Check(W.MaxHealth > before && Mathf.Abs(W.MaxHealth - before * (1f + def.PerStack)) < .01f, $"Max health rose {before:F1} -> {W.MaxHealth:F1} and the missing HP was granted once.");
        else if (def.Stat != RunStat.None)
            Check(StatValue(def) > before, $"'{def.Name}' raised its real stat {before:F2} -> {StatValue(def):F2}.");
        Check(Hud.LevelCaptionText == "RUN" && Hud.LevelValue.text == "2", "The HUD row updates to RUN level 2 immediately.");

        Log("---- MULTIPLE LEVELS AT ONCE");
        int levelBefore = run.Level, picksBefore = run.UpgradePicks;
        run.GrantXp(6000, W.Hero.transform.position);
        int reached = run.Level - levelBefore;
        Check(reached >= 3 && run.PendingPicks == reached, $"6000 XP raised {reached} levels and queued exactly {run.PendingPicks} picks.");
        int guard = 0, mechanics = 0;
        while (run.UpgradeOpen && guard++ < 40)
        {
            AssertOffer(run, $"queued offer {run.UpgradePicks + 1}");
            var chosen = run.Offer[0];
            run.Choose(0);
            if (chosen.Mechanic != RunMechanic.None)
            {
                Check(RunMechanics.Stacks(chosen.Mechanic) >= chosen.MechanicPerStack,
                    $"Mechanical card '{chosen.Id}' reached the live effect bridge ({chosen.Mechanic} = {RunMechanics.Stacks(chosen.Mechanic)}).");
                mechanics++;
            }
            yield return null;
        }
        Log($"MEASURED {mechanics} mechanical (star) card(s) picked and read back through RunMechanics.");
        Check(!run.UpgradeOpen && !W.Mode.Paused && run.PendingPicks == 0, "Every queued pick was resolved and the fight resumed.");
        Check(run.UpgradePicks == picksBefore + reached, $"Picks {run.UpgradePicks} = {picksBefore} + {reached} levels.");
        Check(run.Level == levelBefore + reached, "The level matches the XP curve after the burst.");
        Check(run.Offer.Count == 0, "No stale offer remains after the last pick.");

        Log("---- ESC CANNOT RESUME BEHIND THE MODAL");
        run.GrantXp(run.RequiredXp - run.Xp, W.Hero.transform.position);
        yield return null;
        Check(run.UpgradeOpen && W.Mode.Paused, "A level-up is open and the fight is paused.");
        Check(WorldSession.Instance.Run != null && WorldSession.Instance.Run.UpgradeOpen, "WorldSession sees the open pick (its Esc handler is gated on it).");
        Check(run.Choose(0) && !W.Mode.Paused, "The card, not Esc, is what resumes the fight.");
    }

    // ------------------------------------------------------------------ boss
    IEnumerator Boss()
    {
        Log("---- PULSEBREAKER (authoritative bar, boss XP, Epic milestone pick)");
        while (run.UpgradeOpen) run.Choose(0);
        var hud = Hud;
        int levelBefore = run.Level; long xpBefore = TotalXp(run);
        var bossNpc = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Criminal, Archetype("Pulsebreaker"), 400f, 0f);
        bossNpc.enabled = false; bossNpc.Agent.enabled = false;
        bossNpc.transform.position = W.Hero.transform.position + new Vector3(2f, 0f, 0f); Physics.SyncTransforms();
        Check(PulsebreakerBoss.AttachForVerification(bossNpc) != null && bossNpc.IsBoss, "A real Pulsebreaker body is attached through the verification entry point.");
        yield return null;
        Check(hud.BossBarVisible && hud.BossNameShown == "PULSEBREAKER", $"The boss bar is visible and named '{hud.BossNameShown}'.");
        Check(Mathf.Abs(hud.BossFraction - bossNpc.HealthFraction) < .01f, $"The bar reads authoritative health: {hud.BossFraction:F3} vs {bossNpc.HealthFraction:F3}.");
        Check(hud.BossPhaseShown == "PHASE 1", $"Phase 1 is shown ('{hud.BossPhaseShown}').");

        bossNpc.Damage(260f, W.Powers);
        yield return null;
        Check(Mathf.Abs(hud.BossFraction - bossNpc.HealthFraction) < .01f, $"The bar followed damage to the real current HP ({hud.BossFraction:F3} vs {bossNpc.HealthFraction:F3}).");
        Check(hud.BossBarVisible && hud.BossPhaseShown == "PHASE 1", "The bar stays up while the boss is alive (its Overdrive label is asserted by PulsebreakerVerification.Run, which drives a live NavMesh boss).");

        bossNpc.Damage(99999f, W.Powers);
        int killed = run.BossKills;
        long xpAfter = TotalXp(run);
        Check(killed == 1 && xpAfter - xpBefore == RunTuning.BossXp, $"Boss kill paid exactly {xpAfter - xpBefore} run XP (table {RunTuning.BossXp}) and counted {killed} boss kill.");
        Check(run.Kills > 0 && run.PendingPicks >= 1, $"The boss queued {run.PendingPicks} pick(s): levelling plus the Epic milestone.");

        bool epicSeen = false; int guard = 0;
        while (run.UpgradeOpen && guard++ < 30)
        {
            AssertOffer(run, "boss offer", -1);
            if (run.PendingPicks == 1)
            {
                Check(run.Offer.Count >= 1 && run.Offer.All(d => d.Rarity == RunRarity.Epic), $"The boss milestone pick offers {run.Offer.Count} Epic card(s) only (never padded with a common).");
                epicSeen = true;
            }
            run.Choose(0);
            yield return null;
        }
        Check(epicSeen, "The Epic milestone pick was reached and resolved.");
        Check(!run.UpgradeOpen && !W.Mode.Paused, "After the boss picks the run resumes.");

        float until = Time.realtimeSinceStartup + 20f;
        while (PulsebreakerBoss.Active != null && Time.realtimeSinceStartup < until) yield return null;
        for (int i = 0; i < 2; i++) yield return null;
        Check(PulsebreakerBoss.Active == null && !hud.BossBarVisible, "The boss body cleaned up and its bar is gone (no stuck UI).");
        Check(WorldSession.Instance.Run == run, "The run survived the boss encounter.");
    }

    // ------------------------------------------------------------------ reset + save
    void ResetAndSave()
    {
        Log("---- RESET AND SAVE");
        string save = W.Progression.SavePath;
        Check(!string.IsNullOrEmpty(save) && File.Exists(save), $"The verification save exists at {save}.");
        string json = File.ReadAllText(save);
        Check(!json.Contains("RunLevel") && !json.Contains("UpgradeOpen") && !json.Contains("RunSeed") && !json.Contains("RunUpgrade"),
            "The live save file contains no run state.");
        int persistent = W.Progression.Data.Level;
        Check(persistent > 0, $"Persistent level still exists alongside the run ({persistent}).");
        Check(run.Level > 1, $"The finished run is at level {run.Level} with {run.UpgradePicks} picks (temporary).");

        var finishing = run;
        Check(W.Mode != null, "The session is still live before the run ends.");
        W.Mode.ReturnHome();
        Check(finishing.Closed && !finishing.UpgradeOpen, "Ending the run closes its rewards and any open choice (no XP after the run).");
    }

    // ------------------------------------------------------------------ free play
    IEnumerator FreePlay()
    {
        Log("---- FREE PLAY (no run, no automatic waves)");
        yield return Scene(GameFlow.HomeScene);
        Check(WorldSession.Instance == null || WorldSession.Instance.Run == null, "No run survives the trip home.");
        yield return EnterMode(F.Heroes[0], "fire", "ice", "free-play");
        Isolate(); for (int i = 0; i < 3; i++) yield return null;
        Check(W.Run == null, "Free Play has no run progression.");
        Check(W.Mode != null && W.Mode.Director == null, "Free Play has no wave director, so no automatic boss/round spawning.");
        var hud = Hud;
        Check(hud.LevelCaptionText == "LV", $"The HUD row is back to the saved level caption ('{hud.LevelCaptionText}').");
        var enemy = Enemy("Rusher");
        long before = (long)W.Progression.Data.Level * 1000000 + W.Progression.Data.Xp;
        enemy.Damage(99999f, W.Powers);
        Check(W.Run == null, "Killing in Free Play creates no run state and awards no run XP.");
        long after = (long)W.Progression.Data.Level * 1000000 + W.Progression.Data.Xp;
        Check(after != before, "CONTROL: the kill still paid persistent account XP in Free Play (existing progression intact).");
    }

    // ------------------------------------------------------------------ heroes and difficulty
    IEnumerator Heroes()
    {
        Log("---- HEROES (all three archetypes start at level 1 with their own pool)");
        for (int i = 0; i < F.Heroes.Length; i++)
        {
            var hero = F.Heroes[i];
            var a = FirstAllowedCombat(hero, null);
            var b = FirstAllowedCombat(hero, a);
            Check(a != null && b != null, $"{hero.DisplayName}: an allowed primary pair exists ({a.Id} + {b.Id}).");
            yield return EnterMode(hero, a.Id, b.Id, "endless-fight");
            Isolate(); for (int k = 0; k < 3; k++) yield return null;
            Check(W.Run != null && W.Run.Level == 1 && W.Run.Xp == 0, $"{hero.DisplayName}: run starts at level 1.");
            Check(Mathf.Abs(W.MaxHealth - W.Tuning.Movement.Health * hero.Stats.MaxHealth * W.Difficulty.PlayerHealth) < .01f,
                $"{hero.DisplayName}: max health {W.MaxHealth:F1} = baseline {W.Tuning.Movement.Health:F1} x hero {hero.Stats.MaxHealth:F2} x difficulty {W.Difficulty.PlayerHealth:F2}, applied once.");
            float expected = BaseDamage(Power(a.Id), hero.Stats);
            float actual = W.Powers.Stats(Runtime(a.Id)).Damage;
            Check(Mathf.Abs(actual - expected) < .01f, $"{hero.DisplayName}: {a.DisplayName} runs at its base damage {actual:F2} (hero x{hero.Stats.PowerDamage:F2}).");
            var victim = Enemy("Rusher");
            victim.Damage(99999f, W.Powers);
            Check(W.Run.Kills == 1 && W.Run.Xp == 10, $"{hero.DisplayName}: a Rusher kill paid exactly 10 run XP.");
        }
    }

    IEnumerator Difficulty()
    {
        Log("---- DIFFICULTY (one authoritative multiplier, run adds none)");
        foreach (var id in new[] { "extreme", "hard", "normal" })
        {
            yield return Home();
            Profile.SetDifficulty(id);
            yield return EnterMode(F.Heroes[0], "fire", "ice", "endless-fight");
            Isolate(); for (int i = 0; i < 2; i++) yield return null;
            Check(W.Run != null && W.Run.Level == 1 && W.Run.Mods.MaxHealth == 1f, $"{id}: run level 1 and no run health multiplier.");
            float expected = W.Tuning.Movement.Health * W.Powers.HeroStats.MaxHealth * W.Difficulty.PlayerHealth;
            Check(Mathf.Abs(W.MaxHealth - expected) < .01f, $"{id}: max health {W.MaxHealth:F1} = baseline x hero x difficulty {W.Difficulty.PlayerHealth:F2} exactly once.");
            Check(W.Run.Seed != 0, $"{id}: run seed {W.Run.Seed} exists.");
        }
        Log($"MEASURED difficulty PlayerHealthMultiplier: normal {DifficultyDefinition.Resolve("normal").PlayerHealthMultiplier:F2}, hard {DifficultyDefinition.Resolve("hard").PlayerHealthMultiplier:F2}, extreme {DifficultyDefinition.Resolve("extreme").PlayerHealthMultiplier:F2}.");
    }

    // ------------------------------------------------------------------ perf
    void Perf()
    {
        Log("---- PERFORMANCE");
        var hud = Hud;
        if (hud == null || W == null) { Log("SKIP no HUD in this scene."); return; }
        Check(hud.LastUpdateMs < 4.0, $"HUD update is {hud.LastUpdateMs:F3} ms (budget 4 ms; run row + modal are change-guarded).");
        Check(hud.PopupElementsCreated == GameHud.PopupPoolSize, $"XP popups reuse the fixed pool ({hud.PopupElementsCreated} elements) — no per-kill object.");
        var live = W.Run;
        Check(live != null && live.Stacks.Count <= RunUpgradeLibrary.All.Length, $"Run state is bounded by the catalog size ({live?.Stacks.Count ?? 0} stacks <= {RunUpgradeLibrary.All.Length} cards).");
        Check(Time.timeScale == 1f || W.Mode == null || W.Mode.Paused, "No stray run pause is left behind.");
    }

    void Limit() => Log("LIMIT: batch mode has no hardware keyboard/mouse and no human to judge feel. This suite drives the real UI state, the real kill funnel, real power stats, a real Pulsebreaker body and the real shipping HUD, but it cannot judge whether a level-up pause feels good, whether card text reads well on a TV, or the actual pacing of a played run. Visual screenshots and a human playtest are UNVERIFIED here.");

    // ------------------------------------------------------------------ offer assertions
    void AssertOffer(RunProgress state, string label, int expected = 3)
    {
        if (expected > 0) Check(state.Offer.Count == 3, $"{label}: three cards ({state.Offer.Count}).");
        else Check(state.Offer.Count >= 1 && state.Offer.Count <= 3, $"{label}: {state.Offer.Count} card(s) (1-3).");
        var user = W.Powers;
        string a = user.EquippedA != null ? user.EquippedA.Id : null, b = user.EquippedB != null ? user.EquippedB.Id : null, secondary = user.EquippedSecondary != null ? user.EquippedSecondary.Id : null;
        bool signature = user.Synergy != null && user.Synergy.IsSignature;
        foreach (var d in state.Offer)
        {
            Check(state.Level >= d.MinLevel, $"{label}: '{d.Id}' unlocks at {d.MinLevel} <= level {state.Level}.");
            Check(state.StacksOf(d.Id) < d.MaxStacks, $"{label}: '{d.Id}' is below its cap ({state.StacksOf(d.Id)}/{d.MaxStacks}).");
            if (!string.IsNullOrEmpty(d.RequiresId)) Check(state.StacksOf(d.RequiresId) > 0, $"{label}: prerequisite '{d.RequiresId}' for '{d.Id}' is owned.");
            if (d.IsHero) continue;
            if (d.IsSignature)
            {
                Check(signature, $"{label}: '{d.Id}' is offered only when a signature is equipped.");
                continue;
            }
            Check(d.PowerId == a || d.PowerId == b || d.PowerId == secondary, $"{label}: '{d.Id}' targets an equipped power ('{d.PowerId}').");
        }
        Check(state.Offer.Any(d => d.PowerId == a || d.PowerId == b), $"{label}: at least one card touches an equipped primary.");
    }

    RunUpgradeDefinition PickPrimaryCard(RunProgress state)
    {
        string a = W.Powers.EquippedA != null ? W.Powers.EquippedA.Id : null, b = W.Powers.EquippedB != null ? W.Powers.EquippedB.Id : null;
        var primaries = state.Offer.Where(d => !d.IsHero && !d.IsSignature && (d.PowerId == a || d.PowerId == b)).ToList();
        // Prefer a numeric card for the first pick: that is the one the suite measures against the real PowerUser stats.
        return primaries.FirstOrDefault(d => d.Stat != RunStat.None) ?? primaries.FirstOrDefault() ?? state.Offer[0];
    }

    // ------------------------------------------------------------------ stat probe
    float StatValue(RunUpgradeDefinition def)
    {
        if (def.IsHero) return W.MaxHealth;
        if (def.IsSignature) return def.Stat == RunStat.SignatureCooldown ? W.Powers.SynergyRunner.Cooldown : 1f;
        var rt = Runtime(def.PowerId); if (rt == null) return 0f;
        var s = W.Powers.Stats(rt);
        switch (def.Stat)
        {
            case RunStat.PowerDamage: return s.Damage;
            case RunStat.PowerCooldown: return s.Cooldown;
            case RunStat.PowerRadius: return s.Radius;
            case RunStat.PowerRange: return s.Range;
            case RunStat.PowerDuration: return s.Duration;
            case RunStat.MeleeDamage: return s.Force;
            default: return s.Damage;
        }
    }
    float EnergyValue(RunUpgradeDefinition def)
    {
        var rt = Runtime(def.PowerId); return rt != null ? W.Powers.EnergyCost(rt) : 0f;
    }
    float ChargesValue(RunUpgradeDefinition def)
    {
        var rt = Runtime(def.PowerId); return rt != null ? W.Powers.Stats(rt).Charges : 0f;
    }
    static long TotalXp(RunProgress state) => RunMath.TotalXpTo(state.Level) + state.Xp;
    /// The tier-0 damage PowerUser.Stats produces for a hero (melee powers scale on MeleeDamage, every other on PowerDamage).
    static float BaseDamage(PowerDefinition power, HeroStats hero) =>
        power.GetStats(0).Damage * (power.Effect is PunchEffect ? hero.MeleeDamage : hero.PowerDamage);

    // ------------------------------------------------------------------ scene helpers
    IEnumerator EnterMode(HeroDefinition hero, string a, string b, string mode)
    {
        yield return Home();
        var secondary = FirstAllowedSecondary(hero);
        Check(secondary != null, $"{hero.DisplayName}: an allowed secondary exists ({secondary.Id}).");
        Check(Profile.SetLoadout(hero, Power(a), Power(b), secondary, hero.Primary, hero.Secondary),
            $"Saved {hero.DisplayName} + {a} + {b} + {secondary.Id} through the loadout API.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/" + mode));
        yield return Scene(GameFlow.CityScene);
        Check(W.Mode != null && W.Mode.Definition.Id == mode, $"Session is {mode}.");
    }

    PowerDefinition FirstAllowedCombat(HeroDefinition hero, PowerDefinition avoid)
    {
        foreach (var p in combatPowers) if (p != avoid && F.AllowedCombat(hero, p) && Owns(p)) return p;
        return null;
    }
    PowerDefinition FirstAllowedSecondary(HeroDefinition hero)
    {
        foreach (var p in Resources.LoadAll<PowerDefinition>("Powers")) if (p != null && p.Role == PowerRole.Secondary && F.AllowedSecondary(hero, p) && Owns(p)) return p;
        return null;
    }
    /// The account save lives on the Home screen's ModeScreens; in a city the live session owns the same progress object.
    bool Owns(PowerDefinition power) => W != null ? W.Progression.Owns(power) : Profile.Owns(power);

    // ------------------------------------------------------------------ baseline helpers
    CityNpc Enemy(string archetype)
    {
        var npc = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Criminal, Archetype(archetype), 50f, 0f);
        if (npc == null) throw new Exception("Enemy spawn failed: " + archetype);
        npc.enabled = false; npc.Agent.enabled = false;
        npc.transform.position = new Vector3(2f, 150f, 0f); Physics.SyncTransforms();
        if (!npc.Hostile) throw new Exception(archetype + " spawned non-hostile");
        return npc;
    }
    static EnemyArchetype Archetype(string name) => Resources.Load<EnemyArchetype>("Enemies/" + name);
}
#endif
