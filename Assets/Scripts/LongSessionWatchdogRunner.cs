#if UNITY_EDITOR
// LONG SESSION WATCHDOG — gauntlet item 10. OPT-IN verifier: keeps one real session alive under accelerated,
// controlled simulation and samples counters over time so monotonic leaks become visible as slopes.
// No arbitrary thresholds: the report is a trend table (and a baseline/control comparison when OP_WATCHDOG_CONTROL
// names a baseline census.csv from a previous run). It fixes nothing and changes no gameplay.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class LongSessionWatchdogRunner : MonoBehaviour
{
    public float Minutes = 3f;              // accelerated session minutes (timeScale-elapsed)
    public float SampleEveryGameSeconds = 20f; // game-time seconds between census samples
    public float SimulationTimeScale = 5f;  // accelerated but still deterministic frame stepping
    public Action<int> Finished;
    GauntletVerificationSupport.Evidence ev;
    readonly List<Sample> samples = new List<Sample>();
    float sessionStart;

    sealed class Sample
    {
        public float GameSeconds, RealSeconds; public int Gos, Renderers, Materials, Particles, Audio, Npcs, Crimes, Announcements; public long Managed;
        public string Row() => string.Join(",", Mathf.RoundToInt(GameSeconds), Mathf.RoundToInt(RealSeconds), Gos, Renderers, Materials, Particles, Audio, Npcs, Crimes, Announcements, Managed);
        public static string Header() => "gameSeconds,realSeconds,gameObjects,renderers,materials,particles,audioSources,npcs,crimes,announcements,managedBytes";
    }

    IEnumerator Start()
    {
        var dir = GauntletVerificationSupport.NewEvidenceDir("watchdog");
        ev = new GauntletVerificationSupport.Evidence("watchdog", "WATCHDOG");
        ev.Line("save-sentinel armed: " + GauntletVerificationSupport.SaveSentinel.Arm());
        ev.Line("configuration minutes=" + Minutes.ToString("F1", CultureInfo.InvariantCulture)
            + " sampleEveryGameSeconds=" + SampleEveryGameSeconds + " simulationTimeScale=" + SimulationTimeScale);
        bool invalid = GauntletVerificationSupport.UnityContention.IsInvalid(ev);
        if (invalid) ev.Fail(GauntletVerificationSupport.UnityContention.InvalidLabel + " — all performance/leak samples below are labelled INVALID for comparison purposes (contention sentinel, item 4).");
        else ev.Pass("contention sentinel clean — samples are comparable with other uncontended runs.");

        WorldSession.VerificationSavePath = Path.GetFullPath(Path.Combine(dir, "save-watchdog.json"));
        GameFlow.VerificationSandbox = false;
        yield return GauntletFlow.OpenHome();
        var mode = Resources.LoadAll<GameModeDefinition>("Modes").FirstOrDefault(m => m.Id == "free-play");
        if (mode == null) { ev.Fail("WATCHDOG: free-play definition not found under Resources/Modes."); Terminate(1); yield break; }
        if (!GameFlow.Instance.Select(mode)) { ev.Fail("WATCHDOG: free-play refused selection."); Terminate(1); yield break; }
        yield return GauntletFlow.AwaitCity();
        var w = WorldSession.Instance;
        sessionStart = Time.time;
        ev.Pass("WATCHDOG: Free Play session live (no objectives, never ends; Heat stays active by design).");

        float startGame = Time.time;
        float nextSample = 0f;
        Time.timeScale = SimulationTimeScale;
        var baseline = GauntletVerificationSupport.SceneCensus.Take(w);
        ev.Sample(baseline.Row("t+0 baseline"));
        while (Time.time - startGame < Minutes * 60f)
        {
            if (Time.time >= nextSample)
            {
                nextSample = Time.time + SampleEveryGameSeconds;
                Take(w);
            }
            // Keep the hero safely idle in place (no gameplay changes; just avoids wandering off the city).
            if (Time.frameCount % 300 == 0) w.Hero.DebugSetResources(6f, 3);
            yield return null;
            if (WorldSession.Instance == null) { ev.Fail("WATCHDOG: WorldSession died mid-run (scene reload or crash)."); Terminate(1); yield break; }
        }
        Time.timeScale = 1f;
        Take(w);
        ev.Sample(GauntletVerificationSupport.SceneCensus.Take(w).Row("t+end"));

        AnalyzeTrends();
        GauntletVerificationSupport.SaveSentinel.Verify(ev);
        WriteCensus(dir);
        ev.Write("results.txt");
        ev.Line("watchdog complete: " + samples.Count + " samples over " + Minutes.ToString("F0", CultureInfo.InvariantCulture) + " game-minutes.");
        Terminate(0); // report-only: trend FAILs are informational and always exit 0
    }

    void Terminate(int code) => Finished?.Invoke(code);

    void Take(WorldSession w)
    {
        var c = GauntletVerificationSupport.SceneCensus.Take(w);
        int announcements = 0;
        var feedback = FindAnyObjectByType<GameHud>();
        if (feedback != null) announcements = feedback.BannersStarted + feedback.AlertsStarted + feedback.PopupsStarted;
        if (feedback != null) announcements += feedback.AlertsStarted;
        samples.Add(new Sample { GameSeconds = Time.time - sessionStart, RealSeconds = Time.realtimeSinceStartup, Gos = c.GameObjects, Renderers = c.Renderers, Materials = c.Materials, Particles = c.ParticleSystems, Audio = c.AudioSources, Npcs = c.Npcs, Crimes = c.Crimes, Announcements = announcements, Managed = c.ManagedBytes });
        ev.Sample(c.Row("t+" + Mathf.RoundToInt(Time.time - sessionStart) + "s game-seconds"));
    }

    /// Monotonicity analysis: for each counter, is the trend upward across the run? No thresholds — a counter
    /// grows or it does not. Growth in pooled/finite systems is EXPECTED to be flat; report says which grew.
    void AnalyzeTrends()
    {
        if (samples.Count < 3) { ev.Fail("WATCHDOG: too few samples (" + samples.Count + ") to establish a trend; raise Minutes or lower SampleEveryGameSeconds."); return; }
        var first = samples.First(); var last = samples.Last();
        // Regression slope per counter (per game-hour) — the report format, not a gate.
        double hours = Math.Max(1e-6, (last.GameSeconds - first.GameSeconds) / 3600.0);
        foreach (var (name, get) in new (string, Func<Sample, double>)[]
        {
            ("gameObjects", s => s.Gos), ("renderers", s => s.Renderers), ("materials", s => s.Materials),
            ("particles", s => s.Particles), ("audioSources", s => s.Audio), ("npcs", s => s.Npcs),
            ("crimes", s => s.Crimes), ("announcements", s => s.Announcements), ("managedBytes", s => s.Managed)
        })
        {
            double n = samples.Count, sx = 0, sy = 0, sxy = 0, sxx = 0;
            foreach (var s in samples)
            {
                double x = s.GameSeconds, y = get(s);
                sx += x; sy += y; sxy += x * y; sxx += x * x;
            }
            double slope = (n * sxy - sx * sy) / Math.Max(1e-9, n * sxx - sx * sx); // per game-second
            double perHour = slope * 3600.0;
            bool monotonicUp = last.GameSeconds > first.GameSeconds && get(last) > get(first) && slope > 0;
            string verdict = monotonicUp ? "GROWING" : "stable/flat";
            ev.Sample("TREND " + name + ": " + first + " -> " + last + "; slope " + perHour.ToString("F0", CultureInfo.InvariantCulture) + " per game-hour; verdict " + verdict + ".");
            if (monotonicUp && name != "managedBytes") // managed memory always drifts; only structural counters are suspicious
                ev.Fail("WATCHDOG LEAK CANDIDATE — " + name + " grew monotonically over the session (" + first + " -> " + last + ", ~" + perHour.ToString("F0", CultureInfo.InvariantCulture) + "/game-hour). Exact offender needs a baseline/control comparison (see docs/verification-gauntlet.md).");
        }
        // Compare against a baseline census if one was provided (no arbitrary thresholds — the control defines them).
        string baselinePath = Environment.GetEnvironmentVariable("OP_WATCHDOG_CONTROL");
        if (!string.IsNullOrEmpty(baselinePath) && File.Exists(baselinePath))
        {
            var baseRows = File.ReadAllLines(baselinePath).Skip(1).Select(l => l.Split(',')).Where(p => p.Length >= 11).ToList();
            if (baseRows.Count > 0)
            {
                var lastBase = baseRows.Last();
                ev.Pass("WATCHDOG CONTROL: baseline loaded from " + baselinePath + " (final row " + string.Join("/", lastBase.Skip(2).Take(6)) + "); compare TREND rows above against it manually.");
            }
        }
        else ev.Sample("WATCHDOG CONTROL: no baseline census provided (set OP_WATCHDOG_CONTROL to a previous run's census.csv); growth verdicts above are first-impression, not thresholds.");
    }

    void WriteCensus(string dir)
    {
        var lines = new List<string> { Sample.Header() };
        lines.AddRange(samples.Select(s => s.Row()));
        File.WriteAllLines(Path.Combine(dir, "census.csv"), lines);
    }
}

#endif
