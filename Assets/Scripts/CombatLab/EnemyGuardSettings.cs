using UnityEngine;

[System.Serializable]
public sealed class EnemyGuardSettings
{
    public bool Enabled;
    [Range(20,160)] public float ArcDegrees=110;
    [Range(.05f,.9f)] public float DamageReduction=.65f;
    [Min(1)] public float Capacity=75;
    [Min(.25f)] public float BreakSeconds=1.8f;
    [Min(1)] public float BreakImpulse=900;
    [Min(1)] public float TurnDegreesPerSecond=85;
}
