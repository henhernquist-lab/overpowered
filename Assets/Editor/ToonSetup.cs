using UnityEditor;
using UnityEngine;
public static class ToonSetup
{
    public static void Enable()=>Configure(true,true);
    public static void RetainStandard()=>Configure(false,false);
    static void Configure(bool city,bool heroes)
    {
        var palette=AssetDatabase.LoadAssetAtPath<CityPalette>("Assets/Resources/CityPalette.asset");
        var shader=Shader.Find("Toon/Toon");
        if(shader==null||!shader.isSupported)throw new System.Exception("Built-in Toon shader missing or unsupported");
        palette.ToonCity=city;palette.ToonHeroes=heroes;EditorUtility.SetDirty(palette);AssetDatabase.SaveAssets();
        Debug.Log($"TOON palette configured: city={city}, heroes={heroes}, hero outline={palette.HeroOutline}");
        EditorApplication.Exit(0);
    }
}
