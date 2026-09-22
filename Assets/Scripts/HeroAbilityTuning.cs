/// Tuning for the two self-contained player gestures, Backflip and Hurricane Kick.
///
/// Neither is a new system. The Backflip moves through the same CharacterController and
/// gravity integration that walking and jumping already use, and the Hurricane Kick is paid
/// for out of the existing Super Strength charges/cooldown/energy pool, so there is no new
/// power type, resource, data asset category, or invincibility/dodge mechanic here.
public static class HeroAbilityTuning
{
    // Backflip: a short backward dash plus a small hop. Values are in metres and seconds.
    public const float BackflipCooldown = 2.5f;  // time before another backflip is accepted
    public const float BackflipSeconds = .9f;    // horizontal dash window; the clip is fitted to it
    public const float BackflipDistance = 4.2f;  // metres travelled backward across that window
    public const float BackflipHopSpeed = 5.5f;  // upward speed; existing gravity brings it back down

    // Hurricane Kick: deliberately heavier and wider than the Super Strength punch.
    // These are multipliers on the strength power's already-tier-scaled stats, not absolutes,
    // so an upgraded strength power keeps improving the kick instead of the kick falling behind.
    public const float KickForceMultiplier = 1.5f;   // punch 1350 N.s -> 2025 N.s at tier 0
    public const float KickDamageMultiplier = 1.7f;  // punch 35 -> 59.5 at tier 0
    public const float KickOriginHeight = 1f;        // matches the punch origin height
    public const float KickOriginOffset = 1.4f;      // centred on the sweep, slightly under the punch's 1.7
    public const float KickRadius = 4.6f;            // punch 3.3 m -> a visibly wider sweep
    public const float KickUpwardForce = .18f;       // flatter than the punch's 0.28 lift
}
