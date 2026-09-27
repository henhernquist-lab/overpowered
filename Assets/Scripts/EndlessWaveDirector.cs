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
    [Header("Optional milestones (modifier / elite / miniboss waves, extra score events). Null = the original waves exactly.")]
    public EndlessWaveEvents Events;

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
    public int Remaining => Mathf.Max(0, Size - WaveKills);
    /// This wave's plan when the director has Events (null otherwise: the original formulas).
    public WavePlan Plan { get; private set; }
    /// Enemies in the current wave (the plan's size with Events, else WaveSize).
    public int Size => Plan != null ? Plan.Size : Tuning.WaveSize(Wave);
    /// MaxAlive slots in use: 1 per regular enemy, EliteAliveCost / MinibossAliveCost for elites / the miniboss.
    public int AliveCost { get; private set; }
    public int PeakAlive { get; private set; }
    public int PeakAliveCost { get; private set; }
    public int EliteKills { get; private set; }
    public int MinibossKills { get; private set; }
    public int FlawlessWaves { get; private set; }
    /// Slot kind of each Alive entry (parallel list).
    public readonly List<EndlessWaveEvents.Slot> AliveSlots = new List<EndlessWaveEvents.Slot>();
    /// Every score award the director makes (kill, elite kill, miniboss kill, wave clear, flawless), after it is added.
    public event System.Action<EndlessScoreEvent> Scored;
    bool hitThisWave;
    NavMeshPath path;

    public void Configure(EndlessWaveDirector tuning, GameModeSession session)
    {
        Attach(session); Tuning = tuning; path = new NavMeshPath();
        World.PlayerDamaged += OnPlayerDamaged;
        Arena = ArenaCentre();
        Intermission = true; IntermissionLeft = Tuning.IntermissionSeconds;
    }

    /// Arena: the spawn district's encounter site nearest the player spawn (CityDistrict.ArenaSite), snapped to the NavMesh.
    Vector3 ArenaCentre()
    {
        var best = World.City.ArenaSite();
        return NavMesh.SamplePosition(best, out var hit, World.Tuning.Npcs.NavSampleRadius, NavMesh.AllAreas) ? hit.position : best;
    }

    public override void Tick(float dt)
    {
        for (int i = Alive.Count - 1; i >= 0; i--) if (Alive[i] == null) { AliveCost -= CostAt(i); Alive.RemoveAt(i); AliveSlots.RemoveAt(i); Spawned--; Lost++; }
        if (Intermission)
        {
            IntermissionLeft -= dt;
            if (IntermissionLeft <= 0f) StartWave(Wave + 1);
            return;
        }
        Reinforce();
        if (WaveKills >= Size)
        {
            Award(EndlessScoreKind.WaveClear, Tuning.WaveClearBonus * Wave);
            int flawless = 0;
            if (Tuning.Events != null && !hitThisWave) { FlawlessWaves++; flawless = Tuning.Events.FlawlessBonus * Wave; Award(EndlessScoreKind.Flawless, flawless); }
            World.Message = $"WAVE {Wave} CLEARED  +{Tuning.WaveClearBonus * Wave}" + (flawless > 0 ? $"  FLAWLESS +{flawless}" : "");
            Intermission = true; IntermissionLeft = Tuning.IntermissionSeconds;
            Announce($"{WaveKills} DEFEATED", $"WAVE {Wave} CLEARED", $"+{Tuning.WaveClearBonus * Wave + flawless} SCORE");
        }
    }

    void StartWave(int wave)
    {
        Wave = wave; Spawned = 0; WaveKills = 0; Intermission = false; IntermissionLeft = 0f; hitThisWave = false;
        Plan = Tuning.Events != null ? Tuning.Events.Plan(Tuning, wave) : null;
        World.Message = $"WAVE {Wave}: {Size} enemies";
        if (Plan != null && (Plan.Modifiers.Count > 0 || Plan.Miniboss))
        {
            string names = Plan.Names + (Plan.Miniboss ? (Plan.Modifiers.Count > 0 ? " + " : "") + "MINIBOSS" : "");
            World.Message += " · " + names;
            Announce($"WAVE {Wave}", names, Plan.Elites > 0 ? $"{Plan.Elites} ELITES" : "");
        }
        Reinforce();
    }

    /// Keeps min(MaxAlive, enemies still to come) alive-slots filled until the whole wave has been spawned. Without Events
    /// every enemy costs one slot (the original rule); an elite / miniboss costs more, and waits for room unless nothing
    /// is alive (a heavy enemy is never blocked forever by its own cost).
    public int AliveTargetNow => Mathf.Min(Mathf.Max(1, Tuning.MaxAlive), Size);
    void Reinforce()
    {
        while (Spawned < Size)
        {
            int cost = Tuning.Events != null && Plan != null ? Tuning.Events.CostOf(Tuning.Events.SlotOf(Plan, Spawned)) : 1;
            if (!Room(Alive.Count, AliveCost, cost, AliveTargetNow, Tuning.MaxAlive)) break;
            if (!SpawnOne()) break; // no valid ring point this tick (player standing on the ring); retry next tick
        }
    }
    /// The alive-budget rule (shared with EndlessSimulation): room for the next enemy of `nextCost` slots?
    public static bool Room(int aliveCount, int aliveCost, int nextCost, int target, int maxAlive) =>
        aliveCount < Mathf.Max(1, maxAlive) && (aliveCount == 0 || aliveCost + nextCost <= target);
    int CostAt(int i) => Tuning.Events != null ? Tuning.Events.CostOf(AliveSlots[i]) : 1;

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
            var slot = EndlessWaveEvents.Slot.Regular; var spec = Spec(ref slot);
            var npc = CityNpc.Spawn(World, hit.position, spec.Role, spec.Archetype);
            if (npc == null) continue;
            npc.SetCombatStats(spec.Health, spec.Damage);
            npc.AlwaysAggro = true;
            npc.Damaged += dead => { if (dead) Defeated(npc); };
            Alive.Add(npc); AliveSlots.Add(slot); AliveCost += Tuning.Events != null ? Tuning.Events.CostOf(slot) : 1; Spawned++;
            PeakAlive = Mathf.Max(PeakAlive, Alive.Count); PeakAliveCost = Mathf.Max(PeakAliveCost, AliveCost);
            return true;
        }
        return false;
    }

    /// The next enemy's spec: the director's Compose (unchanged without Events), then the plan's multipliers and slot.
    public EnemySpec Spec(ref EndlessWaveEvents.Slot slot)
    {
        var events = Tuning.Events;
        if (events == null || Plan == null) return Tuning.Compose(Wave, Spawned, World.Progression.Data.Side);
        slot = events.SlotOf(Plan, Spawned);
        var spec = Tuning.Compose(Wave, EndlessWaveEvents.CompositionIndex(Plan, Spawned), World.Progression.Data.Side);
        spec.Health *= Plan.Health; spec.Damage *= Plan.Damage;
        if (slot == EndlessWaveEvents.Slot.Elite) { spec.Health *= events.EliteHealthMultiplier; spec.Damage *= events.EliteDamageMultiplier; }
        else if (slot == EndlessWaveEvents.Slot.Miniboss)
        {
            if (events.MinibossArchetype != null) spec.Archetype = events.MinibossArchetype;
            float h = spec.Archetype != null ? spec.Archetype.HealthMultiplier : 1f, d = spec.Archetype != null ? spec.Archetype.DamageMultiplier : 1f;
            spec.Health = Tuning.HealthFor(Wave) * Plan.Health * h * events.MinibossHealthMultiplier;
            spec.Damage = Tuning.DamageFor(Wave) * Plan.Damage * d * events.MinibossDamageMultiplier;
        }
        return spec;
    }
    void Defeated(CityNpc npc)
    {
        int i = Alive.IndexOf(npc); if (i < 0) return;
        var slot = AliveSlots[i]; AliveCost -= CostAt(i); Alive.RemoveAt(i); AliveSlots.RemoveAt(i);
        if (Session.Ended) return;
        Kills++; WaveKills++;
        var events = Tuning.Events;
        Award(EndlessScoreKind.Kill, Plan != null ? Mathf.RoundToInt(Tuning.KillScore * Wave * Plan.Score) : Tuning.KillScore * Wave);
        if (events == null) return;
        if (slot == EndlessWaveEvents.Slot.Elite) { EliteKills++; Award(EndlessScoreKind.EliteKill, events.EliteKillScore * Wave); }
        else if (slot == EndlessWaveEvents.Slot.Miniboss) { MinibossKills++; Award(EndlessScoreKind.MinibossKill, events.MinibossKillScore * Wave); }
    }
    void Award(EndlessScoreKind kind, int amount) { Session.AddScore(amount); Scored?.Invoke(new EndlessScoreEvent(kind, amount, Wave)); }
    void OnPlayerDamaged(bool dead) { if (!Intermission) hitThisWave = true; }
    void OnDestroy() { if (Session != null && World != null) World.PlayerDamaged -= OnPlayerDamaged; }

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
