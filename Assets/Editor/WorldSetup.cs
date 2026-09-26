using System.Linq;
using UnityEditor;
using UnityEngine;

/// Brings existing CityLayout / CityArtSettings assets up to the district world: serializes the new world fields with their
/// defaults, appends any missing building styles and structure recipes by name, and selects BuildingMeshes static geometry.
/// Idempotent; never overwrites a style/recipe that already exists (designers' edits win).
/// Unity -batchmode -projectPath <project> -executeMethod WorldSetup.Apply -quit
public static class WorldSetup
{
    [MenuItem("Overpowered/World/Apply district world defaults to assets")]
    public static void Apply()
    {
        CityArtSetup.Create();
        var art = AssetDatabase.LoadAssetAtPath<CityArtSettings>("Assets/Resources/CityArtSettings.asset");
        var defaults = ScriptableObject.CreateInstance<CityArtSettings>();
        var styles = art.Styles.ToList(); int addedStyles = 0;
        foreach (var style in defaults.Styles) if (!styles.Any(s => s.Name == style.Name)) { styles.Add(style); addedStyles++; }
        art.Styles = styles.ToArray();
        int addedRecipes = 0;
        foreach (var recipe in defaults.Structures) if (!art.Structures.Any(r => r.Name == recipe.Name)) { art.Structures.Add(recipe); addedRecipes++; }
        art.StaticGeometry = StaticGeometryMode.BuildingMeshes;
        Object.DestroyImmediate(defaults);
        EditorUtility.SetDirty(art);
        var layout = AssetDatabase.LoadAssetAtPath<CityLayout>("Assets/Resources/CityLayout.asset");
        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WORLD SETUP] styles +{addedStyles} (now {art.Styles.Length}), recipes +{addedRecipes} (now {art.Structures.Count}), static geometry {art.StaticGeometry}, districts {layout.Districts.Count}");
    }
}
