using System.IO;
using UnityEditor;
using UnityEngine;

/// District gameplay data through the editor (no hand-written .asset files).
/// 1) "Create missing district profiles" (safe, idempotent, never overwrites): Resources/Districts/<name>.asset for the four
///    districts and Resources/DistrictProfiles.asset mapping CityLayout district names to them, created DISABLED (every
///    district plays exactly as before).
/// 2) "Enable district profiles" (explicit content switch, after DistrictProfileVerification passes).
/// The values below are suggestions for Henry's playtest, not measured tuning.
/// Batch: -executeMethod DistrictProfileSetup.Batch (step 1 only)
public static class DistrictProfileSetup
{
    const string Folder = "Assets/Resources/Districts/", SetPath = "Assets/Resources/DistrictProfiles.asset";
    [MenuItem("Overpowered/Districts/Create missing district profiles")]
    public static void Create()
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var downtown = Profile("downtown", p => { p.CivilianDensity = 1.3f; p.PoliceResponse = 1.3f; p.HeatResponse = 1.2f; p.DestructionReward = 1.2f; p.DifficultyOffset = 1;
            p.MissionTags = new[] { "urban", "corporate", "highrise" }; p.ActivityTags = new[] { "rooftops", "avenues" }; p.CategoryWeights = new[] { W("heist", 1.5f), W("emergency", 1.3f) }; });
        var park = Profile("park", p => { p.CivilianDensity = 1.4f; p.PoliceResponse = .8f; p.HeatResponse = .9f; p.DestructionReward = .8f;
            p.MissionTags = new[] { "open", "crowd", "event" }; p.ActivityTags = new[] { "paths", "lawns" }; p.CategoryWeights = new[] { W("defence", 1.5f), W("chaos", 1.3f) }; });
        var residential = Profile("residential", p => { p.CivilianDensity = 1.1f; p.PoliceResponse = .9f; p.HeatResponse = 1.1f;
            p.MissionTags = new[] { "homes", "neighbourhood" }; p.ActivityTags = new[] { "yards", "canal" }; p.CategoryWeights = new[] { W("defence", 1.3f), W("break-in", 1.4f) }; });
        var docks = Profile("docks", p => { p.CivilianDensity = .6f; p.PoliceResponse = .8f; p.HeatResponse = .8f; p.DestructionReward = 1.4f; p.DifficultyOffset = 1;
            p.MissionTags = new[] { "cargo", "waterfront", "industrial" }; p.ActivityTags = new[] { "cranes", "quays" }; p.CategoryWeights = new[] { W("vehicle", 1.5f), W("heist", 1.2f) }; });
        if (AssetDatabase.LoadAssetAtPath<DistrictProfileSet>(SetPath) == null)
        {
            var set = ScriptableObject.CreateInstance<DistrictProfileSet>(); set.Enabled = false;
            set.Entries = new[] { E("Downtown", downtown), E("Park", park), E("Residential", residential), E("Docks", docks) };
            AssetDatabase.CreateAsset(set, SetPath);
        }
        AssetDatabase.SaveAssets();
    }
    [MenuItem("Overpowered/Districts/Enable district profiles (content switch)")]
    public static void Enable() { Create(); var set = AssetDatabase.LoadAssetAtPath<DistrictProfileSet>(SetPath); set.Enabled = true; EditorUtility.SetDirty(set); AssetDatabase.SaveAssets(); }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
    static DistrictGameplayProfile.CategoryWeight W(string category, float weight) => new DistrictGameplayProfile.CategoryWeight { Category = category, Weight = weight };
    static DistrictProfileSet.Entry E(string district, DistrictGameplayProfile p) => new DistrictProfileSet.Entry { District = district, Profile = p };
    static DistrictGameplayProfile Profile(string id, System.Action<DistrictGameplayProfile> set)
    {
        string path = Folder + id + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<DistrictGameplayProfile>(path); if (existing != null) return existing;
        var p = ScriptableObject.CreateInstance<DistrictGameplayProfile>(); set(p); AssetDatabase.CreateAsset(p, path); return p;
    }
}
