using System;
using UnityEngine;

[Serializable] public sealed class HeroLoadout
{
    public string HeroId, PowerA, PowerB;
    public CityColor Primary, Secondary;
}
[CreateAssetMenu(menuName="Overpowered/Forge/Catalog and Tuning")]
public sealed class ForgeCatalog : ScriptableObject
{
    public HeroDefinition[] Heroes;
    public PowerSynergyDefinition[] Synergies;
    public CityColor[] SuitColors={CityColor.Blue,CityColor.Cyan,CityColor.Teal,CityColor.Red,CityColor.Amber,CityColor.UiPurple,CityColor.Metal,CityColor.Cream};
    public KeyCode SynergyKey=KeyCode.C;
    public KeyCode SynergyGamepadButton=KeyCode.JoystickButton5;
    public float BasicCooldown=.65f, BasicDamage=8, BasicForce=160, BasicRadius=1.35f;
    public float FovKick=3, FovSeconds=.16f;
    public float ShockwaveSeconds=.45f, ParticleLifetime=.4f, ParticleSize=.22f;
    public int EffectPoolSize=6, ParticlesPerBurst=28;
    public HeroDefinition Hero(string id)=>Array.Find(Heroes,h=>h!=null&&h.Id==id)??Heroes[0];
    public PowerSynergyDefinition Resolve(PowerDefinition a,PowerDefinition b)=>Array.Find(Synergies,s=>s!=null&&s.Matches(a,b));
    public bool Allowed(HeroDefinition hero,PowerDefinition power)=>power!=null&&Array.IndexOf(hero.AvailablePowers,power)>=0;
    public CityColor ColorOrDefault(CityColor value,CityColor fallback)=>Array.IndexOf(SuitColors,value)>=0?value:fallback;
}
