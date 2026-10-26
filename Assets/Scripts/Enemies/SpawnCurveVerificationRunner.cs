#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// See Assets/Editor/SpawnCurveVerification.cs. Proves the Enemy Spawn Director 2.0 rules against the SHIPPING data: the
/// two wave curves (Standard / Endless) at the rounds that matter, the director's deterministic weighted rotation over
/// those curves, the boss round budget, difficulty applied exactly once, and a live Endless run through the first boss
/// round. Free Play is checked to stay wave-free.
public sealed class SpawnCurveVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/SpawnCurve/";
    protected override string ResultFile => "results.txt";

    EndlessWaveEvents Events => Resources.Load<EndlessWaveEvents>("ModeDirectors/EndlessWaveEvents");
    EndlessWaveDirector Shipping => Resources.Load<EndlessWaveDirector>("ModeDirectors/EndlessWaves");
    EndlessWaveState State => W.Mode != null ? W.Mode.Director as EndlessWaveState : null;
    GameHud Hud => FindAnyObjectByType<GameHud>();

    static readonly int[] Rounds = { 1, 2, 3, 4, 5, 10, 15, 20, 25, 50, 100 };
    readonly float[] weights = new float[EnemyCompositionCurve.Slots];

    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Data();
        yield return Curves();
        yield return Rotation();
        yield return Plans();
        yield return Live();
        yield return FreePlay();
        Log("LIMIT: batch Play Mode proves the curves, the drawn mix, the boss budget, single difficulty application and round flow; encounter feel, pacing at human speed and sustained standalone frame rate remain unverified.");
    }

    // ------------------------------------------------------------------ A. shipping data
    IEnumerator Data()
    {
        Log("---- SHIPPING DATA");
        var events = Events;
        Check(events != null, "EndlessWaveEvents asset loads.");
        Check(Shipping != null && Shipping.Events == events, "The shipping Endless director runs the wave-events asset (minibosses and the composition curve are live).");
        Check(events.CurveKind == CompositionCurveKind.Endless, $"Shipping composition curve is {events.CurveKind}.");
        Check(events.MinibossEvery == 5 && events.MinibossArchetype != null && events.MinibossArchetype.DisplayName == "Pulsebreaker",
            $"Miniboss every {events.MinibossEvery} rounds: {events.MinibossArchetype?.DisplayName}.");
        Check(events.MinibossRegularCost == 3, $"A boss round trades {events.MinibossRegularCost} regular spawns for the boss.");
        Check(events.EliteFromWave > 50 && events.ModifierFromWave > 50,
            "SHIPPING SCOPE: elite and modifier waves stay off; the events asset is wired for the curve and the boss schedule.");
        Check(Shipping.Composition != null && Shipping.Composition.Length == 5, $"Legacy weight table kept as the fallback ({Shipping.Composition?.Length} entries).");

        var end = events.EndlessCurve; var std = events.StandardCurve;
        Check(End(end) && End(std), "Both curves reference all five regular archetypes; Pulsebreaker is never part of the pool.");
        var standard = events.StandardCurve;
        Check(standard.GunnerFromWave == 2 && standard.BruteFromWave == 3 && standard.SkirmisherFromWave == 3 && standard.BulwarkFromWave == 5,
            $"Introduction rounds preserved: Gunner {standard.GunnerFromWave}, Brute {standard.BruteFromWave}, Skirmisher {standard.SkirmisherFromWave}, Bulwark {standard.BulwarkFromWave}.");
        yield return null;
    }

    bool End(EnemyCompositionCurve curve) =>
        curve != null && curve.Rusher != null && curve.Gunner != null && curve.Brute != null && curve.Skirmisher != null && curve.Bulwark != null &&
        curve.Rusher.DisplayName == "Rusher" && curve.Gunner.DisplayName == "Gunner" && curve.Brute.DisplayName == "Brute" &&
        curve.Skirmisher.DisplayName == "Skirmisher" && curve.Bulwark.DisplayName == "Bulwark";

    // ------------------------------------------------------------------ B. curve maths
    IEnumerator Curves()
    {
        Log("---- CURVE MATHS");
        var events = Events;
        foreach (var kind in new[] { CompositionCurveKind.Standard, CompositionCurveKind.Endless })
            foreach (int wave in Rounds) Validate(events, kind, wave);

        // Published tables, exactly as specified.
        Table(events, CompositionCurveKind.Standard, 5, .32f, .29f, .19f, .12f, .08f);
        Table(events, CompositionCurveKind.Standard, 20, .25f, .26f, .19f, .17f, .13f);
        Table(events, CompositionCurveKind.Standard, 100, .25f, .26f, .19f, .17f, .13f);
        Table(events, CompositionCurveKind.Endless, 5, .32f, .28f, .20f, .12f, .08f);
        Table(events, CompositionCurveKind.Endless, 15, .23f, .28f, .20f, .17f, .12f);
        Table(events, CompositionCurveKind.Endless, 25, .14f, .28f, .20f, .22f, .16f);
        Table(events, CompositionCurveKind.Endless, 100, .14f, .28f, .20f, .22f, .16f);
        yield return null;
    }

    void Validate(EndlessWaveEvents events, CompositionCurveKind kind, int wave)
    {
        var curve = events.Curve(kind);
        Check(curve.Weights(kind, wave, weights), $"{kind} wave {wave}: weights exist.");
        float sum = 0f; bool finite = true; bool nonNegative = true; float worst = 0f;
        for (int slot = 0; slot < EnemyCompositionCurve.Slots; slot++)
        {
            float w = weights[slot];
            if (float.IsNaN(w) || float.IsInfinity(w)) finite = false;
            if (w < 0f) nonNegative = false;
            sum += w; worst = Mathf.Max(worst, w);
        }
        Check(finite && nonNegative, $"{kind} wave {wave}: every weight is finite and non-negative.");
        Check(Mathf.Abs(sum - 1f) <= 1e-4f, $"{kind} wave {wave}: weights sum to {sum:0.0000}.");
        for (int slot = 0; slot < EnemyCompositionCurve.Slots; slot++)
            if (!curve.Unlocked(slot, wave))
                Check(weights[slot] == 0f, $"{kind} wave {wave}: locked {curve.At(slot)?.DisplayName ?? "empty slot"} carries zero weight.");
        Check(weights[0] > 0f, $"{kind} wave {wave}: the Rusher never disappears (weight {weights[0]:0.000}).");
        if (curve.UnlockedCount(wave) > 1) Check(worst < .6f, $"{kind} wave {wave}: no single archetype dominates ({worst:0.000}).");
        if (wave <= 2) Check(!curve.Unlocked(3, wave) && !curve.Unlocked(4, wave) && !curve.Unlocked(2, wave), $"{kind} wave {wave}: teaching rounds stay Rusher/Gunner.");
        if (wave == 4) Check(!curve.Unlocked(4, wave), $"{kind} wave 4: the Bulwark has not joined yet.");
        if (wave >= 5) Check(curve.Unlocked(4, wave) && curve.Unlocked(3, wave) && curve.Unlocked(2, wave), $"{kind} wave {wave}: all five archetypes are eligible.");
    }

    void Table(EndlessWaveEvents events, CompositionCurveKind kind, int wave, float rusher, float gunner, float brute, float skirmisher, float bulwark)
    {
        var curve = events.Curve(kind);
        curve.Weights(kind, wave, weights);
        float drift = Mathf.Max(Mathf.Abs(weights[0] - rusher), Mathf.Max(Mathf.Abs(weights[1] - gunner), Mathf.Max(Mathf.Abs(weights[2] - brute), Mathf.Max(Mathf.Abs(weights[3] - skirmisher), Mathf.Abs(weights[4] - bulwark)))));
        Check(drift <= .002f, $"{kind} wave {wave}: {weights[0]:P0}/{weights[1]:P0}/{weights[2]:P0}/{weights[3]:P0}/{weights[4]:P0} (spec {rusher:P0}/{gunner:P0}/{brute:P0}/{skirmisher:P0}/{bulwark:P0}).");
    }

    // ------------------------------------------------------------------ C. the drawn mix
    IEnumerator Rotation()
    {
        Log("---- DRAWN MIX (deterministic weighted rotation over the shipping curve)");
        var director = Shipping; var events = Events;
        foreach (int wave in new[] { 5, 15, 25 })
        {
            const int count = 200;
            var seen = new Dictionary<EnemyArchetype, int>();
            var curve = events.Curve(events.CurveKind);
            curve.Weights(events.CurveKind, wave, weights);
            for (int i = 0; i < count; i++)
            {
                var archetype = director.ArchetypeFor(wave, i);
                Check(archetype != null, $"Endless wave {wave}: spawn {i} resolves an archetype.");
                seen[archetype] = seen.TryGetValue(archetype, out int n) ? n + 1 : 1;
            }
            for (int slot = 0; slot < EnemyCompositionCurve.Slots; slot++)
            {
                var archetype = curve.At(slot);
                if (!curve.Unlocked(slot, wave)) { Check(!seen.ContainsKey(archetype), $"Endless wave {wave}: locked {archetype.DisplayName} never drawn."); continue; }
                int drawn = seen.TryGetValue(archetype, out int n) ? n : 0;
                float expected = weights[slot] * count;
                Check(Mathf.Abs(drawn - expected) <= 3f, $"Endless wave {wave}: {archetype.DisplayName} drawn {drawn}/{count} ({drawn / (float)count:P0}, curve {weights[slot]:P0}, expected ~{expected:0}).");
            }
            Check(seen.Count == curve.UnlockedCount(wave), $"Endless wave {wave}: exactly the {curve.UnlockedCount(wave)} eligible archetypes appear ({seen.Count}).");
        }
        // Introduction rounds straight from the real spawn order.
        Only("wave 1", director.ArchetypeFor(1, 0), "Rusher", 1, 200);
        Only("wave 1", director.ArchetypeFor(1, 37), "Rusher", 1, 200);
        var wave2 = Distinct(director, 2, 120); var wave3 = Distinct(director, 3, 120); var wave4 = Distinct(director, 4, 160); var wave5 = Distinct(director, 5, 200);
        Check(wave2.SetEquals(new[] { "Rusher", "Gunner" }), $"Wave 2 pool: {Names(wave2)}.");
        Check(wave3.SetEquals(new[] { "Rusher", "Gunner", "Brute", "Skirmisher" }), $"Wave 3 pool: {Names(wave3)}.");
        Check(!wave4.Contains("Bulwark") && wave5.Contains("Bulwark"), $"Bulwark joins at wave 5 (wave 4: {Names(wave4)} / wave 5: {Names(wave5)}).");
        Check(wave5.Count == 5, $"Wave 5 fields all five archetypes: {Names(wave5)}.");

        // Legacy control: the weight table path is untouched.
        var legacy = Instantiate(events); legacy.CurveKind = CompositionCurveKind.WeightTable;
        var tableDirector = Instantiate(director); tableDirector.Events = legacy;
        var legacySet = Distinct(tableDirector, 5, 200);
        Check(legacySet.Count == 5, $"CONTROL: with CurveKind WeightTable the old Composition weights still field five archetypes ({Names(legacySet)}).");
        var legacyWave1 = Distinct(tableDirector, 1, 40);
        Check(legacyWave1.SetEquals(new[] { "Rusher" }), $"CONTROL: legacy wave 1 is still Rusher-only ({Names(legacyWave1)}).");
        Object.DestroyImmediate(legacy); Object.DestroyImmediate(tableDirector);
        yield return null;
    }

    void Only(string label, EnemyArchetype archetype, string expected, int wave, int count)
    {
        Check(archetype != null && archetype.DisplayName == expected, $"{label}: spawn resolves {expected} ({archetype?.DisplayName}).");
    }

    HashSet<string> Distinct(EndlessWaveDirector director, int wave, int count)
    {
        var set = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            var archetype = director.ArchetypeFor(wave, i);
            if (archetype != null) set.Add(archetype.DisplayName);
        }
        return set;
    }

    static string Names(HashSet<string> set) => string.Join(", ", set);

    // ------------------------------------------------------------------ D. plans and the boss budget
    IEnumerator Plans()
    {
        Log("---- PLANS AND BOSS BUDGET");
        var events = Events; var director = Shipping;
        for (int wave = 1; wave <= 30; wave++)
        {
            var plan = events.Plan(director, wave);
            bool expectedBoss = wave % events.MinibossEvery == 0;
            Check(plan.Miniboss == expectedBoss, $"Wave {wave}: miniboss {plan.Miniboss} (schedule every {events.MinibossEvery}).");
            int baseRegulars = director.WaveSize(wave);
            int expectedRegulars = expectedBoss ? Mathf.Max(1, baseRegulars - events.MinibossRegularCost) : baseRegulars;
            Check(plan.Regulars == expectedRegulars, $"Wave {wave}: {plan.Regulars} regulars (base {baseRegulars}{(expectedBoss ? $" - {events.MinibossRegularCost} for the boss" : "")}).");
            Check(plan.Size == plan.Regulars + (plan.Miniboss ? 1 : 0), $"Wave {wave}: size {plan.Size} = regulars + boss.");
            Check(plan.Elites == 0, $"Wave {wave}: no elites in shipping scope.");
            int bosses = 0; for (int i = 0; i < plan.Size; i++) if (events.SlotOf(plan, i) == EndlessWaveEvents.Slot.Miniboss) bosses++;
            Check(bosses == (expectedBoss ? 1 : 0), $"Wave {wave}: exactly {bosses} miniboss slot(s).");
            Check(events.CostOf(EndlessWaveEvents.Slot.Miniboss) <= director.MaxAlive, "A boss fits the alive budget.");
        }
        Check(events.Plan(director, 5).Miniboss && !events.Plan(director, 6).Miniboss && events.Plan(director, 10).Miniboss && events.Plan(director, 15).Miniboss,
            "Boss rounds 5, 10 and 15; round 6 has none.");
        yield return null;
    }

    // ------------------------------------------------------------------ E. live Endless run through the boss round
    IEnumerator Live()
    {
        Log("---- LIVE ENDLESS THROUGH ROUND 5 (shipping data, Extreme difficulty, fixed combat roll)");
        yield return Home();
        Check(Profile.SetDifficulty("extreme"), "Difficulty set to Extreme (isolated verification save).");
        Check(Profile.SetLoadout(F.Heroes[0], Power("ice"), Power("strength"), F.Heroes[0].Primary, F.Heroes[0].Secondary), "Loadout saved.");
        var shipping = Resources.Load<GameModeDefinition>("Modes/endless-fight");
        var mode = Instantiate(shipping);
        var director = Instantiate(Shipping); mode.Director = director;
        director.Events = Events;                     // the SHIPPING events asset, not a recipe
        director.IntermissionSeconds = .6f; director.MaxAlive = 8;
        director.FirstWaveCount = 3; director.EnemiesPerWave = 2;
        GameFlow.Instance.Select(mode);
        yield return Scene(GameFlow.CityScene);
        W.Rolls.FixedUnit = .5f;                      // exact spawn variance: health ratios are measurable
        float difficulty = W.Difficulty.EnemyHealth;
        Log($"Live fixture: Extreme enemy-health x{difficulty:F3}, MaxAlive {director.MaxAlive}, 3 + 2 per wave.");
        yield return null;

        int lastWave = 0, bossesSeen = 0; bool healthChecked = false;
        var mix = new HashSet<string>(); var waveMix = new HashSet<string>();
        for (float until = Time.time + 120f; Time.time < until && State.Wave < 6;)
        {
            if (State.Wave != lastWave)
            {
                if (lastWave > 0) Log($"Wave {lastWave} mix: {Names(waveMix)}");
                lastWave = State.Wave; waveMix.Clear();
                Check(State.Plan != null, $"Wave {State.Wave}: a plan exists.");
                if (State.Wave == 1) Check(!State.Plan.Miniboss && State.Plan.Size == director.WaveSize(1), $"Wave 1: {State.Plan.Size} regulars, no boss.");
                if (State.Wave == 5)
                {
                    Check(State.Plan.Miniboss, "Wave 5 is a boss round.");
                    Check(State.Plan.Regulars == Mathf.Max(1, director.WaveSize(5) - Events.MinibossRegularCost), $"Wave 5: {State.Plan.Regulars} regulars after the boss trade.");
                }
            }
            Check(State.Alive.Count <= director.MaxAlive, $"Alive budget holds ({State.Alive.Count}/{director.MaxAlive}).");
            for (int i = 0; i < State.Alive.Count; i++)
            {
                var npc = State.Alive[i]; if (npc == null || npc.Archetype == null) continue;
                var slot = State.AliveSlots[i];
                if (slot == EndlessWaveEvents.Slot.Miniboss)
                {
                    if (mix.Add("BOSS") && bossesSeen++ == 0)
                    {
                        Check(npc.IsBoss && npc.Boss != null, "The boss body carries the Pulsebreaker brain.");
                        Check(npc.Archetype == Events.MinibossArchetype, $"Boss archetype {npc.Archetype.DisplayName}.");
                    }
                    continue;
                }
                waveMix.Add(npc.Archetype.DisplayName); mix.Add(npc.Archetype.DisplayName);
                if (!healthChecked && State.Wave == 5)
                {
                    healthChecked = true;
                    float expected = director.HealthFor(5) * State.Plan.Health * npc.Archetype.HealthMultiplier * difficulty;
                    Check(Mathf.Abs(npc.MaxHealth - expected) <= .05f,
                        $"Difficulty applied exactly once: {npc.Archetype.DisplayName} at {npc.MaxHealth:F2} HP = HealthFor(5) {director.HealthFor(5):F2} x archetype {npc.Archetype.HealthMultiplier} x Extreme {difficulty:F3} (a doubled multiplier would be {expected * difficulty:F2}).");
                }
            }
            for (int i = State.Alive.Count - 1; i >= 0; i--) if (State.Alive[i] != null) State.Alive[i].Damage(99999f, W.Powers);
            yield return null;
        }
        Check(bossesSeen == 1, $"Exactly one Pulsebreaker in the boss round ({bossesSeen}).");
        Check(!mix.Contains("Bulwark") || State.Wave > 5, "The Bulwark does not appear before round 5.");
        Check(State.Wave >= 6, $"Round advanced after the boss died (round {State.Wave}).");
        Check(State.MinibossKills == 1, $"Miniboss kill counted once ({State.MinibossKills}).");
        Check(PulsebreakerBoss.Active == null, "The boss is gone after the kill.");
        Check(Hud == null || !Hud.BossBarVisible, "Boss bar hidden after the kill.");
        W.Rolls.FixedUnit = null;
    }

    // ------------------------------------------------------------------ F. Free Play stays wave-free
    IEnumerator FreePlay()
    {
        Log("---- FREE PLAY (no automatic waves)");
        yield return Home();
        yield return Enter(F.Heroes[0], "ice", "strength", "free-play");
        for (float until = Time.time + 2.5f; Time.time < until;)
        {
            Check(W.Mode.Definition.Director == null && State == null, "Free Play has no round director at any frame.");
            Check(PulsebreakerBoss.Active == null, "No boss is spawned automatically in Free Play.");
            yield return null;
        }
        Check(Hud == null || !Hud.BossBarVisible, "No boss bar in Free Play.");
        Check(W.Npcs.Count > 0, $"Ambient population preserved ({W.Npcs.Count} NPCs).");
    }
}
#endif
