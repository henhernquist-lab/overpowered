using System;
using UnityEngine;

[Serializable] public sealed class PowerTier
{
    public string Name = "Upgrade";
    public int PointCost = 1, ExtraCharges = 1;
    public float ForceMultiplier = 1.4f, DamageMultiplier = 1.4f, DurationMultiplier = 1.3f, RangeMultiplier = 1.15f, CooldownMultiplier = .9f;
}
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
public sealed class PowerRuntime
{
    public PowerDefinition Definition;
    public int Charges;
    public float Cooldown, ChargeTimer, Fuel;
    public PowerRuntime(PowerDefinition definition) { Definition = definition; Charges = definition.Charges; Fuel = definition.Duration; }
}
