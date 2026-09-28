#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

/// See HeroStatsVerification. Each hero plays a real Hero session (Strength + Ice equipped through the saved loadout) and the
/// same measurements are taken for all three, so VECTOR (baseline) is the CONTROL for every TITAN / NOVA difference:
/// max health (and who survives a 90-damage hit), max energy, energy regen (measured with the controller running), run speed
/// (real CharacterController displacement), melee damage + knockback force (paid punch on a real actor), power cooldown
/// (Ice), incoming knockback (a live Brute's slam in the city). PowerDamage is 1 on every shipping hero, so it is shown with
/// an IN-MEMORY test hero (x1.5, catalog restored immediately). Also checks the Hero Forge comparison bars.
public sealed class HeroStatsVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/HeroStats/";
    protected override string ResultFile => "results.txt";
    static ForgeCatalog registeredCatalog; static HeroDefinition[] originalHeroes;
    public static void RestoreCatalog() { if (registeredCatalog != null && originalHeroes != null) registeredCatalog.Heroes = originalHeroes; registeredCatalog = null; originalHeroes = null; }
    readonly Dictionary<string, Dictionary<string, float>> measured = new Dictionary<string, Dictionary<string, float>>();
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var vector = F.Hero("vector"); var titan = F.Hero("titan"); var nova = F.Hero("nova");
        Check(vector.Stats.IsBaseline, "VECTOR is the baseline (all multipliers 1, no resistance).");
        var t = titan.Stats; var n = nova.Stats;
        Check(Mathf.Approximately(t.MaxHealth, 1.4f) && Mathf.Approximately(t.MeleeDamage, 1.25f) && Mathf.Approximately(t.MoveSpeed, .85f) && Mathf.Approximately(t.MaxEnergy, .8f) && t.KnockbackResistance > 0,
            $"TITAN data: health x{t.MaxHealth}, melee x{t.MeleeDamage}, speed x{t.MoveSpeed}, energy x{t.MaxEnergy}, knockback resist {t.KnockbackResistance}.");
        Check(Mathf.Approximately(n.MaxEnergy, 1.4f) && Mathf.Approximately(n.EnergyRegen, 1.4f) && Mathf.Approximately(n.CooldownMultiplier, .8f) && Mathf.Approximately(n.MoveSpeed, 1.1f) && Mathf.Approximately(n.MaxHealth, .75f),
            $"NOVA data: energy x{n.MaxEnergy}, regen x{n.EnergyRegen}, cooldowns x{n.CooldownMultiplier}, speed x{n.MoveSpeed}, health x{n.MaxHealth}.");
        yield return ForgeBars(vector, titan, nova);
        foreach (var hero in new[] { vector, titan, nova }) yield return Measure(hero);
        yield return Knockback(vector); yield return Knockback(titan);
        yield return PowerDamage(vector);
        // ------------------------------------------------ comparisons against the VECTOR control
        var v = measured["vector"]; var ti = measured["titan"]; var no = measured["nova"];
        Check(Mathf.Abs(ti["health"] - v["health"] * 1.4f) < .01f && Mathf.Abs(no["health"] - v["health"] * .75f) < .01f, $"MaxHealth: {v["health"]} / {ti["health"]} / {no["health"]} (VECTOR / TITAN / NOVA).");
        Check(ti["survived"] == 1 && v["survived"] == 1 && no["survived"] == 0, "MaxHealth in play: a 90-damage hit leaves TITAN and VECTOR alive and defeats NOVA.");
        Check(Mathf.Abs(ti["energy"] - v["energy"] * .8f) < .01f && Mathf.Abs(no["energy"] - v["energy"] * 1.4f) < .01f, $"MaxEnergy: {v["energy"]} / {ti["energy"]} / {no["energy"]}.");
        Check(Mathf.Abs(no["regen"] / v["regen"] - 1.4f) < .05f && Mathf.Abs(ti["regen"] / v["regen"] - 1f) < .05f, $"EnergyRegen measured: {v["regen"]:F2} / {ti["regen"]:F2} / {no["regen"]:F2} per s.");
        Check(Mathf.Abs(ti["speed"] / v["speed"] - .85f) < .04f && Mathf.Abs(no["speed"] / v["speed"] - 1.1f) < .04f, $"MoveSpeed measured: {v["speed"]:F2} / {ti["speed"]:F2} / {no["speed"]:F2} m/s.");
        Check(Mathf.Abs(ti["melee"] / v["melee"] - 1.25f) < .01f && Mathf.Abs(no["melee"] / v["melee"] - 1f) < .01f, $"MeleeDamage on a real actor: {v["melee"]:F2} / {ti["melee"]:F2} / {no["melee"]:F2}.");
        Check(Mathf.Abs(ti["force"] / v["force"] - 1.25f) < .01f, $"Melee knockback force dealt: {v["force"]:F0} / {ti["force"]:F0} N.s.");
        Check(Mathf.Abs(no["cooldown"] / v["cooldown"] - .8f) < .01f && Mathf.Abs(ti["cooldown"] - v["cooldown"]) < .001f, $"Ice cooldown after a cast: {v["cooldown"]:F3} / {ti["cooldown"]:F3} / {no["cooldown"]:F3} s.");
        Check(v["ice"] > 0f && Mathf.Abs(no["ice"] - v["ice"]) < .01f && Mathf.Abs(ti["ice"] - v["ice"]) < .01f, $"CONTROL: shipping PowerDamage is 1 for all three (same, non-zero Ice damage {v["ice"]:F2}).");
        Check(v["pushed"] > 1.5f && ti["pushed"] < v["pushed"] * (1f - t.KnockbackResistance) + .3f, $"Incoming knockback from a live Brute slam: VECTOR {v["pushed"]:F2} m, TITAN {ti["pushed"]:F2} m (resist {t.KnockbackResistance}).");
        Check(measured["verification-stats"]["ice"] > v["ice"] * 1.49f && measured["verification-stats"]["ice"] < v["ice"] * 1.51f, $"PowerDamage x1.5 (in-memory hero): Ice {measured["verification-stats"]["ice"]:F2} vs VECTOR {v["ice"]:F2}.");
        Log("LIMIT: movement uses SuperHeroController.ScriptedMove (batch mode has no keyboard) through the same Update path as the axes; no human feel test; HUD bar fractions compile-checked only.");
    }
    IEnumerator ForgeBars(HeroDefinition vector, HeroDefinition titan, HeroDefinition nova)
    {
        var menu = FindAnyObjectByType<ModeScreens>();
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = menu.ForgeButton; menu.ForgeButton.SendEvent(e); }
        yield return null;
        var screen = menu.ForgeScreen; Check(screen != null && screen.StatsPanel != null, "Hero Forge shows an archetype stats panel beside the preview.");
        foreach (var hero in new[] { vector, titan, nova })
        {
            Check(screen.SelectHero(hero), "Select " + hero.DisplayName);
            yield return null;
            foreach (var key in HeroForgeScreen.StatKeys)
            {
                var value = screen.StatsPanel.Q<Label>("forge-stat-" + key + "-value"); var fill = screen.StatsPanel.Q<VisualElement>("forge-stat-" + key + "-fill");
                Check(value != null && value.text == HeroForgeScreen.StatText(hero, key) && fill != null && Mathf.Abs(fill.style.width.value.value - screen.StatFill(hero, key) * 100f) < .01f,
                    $"{hero.DisplayName} {key}: \"{value?.text}\" bar {screen.StatFill(hero, key) * 100f:F0}%.");
            }
        }
        screen.SelectHero(titan); Check(screen.StatsPanel.Q<Label>("forge-stat-health-value").text == "140 HP", "TITAN health bar reads 140 HP (1.4 x 100).");
        screen.SelectHero(nova); Check(screen.StatsPanel.Q<Label>("forge-stat-cooldown-value").text == "x0.80", "NOVA cooldown bar reads x0.80.");
        screen.Close();
    }
    IEnumerator Measure(HeroDefinition hero)
    {
        Log("---- " + hero.DisplayName);
        var m = measured[hero.Id] = new Dictionary<string, float>();
        yield return Enter(hero, "strength", "ice");
        Check(W.Powers.HeroDefinition == hero, "Session hero is " + hero.DisplayName);
        m["health"] = W.Health; m["energy"] = W.Powers.Energy;
        Check(Mathf.Approximately(W.Health, W.MaxHealth) && Mathf.Approximately(W.Powers.Energy, W.Powers.MaxEnergy), $"Starts at its own maxima: {W.Health} HP / {W.Powers.Energy} energy.");
        Isolate(); var actor = Actor(new Vector3(0, 150, 2.2f), NpcRole.Criminal, 1000); var far = Actor(new Vector3(0, 150, 12), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        // Melee: one paid punch.
        W.Hero.DebugSetResources(6, 3, 0); float hp = actor.Health;
        Check(W.Hero.TryPunch(), "Punch."); yield return new WaitForSeconds(.6f);
        m["melee"] = hp - actor.Health; m["force"] = W.Hero.LastForce;
        // The melee actor stands on the crosshair line to `far` (integration #1: Ice hit it and every Ice sample read 0).
        // Take it out of the scene before any Ice cast, and prove the line to `far` is clear.
        actor.gameObject.SetActive(false); Physics.SyncTransforms();
        // Power: Ice on the far actor twice (damage + cooldown), which also spends 2 x 10 energy for the regen measurement.
        var ice = Runtime("ice"); Check(W.Powers.Select(ice), "Select Ice.");
        Aim(Chest(far));
        Check(W.Powers.FindTarget(W.Powers.Stats(ice).Range, out var aimed) && aimed.collider.GetComponentInParent<CityNpc>() == far, "Crosshair line reaches the far actor (no occluder).");
        hp = far.Health; Check(W.Powers.Use(ice), "Ice cast."); m["ice"] = hp - far.Health; m["cooldown"] = ice.Cooldown;
        float iceData = ice.Definition.GetStats(Mathf.Max(0, W.Progression.Tier(ice.Definition))).Damage * hero.Stats.PowerDamage;
        Check(m["ice"] > 0f && Mathf.Abs(m["ice"] - iceData) < .01f, $"Measured Ice damage {m["ice"]:F2} == data {iceData:F2} (damage x PowerDamage {hero.Stats.PowerDamage}).");
        yield return new WaitForSeconds(ice.Cooldown + .05f); Check(W.Powers.Use(ice), "Second Ice cast (energy for the regen sample).");
        // Regen: controller running (PowerUser.Tick), standing still on the floor.
        PlaceHero(W.Hero.transform.position, true); yield return null;
        // 0.5 s sample: long enough to measure, short enough that NOVA (140 max, 16.8/s) cannot reach its cap after two casts.
        float e0 = W.Powers.Energy, t0 = Time.time; yield return new WaitForSeconds(.5f);
        float gained = W.Powers.Energy - e0, dt = Time.time - t0;
        Check(W.Powers.Energy < W.Powers.MaxEnergy && gained > 0f, $"Regen sample never touched the cap ({e0:F1} -> {W.Powers.Energy:F1} of {W.Powers.MaxEnergy}).");
        m["regen"] = gained / dt;
        // Run speed: real Update movement with a scripted input, on the open floor.
        W.Hero.ScriptedMove = Vector3.right; W.Hero.ScriptedRun = true; yield return new WaitForSeconds(.3f);
        Vector3 p0 = W.Hero.transform.position; t0 = Time.time; yield return new WaitForSeconds(1f);
        Vector3 d = W.Hero.transform.position - p0; d.y = 0; m["speed"] = d.magnitude / (Time.time - t0);
        W.Hero.ScriptedMove = Vector3.zero; W.Hero.ScriptedRun = false;
        // Health in play: one 90-damage hit.
        W.DamagePlayer(90); m["survived"] = W.PlayerDead ? 0 : 1;
        Log($"MEASURED {hero.DisplayName}: health {m["health"]}, energy {m["energy"]}, regen {m["regen"]:F2}/s, run {m["speed"]:F2} m/s, punch {m["melee"]:F2} dmg @ {m["force"]:F0} N.s, Ice {m["ice"]:F2} dmg / {m["cooldown"]:F3} s cooldown, survives 90: {m["survived"] == 1}.");
    }
    IEnumerator Knockback(HeroDefinition hero)
    {
        Log("---- knockback " + hero.DisplayName);
        yield return Enter(hero, "strength", "ice");
        PlaceHero(W.City.Spawn + Vector3.up * .05f); Cam.GetComponent<ThirdPersonCamera>().enabled = false;
        Vector3 at = W.Hero.transform.position; CityNpc brute = null;
        for (int a = 0; a < 360 && brute == null; a += 30)
            if (NavMesh.SamplePosition(at + Quaternion.Euler(0, a, 0) * Vector3.forward * 3.5f, out var hit, 1f, NavMesh.AllAreas))
                brute = CityNpc.Spawn(W, hit.position, NpcRole.Criminal, EnemyRoster.Current.PursuingHero);
        Check(brute != null && brute.Archetype.Knockback > 0, $"Live Brute (knockback {brute?.Archetype.Knockback} m) placed next to the hero on the NavMesh.");
        brute.SetCombatStats(1000, 5);
        // Attacked fires at every release, just before Hits++ and the knockback of a landed slam: the position recorded at the
        // release that raised Hits is where the push started.
        Vector3 released = Vector3.zero; System.Action onAttack = () => released = W.Hero.transform.position;
        brute.Attacked += onAttack;
        float until = Time.time + 12; int hits = brute.Hits;
        while (brute.Hits == hits && Time.time < until) yield return null;
        brute.Attacked -= onAttack;
        Check(brute.Hits > hits, "The Brute's slam landed on the player.");
        yield return new WaitForSeconds(brute.Archetype.KnockbackSeconds + .25f);
        Vector3 push = W.Hero.transform.position - released; push.y = 0;
        measured[hero.Id]["pushed"] = push.magnitude;
        Log($"MEASURED {hero.DisplayName} knockback: {push.magnitude:F2} m (data {brute.Archetype.Knockback} m x (1 - {hero.Stats.KnockbackResistance})).");
    }
    IEnumerator PowerDamage(HeroDefinition vector)
    {
        Log("---- PowerDamage (in-memory test hero)");
        yield return Home();
        var test = Instantiate(vector); test.name = "verification-stats"; test.Id = "verification-stats"; test.DisplayName = "STATS TEST";
        test.Stats = new HeroStats { PowerDamage = 1.5f };
        registeredCatalog = F; originalHeroes = F.Heroes; F.Heroes = F.Heroes.Concat(new[] { test }).ToArray();
        yield return Enter(test, "strength", "ice");
        RestoreCatalog();
        Check(W.Powers.HeroDefinition == test && !F.Heroes.Contains(test), "Test hero drives the session; catalog already restored (in memory only, never saved).");
        var m = measured[test.Id] = new Dictionary<string, float>();
        Isolate(); var far = Actor(new Vector3(0, 150, 12), NpcRole.Criminal, 1000); yield return new WaitForSeconds(.3f);
        var ice = Runtime("ice"); W.Powers.Select(ice); Aim(Chest(far)); float hp = far.Health;
        Check(W.Powers.Use(ice), "Ice cast by the x1.5 PowerDamage hero."); m["ice"] = hp - far.Health;
        float data = ice.Definition.GetStats(Mathf.Max(0, W.Progression.Tier(ice.Definition))).Damage * 1.5f;
        Check(Mathf.Abs(m["ice"] - data) < .01f, $"x1.5 hero's measured Ice damage {m["ice"]:F2} == data x1.5 = {data:F2}.");
    }
}
#endif
