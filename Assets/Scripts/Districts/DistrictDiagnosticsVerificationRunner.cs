#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// See DistrictDiagnosticsVerification. OFF by default (no component without the toggle / flag); attached by hand it samples
/// on its interval, never per frame; time, hits, civilian outcomes, player damage / defeats, Heat, XP and a mission outcome
/// land in the district where they happened; the CSV / JSON files round-trip the in-memory totals; attaching twice does not
/// double count; the session-end auto write produces the files.
public sealed class DistrictDiagnosticsVerificationRunner : StagedMissionRunner
{
    protected override string Folder => "Verification/DistrictDiagnostics/";
    protected override string ResultFile => "results.txt";
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Session("fire", "ice", "villain");
        if (DistrictDiagnostics.Requested() && !DistrictDiagnostics.Enabled) Log("SKIP default-off control: the process was started with " + DistrictDiagnostics.CommandLineFlag + ".");
        else Check(W.GetComponent<DistrictDiagnostics>() == null, "CONTROL: diagnostics are OFF by default - the session has no DistrictDiagnostics component.");
        var diag = DistrictDiagnostics.Attach(W);
        diag.SampleSeconds = .25f; diag.PropSampleSeconds = .5f; diag.RendererSampleSeconds = 1f; diag.BucketSeconds = 2f; diag.AutoWrite = false;
        Check(DistrictDiagnostics.Attach(W) == diag && W.GetComponents<DistrictDiagnostics>().Length == 1, "Attach twice = one component (no double subscription).");
        int a = W.City.DistrictAt(W.City.Spawn), b = Enumerable.Range(0, W.City.DistrictDefinitions.Count).First(i => i != a && W.City.SidewalkDistrict.Contains(i));
        Vector3 inA = SidewalkIn(a), inB = SidewalkIn(b);
        Log($"District A = {a} ({W.City.DistrictDefinitions[a].Name}), B = {b} ({W.City.DistrictDefinitions[b].Name}); sample {diag.SampleSeconds} s, props {diag.PropSampleSeconds} s, renderers {diag.RendererSampleSeconds} s, buckets {diag.BucketSeconds} s.");

        Log("---- TIME AND SAMPLING");
        Ground(inA); float start = Time.time; int samples0 = diag.TotalSamples, frames0 = diag.FramesObserved;
        yield return new WaitForSeconds(1.5f);
        Ground(inB);
        yield return new WaitForSeconds(1.5f);
        diag.Sample();
        float elapsed = Time.time - start; int samples = diag.TotalSamples - samples0, frames = diag.FramesObserved - frames0;
        var ta = diag.TotalsFor(a); var tb = diag.TotalsFor(b);
        Check(ta.Seconds >= 1f && ta.Seconds <= 2.1f && tb.Seconds >= 1f && tb.Seconds <= 2.1f, $"Player time: A {ta.Seconds:F2} s, B {tb.Seconds:F2} s after ~1.5 s in each (sample-interval resolution).");
        Check(tb.Entries >= 1, $"Crossing into B counted as an entry ({tb.Entries}).");
        Check(samples <= elapsed / diag.SampleSeconds + 3 && samples >= 4, $"Sampled {samples} times in {elapsed:F2} s over {frames} frames: on the {diag.SampleSeconds} s interval, not every frame.");
        Check(diag.PropSamples >= 2 && diag.RendererSamples >= 2, $"Props sampled {diag.PropSamples} times, renderers {diag.RendererSamples} times (scene scans on their own slower intervals).");
        int rendererTotal = Enumerable.Range(-1, W.City.DistrictDefinitions.Count + 1).Sum(d => diag.TotalsFor(d).RenderersLast);
        Check(rendererTotal > 0, $"Renderer snapshot: {rendererTotal} enabled renderers bucketed by district.");
        Check(ta.Samples == tb.Samples && ta.Samples == diag.TotalSamples, $"Every district is sampled together ({ta.Samples} samples each), so averages share a denominator.");
        int civilians = W.Npcs.Count(n => n != null && !n.Dead && n.gameObject.activeInHierarchy && n.Role == NpcRole.Civilian);
        Check(Enumerable.Range(-1, W.City.DistrictDefinitions.Count + 1).Sum(d => diag.TotalsFor(d).CiviliansMax) >= Mathf.Min(1, civilians), $"Civilian population recorded ({civilians} alive now).");

        Log("---- EVENTS LAND IN THEIR DISTRICT");
        var before = Snapshot(diag, a, b);
        var civ = CityNpc.Spawn(W, inB, NpcRole.Civilian); Check(civ != null, "Test civilian spawned on a B sidewalk.");
        civ.Damage(1f, W.Powers);
        W.Progression.AddXp(7, inA, "diagnostics-test");
        float heat = W.Heat; W.AddHeat(.5f); float gained = W.Heat - heat;
        W.DamagePlayer(1f, true);
        var after = Snapshot(diag, a, b);
        Check(after.bHits - before.bHits == 1 && after.aHits == before.aHits, $"Player hit on a B civilian: B hits +{after.bHits - before.bHits}, A +{after.aHits - before.aHits}.");
        Check(after.bCivPlayer - before.bCivPlayer == 1, "The civilian ledger's HarmedByPlayer outcome is attributed to B once.");
        Check(after.aXp - before.aXp == 7 && after.bXp == before.bXp, "XP granted at a position in A is counted in A (7), not where the player stands.");
        Check(Mathf.Abs(after.bHeat - before.bHeat - gained) < 1e-4f && gained > 0f, $"Heat gained in B: +{gained:F2}.");
        Check(after.bDamage - before.bDamage == 1 && after.bDefeats == before.bDefeats, "One player-damage event in B, no defeat.");
        civ.Damage(1f, W.Powers);
        Check(Snapshot(diag, a, b).bCivPlayer == after.bCivPlayer, "CONTROL: harming the same civilian again does not count a second outcome.");

        Log("---- MISSION STAGE AND OUTCOME");
        var e = Spawn(Definition("Diagnostics survive", Staged(new MissionStageSpec { Kind = StageKind.Survive, Label = "DIAG WAIT", Seconds = .6f })));
        int site = W.City.DistrictAt(e.Site); var siteBefore = diag.TotalsFor(site); int successes = siteBefore.Successes;
        diag.Sample();
        Check(diag.TotalsFor(site).StageSamples.Keys.Any(k => k.StartsWith("Survive")), $"The active stage is sampled in the site district {site}: {string.Join(";", diag.TotalsFor(site).StageSamples.Select(p => p.Key + ":" + p.Value))}.");
        Check(diag.TotalsFor(site).EncountersSpawned >= 1, "The spawn is counted in the site district.");
        yield return Outcome(e, 5f);
        Check(Ended(e) && Result(e).Success && diag.TotalsFor(site).Successes == successes + 1, $"Mission success counted once in district {site}.");

        Log("---- DEFEAT");
        W.DamagePlayer(W.Health + 1f, true);
        int here = W.City.DistrictAt(W.Hero.transform.position);
        Check(diag.TotalsFor(here).PlayerDefeats == 1, $"The player defeat is counted in district {here}.");
        yield return new WaitForSeconds(W.Tuning.Movement.RespawnDelay + .5f);

        Log("---- FILES");
        string outFolder = Folder + "out"; if (Directory.Exists(outFolder)) Directory.Delete(outFolder, true);
        var paths = diag.Write(outFolder, "run");
        Check(paths.All(File.Exists), "Wrote " + string.Join(", ", paths.Select(Path.GetFileName)) + ".");
        var bucketLines = File.ReadAllLines(paths[0]); var totalLines = File.ReadAllLines(paths[1]); int columns = DistrictDiagnostics.CsvHeader.Split(',').Length;
        Check(bucketLines[0] == DistrictDiagnostics.CsvHeader && totalLines[0] == DistrictDiagnostics.CsvHeader, $"Both CSVs carry the {columns}-column header.");
        Check(bucketLines.Skip(1).All(l => DistrictDiagnostics.SplitCsv(l).Length == columns) && totalLines.Skip(1).All(l => DistrictDiagnostics.SplitCsv(l).Length == columns), "Every row has exactly that many columns.");
        int bucketCount = bucketLines.Skip(1).Select(l => DistrictDiagnostics.SplitCsv(l)[0]).Distinct().Count();
        Check(bucketCount >= 2, $"{bucketLines.Length - 1} bucket rows over {bucketCount} time buckets of {diag.BucketSeconds} s.");
        var rowB = totalLines.Skip(1).Select(DistrictDiagnostics.SplitCsv).First(r => r[2] == b.ToString());
        int col(string name) => Array.IndexOf(DistrictDiagnostics.CsvHeader.Split(','), name);
        Check(rowB[col("player_hits")] == diag.TotalsFor(b).PlayerHits.ToString() && rowB[col("xp")] == diag.TotalsFor(b).Xp.ToString() && rowB[col("civ_harmed_player")] == diag.TotalsFor(b).CivilianOutcomes[(int)CivilianOutcome.HarmedByPlayer].ToString(),
            $"Totals CSV round-trips district B: hits {rowB[col("player_hits")]}, xp {rowB[col("xp")]}, harmed by player {rowB[col("civ_harmed_player")]}.");
        int sumHits = bucketLines.Skip(1).Select(DistrictDiagnostics.SplitCsv).Where(r => r[2] == b.ToString()).Sum(r => int.Parse(r[col("player_hits")]));
        Check(sumHits == diag.TotalsFor(b).PlayerHits, $"Bucket rows sum to the totals (B hits {sumHits}).");
        string json = File.ReadAllText(paths[2]);
        Check(json.Contains("\"districts\"") && json.Contains("\"samplingMilliseconds\"") && json.Count(c => c == '{') == json.Count(c => c == '}') && json.Count(c => c == '[') == json.Count(c => c == ']'),
            "JSON has the session header and a districts array (balanced).");
        Log($"MEASURED sampling cost: {diag.SamplingMilliseconds:F2} ms over {diag.TotalSamples} samples + {diag.RendererSamples} renderer scans ({diag.SamplingMilliseconds / Mathf.Max(1, diag.TotalSamples + diag.RendererSamples):F3} ms each); {diag.FramesObserved} frames observed.");

        Log("---- AUTO WRITE AT SESSION END");
        string autoFolder = Folder + "auto"; if (Directory.Exists(autoFolder)) Directory.Delete(autoFolder, true);
        diag.Folder = autoFolder; diag.AutoWrite = true;
        yield return Home();
        var auto = Directory.Exists(autoFolder) ? Directory.GetFiles(autoFolder) : new string[0];
        Check(auto.Count(f => f.EndsWith("-buckets.csv")) == 1 && auto.Count(f => f.EndsWith("-totals.csv")) == 1 && auto.Count(f => f.EndsWith(".json")) == 1, $"Leaving the session wrote one file set: {string.Join(", ", auto.Select(Path.GetFileName))}.");
        Log("LIMIT: sampling cost is measured in this editor run only; no build or long-session profile. Renderer counts are enabled renderers by bounds centre (isVisible depends on the cameras that rendered last frame).");
    }
    (int aHits, int bHits, int aXp, int bXp, float bHeat, int bDamage, int bDefeats, int bCivPlayer) Snapshot(DistrictDiagnostics d, int a, int b)
    {
        var ta = d.TotalsFor(a); var tb = d.TotalsFor(b);
        return (ta.PlayerHits, tb.PlayerHits, ta.Xp, tb.Xp, tb.HeatGained, tb.PlayerDamageEvents, tb.PlayerDefeats, tb.CivilianOutcomes[(int)CivilianOutcome.HarmedByPlayer]);
    }
    Vector3 SidewalkIn(int district) { for (int i = 0; i < W.City.Sidewalks.Count; i++) if (W.City.SidewalkDistrict[i] == district) return W.City.Sidewalks[i]; throw new Exception("No sidewalk in district " + district); }
}
#endif
