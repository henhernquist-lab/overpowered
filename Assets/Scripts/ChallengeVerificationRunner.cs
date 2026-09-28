#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// See ChallengeVerification. Challenge assets (stable kebab-case ids equal to their asset names, XP / points only), then
/// in-memory test challenges in a real session: parameter and side controls, progress below target pays nothing, the
/// target pays EXACTLY once (points and XP deltas, one "challenge" XP grant), no re-pay on further progress or a direct
/// second PayChallenge, a single-session best-value challenge, and a removed challenge id kept untouched and never paid.
/// Reload (a second Unity process): the saved state reads back identically, and further progress in a NEW session, even
/// with the target raised, never pays again.
public sealed class ChallengeVerificationRunner : SessionVerificationRunner
{
    public bool Reload;
    protected override string Folder => "Verification/Challenges/";
    protected override string ResultFile => Reload ? "reload.txt" : "results.txt";
    string Expected => Folder + "expected.txt";
    readonly List<XpGrant> grants = new List<XpGrant>();
    static ChallengeDefinition Def(string id, ChallengeMetric metric, ChallengeScope scope, int target, int xp, int points, string parameter = "", ChallengeSide side = ChallengeSide.Any)
    {
        var d = ScriptableObject.CreateInstance<ChallengeDefinition>(); d.name = id; d.Id = id; d.Metric = metric; d.Scope = scope; d.Target = target; d.RewardXp = xp; d.RewardPoints = points; d.Parameter = parameter; d.Side = side; return d;
    }
    List<ChallengeDefinition> Tests(int iceTarget) => new List<ChallengeDefinition>
    {
        Def("verify-ice-kills", ChallengeMetric.Kills, ChallengeScope.Lifetime, iceTarget, 50, 1, "ice"),
        Def("verify-session-powers", ChallengeMetric.DistinctPowersInSession, ChallengeScope.SingleSession, 2, 0, 2),
        Def("verify-villain-kills", ChallengeMetric.Kills, ChallengeScope.Lifetime, 1, 0, 5, "", ChallengeSide.Villain),
    };
    /// Kills grant XP and level-ups grant points too; expected points = before + reward + levels gained x PointsPerLevel.
    int Expect(int pointsBefore, int levelBefore, int reward) => pointsBefore + reward + (W.Progression.Data.Level - levelBefore) * Resources.Load<GameTuning>("GameTuning").Progression.PointsPerLevel;
    static string Line(ChallengeProgress c) => $"{c.Id} progress={c.Progress} completed={c.Completed} paid={c.Paid}";
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        if (Reload) { yield return ReadBack(); yield break; }
        Assets();
        yield return FirstRun();
    }
    void Assets()
    {
        Log("---- CHALLENGE ASSETS");
        var catalog = ChallengeCatalog.Current; var all = catalog != null ? catalog.Challenges.Where(c => c != null).ToArray() : new ChallengeDefinition[0];
        Check(catalog != null && all.Length >= 10 && all.Length == Resources.LoadAll<ChallengeDefinition>("Challenges").Length, $"Catalog lists all {all.Length} challenge assets (enabled: {catalog?.Enabled}; the setup creates it disabled).");
        var kebab = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");
        Check(all.All(c => kebab.IsMatch(c.Id) && c.Id == c.name), "Every id is kebab-case and equals its asset name (the stable save key).");
        Check(all.Select(c => c.Id).Distinct().Count() == all.Length, "No two challenges share an id.");
        Check(all.All(c => c.Target >= 1 && c.RewardXp >= 0 && c.RewardPoints >= 0 && c.RewardXp + c.RewardPoints > 0), "Targets >= 1; rewards are XP / upgrade points only, never negative, never empty.");
        foreach (var c in all.OrderBy(c => c.Id)) Log($"  {c.Id}: {c.Metric} {c.Scope} {(string.IsNullOrEmpty(c.Parameter) ? "" : "[" + c.Parameter + "] ")}{(c.Side != ChallengeSide.Any ? c.Side + " " : "")}target {c.Target} -> {c.RewardXp} XP, {c.RewardPoints} pt");
    }
    void Kill(string credit) { var npc = Actor(new Vector3(Random.Range(-20f, 20f), 150f, Random.Range(10f, 30f)), NpcRole.Criminal, 1f); using (W.Powers.Credit(credit)) npc.Damage(10f, W.Powers); }
    IEnumerator FirstRun()
    {
        Log("---- FIRST PROCESS");
        yield return Enter(F.Heroes[0], "ice", "strength", "hero");
        Isolate();
        var tracker = W.Mode.Challenges; var tests = Tests(3); tracker.UseDefinitions(tests);
        var p = W.Progression; grants.Clear(); p.XpGranted += grants.Add;
        p.Challenge("removed-challenge", true).Progress = 7;
        int points = p.Data.Points, level = p.Data.Level; var ice = tests[0];
        Kill("ice"); Kill("ice"); yield return null;
        Check(p.Challenge(ice.Id).Progress == 2 && !p.Challenge(ice.Id).Paid && p.Data.Points == Expect(points, level, 0) && !grants.Any(g => g.Reason == "challenge"), "Two ice kills: progress 2/3, nothing paid yet.");
        Kill("fire"); Kill("strength");
        Check(p.Challenge(ice.Id).Progress == 2, "CONTROL: kills credited to other powers do not count for the ice challenge.");
        Check(p.Challenge("verify-villain-kills") == null || p.Challenge("verify-villain-kills").Progress == 0, "CONTROL: a villain-only challenge does not progress on the hero side.");
        var session = tests[1];
        Check(p.Challenge(session.Id) != null && p.Challenge(session.Id).Paid && p.Data.Points == Expect(points, level, session.RewardPoints), $"Single-session best value: enemy hits from two different powers (ice, then fire) pay {session.RewardPoints} points once.");
        points = p.Data.Points; level = p.Data.Level;
        Kill("ice"); yield return null;
        var paidGrants = grants.Where(g => g.Reason == "challenge").ToList();
        Check(p.Challenge(ice.Id).Paid && p.Challenge(ice.Id).Completed && p.Data.Points == Expect(points, level, ice.RewardPoints) && paidGrants.Count == 1 && paidGrants[0].Amount == ice.RewardXp,
            $"Third ice kill completes it: exactly +{ice.RewardPoints} point (beyond level-up points) and one {ice.RewardXp} XP challenge grant.");
        points = p.Data.Points; level = p.Data.Level;
        Kill("ice"); Kill("ice");
        Check(!p.PayChallenge(ice) && p.Data.Points == Expect(points, level, 0) && grants.Count(g => g.Reason == "challenge") == 1, "No double pay: more progress and a direct second PayChallenge change nothing.");
        Check(p.Challenge("removed-challenge").Progress == 7 && !p.Challenge("removed-challenge").Paid, "A removed challenge id stays in the save untouched and is never paid.");
        p.XpGranted -= grants.Add;
        W.Mode.ReturnHome(); yield return Scene(GameFlow.HomeScene);
        var probe = Probe();
        File.WriteAllLines(Expected, new[] { $"points={probe.Data.Points} level={probe.Data.Level} xp={probe.Data.Xp}" }.Concat(probe.Data.Challenges.OrderBy(c => c.Id).Select(Line)));
        Log("Saved state for the reload:"); foreach (var l in File.ReadAllLines(Expected)) Log("  " + l);
        Check(probe.Challenge(ice.Id).Paid, "The paid flag is in the saved file.");
        Destroy(probe.gameObject);
    }
    PlayerProgression Probe()
    {
        var probe = new GameObject("Challenge probe").AddComponent<PlayerProgression>();
        probe.Initialize(Resources.Load<GameTuning>("GameTuning").Progression, Resources.LoadAll<PowerDefinition>("Powers"), WorldSession.VerificationSavePath);
        return probe;
    }
    IEnumerator ReadBack()
    {
        Log("---- SEPARATE-PROCESS RELOAD");
        var expected = File.ReadAllLines(Expected); var probe = Probe();
        var actual = new[] { $"points={probe.Data.Points} level={probe.Data.Level} xp={probe.Data.Xp}" }.Concat(probe.Data.Challenges.OrderBy(c => c.Id).Select(Line)).ToArray();
        Check(probe.LastError == null && expected.SequenceEqual(actual), "Reloaded challenge state, points, level and XP equal the saved ones:\n  " + string.Join("\n  ", actual));
        Destroy(probe.gameObject);
        yield return Enter(F.Heroes[0], "ice", "strength", "hero");
        Isolate();
        var tests = Tests(10); W.Mode.Challenges.UseDefinitions(tests);
        var p = W.Progression; int points = p.Data.Points, level = p.Data.Level; grants.Clear(); p.XpGranted += grants.Add;
        for (int i = 0; i < 10; i++) Kill("ice");
        yield return null;
        Check(p.Data.Points == Expect(points, level, 0) && !grants.Any(g => g.Reason == "challenge") && p.Challenge("verify-ice-kills").Paid, "New process, new session, target raised to 10 and 10 more ice kills: the already-paid challenge never pays again.");
        p.XpGranted -= grants.Add;
    }
}
#endif
