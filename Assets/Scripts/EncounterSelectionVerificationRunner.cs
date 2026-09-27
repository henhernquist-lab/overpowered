#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// See EncounterSelectionVerification. The selection rule on in-memory options (weights, zero weight, anti-repeat window and
/// its relaxation, difficulty bands, district filters and fallback, seeded determinism), then in real sessions: the shipping
/// modes have no Selection and still spawn Encounters[0] first (defaults unchanged), and an in-memory mode clone with a
/// seeded Selection spawns through GameModeSession.SpawnNext from its options only.
public sealed class EncounterSelectionVerificationRunner : StagedMissionRunner
{
    protected override string Folder => "Verification/EncounterSelection/";
    protected override string ResultFile => "results.txt";
    EncounterDefinition A, B, C, D;
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        A = Enc("A"); B = Enc("B"); C = Enc("C"); D = Enc("D");
        Weights(); AntiRepeat(); Bands(); Districts(); Determinism();
        yield return Integration();
    }
    static EncounterDefinition Enc(string name) { var e = ScriptableObject.CreateInstance<EncounterDefinition>(); e.name = e.DisplayName = name; return e; }
    static EncounterSelection.Option O(EncounterDefinition e, float w, int min = 0, int max = 999, params string[] districts) => new EncounterSelection.Option { Encounter = e, Weight = w, MinDifficulty = min, MaxDifficulty = max, Districts = districts };
    static EncounterSelection Selection(int window, bool seeded, int seed, params EncounterSelection.Option[] options)
    { var s = ScriptableObject.CreateInstance<EncounterSelection>(); s.Options = options; s.AntiRepeatWindow = window; s.Seeded = seeded; s.Seed = seed; return s; }
    static Dictionary<string, int> Count(EncounterSelectionState state, int picks, int difficulty = 0, string district = "Downtown")
    {
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < picks; i++) { var e = state.Pick(difficulty, district); string k = e != null ? e.name : "null"; counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1; }
        return counts;
    }
    static string Show(Dictionary<string, int> c) => string.Join(", ", c.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
    void Weights()
    {
        Log("---- WEIGHTS");
        var counts = Count(Selection(0, true, 7, O(A, 1), O(B, 3), O(C, 0)).Begin(), 10000);
        Log($"MEASURED 10000 seeded picks, weights A1 B3 C0: {Show(counts)}.");
        float a = counts.TryGetValue("A", out int na) ? na / 10000f : 0f;
        Check(Mathf.Abs(a - .25f) < .03f && !counts.ContainsKey("C") && !counts.ContainsKey("null"), $"Picks follow the weights (A {a:P1} vs 25%); a zero-weight option is never picked.");
        var none = Selection(0, true, 7, O(A, 0), O(null, 5)).Begin();
        Check(none.Pick(0, "Downtown") == null, "CONTROL: only zero-weight / empty options -> no pick (null), no exception.");
    }
    void AntiRepeat()
    {
        Log("---- ANTI-REPEAT");
        var state = Selection(2, true, 11, O(A, 1), O(B, 1), O(C, 1)).Begin(); int violations = 0;
        var h = state.History;
        for (int i = 0; i < 1000; i++) { var e = state.Pick(0, "Park"); int n = h.Count; if ((n >= 2 && h[n - 2] == e) || (n >= 3 && h[n - 3] == e)) violations++; }
        Check(violations == 0, "Window 2 over 3 options: no encounter repeats within the last 2 picks in 1000 picks.");
        var control = Selection(0, true, 11, O(A, 1), O(B, 1), O(C, 1)).Begin(); int repeats = 0; EncounterDefinition last = null;
        for (int i = 0; i < 1000; i++) { var e = control.Pick(0, "Park"); if (e == last) repeats++; last = e; }
        Check(repeats > 100, $"CONTROL: window 0 repeats back-to-back ({repeats} of 1000).");
        var single = Count(Selection(3, true, 11, O(A, 1)).Begin(), 20);
        Check(single.Count == 1 && single["A"] == 20, "Window relaxes rather than stall when only one option is eligible (20/20 picks).");
    }
    void Bands()
    {
        Log("---- DIFFICULTY BANDS");
        var s = Selection(0, true, 5, O(A, 1, 0, 2), O(B, 1, 3), O(C, 1, 1, 4)); var state = s.Begin();
        for (int d = 0; d <= 6; d++)
        {
            var c = Count(state, 300, d);
            bool ok = (c.ContainsKey("A") == (d <= 2)) && (c.ContainsKey("B") == (d >= 3)) && (c.ContainsKey("C") == (d >= 1 && d <= 4));
            Check(ok, $"Band {d}: {Show(c)} (A 0-2, B 3+, C 1-4).");
        }
        s.DifficultyStep = 2; state = s.Begin();
        Check(state.Band(5) == 2 && Count(state, 200, 5).ContainsKey("A") && !Count(state, 200, 6).ContainsKey("A"), "DifficultyStep 2: value 5 -> band 2 (A eligible), value 6 -> band 3 (A not).");
    }
    void Districts()
    {
        Log("---- DISTRICTS");
        var s = Selection(0, true, 3, O(A, 1, 0, 999, "Docks"), O(B, 1)); var state = s.Begin();
        var docks = Count(state, 400, 0, "Docks"); var park = Count(state, 400, 0, "Park");
        Check(docks.ContainsKey("A") && docks.ContainsKey("B") && !park.ContainsKey("A") && park["B"] == 400, $"Docks-only option: Docks {Show(docks)}; Park {Show(park)}.");
        Check(Count(state, 50, 0, "docks").ContainsKey("A"), "District names match case-insensitively.");
        var only = Selection(0, true, 3, O(A, 1, 0, 999, "Docks")); only.DistrictFallback = true;
        Check(only.Begin().Pick(0, "Park") == A, "Fallback on: nothing allowed in Park -> district filter ignored for that pick.");
        only.DistrictFallback = false;
        Check(only.Begin().Pick(0, "Park") == null, "CONTROL: fallback off -> no pick in Park (this cycle spawns nothing).");
    }
    void Determinism()
    {
        Log("---- SEEDED");
        string Run(int seed) { var st = Selection(1, true, seed, O(A, 1), O(B, 2), O(C, 3, 0, 999, "Docks"), O(D, 1, 2)).Begin(); var names = new List<string>(); for (int i = 0; i < 200; i++) names.Add(st.Pick(i / 40, i % 3 == 0 ? "Docks" : "Park").name); return string.Join("", names); }
        string a = Run(99), b = Run(99), c = Run(100);
        Check(a == b, $"Same seed, same inputs -> identical 200-pick sequence ({a.Substring(0, 24)}...).");
        Check(a != c, "CONTROL: a different seed gives a different sequence.");
    }
    IEnumerator Integration()
    {
        Log("---- SESSIONS");
        var hero = Resources.Load<GameModeDefinition>("Modes/hero"); var villain = Resources.Load<GameModeDefinition>("Modes/villain");
        Check(hero.Selection == null && villain.Selection == null, "Shipping Hero / Villain modes have no Selection (round robin unchanged).");
        yield return Session("ice", "strength", "hero");
        var first = W.Crimes.Where(c => c != null && c.Encounter != null).Select(c => c.Encounter.Definition).FirstOrDefault();
        Check(W.Mode.Selection == null && first == hero.Encounters[0], $"Default hero session: first encounter is Encounters[0] ({first?.DisplayName}).");
        // In-memory clone of the hero mode with a seeded Selection whose only option is a staged-free original encounter.
        var bank = Resources.Load<EncounterDefinition>("Encounters/bank");
        var mode = Instantiate(hero); mode.Encounters = new EncounterDefinition[0];
        mode.Selection = Selection(1, true, 42, O(bank, 1));
        yield return Home();
        Check(Profile.SetLoadout(F.Heroes[0], Power("ice"), Power("strength"), F.Heroes[0].Primary, F.Heroes[0].Secondary), "Loadout saved.");
        GameFlow.Instance.Select(mode); yield return Scene(GameFlow.CityScene);
        var spawned = W.Crimes.Where(c => c != null && c.Encounter != null).Select(c => c.Encounter.Definition).ToList();
        Check(W.Mode.Selection != null && spawned.Count >= 1 && spawned.All(d => d == bank) && W.Mode.Selection.History.Count == spawned.Count,
            $"Mode with a Selection and an EMPTY Encounters list spawns from its options through SpawnNext ({spawned.Count} x {bank.DisplayName}; history {W.Mode.Selection.History.Count}).");
    }
}
#endif
