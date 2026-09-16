using UnityEngine;

[CreateAssetMenu(menuName = "Overpowered/Procedural Animation Tuning")]
public sealed class ProceduralAnimationTuning : ScriptableObject
{
    [Header("Punch: seconds, degrees, metres")]
    [Min(.001f)] public float PunchAttackSeconds = .065f;
    [Min(.001f)] public float PunchRecoverSeconds = .24f;
    public float PunchArmDegrees = -100f;
    public float PunchBodyPitchDegrees = 12f;
    public float PunchBodyYawDegrees = -18f;
    public float PunchReach = .18f;

    [Header("Flight")]
    public float FlightPitchDegrees = 48f;
    [Min(.001f)] public float FlightFullLeanSpeed = 12f;
    [Min(.001f)] public float PoseResponse = 12f;

    [Header("Ground movement")]
    [Min(.001f)] public float RunFullLeanSpeed = 9f;
    public float RunLeanDegrees = 10f;
    [Min(0f)] public float BobHeight = .045f;
    [Min(0f)] public float BobCyclesPerMetre = .65f;
    [Min(.001f)] public float BobResponse = 14f;

    [Header("Landing")]
    [Min(0f)] public float LandMinimumImpactSpeed = 3f;
    [Min(.001f)] public float LandFullImpactSpeed = 16f;
    [Min(.001f)] public float LandCompressSeconds = .055f;
    [Min(.001f)] public float LandRecoverSeconds = .24f;
    [Range(0f, .8f)] public float LandSquash = .22f;
    [Range(0f, .8f)] public float LandWiden = .1f;
    public float LandBackTiltDegrees = 13f;
}
