using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// Save migration and robustness of PlayerProgression, in EDIT mode (no scene), on sandbox files only:
/// legacy saves, missing lists, duplicates, unknown / removed ids, removed synergy pairs, corrupt and truncated files with
/// and without a .bak, interrupted .tmp files (ignored beside a readable save, used when it is the newest readable copy),
/// huge and negative values, the unchanged Version policy, repair idempotence, and that the player's REAL save file is
/// never touched. Run writes a repaired save and a pointer; Reload (a second Unity process) reads it back.
///   Unity -batchmode -projectPath <copy> -executeMethod SaveRobustnessVerification.Run
///   Unity -batchmode -projectPath <copy> -executeMethod SaveRobustnessVerification.Reload
/// Output: Verification/SaveRobustness/results.txt and reload.txt.
public static class SaveRobustnessVerification
{
    const string Folder = "Verification/SaveRobustness/";
    static string Saves => Path.GetFullPath(Folder + "saves/");
    static readonly List<string> log = new List<string>();
    static GameTuning Tuning => Resources.Load<GameTuning>("GameTuning");
    static PowerDefinition[] Powers { get { var p = Resources.LoadAll<PowerDefinition>("Powers"); Array.Sort(p, (a, b) => string.CompareOrdinal(a.Id, b.Id)); return p; } }
    static void Check(bool ok, string line) { if (!ok) throw new Exception(line); log.Add("PASS " + line); }
    static void Log(string line) { log.Add(line); }
    public static void Run() { Execute("results.txt", Checks); }
    public static void Reload() { Execute("reload.txt", ReloadChecks); }
    static void Execute(string file, Action body)
    {
        log.Clear(); int code = 0; Directory.CreateDirectory(Saves);
        string real = RealSavePath(); var before = Fingerprint(real);
        try { body(); Check(Fingerprint(real) == before, $"The real save ({real}) is untouched: {before}."); }
        catch (Exception e) { log.Add("FAIL " + e); code = 1; }
        File.WriteAllLines(Folder + file, log);
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
    static string RealSavePath() => Path.Combine(Application.persistentDataPath, Tuning.Progression.SaveFilename);
    static string Fingerprint(string path)
    {
        var parts = new List<string>();
        foreach (var f in new[] { path, path + ".bak", path + ".tmp", path + ".corrupt" })
            parts.Add(File.Exists(f) ? $"{Path.GetFileName(f)}:{new FileInfo(f).Length}b@{File.GetLastWriteTimeUtc(f):O}" : Path.GetFileName(f) + ":absent");
        return string.Join(" ", parts);
    }
    // ---------------------------------------------------------------- helpers
    static string Fresh(string name) { string p = Saves + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json"; return p; }
    static string Write(string name, string json) { string p = Fresh(name); File.WriteAllText(p, json); return p; }
    static PlayerProgression Load(string path)
    {
        var go = new GameObject("Save probe") { hideFlags = HideFlags.HideAndDontSave };
        var p = go.AddComponent<PlayerProgression>(); p.Initialize(Tuning.Progression, Powers, path); return p;
    }
    static void Done(PlayerProgression p) { UnityEngine.Object.DestroyImmediate(p.gameObject); }
    static string Save(int level, int xp = 0, int points = 0, string extra = "") =>
        "{\"Version\":1,\"Level\":" + level + ",\"Xp\":" + xp + ",\"Points\":" + points + ",\"Powers\":[{\"Id\":\"strength\",\"Tier\":0}],\"Rooftops\":[]" + extra + "}";
    // ---------------------------------------------------------------- checks
    static void Checks()
    {
        Log("Sandbox: " + Saves);
        Legacy(); MissingLists(); Duplicates(); UnknownIds(); Corrupt(); Temp(); Values(); Policy(); Idempotent();
    }
    static void Legacy()
    {
        Log("---- LEGACY");
        var p = Load(Write("legacy", Save(3, 10, 2)));
        Check(p.LastError == null && p.RecoveredFrom == null && p.Data.Level == 3 && p.Data.Xp == 10 && p.Data.Points == 2 && p.Data.ModeRecords != null && p.Data.SeenHints != null && p.Data.PowerStats != null && p.Data.PowerStats.Count == 0 && p.Repairs.Count == 0,
            "Minimal legacy save (no ModeRecords / SeenHints / Loadout / PowerStats) loads unchanged with empty new lists and no repairs.");
        Check(Powers.Where(d => d.InitiallyUnlocked).All(p.Owns) && p.Data.Loadout != null && !string.IsNullOrEmpty(p.Data.Loadout.HeroId), $"Initially unlocked powers granted, loadout validated ({p.Data.Loadout.HeroId}: {p.Data.Loadout.PowerA} + {p.Data.Loadout.PowerB}).");
        Done(p);
    }
    static void MissingLists()
    {
        Log("---- MISSING LISTS");
        var p = Load(Write("missing", "{\"Version\":1,\"Level\":4,\"Xp\":5,\"Points\":1}"));
        Check(p.LastError == null && p.Data.Level == 4 && p.Data.Powers != null && p.Data.Rooftops != null && p.Data.ModeRecords != null && p.Data.SeenHints != null,
            $"Save without Powers / Rooftops lists keeps its level 4 (lists created{(p.Repairs.Count > 0 ? ": " + string.Join(", ", p.Repairs) : " by JsonUtility defaults")}).");
        Done(p);
    }
    static void Duplicates()
    {
        Log("---- DUPLICATES");
        string json = "{\"Version\":1,\"Level\":2,\"Xp\":0,\"Points\":0,\"Powers\":[{\"Id\":\"strength\",\"Tier\":0},{\"Id\":\"strength\",\"Tier\":1},{\"Id\":\"\",\"Tier\":4}]," +
            "\"Rooftops\":[\"roof-a\",\"roof-a\",\"roof-b\"],\"SeenHints\":[\"move\",\"move\",\"\"],\"ModeRecords\":[{\"Id\":\"endless-fight\",\"BestScore\":100,\"BestWave\":3,\"Runs\":2},{\"Id\":\"endless-fight\",\"BestScore\":250,\"BestWave\":2,\"Runs\":5}]," +
            "\"PowerStats\":[{\"Id\":\"ice\",\"Uses\":4,\"Hits\":3,\"Kills\":-2,\"Sessions\":1,\"Damage\":40.5},{\"Id\":\"ice\",\"Uses\":9,\"Hits\":1,\"Kills\":1,\"Sessions\":2,\"Damage\":-3},{\"Id\":\"\",\"Uses\":1}]," +
            "\"Challenges\":[{\"Id\":\"first-blood\",\"Progress\":1,\"Completed\":false,\"Paid\":true},{\"Id\":\"first-blood\",\"Progress\":0,\"Completed\":false,\"Paid\":false},{\"Id\":\"removed-challenge\",\"Progress\":9,\"Completed\":false,\"Paid\":false},{\"Id\":\"neg\",\"Progress\":-5}]}";
        var p = Load(Write("duplicates", json));
        var strength = p.Data.Powers.Where(x => x.Id == "strength").ToList(); var record = p.Record("endless-fight");
        Check(strength.Count == 1 && strength[0].Tier == 1 && !p.Data.Powers.Any(x => string.IsNullOrEmpty(x.Id)), "Duplicate power entries merged (highest tier kept), empty ids dropped.");
        Check(p.Data.Rooftops.Count == 2 && p.Data.SeenHints.SequenceEqual(new[] { "move" }), "Rooftops and hints de-duplicated.");
        Check(p.Data.ModeRecords.Count(r => r.Id == "endless-fight") == 1 && record.BestScore == 250 && record.BestWave == 3 && record.Runs == 5, "Duplicate mode records merged (best of each field).");
        var ice = p.Data.PowerStats.Where(u => u.Id == "ice").ToList();
        Check(ice.Count == 1 && ice[0].Uses == 9 && ice[0].Hits == 3 && ice[0].Kills == 1 && ice[0].Sessions == 2 && Mathf.Abs(ice[0].Damage - 40.5f) < .01f && !p.Data.PowerStats.Any(u => string.IsNullOrEmpty(u.Id)),
            "Per-power stats: duplicates merged field by field (max), negative values clamped to 0, empty ids dropped.");
        var fb = p.Data.Challenges.Where(c => c.Id == "first-blood").ToList(); var removed = p.Challenge("removed-challenge");
        Check(fb.Count == 1 && fb[0].Paid && fb[0].Completed && fb[0].Progress == 1 && removed != null && removed.Progress == 9 && !removed.Paid && p.Challenge("neg").Progress == 0,
            "Challenges: duplicate entries merged keeping PAID (so it can never pay twice), paid implies completed, a removed challenge id kept untouched, negative progress clamped.");
        Log("Repairs: " + string.Join("; ", p.Repairs)); Done(p);
    }
    static void UnknownIds()
    {
        Log("---- UNKNOWN / REMOVED IDS");
        string json = "{\"Version\":1,\"Level\":2,\"Xp\":0,\"Points\":0,\"Powers\":[{\"Id\":\"strength\",\"Tier\":0},{\"Id\":\"removed-power\",\"Tier\":3}],\"Rooftops\":[]," +
            "\"ModeRecords\":[{\"Id\":\"removed-mode\",\"BestScore\":77,\"BestWave\":0,\"Runs\":1}],\"Loadout\":{\"HeroId\":\"removed-hero\",\"PowerA\":\"removed-power\",\"PowerB\":\"strength\",\"Primary\":0,\"Secondary\":0}}";
        var p = Load(Write("unknown", json));
        var forge = Resources.Load<ForgeCatalog>("ForgeCatalog");
        Check(p.LastError == null && p.Data.Powers.Any(x => x.Id == "removed-power" && x.Tier == 3) && p.BestScore("removed-mode") == 77, "Unknown power / mode ids are kept untouched (forward compatible).");
        Check(forge.Heroes.Any(h => h.Id == p.Data.Loadout.HeroId) && Powers.Any(d => d.Id == p.Data.Loadout.PowerA) && p.Data.Loadout.PowerA != p.Data.Loadout.PowerB,
            $"A loadout naming a removed hero / power is validated to a real one ({p.Data.Loadout.HeroId}: {p.Data.Loadout.PowerA} + {p.Data.Loadout.PowerB}).");
        Done(p);
        // A saved pair whose old synergy was deleted (ice + strength was Glacier Fist) resolves to no synergy or a shipping one.
        var shipping = new[] { "sonic-slam", "thermal-shock", "solar-flare", "void-grasp", "eclipse-beam" };
        var ice = Powers.First(d => d.Id == "ice"); var str = Powers.First(d => d.Id == "strength"); var resolved = forge.Resolve(ice, str);
        Check(resolved == null || shipping.Contains(resolved.Id), $"Removed synergy pair ice + strength resolves to {(resolved == null ? "no synergy" : resolved.Id)} (never a deleted synergy).");
    }
    static void Corrupt()
    {
        Log("---- CORRUPT / TRUNCATED");
        string path = Fresh("corrupt"); string truncated = Save(6).Substring(0, 30);
        File.WriteAllText(path, truncated); File.WriteAllText(path + ".bak", Save(7, 3));
        var p = Load(path);
        Check(p.LastError != null && p.RecoveredFrom == path + ".bak" && p.Data.Level == 7 && p.Data.Xp == 3, $"Truncated save + readable .bak: recovered level 7 from .bak (error kept for the HUD: {p.LastError}).");
        Check(File.Exists(path + ".corrupt") && File.ReadAllText(path + ".corrupt") == truncated, "The unreadable file is preserved as .corrupt before anything overwrites it.");
        p.Save(); Done(p);
        var again = Load(path);
        Check(again.LastError == null && again.RecoveredFrom == null && again.Data.Level == 7, "After the next save the main file is valid again (level 7, no error).");
        Check(File.Exists(path + ".bak") && File.ReadAllText(path + ".bak") == Save(7, 3), "The good .bak was NOT overwritten by the corrupt file (the corrupt save was removed after being preserved).");
        Done(again);
        string alone = Fresh("corrupt-alone"); File.WriteAllText(alone, "{ not json");
        var q = Load(alone);
        Check(q.LastError != null && q.RecoveredFrom == null && q.Data.Level == 1 && File.Exists(alone + ".corrupt"), "CONTROL: unreadable save with no backup -> fresh progression in memory, file preserved as .corrupt.");
        Done(q);
        string empty = Write("empty", "");
        var r = Load(empty); Check(r.Data.Level == 1 && r.LastError != null, "Empty (0-byte) save -> treated as unreadable, fresh progression."); Done(r);
    }
    static void Temp()
    {
        Log("---- INTERRUPTED TEMP FILES");
        string path = Fresh("temp"); File.WriteAllText(path, Save(5)); File.WriteAllText(path + ".tmp", Save(9));
        var p = Load(path);
        Check(p.Data.Level == 5 && p.RecoveredFrom == null, "A leftover .tmp beside a readable save is ignored (the save is authoritative).");
        p.Save(); Check(!File.Exists(path + ".tmp") && File.Exists(path + ".bak"), "The next save consumes / replaces the stale .tmp."); Done(p);
        string first = Fresh("temp-first"); File.WriteAllText(first + ".tmp", Save(9));
        var q = Load(first);
        Check(q.Data.Level == 9 && q.RecoveredFrom == first + ".tmp", "Interrupted FIRST save (only a complete .tmp exists) is recovered."); Done(q);
        string both = Fresh("temp-both"); File.WriteAllText(both + ".bak", Save(4)); File.WriteAllText(both + ".tmp", Save(8));
        File.SetLastWriteTimeUtc(both + ".bak", DateTime.UtcNow.AddMinutes(-10)); File.SetLastWriteTimeUtc(both + ".tmp", DateTime.UtcNow);
        var r = Load(both);
        Check(r.Data.Level == 8 && r.RecoveredFrom == both + ".tmp", "Save missing, .bak and .tmp both readable: the newer (.tmp) wins."); Done(r);
        string torn = Fresh("temp-torn"); File.WriteAllText(torn + ".bak", Save(4)); File.WriteAllText(torn + ".tmp", Save(8).Substring(0, 20));
        var s = Load(torn);
        Check(s.Data.Level == 4 && s.RecoveredFrom == torn + ".bak", "Torn .tmp is skipped; the readable .bak is used."); Done(s);
    }
    static void Values()
    {
        Log("---- LARGE AND NEGATIVE VALUES");
        var p = Load(Write("huge", Save(int.MaxValue, int.MaxValue, int.MaxValue)));
        Check(p.Data.Level == PlayerProgression.MaxLevel && p.RequiredXp > 0, $"Level {int.MaxValue} clamped to {PlayerProgression.MaxLevel}; RequiredXp {p.RequiredXp} stays positive.");
        var watch = Stopwatch.StartNew(); p.AddXp(1000);
        Check(watch.ElapsedMilliseconds < 200 && p.Data.Level == PlayerProgression.MaxLevel && p.Data.Xp >= 0 && p.Data.Points >= 0, $"AddXp at the cap returns in {watch.ElapsedMilliseconds} ms with no overflow (xp {p.Data.Xp}, points {p.Data.Points}).");
        Done(p);
        var big = Load(Write("big-xp", Save(1, int.MaxValue - 5, int.MaxValue - 1)));
        watch.Restart(); big.AddXp(100);
        Check(watch.ElapsedMilliseconds < 2000 && big.Data.Xp >= 0 && big.Data.Points >= 0 && big.Data.Level <= PlayerProgression.MaxLevel, $"Near-max XP and points: a grant levels up without wrapping negative (level {big.Data.Level}, xp {big.Data.Xp}, points {big.Data.Points}, {watch.ElapsedMilliseconds} ms).");
        Done(big);
        var counters = Load(Write("counters", Save(2, 0, 0, ",\"SessionsPlayed\":-4,\"SessionsWon\":9,\"BestSessionScore\":-1")));
        Check(counters.LastError == null && counters.Data.SessionsPlayed == 0 && counters.Data.SessionsWon == 0 && counters.Data.BestSessionScore == 0, "Negative / inconsistent counters are clamped (played 0, won 0, best 0).");
        Done(counters);
        string negative = Fresh("negative"); File.WriteAllText(negative, Save(-3)); File.WriteAllText(negative + ".bak", Save(2));
        var n = Load(negative);
        Check(n.LastError != null && n.Data.Level == 2 && n.RecoveredFrom == negative + ".bak", "A negative level makes the save invalid (unchanged policy); the .bak is used."); Done(n);
    }
    static void Policy()
    {
        Log("---- VERSION POLICY (unchanged)");
        var p = Load(Write("version2", "{\"Version\":2,\"Level\":9}"));
        Check(p.LastError != null && p.Data.Level == 1, "CONTROL: a Version 2 save is still rejected."); Done(p);
    }
    static void Idempotent()
    {
        Log("---- REPAIR IS IDEMPOTENT + SEPARATE-PROCESS RELOAD");
        string json = "{\"Version\":1,\"Level\":3,\"Xp\":12,\"Points\":4,\"Powers\":[{\"Id\":\"strength\",\"Tier\":1},{\"Id\":\"strength\",\"Tier\":0}],\"Rooftops\":[\"roof-a\",\"roof-a\"]}";
        string path = Write("roundtrip", json);
        var p = Load(path); int repairs = p.Repairs.Count; p.Save(); Done(p);
        var again = Load(path);
        Check(repairs > 0 && again.Repairs.Count == 0 && again.Data.Level == 3 && again.Data.Rooftops.Count == 1 && again.Data.Powers.Count(x => x.Id == "strength") == 1, $"{repairs} repairs saved; reloading the repaired file needs none.");
        Done(again);
        File.WriteAllText(Folder + "reload-pointer.txt", path);
        Log("Reload pointer written: " + path);
    }
    static void ReloadChecks()
    {
        string path = File.ReadAllText(Folder + "reload-pointer.txt").Trim();
        Check(path.StartsWith(Saves), "Reload path is inside the sandbox: " + path);
        var p = Load(path);
        Check(p.LastError == null && p.Repairs.Count == 0 && p.Data.Level == 3 && p.Data.Xp == 12 && p.Data.Points == 4 && p.Data.Rooftops.Count == 1 && p.Tier(Powers.First(d => d.Id == "strength")) == 1,
            "Separate process: the repaired save reads back identically (level 3, xp 12, points 4, strength tier 1, 1 rooftop, no repairs).");
        Done(p);
    }
}
