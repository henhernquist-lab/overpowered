using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Endless escalating waves. EVERY escalation constant lives on this asset; one asset can serve several modes
/// (Hero and Villain Endless Fight share Resources/ModeDirectors/EndlessWaves.asset).
[CreateAssetMenu(menuName="Overpowered/Mode Director/Endless Waves")]
public class EndlessWaveDirector : ModeDirector
{
    [Header("Wave size: FirstWaveCount + EnemiesPerWave * (wave - 1) enemies in total")]
    public int FirstWaveCount=3;
    public int EnemiesPerWave=2;
    [Tooltip("Performance cap on SIMULTANEOUSLY alive enemies; the rest of a larger wave arrives as reinforcements. " +
             "Each humanoid costs ~0.1 ms of CPU skinning (STATUS.md, 2026-09-22).")]
    public int MaxAlive=12;
    [Header("Per-wave stats, set explicitly on each enemy: base * (1 + growth * (wave - 1)). Heat stars never scale them.")]
    public float BaseHealth=65f;
    public float BaseDamage=8f;
    public float HealthGrowthPerWave=.15f;
    public float DamageGrowthPerWave=.1f;
    [Header("Pacing and arena (centred on the street intersection nearest the city centre)")]
    public float IntermissionSeconds=4f;
    public float SpawnRadius=22f;
    public float MinSpawnDistanceFromPlayer=12f;
    [Tooltip("Ring directions tried per spawn before retrying on a later tick.")] public int SpawnCandidates=16;
    [Header("Score: each kill = KillScore * wave; each cleared wave = WaveClearBonus * wave")]
    public int KillScore=10;
    public int WaveClearBonus=50;
    [Header("Enemy role by player side (a role must be hostile to that side)")]
    public NpcRole HeroSideEnemy=NpcRole.Criminal;
    public NpcRole VillainSideEnemy=NpcRole.Cop;
    [Header("Archetype composition (independent of role, so both sides get the full mix)")]
    [Tooltip("From FromWave on, each archetype joins the wave with its Weight. Enemies are dealt by a deterministic smooth " +
             "weighted round-robin over spawn order, so a wave's mix is exact and repeatable. Health/damage = wave value x the " +
             "archetype's multipliers. Empty = the role's roster archetype for every enemy.")]
    public ArchetypeShare[] Composition = new ArchetypeShare[0];

    public int WaveSize(int wave) => Mathf.Max(0, FirstWaveCount + EnemiesPerWave * (wave - 1));
    public int AliveTarget(int wave) => Mathf.Min(Mathf.Max(1, MaxAlive), WaveSize(wave));
    public float HealthMultiplier(int wave) => 1f + HealthGrowthPerWave * (wave - 1);
    public float DamageMultiplier(int wave) => 1f + DamageGrowthPerWave * (wave - 1);
    public float HealthFor(int wave) => BaseHealth * HealthMultiplier(wave);
    public float DamageFor(int wave) => BaseDamage * DamageMultiplier(wave);

    // ---------------------------------------------------------------------------------------------
    // EXTENSION POINT — enemy TYPE composition per wave.
    // Every enemy of every wave is created from the EnemySpec returned here (index = 0-based spawn order within the
    // wave). The role comes from the player's side (hostility/colour); the archetype (behaviour) comes from the
    // Composition table above. Health/damage stay explicit: the wave's exact escalation value x archetype multipliers.
    // ---------------------------------------------------------------------------------------------
    public virtual EnemySpec Compose(int wave, int index, PlayerSide side)
    {
        var role = side == PlayerSide.Hero ? HeroSideEnemy : VillainSideEnemy;
        var archetype = ArchetypeFor(wave, index);
        if (archetype == null && EnemyRoster.Current != null) archetype = EnemyRoster.Current.For(role);
        float health = archetype != null ? archetype.HealthMultiplier : 1f, damage = archetype != null ? archetype.DamageMultiplier : 1f;
        return new EnemySpec { Role = role, Archetype = archetype, Health = HealthFor(wave) * health, Damage = DamageFor(wave) * damage };
    }

    /// Smooth weighted round-robin over the archetypes eligible in this wave (FromWave <= wave, Weight > 0), replayed
    /// from the wave's first spawn to `index`: deterministic, interleaved, and proportional to the weights.
    public EnemyArchetype ArchetypeFor(int wave, int index)
    {
        if (Composition == null || Composition.Length == 0) return null;
        var current = new int[Composition.Length]; EnemyArchetype pick = null;
        for (int step = 0; step <= Mathf.Max(0, index); step++)
        {
            int total = 0, best = -1;
            for (int i = 0; i < Composition.Length; i++)
            {
                var share = Composition[i];
                if (share.Archetype == null || share.Weight <= 0 || wave < share.FromWave) continue;
                current[i] += share.Weight; total += share.Weight;
                if (best < 0 || current[i] > current[best]) best = i;
            }
            if (best < 0) return null;
            current[best] -= total; pick = Composition[best].Archetype;
        }
        return pick;
    }

    public override ModeDirectorState Begin(GameModeSession session)
    {
        var state = session.gameObject.AddComponent<EndlessWaveState>();
        state.Configure(this, session);
        return state;
    }
}

public struct EnemySpec { public NpcRole Role; public EnemyArchetype Archetype; public float Health, Damage; }
[System.Serializable] public struct ArchetypeShare { public EnemyArchetype Archetype; [Min(1)] public int FromWave; [Min(0)] public int Weight; }

/// Runtime wave state for EndlessWaveDirector (lives on the session object, destroyed with the city scene).
public sealed class EndlessWaveState : ModeDirectorState
{
    public EndlessWaveDirector Tuning { get; private set; }
    /// Current wave, or the last cleared wave during an intermission. 0 before the first wave.
    public int Wave { get; private set; }
    public bool Intermission { get; private set; }
    public float IntermissionLeft { get; private set; }
    /// Enemies spawned / defeated in the current wave.
    public int Spawned { get; private set; }
    public int WaveKills { get; private set; }
    /// Enemies defeated in the whole run.
    public int Kills { get; private set; }
    /// Enemies that vanished without being defeated; they are re-spawned so a wave can never soft-lock.
    public int Lost { get; private set; }
    public Vector3 Arena { get; private set; }
    public readonly List<CityNpc> Alive = new List<CityNpc>();
    public int Remaining => Mathf.Max(0, Tuning.WaveSize(Wave) - WaveKills);
    NavMeshPath path;

    public void Configure(EndlessWaveDirector tuning, GameModeSession session)
    {
        Attach(session); Tuning = tuning; path = new NavMeshPath();
        Arena = ArenaCentre();
        Intermission = true; IntermissionLeft = Tuning.IntermissionSeconds;
    }

    /// Street intersection nearest the city centre (same site grid as GameModeSession.SpawnNext); the 3-block city has
    /// four equidistant intersections, so ties go to the one nearest the player's spawn.
    Vector3 ArenaCentre()
    {
        var city = World.Tuning.City; float pitch = city.BlockSize + city.StreetWidth;
        Vector3 best = World.City.Spawn; float bestCentre = float.MaxValue, bestSpawn = float.MaxValue;
        for (int x = 0; x < city.Blocks - 1; x++) for (int z = 0; z < city.Blocks - 1; z++)
        {
            var site = new Vector3((x - (city.Blocks - 2) * .5f) * pitch, 0, (z - (city.Blocks - 2) * .5f) * pitch);
            float centre = Mathf.Round(site.sqrMagnitude * 100f), spawn = (site - World.City.Spawn).sqrMagnitude;
            if (centre < bestCentre || (centre == bestCentre && spawn < bestSpawn)) { best = site; bestCentre = centre; bestSpawn = spawn; }
        }
        return NavMesh.SamplePosition(best, out var hit, World.Tuning.Npcs.NavSampleRadius, NavMesh.AllAreas) ? hit.position : best;
    }

    public override void Tick(float dt)
    {
        for (int i = Alive.Count - 1; i >= 0; i--) if (Alive[i] == null) { Alive.RemoveAt(i); Spawned--; Lost++; }
        if (Intermission)
        {
            IntermissionLeft -= dt;
            if (IntermissionLeft <= 0f) StartWave(Wave + 1);
            return;
        }
        Reinforce();
        if (WaveKills >= Tuning.WaveSize(Wave))
        {
            Session.AddScore(Tuning.WaveClearBonus * Wave);
            World.Message = $"WAVE {Wave} CLEARED  +{Tuning.WaveClearBonus * Wave}";
            Intermission = true; IntermissionLeft = Tuning.IntermissionSeconds;
        }
    }

    void StartWave(int wave)
    {
        Wave = wave; Spawned = 0; WaveKills = 0; Intermission = false; IntermissionLeft = 0f;
        World.Message = $"WAVE {Wave}: {Tuning.WaveSize(Wave)} enemies";
        Reinforce();
    }

    /// Keeps min(MaxAlive, enemies still to come) alive until the whole wave has been spawned.
    void Reinforce()
    {
        while (Spawned < Tuning.WaveSize(Wave) && Alive.Count < Tuning.AliveTarget(Wave))
            if (!SpawnOne()) break; // no valid ring point this tick (player standing on the ring); retry next tick
    }

    bool SpawnOne()
    {
        Vector3 player = World.Hero.transform.position; player.y = 0f;
        int candidates = Mathf.Max(1, Tuning.SpawnCandidates); float start = Random.value * 360f;
        for (int i = 0; i < candidates; i++)
        {
            Vector3 point = Arena + Quaternion.Euler(0f, start + i * 360f / candidates, 0f) * Vector3.forward * Tuning.SpawnRadius;
            if (!NavMesh.SamplePosition(point, out var hit, World.Tuning.Npcs.NavSampleRadius, NavMesh.AllAreas)) continue;
            Vector3 flat = hit.position; flat.y = 0f;
            if (Vector3.Distance(flat, player) < Tuning.MinSpawnDistanceFromPlayer) continue;
            // Ground only: a rooftop NavMesh island would strand the enemy.
            if (!NavMesh.CalculatePath(hit.position, Arena, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            var spec = Tuning.Compose(Wave, Spawned, World.Progression.Data.Side);
            var npc = CityNpc.Spawn(World, hit.position, spec.Role, spec.Archetype);
            if (npc == null) continue;
            npc.SetCombatStats(spec.Health, spec.Damage);
            npc.AlwaysAggro = true;
            npc.Damaged += dead => { if (dead) Defeated(npc); };
            Alive.Add(npc); Spawned++;
            return true;
        }
        return false;
    }

    void Defeated(CityNpc npc)
    {
        if (!Alive.Remove(npc) || Session.Ended) return;
        Kills++; WaveKills++;
        Session.AddScore(Tuning.KillScore * Wave);
    }

    /// Saved per-mode best, or this run's score once it is higher.
    public int Best => Mathf.Max(World.Progression.BestScore(Session.Definition.Id), Session.Score);

    public override string HudLine
    {
        get
        {
            int best = Best;
            string tail = $"SCORE {Session.Score} · BEST {best}";
            if (!Intermission) return $"WAVE {Wave} · LEFT {Remaining} · {tail}";
            return (Wave == 0 ? "GET READY" : $"WAVE {Wave} CLEARED") + $" · WAVE {Wave + 1} IN {Mathf.CeilToInt(Mathf.Max(0f, IntermissionLeft))}s · {tail}";
        }
    }

    public override void HudStats(List<DirectorHudStat> into)
    {
        into.Add(new DirectorHudStat("WAVE", Wave));
        into.Add(new DirectorHudStat("LEFT", Intermission ? 0 : Remaining));
        into.Add(new DirectorHudStat("SCORE", Session.Score));
        into.Add(new DirectorHudStat("BEST", Best));
    }

    public override string HudCaption =>
        !Intermission ? null : (Wave == 0 ? "GET READY" : $"WAVE {Wave} CLEARED") + $"  ·  WAVE {Wave + 1} IN {Mathf.CeilToInt(Mathf.Max(0f, IntermissionLeft))}s";

    public override void Describe(SessionResult result) { result.Wave = Wave; result.EnemiesDefeated = Kills; }
}
