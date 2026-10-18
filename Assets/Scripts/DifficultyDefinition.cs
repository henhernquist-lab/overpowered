using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Difficulty")]
public sealed class DifficultyDefinition : ScriptableObject
{
    public string Id, DisplayName, Subtitle;
    public float PlayerDamageMultiplier=1, EnemyHealthMultiplier=1, PlayerHealthMultiplier=1;
    public float EnemySpawnRateMultiplier=1, EnergyUsageMultiplier=1, CooldownDurationMultiplier=1;
    public static DifficultyDefinition Find(string id) => string.IsNullOrEmpty(id)?null:Resources.Load<DifficultyDefinition>("Difficulties/"+id);
    public static DifficultyDefinition Resolve(string id) => Find(id)??Resources.Load<DifficultyDefinition>("Difficulties/normal");
}

/// Value snapshot: later settings/asset changes cannot alter a running session.
public readonly struct SessionDifficulty
{
    public readonly string Id, DisplayName;
    public readonly float PlayerDamage, EnemyHealth, PlayerHealth, SpawnRate, EnergyUsage, Cooldown;
    public float EnemyDamage => 1f;
    public SessionDifficulty(DifficultyDefinition d)
    {
        Id=d!=null?d.Id:"normal";DisplayName=d!=null?d.DisplayName:"NORMAL";
        PlayerDamage=d!=null?d.PlayerDamageMultiplier:1;EnemyHealth=d!=null?d.EnemyHealthMultiplier:1;
        PlayerHealth=d!=null?d.PlayerHealthMultiplier:1;SpawnRate=d!=null?d.EnemySpawnRateMultiplier:1;
        EnergyUsage=d!=null?d.EnergyUsageMultiplier:1;Cooldown=d!=null?d.CooldownDurationMultiplier:1;
    }
    public float Interval(float baseline) => baseline/SpawnRate;
    public string Details => $"{PlayerDamage*100:0.#}% damage · {EnemyHealth*100:0.#}% enemy health · {PlayerHealth*100:0.#}% your health\n{SpawnRate*100:0.#}% spawn rate · {EnergyUsage*100:0.#}% energy use · {Cooldown*100:0.#}% cooldown duration";
}
