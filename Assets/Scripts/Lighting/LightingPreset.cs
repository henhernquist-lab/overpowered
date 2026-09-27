using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Lighting/Preset")]
public sealed class LightingPreset : ScriptableObject
{
    [Header("Directional light (sun / moon): rotation = Euler(Elevation, Azimuth, 0)")]
    public float Elevation=45;
    public float Azimuth=-35;
    public Color LightColor=Color.white;
    [Min(0)] public float Intensity=1.2f;
    [Header("Ambient = scene sky probe x AmbientTint + AmbientFloor")]
    public Color AmbientTint=Color.white;
    public Color AmbientFloor=Color.black;
    [Tooltip("RenderSettings.reflectionIntensity (the skybox reflection on the palette's low-smoothness surfaces).")] [Range(0,1)] public float Reflections=1;
    [Header("Fog (exponential, CityArtSettings.Rendering.FogDensity x FogDensityScale)")]
    public CityColor FogColor=CityColor.Haze;
    [Min(0)] public float FogDensityScale=1;
    [Header("Procedural sky (a session copy of the scene skybox)")]
    public Color SkyTint=new Color(.5f,.5f,.5f);
    public Color SkyGround=new Color(.369f,.349f,.341f);
    [Min(0)] public float SkyExposure=1.3f;
    [Range(0,5)] public float Atmosphere=1;
    [Range(0,1)] public float SunSize=.04f;
    [Header("City emission and street lights (0 = off / day, 1 = full night)")]
    [Range(0,1)] public float Glow;
    [Range(0,1)] public float StreetLights;
}
