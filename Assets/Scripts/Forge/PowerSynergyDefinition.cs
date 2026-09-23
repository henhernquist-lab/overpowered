using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Synergy")]
public sealed class PowerSynergyDefinition : ScriptableObject
{
    public string Id, DisplayName;
    [TextArea] public string Description;
    public PowerDefinition PowerA, PowerB;
    public MenuGlyph Icon=MenuGlyph.Star;
    public SynergyEffect Effect;
    public float Cooldown=10, Duration=3, Damage=40, Force=2600, Radius=7, Range=24;
    public float LiftSpeed=14, LiftSeconds=.3f, DiveSpeed=42, FreezeSeconds=2;
    public float MaxMass=500, OrbitRadius=2.8f, Spring=35, Damping=10, LaunchInterval=.15f;
    public float BonusMultiplier=1.7f, MeleeMultiplier=1.65f;
    public float BurnSeconds;
    public int MaxTargets=4;
    public CityColor Primary=CityColor.Cyan, Secondary=CityColor.Cream;
    public bool Matches(PowerDefinition a,PowerDefinition b)=>a!=null&&b!=null&&a!=b&&
        ((a==PowerA&&b==PowerB)||(a==PowerB&&b==PowerA));
}
public abstract class SynergyEffect : ScriptableObject
{
    public virtual bool Repeat(SynergyRunner runner)=>false;
    public abstract bool CanBegin(SynergyRunner runner);
    public abstract System.Collections.IEnumerator Execute(SynergyRunner runner);
}
