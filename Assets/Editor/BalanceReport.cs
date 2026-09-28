using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Scene-free balance report from the SHIPPING data (power assets at every tier, Forge heroes' stat archetypes, energy tuning,
/// the Endless director). It changes nothing; it flags. Model (stated in the output): one target, perfect aim and uptime, no
/// travel, enough targets for chains / spreads when noted. Per power and tier: burst (all charges), sustained damage per second
/// limited by cooldown, charge recharge and energy regen, and the binding limit. Per hero x loadout (55 pairs): combined
/// sustained dps, burst, synergy, and z-score outliers (|z| >= 2). Endless: seconds for each loadout to kill a wave's total
/// health at waves 1 / 5 / 10 / 20 (original formulas). Poison / Laser / Lightning / Force Field use their own models.
/// Output: Verification/Balance/results.txt, powers.csv, loadouts.csv, summary.json.
///   Unity -batchmode -projectPath <copy> -executeMethod BalanceReport.Run
public static class BalanceReport
{
    const string Folder = "Verification/Balance/";
    static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    public static void Run()
    {
        var log = new List<string>(); int code = 0;
        try { RosterSetup.Create(); Report(log); } catch (Exception e) { log.Add("FAIL " + e); code = 1; }
        Directory.CreateDirectory(Folder); File.WriteAllLines(Folder + "results.txt", log);
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
    sealed class Row { public string Power, Limit, Note; public int Tier, Charges; public float Damage, Cooldown, Recharge, Cost, Burst, Sustained; }
    static void Report(List<string> log)
    {
        var tuning = Resources.Load<GameTuning>("GameTuning"); var forge = Resources.Load<ForgeCatalog>("ForgeCatalog");
        var powers = Resources.LoadAll<PowerDefinition>("Powers").OrderBy(p => p.Id).ToArray();
        var waves = AssetDatabase.LoadAssetAtPath<EndlessWaveDirector>("Assets/Resources/ModeDirectors/EndlessWaves.asset");
        float energy = tuning.Movement.Energy, regen = tuning.Movement.EnergyRecharge;
        log.Add($"MODEL: single target, perfect aim / uptime, no travel; energy {energy} (+{regen}/s) x hero archetype. A report, not a playtest.");
        var csv = new StringBuilder("power,tier,damage,cooldown,charges,recharge,cost,burst,sustained_dps,limit,note\n");
        var rows = new List<Row>();
        foreach (var p in powers)
            for (int tier = 0; tier <= p.Upgrades.Length; tier++)
            {
                var r = Model(p, p.GetStats(tier), HeroStats.Baseline, energy, regen); r.Tier = tier; rows.Add(r);
                csv.AppendLine(string.Join(",", p.Id, tier, F(r.Damage), F(r.Cooldown), r.Charges, F(r.Recharge), F(r.Cost), F(r.Burst), F(r.Sustained), r.Limit, "\"" + r.Note + "\""));
            }
        File.WriteAllText(Folder + "powers.csv", csv.ToString());
        log.Add("---- POWERS (tier 0, baseline hero)");
        foreach (var r in rows.Where(r => r.Tier == 0).OrderByDescending(r => r.Sustained))
            log.Add($"{r.Power,-12} sustained {F(r.Sustained),7} dps (limited by {r.Limit}), burst {F(r.Burst),6}; {r.Note}");
        var damaging = rows.Where(r => r.Tier == 0 && r.Sustained > 0f).ToList();
        float mean = damaging.Average(r => r.Sustained), sd = Mathf.Sqrt(damaging.Average(r => (r.Sustained - mean) * (r.Sustained - mean)));
        foreach (var r in damaging) { float z = sd > 0 ? (r.Sustained - mean) / sd : 0; if (Mathf.Abs(z) >= 1.5f) log.Add($"FLAG power {r.Power}: sustained {F(r.Sustained)} dps is {F(z)} sd from the damaging-power mean {F(mean)}."); }
        // ---- loadouts x heroes
        var loadCsv = new StringBuilder("hero,power_a,power_b,synergy,sustained_dps,burst,wave1_s,wave5_s,wave10_s,wave20_s\n");
        var pairs = new List<(string hero, string a, string b, string syn, float dps, float burst)>();
        foreach (var hero in forge.Heroes)
            for (int i = 0; i < powers.Length; i++)
                for (int j = i + 1; j < powers.Length; j++)
                {
                    var a = Model(powers[i], powers[i].GetStats(0), hero.Stats ?? HeroStats.Baseline, energy, regen);
                    var b = Model(powers[j], powers[j].GetStats(0), hero.Stats ?? HeroStats.Baseline, energy, regen);
                    // Two powers share one energy pool: if both are energy-limited, their sum cannot exceed the pool's rate.
                    float dps = a.Sustained + b.Sustained; if (a.Limit == "energy" && b.Limit == "energy") dps = Mathf.Max(a.Sustained, b.Sustained);
                    var syn = forge.Resolve(powers[i], powers[j]);
                    pairs.Add((hero.Id, powers[i].Id, powers[j].Id, syn != null ? syn.Id : "-", dps, a.Burst + b.Burst));
                    string[] ttk = new[] { 1, 5, 10, 20 }.Select(w => dps > 0 ? F(waves.WaveSize(w) * waves.HealthFor(w) / dps) : "inf").ToArray();
                    loadCsv.AppendLine(string.Join(",", hero.Id, powers[i].Id, powers[j].Id, syn != null ? syn.Id : "-", F(dps), F(a.Burst + b.Burst), string.Join(",", ttk)));
                }
        File.WriteAllText(Folder + "loadouts.csv", loadCsv.ToString());
        log.Add($"---- LOADOUTS ({forge.Heroes.Length} heroes x 55 pairs; basic melee not included)");
        float lm = pairs.Average(p => p.dps), ls = Mathf.Sqrt(pairs.Average(p => (p.dps - lm) * (p.dps - lm)));
        foreach (var p in pairs.OrderByDescending(p => p.dps).Take(5)) log.Add($"TOP    {p.hero} {p.a}+{p.b}: {F(p.dps)} dps, burst {F(p.burst)}{(p.syn != "-" ? ", synergy " + p.syn : "")}");
        foreach (var p in pairs.OrderBy(p => p.dps).Take(5)) log.Add($"BOTTOM {p.hero} {p.a}+{p.b}: {F(p.dps)} dps, burst {F(p.burst)}{(p.syn != "-" ? ", synergy " + p.syn : "")}");
        int flagged = 0;
        foreach (var p in pairs) { float z = ls > 0 ? (p.dps - lm) / ls : 0; if (Mathf.Abs(z) >= 2f) { flagged++; log.Add($"FLAG loadout {p.hero} {p.a}+{p.b}: {F(p.dps)} dps, z {F(z)}."); } }
        log.Add($"Loadout sustained dps: mean {F(lm)}, sd {F(ls)}, {flagged} outlier(s) at |z| >= 2.");
        log.Add("---- ENDLESS (original formulas): seconds of pure damage to clear a wave, median loadout");
        var median = pairs.OrderBy(p => p.dps).ElementAt(pairs.Count / 2);
        foreach (int w in new[] { 1, 5, 10, 20, 30 }) log.Add($"wave {w,2}: {waves.WaveSize(w)} x {F(waves.HealthFor(w))} hp = {F(waves.WaveSize(w) * waves.HealthFor(w))} hp -> {F(waves.WaveSize(w) * waves.HealthFor(w) / Mathf.Max(.01f, median.dps))} s at the median {F(median.dps)} dps ({median.a}+{median.b}).");
        var json = new StringBuilder("{\n");
        json.Append($"  \"powerMeanDps\": {F(mean)}, \"powerSdDps\": {F(sd)}, \"loadoutMeanDps\": {F(lm)}, \"loadoutSdDps\": {F(ls)}, \"loadoutOutliers\": {flagged},\n");
        json.Append("  \"powers\": [" + string.Join(", ", rows.Where(r => r.Tier == 0).Select(r => $"{{\"id\":\"{r.Power}\",\"dps\":{F(r.Sustained)},\"burst\":{F(r.Burst)},\"limit\":\"{r.Limit}\"}}")) + "]\n}\n");
        File.WriteAllText(Folder + "summary.json", json.ToString());
        log.Add("PASS Report written: powers.csv, loadouts.csv, summary.json (flags are for Henry's playtest, not failures).");
    }
    /// Sustained damage per second for one power under the stated model, and what limits it.
    static Row Model(PowerDefinition p, PowerStats s, HeroStats hero, float energy, float regen)
    {
        var r = new Row { Power = p.Id, Charges = s.Charges, Recharge = p.ChargeRecharge, Cost = p.ResourceCost, Note = "" };
        bool melee = p.Effect is PunchEffect; float dmgMul = melee ? hero.MeleeDamage : hero.PowerDamage, cdMul = melee || (p.Effect != null && p.Effect.IsFlight) ? 1f : hero.CooldownMultiplier;
        float damage = s.Damage * dmgMul, cooldown = s.Cooldown * cdMul, pool = energy * hero.MaxEnergy, rate = regen * hero.EnergyRegen;
        r.Damage = damage; r.Cooldown = cooldown;
        if (p.Effect == null || p.Effect.IsFlight) { r.Limit = "n/a"; r.Note = "traversal"; return r; }
        if (p.Effect is ForceFieldEffect ff) { r.Limit = "n/a"; r.Note = $"defensive: absorbs {ff.Capacity} per cast, {F(ff.Capacity * Mathf.Min(1f / Mathf.Max(.01f, cooldown), 1f / Mathf.Max(.01f, p.ChargeRecharge)))} absorb/s sustained"; return r; }
        if (p.Activation == PowerActivation.Channeled)
        {
            float net = p.DrainPerSecond - rate; float duty = net <= 0 ? 1f : rate / p.DrainPerSecond;
            r.Burst = net <= 0 ? damage * 10f : damage * pool / net; r.Sustained = damage * duty; r.Limit = net <= 0 ? "none" : "energy";
            r.Note = $"channel {F(damage)}/s, drain {p.DrainPerSecond}/s; full pool lasts {(net <= 0 ? "forever" : F(pool / net) + " s")}"; return r;
        }
        float perCast = damage;
        if (p.Effect is PoisonEffect) { perCast = damage * s.Duration; r.Note = $"{F(damage)}/s over {s.Duration} s per target (+ spreads on death)"; }
        else if (p.Effect is LightningEffect le) { float chain = 0, d = damage; for (int k = 0; k <= le.MaxArcs; k++) { chain += d; d *= le.Falloff; } r.Note = $"single target {F(damage)}; full chain of {le.MaxArcs + 1} = {F(chain)} total"; }
        else if (p.Effect is DarknessEffect) r.Note = $"roots {s.Duration} s";
        else if (p.Effect is SpeedDashEffect) r.Note = "per enemy passed";
        else if (p.Effect is FireBlastEffect) r.Note = $"area radius {s.Radius}";
        float byCooldown = 1f / Mathf.Max(.01f, cooldown), byCharges = 1f / Mathf.Max(.01f, p.ChargeRecharge), byEnergy = p.ResourceCost > 0 ? rate / p.ResourceCost : float.PositiveInfinity;
        float uses = Mathf.Min(byCooldown, Mathf.Min(byCharges, byEnergy));
        r.Limit = uses == byCooldown ? "cooldown" : uses == byCharges ? "charge recharge" : "energy";
        r.Sustained = perCast * uses; r.Burst = perCast * Mathf.Max(1, s.Charges);
        return r;
    }
}
