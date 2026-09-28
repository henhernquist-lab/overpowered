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
        if (AssetDatabase.LoadAssetAtPath<VisualPreset>(Path) != null) return;
        var p = ScriptableObject.CreateInstance<VisualPreset>(); p.Enabled = false;
        AssetDatabase.CreateAsset(p, Path); AssetDatabase.SaveAssets();
    }
    [MenuItem("Overpowered/Visual/Enable visual preset (content switch)")]
    public static void Enable() => Set(true);
    [MenuItem("Overpowered/Visual/Disable visual preset")]
    public static void Disable() => Set(false);
    static void Set(bool on) { Create(); var p = AssetDatabase.LoadAssetAtPath<VisualPreset>(Path); p.Enabled = on; EditorUtility.SetDirty(p); AssetDatabase.SaveAssets(); Debug.Log("Visual preset " + (on ? "ENABLED" : "disabled")); }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
}
