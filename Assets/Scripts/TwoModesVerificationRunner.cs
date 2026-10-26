#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// See Assets/Editor/TwoModesVerification.cs. TWO MODES + GUARANTEED PULSEBREAKER ROUNDS:
/// the mode catalog (exactly ENDLESS FIGHT and FREE PLAY on the menu, Hero / Villain still loadable by id), the real Home
/// cards and their real submits, Free Play isolation, the boss schedule for rounds 1-30, live boss rounds 5 and 10 with the
/// boss bar and its cleanup, and the save the run leaves behind.
/// Output: Verification/TwoModes/results.txt.
public sealed class TwoModesVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/TwoModes/";
    protected override string ResultFile => "results.txt";

    ModeScreens menu;
    EndlessWaveState State => W != null && W.Mode != null ? W.Mode.Director as EndlessWaveState : null;
    GameHud Hud => FindAnyObjectByType<GameHud>();
    static GameFlow Flow => GameFlow.Instance;

    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Catalog();
        yield return HomeMenu();
        yield return FreePlay();
        yield return Schedule();
        yield return LiveBossRounds();
        yield return Save();
        Log("LIMIT: batch Play Mode. Rounds are advanced by defeating enemies through CityNpc.Damage; the menu is driven by " +
            "navigation-submit events, not hardware input. No hands-on playtest and no sustained standalone FPS measurement.");
    }

    // ------------------------------------------------------------------ A. the catalog
    IEnumerator Catalog()
    {
        Log("---- MODE CATALOG");
        var modes = Resources.LoadAll<GameModeDefinition>("Modes");
        var visible = modes.Where(m => m.Playable && !m.MenuHidden).OrderBy(m => m.MenuOrder).ToArray();
        Check(visible.Length == 2 && visible[0].Id == "endless-fight" && visible[1].Id == "free-play",
            "Exactly two player-facing modes, in menu order: " + string.Join(" + ", visible.Select(m => m.DisplayName)));
        foreach (var id in new[] { "hero", "villain", "endless-fight-villain" })
        {
            var mode = modes.FirstOrDefault(m => m.Id == id);
            Check(mode != null && mode.Playable && mode.MenuHidden, $"{id}: still a Playable definition, hidden from the menu.");
        }
        var endless = modes.First(m => m.Id == "endless-fight");
        var free = modes.First(m => m.Id == "free-play");
        Check(endless.Director is EndlessWaveDirector && free.Director == null, "Endless Fight owns the round director; Free Play has none.");
        Check(endless.Encounters.Length == 0 && free.Encounters.Length == 0 && (free.Hud & ModeHud.Heat) != 0,
            $"Both modes run without encounter spawning; Free Play keeps the Heat HUD ({free.Hud}).");
        var events = Resources.Load<EndlessWaveEvents>("ModeDirectors/EndlessWaveEvents");
        Check(events != null && events.MinibossEvery == 5 && events.MinibossArchetype != null && events.MinibossArchetype.DisplayName == "Pulsebreaker",
            $"Boss schedule data: one {events.MinibossArchetype.DisplayName} every {events.MinibossEvery} rounds.");
        Check(Resources.Load<PulsebreakerTuning>("Enemies/PulsebreakerTuning") != null, "Boss tuning asset loads.");
        yield return null;
    }

    // ------------------------------------------------------------------ B. the two Home cards
    IEnumerator HomeMenu()
    {
        Log("---- HOME MENU");
        menu = FindAnyObjectByType<ModeScreens>();
        for (int i = 0; i < 12; i++) yield return null;
        Check(menu != null, "Home menu exists.");
        var buttons = menu.ModeButtons;
        Check(buttons.Count == 3 && buttons.ContainsKey("endless-fight") && buttons.ContainsKey("free-play") && buttons.ContainsKey("battle-lab"),
            "Home offers exactly two mode cards plus the Battle Lab route: " + string.Join(", ", buttons.Keys));
        Check(!buttons.ContainsKey("hero") && !buttons.ContainsKey("villain") && !buttons.ContainsKey("endless-fight-villain"),
            "No Hero, Villain or Endless (Villain) card remains on Home.");
        var endlessCard = buttons["endless-fight"]; var freeCard = buttons["free-play"];
        Check(endlessCard.enabledInHierarchy && freeCard.enabledInHierarchy && endlessCard.worldBound.width > 0 && freeCard.worldBound.width > 0,
            $"Both mode cards render at real size ({endlessCard.worldBound}, {freeCard.worldBound}).");
        Check(endlessCard.worldBound.xMax < freeCard.worldBound.xMin && Mathf.Abs(endlessCard.worldBound.y - freeCard.worldBound.y) < 1f,
            "The two cards sit side by side in the same row.");
        Check(menu.ForgeButton != null && menu.ForgeButton.enabledInHierarchy && menu.ForgeButton.name == "hero-forge-open",
            "Hero Forge keeps its own Home route (it is not a third gameplay mode).");
        Check(buttons["battle-lab"].worldBound.yMin > endlessCard.worldBound.yMax, "Battle Lab keeps its route below the cards.");

        Submit(endlessCard); yield return Scene(GameFlow.CityScene);
        Check(W.Mode != null && W.Mode.Definition.Id == "endless-fight" && State != null,
            "Real ENDLESS FIGHT card submit starts an Endless session with its wave director.");
        Check(menu == null || !menu.isActiveAndEnabled, "The Home menu is gone once the session starts (no lingering UI document).");
        yield return WaitWave(1, 25f);
        int first = State.Size;
        yield return ClearWave(25f);
        Check(State.Intermission && State.Wave == 1, $"Round 1 cleared ({first} enemies) through normal kills.");
        yield return WaitWave(2, 25f);
        Check(State.Wave == 2 && State.Spawned > 0 && !State.Intermission, "Round 2 started: rounds keep progressing.");
        Flow.Home(); yield return Scene(GameFlow.HomeScene); yield return Menu();
        Submit(menu.ModeButtons["free-play"]); yield return Scene(GameFlow.CityScene);
        Check(W.Mode.Definition.Id == "free-play" && W.Mode.Definition.Director == null && State == null,
            "Real FREE PLAY card submit starts a Free Play session with no director and no wave state.");
        Flow.Home(); yield return Scene(GameFlow.HomeScene);
    }

    // ------------------------------------------------------------------ C. Free Play stays free
    IEnumerator FreePlay()
    {
        Log("---- FREE PLAY (no waves, no boss)");
        yield return Menu();
        Submit(menu.ModeButtons["free-play"]); yield return Scene(GameFlow.CityScene);
        Check(W.Mode.Definition.Id == "free-play" && W.Mode.Definition.Director == null, "Free Play session runs without a round director.");
        int frames = 0, bossFrames = 0;
        for (float until = Time.time + 3f; Time.time < until;)
        {
            frames++; if (PulsebreakerBoss.Active != null) bossFrames++;
            yield return null;
        }
        Check(bossFrames == 0, $"Free Play: no Pulsebreaker existed on any of {frames} frames.");
        var hud = Hud;
        Check(hud != null && !hud.BossBarVisible, "Free Play: the boss bar is never shown.");
        Check(W.Npcs.Count > 0, $"Free Play still populates the city ({W.Npcs.Count} NPCs).");
        // The Combat Lab / manual spawn path is not a mode: it must not create a boss wave either.
        Check(W.Mode.Director == null, "CONTROL: repeated frames never install a director in Free Play.");
        Flow.Home(); yield return Scene(GameFlow.HomeScene);
    }

    // ------------------------------------------------------------------ D. the schedule (pure data)
    IEnumerator Schedule()
    {
        Log("---- BOSS SCHEDULE (plan-level, rounds 1-30)");
        var director = (EndlessWaveDirector)Resources.Load<GameModeDefinition>("Modes/endless-fight").Director;
        var events = director.Events;
        int bossRounds = 0;
        for (int wave = 1; wave <= 30; wave++)
        {
            var plan = events.Plan(director, wave, 20261025);
            bool expected = wave >= 5 && wave % 5 == 0;
            Check(plan.Miniboss == expected, $"Round {wave}: miniboss {(plan.Miniboss ? "scheduled" : "absent")} (wave of {plan.Size} enemies).");
            if (!plan.Miniboss) continue;
            bossRounds++;
            Check(events.SlotOf(plan, 0) == EndlessWaveEvents.Slot.Miniboss,
                $"Round {wave}: the first spawn is the reserved miniboss slot, ahead of every regular body.");
            Check(plan.Regulars < Mathf.RoundToInt(director.WaveSize(wave) * plan.Count),
                $"Round {wave}: the boss takes {events.MinibossRegularCost} regular bodies off the wave instead of stacking on top of it.");
        }
        Check(bossRounds == 6, $"{bossRounds} boss rounds in 30: 5, 10, 15, 20, 25, 30.");
        foreach (var wave in new[] { 1, 2, 3, 4, 6, 7, 8, 9, 11, 14, 16 })
            Check(!events.Plan(director, wave, 20261025).Miniboss, $"CONTROL: round {wave} has no miniboss.");
        Check(events.Plan(director, 5, 1).Miniboss && events.Plan(director, 5, int.MaxValue).Miniboss && events.Plan(director, 5, -99).Miniboss,
            "The boss schedule is a rule, not a seeded draw: every run seed gets the boss on round 5.");
        yield return null;
    }

    // ------------------------------------------------------------------ E. live boss rounds
    IEnumerator LiveBossRounds()
    {
        Log("---- LIVE BOSS ROUNDS (in-memory clone: 1 + 1 per round, 0.2 s intermission, miniboss every 5th)");
        yield return Home();
        Check(Profile.SetLoadout(F.Heroes[0], Power("ice"), Power("strength"), F.Heroes[0].Primary, F.Heroes[0].Secondary), "Loadout saved for the boss-round fixture.");
        var shipping = Resources.Load<GameModeDefinition>("Modes/endless-fight");
        var mode = Instantiate(shipping);
        var director = Instantiate((EndlessWaveDirector)shipping.Director);
        var events = Instantiate(Resources.Load<EndlessWaveEvents>("ModeDirectors/EndlessWaveEvents"));
        mode.Director = director; director.Events = events;
        director.IntermissionSeconds = .2f; director.MaxAlive = 6; director.FirstWaveCount = 1; director.EnemiesPerWave = 1;
        events.EliteFromWave = 99; events.ModifierFromWave = 99; events.MinibossEvery = 5;
        EndlessWaveState.RunSeedOverride = 20261025;
        Flow.Select(mode); yield return Scene(GameFlow.CityScene);
        Check(State != null && State.Wave <= 1, $"Clone session started at round {State.Wave}.");

        int lastWave = 0, bosses = 0, bossRound = 0;
        // Runs until both boss rounds (5 and 10) have produced a boss, so round 10's boss is never cut off by the wave cap.
        for (float deadline = Time.time + 240f; bosses < 2 && Time.time < deadline;)
        {
            if (State.Wave != lastWave)
            {
                lastWave = State.Wave;
                bool expected = State.Wave >= 5 && State.Wave % 5 == 0;
                Check(State.Plan == null || State.Plan.Miniboss == expected,
                    $"Round {State.Wave} plan: miniboss {(State.Plan != null && State.Plan.Miniboss ? "yes" : "no")} (expected {(expected ? "yes" : "no")}).");
            }
            var boss = PulsebreakerBoss.Active;
            if (boss != null)
            {
                if (boss.Wave != bossRound)
                {
                    bossRound = boss.Wave; bosses++;
                    yield return null; yield return null;
                    var hud = Hud;
                    Check(boss.Npc != null && boss.Npc.IsBoss && boss.Npc.Archetype != null && boss.Npc.Archetype.DisplayName == "Pulsebreaker",
                        $"Round {boss.Wave}: the live body is a real Pulsebreaker ({boss.Npc.MaxHealth:F0} max HP, {boss.Npc.ContactDamage:F1} contact damage).");
                    Check(boss.Npc.Hostile && boss.Npc.AlwaysAggro, $"Round {boss.Wave}: the boss is hostile and permanently aggroed.");
                    Check(hud != null && hud.BossBarVisible, $"Round {boss.Wave}: the boss bar is visible while it is alive.");
                    Check(hud.BossNameShown == "PULSEBREAKER" && hud.BossPhaseShown == "PHASE 1",
                        $"Round {boss.Wave}: the bar reads {hud.BossNameShown} / {hud.BossPhaseShown}.");
                    Check(Mathf.Abs(hud.BossFraction - boss.Npc.HealthFraction) < .02f,
                        $"Round {boss.Wave}: the bar shows the authoritative fraction ({hud.BossFraction:P1} of {boss.Npc.MaxHealth:F0} HP).");
                    Check(hud.OverlayRoot.Query<VisualElement>("hud-boss").ToList().Count == 1,
                        $"Round {boss.Wave}: exactly one boss bar element exists (no UI accumulation).");
                    // The bar follows real damage before the kill.
                    boss.Npc.Damage(boss.Npc.MaxHealth * .1f, null);
                    yield return null; yield return null;
                    Check(hud.BossFraction < .95f && Mathf.Abs(hud.BossFraction - boss.Npc.HealthFraction) < .03f,
                        $"Round {boss.Wave}: bar follows real damage ({hud.BossFraction:P1}).");
                    boss.Npc.Damage(99999f, null);
                }
            }
            foreach (var npc in W.Npcs.ToArray())
                if (npc != null && !npc.Dead && npc.Hostile && !npc.IsBoss) npc.Damage(99999f, null);
            yield return null;
        }
        Check(bosses == 2 && State.Wave >= 10, $"Rounds 5 and 10 each spawned exactly one Pulsebreaker; run reached round {State.Wave}.");
        Check(W.Mode.Score > 0 && State.Kills >= 10, $"Session counters come from the real director: {State.Kills} kills, {W.Mode.Score} score.");
        for (float until = Time.time + 6f; Time.time < until && PulsebreakerBoss.Active != null;) yield return null;
        Check(PulsebreakerBoss.Active == null, "No boss remains after the last boss round.");
        Check(Hud == null || !Hud.BossBarVisible, "The boss bar is gone after the last boss died.");
        Check(State.MinibossKills == 2, $"Both boss kills counted toward round completion ({State.MinibossKills}).");
        yield return null;
        // Control: rounds 11-14 are not boss rounds, so no boss may exist on any frame of them.
        int strayBoss = 0;
        for (int i = 0; i < 150 && State.Wave < 15; i++)
        {
            if (PulsebreakerBoss.Active != null && State.Wave >= 11) { strayBoss++; PulsebreakerBoss.Active.Npc.Damage(99999f, null); }
            foreach (var npc in W.Npcs.ToArray()) if (npc != null && !npc.Dead && npc.Hostile && !npc.IsBoss) npc.Damage(99999f, null);
            yield return null;
        }
        Check(strayBoss == 0, $"CONTROL: rounds 11-14 carried no boss on any frame (now round {State.Wave}).");
        Check(State.Wave >= 15, $"Run reached round {State.Wave}.");
        // Round 15 is the next boss round: nothing but the wave itself should be needed for the boss to arrive.
        for (float until = Time.time + 45f; PulsebreakerBoss.Active == null && Time.time < until;)
        {
            foreach (var npc in W.Npcs.ToArray()) if (npc != null && !npc.Dead && npc.Hostile && !npc.IsBoss) npc.Damage(99999f, null);
            yield return null;
        }
        var third = PulsebreakerBoss.Active;
        Check(third != null && third.Wave == 15, $"Round 15 spawned the third Pulsebreaker (wave {third?.Wave}).");
        yield return null; yield return null;
        Check(Hud != null && Hud.BossBarVisible && Hud.BossPhaseShown == "PHASE 1", "Round 15: the bar is back for the new boss.");
        third.Npc.Damage(99999f, null);
        for (float until = Time.time + 6f; Time.time < until && PulsebreakerBoss.Active != null;) yield return null;
        Check(State.MinibossKills == 3, $"Three boss kills counted toward round completion ({State.MinibossKills}).");
    }

    // ------------------------------------------------------------------ F. the save
    IEnumerator Save()
    {
        Log("---- SAVE");
        int wave = State.Wave, score = W.Mode.Score;
        Check(wave >= 10, $"Run reached round {wave} before the session ended.");
        W.Mode.Finish(SessionOutcome.Lost, "Two-mode verification ended the run after the boss rounds");
        yield return Scene(GameFlow.ResultsScene);
        var disk = JsonUtility.FromJson<ProgressSave>(File.ReadAllText(WorldSession.VerificationSavePath));
        Check(disk != null && disk.ModeRecords.Count > 0, $"Save holds {disk?.ModeRecords.Count} mode record(s).");
        Check(disk.ModeRecords.All(r => Resources.Load<GameModeDefinition>("Modes/" + r.Id) != null),
            "Every saved mode id still resolves to a definition (no orphan ids after the menu change): " + string.Join(", ", disk.ModeRecords.Select(r => r.Id)));
        Check(disk.LastModeId == "endless-fight" && Profile.BestScore("endless-fight") == score,
            $"Saved last mode {disk.LastModeId}; best score {Profile.BestScore("endless-fight")} equals the run's {score}.");
        Check(Profile.Record("endless-fight").BestWave == wave, $"Best wave recorded: {Profile.Record("endless-fight").BestWave} (run reached {wave}).");
        yield return null;
    }

    // ------------------------------------------------------------------ helpers
    IEnumerator Menu()
    {
        for (float until = Time.time + 10f; menu == null || !menu.isActiveAndEnabled || menu.ModeButtons.Count == 0;)
        {
            if (Time.time > until) throw new System.Exception("Home menu timeout");
            menu = FindAnyObjectByType<ModeScreens>();
            yield return null;
        }
        for (int i = 0; i < 8; i++) yield return null;
    }
    void Submit(Button button) { using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
    IEnumerator WaitWave(int wave, float seconds)
    {
        float until = Time.time + seconds;
        while ((State == null || State.Wave < wave || State.Intermission) && Time.time < until) yield return null;
        Check(State != null && State.Wave == wave && !State.Intermission, $"Round {wave} started.");
    }
    IEnumerator ClearWave(float seconds)
    {
        float until = Time.time + seconds;
        while (State != null && !State.Intermission && Time.time < until)
        {
            foreach (var npc in W.Npcs.ToArray()) if (npc != null && !npc.Dead && npc.Hostile && !npc.IsBoss) npc.Damage(99999f, null);
            yield return null;
        }
        Check(State != null && State.Intermission, "Round cleared.");
    }
}
#endif
