#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// See Assets/Editor/PulsebreakerVerification.cs. Positive and negative controls for the Pulsebreaker miniboss:
/// data, mode gating, round spawning, the three attacks (readable telegraphs, real dodge windows, bounded rush),
/// the 40% phase shift, the boss bar and the untouched ordinary enemy cycle.
public sealed class PulsebreakerVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/Pulsebreaker/";
    protected override string ResultFile => "results.txt";

    PulsebreakerBoss Boss => PulsebreakerBoss.Active;
    EndlessWaveState State => W.Mode != null ? W.Mode.Director as EndlessWaveState : null;
    GameHud Hud => FindAnyObjectByType<GameHud>();
    PulsebreakerTuning Tuning => Resources.Load<PulsebreakerTuning>("Enemies/PulsebreakerTuning");
    EnemyArchetype Pulsebreaker => Resources.Load<EnemyArchetype>("Enemies/Pulsebreaker");

    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Data();
        yield return ModeGating();
        yield return FreePlay();
        yield return RoundSpawning();
        yield return Fixtures();
        yield return Regressions();
        yield return Persistence();
        Log("LIMIT: batch Play Mode verifies state, timing, damage and layout; hands-on feel, physical input, day/night readability and sustained standalone frame rate remain unverified.");
    }

    // ------------------------------------------------------------------ A. data and shared maths
    IEnumerator Data()
    {
        Log("---- DATA");
        var tuning = Tuning;
        Check(tuning != null, "PulsebreakerTuning asset loads from Resources/Enemies.");
        Check(Mathf.Abs(tuning.SlamWindup - 1f) < .0001f && Mathf.Abs(tuning.SlamRadius - 8f) < .0001f && Mathf.Abs(tuning.DamageCapFraction - .25f) < .0001f,
            $"Tuning: slam windup {tuning.SlamWindup}s, ring {tuning.SlamRadius}m, damage cap {tuning.DamageCapFraction:P0}.");
        Check(Mathf.Abs(tuning.PhaseHealthFraction - .4f) < .0001f && tuning.FreezeResistance < 1f && tuning.CannonWindup > tuning.SlamWindup,
            $"Tuning: phase at {tuning.PhaseHealthFraction:P0} HP, freeze x{tuning.FreezeResistance}, cannon windup {tuning.CannonWindup}s.");

        var archetype = Pulsebreaker;
        Check(archetype != null && archetype.DisplayName == "Pulsebreaker", "Pulsebreaker archetype exists.");
        Check(archetype.Kind == AttackKind.Slam && archetype.HealthMultiplier == 1f && archetype.DamageMultiplier == 1f && archetype.Accent == CityColor.Cyan,
            $"Archetype: Kind {archetype.Kind}, health x{archetype.HealthMultiplier}, damage x{archetype.DamageMultiplier}, accent {archetype.Accent}, scale {archetype.VisualScale}.");

        var events = Resources.Load<EndlessWaveEvents>("ModeDirectors/EndlessWaveEvents");
        Check(events != null && events.MinibossArchetype == archetype && events.MinibossEvery == 5,
            $"Endless miniboss slot: every {events.MinibossEvery} rounds, archetype {events.MinibossArchetype.DisplayName}.");

        // Shockwave geometry (shared with gameplay): radius band crossed, height gate overrides everything.
        Check(PulsebreakerBoss.ShockwaveHits(4f, 0f, 3.5f, 4.2f, .9f, 1.3f), "Shockwave: a grounded player at 4 m is hit as the ring crosses them.");
        Check(!PulsebreakerBoss.ShockwaveHits(9f, 0f, 8f, 8.2f, .9f, 1.3f), "CONTROL: 9 m is outside the 8 m ring.");
        Check(!PulsebreakerBoss.ShockwaveHits(4f, 2f, 3.5f, 4.2f, .9f, 1.3f), "CONTROL: 2 m above the impact plane is not hit (the jump escape).");
        Check(!PulsebreakerBoss.ShockwaveHits(2f, 0f, 5f, 6f, .9f, 1.3f), "CONTROL: the ring has already passed a player at 2 m.");
        yield return null;
    }

    IEnumerator ModeGating()
    {
        Log("---- MODE GATING");
        foreach (var id in new[] { "hero", "villain", "free-play" })
        {
            var mode = Resources.Load<GameModeDefinition>("Modes/" + id);
            Check(mode != null && mode.Director == null, $"{id}: no round director, so a miniboss can never spawn there.");
        }
        foreach (var id in new[] { "endless-fight", "endless-fight-villain" })
        {
            var mode = Resources.Load<GameModeDefinition>("Modes/" + id);
            Check(mode != null && mode.Director is EndlessWaveDirector, $"{id}: round director present.");
        }
        yield return null;
    }

    IEnumerator FreePlay()
    {
        Log("---- FREE PLAY (unchanged)");
        yield return Enter(F.Heroes[0], "ice", "strength", "free-play");
        for (float until = Time.time + 2.5f; Time.time < until;)
        {
            Check(PulsebreakerBoss.Active == null, "Free Play: no Pulsebreaker exists at any frame.");
            yield return null;
        }
        var hud = Hud;
        Check(hud != null && !hud.BossBarVisible && hud.BossNameShown == null, "Free Play: the boss bar is never shown.");
        Check(W.Mode.Definition.Director == null && W.Npcs.Count > 0, $"Free Play: director null, {W.Npcs.Count} NPCs still populated.");
    }

    // ------------------------------------------------------------------ B. round spawning
    IEnumerator EnterEndless(float intermission, int maxAlive, int firstWave, int perWave, int bossEvery)
    {
        yield return Home();
        Check(Profile.SetLoadout(F.Heroes[0], Power("ice"), Power("strength"), F.Heroes[0].Primary, F.Heroes[0].Secondary), "Loadout saved for the Endless fixture.");
        var shipping = Resources.Load<GameModeDefinition>("Modes/endless-fight");
        var mode = Instantiate(shipping);
        var director = Instantiate((EndlessWaveDirector)shipping.Director);
        var events = Instantiate(Resources.Load<EndlessWaveEvents>("ModeDirectors/EndlessWaveEvents"));
        mode.Director = director; director.Events = events;
        director.IntermissionSeconds = intermission; director.MaxAlive = maxAlive;
        director.FirstWaveCount = firstWave; director.EnemiesPerWave = perWave;
        events.MinibossEvery = bossEvery; events.EliteFromWave = 99; events.ModifierFromWave = 99;
        GameFlow.Instance.Select(mode);
        yield return Scene(GameFlow.CityScene);
        Check(State != null, "Endless clone started with its wave state.");
    }

    void KillEnemies(bool keepBoss)
    {
        foreach (var npc in W.Npcs.ToArray())
            if (npc != null && !npc.Dead && npc.Hostile && !(keepBoss && npc.IsBoss)) npc.Damage(99999f, null);
    }

    IEnumerator RoundSpawning()
    {
        Log("---- ROUND SPAWNING (in-memory director clone: 1 + 1 per wave, miniboss every 5th, no elites/modifiers)");
        yield return EnterEndless(.15f, 6, 1, 1, 5);
        var director = (EndlessWaveDirector)W.Mode.Definition.Director;
        int lastWave = 0;
        for (float until = Time.time + 90f; State.Wave < 5 && Time.time < until;)
        {
            if (State.Wave != lastWave)
            {
                lastWave = State.Wave;
                Check(State.Plan == null || !State.Plan.Miniboss || State.Wave >= 5, $"Round {State.Wave}: no miniboss before round 5.");
                if (State.Plan != null) Check(!State.Plan.Miniboss, $"Round {State.Wave} plan has no miniboss slot.");
            }
            KillEnemies(false);
            yield return null;
        }
        Check(State.Wave == 5, $"Reached round 5 ({State.Wave}).");
        // Wait for the boss spawn (it is the first spawn of the round).
        for (float until = Time.time + 15f; Boss == null && Time.time < until;) { KillEnemies(true); yield return null; }
        Check(Boss != null && Boss.Npc != null && Boss.Npc.IsBoss, "Round 5 spawned exactly one Pulsebreaker.");
        Check(State.Plan != null && State.Plan.Miniboss, "Round 5 plan marks the miniboss slot.");
        Check(Boss.Npc.Archetype == Pulsebreaker, "The spawned body carries the Pulsebreaker archetype.");
        float expectedHealth = director.HealthFor(5) * (State.Plan != null ? State.Plan.Health : 1f) * 8f * W.Difficulty.EnemyHealth;
        Check(Mathf.Abs(Boss.Npc.MaxHealth - expectedHealth) <= expectedHealth * .12f,
            $"Boss health {Boss.Npc.MaxHealth:F1} matches HealthFor(5) x slot 8 x difficulty {W.Difficulty.EnemyHealth:F2} = {expectedHealth:F1} (spawn variance 10%).");
        Check(Boss.Npc.Hostile && Boss.Npc.AlwaysAggro, "Boss is hostile to the player side and permanently aggroed.");
        Check(Boss.Npc.MaxHealth > 600f && Boss.Npc.MaxHealth < 1100f, $"Boss health is a miniboss budget, not a damage sponge ({Boss.Npc.MaxHealth:F0}).");
        int before = PulsebreakerBoss.AttachRefusals;
        Check(PulsebreakerBoss.Attach(Boss.Npc, 5) == Boss && PulsebreakerBoss.AttachRefusals == before, "Re-attaching the same body is idempotent (no duplicate brain).");
        Check(W.AttackTokens.Count <= W.AttackTokens.Budget, $"Attack-token budget respected ({W.AttackTokens.Count}/{W.AttackTokens.Budget}).");

        // The HUD follows the real health of the real boss.
        yield return null; yield return null;
        var hud = Hud;
        Check(hud != null && hud.BossBarVisible, "Boss bar is visible while the boss is alive.");
        Check(hud.BossNameShown == "PULSEBREAKER" && hud.BossPhaseShown == "PHASE 1", $"Boss bar reads {hud.BossNameShown} / {hud.BossPhaseShown}.");
        Check(Mathf.Abs(hud.BossFraction - Boss.Npc.HealthFraction) < .01f, $"Boss bar fraction {hud.BossFraction:P1} equals the authoritative health.");
        Boss.Npc.Damage(Boss.Npc.MaxHealth * .1f, null);
        yield return null; yield return null;
        Check(hud.BossFraction < .95f && Mathf.Abs(hud.BossFraction - Boss.Npc.HealthFraction) < .02f, $"Boss bar follows real damage ({hud.BossFraction:P1}).");

        // Death counts toward round completion and the boss bar disappears.
        Boss.Npc.Damage(99999f, null);
        for (float until = Time.time + 5f; State.MinibossKills < 1 && Time.time < until;) yield return null;
        Check(State.MinibossKills == 1, $"Miniboss kill recorded once ({State.MinibossKills}).");
        for (float until = Time.time + 5f; Time.time < until && PulsebreakerBoss.Active != null;) yield return null;
        Check(PulsebreakerBoss.Active == null, "No active boss after the kill.");
        for (float until = Time.time + 20f; State.Wave < 6 && Time.time < until;) { KillEnemies(false); yield return null; }
        Check(State.Wave >= 5 && State.Wave < 7, $"Round advanced after the boss died (round {State.Wave}).");
        yield return null; yield return null;
        Check(Hud == null || !Hud.BossBarVisible, "Boss bar hidden after death.");
        for (int i = 0; i < 40; i++)
        {
            Check(PulsebreakerBoss.Active == null, "CONTROL: round 6 has no boss.");
            KillEnemies(false);
            if (State.Wave >= 6 && State.Wave != 5) break;
            yield return null;
        }
    }

    // ------------------------------------------------------------------ C. combat fixtures
    IEnumerator Fixtures()
    {
        Log("---- COMBAT FIXTURES (frozen clone: no waves, one controlled boss)");
        yield return EnterEndless(999f, 4, 1, 1, 5);
        yield return null;
        Vector3 hero = W.Hero.transform.position;
        Vector3 lane = ClearLane(hero, out Vector3 at, out float clear);
        Check(clear >= 12f, $"The fixture street gives {clear:F1} m of clear lane along {lane}.");
        var boss = SpawnBoss(at, 900f, 12f);
        Check(boss != null && boss.Npc != null, "Boss spawned for the combat fixture.");
        Check(boss.Npc.MaxHealth >= 810f && boss.Npc.MaxHealth <= 990f && Mathf.Abs(boss.Npc.ContactDamage - 12f) < .01f,
            $"Fixture stats: {boss.Npc.MaxHealth:F1} HP (900 mean with the shared 10% spawn variance), {boss.Npc.ContactDamage} damage exact.");
        Check(boss.Npc.Boss == boss && boss.Npc.IsBoss, "The body knows its boss brain (floating bar suppression + round hooks).");
        for (float until = Time.time + 4f; Time.time < until && boss.State == PulsebreakerState.Idle;) yield return null;
        Check(boss.State != PulsebreakerState.Idle, $"Arrival finished into {boss.State}.");

        // --- slam: telegraph, timing, damage
        yield return BackToApproach(boss);
        PlaceHero(hero);
        PlaceBoss(boss, hero + lane * 3.5f);
        yield return WaitForAttack(boss, PulsebreakerAttack.Slam);
        Check(boss.TelegraphVisible, "The slam shows a ground telegraph during the windup.");
        int hits = boss.PlayerHits, releases = boss.Releases;
        for (float until = Time.time + 3f; Time.time < until && boss.Releases == releases;) yield return null;
        Check(boss.Releases > releases, "Slam released.");
        Check(boss.LastMeasuredWindup >= Tuning.SlamWindup * .9f, $"Measured slam windup {boss.LastMeasuredWindup:F2}s >= {Tuning.SlamWindup:F2}s (readable warning).");
        Check(!boss.TelegraphVisible, "Telegraph hidden at release.");
        Check(boss.SlamRings >= 1 && boss.RingVisible, "The seismic ring expanded from the impact point.");
        for (float until = Time.time + 2f; Time.time < until && boss.PlayerHits == hits;) yield return null;
        Check(boss.PlayerHits > hits, "A grounded player standing in the ring takes slam damage.");
        for (float until = Time.time + 3f; Time.time < until && boss.HoldingToken;) yield return null;
        Check(!boss.HoldingToken, "The attack token is released after the attack (crowd rhythm preserved).");

        // --- slam dodge by distance (a negative control on the same attack)
        yield return BackToApproach(boss);
        PlaceHero(hero);
        PlaceBoss(boss, hero + lane * 12f);
        hits = boss.PlayerHits;
        yield return WaitForAttack(boss, PulsebreakerAttack.Slam);
        for (float until = Time.time + 2f; Time.time < until && boss.State == PulsebreakerState.Execution;) yield return null;
        Check(boss.PlayerHits == hits, $"CONTROL: standing {Vector3.Distance(hero, boss.Npc.transform.position):F0} m away avoids the {Tuning.SlamRadius:F0} m ring entirely.");

        // --- slam dodge by height (jump / hover)
        yield return BackToApproach(boss);
        PlaceHero(hero);
        PlaceBoss(boss, hero + lane * 3.5f);
        hits = boss.PlayerHits;
        bool airborne = false;
        for (float until = Time.time + 12f; Time.time < until;)
        {
            if (boss.State == PulsebreakerState.Windup && boss.Attack == PulsebreakerAttack.Slam) airborne = true;
            if (airborne) HeroY(hero.y + 2.2f);
            else if ((boss.State == PulsebreakerState.Approach || boss.State == PulsebreakerState.Reposition) && !boss.HoldingToken) boss.ForceNextAttack(PulsebreakerAttack.Slam);
            if (boss.State == PulsebreakerState.Recovery) break;
            yield return null;
        }
        HeroY(hero.y);
        Check(airborne, "Boss committed to the slam for the jump control.");
        Check(boss.PlayerHits == hits, "CONTROL: a player 2.2 m above the impact plane is not hit (the jump escape).");

        // --- cannon: telegraph, bolt, damage
        yield return BackToApproach(boss);
        PlaceHero(hero);
        PlaceBoss(boss, hero + lane * 12f);
        int bolts = boss.BoltsFired; hits = boss.PlayerHits;
        yield return WaitForAttack(boss, PulsebreakerAttack.Cannon);
        Check(boss.TelegraphVisible, "The cannon shows a targeting lane during the windup.");
        for (float until = Time.time + 3f; Time.time < until && boss.BoltsFired == bolts;) yield return null;
        Check(boss.BoltsFired == bolts + 1, "One bolt fired at release.");
        Check(boss.BoltObjects == 3, $"The bolt pool is bounded ({boss.BoltObjects} objects, reused).");
        for (float until = Time.time + 2.5f; Time.time < until && boss.PlayerHits == hits;) yield return null;
        Check(boss.PlayerHits > hits, "A stationary player in the locked lane is hit.");

        // --- cannon dodge by moving out of the locked lane
        yield return BackToApproach(boss);
        PlaceHero(hero);
        PlaceBoss(boss, hero + lane * 12f);
        hits = boss.PlayerHits;
        yield return WaitForAttack(boss, PulsebreakerAttack.Cannon);
        Vector3 lockedAt = hero; bolts = boss.BoltsFired;
        for (float until = Time.time + 3f; Time.time < until && boss.BoltsFired == bolts;)
        {
            PlaceHero(lockedAt + Vector3.Cross(Vector3.up, lane) * 3.5f);
            yield return null;
        }
        for (float until = Time.time + 2f; Time.time < until && boss.State == PulsebreakerState.Execution;) yield return null;
        Check(boss.PlayerHits == hits, "CONTROL: stepping 3.5 m out of the locked lane avoids the shot.");

        // --- cannon blocked by a solid wall
        yield return BackToApproach(boss);
        PlaceHero(hero);
        PlaceBoss(boss, hero + lane * 12f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Pulsebreaker verification wall";
        wall.transform.position = hero + lane * 6f + Vector3.up * 2f;
        wall.transform.localScale = new Vector3(6f, 5f, 1f);
        wall.transform.rotation = Quaternion.LookRotation(lane);
        Physics.SyncTransforms();
        hits = boss.PlayerHits; bolts = boss.BoltsFired;
        yield return WaitForAttack(boss, PulsebreakerAttack.Cannon);
        for (float until = Time.time + 3f; Time.time < until && boss.BoltsFired == bolts;) yield return null;
        for (float until = Time.time + 1.5f; Time.time < until && boss.State == PulsebreakerState.Execution;) yield return null;
        Check(boss.PlayerHits == hits, "CONTROL: a solid wall between the boss and the player stops the bolt.");
        Object.DestroyImmediate(wall);
        Physics.SyncTransforms();

        // --- rush: travel, miss recovery, wall stop
        yield return BackToApproach(boss);
        PlaceHero(hero);
        PlaceBoss(boss, hero + lane * 12f);
        int misses = boss.RushMisses;
        Vector3 rushStart = boss.Npc.transform.position;
        yield return WaitForAttack(boss, PulsebreakerAttack.Rush);
        Check(boss.TelegraphVisible, "The rush telegraphs its lane before travelling.");
        // The lane is locked at commit: sidestep it and the rush has to miss.
        PlaceHero(hero + Vector3.Cross(Vector3.up, lane) * 4f);
        for (float until = Time.time + 3f; Time.time < until && boss.State != PulsebreakerState.Recovery && boss.State != PulsebreakerState.Approach;) yield return null;
        float travelled = Vector3.Distance(rushStart, boss.Npc.transform.position);
        Check(travelled > 2f && travelled < 20f, $"Rush travel is bounded and real ({travelled:F1} m).");
        Check(boss.RushMisses > misses, "A rush whose lane was dodged ends in the miss recovery.");
        for (float until = Time.time + 4f; Time.time < until && boss.State != PulsebreakerState.Approach && boss.State != PulsebreakerState.Reposition;) yield return null;
        Check(boss.State == PulsebreakerState.Approach || boss.State == PulsebreakerState.Reposition, $"Rush recovers into {boss.State} (never a stuck state).");

        yield return BackToApproach(boss);
        PlaceHero(hero);
        PlaceBoss(boss, hero + lane * 12f);
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "Pulsebreaker rush blocker";
        blocker.transform.position = hero + lane * 7f + Vector3.up * 2f;
        blocker.transform.localScale = new Vector3(6f, 5f, 1f);
        blocker.transform.rotation = Quaternion.LookRotation(lane);
        Physics.SyncTransforms();
        yield return WaitForAttack(boss, PulsebreakerAttack.Rush);
        for (float until = Time.time + 3f; Time.time < until && boss.State != PulsebreakerState.Recovery && boss.State != PulsebreakerState.Approach;) yield return null;
        float along = Vector3.Dot(boss.Npc.transform.position - hero, lane);
        Check(along < 7f, $"The rush stopped on the near side of the wall ({along:F1} m from the player, wall at 7 m; {boss.RushBlocked} hard blocks).");
        Check(boss.State == PulsebreakerState.Approach || boss.State == PulsebreakerState.Reposition || boss.State == PulsebreakerState.Recovery, $"Boss never sticks in the rush ({boss.State}).");
        Object.DestroyImmediate(blocker);
        Physics.SyncTransforms();

        // --- phase two at 40%
        yield return BackToApproach(boss);
        PlaceHero(hero);
        Check(!boss.Phase2 && boss.PhaseShifts == 0, "Phase 1 before the threshold.");
        boss.Npc.Damage(boss.Npc.MaxHealth * .62f, null);
        for (float until = Time.time + 4f; Time.time < until && !boss.Phase2;) yield return null;
        Check(boss.Phase2 && boss.PhaseShifts == 1, $"Overdrive triggered once at {boss.Npc.HealthFraction:P0} HP (threshold {Tuning.PhaseHealthFraction:P0}).");
        for (float until = Time.time + 3f; Time.time < until && boss.State != PulsebreakerState.Approach && boss.State != PulsebreakerState.Reposition;) yield return null;
        Check(!boss.HoldingToken, "The phase shift releases the attack token.");
        Check(boss.Tuning.Phase2GapMultiplier < 1f && boss.Tuning.Phase2RecoveryMultiplier < 1f, "Phase 2 compresses gaps, not telegraphs.");
        yield return null; yield return null;
        Check(Hud != null && Hud.BossPhaseShown == "PHASE 2", "Boss bar switched to PHASE 2.");
        boss.Npc.Damage(boss.Npc.MaxHealth * .1f, null);
        yield return null; yield return null;
        Check(boss.PhaseShifts == 1, "CONTROL: the phase shift never repeats.");

        // --- death in phase two
        boss.Npc.Damage(99999f, null);
        for (float until = Time.time + 5f; Time.time < until && PulsebreakerBoss.Active != null;) yield return null;
        Check(PulsebreakerBoss.Active == null && boss.Npc.Dead, "Boss dies through the authoritative CityNpc damage path.");
        yield return null; yield return null;
        Check(Hud == null || !Hud.BossBarVisible, "Boss bar disappears on death.");

        // --- death during the phase transition
        var boss2 = SpawnBoss(ClearLane(hero, out Vector3 at2, out _) == lane ? at2 : W.Hero.transform.position + lane * 8f, 400f, 10f);
        Check(boss2 != null && PulsebreakerBoss.Active == boss2, "A second boss can spawn once the first is gone (single live boss, not a one-shot).");
        boss2.Npc.Damage(boss2.Npc.MaxHealth * .61f, null);
        for (float until = Time.time + 3f; Time.time < until && boss2.State != PulsebreakerState.PhaseTransition;) yield return null;
        boss2.Npc.Damage(99999f, null);
        yield return null; yield return null;
        Check(boss2.Npc.Dead && PulsebreakerBoss.Active == null, "Killing the boss during the phase transition is safe (death wins).");
        Check(boss2.PhaseShifts <= 1, "No phase shift survives death.");
    }

    // ------------------------------------------------------------------ D. ordinary systems unchanged
    IEnumerator Regressions()
    {
        Log("---- REGRESSIONS");
        // A fresh session with no boss: ordinary archetypes still cycle their own attacks.
        yield return EnterEndless(999f, 4, 1, 1, 5);
        yield return null;
        Vector3 hero = W.Hero.transform.position;
        ClearLane(hero, out Vector3 at, out _);
        var rusher = CityNpc.Spawn(W, at, NpcRole.Criminal, Resources.Load<EnemyArchetype>("Enemies/Rusher"), 80f, 8f);
        Check(rusher != null && !rusher.IsBoss && rusher.Boss == null, "A normal archetype has no boss brain (inert hook).");
        for (float until = Time.time + 8f; Time.time < until && rusher.Windups == 0;) yield return null;
        Check(rusher.Windups > 0, $"Rusher still runs its own telegraphed cycle ({rusher.Windups} windups).");
        // Freeze resistance: the boss is harder to lock down, an ordinary enemy is not.
        rusher.Freeze(2f);
        Check(rusher.Frozen && Mathf.Abs(rusher.FreezeResistance - 1f) < .0001f, "Ordinary enemies keep freeze resistance 1.");
        yield return null;

        Vector3 lane = ClearLane(W.Hero.transform.position, out Vector3 at2, out _);
        var boss = SpawnBoss(at2, 900f, 12f);
        yield return null;
        boss.Npc.Freeze(2f);
        Check(boss.Npc.Frozen && Mathf.Abs(boss.Npc.FreezeResistance - Tuning.FreezeResistance) < .0001f,
            $"Boss freeze is scaled by {boss.Npc.FreezeResistance} for the same 2 s request (no permanent lock).");
        for (float until = Time.time + 3f; Time.time < until && boss.Npc.Frozen;) yield return null;
        Check(!boss.Npc.Frozen, "The boss thaws normally.");
        Check(boss.State == PulsebreakerState.Approach || boss.State == PulsebreakerState.Staggered || boss.State == PulsebreakerState.Windup || boss.State == PulsebreakerState.Recovery,
            $"Boss resumed a valid state after the freeze ({boss.State}).");

        // Freeze during a windup cancels the telegraphed attack.
        for (float until = Time.time + 6f; Time.time < until && boss.State != PulsebreakerState.Windup;)
        {
            boss.ForceNextAttack(PulsebreakerAttack.Slam);
            yield return null;
        }
        int cancels = boss.Cancels;
        Check(boss.State == PulsebreakerState.Windup, "Boss reached a windup for the cancel control.");
        boss.Npc.Freeze(1f);
        yield return null; yield return null;
        Check(boss.Cancels > cancels && !boss.TelegraphVisible, "Freezing mid-windup cancels the attack and hides the telegraph.");
        for (float until = Time.time + 3f; Time.time < until && boss.Npc.Frozen;) yield return null;
        Check(!boss.HoldingToken, "No token is leaked by a cancelled attack.");
        boss.Npc.Damage(99999f, null);
        for (float until = Time.time + 4f; Time.time < until && PulsebreakerBoss.Active != null;) yield return null;
        Check(PulsebreakerBoss.Active == null, "Cleanup after the regression fixtures.");
    }

    /// Grants XP through the real progression API (which saves immediately) so the separate-process Reload has something
    /// concrete to read back, then records which save file the next process must open.
    IEnumerator Persistence()
    {
        int before = W.Progression.Data.Xp + W.Progression.Data.Level * 10000;
        W.Progression.AddXp(250);
        Check(W.Progression.Data.Xp + W.Progression.Data.Level * 10000 > before,
            $"Progression accepted a real XP grant (level {W.Progression.Data.Level}, {W.Progression.Data.Xp} XP).");
        SessionPath();
        yield return null;
    }

    void SessionPath()
    {
        var file = System.IO.Path.Combine(Folder, "save-path.txt");
        System.IO.Directory.CreateDirectory(Folder);
        System.IO.File.WriteAllText(file, WorldSession.VerificationSavePath);
    }

    // ------------------------------------------------------------------ helpers
    PulsebreakerBoss SpawnBoss(Vector3 position, float health, float damage)
    {
        var npc = CityNpc.Spawn(W, position, NpcRole.Criminal, Pulsebreaker, health, damage);
        if (npc == null) return null;
        return PulsebreakerBoss.AttachForVerification(npc, 5);
    }

    /// Waits for the boss to commit to a specific attack, re-asserting the (verification-only) forced choice until it
    /// lands, so a random attack already in progress can never be mistaken for the one under test.
    IEnumerator WaitForAttack(PulsebreakerBoss boss, PulsebreakerAttack attack, float timeout = 14f)
    {
        for (float until = Time.time + timeout; Time.time < until;)
        {
            if (boss.State == PulsebreakerState.Windup && boss.Attack == attack) break;
            if ((boss.State == PulsebreakerState.Approach || boss.State == PulsebreakerState.Reposition) && !boss.HoldingToken) boss.ForceNextAttack(attack);
            yield return null;
        }
        Check(boss.State == PulsebreakerState.Windup && boss.Attack == attack, $"Boss committed to {attack} (state {boss.State}).");
    }

    void PlaceBoss(PulsebreakerBoss boss, Vector3 position)
    {
        if (boss.Npc.Agent != null && boss.Npc.Agent.isOnNavMesh) boss.Npc.Agent.Warp(position);
        else boss.Npc.transform.position = position;
        Physics.SyncTransforms();
    }

    IEnumerator BackToApproach(PulsebreakerBoss boss)
    {
        for (float until = Time.time + 8f; Time.time < until;)
        {
            if ((boss.State == PulsebreakerState.Approach || boss.State == PulsebreakerState.Reposition) && !boss.HoldingToken) break;
            yield return null;
        }
        Check(boss.State == PulsebreakerState.Approach || boss.State == PulsebreakerState.Reposition, $"Boss is ready for the next fixture ({boss.State}).");
    }

    static Vector3 Offset(float distance) => Vector3.forward * distance;

    /// Picks the clearest straight lane on the NavMesh around the hero: a flat, connected, capsule-clear direction.
    Vector3 ClearLane(Vector3 hero, out Vector3 point, out float distance)
    {
        Vector3 best = hero + Offset(6f);
        point = best;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * 30f;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            for (float d = 14f; d >= 6f; d -= 2f)
            {
                Vector3 candidate = hero + direction * d;
                if (!NavMesh.SamplePosition(candidate, out var hit, 2.5f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - hero.y) > 1.2f) continue;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(hero, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                if (!LaneClear(hero, hit.position)) continue;
                point = hit.position; distance = d; return direction;
            }
        }
        distance = Vector3.Distance(hero, best);
        if (NavMesh.SamplePosition(best, out var fallback, 4f, NavMesh.AllAreas)) point = fallback.position;
        return Vector3.forward;
    }

    bool LaneClear(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from; delta.y = 0f;
        if (delta.magnitude < .5f) return true;
        var hits = Physics.CapsuleCastAll(from + Vector3.up * .5f, from + Vector3.up * 1.4f, .4f, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            var npc = hit.collider.GetComponentInParent<CityNpc>();
            if (npc != null || hit.collider.transform.root == W.Hero.transform) continue;
            return false;
        }
        return true;
    }

    void PlaceHero(Vector3 position)
    {
        var body = W.Hero.GetComponent<CharacterController>();
        body.enabled = false;
        W.Hero.transform.position = position;
        body.enabled = true;
        W.Hero.ResetMotion();
    }

    void HeroY(float y)
    {
        var body = W.Hero.GetComponent<CharacterController>();
        Vector3 position = W.Hero.transform.position;
        body.enabled = false;
        W.Hero.transform.position = new Vector3(position.x, y, position.z);
        body.enabled = true;
    }
}
#endif
