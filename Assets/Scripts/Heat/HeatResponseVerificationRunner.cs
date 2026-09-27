#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// See HeatResponseVerification. Heat response tiers: no / neutral profile = the old police counts, arrival and decay
/// exactly; tiers switch on only at their thresholds and off again when Heat drops (removing elite / persistence from the
/// police they spawned); decay slows only while pursued; Endless explicit stats ignore Heat; hero police stay non-hostile.
public sealed class HeatResponseVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/HeatResponse/";
    protected override string ResultFile => "results.txt";
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Enter(F.Heroes[0], "fire", "ice", "villain");
        Isolate();   // 150 m above the street: Heat police spawn below and cannot reach or shoot the player during the counts
        OldBehaviour("no profile", null);
        OldBehaviour("neutral profile", Neutral());
        yield return Tiers();
        yield return Endless();
        yield return HeroSide();
    }
    static HeatResponseProfile Neutral()
    {
        var p = ScriptableObject.CreateInstance<HeatResponseProfile>(); p.Enabled = true;
        p.Tiers = new[] { new HeatResponseProfile.Tier { Label = "ORIGINAL", MinStars = 1 } };
        return p;
    }
    static HeatResponseProfile Tiered()
    {
        var p = ScriptableObject.CreateInstance<HeatResponseProfile>(); p.Enabled = true;
        p.Tiers = new[]
        {
            new HeatResponseProfile.Tier { Label = "RESPONSE", MinStars = 2, ResponseCountMultiplier = 1.5f, ArrivalIntervalMultiplier = .75f },
            new HeatResponseProfile.Tier { Label = "MANHUNT", MinStars = 4, ResponseCountMultiplier = 2f, ArrivalIntervalMultiplier = .5f, HostileArchetype = Resources.Load<EnemyArchetype>("Enemies/Brute"), ArchetypeShare = .5f,
                EliteHealthMultiplier = 3f, Persistent = true, PursuedDecayMultiplier = .5f, RoadblockEligible = true },
        };
        return p;
    }
    void SetStars(int stars) { W.AddHeat(-W.Heat); if (stars > 0) W.AddHeat(stars - .5f); }
    int Police() { W.ReconcilePolice(); return W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Cop && n.Encounter == null); }
    int Base(int stars, PlayerSide side) { var p = W.Tuning.Heat.Police(side); return p.PatrolCount + stars * p.CopsPerStar; }
    float Decay(float heat, float seconds) { W.AddHeat(-W.Heat); W.AddHeat(heat); float before = W.Heat; W.TickHeat(W.Tuning.Heat.DecayDelay); W.TickHeat(seconds); return before - W.Heat; }
    void OldBehaviour(string label, HeatResponseProfile profile)
    {
        Log($"---- {label.ToUpperInvariant()} = OLD BEHAVIOUR");
        W.Response.Use(profile);
        var counts = new List<string>(); bool same = true;
        for (int stars = 0; stars <= W.Tuning.Heat.MaximumStars; stars++) { SetStars(stars); int got = Police(), want = Base(W.Stars, PlayerSide.Villain); counts.Add($"{W.Stars}*:{got}"); same &= got == want; }
        Check(same, $"{label}: Heat police counts equal PatrolCount + stars x CopsPerStar at every star level ({string.Join(" ", counts)}).");
        Check(W.Response.ArrivalMultiplier == 1f && W.Response.CountMultiplier == 1f && W.Response.DecayMultiplier == 1f && W.Response.NextArchetype() == null, $"{label}: arrival, count and decay multipliers are exactly 1; no archetype override.");
        float dropped = Decay(3f, 2f);
        Check(Mathf.Abs(dropped - 2f * W.Tuning.Heat.DecayPerSecond) < 1e-4f, $"{label}: Heat decays {dropped:F4} over 2 s = DecayPerSecond x 2.");
        SetStars(0); W.ReconcilePolice();
    }
    IEnumerator Tiers()
    {
        Log("---- TIERS");
        var profile = Tiered(); W.Response.Use(profile);
        var roadblocks = new List<string>(); System.Action<HeatResponseProfile.Tier> onRoadblock = t => roadblocks.Add(t.Label); W.Response.RoadblockEligible += onRoadblock;
        SetStars(1); yield return null;
        Check(W.Response.Tier == null && Police() == Base(1, PlayerSide.Villain), "1 star: below every tier - the old count.");
        SetStars(2); yield return null;
        Check(W.Response.Tier?.Label == "RESPONSE" && Police() == Mathf.RoundToInt(Base(2, PlayerSide.Villain) * 1.5f) && W.Response.ArrivalMultiplier == .75f, $"2 stars (threshold): RESPONSE tier, count x1.5, arrival x0.75.");
        SetStars(3); yield return null;
        Check(W.Response.Tier?.Label == "RESPONSE", "3 stars: still RESPONSE (MANHUNT starts at 4).");
        int before = W.Npcs.Count;
        SetStars(4); yield return null;
        int manhunt = Police(); var fresh = W.Npcs.Skip(before).Where(n => n != null && n.Role == NpcRole.Cop && n.Encounter == null).ToList();
        Check(W.Response.Tier?.Label == "MANHUNT" && manhunt == Mathf.RoundToInt(Base(4, PlayerSide.Villain) * 2f) && roadblocks.SequenceEqual(new[] { "MANHUNT" }), $"4 stars: MANHUNT, count x2 ({manhunt}), roadblock hook raised once.");
        var brute = profile.Tiers[1].HostileArchetype;
        Check(fresh.Count > 1 && fresh.All(n => n.AlwaysAggro && n.GetComponent<HeatTierTag>() != null) && fresh.Count(n => n.Archetype == brute) == fresh.Count / 2,
            $"Police spawned at MANHUNT: all persistent, {fresh.Count(n => n.Archetype == brute)} of {fresh.Count} are {brute?.name} (share 0.5), elite health {fresh[0].MaxHealth:F0}.");
        var cop = fresh.First(n => n.Archetype != brute); float elite = cop.MaxHealth;
        var watcher = Actor(W.Hero.transform.position + new Vector3(0, 0, 6), NpcRole.Cop, 1000f); yield return null; W.Pursuit.Sample();
        Check(W.Pursuit.State == PursuitState.Pursued && W.Response.DecayMultiplier == .5f, "Pursued at MANHUNT: Heat decay x0.5.");
        watcher.gameObject.SetActive(false);
        SetStars(1); yield return null; yield return null;
        Check(W.Response.Tier == null && W.Response.DecayMultiplier == 1f && Police() == Base(1, PlayerSide.Villain), "Heat back to 1 star: no tier, decay and count back to the old values.");
        var survivors = fresh.Where(n => n != null && !n.Dead).ToList();
        Check(survivors.All(n => !n.AlwaysAggro && n.GetComponent<HeatTierTag>().Removed), $"Tier modifiers removed from the {survivors.Count} MANHUNT police still alive (persistence off).");
        if (cop != null) Check(Mathf.Abs(cop.MaxHealth - elite / 3f) < .5f, $"Elite health reverted ({elite:F0} -> {cop.MaxHealth:F0}).");
        W.Response.RoadblockEligible -= onRoadblock;
        W.Response.Use(null); SetStars(0); W.ReconcilePolice();
    }
    IEnumerator Endless()
    {
        Log("---- ENDLESS STATS IGNORE HEAT");
        yield return Enter(F.Heroes[0], "fire", "ice", "endless-fight");
        W.Response.Use(Tiered()); W.AddHeat(5f);
        var state = W.Mode.Director as EndlessWaveState; var d = (EndlessWaveDirector)W.Mode.Definition.Director;
        float until = Time.time + 20f; while ((state.Wave < 1 || state.Alive.Count == 0) && Time.time < until) yield return null;
        var npcs = state.Alive.Where(n => n != null).ToList();
        Check(npcs.Count > 0 && npcs.All(n => Mathf.Abs(n.MaxHealth - d.HealthFor(state.Wave) * (n.Archetype != null ? n.Archetype.HealthMultiplier : 1f)) < .05f && n.GetComponent<HeatTierTag>() == null),
            $"At {W.Stars} stars with MANHUNT active, Endless wave {state.Wave} enemies keep their explicit health ({npcs.FirstOrDefault()?.MaxHealth:F1}) and no tier tag.");
        Check(!W.Mode.Definition.SpawnPolice, "Endless spawns no Heat police at all (SpawnPolice off).");
    }
    IEnumerator HeroSide()
    {
        Log("---- HERO POLICE STAY NON-HOSTILE");
        yield return Enter(F.Heroes[0], "ice", "strength", "hero");
        var profile = Tiered(); W.Response.Use(profile);
        SetStars(4); yield return null;
        Check(W.Response.Tier == null && Police() == Base(W.Stars, PlayerSide.Hero), "ApplyToHero off: no tier for the hero, the old count.");
        profile.ApplyToHero = true; W.Response.Use(profile); yield return null;
        int count = Police(); var police = W.Npcs.Where(n => n != null && !n.Dead && n.Role == NpcRole.Cop && n.Encounter == null).ToList();
        Check(W.Response.Tier?.Label == "MANHUNT" && count == Mathf.RoundToInt(Base(W.Stars, PlayerSide.Hero) * 2f) && police.All(n => !n.Hostile && !W.PoliceHostileTo(n)),
            $"ApplyToHero on: MANHUNT changes the hero's patrol count ({count}) but every one of them stays non-hostile (persistent ones included).");
    }
}
#endif
