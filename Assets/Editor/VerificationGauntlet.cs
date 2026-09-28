#if UNITY_EDITOR
// VERIFICATION GAUNTLET — editor entry points (items 1, 3, 6, 7).
// Unity -batchmode -projectPath <project> -executeMethod VerificationGauntlet.Inventory -logFile <log>
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class VerificationGauntlet
{
    // Inventory + hard-code audit entries live in VerificationGauntletInventory (stable -executeMethod targets).

    /// Repeatability batch (item 3): defaults to 3 rounds of the 6 historically flaky suites.
    /// Repeat count configurable via OP_GAUNTLET_REPEATS (or Menu replay with a fixed count).
    public static void Repeatability()
    {
        int repeats = 3;
        string raw = Environment.GetEnvironmentVariable("OP_GAUNTLET_REPEATS");
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var parsed) && parsed > 0) repeats = parsed;
        var go = new GameObject("Verification gauntlet repeatability");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<RepeatabilityVerificationRunner>();
        runner.Repeats = repeats;
        EditorApplication.isPlaying = true;
    }
}

// ---------------------------------------------------------------------- repeatability runner (item 3)
/// Runs the historically flaky suites N times inside one Unity process (with save-sentinel checks per suite),
/// recording every round and the first failing round per suite. Deterministic tracking: every round logs its
/// index and (for future fixed-seed work) the project's Random seed state — the suites themselves are time-based.
public sealed class RepeatabilityVerificationRunner : MonoBehaviour
{
    public int Repeats = 3;
    GauntletVerificationSupport.Evidence ev;
    readonly GauntletVerificationSupport.BatchRecord record = new GauntletVerificationSupport.BatchRecord();

    readonly (string suite, Func<System.Collections.IEnumerator> body)[] FlakySuites =
    {
        ("HudPhase2",      () => RepeatabilitySuites.HudPhase2()),
        ("HudPhase3",      () => RepeatabilitySuites.HudPhase3()),
        ("Audio",          () => RepeatabilitySuites.Audio()),
        ("Sidekick",       () => RepeatabilitySuites.Sidekick()),
        ("Humanoid",       () => RepeatabilitySuites.Humanoid()),
        ("BackflipHurricane", () => RepeatabilitySuites.BackflipHurricane()),
    };

    System.Collections.IEnumerator Start()
    {
        var dir = GauntletVerificationSupport.NewEvidenceDir("repeatability");
        ev = new GauntletVerificationSupport.Evidence("repeatability", "REPEATABILITY");
        RepeatabilitySuites.Bind(ev);
        ev.Line("repeats " + Repeats);
        ev.Line("save-sentinel armed: " + GauntletVerificationSupport.SaveSentinel.Arm());
        record.Suite = "RepeatabilityGauntlet"; record.EvidencePath = dir;
        record.ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();

        foreach (var (suite, body) in FlakySuites)
        {
            int firstFailure = -1;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int round = 1; round <= Repeats; round++)
            {
                int passBefore = ev.PassCount, failBefore = ev.FailCount;
                ev.Line("ROUND " + suite + " #" + round + "/" + Repeats + " begin");
                var enumerator = body();
                bool again = true;
                while (again)
                {
                    try
                    {
                        if (!enumerator.MoveNext()) again = false;
                    }
                    catch (Exception ex) { ev.Fail(suite + " round " + round + " exception: " + ex.Message); again = false; }
                    if (again) yield return enumerator.Current;
                }
                int fails = ev.FailCount - failBefore;
                ev.Line("ROUND " + suite + " #" + round + " done: pass " + (ev.PassCount - passBefore) + " fail " + fails
                    + " seedFrameRandomState=" + UnityEngine.Random.state.GetHashCode());
                if (fails > 0 && firstFailure < 0) firstFailure = round;
            }
            sw.Stop();
            if (firstFailure < 0) ev.Pass(suite + ": " + Repeats + "/" + Repeats + " rounds clean.");
            else ev.Fail(suite + ": FIRST FAILING ROUND " + firstFailure + " of " + Repeats + ".");
            record.DurationSeconds += sw.Elapsed.TotalSeconds;
        }
        if (!GauntletVerificationSupport.SaveSentinel.Verify(ev)) record.FailCount++;
        record.PassCount = ev.PassCount; record.FailCount += ev.FailCount;
        WriteSummary(dir);
        ev.Write("results.txt");
        EditorApplication.Exit(record.FailCount == 0 ? 0 : 1);
    }

    void WriteSummary(string dir)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("repeatability gauntlet — " + Repeats + " rounds x 6 suites");
        sb.AppendLine("commit " + GauntletVerificationSupport.CommitSha);
        foreach (var l in ev.Lines) if (l.StartsWith("FAIL ") || l.StartsWith("ROUND ") && l.Contains("done")) sb.AppendLine(l);
        File.WriteAllText(Path.Combine(dir, "summary.txt"), sb.ToString());
    }
}

/// One repeatable stage per flaky suite. Each stage: launch the REAL shipping flow through GameFlow with a FRESH
/// sandbox save (never the user's), run a bounded deterministic workload, then assert the suite's own contract
/// (no shipping runner is modified; each stage here mirrors that suite's core assertions).
public static class RepeatabilitySuites
{
    static GauntletVerificationSupport.Evidence ev;

    public static void Bind(GauntletVerificationSupport.Evidence evidence) => ev = evidence;

    public static System.Collections.IEnumerator HudPhase2()
    {
        using (var session = new GauntletSession("repeat-hud2"))
        {
            yield return session.OpenHome();
            Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "HudPhase2: Hero mode selects.");
            yield return session.AwaitCity();
            var hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
            Check(hud != null, "HudPhase2: guidance HUD is up.");
            Check(WorldSession.Instance.Crimes.Exists(c => c != null && !c.Resolved), "HudPhase2: initial encounter exists for the waypoint.");
        }
    }

    public static System.Collections.IEnumerator HudPhase3()
    {
        using (var session = new GauntletSession("repeat-hud3"))
        {
            yield return session.OpenHome();
            Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "HudPhase3: Hero mode selects.");
            yield return session.AwaitCity();
            var feedback = UnityEngine.Object.FindAnyObjectByType<GameHud>();
            Check(feedback != null, "HudPhase3: feedback HUD is up.");
            WorldSession.Instance.Progression.AddXp(50, WorldSession.Instance.Hero.transform.position, "repeat");
            yield return new WaitForSecondsRealtime(0.2f);
            Check(feedback.PopupXpShown >= 50, "HudPhase3: +XP popup shown for a real grant (" + feedback.PopupXpShown + ").");
        }
    }

    public static System.Collections.IEnumerator Audio()
    {
        using (var session = new GauntletSession("repeat-audio"))
        {
            yield return session.OpenHome();
            Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Audio: Hero mode selects.");
            yield return session.AwaitCity();
            var a = AudioDirector.Instance;
            Check(a != null && a.Sources.Length == 24, "Audio: director with 24 preallocated sources.");
            Check(a.Sources.Count(s => s != null && s.isPlaying && s.clip != null) > 0, "Audio: at least one source playing after city load.");
        }
    }

    public static System.Collections.IEnumerator Sidekick()
    {
        using (var session = new GauntletSession("repeat-sidekick"))
        {
            yield return session.OpenHome();
            Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Sidekick: Hero mode selects.");
            yield return session.AwaitCity();
            var hero = WorldSession.Instance.Hero;
            Check(hero != null, "Sidekick: hero spawned.");
            var skins = hero.GetComponentsInChildren<SkinnedMeshRenderer>();
            Check(skins.Length > 0, "Sidekick: hero has skinned body renderers.");
            foreach (var s in skins) Check(s.sharedMesh != null && s.sharedMesh.vertexCount > 0, "Sidekick: body mesh bound (" + s.name + ").");
        }
    }

    public static System.Collections.IEnumerator Humanoid()
    {
        using (var session = new GauntletSession("repeat-humanoid"))
        {
            yield return session.OpenHome();
            Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Humanoid: Hero mode selects.");
            yield return session.AwaitCity();
            var npcs = WorldSession.Instance.Npcs;
            Check(npcs.Count > 0, "Humanoid: NPCs spawned.");
            var withAnimator = npcs.Where(n => n != null && n.GetComponentInChildren<Animator>() != null).ToList();
            Check(withAnimator.Count == npcs.Count(n => n != null), "Humanoid: every NPC has an Animator.");
        }
    }

    public static System.Collections.IEnumerator BackflipHurricane()
    {
        using (var session = new GauntletSession("repeat-backflip"))
        {
            yield return session.OpenHome();
            Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "BackflipHurricane: Hero mode selects.");
            yield return session.AwaitCity();
            var hero = WorldSession.Instance.Hero;
            hero.DebugSetResources(6f, 3);
            Check(hero.TryHurricaneKick(), "BackflipHurricane: kick accepted with full charges.");
            yield return new WaitForSeconds(hero.KickWindupSeconds + 0.05f);
            Check(hero.LastKickImpactTime > 0, "BackflipHurricane: kick impact fired after windup.");
        }
    }

    static void Check(bool pass, string text)
    {
        if (ev == null) return;
        if (pass) ev.Pass(text); else ev.Fail(text);
    }
}

// ---------------------------------------------------------------------- shared session wrapper
/// A verification session with a FRESH sandbox save per instance; disposes safely across scene loads.
/// Save-safety: never touches the user's real progression save; the sentinel wraps every batch.
public sealed class GauntletSession : IDisposable
{
    readonly string leaf;
    public GauntletSession(string leaf) { this.leaf = leaf; }
    string SavePath => Path.GetFullPath(Path.Combine(GauntletVerificationSupport.EvidenceRoot, leaf, "save.json"));

    public System.Collections.IEnumerator OpenHome()
    {
        WorldSession.VerificationSavePath = SavePath;
        GameFlow.VerificationSandbox = false;
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
        if (File.Exists(SavePath)) File.Delete(SavePath);
        yield return GauntletFlow.OpenHome();
    }

    public System.Collections.IEnumerator AwaitCity(float timeoutSeconds = 40f)
    {
        float until = Time.realtimeSinceStartup + timeoutSeconds;
        while (GameFlow.Instance == null || GameFlow.Instance.Loading
            || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != GameFlow.CityScene
            || WorldSession.Instance == null)
        {
            if (Time.realtimeSinceStartup > until) throw new TimeoutException("AwaitCity timed out after " + timeoutSeconds + "s");
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.5f);
    }

    public void Dispose()
    {
        // Return to Home so the next stage starts clean (static-state leakage between stages is caught by the audit runner).
        if (GameFlow.Instance != null && !GameFlow.Instance.Loading) GameFlow.Instance.Home();
    }
}
#endif
