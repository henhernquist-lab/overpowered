using UnityEngine;
using UnityEngine.Rendering;

/// City lighting look (Visual polish, audit items 1 + 2). OPTIONAL and OFF by default: without Resources/VisualPreset, or
/// with Enabled false, the city lights exactly as before (shadowless white sun at 1.2, scene skybox ambient, haze skirt).
/// When on it applies, for the city session only (VisualPresetApplier restores Quality/RenderSettings on teardown):
///   - the sun casts soft shadows (a Light added from code defaults to LightShadows.None, so the city never had any),
///     warm cream colour, lower intensity, shadow distance / cascades capped for performance (detail pieces already
///     never cast: CityArt.FlushBatches);
///   - gradient (trilight) ambient instead of the default skybox's bright blue fill that bleaches up-facing surfaces;
///   - a procedural skybox whose ground half is the fog Haze, so the haze-skirt walls (visible as flat slabs in the sky
///     from the park and quay) can be hidden.
/// Colours come from the shared CityPalette. Tune in Resources/VisualPreset (created by Overpowered > Visual > menus).
[CreateAssetMenu(menuName = "Overpowered/Visual preset")]
public sealed class VisualPreset : ScriptableObject
{
    public bool Enabled;
    [Header("Sun")]
    public CityColor SunColor = CityColor.Cream;
    [Range(0f, 1f)] public float SunWhiteBlend = .55f;
    public float SunIntensity = 1.05f;
    public Vector3 SunEuler = new Vector3(50f, -30f, 0f);
    public LightShadows Shadows = LightShadows.Soft;
    [Range(0f, 1f)] public float ShadowStrength = .55f;
    public float ShadowDistance = 80f;
    [Tooltip("1, 2 or 4.")] public int ShadowCascades = 2;
    [Header("Ambient (trilight)")]
    public CityColor AmbientSky = CityColor.Slate, AmbientEquator = CityColor.UiPurple, AmbientGround = CityColor.Metal;
    [Range(0f, 2f)] public float AmbientSkyScale = 1.25f, AmbientEquatorScale = 1.2f, AmbientGroundScale = 1.1f;
    [Header("Sky / horizon")]
    public bool ProceduralSky = true;
    public CityColor SkyTint = CityColor.Haze, SkyGround = CityColor.Haze;
    public float SkyExposure = 1.15f, SkyAtmosphere = .75f, SunSize = .025f;
    [Tooltip("Hide the haze-skirt walls (only when ProceduralSky paints the lower sky in the fog colour).")] public bool HideHazeSkirt = true;

    /// Verification / capture override for this process: null = the asset's Enabled flag.
    public static bool? ForceEnabled;
    public static VisualPreset Current
    {
        get
        {
            var p = Resources.Load<VisualPreset>("VisualPreset");
            if (ForceEnabled == true && p == null) { p = CreateInstance<VisualPreset>(); p.hideFlags = HideFlags.HideAndDontSave; }
            return p;
        }
    }
    public static bool Active(VisualPreset p) => p != null && (ForceEnabled ?? p.Enabled);
    /// Called once by PrototypeBootstrap after the sun and camera exist. No-op when inactive.
    public static void ApplyTo(Light sun, CityDistrict city)
    {
        var p = Current; if (!Active(p) || sun == null) return;
        sun.gameObject.AddComponent<VisualPresetApplier>().Apply(p, sun, city);
    }
}

/// Applies a VisualPreset and puts every global it touched back when the city session ends.
public sealed class VisualPresetApplier : MonoBehaviour
{
    float shadowDistance; int cascades; AmbientMode ambientMode; Color sky, equator, ground; Material skybox; bool saved;
    Material created; readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();
    public VisualPreset Preset { get; private set; }
    public void Apply(VisualPreset p, Light sun, CityDistrict city)
    {
        Preset = p; var palette = CityMaterials.Current != null ? CityMaterials.Current.Palette : Resources.Load<CityPalette>("CityPalette");
        Color C(CityColor c) => palette != null ? palette.Colors[(int)c] : Color.gray;
        shadowDistance = QualitySettings.shadowDistance; cascades = QualitySettings.shadowCascades;
        ambientMode = RenderSettings.ambientMode; sky = RenderSettings.ambientSkyColor; equator = RenderSettings.ambientEquatorColor; ground = RenderSettings.ambientGroundColor; skybox = RenderSettings.skybox; saved = true;

        sun.color = Color.Lerp(C(p.SunColor), Color.white, p.SunWhiteBlend); sun.intensity = p.SunIntensity; sun.transform.rotation = Quaternion.Euler(p.SunEuler);
        sun.shadows = p.Shadows; sun.shadowStrength = p.ShadowStrength;
        QualitySettings.shadowDistance = p.ShadowDistance; QualitySettings.shadowCascades = p.ShadowCascades;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = C(p.AmbientSky) * p.AmbientSkyScale;
        RenderSettings.ambientEquatorColor = C(p.AmbientEquator) * p.AmbientEquatorScale;
        RenderSettings.ambientGroundColor = C(p.AmbientGround) * p.AmbientGroundScale;

        if (p.ProceduralSky)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader != null)
            {
                created = new Material(shader) { name = "Visual preset sky" };
                created.SetColor("_SkyTint", C(p.SkyTint)); created.SetColor("_GroundColor", C(p.SkyGround));
                created.SetFloat("_Exposure", p.SkyExposure); created.SetFloat("_AtmosphereThickness", p.SkyAtmosphere); created.SetFloat("_SunSize", p.SunSize);
                RenderSettings.skybox = created; RenderSettings.sun = sun;
                if (p.HideHazeSkirt && city != null && city.Art != null && city.Art.BackdropRoot != null)
                    foreach (var r in city.Art.BackdropRoot.GetComponentsInChildren<Renderer>())
                        if (r.gameObject.name == CityArt.HazeSkirtName && r.enabled) { r.enabled = false; hidden.Add(r); }
            }
        }
    }
    void OnDestroy()
    {
        if (!saved) return;
        QualitySettings.shadowDistance = shadowDistance; QualitySettings.shadowCascades = cascades;
        RenderSettings.ambientMode = ambientMode; RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
        RenderSettings.skybox = skybox;
        foreach (var r in hidden) if (r != null) r.enabled = true;
        if (created != null) Destroy(created);
    }
}
