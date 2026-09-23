using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Hero")]
public sealed class HeroDefinition : ScriptableObject
{
    public string Id, DisplayName;
    public GameObject CharacterPrefab;
    public Sprite Portrait;
    public CityColor Primary=CityColor.Blue, Secondary=CityColor.Cyan;
    public Vector3 VisualScale=Vector3.one;
    public PowerDefinition[] AvailablePowers;
    public PowerDefinition DefaultA, DefaultB;
    public HumanoidAnimationTuning Animation;
}
