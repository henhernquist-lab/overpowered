#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// See EndlessEventsVerification. (1) The shipping Endless director has no Events: wave 1 keeps the original size, kill
/// and clear scores, and no elite / miniboss / flawless events exist. (2) An in-memory mode + director clone carrying the
/// EndlessEventsSetup recipe (tightened so three waves cover every feature: elites from wave 1, a miniboss every 2nd wave,
/// modifiers every wave from 2, MaxAlive 4): live spawn budget every frame, each enemy's health by slot against the
/// formulas, and every score event summed against the session score, with a flawless wave and a hit (non-flawless) control.
public sealed class EndlessEventsVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/EndlessEvents/";
    protected override string ResultFile => "results.txt";
    /// Set by the editor launcher (EndlessEventsSetup.Build, in memory).
    public EndlessWaveEvents Recipe;
    readonly List<EndlessScoreEvent> events = new List<EndlessScoreEvent>();
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        Check(Recipe != null && Recipe.Modifiers.Length == 5, "Wave-events recipe built in memory (5 modifiers).");
        yield return Defaults();
        yield return WithEvents();
        Log("LIMIT: enemies are defeated by direct damage the moment they spawn (the budget, stats and scoring are what is measured); no difficulty or pacing playtest.");
    }
    EndlessWaveState State => W.Mode.Director as EndlessWaveState;
    IEnumerator WaitWave(int wave, float seconds = 20f)
    {
        float until = Time.time + seconds;
        while ((State.Wave < wave || State.Intermission) && Time.time < until) yield return null;
        Check(State.Wave == wave && !State.Intermission, $"Wave {wave} started.");
    }
    void KillAll() { foreach (var npc in State.Alive.ToArray()) if (npc != null && !npc.Dead) npc.Damage(99999f, W.Powers); }
    IEnumerator Defaults()
    {
        Log("---- SHIPPING DIRECTOR (no events)");
        yield return Enter(F.Heroes[0], "ice", "strength", "endless-fight");
        var d = (EndlessWaveDirector)W.Mode.Definition.Director;
        Check(d.Events == null, "Shipping EndlessWaves has no Events assigned.");
        events.Clear(); State.Scored += events.Add; int score = W.Mode.Score;
        yield return WaitWave(1);
        Check(State.Plan == null && State.Size == d.WaveSize(1), $"Wave 1 plan-free, size {State.Size} = WaveSize(1).");
        float until = Time.time + 20f;
        while (!State.Intermission && Time.time < until) { KillAll(); yield return null; }
        Check(State.Intermission && State.Wave == 1, "Wave 1 cleared.");
        int kills = events.Count(e => e.Kind == EndlessScoreKind.Kill), clears = events.Count(e => e.Kind == EndlessScoreKind.WaveClear);
        Check(kills == d.WaveSize(1) && events.Where(e => e.Kind == EndlessScoreKind.Kill).All(e => e.Amount == d.KillScore) && clears == 1 && events.Single(e => e.Kind == EndlessScoreKind.WaveClear).Amount == d.WaveClearBonus,
            $"Original scoring: {kills} kills x {d.KillScore}, clear {d.WaveClearBonus}.");
        Check(!events.Any(e => e.Kind == EndlessScoreKind.EliteKill || e.Kind == EndlessScoreKind.MinibossKill || e.Kind == EndlessScoreKind.Flawless), "CONTROL: no elite / miniboss / flawless events without Events.");
        Check(W.Mode.Score - score == events.Sum(e => e.Amount), $"Score delta {W.Mode.Score - score} = sum of score events.");
    }
    IEnumerator WithEvents()
    {
        Log("---- DIRECTOR CLONE WITH EVENTS");
        var shipping = Resources.Load<GameModeDefinition>("Modes/endless-fight");
        var mode = Instantiate(shipping); var d = Instantiate((EndlessWaveDirector)shipping.Director); mode.Director = d;
        var ev = Instantiate(Recipe); d.Events = ev; d.IntermissionSeconds = .5f; d.MaxAlive = 4;
        ev.EliteFromWave = 1; ev.EliteShare = .5f; ev.MinibossEvery = 2; ev.ModifierFromWave = 2; ev.ModifierEvery = 1;
        foreach (var m in ev.Modifiers) m.FromWave = 1;
        Log("In-memory clone: intermission 0.5 s, MaxAlive 4, elites from wave 1 at 50%, miniboss every 2nd wave, a modifier every wave from 2.");
        yield return Home();
        Check(Profile.SetLoadout(F.Heroes[0], Power("ice"), Power("strength"), F.Heroes[0].Primary, F.Heroes[0].Secondary), "Loadout saved.");
        GameFlow.Instance.Select(mode); yield return Scene(GameFlow.CityScene);
        events.Clear(); State.Scored += events.Add;
        for (int wave = 1; wave <= 3; wave++)
        {
            yield return WaitWave(wave);
            var plan = State.Plan; int score = W.Mode.Score, first = events.Count; bool hit = wave == 2;
            Check(plan != null && plan.Size == ev.Plan(d, wave).Size, $"Wave {wave} plan: {plan.Size} enemies, {plan.Elites} elites, miniboss {plan.Miniboss}, modifiers [{plan.Names}], x{plan.Health:0.##} hp x{plan.Damage:0.##} dmg x{plan.Score:0.##} score.");
            if (hit) { W.DamagePlayer(1f, true); Log("Player takes 1 damage during wave 2 (flawless CONTROL)."); }
            var seen = new HashSet<CityNpc>(); int budgetViolations = 0, statViolations = 0, bosses = 0, elites = 0; float until = Time.time + 30f;
            while (!State.Intermission && Time.time < until)
            {
                if (State.Alive.Count > d.MaxAlive || (State.Alive.Count > 1 && State.AliveCost > State.AliveTargetNow)) budgetViolations++;
                for (int i = 0; i < State.Alive.Count; i++)
                {
                    var npc = State.Alive[i]; if (npc == null || !seen.Add(npc)) continue;
                    var slot = State.AliveSlots[i]; float arch = npc.Archetype != null ? npc.Archetype.HealthMultiplier : 1f;
                    float expected = d.HealthFor(wave) * plan.Health * arch * (slot == EndlessWaveEvents.Slot.Elite ? ev.EliteHealthMultiplier : slot == EndlessWaveEvents.Slot.Miniboss ? ev.MinibossHealthMultiplier : 1f);
                    if (Mathf.Abs(npc.MaxHealth - expected) > .05f) { statViolations++; Log($"MISMATCH {slot} {npc.MaxHealth:F2} vs {expected:F2}"); }
                    if (slot == EndlessWaveEvents.Slot.Miniboss) { bosses++; if (ev.MinibossArchetype != null && npc.Archetype != ev.MinibossArchetype) statViolations++; }
                    if (slot == EndlessWaveEvents.Slot.Elite) elites++;
                }
                // Defeat one enemy per frame so reinforcements have to wait for budget room.
                var victim = State.Alive.FirstOrDefault(n => n != null && !n.Dead); if (victim != null) victim.Damage(99999f, W.Powers);
                yield return null;
            }
            Check(State.Intermission, $"Wave {wave} cleared ({seen.Count} enemies seen, peak alive {State.PeakAlive}, peak cost {State.PeakAliveCost}).");
            Check(budgetViolations == 0 && State.PeakAlive <= d.MaxAlive, $"Alive budget held every frame: <= {d.MaxAlive} alive, cost <= target (or a lone heavy enemy).");
            Check(statViolations == 0 && seen.Count == plan.Size && elites == plan.Elites && bosses == (plan.Miniboss ? 1 : 0), $"Every enemy's health matches its slot formula ({elites} elites, {bosses} miniboss).");
            var mine = events.Skip(first).ToList();
            int kill = Mathf.RoundToInt(d.KillScore * wave * plan.Score);
            Check(mine.Count(e => e.Kind == EndlessScoreKind.Kill) == plan.Size && mine.Where(e => e.Kind == EndlessScoreKind.Kill).All(e => e.Amount == kill), $"{plan.Size} kill events of {kill} (KillScore x wave x modifier score).");
            Check(mine.Count(e => e.Kind == EndlessScoreKind.EliteKill) == plan.Elites && mine.Where(e => e.Kind == EndlessScoreKind.EliteKill).All(e => e.Amount == ev.EliteKillScore * wave), $"{plan.Elites} elite-kill events of {ev.EliteKillScore * wave}.");
            Check(mine.Count(e => e.Kind == EndlessScoreKind.MinibossKill) == (plan.Miniboss ? 1 : 0), $"Miniboss-kill events: {(plan.Miniboss ? 1 : 0)}.");
            Check(mine.Count(e => e.Kind == EndlessScoreKind.WaveClear) == 1 && mine.Count(e => e.Kind == EndlessScoreKind.Flawless) == (hit ? 0 : 1),
                hit ? "CONTROL: a hit during the wave -> no flawless bonus." : $"No damage taken -> one flawless bonus of {ev.FlawlessBonus * wave}.");
            Check(W.Mode.Score - score == mine.Sum(e => e.Amount), $"Score delta {W.Mode.Score - score} = sum of this wave's score events.");
        }
    }
}
#endif
