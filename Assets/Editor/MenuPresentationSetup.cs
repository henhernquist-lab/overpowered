using UnityEditor;
using UnityEngine;

public static class MenuPresentationSetup
{
    [MenuItem("Overpowered/Create menu presentation data")]
    public static void Create()
    {
        const string path="Assets/Resources/MenuPresentationTuning.asset";
        if(AssetDatabase.LoadAssetAtPath<MenuPresentationTuning>(path)==null)AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<MenuPresentationTuning>(),path);
        foreach(var power in Resources.LoadAll<PowerDefinition>("Powers"))
        {
            if(power.Effect is FlightEffect)power.MenuIcon=MenuGlyph.Wing;
            else if(power.Effect is PunchEffect)power.MenuIcon=MenuGlyph.Fist;
            else if(power.Effect is FireBlastEffect)power.MenuIcon=MenuGlyph.Flame;
            else if(power.Effect is IceEffect)power.MenuIcon=MenuGlyph.Crystal;
            else if(power.Effect is TelekinesisEffect)power.MenuIcon=MenuGlyph.Orbit;
            EditorUtility.SetDirty(power);
        }
        AssetDatabase.SaveAssets();
    }
    public static void CreateBatch(){Create();EditorApplication.Exit(0);}
}
