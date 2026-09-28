using System.IO;
using UnityEditor;
using UnityEngine;

/// Challenge data through the editor (no hand-written .asset files).
/// 1) "Create missing challenges" (safe, idempotent, never overwrites): Resources/Challenges/<id>.asset for every recipe below
///    and Resources/ChallengeCatalog.asset listing them, created DISABLED (challenges stay dormant).
/// 2) "Enable challenges" (explicit content switch, after ChallengeVerification passes): catalog Enabled = true.
/// The asset name IS the stable id (never rename a shipped challenge). Rewards: XP and upgrade points only.
/// Batch: -executeMethod ChallengeSetup.Batch (step 1 only)
public static class ChallengeSetup
{
    public const string Folder = "Assets/Resources/Challenges/";
    [MenuItem("Overpowered/Challenges/Create missing challenges")]
    public static void Create()
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        Make("first-blood", "FIRST BLOOD", "Take down your first enemy.", ChallengeMetric.Kills, ChallengeScope.Lifetime, 1, 25);
        Make("street-sweeper", "STREET SWEEPER", "Take down 100 enemies.", ChallengeMetric.Kills, ChallengeScope.Lifetime, 100, 200, 1);
        Make("ice-age", "ICE AGE", "Take down 25 enemies with Ice.", ChallengeMetric.Kills, ChallengeScope.Lifetime, 25, 100, 0, "ice");
        Make("pyromaniac", "PYROMANIAC", "Take down 25 enemies with Fire Blast.", ChallengeMetric.Kills, ChallengeScope.Lifetime, 25, 100, 0, "fire");
        Make("heavy-hitter", "HEAVY HITTER", "Take down 50 enemies with Super Strength.", ChallengeMetric.Kills, ChallengeScope.Lifetime, 50, 150, 0, "strength");
        Make("storm-caller", "STORM CALLER", "Take down 25 enemies with Lightning.", ChallengeMetric.Kills, ChallengeScope.Lifetime, 25, 100, 0, "lightning");
        Make("synergist", "SYNERGIST", "Trigger your hero's synergy 10 times.", ChallengeMetric.SynergyUses, ChallengeScope.Lifetime, 10, 150, 1);
        Make("elite-hunter", "ELITE HUNTER", "Defeat 10 elite enemies in Endless Fight.", ChallengeMetric.EliteKills, ChallengeScope.Lifetime, 10, 150);
        Make("giant-slayer", "GIANT SLAYER", "Defeat 3 Endless minibosses.", ChallengeMetric.MinibossKills, ChallengeScope.Lifetime, 3, 150, 1);
        Make("wave-rider", "WAVE RIDER", "Clear Endless wave 10.", ChallengeMetric.EndlessWave, ChallengeScope.Lifetime, 10, 250, 1);
        Make("untouchable", "UNTOUCHABLE", "Clear 3 Endless waves without taking damage in one run.", ChallengeMetric.FlawlessWaves, ChallengeScope.SingleSession, 3, 200);
        Make("showboat", "SHOWBOAT", "Reach a style total of 1500 in one session.", ChallengeMetric.SessionStyle, ChallengeScope.SingleSession, 1500, 150);
        Make("versatile", "VERSATILE", "Land hits with both equipped powers in one session.", ChallengeMetric.DistinctPowersInSession, ChallengeScope.SingleSession, 2, 50);
        Make("city-guardian", "CITY GUARDIAN", "Complete 10 missions as a hero.", ChallengeMetric.MissionsCompleted, ChallengeScope.Lifetime, 10, 200, 1, null, ChallengeSide.Hero);
        Make("mastermind", "MASTERMIND", "Complete 10 jobs as a villain.", ChallengeMetric.MissionsCompleted, ChallengeScope.Lifetime, 10, 200, 1, null, ChallengeSide.Villain);
        const string catalogPath = "Assets/Resources/ChallengeCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<ChallengeCatalog>(catalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<ChallengeCatalog>(); catalog.Enabled = false; AssetDatabase.CreateAsset(catalog, catalogPath); }
        var all = new System.Collections.Generic.List<ChallengeDefinition>(catalog.Challenges ?? new ChallengeDefinition[0]);
        foreach (var guid in AssetDatabase.FindAssets("t:ChallengeDefinition", new[] { Folder.TrimEnd('/') }))
        { var c = AssetDatabase.LoadAssetAtPath<ChallengeDefinition>(AssetDatabase.GUIDToAssetPath(guid)); if (c != null && !all.Contains(c)) all.Add(c); }
        all.RemoveAll(c => c == null); all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); catalog.Challenges = all.ToArray(); EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
    }
    [MenuItem("Overpowered/Challenges/Enable challenges (content switch)")]
    public static void Enable() { Create(); var catalog = AssetDatabase.LoadAssetAtPath<ChallengeCatalog>("Assets/Resources/ChallengeCatalog.asset"); catalog.Enabled = true; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
    static void Make(string id, string title, string description, ChallengeMetric metric, ChallengeScope scope, int target, int xp, int points = 0, string parameter = null, ChallengeSide side = ChallengeSide.Any)
    {
        string path = Folder + id + ".asset";
        if (AssetDatabase.LoadAssetAtPath<ChallengeDefinition>(path) != null) return;
        var c = ScriptableObject.CreateInstance<ChallengeDefinition>();
        c.Id = id; c.Title = title; c.Description = description; c.Metric = metric; c.Scope = scope; c.Target = target;
        c.RewardXp = xp; c.RewardPoints = points; c.Parameter = parameter ?? ""; c.Side = side;
        AssetDatabase.CreateAsset(c, path);
    }
}
