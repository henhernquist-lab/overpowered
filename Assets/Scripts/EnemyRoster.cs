using UnityEngine;

/// Default archetype per existing role (Resources/Enemies/EnemyRoster.asset) plus the crowd-rhythm tuning.
/// Role decides hostility/colour; this table decides behaviour, so Hero/Villain modes get varied, telegraphed combat
/// without any mode-ID switch. Endless waves pick archetypes themselves (EndlessWaveDirector.Composition).
[CreateAssetMenu(menuName="Overpowered/Enemy Roster")]
public sealed class EnemyRoster : ScriptableObject
{
    public EnemyArchetype Criminal, Cop, PursuingHero;
    [Header("Attack-token budget")]
    [Tooltip("Hostile NPCs allowed to be engaged/in windup at once.")] public int MaxConcurrentAttackers=2;
    [Tooltip("A melee NPC holding a token that cannot reach the player in this time gives the token back.")] public float EngageTimeoutSeconds=2.5f;
    [Tooltip("Delay before an NPC that timed out or was blocked asks again.")] public float RetrySeconds=.5f;
    [Header("Waiting ring (melee/slam NPCs without a token)")]
    public float WaitRingRadius=4.5f;
    [Tooltip("Ring tolerance: inside Radius +/- this an NPC circles instead of approaching/backing off.")] public float RingSlack=1f;
    [Tooltip("Minimum arc spacing between waiting NPCs on the same ring, metres.")] public float RingSpacing=1.8f;
    [Tooltip("Circling walk speed, m/s (also Gunner strafing inside its band).")] public float CircleSpeed=1.2f;
    [Tooltip("How far ahead along the ring the circling destination is placed.")] public float CircleLeadDegrees=25f;

    public EnemyArchetype For(NpcRole role)=>role==NpcRole.Criminal?Criminal:role==NpcRole.Cop?Cop:role==NpcRole.PursuingHero?PursuingHero:null;
    static EnemyRoster current;
    public static EnemyRoster Current{get{if(current==null)current=Resources.Load<EnemyRoster>("Enemies/EnemyRoster");return current;}}
    void OnValidate(){MaxConcurrentAttackers=Mathf.Max(0,MaxConcurrentAttackers);WaitRingRadius=Mathf.Max(1f,WaitRingRadius);RingSlack=Mathf.Max(.1f,RingSlack);RingSpacing=Mathf.Max(0,RingSpacing);CircleSpeed=Mathf.Max(.1f,CircleSpeed);}
}
