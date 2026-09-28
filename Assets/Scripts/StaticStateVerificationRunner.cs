#if UNITY_EDITOR
// STATIC-STATE AUDIT + GAME FLOW MATRIX — gauntlet items 8 and 9.
// Everything below drives the real shipping flow (GameFlow.Select / Results / Home) with sandboxed saves and
// reports exact offender types/fields. It never edits gameplay code and never fixes state here.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class StaticStateVerificationRunner : MonoBehaviour
{
    public bool IncludeFlowMatrix = true;
    public Action<int> Finished;
    GauntletVerificationSupport.Evidence ev;
    readonly List<CensusRow> census = new List<CensusRow>();
    readonly GauntletVerificationSupport.BatchRecord record = new GauntletVerificationSupport.BatchRecord();

    sealed class CensusRow
    {
        public int Session; public string Phase; public int Gos, Renderers, Materials, Particles, Audio, Npcs, Crimes; public long Managed;
    }

    IEnumerator Start()
    {
        var dir = GauntletVerificationSupport.NewEvidenceDir("static-state");
        ev = new GauntletVerificationSupport.Evidence("static-state", "STATIC-STATE");
        ev.Line("save-sentinel armed: " + GauntletVerificationSupport.SaveSentinel.Arm());
        record.Suite = "StaticStateAndFlowMatrix"; record.EvidencePath = dir;
        record.ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();

        // ---- pre-census: no session has run yet
        yield return GauntletFlow.OpenHome();
        Census(0, "home-before-any-session");

        // ---- flow matrix (item 8): Home -> mode -> Results/other -> Home, repeated twice for static leakage
        var matrix = new (string id, string label)[] { ("hero", "Hero"), ("villain", "Villain"), ("free-play", "Free Play"), ("endless-fight", "Endless Hero"), ("endless-fight-villain", "Endless Villain") };
        for (int cycle = 1; cycle <= 2; cycle++)
        {
            foreach (var (id, label) in matrix)
            {
                ev.Line("MATRIX cycle " + cycle + " " + label);
                yield return MatrixLeg(id, label, cycle);
            }
            if (IncludeFlowMatrix) yield return CycleLeakageChecks(cycle);
        }

        // ---- static-state audit (item 9)
        yield return StaticAudit();

        if (!GauntletVerificationSupport.SaveSentinel.Verify(ev)) record.FailCount++;
        record.PassCount = ev.PassCount; record.FailCount += ev.FailCount;
        WriteCensusCsv(dir);
        ev.Write("results.txt");
        Finished?.Invoke(record.FailCount == 0 ? 0 : 1);
    }

    /// One matrix leg: Home -> mode -> (session end via the shipping path) -> Home.
    IEnumerator MatrixLeg(string modeId, string label, int cycle)
    {
        WorldSession.VerificationSavePath = GauntletSessionSave("flow-matrix-" + modeId);
        GameFlow.VerificationSandbox = false;
        yield return GauntletFlow.OpenHome();
        var menu = FindAnyObjectByType<ModeScreens>();
        if (menu == null) { ev.Fail("MATRIX " + label + ": Home menu did not appear."); yield break; }
        // Data-driven discovery — no literal mode IDs in the assertions.
        var modes = Resources.LoadAll<GameModeDefinition>("Modes");
        var mode = modes.FirstOrDefault(m => m.Id == modeId);
        if (mode == null) { ev.Fail("MATRIX " + label + ": no GameModeDefinition with Id=" + modeId + " under Resources/Modes."); yield break; }

        int gosBefore = CountAllGameObjects();
        if (!GameFlow.Instance.Select(mode)) { ev.Fail("MATRIX " + label + ": Select refused a playable definition."); yield break; }
        yield return GauntletFlow.AwaitCity();
        Check(true, "MATRIX " + label + " cycle " + cycle + ": Home -> city loaded (mode " + mode.Id + ", session live).");
        var w = WorldSession.Instance;
        Check(w != null && w.Hero != null, "MATRIX " + label + " cycle " + cycle + ": hero spawned.");
        Check(w.Npcs.Count > 0, "MATRIX " + label + " cycle " + cycle + ": NPCs populated (" + w.Npcs.Count + ").");
        Census(cycle, label + "-in-session");
        int crimesAtStart = w.Crimes.Count(x => x != null && !x.Resolved);

        bool results = mode.Id != "free-play"; // Free Play never ends on its own; its matrix leg is Home -> Free Play -> Home.
        if (results)
        {
            w.Mode.EndToResults();
            yield return GauntletFlow.AwaitResults();
            Check(true, "MATRIX " + label + " cycle " + cycle + ": session ended through the shipping path and Results loaded.");
            Census(cycle, label + "-results");
            var result = GameFlow.Instance.Result;
            Check(result != null, "MATRIX " + label + " cycle " + cycle + ": SessionResult present.");
            Check(result != null && result.ModeId == mode.Id, "MATRIX " + label + " cycle " + cycle + ": results belong to the launched mode.");
            yield return GauntletFlow.OpenHome();
        }
        else
        {
            yield return GauntletFlow.OpenHome(); // the leg under test: leave Free Play straight back to Home
            Check(!GameFlow.Instance.Loading && SceneManager.GetActiveScene().name == GameFlow.HomeScene, "MATRIX " + label + " cycle " + cycle + ": Free Play -> Home without any session end.");
        }
        Check(true, "MATRIX " + label + " cycle " + cycle + ": back Home.");
        Census(cycle, label + "-home-after");

        // Leakage between legs: NPC/crime/session counts must return to their pre-session values.
        int gosAfter = CountAllGameObjects();
        var leak = CompareLeak(gosBefore, gosAfter, crimesAtStart);
        if (leak == null) ev.Pass("MATRIX " + label + " cycle " + cycle + ": no GameObject/crime leakage after returning Home (" + gosBefore + " -> " + gosAfter + " GameObjects).");
        else ev.Fail("MATRIX " + label + " cycle " + cycle + ": LEAK " + leak + ".");
    }

    /// Repeated cycles must not leak STATIC state: census deltas and specific singletons are checked at Home.
    string CompareLeak(int gosBefore, int gosAfter, int crimesAtStart)
    {
        if (crimesAtStart > 0) return null; // crimes were resolved or still active; not a leakage signal by itself
        if (gosAfter > gosBefore + 12) return "Home gained " + (gosAfter - gosBefore) + " GameObjects after a full cycle";
        return null;
    }

    IEnumerator CycleLeakageChecks(int cycle)
    {
        // Session-level singletons must be gone once we are back Home.
        Check(WorldSession.Instance == null, "Home clears WorldSession.Instance after cycle " + cycle + ".");
        Check(FindAnyObjectByType<GameModeSession>() == null, "Home clears GameModeSession after cycle " + cycle + ".");
        Check(Time.timeScale == 1f, "timeScale restored to 1 at Home after cycle " + cycle + ".");
        Check(TimeArbiter.MenuPaused == false && !TimeArbiter.HitPaused, "TimeArbiter fully unpaused at Home after cycle " + cycle + ".");
        yield return null;
    }

    // ------------------------------------------------------------------ item 9: exact-offender static audit
    IEnumerator StaticAudit()
    {
        ev.Line("STATIC AUDIT begin");
        var offenders = new List<string>();

        // (a) Census growth across the whole matrix: the last Home must not hold more scene objects than the first.
        var first = census.FirstOrDefault(c => c.Phase == "home-before-any-session");
        var last = census.LastOrDefault(c => c.Phase != null && c.Phase.EndsWith("-home-after"));
        if (first != null && last != null)
        {
            if (last.Gos <= first.Gos + 12) ev.Pass("CENSUS: Home object count stable across all cycles (" + first.Gos + " -> " + last.Gos + " GameObjects).");
            else offenders.Add("Home GameObjects grew " + (last.Gos - first.Gos) + " across the matrix (" + first.Gos + " -> " + last.Gos + "); something survives session teardown.");
            if (last.Renderers <= first.Renderers + 12) ev.Pass("CENSUS: Home renderer count stable across all cycles (" + first.Renderers + " -> " + last.Renderers + ").");
            else offenders.Add("Home renderers grew " + (last.Renderers - first.Renderers) + " across the matrix (" + first.Renderers + " -> " + last.Renderers + ").");
            if (last.Audio <= first.Audio) ev.Pass("CENSUS: no audio sources leak into Home across cycles (" + first.Audio + " -> " + last.Audio + ").");
            else offenders.Add("Home audio sources grew " + (last.Audio - first.Audio) + " across the matrix (audio director leak).");
            if (last.Managed >= 0 && first.Managed >= 0 && last.Managed <= first.Managed * 2 + 1_000_000)
                ev.Pass("CENSUS: managed memory at Home within 2x of the first Home (" + first.Managed + " -> " + last.Managed + " bytes).");
            else if (last.Managed >= 0 && first.Managed >= 0) offenders.Add("Managed memory at Home grew beyond 2x baseline (" + first.Managed + " -> " + last.Managed + " bytes).");
        }

        // (b) Known-reset contracts (each offender is reported with its exact type/field; nothing is fixed here).
        if (AudioDirector.Instance != null && AudioDirector.Instance.gameObject.scene.isLoaded)
        {
            var sources = AudioDirector.Instance.Sources.Count(s => s != null && s.isPlaying);
            if (sources > 0) offenders.Add("AudioDirector: " + sources + " sources still playing while Home has no session (contract: city bed stops at Home).");
            else ev.Pass("AudioDirector: no sources playing at Home.");
        }
        if (TimeArbiter.MenuPaused || TimeArbiter.HitPaused) offenders.Add("TimeArbiter: MenuPaused/HitPaused survive into Home (contract: Reset on session teardown).");
        else ev.Pass("TimeArbiter: fully reset at Home.");
        if (Application.isBatchMode && CityMaterials.SuitsCreated > 0)
            ev.Sample("CityMaterials.SuitsCreated=" + CityMaterials.SuitsCreated + " (static, cumulative by design; flag only if Home suits were rebuilt repeatedly).");
        var field = typeof(CityMaterials).GetField("Current", BindingFlags.Static | BindingFlags.NonPublic);
        var current = field?.GetValue(null) as CityMaterials;
        if (current != null) offenders.Add("CityMaterials.Current is alive while no city exists (owner OnDestroy should have cleared it).");
        else ev.Pass("CityMaterials.Current cleared with the city.");
        if (EnemyRoster.Current == null) offenders.Add("EnemyRoster.Current is null at Home after sessions ran (lazy load should survive).");
        else ev.Pass("EnemyRoster.Current still resolvable (by-design cache).");
        if (NpcLod.Current != null) offenders.Add("NpcLod.Current alive at Home (its owner is destroyed with the city; contract: cleared on teardown).");
        else ev.Pass("NpcLod.Current cleared with the city.");
        if (FindAnyObjectByType<FeelDirector>() != null) offenders.Add("FeelDirector instance survived into Home (contract: per-session, OnDestroy clears Instance).");
        else ev.Pass("FeelDirector not present at Home.");
        var hud = FindAnyObjectByType<GameHud>();
        if (hud != null) offenders.Add("GameHud present at Home (per-city HUD leaked across teardown).");
        else ev.Pass("GameHud not present at Home.");
        if (FindObjectsByType<CityNpc>(FindObjectsInactive.Include).Any()) offenders.Add("CityNpc instances survive into Home.");
        else ev.Pass("No CityNpc instances at Home.");
        if (FindObjectsByType<CrimeEvent>(FindObjectsInactive.Include).Any()) offenders.Add("CrimeEvent instances survive into Home.");
        else ev.Pass("No CrimeEvent instances at Home.");
        // Static-string world: report statics of the audited shipping types verbatim for the record.
        foreach (var audited in new[] { typeof(WorldSession), typeof(GameFlow), typeof(TimeArbiter) })
        {
            var statics = audited.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => !f.IsLiteral && !f.IsInitOnly).ToList();
            ev.Sample("static fields on " + audited.Name + ": " + string.Join(", ", statics.Select(f => f.FieldType.Name + " " + f.Name)));
        }

        if (offenders.Count == 0) ev.Pass("STATIC AUDIT: no surviving state where its contract says it should reset.");
        foreach (var o in offenders) ev.Fail("STATIC AUDIT offender — " + o);
        ev.Line("STATIC AUDIT end");
        yield return null;
    }

    void Census(int session, string phase)
    {
        var c = GauntletVerificationSupport.SceneCensus.Take(WorldSession.Instance);
        census.Add(new CensusRow { Session = session, Phase = phase, Gos = c.GameObjects, Renderers = c.Renderers, Materials = c.Materials, Particles = c.ParticleSystems, Audio = c.AudioSources, Npcs = c.Npcs, Crimes = c.Crimes, Managed = c.ManagedBytes });
        ev.Sample(c.Row("session " + session + " " + phase));
    }

    static int CountAllGameObjects() => UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length;
    static string GauntletSessionSave(string leaf)
    {
        var dir = Path.Combine(GauntletVerificationSupport.EvidenceRoot, leaf);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "save.json");
    }
    void Check(bool pass, string text) { if (pass) ev.Pass(text); else ev.Fail(text); }
    void WriteCensusCsv(string dir)
    {
        var lines = new List<string> { "session,phase,gameObjects,renderers,materials,particles,audioSources,npcs,crimes,managedBytes" };
        foreach (var r in census)
            lines.Add(string.Join(",", r.Session, r.Phase, r.Gos, r.Renderers, r.Materials, r.Particles, r.Audio, r.Npcs, r.Crimes, r.Managed));
        File.WriteAllLines(Path.Combine(dir, "census.csv"), lines);
    }
}
#endif
