using UnityEditor;
using UnityEngine;

/// City lighting preset through the editor (no hand-written .asset). "Create missing" makes Resources/VisualPreset.asset
/// DISABLED (the city lights exactly as before). "Enable" / "Disable" are the explicit content switch; judge it with
/// PolishCapture (Before = off, After = on) before enabling for everyone.
/// Batch: -executeMethod VisualPresetSetup.Batch (create only)
public static class VisualPresetSetup
{
    const string Path = "Assets/Resources/VisualPreset.asset";
    [MenuItem("Overpowered/Visual/Create missing visual preset")]
    public static void Create()
    {
        var p = AssetDatabase.LoadAssetAtPath<VisualPreset>(Path);
        if (p == null) { p = ScriptableObject.CreateInstance<VisualPreset>(); p.Enabled = false; AssetDatabase.CreateAsset(p, Path); }
        // Create-missing at field level: suggested role looks only when the list is still empty (inspector tuning is kept).
        if (p.EnemyLooks == null || p.EnemyLooks.Length == 0)
        {
            VisualPreset.EnemyLook L(string name, Vector3 silhouette, CityColor accent) => new VisualPreset.EnemyLook { Archetype = AssetDatabase.LoadAssetAtPath<EnemyArchetype>("Assets/Resources/Enemies/" + name + ".asset"), Silhouette = silhouette, OverrideAccent = true, Accent = accent };
            p.EnemyLooks = new[]
            {
                L("Rusher", new Vector3(.9f, 1.05f, .9f), CityColor.Amber),     // lean, light, hot accent
                L("Gunner", new Vector3(1f, 1f, 1f), CityColor.UiInk),          // was hero Cyan: hostile ranged role in off-white
                L("Brute", new Vector3(1.22f, .96f, 1.18f), CityColor.Metal),   // wide, heavy, near-black
            };
            p.EnemyLooks = System.Array.FindAll(p.EnemyLooks, l => l.Archetype != null);
            EditorUtility.SetDirty(p);
        }
        AssetDatabase.SaveAssets();
    }
    [MenuItem("Overpowered/Visual/Enable visual preset (content switch)")]
    public static void Enable() => Set(true);
    [MenuItem("Overpowered/Visual/Disable visual preset")]
    public static void Disable() => Set(false);
    static void Set(bool on) { Create(); var p = AssetDatabase.LoadAssetAtPath<VisualPreset>(Path); p.Enabled = on; EditorUtility.SetDirty(p); AssetDatabase.SaveAssets(); Debug.Log("Visual preset " + (on ? "ENABLED" : "disabled")); }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
}
