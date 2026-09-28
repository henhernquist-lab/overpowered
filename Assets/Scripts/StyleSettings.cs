using UnityEngine;

/// Style tuning (optional asset Resources/StyleSettings; the defaults below apply without it).
[CreateAssetMenu(menuName = "Overpowered/Style settings")]
public sealed class StyleSettings : ScriptableObject
{
    [Header("Base points")]
    public int HitPoints = 5, KillPoints = 40, AirborneKillBonus = 15, SynergyPoints = 60, MultiKillBonus = 30;
    public float MultiKillWindow = 1.5f;
    [Header("Variety multiplier: +VarietyStep per distinct power used in the last VarietyWindow seconds")]
    public float VarietyWindow = 8f, VarietyStep = .25f, MaxMultiplier = 3f;
    [Header("Anti-exploit")]
    [Tooltip("The Nth consecutive event from the same power is worth RepeatDecay^(N-1) (floored at MinRepeatFactor).")]
    public float RepeatDecay = .7f, MinRepeatFactor = .2f;
    [Tooltip("Hit points per NPC are capped at PerTargetHitCap hits within TargetWindow seconds (kills still count).")]
    public int PerTargetHitCap = 4; public float TargetWindow = 10f;
    [Tooltip("No event for this long resets the multiplier and the repeat streak.")] public float IdleSeconds = 4f;
    [Tooltip("Rate limit: at most this many points per second (token bucket, one second of burst).")] public float MaxPointsPerSecond = 400f;
    [Header("Ranks (thresholds on the session total)")]
    public int[] RankThresholds = { 0, 200, 600, 1500, 3000 };
    public string[] Ranks = { "D", "C", "B", "A", "S" };
    public static StyleSettings Current { get { var s = Resources.Load<StyleSettings>("StyleSettings"); return s != null ? s : Default; } }
    static StyleSettings defaults;
    static StyleSettings Default { get { if (defaults == null) { defaults = CreateInstance<StyleSettings>(); defaults.hideFlags = HideFlags.HideAndDontSave; } return defaults; } }
}
