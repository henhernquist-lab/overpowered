using System.Collections.Generic;
using UnityEngine;

public enum CityColor { Road, Pavement, Cream, Brick, Sand, Teal, Slate, Roof, Glass, Amber, Metal, Wood, Leaf, Red, Blue, Cyan, Fire, UiNavy, UiPanel, UiMuted, UiInk, HeroAccent, VillainAccent, UiPurple }
[CreateAssetMenu(menuName="Overpowered/City Palette")]
public sealed class CityPalette : ScriptableObject
{
    // The only color source for generated materials. Indices match CityColor (keep their order stable).
    public Color[] Colors={new Color(.10f,.15f,.21f),new Color(.45f,.53f,.56f),new Color(.92f,.87f,.71f),
        new Color(.65f,.29f,.23f),new Color(.79f,.63f,.40f),new Color(.19f,.48f,.47f),new Color(.30f,.39f,.52f),
        new Color(.22f,.29f,.34f),new Color(.08f,.23f,.32f),new Color(1f,.70f,.26f),new Color(.19f,.23f,.28f),
        new Color(.43f,.27f,.19f),new Color(.34f,.53f,.31f),new Color(.80f,.23f,.22f),new Color(.13f,.36f,.72f),
        new Color(.31f,.86f,.80f),new Color(1f,.39f,.12f),
        new Color(.045f,.055f,.14f),new Color(.095f,.105f,.23f),new Color(.61f,.67f,.80f),new Color(.96f,.96f,.91f),
        new Color(.12f,.78f,1f),new Color(1f,.29f,.16f),new Color(.24f,.15f,.40f)};
    [Range(0,1)] public float Smoothness=.08f;
}

// Materials are shared, owned by the generated city and updated live from the palette asset.
public sealed class CityMaterials : MonoBehaviour
{
    public static CityMaterials Current {get;private set;}
    public CityPalette Palette {get;private set;}
    readonly Dictionary<CityColor,Material> materials=new Dictionary<CityColor,Material>();
    public IEnumerable<Material> All=>materials.Values;
    public void Initialize(CityPalette palette) {Current=this;Palette=palette;}
    public static Material Get(CityColor color)
    {
        if(Current==null)throw new System.InvalidOperationException("City palette must be initialized before geometry.");
        if(!Current.materials.TryGetValue(color,out var mat))
        {
            mat=new Material(Shader.Find("Standard")){name="Palette/"+color,enableInstancing=true};
            Current.materials.Add(color,mat);Current.Apply();
        }
        return mat;
    }
    public void Apply()
    {
        foreach(var pair in materials)
        {pair.Value.color=Palette.Colors[(int)pair.Key];pair.Value.SetFloat("_Glossiness",Palette.Smoothness);}
    }
    void LateUpdate(){Apply();}
    void OnDestroy(){foreach(var mat in materials.Values)Destroy(mat);if(Current==this)Current=null;}
}
