using System;
using UnityEngine;

[Serializable] public sealed class PowerTier
{
    public string Name = "Upgrade";
    public int PointCost = 1, ExtraCharges = 1;
    public float ForceMultiplier = 1.4f, DamageMultiplier = 1.4f, DurationMultiplier = 1.3f, RangeMultiplier = 1.15f, CooldownMultiplier = .9f;
}
/// Instant powers pay one charge + ResourceCost per activation. Channeled powers (Laser Eyes) are held: ResourceCost is only the
/// energy needed to START, no charge is spent, DrainPerSecond is taken every frame while held, and Cooldown starts when the
/// channel ends. Defensive powers (Force Field) are ordinary instant activations whose effect acts on the player.
public enum PowerActivation { Instant, Channeled }
[CreateAssetMenu(menuName = "Overpowered/Power")]
public sealed class PowerDefinition : ScriptableObject
{
    public string Id, DisplayName;
    [TextArea] public string Description;
    public PowerEffect Effect;
    public bool InitiallyUnlocked;
    public int UnlockCost = 1, Charges = 3;
    public float Cooldown = .6f, ChargeRecharge = 1.5f, ResourceCost = 10f;
    public float Damage = 35f, Force = 450f, Radius = 3f, Range = 20f, Duration = 4f;
    public float GroundRecharge = PrototypeTuning.FlightRechargePerSecond;
    public float OriginOffset = 1.7f, OriginHeight = 1f, UpwardForce = .28f;
    public float ProjectileSpeed = 28f, ProjectileSize = .35f, HoldDistance = 3f, HoldSpring = 35f, HoldDamping = 10f, HoldMaxMass = 500f;
    public Color Color = new Color(1f,.25f,.03f);
    public CityColor PaletteColor = CityColor.Fire;
    public bool CastingPresentation;
    public MenuGlyph MenuIcon=MenuGlyph.Star;
    public PowerActivation Activation = PowerActivation.Instant;
    [Tooltip("Channeled only: energy per second while the fire button is held (ResourceCost is the minimum energy to start).")]
    public float DrainPerSecond;
    public PowerTier[] Upgrades = { new PowerTier(), new PowerTier() };
    public PowerStats GetStats(int tier)
    {
        var s = new PowerStats { Charges = Charges, Cooldown = Cooldown, Damage = Damage, Force = Force, Range = Range, Radius = Radius, Duration = Duration };
        for (int i = 0; i < Mathf.Min(tier, Upgrades.Length); i++)
        {
            var u = Upgrades[i]; s.Charges += u.ExtraCharges; s.Cooldown *= u.CooldownMultiplier;
            s.Force *= u.ForceMultiplier; s.Damage *= u.DamageMultiplier; s.Range *= u.RangeMultiplier; s.Radius *= u.RangeMultiplier; s.Duration *= u.DurationMultiplier;
        }
        return s;
    }
}
public struct PowerStats { public int Charges; public float Cooldown, Damage, Force, Range, Radius, Duration; }
public abstract class PowerEffect : ScriptableObject
{
    public virtual bool IsFlight => false;
    public abstract bool Execute(PowerUser user, PowerRuntime power);
}
/// Held powers: Execute starts the channel (true = started), Sustain runs every frame it stays held, Stop runs once when it ends.
public abstract class ChanneledEffect : PowerEffect
{
    public abstract void Sustain(PowerUser user, PowerRuntime power, float dt);
    public virtual void Stop(PowerUser user, PowerRuntime power) { }
}
public sealed class PowerRuntime
{
    public PowerDefinition Definition;
    public int Charges;
    public float Cooldown, ChargeTimer, Fuel;
    public PowerRuntime(PowerDefinition definition) { Definition = definition; Charges = definition.Charges; Fuel = definition.Duration; }
}
