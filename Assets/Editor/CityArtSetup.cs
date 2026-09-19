using UnityEditor;
using UnityEngine;

public static class CityArtSetup
{
    [MenuItem("Overpowered/Create missing city art assets")]
    public static void Create()
    {
        var palette=AssetDatabase.LoadAssetAtPath<CityPalette>("Assets/Resources/CityPalette.asset");
        if(palette==null){palette=ScriptableObject.CreateInstance<CityPalette>();AssetDatabase.CreateAsset(palette,"Assets/Resources/CityPalette.asset");}
        var settings=AssetDatabase.LoadAssetAtPath<CityArtSettings>("Assets/Resources/CityArtSettings.asset");
        if(settings==null){settings=ScriptableObject.CreateInstance<CityArtSettings>();settings.Palette=palette;AssetDatabase.CreateAsset(settings,"Assets/Resources/CityArtSettings.asset");}
        AssetDatabase.SaveAssets();AssetDatabase.Refresh();
    }
    [MenuItem("Overpowered/Bake current art placements to editable data")]
    public static void Bake()
    {
        Create();var settings=Resources.Load<CityArtSettings>("CityArtSettings");
        if(settings.UseAuthoredPlacements){Debug.LogWarning("Already using authored placements; disable that option explicitly before replacing them.");return;}
        Undo.RecordObject(settings,"Bake seeded art placements");
        var city=Resources.Load<GameTuning>("GameTuning").City;
        settings.AuthoredPlacements=settings.Generate(city,Resources.Load<CityLayout>("CityLayout").Generate(city));settings.UseAuthoredPlacements=true;
        EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
    }
}
