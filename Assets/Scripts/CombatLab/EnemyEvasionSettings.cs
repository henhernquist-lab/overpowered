using UnityEngine;

[System.Serializable]
public sealed class EnemyEvasionSettings
{
    public bool Enabled;
    [Min(1)] public float Cooldown=4.5f;
    [Min(.1f)] public float Telegraph=.22f, TravelSeconds=.32f, Recovery=.6f;
    [Min(0)] public float LateralDistance=2.2f, DashDistance=3f, RetreatDistance=1.8f;
    public float MinimumDistance=2.4f, MaximumDistance=10f;
    [Range(1,3)] public int BurstCount=2;
    [Min(.15f)] public float BurstWindup=.26f;
}
