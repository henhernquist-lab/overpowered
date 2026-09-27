using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Scene-free Endless simulation (no play mode, no save touched). Uses the SHIPPING director asset's formulas plus the
/// wave-events recipe (EndlessEventsSetup.Build, in memory) and the real rules: EndlessWaveEvents.Plan / SlotOf / CostOf and
/// EndlessWaveState.Room (the alive budget). For every wave 1..50: plan, per-slot stats, a spawn/kill replay of the alive
/// budget (oldest enemy dies first), full-clear score by event, cumulative score, and a time-to-clear estimate at an
/// ASSUMED reference damage rate. Formulas are re-derived independently and compared at waves 1, 5, 10, 20, 25.
/// Output: Verification/EndlessSimulation/results.txt + waves.csv + waves.json.
///   Unity -batchmode -projectPath <copy> -executeMethod EndlessSimulation.Run
public static class EndlessSimulation
{
    const string Folder = "Verification/EndlessSimulation/";
    /// Reference player damage per second for the time estimate (a Strength punch ~35 per ~0.8 s + powers); an assumption.
    const float ReferenceDps = 45f;
    public static void Run()
    {
        int code = 0; var log = new List<string>();
        try { Simulate(log); }
        catch (Exception e) { log.Add("FAIL " + e); code = 1; }
        Directory.CreateDirectory(Folder); File.WriteAllLines(Folder + "results.txt", log);
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
    static void Check(List<string> log, bool ok, string line) { if (!ok) throw new Exception(line); log.Add("PASS " + line); }
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static void Simulate(List<string> log)
    {
        var director = AssetDatabase.LoadAssetAtPath<EndlessWaveDirector>("Assets/Resources/ModeDirectors/EndlessWaves.asset");
        Check(log, director != null, $"Shipping director loaded: FirstWaveCount {director.FirstWaveCount}, EnemiesPerWave {director.EnemiesPerWave}, MaxAlive {director.MaxAlive}, health {director.BaseHealth} x(1+{director.HealthGrowthPerWave}(w-1)), damage {director.BaseDamage} x(1+{director.DamageGrowthPerWave}(w-1)).");
        log.Add($"Shipping director Events: {(director.Events == null ? "none (original waves)" : director.Events.name)}. This simulation uses the EndlessEventsSetup recipe in memory.");
        var events = EndlessEventsSetup.Build();
        // ---- defaults: without events every wave is exactly the original formula
        for (int w = 1; w <= 50; w++)
        {
            int expected = director.FirstWaveCount + director.EnemiesPerWave * (w - 1);
            if (director.WaveSize(w) != expected || director.AliveTarget(w) != Mathf.Min(Mathf.Max(1, director.MaxAlive), expected)) throw new Exception("Original formula mismatch at wave " + w);
        }
        log.Add("PASS Original formulas (no events) unchanged for waves 1-50: size = First + PerWave x (w-1), alive target = min(MaxAlive, size).");
        var csv = new StringBuilder("wave,modifiers,size,regulars,elites,miniboss,health_mul,damage_mul,score_mul,regular_hp,elite_hp,miniboss_hp,regular_dmg,elite_dmg,miniboss_dmg,total_hp,peak_alive,peak_cost,alive_target,clear_score,cumulative_score,est_clear_seconds\n");
        var json = new StringBuilder("[\n"); long cumulative = 0;
        for (int w = 1; w <= 50; w++)
        {
            var plan = events.Plan(director, w);
            var again = events.Plan(director, w);
            if (plan.Size != again.Size || plan.Names != again.Names || plan.Elites != again.Elites) throw new Exception("Plan not deterministic at wave " + w);
            if (plan.Modifiers.Count > events.MaxModifiersPerWave || plan.Modifiers.Distinct().Count() != plan.Modifiers.Count || plan.Modifiers.Any(m => m.FromWave > w)) throw new Exception("Modifier rule broken at wave " + w);
            if (plan.Elites > plan.Regulars || plan.Size != plan.Regulars + (plan.Miniboss ? 1 : 0)) throw new Exception("Slot counts broken at wave " + w);
            int eliteSlots = 0, bossSlots = 0; for (int i = 0; i < plan.Size; i++) { var slot = events.SlotOf(plan, i); if (slot == EndlessWaveEvents.Slot.Elite) eliteSlots++; if (slot == EndlessWaveEvents.Slot.Miniboss) bossSlots++; }
            if (eliteSlots != plan.Elites || bossSlots != (plan.Miniboss ? 1 : 0)) throw new Exception($"SlotOf distribution broken at wave {w}: {eliteSlots} elites vs {plan.Elites}");
            float rHp = director.HealthFor(w) * plan.Health, rDmg = director.DamageFor(w) * plan.Damage;   // regular (archetype multiplier 1 reference)
            float eHp = rHp * events.EliteHealthMultiplier, eDmg = rDmg * events.EliteDamageMultiplier;
            float bossArchH = events.MinibossArchetype != null ? events.MinibossArchetype.HealthMultiplier : 1f, bossArchD = events.MinibossArchetype != null ? events.MinibossArchetype.DamageMultiplier : 1f;
            float bHp = plan.Miniboss ? director.HealthFor(w) * plan.Health * bossArchH * events.MinibossHealthMultiplier : 0f, bDmg = plan.Miniboss ? director.DamageFor(w) * plan.Damage * bossArchD * events.MinibossDamageMultiplier : 0f;
            float totalHp = (plan.Regulars - plan.Elites) * rHp + plan.Elites * eHp + bHp;
            if (float.IsNaN(totalHp) || float.IsInfinity(totalHp) || rHp <= 0f || rDmg <= 0f) throw new Exception("Non-finite stats at wave " + w);
            // Alive-budget replay: fill with the real Room rule, kill the oldest, until the wave is spawned and dead.
            int target = Mathf.Min(Mathf.Max(1, director.MaxAlive), plan.Size), spawned = 0, dead = 0, peak = 0, peakCost = 0, guard = 0;
            var alive = new Queue<int>(); int cost = 0;
            while (dead < plan.Size)
            {
                while (spawned < plan.Size)
                {
                    int next = events.CostOf(events.SlotOf(plan, spawned));
                    if (!EndlessWaveState.Room(alive.Count, cost, next, target, director.MaxAlive)) break;
                    alive.Enqueue(next); cost += next; spawned++;
                }
                peak = Mathf.Max(peak, alive.Count); peakCost = Mathf.Max(peakCost, cost);
                if (alive.Count == 0 || ++guard > 10000) throw new Exception("Alive budget deadlock at wave " + w);
                cost -= alive.Dequeue(); dead++;
            }
            if (peak > director.MaxAlive || (peakCost > target && peak > 1)) throw new Exception($"Alive budget exceeded at wave {w}: {peak} alive, cost {peakCost} / {target}");
            int kill = Mathf.RoundToInt(director.KillScore * w * plan.Score);
            long clear = (long)kill * plan.Size + (long)events.EliteKillScore * w * plan.Elites + (plan.Miniboss ? (long)events.MinibossKillScore * w : 0) + director.WaveClearBonus * w + events.FlawlessBonus * w;
            cumulative += clear;
            float seconds = totalHp / ReferenceDps + director.IntermissionSeconds;
            csv.AppendLine(string.Join(",", w, "\"" + plan.Names + "\"", plan.Size, plan.Regulars, plan.Elites, plan.Miniboss ? 1 : 0, F(plan.Health), F(plan.Damage), F(plan.Score), F(rHp), F(eHp), F(bHp), F(rDmg), F(eDmg), F(bDmg), F(totalHp), peak, peakCost, target, clear, cumulative, F(seconds)));
            json.Append($"  {{\"wave\":{w},\"modifiers\":\"{plan.Names}\",\"size\":{plan.Size},\"elites\":{plan.Elites},\"miniboss\":{(plan.Miniboss ? "true" : "false")},\"regularHp\":{F(rHp)},\"totalHp\":{F(totalHp)},\"peakAlive\":{peak},\"clearScore\":{clear},\"cumulative\":{cumulative},\"estSeconds\":{F(seconds)}}}{(w < 50 ? "," : "")}\n");
            if (w == 1 || w == 5 || w == 10 || w == 20 || w == 25) Formula(log, director, events, plan, w, rHp, eHp, bHp, kill, clear);
            if (w % 5 == 0 || w == 1) log.Add($"WAVE {w,2}: {plan.Size,3} enemies ({plan.Elites} elite{(plan.Miniboss ? ", miniboss" : "")}){(plan.Modifiers.Count > 0 ? " [" + plan.Names + "]" : "")}; regular hp {rHp:F0} dmg {rDmg:F1}; total hp {totalHp:F0}; peak alive {peak} (cost {peakCost}/{target}); full-clear score {clear} (cumulative {cumulative}); est. {seconds:F0} s at {ReferenceDps} dps.");
        }
        json.Append("]\n");
        File.WriteAllText(Folder + "waves.csv", csv.ToString()); File.WriteAllText(Folder + "waves.json", json.ToString());
        log.Add("PASS Waves 1-50: deterministic plans, modifier rules, exact elite/miniboss slot counts, finite stats, no alive-budget deadlock, peak alive <= MaxAlive and cost <= target (a lone heavy enemy excepted).");
        log.Add("NOTE est_clear_seconds assumes a constant " + ReferenceDps + " dps with no travel, misses or defensive time: a ratio between waves, not a playtest number.");
    }
    /// Independent re-derivation of the formulas for one wave (explicit arithmetic, not the plan's own code path).
    static void Formula(List<string> log, EndlessWaveDirector d, EndlessWaveEvents e, WavePlan plan, int w, float rHp, float eHp, float bHp, int kill, long clear)
    {
        int baseSize = d.FirstWaveCount + d.EnemiesPerWave * (w - 1);
        float count = 1f, health = 1f, score = 1f, bonus = 0f; foreach (var m in plan.Modifiers) { count *= m.Count; health *= m.Health; score *= m.Score; bonus += m.EliteShareBonus; }
        bool modWave = w >= e.ModifierFromWave && (w - e.ModifierFromWave) % e.ModifierEvery == 0;
        int mods = modWave ? Math.Min(e.MaxModifiersPerWave, 1 + (w - e.ModifierFromWave) / e.ExtraModifierEvery) : 0;
        int regulars = Math.Max(1, (int)Math.Round(baseSize * (double)count, MidpointRounding.ToEven));
        bool boss = w % e.MinibossEvery == 0;
        float share = w >= e.EliteFromWave ? Mathf.Clamp01(Mathf.Min(e.MaxEliteShare, e.EliteShare + e.EliteShareGrowthPerWave * (w - e.EliteFromWave)) + bonus) : 0f;
        int elites = (int)Math.Floor(share * regulars + 1e-4);
        float hp = d.BaseHealth * (1f + d.HealthGrowthPerWave * (w - 1)) * health;
        Check(log, plan.Modifiers.Count == mods && plan.Regulars == regulars && plan.Miniboss == boss && plan.Elites == elites && Mathf.Abs(rHp - hp) < .01f && Mathf.Abs(eHp - hp * e.EliteHealthMultiplier) < .01f && Mathf.Abs(plan.Score - score) < 1e-4f,
            $"Wave {w} formulas: {mods} modifier(s) [{plan.Names}], {baseSize} base x {count:0.##} = {regulars} regulars, {elites} elites (share {share:P0}), miniboss {boss}, regular hp {hp:F1}, elite hp {hp * e.EliteHealthMultiplier:F1}, miniboss hp {bHp:F1}, kill score {kill}, full clear {clear}.");
    }
}
