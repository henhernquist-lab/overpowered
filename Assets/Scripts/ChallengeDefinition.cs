using UnityEngine;

/// One challenge (Resources/Challenges). Id is the STABLE save key: never rename it once shipped (renaming = a new challenge
/// whose old progress stays in the save untouched). Rewards use the existing currencies only (XP and upgrade points).
[CreateAssetMenu(menuName = "Overpowered/Challenge")]
public sealed class ChallengeDefinition : ScriptableObject
{
    public string Id = "challenge-id";
    public string Title = "CHALLENGE";
    [TextArea] public string Description;
    public ChallengeMetric Metric;
    public ChallengeScope Scope;
    public ChallengeSide Side;
    [Tooltip("See ChallengeMetric; empty = any.")] public string Parameter;
    [Min(1)] public int Target = 1;
    [Min(0)] public int RewardXp, RewardPoints;
    /// Counting metrics add up; best-value metrics keep the highest value seen.
    public bool Accumulates => Metric != ChallengeMetric.EndlessWave && Metric != ChallengeMetric.SessionStyle && Metric != ChallengeMetric.DistinctPowersInSession;
}
