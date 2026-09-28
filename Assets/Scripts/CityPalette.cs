using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum CityColor { Road, Pavement, Cream, Brick, Sand, Teal, Slate, Roof, Glass, Amber, Metal, Wood, Leaf, Red, Blue, Cyan, Fire, UiNavy, UiPanel, UiMuted, UiInk, HeroAccent, VillainAccent, UiPurple, Water, Haze, Lawn, LitWindow, LampGlow, Neon, Beacon, DuskHaze, NightHaze }
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
        new Color(.12f,.78f,1f),new Color(1f,.29f,.16f),new Color(.24f,.15f,.40f),
        new Color(.13f,.37f,.47f),new Color(.66f,.74f,.82f),new Color(.42f,.64f,.34f),
        // Phase 6 lighting (appended): emissive variants keep their day colour (Amber, Amber, Cream, Red) and glow at night;
        // dusk/night fog.
        new Color(1f,.70f,.26f),new Color(1f,.70f,.26f),new Color(.92f,.87f,.71f),new Color(.80f,.23f,.22f),
        new Color(.62f,.50f,.52f),new Color(.075f,.095f,.17f)};
    [Range(0,1)] public float Smoothness=.08f;
    [Header("Emissive swatches (Phase 6 lighting)")]
    [Tooltip("Palette materials that glow. Emission = Colors[Emission] x Strength x the session's night glow (0 by day, so the day look is unchanged).")]
    public EmissiveSwatch[] Emissive={
        new EmissiveSwatch{Swatch=CityColor.LitWindow,Emission=CityColor.LitWindow,Strength=1f},
        new EmissiveSwatch{Swatch=CityColor.LampGlow,Emission=CityColor.LampGlow,Strength=1.3f},
        new EmissiveSwatch{Swatch=CityColor.Neon,Emission=CityColor.Cyan,Strength=1.1f},
        new EmissiveSwatch{Swatch=CityColor.Beacon,Emission=CityColor.Beacon,Strength=1.4f},
        new EmissiveSwatch{Swatch=CityColor.Fire,Emission=CityColor.Fire,Strength=.6f},
        new EmissiveSwatch{Swatch=CityColor.Cyan,Emission=CityColor.Cyan,Strength=.35f}};
    public EmissiveSwatch EmissiveOf(CityColor color){if(Emissive!=null)foreach(var e in Emissive)if(e!=null&&e.Swatch==color)return e;return null;}
    [Header("Built-in toon presentation")]
    public bool ToonCity, ToonHeroes;
    [Range(0,1)] public float ToonShade=.65f, ToonDeepShade=.42f;
    [Range(0,1)] public float ToonStep=.5f, ToonFeather=.04f;
    [Range(0,2)] public float HeroOutline=.35f;

}

[System.Serializable] public sealed class EmissiveSwatch
{
    public CityColor Swatch;
    [Tooltip("Swatch whose colour the surface emits (may differ from its own day colour, e.g. cream sign glyphs glowing cyan).")] public CityColor Emission;
    [Min(0)] public float Strength=1;
}

// Materials are shared, owned by the generated city and updated live from the palette asset.
// Palette edits (inspector or code) are picked up by change detection; material properties are only
// written when a swatch or the smoothness actually differs from what was last applied.
public sealed class CityMaterials : MonoBehaviour
{
    public static CityMaterials Current {get;private set;}
    public CityPalette Palette {get;private set;}
    readonly Dictionary<CityColor,Material> materials=new Dictionary<CityColor,Material>();
    readonly Dictionary<CityColor,Material> vfxMaterials=new Dictionary<CityColor,Material>();
    readonly Dictionary<CityColor,Color> applied=new Dictionary<CityColor,Color>();
    float appliedSmoothness=float.NaN; int appliedToonHash, appliedEmissiveHash;
    /// Night glow 0..1 set by the session's WorldLightingDirector (0 = day: every emissive swatch emits black).
    public float Glow {get;private set;}
    int EmissiveHash
    {
        get { unchecked { int h=17; if(Palette.Emissive!=null)foreach(var e in Palette.Emissive)if(e!=null){h=h*31+(int)e.Swatch;h=h*31+(int)e.Emission;h=h*31+e.Strength.GetHashCode();h=h*31+Palette.Colors[(int)e.Emission].GetHashCode();} return h; } }
    }
    /// Sets the night glow and rewrites ONLY the emissive materials (a cycle calls this as the light changes).
    public void SetGlow(float glow)
    {
        glow=Mathf.Clamp01(glow);if(glow==Glow)return;Glow=glow;
        foreach(var pair in materials)Emission(pair.Value,pair.Key);
    }
    /// The emission colour a swatch's material currently has (black for non-emissive swatches).
    public Color EmissionOf(CityColor color)
    {
        var e=Palette.EmissiveOf(color);if(e==null)return Color.black;
        var c=Palette.Colors[(int)e.Emission]*(e.Strength*Glow);c.a=1;return c;
    }
    // Standard: the _EMISSION keyword stays ON for emissive swatches (black by day), so the shader variant never switches
    // mid-session; non-emissive swatches never get it. Toon keeps its own emissive colour property.
    void Emission(Material material,CityColor color)
    {
        if(Palette.EmissiveOf(color)==null)return;
        var c=EmissionOf(color);
        material.EnableKeyword("_EMISSION");material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        material.SetColor("_EmissionColor",c);if(material.HasProperty("_Emissive_Color"))material.SetColor("_Emissive_Color",c);
    }
    int ToonHash
    {
        get { unchecked { int h=Palette.ToonCity?1:0; h=h*31+(Palette.ToonHeroes?1:0); h=h*31+Palette.ToonShade.GetHashCode(); h=h*31+Palette.ToonDeepShade.GetHashCode(); h=h*31+Palette.ToonStep.GetHashCode(); h=h*31+Palette.ToonFeather.GetHashCode(); return h*31+Palette.HeroOutline.GetHashCode(); } }
    }
    public IEnumerable<Material> All=>materials.Values;
    // Sidekick suits: ONE material + colour map per (suit, primary, secondary), shared by every renderer that asks, rebuilt
    // in place when the palette changes, destroyed with this owner. Never per frame or per character.
    sealed class SuitEntry{public Material Material;public Texture2D Map;public Color Primary,Secondary,Trim;}
    readonly Dictionary<(SidekickSuit,CityColor,CityColor),SuitEntry> suits=new Dictionary<(SidekickSuit,CityColor,CityColor),SuitEntry>();
    public int SuitCount=>suits.Count;
    /// Palette materials plus the suit materials generated from palette colours (both owned and destroyed here).
    public IEnumerable<Material> Owned=>materials.Values.Concat(vfxMaterials.Values).Concat(suits.Values.Select(e=>e.Material));
    public static int SuitsCreated {get;private set;}
    public static Material Suit(SidekickSuit suit,CityColor primary,CityColor secondary)
    {
        if(Current==null)throw new System.InvalidOperationException("City palette must be initialized before geometry.");
        return Current.SuitMaterial(suit,primary,secondary);
    }
    public Material SuitMaterial(SidekickSuit suit,CityColor primary,CityColor secondary)
    {
        if(suits.TryGetValue((suit,primary,secondary),out var entry))return entry.Material;
        entry=new SuitEntry{Primary=Palette.Colors[(int)primary],Secondary=Palette.Colors[(int)secondary],Trim=Palette.Colors[(int)suit.Trim]};
        entry.Map=suit.Build(entry.Primary,entry.Secondary,entry.Trim);
        entry.Material=suit.CreateMaterial(entry.Map,$"Suit/{suit.name}/{primary}+{secondary}",Palette.Smoothness);
        if(!suit.AuthoredShader)Surface(entry.Material,Color.white,entry.Map,true);
        suits.Add((suit,primary,secondary),entry);SuitsCreated++;return entry.Material;
    }
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
    /// Shared unlit palette material for thin VFX geometry, where Standard's view-facing ribbon normals make colours muddy.
    /// At most one per swatch, cached, recoloured by Apply and destroyed with this owner. Call during pool setup, not per cast.
    public static Material GetVfx(CityColor color)
    {
        if(Current==null)throw new System.InvalidOperationException("City palette must be initialized before VFX.");
        if(!Current.vfxMaterials.TryGetValue(color,out var mat))
        {
            var shader=Shader.Find("Unlit/Color");
            // A player build can strip an unreferenced built-in shader: fall back to the lit shared swatch rather than fail the session.
            if(shader==null||!shader.isSupported){Debug.LogWarning("Unlit/Color unavailable; VFX swatch "+color+" uses the lit palette material.");return Get(color);}
            mat=new Material(shader){name="Palette/Vfx/"+color,enableInstancing=true,color=Current.Palette.Colors[(int)color]};
            Current.vfxMaterials.Add(color,mat);
        }
        return mat;
    }
    public void Apply()
    {
        foreach(var pair in materials)
        {var color=Palette.Colors[(int)pair.Key];Surface(pair.Value,color,null,false);Emission(pair.Value,pair.Key);applied[pair.Key]=color;}
        foreach(var pair in vfxMaterials)pair.Value.color=Palette.Colors[(int)pair.Key];
        appliedSmoothness=Palette.Smoothness; appliedToonHash=ToonHash; appliedEmissiveHash=EmissiveHash;
        foreach(var pair in suits)
        {
            var e=pair.Value;var (suit,primary,secondary)=pair.Key;
            e.Primary=Palette.Colors[(int)primary];e.Secondary=Palette.Colors[(int)secondary];e.Trim=Palette.Colors[(int)suit.Trim];suit.Write(e.Map,e.Primary,e.Secondary,e.Trim);
            if(!suit.AuthoredShader)Surface(e.Material,Color.white,e.Map,true);
        }
    }
    public void Surface(Material material,Color color,Texture map,bool hero)
    {
        bool toon=hero?Palette.ToonHeroes:Palette.ToonCity;
        var shader=Shader.Find(toon?"Toon/Toon":"Standard");
        if(shader==null||!shader.isSupported)throw new System.InvalidOperationException("Requested surface shader unavailable: "+(toon?"Toon/Toon":"Standard"));
        if(material.shader!=shader)material.shader=shader;
        material.color=color;material.SetTexture("_MainTex",map);
        if(!toon){material.SetFloat("_Glossiness",Palette.Smoothness);return;}
        material.SetColor("_BaseColor",color);material.SetColor("_Color",color);
        material.SetColor("_1st_ShadeColor",color*Palette.ToonShade);material.SetColor("_2nd_ShadeColor",color*Palette.ToonDeepShade);
        material.SetFloat("_Use_BaseAs1st",1);material.SetFloat("_Use_1stAs2nd",1);
        material.SetFloat("_BaseColor_Step",Palette.ToonStep);material.SetFloat("_BaseShade_Feather",Palette.ToonFeather);
        material.SetFloat("_ShadeColor_Step",.15f);material.SetFloat("_1st2nd_Shades_Feather",Palette.ToonFeather);
        material.SetFloat("_HighColor_Power",0);material.SetFloat("_RimLight",0);material.SetFloat("_MatCap",0);
        material.SetColor("_Outline_Color",Palette.Colors[(int)CityColor.UiNavy]);material.SetFloat("_Is_LightColor_Outline",0);
        material.SetFloat("_Outline_Width",hero?Palette.HeroOutline:0);material.EnableKeyword("_OUTLINE_NML");
        bool outline=hero&&Palette.HeroOutline>0;material.SetShaderPassEnabled("Outline",outline);
        if(outline)material.DisableKeyword("_DISABLE_OUTLINE");else material.EnableKeyword("_DISABLE_OUTLINE");
        material.EnableKeyword("UTS_RP_BUILTIN");
        material.EnableKeyword("_IS_CLIPPING_OFF");material.EnableKeyword("_EMISSIVE_SIMPLE");
    }
    bool Changed()
    {
        if(!appliedSmoothness.Equals(Palette.Smoothness)||appliedToonHash!=ToonHash||appliedEmissiveHash!=EmissiveHash)return true;
        foreach(var pair in materials)if(!applied.TryGetValue(pair.Key,out var color)||!color.Equals(Palette.Colors[(int)pair.Key]))return true;
        foreach(var pair in vfxMaterials)if(!pair.Value.color.Equals(Palette.Colors[(int)pair.Key]))return true;
        foreach(var pair in suits)
            if(!pair.Value.Primary.Equals(Palette.Colors[(int)pair.Key.Item2])||!pair.Value.Secondary.Equals(Palette.Colors[(int)pair.Key.Item3])||!pair.Value.Trim.Equals(Palette.Colors[(int)pair.Key.Item1.Trim]))return true;
        return false;
    }
    void LateUpdate(){if(Changed())Apply();}
    void OnDestroy()
    {
        foreach(var mat in materials.Values)Destroy(mat);
        foreach(var mat in vfxMaterials.Values)Destroy(mat);
        foreach(var e in suits.Values){Destroy(e.Material);Destroy(e.Map);}suits.Clear();
        if(Current==this)Current=null;
    }
}
