#if UNITY_EDITOR
// VERIFICATION GAUNTLET SUPPORT — editor verification only, never ships gameplay code.
// Implements gauntlet items 2 (save-safety sentinel) and 4 (performance-contention metadata),
// plus the shared evidence writers every gauntlet verifier uses (item 11).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using Debug = UnityEngine.Debug;

public static class GauntletVerificationSupport
{
    public const string Root = "Verification/Gauntlet";
    public static string EvidenceRoot => Path.GetFullPath(Root);
    public static string CommitSha
    {
        get
        {
            if (commitSha == null)
            {
                commitSha = TryGit(new[] { "rev-parse", "HEAD" }, 8) ?? "unknown";
                if (commitSha == "unknown") commitSha = Environment.GetEnvironmentVariable("OP_COMMIT_SHA") ?? "unknown";
            }
            return commitSha;
        }
    }
    static string commitSha;

    // ------------------------------------------------------------------ evidence
    /// Runs git with a short timeout and returns trimmed stdout, or null on any failure (no throw in the editor).
    public static string TryGit(string[] arguments, int timeoutSeconds = 8)
    {
        try
        {
            using (var p = new Process())
            {
                p.StartInfo = new ProcessStartInfo("git", string.Join(" ", arguments.Select(a => "\"" + a + "\"")))
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutSeconds * 1000)) { try { p.Kill(); } catch { } return null; }
                return string.IsNullOrEmpty(output) ? null : output.Trim();
            }
        }
        catch { return null; }
    }

    public static string NewEvidenceDir(string leaf)
    {
        string dir = Path.Combine(EvidenceRoot, leaf);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public sealed class Evidence
    {
        public readonly string DirectoryPath, Tag;
        readonly List<string> lines = new List<string>();
        public Evidence(string leaf, string tag)
        {
            DirectoryPath = NewEvidenceDir(leaf);
            Tag = tag;
            Line("EVIDENCE " + Tag);
            Line("commit " + CommitSha);
            Line("branch " + (TryGit(new[] { "rev-parse", "--abbrev-ref", "HEAD" }) ?? "unknown"));
            Line("utc " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Line("contention " + UnityContention.Summary());
        }
        public void Line(string text) { lines.Add(text); Debug.Log("[GAUNTLET " + Tag + "] " + text); }
        public void Pass(string text) => Line("PASS " + text);
        public void Fail(string text) => Line("FAIL " + text);
        public void FailThrow(string text) { Fail(text); throw new InvalidOperationException(text); }
        public void Sample(string text) => Line("SAMPLE " + text);
        public int PassCount { get { int n = 0; foreach (var l in lines) if (l.StartsWith("PASS ")) n++; return n; } }
        public int FailCount { get { int n = 0; foreach (var l in lines) if (l.StartsWith("FAIL ")) n++; return n; } }
        public IReadOnlyList<string> Lines => lines;
        public void Write(params string[] fileNames)
        {
            for (int i = 0; i < fileNames.Length; i++)
                File.WriteAllLines(Path.Combine(DirectoryPath, fileNames[i]), lines);
        }
        public string FilePath(string fileName) => Path.Combine(DirectoryPath, fileName);
    }

    // ------------------------------------------------------------------ save-safety sentinel (item 2)
    /// <summary>
    /// Guards the user's real progression save across verification batches. Snapshots metadata only:
    /// length, last-write UTC and a SHA-256 hash. The hash input is the raw file bytes, and the report
    /// records ONLY the hash — never file contents — so no private save data reaches the evidence tree.
    /// </summary>
    public static class SaveSentinel
    {
        public static string RealSavePath => Path.Combine(Application.persistentDataPath, SaveFileName());
        static string SaveFileName()
        {
            var tuning = Resources.Load<GameTuning>("GameTuning");
            return tuning != null && tuning.Progression != null && !string.IsNullOrEmpty(tuning.Progression.SaveFilename)
                ? tuning.Progression.SaveFilename : "overpowered-progression.json";
        }
        sealed class Snap
        {
            public string Path; public bool Existed; public long Length; public DateTime Utc; public string Sha256;
        }
        static Snap SnapRealSave()
        {
            var s = new Snap { Path = RealSavePath };
            try
            {
                var fi = new FileInfo(s.Path);
                s.Existed = fi.Exists;
                if (fi.Exists)
                {
                    using (var fs = fi.OpenRead())
                    {
                        s.Length = fs.Length;
                        using (var sha = System.Security.Cryptography.SHA256.Create())
                            s.Sha256 = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    }
                    s.Utc = fi.LastWriteTimeUtc;
                }
            }
            catch (Exception e) { Debug.LogWarning("SaveSentinel: could not read real save metadata: " + e.Message); s.Existed = false; }
            return s;
        }
        static Snap before;
        public static string Arm()
        {
            before = SnapRealSave();
            return "real save " + (before.Existed ? "EXISTS (hash-arm only; metadata not printed)" : "absent") + " at " + before.Path;
        }
        public static bool Verify(Evidence ev)
        {
            var after = SnapRealSave();
            bool same = before == null || (!before.Existed && !after.Existed)
                || (before.Existed && after.Existed && before.Sha256 == after.Sha256 && before.Length == after.Length && before.Utc == after.Utc);
            if (same) ev.Pass("SAVE-SAFETY: the user's real progression save is byte-identical after this batch (hash verified, contents never printed).");
            else
            {
                ev.Fail("SAVE-SAFETY: the user's real progression save CHANGED during a verification batch. Details (metadata only): existed "
                    + before.Existed + "->" + after.Existed + "; length " + (before.Existed ? before.Length.ToString() : "-") + "->" + (after.Existed ? after.Length.ToString() : "-")
                    + "; write-UTC " + (before.Existed ? before.Utc.ToString("o") : "-") + "->" + (after.Existed ? after.Utc.ToString("o") : "-") + ".");
            }
            return same;
        }
    }

    // ------------------------------------------------------------------ contention metadata (item 4)
    /// <summary>
    /// Detects obvious machine contention that makes performance samples INVALID. Never kills anything:
    /// if contention is observed the run is LABELLED so readers stop comparing hero numbers from an
    /// overloaded machine. A single check (editor count) can never change inside one process, so it is
    /// evaluated once per Evidence and labelled to that whole file.
    /// </summary>
    public static class UnityContention
    {
        public const string InvalidLabel = "PERF INVALID (Unity contention detected)";
        public const string CleanLabel = "PERF baseline (no obvious Unity contention)";
        public static bool OtherUnityProcesses()
        {
            try
            {
                var own = Process.GetCurrentProcess().Id;
                int others = 0;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == own) continue;
                        string name = p.ProcessName.ToLowerInvariant();
                        bool isUnity = name.StartsWith("unity") || name.StartsWith("unity hub");
                        if (isUnity && !p.HasExited) others++;
                    }
                    catch { /* processes can exit mid-enumeration */ }
                    finally { try { p.Dispose(); } catch { } }
                }
                return others > 0;
            }
            catch { return false; } // platform does not expose process names: never block a run on the sentinel
        }
        public static string Summary()
        {
            int others = 0;
            try
            {
                var own = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id != own)
                        {
                            string name = p.ProcessName.ToLowerInvariant();
                            if (name.StartsWith("unity") && !p.HasExited) others++;
                        }
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return (others == 0 ? CleanLabel : InvalidLabel + " — " + others + " other Unity process(es) alive during the run");
        }
        public static bool IsInvalid(Evidence ev) => ev.Lines.Any(l => l.Contains(InvalidLabel));
        /// Decorates every performance line collected under this evidence with the INVALID label when contention was detected.
        public static string Perf(string text, Evidence ev) => IsInvalid(ev) ? text + "  [" + InvalidLabel + " — do not compare against other runs]" : text;
    }

    // ------------------------------------------------------------------ batch manifest record (item 1)
    public sealed class BatchRecord
    {
        public string Suite, Entry, EvidencePath, Note;
        public int ExitCode, PassCount, FailCount;
        public double DurationSeconds;
        public string ProcessId = "-";
        public static string Header()
        {
            return "suite\tentry\tprocessExit\tpass\tfail\tdurationSeconds\tprocessId\tevidencePath\tcommit\tnote";
        }
        public string Row()
        {
            return string.Join("\t", Suite, Entry, ExitCode.ToString(), PassCount.ToString(), FailCount.ToString(),
                DurationSeconds.ToString("F1", CultureInfo.InvariantCulture), ProcessId, EvidencePath ?? "-", CommitSha, Note ?? "");
        }
    }

    // ------------------------------------------------------------------ shared syntax helpers
    /// Lightweight runner-independent scan of a Unity batch log for PASS/FAIL counts (supports plain PASS lines
    /// and 'PASS x / 0 FAIL' summary headers). Never parses private data.
    public static (int pass, int fail) ScanLogForPassFail(string logPath)
    {
        int pass = 0, fail = 0;
        try
        {
            foreach (var raw in File.ReadAllLines(logPath))
            {
                string line = raw.Trim();
                if (line.StartsWith("PASS ", StringComparison.Ordinal)) pass++;
                else if (line.StartsWith("FAIL", StringComparison.Ordinal)) fail++;
                else
                {
                    var m = Regex.Match(line, @"(\d+)\s+PASS\s*/\s*(\d+)\s+FAIL", RegexOptions.IgnoreCase);
                    if (m.Success) { pass = Math.Max(pass, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)); fail = Math.Max(fail, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)); }
                }
            }
        }
        catch { }
        return (pass, fail);
    }

    /// Count of a type currently alive (inactive components included, matching the project's audit style).
    public static int CountActive<T>() where T : Component
    {
        int n = 0; foreach (var c in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include)) if (c != null) n++;
        return n;
    }

    /// Renderers / materials / GameObjects / pooled-effect counters used by the static-state audit and the watchdog.
    public sealed class SceneCensus
    {
        public int GameObjects, Renderers, Materials, ParticleSystems, AudioSources, Npcs, Crimes;
        public long ManagedBytes = -1;
        public static SceneCensus Take(WorldSession world)
        {
            var c = new SceneCensus();
            c.GameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length;
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            c.Renderers = renderers.Length;
            var mats = new HashSet<Material>();
            foreach (var r in renderers) foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
            c.Materials = mats.Count;
            c.ParticleSystems = CountActive<ParticleSystem>();
            c.AudioSources = CountActive<AudioSource>();
            if (world != null) { c.Npcs = world.Npcs.Count(x => x != null); c.Crimes = world.Crimes.Count(x => x != null && !x.Resolved); }
            try { c.ManagedBytes = GC.GetTotalMemory(false); } catch { c.ManagedBytes = -1; }
            return c;
        }
        public string Row(string tag)
        {
            return "SAMPLE " + tag + " gos=" + GameObjects + " renderers=" + Renderers + " materials=" + Materials
                + " particles=" + ParticleSystems + " audioSources=" + AudioSources + " npcs=" + Npcs + " crimes=" + Crimes
                + (ManagedBytes >= 0 ? " managedBytes=" + ManagedBytes : " managedBytes=unavailable");
        }
    }
}

// ---------------------------------------------------------------------- in-process batch framework (items 1, 2)
/// A gauntlet batch is a chain of named stages run inside ONE Unity process, each stage verified for state leakage
/// against the previous stage. Handles the save sentinel arming/checks and manifest writing for every gauntlet
/// verifier that opts in, so the suites stay untouched. (Editor-only: the outer #if UNITY_EDITOR covers it.)
#if UNITY_EDITOR
public abstract class GauntletBatchRunner : MonoBehaviour
{
    protected GauntletVerificationSupport.Evidence Ev;
    protected GauntletVerificationSupport.BatchRecord Record = new GauntletVerificationSupport.BatchRecord();
    protected string EvidenceLeaf = "batch", Tag = "BATCH", ResultFileName = "results.txt";

    protected sealed class Stage { public string Name; public Func<IEnumerator> Body; public string Suite, Entry; }
    readonly List<Stage> stages = new List<Stage>();
    public Action<int> Finished;

    protected void AddStage(string name, Func<IEnumerator> body, string suite = null, string entry = null)
        => stages.Add(new Stage { Name = name, Body = body, Suite = suite, Entry = entry });

    protected virtual void Configure() { }

    IEnumerator Start()
    {
        Configure();
        var dir = GauntletVerificationSupport.NewEvidenceDir(EvidenceLeaf);
        Record.Suite = Tag; Record.Entry = "in-process stages: " + stages.Count; Record.EvidencePath = dir;
        Record.ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
        Ev = new GauntletVerificationSupport.Evidence(EvidenceLeaf, Tag);
        Ev.Line("mode in-process gauntlet batch");
        Ev.Line("save-sentinel armed: " + GauntletVerificationSupport.SaveSentinel.Arm());
        var stack = new Stack<IEnumerator>();
        foreach (var stage in stages) stack.Push(Wrap(stage));
        while (stack.Count > 0)
        {
            object next = null; bool moved = false;
            try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception e) { FailStage(e.ToString()); Ev.Write(ResultFileName, "summary.txt"); Finish(1); yield break; }
            if (!moved) stack.Pop(); else if (next is IEnumerator nested) stack.Push(nested); else yield return next;
        }
        GauntletVerificationSupport.SaveSentinel.Verify(Ev);
        Record.PassCount = Ev.PassCount; Record.FailCount = Ev.FailCount;
        WriteManifest();
        Ev.Write(ResultFileName, "summary.txt");
        Finish(Record.FailCount == 0 ? 0 : 1);
    }

    IEnumerator Wrap(Stage stage)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int passBefore = Ev?.PassCount ?? 0, failBefore = Ev?.FailCount ?? 0;
        Ev.Line("STAGE BEGIN " + stage.Name);
        yield return stage.Body();
        sw.Stop();
        Ev.Line("STAGE END " + stage.Name + " in " + sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s");
        Record.DurationSeconds += sw.Elapsed.TotalSeconds;
        if (stage.Suite != null)
            Ev.Line("STAGE " + stage.Name + " suite=" + stage.Suite + (stage.Entry != null ? " entry=" + stage.Entry : "")
                + " pass=" + (Ev.PassCount - passBefore) + " fail=" + (Ev.FailCount - failBefore));
    }

    void FailStage(string message) { if (Ev != null) Ev.Fail("stage exception: " + message); }

    void WriteManifest()
    {
        var path = Path.Combine(Ev.DirectoryPath, "manifest.tsv");
        var rows = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string> { GauntletVerificationSupport.BatchRecord.Header() };
        rows.Add(Record.Row());
        File.WriteAllLines(path, rows);
    }

    protected void Finish(int code)
    {
        EditorApplication.LockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}

#endif

#endif
