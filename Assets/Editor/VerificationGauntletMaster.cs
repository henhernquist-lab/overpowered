#if UNITY_EDITOR
// MASTER REGRESSION ORCHESTRATOR — gauntlet item 1.
// Runs the project's EXISTING suites sequentially, each in its own Unity process against a fresh working copy of
// this project (no suite is rewritten, no gameplay file is touched). Produces one manifest (TSV) and a compact
// dashboard (summary.md) under Verification/Gauntlet/master/.
//
// Unity -batchmode -projectPath <project> -executeMethod VerificationGauntletMaster.Run -logFile <log>
// Optional environment:
//   OP_GAUNTLET_ISOLATE=per-suite   copy the project fresh for EVERY suite (much slower, maximum isolation)
//   OP_GAUNTLET_EXTENDED=1          also run the profile/benchmark suites (WorldProfile, PerformanceProfile, ...)
//   OP_GAUNTLET_ONLY=Suite1,Suite2  run only these suites (case-insensitive match on the suite column)
//   OP_GAUNTLET_TIMEOUT=2400        per-suite process timeout in seconds
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Evidence = GauntletVerificationSupport.Evidence;

public static class VerificationGauntletMaster
{
    [MenuItem("Overpowered/Verification/Master Regression Orchestrator")]
    public static void Run()
    {
        var ev = new GauntletVerificationSupport.Evidence("master", "MASTER");
        ev.Line("orchestrator starting on branch " + (GauntletVerificationSupport.TryGit(new[] { "rev-parse", "--abbrev-ref", "HEAD" }) ?? "unknown")
            + " commit " + GauntletVerificationSupport.CommitSha);
        ev.Line("contention at start: " + GauntletVerificationSupport.UnityContention.Summary());
        bool invalid = GauntletVerificationSupport.UnityContention.IsInvalid(ev);
        if (invalid) ev.Fail(GauntletVerificationSupport.UnityContention.InvalidLabel + " — every duration below is real, but any performance suite's FPS numbers are NOT comparable with uncontended runs.");
        ev.Line("save-sentinel armed: " + GauntletVerificationSupport.SaveSentinel.Arm());

        bool perSuite = Environment.GetEnvironmentVariable("OP_GAUNTLET_ISOLATE") == "per-suite";
        bool extended = Environment.GetEnvironmentVariable("OP_GAUNTLET_EXTENDED") == "1";
        int timeout = 2400;
        string t = Environment.GetEnvironmentVariable("OP_GAUNTLET_TIMEOUT");
        if (!string.IsNullOrEmpty(t) && int.TryParse(t, out var parsed) && parsed > 0) timeout = parsed;
        var only = Environment.GetEnvironmentVariable("OP_GAUNTLET_ONLY");
        var selected = Suites.Where(s => (extended || s.Core) && (only == null || only.Split(',').Any(o => o.Trim().Equals(s.Name, StringComparison.OrdinalIgnoreCase)))).ToList();

        // ---- one fresh working copy for the whole run (or per suite)
        string copy = perSuite ? null : MakeWorkingCopy(ev, "shared");
        var records = new List<GauntletVerificationSupport.BatchRecord>();
        var master = Stopwatch.StartNew();

        foreach (var suite in selected)
        {
            if (perSuite) copy = MakeWorkingCopy(ev, suite.Name);
            records.Add(RunSuite(ev, suite, copy, timeout, ".Run"));
            GauntletVerificationSupport.SaveSentinel.Verify(ev);   // item 2: sentinel after EVERY batch
            if (suite.ReloadEntry != null)
            {
                records.Add(RunSuite(ev, suite, copy, timeout, ".Reload"));
                GauntletVerificationSupport.SaveSentinel.Verify(ev);
            }
        }
        if (perSuite && copy != null) { try { Directory.Delete(copy, true); } catch (Exception e) { ev.Sample("working copy cleanup deferred: " + e.Message); } }
        master.Stop();

        string manifest = WriteManifest(ev, records);
        WriteSummary(ev, records, master.Elapsed, invalid);
        ev.Pass("MASTER ORCHESTRATOR finished " + records.Count + " suite processes in " + master.Elapsed.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture) + " min; manifest " + manifest);
        ev.Write("results.txt");
        int failed = records.Count(r => r.ExitCode != 0);
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    static GauntletVerificationSupport.BatchRecord RunSuite(Evidence ev, Suite suite, string copy, int timeoutSeconds, string suffix)
    {
        string entry = suffix == ".Reload" ? suite.ReloadEntry : suite.Entry;
        var record = new GauntletVerificationSupport.BatchRecord
        {
            Suite = suffix == ".Reload" ? suite.Name + ".Reload" : suite.Name,
            Entry = entry,
            EvidencePath = Path.Combine(copy ?? ".", suite.EvidenceDir).Replace("\\", "/")
        };
        string log = Path.Combine(EvidenceMasterDir(), "logs", record.Suite + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(log));
        var sw = Stopwatch.StartNew();
        try
        {
            using (var p = new Process())
            {
                p.StartInfo = new ProcessStartInfo(EditorApplication.applicationPath,
                    "-batchmode -projectPath \"" + copy + "\" -executeMethod " + entry + " -logFile \"" + log + "\"")
                { UseShellExecute = false };
                ev.Line("LAUNCH " + record.Suite + ": " + entry + " (log " + log + ")");
                p.Start();
                record.ProcessId = p.Id.ToString();
                if (!p.WaitForExit(timeoutSeconds * 1000))
                {
                    // OUR OWN timed-out child only — never another Unity process (see item 4's no-kill rule).
                    try { p.Kill(); } catch { }
                    record.ExitCode = -1; record.Note = "TIMEOUT after " + timeoutSeconds + "s (child process killed)";
                    ev.Fail(record.Suite + " timed out after " + timeoutSeconds + "s; process killed; log kept at " + log);
                }
                else record.ExitCode = p.ExitCode;
            }
        }
        catch (Exception e)
        {
            record.ExitCode = -1; record.Note = "launch failed: " + e.Message;
            ev.Fail(record.Suite + " could not launch: " + e.Message);
        }
        sw.Stop();
        record.DurationSeconds = sw.Elapsed.TotalSeconds;
        var (pass, fail) = GauntletVerificationSupport.ScanLogForPassFail(log);
        record.PassCount = pass; record.FailCount = fail;
        if (record.ExitCode == 0) ev.Pass(record.Suite + " exit 0 — " + pass + " PASS / " + fail + " FAIL in " + record.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s (evidence " + record.EvidencePath + ").");
        else ev.Fail(record.Suite + " exit " + record.ExitCode + " — " + pass + " PASS / " + fail + " FAIL in " + record.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s (log " + log + ").");
        return record;
    }

    /// Copies the project (Assets, Packages, ProjectSettings, dotfiles) WITHOUT Library/Temp/Logs/obj/bin, so the
    /// first launch in the copy re-imports cleanly and can never write into the real project.
    static string MakeWorkingCopy(Evidence ev, string label)
    {
        string copy = perSuiteBase(label);
        if (Directory.Exists(copy)) { try { Directory.Delete(copy, true); } catch (Exception e) { ev.Fail("could not clear working copy " + copy + ": " + e.Message); } }
        Directory.CreateDirectory(copy);
        foreach (var dir in new[] { "Assets", "Packages", "ProjectSettings" })
            FileUtil.CopyFileOrDirectory(Path.GetFullPath(dir), Path.Combine(copy, dir));
        foreach (var file in new[] { ".gitattributes", ".gitignore" })
            if (File.Exists(file)) File.Copy(file, Path.Combine(copy, file));
        ev.Pass("working copy ready: " + copy + (label != "shared" ? " (isolated for " + label + ")" : " (shared across suites)"));
        return copy;
    }
    static string perSuiteBase(string label) => WorkingCopyBase + "-" + label;
    static string WorkingCopyBase => Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "overpowered-gauntlet-workcopy"));
    static string EvidenceMasterDir() => Path.Combine(GauntletVerificationSupport.EvidenceRoot, "master");

    sealed class Suite
    {
        public string Name; public string Entry; public string ReloadEntry; public string EvidenceDir; public bool Core = true; public string Note = "";
    }

    // The project's existing suites, exactly as they are invoked today (see STATUS.md's regression sweeps).
    static readonly Suite[] Suites =
    {
        new Suite { Name="Audio",            Entry="AudioVerification.Run",             ReloadEntry="AudioVerification.Reload",             EvidenceDir="Verification/Audio" },
        new Suite { Name="Mode",             Entry="ModeVerification.Run",              ReloadEntry="ModeVerification.Reload",              EvidenceDir="Verification/Modes" },
        new Suite { Name="ModeExpansion",    Entry="ModeExpansionVerification.Run",     ReloadEntry="ModeExpansionVerification.Reload",     EvidenceDir="Verification/ModeExpansion" },
        new Suite { Name="HeroForge",        Entry="HeroForgeVerification.Run",         ReloadEntry="HeroForgeVerification.Reload",         EvidenceDir="Verification/Forge" },
        new Suite { Name="Combat",           Entry="CombatVerification.Run",                                                                EvidenceDir="Verification/Combat" },
        new Suite { Name="City",             Entry="CityVerification.Run",              ReloadEntry="CityVerification.Reload",              EvidenceDir="Verification/City" },
        new Suite { Name="CityArt",          Entry="CityArtVerification.Run",                                                               EvidenceDir="Verification/Art" },
        new Suite { Name="Humanoid",         Entry="HumanoidVerification.Run",                                                              EvidenceDir="Verification/Humanoid" },
        new Suite { Name="BackflipHurricane",Entry="BackflipHurricaneVerification.Run",                                                     EvidenceDir="Verification/Abilities" },
        new Suite { Name="Menus",            Entry="MenuPresentationVerification.Run",                                                      EvidenceDir="Verification/Menus" },
        new Suite { Name="Hud",              Entry="HudVerification.Run",                                                                   EvidenceDir="Verification/Hud" },
        new Suite { Name="HudPhase2",        Entry="HudPhase2Verification.Run",         ReloadEntry="HudPhase2Verification.Reload",         EvidenceDir="Verification/Hud/phase2" },
        new Suite { Name="HudPhase3",        Entry="HudPhase3Verification.Run",         ReloadEntry="HudPhase3Verification.Reload",         EvidenceDir="Verification/Hud/phase3" },
        new Suite { Name="Feel",             Entry="FeelVerification.Run",                                                                  EvidenceDir="Verification/Feel" },
        new Suite { Name="FirstPerson",      Entry="FirstPersonVerification.Run",       ReloadEntry="FirstPersonVerification.Reload",       EvidenceDir="Verification/FirstPerson" },
        new Suite { Name="Ice",              Entry="IceVerification.Run",               ReloadEntry="IceVerification.Reload",               EvidenceDir="Verification/Ice" },
        new Suite { Name="Sidekick",         Entry="SidekickVerification.Run",          ReloadEntry="SidekickVerification.Reload",          EvidenceDir="Verification/Sidekick" },
        new Suite { Name="SynergyAvailability",Entry="SynergyAvailabilityVerification.Run",ReloadEntry="SynergyAvailabilityVerification.Reload",EvidenceDir="Verification/Synergy" },
        new Suite { Name="World",            Entry="WorldVerification.Run",             ReloadEntry="WorldVerification.Reload",             EvidenceDir="Verification/World/verify" },
        new Suite { Name="ProceduralAnimation",Entry="ProceduralAnimationVerification.Run",                                                 EvidenceDir="Verification/Animation" },
        new Suite { Name="Balance",          Entry="BalanceVerification.Run",                                                               EvidenceDir="Verification/Balance" },
        new Suite { Name="PowerPayoff",      Entry="PowerPayoffVerification.Run",                                                               EvidenceDir="Verification/Payoff" },
        new Suite { Name="WorldProfile",     Entry="WorldProfile.Run",                                                                      EvidenceDir="Verification/World/profile", Core=false, Note="profile; extended set" },
        new Suite { Name="SidekickNpcProfile",Entry="SidekickNpcProfile.Run",                                                                 EvidenceDir="Verification/Sidekick/npc-fps", Core=false, Note="profile; extended set" },
        new Suite { Name="PerformanceProfile",Entry="PerformanceProfile.Run",                                                                EvidenceDir="Verification/Performance", Core=false, Note="contention-sensitive; extended set" },
        new Suite { Name="PowerPayoffBenchmark",Entry="PowerPayoffBenchmark.Run",                                                            EvidenceDir="Verification/Payoff/bench", Core=false, Note="benchmark; extended set" },
        new Suite { Name="SidekickClipCheck",Entry="SidekickClipCheck.Run",                                                                 EvidenceDir="Verification/Sidekick", Core=false, Note="asset check" },
    };

    static string WriteManifest(Evidence ev, List<GauntletVerificationSupport.BatchRecord> records)
    {
        Directory.CreateDirectory(EvidenceMasterDir());
        string path = Path.Combine(EvidenceMasterDir(), "manifest.tsv");
        var lines = new List<string> { "# gauntlet master manifest — commit " + GauntletVerificationSupport.CommitSha + " — " + DateTime.UtcNow.ToString("o") };
        lines.Add(GauntletVerificationSupport.BatchRecord.Header());
        lines.AddRange(records.Select(r => r.Row()));
        File.WriteAllLines(path, lines);
        return path;
    }

    static void WriteSummary(Evidence ev, List<GauntletVerificationSupport.BatchRecord> records, TimeSpan total, bool contentionInvalid)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Verification gauntlet — master regression manifest");
        sb.AppendLine();
        sb.AppendLine("- commit: `" + GauntletSupportSha() + "`");
        sb.AppendLine("- branch: `" + (GauntletVerificationSupport.TryGit(new[] { "rev-parse", "--abbrev-ref", "HEAD" }) ?? "unknown") + "`");
        sb.AppendLine("- finished: " + DateTime.UtcNow.ToString("o"));
        sb.AppendLine("- total wall time: " + total.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture) + " min across " + records.Count + " suite processes");
        sb.AppendLine("- contention: " + (contentionInvalid ? GauntletVerificationSupport.UnityContention.InvalidLabel + " — performance-suite numbers are not comparable" : GauntletVerificationSupport.UnityContention.CleanLabel));
        sb.AppendLine("- save sentinel: the user's real progression save was hash-verified unchanged after every suite batch (contents never read into evidence).");
        sb.AppendLine();
        sb.AppendLine("| suite | exit | PASS | FAIL | duration (s) | evidence |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in records)
            sb.AppendLine("| " + r.Suite + " | " + r.ExitCode + " | " + r.PassCount + " | " + r.FailCount + " | " + r.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture) + " | `" + r.EvidencePath + "` |");
        int failed = records.Count(r => r.ExitCode != 0);
        sb.AppendLine();
        sb.AppendLine(failed == 0 ? "**All " + records.Count + " suite processes exited 0.**" : "**" + failed + " suite process(es) did NOT exit 0 — see manifest rows above; do not report the branch green.**");
        File.WriteAllText(Path.Combine(EvidenceMasterDir(), "summary.md"), sb.ToString());
    }
    static string GauntletSupportSha() => GauntletVerificationSupport.CommitSha;
}
#endif
