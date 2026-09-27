#if UNITY_EDITOR
// VERIFICATION INVENTORY + HARD-CODE AUDIT — gauntlet items 6 and 7.
// Static, read-only analysis of the verification surface. NEVER rewrites anything; produces an exact file/line report.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Evidence = GauntletVerificationSupport.Evidence;

public static class VerificationInventoryRunner
{
    // Suite registry: entry point class -> (evidence dir, has reload). Derived from the runners' own file conventions.
    static readonly (string entry, string evidence, bool hasReload, string command)[] Registry =
    {
        ("AudioVerification",            "Verification/Audio",        true,  "-executeMethod AudioVerification.Run (then .Reload)"),
        ("BackflipHurricaneVerification","Verification/Abilities",    false, "-executeMethod BackflipHurricaneVerification.Run"),
        ("BalanceVerification",          "Verification/Balance",      false, "-executeMethod BalanceVerification.Run"),
        ("CameraVerification",           "Verification/Cameras",      false, "-executeMethod CameraVerification.Run"),
        ("CityArtVerification",          "Verification/Art",          false, "-executeMethod CityArtVerification.Run"),
        ("CityVerification",             "Verification/City",         true,  "-executeMethod CityVerification.Run (then .Reload)"),
        ("CombatVerification",           "Verification/Combat",       false, "-executeMethod CombatVerification.Run"),
        ("FeelVerification",             "Verification/Feel",         false, "-executeMethod FeelVerification.Run"),
        ("FirstPersonVerification",      "Verification/FirstPerson",  true,  "-executeMethod FirstPersonVerification.Run (then .Reload)"),
        ("HeroForgeVerification",        "Verification/Forge",        true,  "-executeMethod HeroForgeVerification.Run (then .Reload)"),
        ("HudPhase2Verification",        "Verification/Hud/phase2",   true,  "-executeMethod HudPhase2Verification.Run (then .Reload)"),
        ("HudPhase3Verification",        "Verification/Hud/phase3",   true,  "-executeMethod HudPhase3Verification.Run (then .Reload)"),
        ("HudVerification",              "Verification/Hud",          false, "-executeMethod HudVerification.Run"),
        ("HumanoidVerification",         "Verification/Humanoid",     false, "-executeMethod HumanoidVerification.Run"),
        ("IceVerification",              "Verification/Ice",          true,  "-executeMethod IceVerification.Run (then .Reload)"),
        ("MenuPresentationVerification", "Verification/Menus",        false, "-executeMethod MenuPresentationVerification.Run"),
        ("ModeExpansionVerification",    "Verification/ModeExpansion",true,  "-executeMethod ModeExpansionVerification.Run (then .Reload)"),
        ("ModeVerification",             "Verification/Modes",        true,  "-executeMethod ModeVerification.Run (then .Reload)"),
        ("PowerPayoffVerification",      "Verification/Payoff",       false, "-executeMethod PowerPayoffVerification.Run"),
        ("ProceduralAnimationVerification","Verification/Animation",  false, "-executeMethod ProceduralAnimationVerification.Run"),
        ("SidekickVerification",         "Verification/Sidekick",     true,  "-executeMethod SidekickVerification.Run (then .Reload)"),
        ("SynergyAvailabilityVerification","Verification/Synergy",    true,  "-executeMethod SynergyAvailabilityVerification.Run (then .Reload)"),
        ("WorldVerification",            "Verification/World/verify", true,  "-executeMethod WorldVerification.Run (then .Reload)"),
        ("SidekickNpcProfile",           "Verification/Sidekick/npc-fps", false, "-executeMethod SidekickNpcProfile.Run"),
        ("WorldProfile",                 "Verification/World/profile",false, "-executeMethod WorldProfile.Run"),
        ("PerformanceProfile",           "Verification/Performance",  false, "-executeMethod PerformanceProfile.Run"),
        ("PowerPayoffBenchmark",         "Verification/Payoff/bench", false, "-executeMethod PowerPayoffBenchmark.Run"),
        ("SidekickClipCheck",            "Verification/Sidekick",     false, "-executeMethod SidekickClipCheck.Run"),
    };

    public static int Run()
    {
        var ev = new GauntletVerificationSupport.Evidence("inventory", "INVENTORY");
        ev.Line("repo commit " + GauntletVerificationSupport.CommitSha);
        try { RunInternal(ev); }
        catch (Exception e) { ev.Fail("inventory aborted: " + e); }
        ev.Write("results.txt");
        Console.WriteLine("VerificationInventory complete: " + ev.PassCount + " PASS, " + ev.FailCount + " FLAG, evidence " + ev.DirectoryPath);
        return ev.FailCount == 0 ? 0 : 1;
    }

    static void RunInternal(Evidence ev)
    {
        string editorDir = "Assets/Editor";
        var editorFiles = Directory.GetFiles(editorDir, "*.cs").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        var runnerFiles = Directory.GetFiles("Assets/Scripts", "*VerificationRunner.cs", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var f in Directory.GetFiles("Assets/Scripts", "VerificationRunner.cs", SearchOption.AllDirectories)) runnerFiles.Add(Path.GetFileNameWithoutExtension(f));

        // ---- map every editor class to its runner (runner class name = entry class name + "Runner")
        foreach (var (entry, evidence, hasReload, command) in Registry)
        {
            bool entryExists = editorFiles.Contains(entry);
            bool runnerExists = runnerFiles.Contains(entry + "Runner") || File.Exists("Assets/Editor/" + entry + ".cs");
            string runnerFile = FindFile("Assets/Scripts", entry + "Runner.cs") ?? FindFile("Assets/Editor", entry + ".cs");
            if (entryExists && runnerExists)
                ev.Pass(entry + " -> runner " + (runnerFile ?? "?") + " -> evidence " + evidence + (hasReload ? " (Run + Reload)" : " (Run only)") + "  CMD " + command);
            else if (entryExists && !runnerExists)
                ev.Fail("ENTRY POINT WITHOUT RUNNER: " + entry + " has an Assets/Editor class but no " + entry + "Runner.cs.");
            else if (!entryExists && runnerExists)
                ev.Fail("RUNNER WITHOUT EDITOR ENTRY: " + entry + "Runner exists but no Assets/Editor/" + entry + ".cs launches it.");
        }
        // unregistered files on either side
        foreach (var f in editorFiles)
            if (f.EndsWith("Verification", StringComparison.Ordinal) && !Registry.Any(r => r.entry == f))
                ev.Fail("UNREGISTERED editor verification class: " + f + " (not in the gauntlet registry; add it or explain it).");
        foreach (var f in runnerFiles)
            if (f.EndsWith("VerificationRunner", StringComparison.Ordinal) && !Registry.Any(r => r.entry == f.Replace("Runner", "")))
                ev.Fail("UNREGISTERED runner: " + f + " (no registry row and no matching Assets/Editor entry).");

        // ---- Run/Reload chain audit (item 5's wrapper-level view)
        foreach (var (entry, evidence, hasReload, command) in Registry.Where(r => r.hasReload))
        {
            string entryFile = File.ReadAllText("Assets/Editor/" + entry + ".cs");
            bool run = Regex.IsMatch(entryFile, @"public static void Run\s*\(");
            bool reload = Regex.IsMatch(entryFile, @"public static void Reload\s*\(");
            if (run && reload) ev.Pass("RELOAD CHAIN " + entry + ": Run + Reload both present; save handoff via " + HandoffDescription(entryFile) + ".");
            else if (run && !reload) ev.Fail("RELOAD WITHOUT RUN PAIRING: " + entry + " has Run but no Reload (save handoff untested across processes).");
            else if (!run && reload) ev.Fail("RELOAD WITHOUT RUN PAIRING: " + entry + " has Reload but no Run.");
        }
        foreach (var (entry, evidence, hasReload, command) in Registry.Where(r => !r.hasReload))
        {
            string entryFile = File.Exists("Assets/Editor/" + entry + ".cs") ? File.ReadAllText("Assets/Editor/" + entry + ".cs") : "";
            if (entryFile.Contains("public static void Reload(") && !entryFile.Contains("public static void Run("))
                ev.Fail("RELOAD WITHOUT RUN PAIRING: " + entry + " exposes Reload with no Run.");
        }

        // ---- Run failed -> Reload must not touch the real save (wrapper-level precondition, no shipping change)
        AuditReloadHandoffs(ev, Registry);

        // ---- stale docs referencing suites that no longer exist
        foreach (var doc in new[] { "README.md", "AGENTS.md", "STATUS.md", "docs/architecture.md", "docs/agent-scope.md",
                                    "docs/hero-forge.md", "docs/first-person.md", "docs/presentation-packages.md",
                                    "docs/overnight-queue.md", "docs/toon-evaluation.md", "docs/content.md" })
        {
            if (!File.Exists(doc)) continue;
            foreach (var line in File.ReadAllLines(doc))
                foreach (Match m in Regex.Matches(line, @"\b([A-Z][A-Za-z]+Verification)\b"))
                {
                    string name = m.Groups[1].Value;
                    if (!editorFiles.Contains(name) && !runnerFiles.Contains(name + "Runner") && name != "VerificationHarness")
                        ev.Fail("STALE DOC REFERENCE in " + doc + ": suite \"" + name + "\" is referenced but has no Assets/Editor entry point and no runner.");
                }
        }

        // ---- suites that still assume 5 powers / 10 synergies / Coming Soon modes
        AuditStaleAssumptions(ev, editorFiles, runnerFiles);
    }

    static string FindFile(string root, string name)
    {
        foreach (var f in Directory.GetFiles(root, name, SearchOption.AllDirectories)) return f;
        return null;
    }

    static string HandoffDescription(string entryFile)
    {
        if (entryFile.Contains("save-path.txt")) return "save-path.txt handoff file";
        if (entryFile.Contains("SessionState.GetString(Key+\"save\"")) return "SessionState save pointer";
        if (entryFile.Contains("SessionState")) return "SessionState mode key";
        return "in-file save pointer";
    }

    /// Wrapper-level preconditions: every suite whose Reload re-reads a saved path from disk or SessionState
    /// must fail loudly instead of silently falling back to the user's real save when the Run that wrote the
    /// pointer never completed. Verified by static inspection of the entry file only (no gameplay change).
    static void AuditReloadHandoffs(Evidence ev, (string entry, string evidence, bool hasReload, string command)[] registry)
    {
        foreach (var (entry, evidence, hasReload, command) in registry)
        {
            string path = "Assets/Editor/" + entry + ".cs";
            if (!File.Exists(path)) continue;
            string text = File.ReadAllText(path);
            bool reloadReadsPointer = Regex.IsMatch(text, @"File\.ReadAllText\([^)]*(save-path|pathFile|old-save-path|save\.txt)") 
                                   || Regex.IsMatch(text, @"SessionState\.GetString\([^)]*(save|Save)");
            bool reloadWritesPointer = Regex.IsMatch(text, @"File\.WriteAllText\([^)]*(save-path|pathFile|save\.txt)");
            bool runAllocatesFreshSave = Regex.IsMatch(text, @"Guid\.NewGuid\(\)\.ToString\(""N""\)");
            if (!hasReload) continue;
            if (reloadReadsPointer && reloadWritesPointer)
                ev.Pass("RELOAD HANDOFF " + entry + ": Run writes the save pointer, Reload re-reads it (never the real save).");
            else if (reloadReadsPointer && !reloadWritesPointer && runAllocatesFreshSave)
                ev.Pass("RELOAD HANDOFF " + entry + ": Run allocates a fresh sandbox save and Reload reads the same pointer.");
            else if (reloadReadsPointer && !reloadWritesPointer && !runAllocatesFreshSave)
                ev.Fail("RELOAD HANDOFF " + entry + ": Reload reads a pointer file that no visible Run writes; if Run aborts, Reload silently loads whatever the pointer names.");
            else if (!reloadReadsPointer && !runAllocatesFreshSave)
                ev.Fail("RELOAD HANDOFF " + entry + ": neither a fresh sandbox save nor a pointer handoff is visible; check whether this suite can reach the real progression save.");
        }
    }

    static void AuditStaleAssumptions(Evidence ev, HashSet<string> editorFiles, HashSet<string> runnerFiles)
    {
        // Suites asserting the Coming Soon modes stay disabled (obsolete since Free Play + Endless shipped).
        foreach (var (entry, evidence, hasReload, command) in Registry)
        {
            string path = "Assets/Editor/" + entry + ".cs";
            if (!File.Exists(path)) continue;
            string text = File.ReadAllText(path);
            if (Regex.IsMatch(text, @"COMING SOON[^\n]*refuses launch|refuses launch[^\n]*COMING SOON"))
                ev.Fail("STALE ASSUMPTION in " + entry + ": asserts Free Play / Endless Fight 'refuse launch' as Coming Soon placeholders (both shipped 2026-09-22).");
            var powers = CountAssumedPowers(text);
            if (powers >= 0 && powers != 5) ev.Fail("HARD-CODED power count " + powers + " in " + entry + " (shipping catalog is 5 powers; suites must read PowerUser.Powers.Count).");
            var synergies = CountAssumedSynergies(text);
            if (synergies >= 0 && synergies != 10) ev.Fail("HARD-CODED synergy count " + synergies + " in " + entry + " (shipping catalog is 10 synergies).");
        }
        foreach (var runner in runnerFiles)
        {
            string path = FindFile("Assets/Scripts", runner + ".cs");
            if (path == null) continue;
            string text = File.ReadAllText(path);
            if (Regex.IsMatch(text, @"COMING SOON[^\n]*refuses launch|refuses launch[^\n]*COMING SOON")) ev.Fail("STALE ASSUMPTION in " + runner + ": Coming Soon refusal assertion (obsolete).");
        }
    }

    static int CountAssumedPowers(string text)
    {
        var m = Regex.Match(text, @"Powers\.Count\s*==\s*(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }
    static int CountAssumedSynergies(string text)
    {
        var m = Regex.Match(text, @"Synergies\.Length\s*==\s*(\d+)|synergies\.Count\s*==\s*(\d+)");
        if (!m.Success) return -1;
        return int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
    }
}

// ---------------------------------------------------------------------- hard-coded assumption audit (item 7)
public static class HardCodeAuditRunner
{
    // Patterns that historically bit this project. Each is REPORT-ONLY: no rewrites happen here.
    static readonly (string pattern, string why, bool wordBoundary)[] Patterns =
    {
        (@"Powers\.Count\s*==\s*\d+", "literal power count", false),
        (@"Powers\.Length\s*==\s*\d+", "literal power count", false),
        (@"Powers\.Count\s*[<>]=?\s*\d+", "literal power count", false),
        (@"Cues\.Length\s*==\s*\d+", "literal cue/audio count", false),
        (@"Synergies\??\.Length\s*==\s*\d+|synergies\??\.Count\s*==\s*\d+", "literal synergy count", false),
        (@"Heroes\.Length\s*==\s*\d+", "literal hero count", false),
        (@"Npcs\.Count\s*==\s*\d+", "literal NPC count", false),
        (@"Police\.Count\s*==\s*\d+|Civilians\s*==\s*\d+", "literal police/civilian count", false),
        (@"new Vector3\s*\(\s*-?\d+(\.\d+)?f?\s*,\s*-?\d+(\.\d+)?f?\s*,\s*-?\d+(\.\d+)?f?\s*\)", "literal city-grid coordinate", false),
        (@"new Vector2Int\s*\(\s*\d+\s*,\s*\d+\s*\)", "literal screen/city-grid integer pair", false),
        (@"Modes/([a-z-]+)""", "literal mode ID (data-driven modes should be discovered, not named)", false),
        (@"Screen\.currentResolution|Screen\.SetResolution\s*\(\s*\d+\s*,\s*\d+", "literal screen resolution where dynamic scaling applies", false),
        (@"new Rect\s*\(\s*\d+", "literal pixel-rect (resolution-dependent) layout", false),
    };

    public static int Run()
    {
        var ev = new GauntletVerificationSupport.Evidence("hardcode-audit", "HARDCODE");
        ev.Line("repo commit " + GauntletVerificationSupport.CommitSha);
        var files = Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Assets/Editor", "*.cs"))
            .Where(f => !f.Contains("GauntletVerificationSupport") && !f.Contains("VerificationInventoryRunner")
                     && !f.Contains("HardCodeAuditRunner"))
            .OrderBy(f => f);
        int total = 0;
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                bool inComment = line.TrimStart().StartsWith("//");
                foreach (var (pattern, why, wb) in Patterns)
                {
                    foreach (Match m in Regex.Matches(line, pattern))
                    {
                        // ignore obvious non-issues: defaults in data classes and string constants
                        if (inComment) continue;
                        ev.Fail("HARD-CODED " + why + " — " + Normalized(file) + ":" + (i + 1) + ": " + line.Trim());
                        total++;
                        break; // one report per line per pattern kind
                    }
                }
            }
        }
        ev.Line("total flagged lines " + total);
        ev.Write("results.txt");
        Console.WriteLine("HardCodeAudit complete: " + total + " flagged lines, evidence " + ev.DirectoryPath);
        return 0; // report-only: the audit itself never fails the run
    }

    static string Normalized(string path) => path.Replace("\\", "/");
}

#endif
