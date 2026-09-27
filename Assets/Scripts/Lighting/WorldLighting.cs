using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName="Overpowered/Lighting/World Lighting")]
public sealed class WorldLighting : ScriptableObject
{
    [Tooltip("Used when the session has no mode, or its mode has no Lighting cycle (verification sandboxes).")] public LightingCycle DefaultCycle;
    [Tooltip("Copy the scene skybox once per session and drive it from the presets (false: the scene sky is left untouched).")] public bool DriveSky=true;
    [Tooltip("Ambient/emission are re-applied only when the blended value moved by more than this (a cycle changes slowly).")] public float ApplyEpsilon=.002f;
    public StreetLightSettings StreetLights=new StreetLightSettings();
    public PowerLightSettings PowerLights=new PowerLightSettings();
}
