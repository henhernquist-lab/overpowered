using UnityEditor;
using UnityEngine;

/// Heat response tiers through the editor. "Create missing Heat response profile" (safe, idempotent, never overwrites):
/// Resources/HeatResponse.asset with three suggested villain tiers, created DISABLED (police respond exactly as before).
/// "Enable Heat response tiers" is the explicit content switch. The numbers are suggestions for Henry's playtest.
/// Batch: -executeMethod HeatResponseSetup.Batch
public static class HeatResponseSetup
{
    const string Path = "Assets/Resources/HeatResponse.asset";
    [MenuItem("Overpowered/Heat/Create missing Heat response profile")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<HeatResponseProfile>(Path) != null) return;
        var p = ScriptableObject.CreateInstance<HeatResponseProfile>(); p.Enabled = false; p.ApplyToHero = false;
        var brute = AssetDatabase.LoadAssetAtPath<EnemyArchetype>("Assets/Resources/Enemies/Brute.asset");
        p.Tiers = new[]
        {
            new HeatResponseProfile.Tier { Label = "RESPONSE", MinStars = 2, ArrivalIntervalMultiplier = .8f, ResponseCountMultiplier = 1f, PursuedDecayMultiplier = .8f },
            new HeatResponseProfile.Tier { Label = "TACTICAL", MinStars = 3, ArrivalIntervalMultiplier = .65f, ResponseCountMultiplier = 1.25f, HostileArchetype = brute, ArchetypeShare = .25f, PursuedDecayMultiplier = .6f },
            new HeatResponseProfile.Tier { Label = "MANHUNT", MinStars = 5, ArrivalIntervalMultiplier = .5f, ResponseCountMultiplier = 1.5f, HostileArchetype = brute, ArchetypeShare = .35f, EliteHealthMultiplier = 1.5f, Persistent = true, PursuedDecayMultiplier = .4f, RoadblockEligible = true },
        };
        AssetDatabase.CreateAsset(p, Path); AssetDatabase.SaveAssets();
    }
    [MenuItem("Overpowered/Heat/Enable Heat response tiers (content switch)")]
    public static void Enable() { Create(); var p = AssetDatabase.LoadAssetAtPath<HeatResponseProfile>(Path); p.Enabled = true; EditorUtility.SetDirty(p); AssetDatabase.SaveAssets(); }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
}
