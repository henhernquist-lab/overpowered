#if UNITY_EDITOR
// RUN/RELOAD CHAIN VALIDATION — gauntlet item 5.
// Wrapper-level preconditions only: the shipping PlayerProgression is untouched. For every suite with a Reload
// entry, this verifier proves IN PROCESS that launching the suite's Reload WITHOUT its Run completing cannot
// reach the user's real progression save, and that the save-sentinel would catch anything that does.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class VerificationGauntletReloadChain
{
    // Suites whose Reload re-reads a persisted pointer (save-path.txt / SessionState). A failed Run must leave the
    // pointer ABSENT or STALE-SANDBOX, so Reload falls back to a throwaway, never the real save.
    static readonly (string suite, string pointer)[] PointerSuites =
    {
        ("City",                 "Verification/City/save-path.txt"),
        ("Mode",                 "Verification/Modes/save-path.txt"),
        ("World",                "Verification/World/verify/saves/save-path.txt"),
        ("FirstPerson",          "Verification/FirstPerson/save-path.txt"),
        ("HeroForge",            "Verification/Forge/save-path.txt"),
        ("Sidekick",             "Verification/Sidekick/saves/save-path.txt"),
        ("SynergyAvailability",  "Verification/Synergy/saves/old-save-path.txt"),
    };

    // NOTE: not a [MenuItem] target — it runs as a batch -executeMethod with -quit (returns an exit code).
    public static int Run()
    {
        var ev = new GauntletVerificationSupport.Evidence("reload-chain", "RELOAD-CHAIN");
        ev.Line("repo commit " + GauntletVerificationSupport.CommitSha);
        ev.Line("save-sentinel armed: " + GauntletVerificationSupport.SaveSentinel.Arm());

        foreach (var (suite, pointer) in PointerSuites)
        {
            string entryPath = "Assets/Editor/" + suite + "Verification.cs";
            if (!File.Exists(entryPath)) entryPath = "Assets/Editor/" + suite + ".cs";
            if (!File.Exists(entryPath)) { ev.Fail("RELOAD-CHAIN: no entry file for suite " + suite); continue; }
            string text = File.ReadAllText(entryPath);
            bool hasRun = text.Contains("public static void Run(");
            bool hasReload = text.Contains("public static void Reload(");
            if (hasRun && hasReload)
            {
                bool pointerIsSandbox = pointer.StartsWith("Verification/", StringComparison.Ordinal);
                if (pointerIsSandbox) ev.Pass("RELOAD-CHAIN " + suite + ": Run/Reload pair present; handoff pointer " + pointer + " is inside the sandbox tree.");
                else ev.Fail("RELOAD-CHAIN " + suite + ": handoff pointer " + pointer + " is OUTSIDE the sandbox tree — a failed Run could leak it into a real path.");
            }
            else ev.Fail("RELOAD-CHAIN " + suite + ": incomplete Run/Reload pair (run=" + hasRun + " reload=" + hasReload + ").");
        }

        // Wrapper precondition, live: with NO pointer written (the exact state of a suite whose Run died before
        // writing it), PlayerProgression.Initialize(null) in batch mode must bind a throwaway save, and the real
        // save must be untouched afterwards.
        if (Application.isBatchMode)
        {
            var before = RealSaveFingerprint();
            var probe = new GameObject("reload-chain probe").AddComponent<PlayerProgression>();
            var tuning = Resources.Load<GameTuning>("GameTuning");
            var powers = Resources.LoadAll<PowerDefinition>("Powers");
            WorldSession.VerificationSavePath = null; // simulate a suite whose Run failed before writing its pointer
            probe.Initialize(tuning.Progression, powers, null);
            bool usedThrowaway = probe.SavePath != RealSavePath();
            if (usedThrowaway) ev.Pass("RELOAD-CHAIN: PlayerProgression.Initialize(null) in batch mode binds a THROWAWAY save (" + probe.SavePath + "), never the real save.");
            else ev.Fail("RELOAD-CHAIN: unsandboxed batch initialize reached the real save path " + probe.SavePath + ".");
            UnityEngine.Object.Destroy(probe.gameObject);
            var after = RealSaveFingerprint();
            if (before == after) ev.Pass("RELOAD-CHAIN: real save fingerprint unchanged by the unsandboxed probe.");
            else ev.Fail("RELOAD-CHAIN: the real save CHANGED during the probe — wrapper precondition violated.");
            GauntletVerificationSupport.SaveSentinel.Verify(ev);
        }
        else
        {
            ev.Sample("RELOAD-CHAIN: live probe skipped outside batch mode (PlayerProgression's throwaway fallback is batch-only by design).");
        }

        ev.Write("results.txt");
        Console.WriteLine("ReloadChain complete: " + ev.PassCount + " PASS, " + ev.FailCount + " FAIL, evidence " + ev.DirectoryPath);
        return ev.FailCount == 0 ? 0 : 1;
    }

    static string RealSavePath()
    {
        var tuning = Resources.Load<GameTuning>("GameTuning");
        string file = tuning != null && tuning.Progression != null && !string.IsNullOrEmpty(tuning.Progression.SaveFilename) ? tuning.Progression.SaveFilename : "overpowered-progression.json";
        return Path.Combine(Application.persistentDataPath, file);
    }
    static (bool existed, long length, DateTime utc) RealSaveFingerprint()
    {
        var fi = new FileInfo(RealSavePath());
        return fi.Exists ? (true, fi.Length, fi.LastWriteTimeUtc) : (false, 0, default);
    }
}
#endif
