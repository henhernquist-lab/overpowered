using System;
using UnityEngine;
using UnityEngine.Rendering;

/// One look of the world (Phase 6). Pure data, blended by LightingCycle. The ONE directional light is the sun by day and the
/// moon by night; ambient is the scene's own sky probe (the legacy day look) scaled per channel by AmbientTint plus a flat
/// AmbientFloor, so the Day preset (tint 1, floor 0) reproduces the pre-lighting look exactly. Fog colour is a CityPalette
/// swatch. Glow scales every emissive palette swatch (CityPalette.Emissive); StreetLights scales the capped real lamp lights.
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

[Serializable] public sealed class LightingKey
{
    [Range(0,1)] public float Time;
    public LightingPreset Preset;
}

/// A mode's time of day: presets on a 0..1 day, blended (smoothstep) between neighbouring keys. A static cycle
/// (DurationSeconds 0) holds StartTime for the whole session. Referenced from GameModeDefinition.Lighting.
[CreateAssetMenu(menuName="Overpowered/Lighting/Cycle")]
public sealed class LightingCycle : ScriptableObject
{
    [Tooltip("Presets by time of day (0..1), in ascending Time order.")] public LightingKey[] Keys=new LightingKey[0];
    [Tooltip("Real (scaled) seconds for time of day to run 0 -> 1. 0 = static at StartTime.")] [Min(0)] public float DurationSeconds;
    [Range(0,1)] public float StartTime;
    [Tooltip("True: 1 wraps to 0 (a repeating day). False: time holds at 1 (e.g. dusk that settles into night).")] public bool Loop=true;
    public bool Advances=>DurationSeconds>0;
    public float Advance(float time,float seconds)
    {
        if(!Advances)return time;
        time+=seconds/DurationSeconds;
        return Loop?Mathf.Repeat(time,1f):Mathf.Min(time,1f);
    }
    public float Normalize(float time)=>Loop?Mathf.Repeat(time,1f):Mathf.Clamp01(time);
    /// The two presets around `time` and the smoothstep weight of the second. A single key (or none after it) returns it twice.
    public void Evaluate(float time,out LightingPreset from,out LightingPreset to,out float weight)
    {
        from=to=null;weight=0;int n=Keys==null?0:Keys.Length;if(n==0)return;
        time=Normalize(time);
        if(n==1){from=to=Keys[0].Preset;return;}
        int next=0;while(next<n&&Keys[next].Time<=time)next++;
        int prev=next-1;
        if(prev<0){if(!Loop){from=to=Keys[0].Preset;return;}prev=n-1;}
        if(next>=n){if(!Loop){from=to=Keys[n-1].Preset;return;}next=0;}
        float a=Keys[prev].Time,b=Keys[next].Time,span=b-a,at=time-a;
        if(prev>=next){span+=1f;if(at<0)at+=1f;} // wrapping segment (last key -> first key of the next day)
        from=Keys[prev].Preset;to=Keys[next].Preset;weight=span<=0?1:Mathf.SmoothStep(0,1,Mathf.Clamp01(at/span));
        if(from==to)weight=0;
    }
}

[Serializable] public sealed class StreetLightSettings
{
    [Tooltip("Real point lights placed on the nearest intact, upright street lamps around the camera (0 = emissive lamp heads only). Scaled by the preset's StreetLights.")]
    [Range(0,8)] public int MaxLights=4;
    [Tooltip("Lamps farther than this from the camera never get a light.")] public float SearchRadius=45;
    [Tooltip("Lights fade from full at FadeStart to zero at SearchRadius, so re-assigning the nearest lamps never pops.")] public float FadeStart=26;
    public float Range=11, Intensity=1.7f;
    [Tooltip("Light height above the lamp base (the lamp head is at ~4 m).")] public float Height=3.6f;
    public float RefreshSeconds=.25f;
    public CityColor Color=CityColor.LampGlow;
    [Tooltip("Auto: Unity's per-object pixel light budget (QualitySettings.pixelLightCount) decides pixel vs vertex; ForceVertex never adds a pixel pass.")]
    public LightRenderMode RenderMode=LightRenderMode.Auto;
}

[Serializable] public sealed class PowerLightSettings
{
    [Tooltip("Hard cap: the pool owns exactly this many point lights for the whole session (0 = no power lights).")] [Range(0,8)] public int PoolSize=4;
    [Tooltip("A flash this close to a live flash of the same colour refreshes that light instead of taking a slot (chain Lightning).")] public float MergeDistance=3f;
    [Tooltip("Seconds a follow/hold light takes to fade out after its source ends.")] public float FadeOutSeconds=.12f;
    [Tooltip("A follow light (projectile) is released after this many seconds even if its source still exists.")] public float MaxFollowSeconds=4f;
    [Tooltip("A held light (channelled beam) fades when Hold was not called for this long.")] public float HoldGraceSeconds=.1f;
    [Tooltip("ForcePixel: always a pixel light (few, short-lived and capped by PoolSize). Auto lets pixelLightCount demote them.")]
    public LightRenderMode RenderMode=LightRenderMode.ForcePixel;
}

/// Per-power light (PowerDefinition.Light). Disabled by default: only powers whose data enables it request pooled lights.
[Serializable] public sealed class PowerLightSpec
{
    public bool Enabled;
    public CityColor Color=CityColor.Fire;
    [Min(0)] public float Intensity=3f;
    [Min(0)] public float Range=8f;
    [Tooltip("Flash lifetime; intensity eases to zero over it.")] [Min(.01f)] public float Seconds=.3f;
    [Tooltip("Intensity multiplier for a light that follows a moving source (projectile, beam end); 0 = no travel light.")] [Min(0)] public float Travel=.5f;
}

/// Session lighting configuration (Resources/WorldLighting.asset).
[CreateAssetMenu(menuName="Overpowered/Lighting/World Lighting")]
public sealed class WorldLighting : ScriptableObject
{
    [Tooltip("Used when the session has no mode, or its mode has no Lighting cycle (verification sandboxes).")] public LightingCycle DefaultCycle;
    [Tooltip("Copy the scene skybox once per session and drive it from the presets (false: the scene sky is left untouched).")] public bool DriveSky=true;
    [Tooltip("Ambient/emission are re-applied only when the blended value moved by more than this (a cycle changes slowly).")] public float ApplyEpsilon=.002f;
    public StreetLightSettings StreetLights=new StreetLightSettings();
    public PowerLightSettings PowerLights=new PowerLightSettings();
}

/// The blended values the director applies (one frame's lighting).
public struct LightingState
{
    public Quaternion Rotation; public Color LightColor; public float Intensity;
    public Color AmbientTint, AmbientFloor; public float Reflections;
    public Color Fog; public float FogScale;
    public Color SkyTint, SkyGround; public float SkyExposure, Atmosphere, SunSize;
    public float Glow, StreetLights;
    public static LightingState From(LightingPreset p,CityPalette palette)=>new LightingState
    {
        Rotation=Quaternion.Euler(p.Elevation,p.Azimuth,0),LightColor=p.LightColor,Intensity=p.Intensity,
        AmbientTint=p.AmbientTint,AmbientFloor=p.AmbientFloor,Reflections=p.Reflections,
        Fog=palette.Colors[(int)p.FogColor],FogScale=p.FogDensityScale,
        SkyTint=p.SkyTint,SkyGround=p.SkyGround,SkyExposure=p.SkyExposure,Atmosphere=p.Atmosphere,SunSize=p.SunSize,
        Glow=p.Glow,StreetLights=p.StreetLights
    };
    public static LightingState Lerp(LightingState a,LightingState b,float t)
    {
        if(t<=0)return a;if(t>=1)return b;
        return new LightingState
        {
            Rotation=Quaternion.Slerp(a.Rotation,b.Rotation,t),LightColor=Color.Lerp(a.LightColor,b.LightColor,t),Intensity=Mathf.Lerp(a.Intensity,b.Intensity,t),
            AmbientTint=Color.Lerp(a.AmbientTint,b.AmbientTint,t),AmbientFloor=Color.Lerp(a.AmbientFloor,b.AmbientFloor,t),Reflections=Mathf.Lerp(a.Reflections,b.Reflections,t),
            Fog=Color.Lerp(a.Fog,b.Fog,t),FogScale=Mathf.Lerp(a.FogScale,b.FogScale,t),
            SkyTint=Color.Lerp(a.SkyTint,b.SkyTint,t),SkyGround=Color.Lerp(a.SkyGround,b.SkyGround,t),SkyExposure=Mathf.Lerp(a.SkyExposure,b.SkyExposure,t),
            Atmosphere=Mathf.Lerp(a.Atmosphere,b.Atmosphere,t),SunSize=Mathf.Lerp(a.SunSize,b.SunSize,t),
            Glow=Mathf.Lerp(a.Glow,b.Glow,t),StreetLights=Mathf.Lerp(a.StreetLights,b.StreetLights,t)
        };
    }
    /// The ambient probe for this state: the scene's sky probe scaled per channel, plus a flat floor.
    public SphericalHarmonicsL2 Ambient(SphericalHarmonicsL2 scene)
    {
        var sh=scene;
        for(int c=0;c<3;c++)for(int k=0;k<9;k++)sh[c,k]*=AmbientTint[c];
        sh.AddAmbientLight(AmbientFloor);
        return sh;
    }
}
