using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Combat Lab")]
public sealed class CombatLabSettings : ScriptableObject
{
    [Range(1,24)] public int MaxEnemies=12;
    [Range(1,6)] public int GroupSize=3;
    public float ArenaSize=72, SpawnRadius=13, MinimumSpawnDistance=6;
    public EnemyArchetype[] Archetypes=new EnemyArchetype[0];
}
