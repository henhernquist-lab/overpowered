using UnityEngine;

/// PULSEBREAKER miniboss tuning (Resources/Enemies/PulsebreakerTuning). Every timing, range and multiplier the boss brain
/// uses lives here so the fight can be retuned without touching code. Defaults are the shipping values; a missing asset
/// still leaves a playable boss because the same numbers are the field defaults.
[CreateAssetMenu(menuName = "Overpowered/Enemy/Pulsebreaker Tuning")]
public sealed class PulsebreakerTuning : ScriptableObject
{
    [Header("Shared")]
    [Tooltip("Damage of every attack is capped at this fraction of the player's current MaxHealth (no instant kills).")]
    [Range(.05f, .5f)] public float DamageCapFraction = .25f;
    [Tooltip("Incoming freeze / slow duration multiplier (1 = no resistance, every other enemy's behaviour).")]
    [Range(.1f, 1f)] public float FreezeResistance = .5f;
    [Range(.1f, 1f)] public float RootResistance = .6f;
    [Tooltip("Seconds the arrival stance holds before the first approach.")]
    public float ArrivalSeconds = 1.2f;
    [Tooltip("Idle pause between attacks in phase 1.")] public float GapSeconds = 1.9f;
    [Tooltip("Repositioning walk before choosing the next attack.")] public float RepositionSeconds = 1.1f;

    [Header("Attack 1 - Shockwave Slam (close, jumpable)")]
    public float SlamWindup = 1f;
    public float SlamRecovery = 1.2f;
    [Tooltip("Distance at which the slam becomes a legal choice (m).")] public float SlamChooseRange = 4.6f;
    public float SlamCoreRadius = 3f;
    [Tooltip("How far the seismic ring travels (m).")] public float SlamRadius = 8f;
    [Tooltip("Seconds for the ring to reach SlamRadius.")] public float SlamRingSeconds = .45f;
    [Tooltip("Thickness of the damaging band around the travelling ring (m).")] public float SlamRingBand = .9f;
    [Tooltip("Player feet higher than this above the impact plane are not hit: a jump clears the ring.")]
    public float SlamJumpHeight = 1.3f;
    public float SlamDamageMultiplier = 1f;
    public float SlamKnockback = 3.5f, SlamKnockbackSeconds = .3f;

    [Header("Attack 2 - Pulse Cannon (telegraphed lane)")]
    public float CannonWindup = 1.35f;
    public float CannonRecovery = 1f;
    public float CannonMinRange = 5f, CannonMaxRange = 26f;
    public float CannonDamageMultiplier = 1.25f;
    public float CannonTelegraphWidth = .55f;
    [Tooltip("Muzzle height above the feet before VisualScale.")] public float CannonMuzzleHeight = 1.35f;
    [Tooltip("Bolt travel speed (m/s) and maximum travel distance (m).")] public float CannonBoltSpeed = 24f, CannonBoltRange = 30f;
    [Tooltip("Bolt collision radius (m).")] public float CannonBoltRadius = .45f;
    public float CannonKnockback = 2f, CannonKnockbackSeconds = .25f;

    [Header("Attack 3 - Overdrive Rush (bounded, punishable)")]
    public float RushWindup = .8f;
    public float RushSpeed = 26f;
    [Tooltip("Hard cap on the travel time (s), so the rush can never run away.")] public float RushMaxSeconds = .45f;
    public float RushRecoveryMiss = 1.5f, RushRecoveryHit = .6f, RushStaggerSeconds = .7f;
    public float RushMinRange = 4f, RushMaxRange = 16f;
    public float RushLaneWidth = 1.2f;
    public float RushDamageMultiplier = .9f;
    public float RushKnockback = 5f, RushKnockbackSeconds = .35f;
    [Tooltip("Extra contact radius beyond the two capsules (m).")] public float RushContactRadius = .35f;

    [Header("Phase 2 - Overdrive (at or below PhaseHealthFraction)")]
    [Range(.05f, .95f)] public float PhaseHealthFraction = .4f;
    public float PhaseShiftSeconds = .6f;
    [Tooltip("Attack gap and recovery multipliers in phase 2. Telegraph durations are never shortened.")]
    [Range(.3f, 1f)] public float Phase2GapMultiplier = .6f;
    [Range(.3f, 1f)] public float Phase2RecoveryMultiplier = .7f;
    [Range(1f, 1.6f)] public float Phase2MoveSpeedMultiplier = 1.15f;
    [Tooltip("Seconds added between the two attacks of a phase-2 combination.")] public float ComboGap = .35f;

    [Header("Movement")]
    public float ApproachSpeedMultiplier = .9f;
    [Tooltip("Preferred fighting band while waiting for the attack cooldown.")] public float HoldMin = 3.5f, HoldMax = 7f;
    [Tooltip("Strafe speed while repositioning (m/s).")] public float StrafeSpeed = 1.6f;
    [Tooltip("Maximum distance from the player at which the boss still engages (it is AlwaysAggro, this only stops cross-city chases).")]
    public float MaxEngageDistance = 45f;

    public void Clamp()
    {
        DamageCapFraction = Mathf.Clamp(DamageCapFraction, .05f, .5f);
        FreezeResistance = Mathf.Clamp(FreezeResistance, .1f, 1f);
        RootResistance = Mathf.Clamp(RootResistance, .1f, 1f);
        ArrivalSeconds = Mathf.Max(0f, ArrivalSeconds);
        GapSeconds = Mathf.Max(.2f, GapSeconds);
        RepositionSeconds = Mathf.Max(.2f, RepositionSeconds);
        SlamWindup = Mathf.Max(.3f, SlamWindup); SlamRecovery = Mathf.Max(.2f, SlamRecovery);
        SlamRadius = Mathf.Max(1f, SlamRadius); SlamRingSeconds = Mathf.Max(.1f, SlamRingSeconds);
        SlamRingBand = Mathf.Max(.1f, SlamRingBand);
        CannonWindup = Mathf.Max(.3f, CannonWindup); CannonRecovery = Mathf.Max(.2f, CannonRecovery);
        CannonBoltSpeed = Mathf.Max(1f, CannonBoltSpeed); CannonBoltRange = Mathf.Max(1f, CannonBoltRange);
        RushWindup = Mathf.Max(.3f, RushWindup); RushSpeed = Mathf.Max(1f, RushSpeed);
        RushMaxSeconds = Mathf.Max(.05f, RushMaxSeconds);
        PhaseHealthFraction = Mathf.Clamp(PhaseHealthFraction, .05f, .95f);
        Phase2GapMultiplier = Mathf.Clamp(Phase2GapMultiplier, .3f, 1f);
        Phase2RecoveryMultiplier = Mathf.Clamp(Phase2RecoveryMultiplier, .3f, 1f);
        Phase2MoveSpeedMultiplier = Mathf.Clamp(Phase2MoveSpeedMultiplier, 1f, 1.6f);
        MaxEngageDistance = Mathf.Max(5f, MaxEngageDistance);
    }
    void OnValidate() { Clamp(); }
}
