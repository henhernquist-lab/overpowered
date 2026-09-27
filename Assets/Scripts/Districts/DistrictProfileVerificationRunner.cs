#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// See DistrictProfileVerification. The district gameplay layer in a real Villain session: unset / disabled = the exact old
/// numbers; an in-memory profile that changes ONE value changes only that value and only in its district; crossing a
/// district boundary switches the active profile; gameplay code has no district-name literals; the same city seed gives
/// the same district mapping.
public sealed class DistrictProfileVerificationRunner : StagedMissionRunner
{
    protected override string Folder => "Verification/DistrictProfiles/";
    protected override string ResultFile => "results.txt";
    readonly List<XpGrant> grants = new List<XpGrant>();
    DistrictContext D => W.Districts;
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Session("fire", "ice", "villain");
        Isolate();
        W.Progression.XpGranted += grants.Add;
        int a = W.City.DistrictAt(W.City.Spawn), b = Enumerable.Range(0, D.Count).First(i => i != a && W.City.SidewalkDistrict.Contains(i));
        Vector3 inA = SidewalkIn(a), inB = SidewalkIn(b);
        Log($"District A = {a} ({D.NameOf(a)}), district B = {b} ({D.NameOf(b)}); {D.Count} districts.");
        Defaults(inA, inB);
        yield return OneValue(a, b, inA, inB);
        yield return Crossing(a, b, inA, inB);
        yield return DifficultyAndSelection(a, inA);
        NameScan();
        Determinism();
        D.Use(DistrictProfileSet.Current);
        W.Progression.XpGranted -= grants.Add;
    }
    Vector3 SidewalkIn(int district) { for (int i = 0; i < W.City.Sidewalks.Count; i++) if (W.City.SidewalkDistrict[i] == district) return W.City.Sidewalks[i]; throw new Exception("No sidewalk in district " + district); }
    DistrictProfileSet Map(int district, Action<DistrictGameplayProfile> set)
    {
        var p = ScriptableObject.CreateInstance<DistrictGameplayProfile>(); set(p);
        var s = ScriptableObject.CreateInstance<DistrictProfileSet>(); s.Enabled = true; s.Entries = new[] { new DistrictProfileSet.Entry { District = D.NameOf(district), Profile = p } };
        return s;
    }
    (float heat, int xp) Destruction(Vector3 at)
    {
        W.AddHeat(-W.Heat); grants.Clear(); W.OnDestruction(at);
        return (W.Heat, grants.Where(g => g.Reason == "destruction").Sum(g => g.Amount));
    }
    int Police()
    {
        W.ReconcilePolice();
        return W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Cop && n.Encounter == null);
    }
    void Defaults(Vector3 inA, Vector3 inB)
    {
        Log("---- UNSET / DISABLED = OLD BEHAVIOUR");
        var set = DistrictProfileSet.Current;
        Check(!D.Active && (set == null || !set.Enabled), $"No enabled profile set (asset {(set == null ? "absent" : "present, Enabled=false")}): the layer is inactive, so civilians spawned through the original loop.");
        Check(D.HeatFactorAt(inA) == 1f && D.DestructionFactorAt(inA) == 1f && D.DifficultyOffsetAt(inA) == 0 && D.PoliceFactor == 1f && D.PreferredArchetype(NpcRole.Cop, inA) == null, "Every hook value is neutral (1 / 0 / no archetype).");
        var t = W.Tuning; var d = Destruction(inA);
        Check(d.heat == t.Heat.DestructionHeat && d.xp == t.Progression.DestructionXp, $"Destruction: +{d.heat} Heat, +{d.xp} XP = the tuning values exactly ({t.Heat.DestructionHeat}, {t.Progression.DestructionXp}).");
        W.AddHeat(1.5f); var police = t.Heat.Police(PlayerSide.Villain); int expected = police.PatrolCount + W.Stars * police.CopsPerStar;
        Check(Police() == expected, $"Heat police: {expected} = PatrolCount + {W.Stars} stars x CopsPerStar.");
        W.AddHeat(-W.Heat); W.ReconcilePolice();
    }
    IEnumerator OneValue(int a, int b, Vector3 inA, Vector3 inB)
    {
        Log("---- ONE VALUE CHANGED, ONE DISTRICT");
        var t = W.Tuning;
        D.Use(Map(a, p => p.HeatResponse = 2f));
        Check(D.Active, "An in-memory profile with HeatResponse 2 for district A activates the layer.");
        var inside = Destruction(inA); var outside = Destruction(inB);
        Check(Mathf.Abs(inside.heat - 2f * t.Heat.DestructionHeat) < 1e-5f && inside.xp == t.Progression.DestructionXp, $"In A: Heat x2 ({inside.heat}), XP unchanged ({inside.xp}).");
        Check(outside.heat == t.Heat.DestructionHeat && outside.xp == t.Progression.DestructionXp, "CONTROL in B: Heat and XP unchanged.");
        Check(D.DestructionFactorAt(inA) == 1f && D.DifficultyOffsetAt(inA) == 0 && D.PreferredArchetype(NpcRole.Cop, inA) == null, "CONTROL: the other values of A stay neutral.");
        D.Use(Map(a, p => p.DestructionReward = 3f));
        inside = Destruction(inA);
        Check(inside.heat == t.Heat.DestructionHeat && inside.xp == Mathf.RoundToInt(t.Progression.DestructionXp * 3f), $"DestructionReward 3 in A: XP x3 ({inside.xp}), Heat unchanged.");
        var baseline = D.CivilianCounts(W.Mode.Definition.Civilians);
        D.Use(Map(a, p => p.CivilianDensity = 0f));
        var thinned = D.CivilianCounts(W.Mode.Definition.Civilians);
        Check(thinned[a] == 0 && Enumerable.Range(0, thinned.Length).Where(i => i != a).All(i => thinned[i] == baseline[i]), $"CivilianDensity 0 in A: A gets 0 civilians (was {baseline[a]}), every other district keeps its count.");
        D.Use(Map(a, p => p.PoliceResponse = 2f));
        PlaceHero(inA + Vector3.up * .05f); D.Refresh(); W.AddHeat(1.5f);
        var police = t.Heat.Police(PlayerSide.Villain); int normal = police.PatrolCount + W.Stars * police.CopsPerStar;
        Check(Police() == Mathf.RoundToInt(normal * 2f), $"PoliceResponse 2 while the player is in A: {normal * 2} police instead of {normal}.");
        PlaceHero(inB + Vector3.up * .05f); D.Refresh();
        Check(Police() == normal, "CONTROL: the player in B -> the normal count again.");
        W.AddHeat(-W.Heat); W.ReconcilePolice();
        var cop = EnemyRoster.Current.Cop; var old = cop.Tags;
        try
        {
            cop.Tags = new[] { "verify-tag" };
            D.Use(Map(a, p => p.EnemyTags = new[] { "verify-tag" }));
            Check(D.PreferredArchetype(NpcRole.Cop, inA) == cop && D.PreferredArchetype(NpcRole.Cop, inB) == null, $"EnemyTags: A prefers the tagged archetype ({cop.name}); B has no preference.");
        }
        finally { cop.Tags = old; }
        yield return null;
    }
    IEnumerator Crossing(int a, int b, Vector3 inA, Vector3 inB)
    {
        Log("---- CROSSING A BOUNDARY");
        var pa = ScriptableObject.CreateInstance<DistrictGameplayProfile>(); pa.HeatResponse = 1.5f; var pb = ScriptableObject.CreateInstance<DistrictGameplayProfile>(); pb.PoliceResponse = .5f;
        var set = ScriptableObject.CreateInstance<DistrictProfileSet>(); set.Enabled = true;
        set.Entries = new[] { new DistrictProfileSet.Entry { District = D.NameOf(a), Profile = pa }, new DistrictProfileSet.Entry { District = D.NameOf(b), Profile = pb } };
        D.Use(set);
        var changes = new List<(int, int)>(); Action<int, int> on = (x, y) => changes.Add((x, y)); D.DistrictChanged += on;
        PlaceHero(inA + Vector3.up * .05f); D.Refresh(); changes.Clear();
        Check(D.PlayerDistrict == a && D.PlayerProfile == pa, $"In A: active profile A (HeatResponse {D.PlayerProfile.HeatResponse}).");
        PlaceHero(inB + Vector3.up * .05f); yield return new WaitForSeconds(.35f);
        Check(D.PlayerDistrict == b && D.PlayerProfile == pb && changes.Count == 1 && changes[0] == (a, b), $"Walking into B switches the active profile within 0.25 s (one change event {a} -> {b}).");
        yield return new WaitForSeconds(.35f);
        Check(changes.Count == 1, "CONTROL: staying in B raises no further event.");
        D.DistrictChanged -= on;
    }
    IEnumerator DifficultyAndSelection(int a, Vector3 inA)
    {
        Log("---- DIFFICULTY OFFSET AND TAGGED SELECTION");
        D.Use(Map(a, p => { p.DifficultyOffset = 2; p.MissionTags = new[] { "cargo" }; p.CategoryWeights = new[] { new DistrictGameplayProfile.CategoryWeight { Category = "heist", Weight = 3f } }; }));
        int siteIndex = Enumerable.Range(0, W.City.EncounterSites.Count).First(i => W.City.EncounterSiteDistrict[i] == a);
        var scenario = Staged(new MissionStageSpec { Kind = StageKind.Survive, Label = "WAIT", Seconds = .3f }); scenario.DifficultyStep = 1;
        var crime = W.SpawnEncounter(Definition("Verification district band", scenario), W.City.EncounterSites[siteIndex]);
        var s = (StagedState)crime.Encounter.Scenario;
        Check(s.Band == W.Mode.Successes + 2, $"A staged mission placed in A gets band {s.Band} = {W.Mode.Successes} successes + DifficultyOffset 2.");
        var e1 = Definition("tagged", null); var e2 = Definition("plain", null); var e3 = Definition("heist", null);
        var options = new[]
        {
            new EncounterSelection.Option { Encounter = e1, Weight = 1f, Tags = new[] { "cargo" } },
            new EncounterSelection.Option { Encounter = e2, Weight = 1f },
            new EncounterSelection.Option { Encounter = e3, Weight = 1f, Category = "heist" },
        };
        var scratch = new List<int>(); var none = new List<EncounterDefinition>(); var rng = new System.Random(3);
        int[] withA = new int[3], withNeutral = new int[3], withNull = new int[3];
        var neutralTags = ScriptableObject.CreateInstance<DistrictGameplayProfile>(); neutralTags.MissionTags = new[] { "homes" };
        for (int i = 0; i < 6000; i++)
        {
            withA[EncounterSelection.Choose(options, 0, "", none, 0, false, rng.NextDouble(), scratch, D.ProfileOf(a))]++;
            withNeutral[EncounterSelection.Choose(options, 0, "", none, 0, false, rng.NextDouble(), scratch, neutralTags)]++;
            withNull[EncounterSelection.Choose(options, 0, "", none, 0, false, rng.NextDouble(), scratch)]++;
        }
        Log($"MEASURED 6000 picks each: A (tags cargo, heist x3) {string.Join("/", withA)}; homes-only profile {string.Join("/", withNeutral)}; no profile {string.Join("/", withNull)}.");
        Check(Mathf.Abs(withA[2] / 6000f - .6f) < .03f && withA[0] > 0, "In A the cargo-tagged option is eligible and the heist category weighs x3 (3 / 5 of picks).");
        Check(withNeutral[0] == 0 && withNull[0] > 0 && Mathf.Abs(withNull[2] / 6000f - 1f / 3f) < .03f, "CONTROL: a profile without the tag excludes the tagged option; no profile = the old rule (tags and categories ignored).");
        yield return Outcome(crime.Encounter, 3f);
    }
    /// Gameplay code (not verifiers, recipe libraries or editor setup) must not contain a district name as a string literal.
    void NameScan()
    {
        Log("---- NO DISTRICT-NAME BRANCHES");
        var names = new HashSet<string>(W.City.DistrictDefinitions.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
        var literal = new Regex("\"([^\"\\\\]*)\""); var hits = new List<string>(); int files = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string file = Path.GetFileName(path);
            // Excluded: verifiers, recipe libraries, and the layout / art authorities that DEFINE the districts (CityLayout's
            // default district list, CityArtSettings' landmark styles) - data, not gameplay branches.
            if (file.Contains("Verification") || file.Contains("Runner") || file.Contains("Library") || file.Contains("Benchmark") || file.StartsWith("CityLayout") || file.StartsWith("CityArt")) continue;
            files++; int line = 0;
            foreach (var text in File.ReadLines(path)) { line++; foreach (Match m in literal.Matches(text)) if (names.Contains(m.Groups[1].Value)) hits.Add($"{file}:{line} \"{m.Groups[1].Value}\""); }
        }
        Check(hits.Count == 0, $"{files} gameplay files scanned for the {names.Count} district names as literals: {(hits.Count == 0 ? "none" : string.Join("; ", hits))}.");
    }
    void Determinism()
    {
        Log("---- SAME SEED, SAME MAPPING");
        var layout = W.City.Layout; var config = W.Tuning.City;
        var p1 = layout.Plan(config); var p2 = layout.Plan(config); var live = W.City.Plan;
        bool Same(CityPlan x, CityPlan y) => x.Districts.Count == y.Districts.Count && x.Districts.Select(d => d.Name + d.Region).SequenceEqual(y.Districts.Select(d => d.Name + d.Region)) &&
            x.Sites.SequenceEqual(y.Sites) && x.SiteDistrict.SequenceEqual(y.SiteDistrict) && x.SidewalkDistrict.SequenceEqual(y.SidewalkDistrict);
        Check(Same(p1, p2) && Same(p1, live), $"Seed {config.Seed}: two fresh plans and the live city agree on {p1.Districts.Count} districts, {p1.Sites.Count} sites and {p1.SidewalkDistrict.Count} sidewalk districts.");
        var set = ScriptableObject.CreateInstance<DistrictProfileSet>(); set.Enabled = true;
        var profiles = p1.Districts.Select(d => { var p = ScriptableObject.CreateInstance<DistrictGameplayProfile>(); p.name = d.Name; p.HeatResponse = 2f; return p; }).ToArray();
        set.Entries = p1.Districts.Select((d, i) => new DistrictProfileSet.Entry { District = d.Name, Profile = profiles[i] }).ToArray();
        D.Use(set);
        Check(Enumerable.Range(0, p1.Sites.Count).All(i => D.ProfileAt(p1.Sites[i]) == profiles[p1.SiteDistrict[i]]), "Every site of the fresh plan resolves to the profile of its planned district.");
    }
}
#endif
