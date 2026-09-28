using UnityEngine;

/// FORCE FIELD — the first defensive power. It deals no damage: activation raises a PlayerShield that absorbs up to Capacity
/// incoming damage (AbsorbFraction of each hit) for the power's Duration, then drops (broken or expired). Charges/cooldown
/// are the ordinary PowerDefinition data; a field that is already up refuses a re-cast, so a charge is never wasted.
[CreateAssetMenu(menuName = "Overpowered/Effects/Force field")]
public sealed class ForceFieldEffect : PowerEffect
{
    [Header("Defensive (absorbs; deals no damage)")]
    [Tooltip("Total damage the field absorbs before it breaks.")] public float Capacity = 60f;
    [Tooltip("Share of each incoming hit the field takes while it holds (the rest reaches health).")]
    [Range(0f, 1f)] public float AbsorbFraction = 1f;
    public float RingRadius = 1.1f;
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        if (user.Shield != null && user.Shield.Up) { user.Message = "Force field already up"; return false; }
        // Upgrade tiers lengthen the field (Duration) and add charges; capacity is this asset's value.
        user.ShieldComponent().Raise(Capacity, user.Stats(power).Duration, AbsorbFraction, RingRadius, power.Definition.PaletteColor);
        return true;
    }
}
