using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public enum PulsebreakerState { Idle, Approach, Reposition, Windup, Execution, Recovery, Staggered, PhaseTransition, Dead }
public enum PulsebreakerAttack { None, Slam, Cannon, Rush }

/// PULSEBREAKER: the Endless miniboss. A CityNpc body driven by its own bounded state machine (it never uses the archetype
/// attack cycle), with three telegraphed attacks, two phases and a damage cap. All numbers come from PulsebreakerTuning;
/// damage to the player goes through WorldSession.DamagePlayer and damage to the boss through CityNpc.Damage, so the
/// existing combat, HUD-feedback and difficulty contracts are untouched.
public sealed class PulsebreakerBoss : MonoBehaviour
{
    /// The live boss (at most one per city). The HUD bar and the tests read this instead of a new event.
    public static PulsebreakerBoss Active { get; private set; }
    public static int AttachRefusals { get; private set; }

    public CityNpc Npc { get; private set; }
    public PulsebreakerTuning Tuning { get; private set; }
    public PulsebreakerState State { get; private set; }
    public PulsebreakerAttack Attack { get; private set; }
    public bool Phase2 { get; private set; }
    public int Wave { get; private set; }

    // ---- telemetry (verification and diagnostics; gameplay never reads these) ----
    public int Windups { get; private set; }
    public int Releases { get; private set; }
    public int PlayerHits { get; private set; }
    public int SlamRings { get; private set; }
    public int BoltsFired { get; private set; }
    public int RushHits { get; private set; }
    public int RushBlocked { get; private set; }
    public int PhaseShifts { get; private set; }
    public int Cancels { get; private set; }
    public float LastWindupStarted { get; private set; } = -1f;
    public float LastReleaseAt { get; private set; } = -1f;
    public float LastMeasuredWindup { get; private set; } = -1f;
    public bool TelegraphVisible => disc != null && disc.Visible || line != null && line.Visible;
    public bool RingVisible => vfx != null && vfx.RingVisible;
    public float LiveRingRadius => vfx != null ? vfx.RingRadiusNow : 0f;
    public int BoltObjects { get; private set; }
    public int RushMisses { get; private set; }
    public bool HoldingToken => world != null && world.AttackTokens != null && world.AttackTokens.Holds(Npc);
    /// Verification-only: forces the next attack choice (gameplay never sets this).
    public void ForceNextAttack(PulsebreakerAttack attack) => forcedAttack = attack;
    PulsebreakerAttack forcedAttack = PulsebreakerAttack.None;

    WorldSession world; HumanoidPresentation pose; AttackTelegraph disc, line; PulsebreakerVfx vfx;
    float arrivalEnds, nextAttackAt, windupEnds, executionEnds, recoveryEnds, staggerEnds;
    Vector3 committedPoint, committedDirection, rushDirection; float rushDistance, rushTravelled, lastRingRadius;
    bool slamHit, rushHit, fastFollowup; float nextRepath; System.Random rng;
    int boltCursor; Transform[] bolts; float[] boltDistance; Vector3[] boltDirection;
    static readonly RaycastHit[] hits = new RaycastHit[12];

    // ------------------------------------------------------------------ lifecycle
    public static PulsebreakerBoss Attach(CityNpc npc, int wave)
    {
        if (npc == null) return null;
        var existing = npc.GetComponent<PulsebreakerBoss>();
        if (existing != null) return existing;
        if (Active != null) { AttachRefusals++; Debug.LogWarning("Pulsebreaker already active; refusing a second boss."); return null; }
        var boss = npc.gameObject.AddComponent<PulsebreakerBoss>();
        boss.Initialize(npc, wave);
        return boss;
    }
    /// Verification entry point: attach outside the wave director (never used by gameplay paths).
    public static PulsebreakerBoss AttachForVerification(CityNpc npc, int wave = 1) => Attach(npc, wave);

    void Initialize(CityNpc npc, int wave)
    {
        Npc = npc; Wave = Mathf.Max(1, wave); Active = this;
        world = WorldSession.Instance;
        Tuning = Resources.Load<PulsebreakerTuning>("Enemies/PulsebreakerTuning");
        if (Tuning == null) Tuning = ScriptableObject.CreateInstance<PulsebreakerTuning>();
        rng = new System.Random(unchecked(7919 * Wave + 104729));
        circleSign = rng.Next(2) == 0 ? 1 : -1;
        npc.FreezeResistance = Tuning.FreezeResistance; npc.RootResistance = Tuning.RootResistance;
        npc.AttachBoss(this);
        npc.AlwaysAggro = true;
        gameObject.name = "Pulsebreaker";
        pose = GetComponent<HumanoidPresentation>();
        vfx = new PulsebreakerVfx(transform);
        vfx.Build(pose, world != null ? world.Tuning.Npcs.Height : 1.8f, npc.Archetype != null ? npc.Archetype.VisualScale : 1f);
        State = PulsebreakerState.Idle; arrivalEnds = Time.time + Tuning.ArrivalSeconds;
        SetDestination(transform.position);
        if (world != null) world.Message = "PULSEBREAKER — ELITE THREAT";
    }

    void OnDestroy()
    {
        if (world != null && world.AttackTokens != null) world.AttackTokens.Release(Npc);
        vfx?.Clear();
        if (Active == this) Active = null;
        if (Npc != null) Npc.ClearBossReference(this);
    }

    // ------------------------------------------------------------------ the frame the boss owns
    /// Called by CityNpc.Update before the archetype attack cycle. Returns true when the boss owns this frame.
    public bool Tick()
    {
        if (Npc == null) return false;
        if (Npc.Dead) { if (State != PulsebreakerState.Dead) EnterDead(); return false; }
        if (world == null || world.Hero == null || world.PlayerDead) { Abort("player unavailable"); Npc.Agent.isStopped = true; return false; }
        if (world.Mode != null && (world.Mode.Ended || world.Mode.Paused)) { Abort("session"); Npc.Agent.isStopped = true; return false; }
        if (!Npc.Hostile) { Abort("not hostile"); return false; }
        float dt = Time.deltaTime;
        vfx.Tick(dt);
        TickBolts(dt);
        if (State == PulsebreakerState.Dead) return false;
        if (State == PulsebreakerState.PhaseTransition)
        {
            Npc.Agent.isStopped = true;
            if (Time.time >= staggerEnds) { State = PulsebreakerState.Approach; nextAttackAt = Time.time + .35f; }
            return true;
        }
        if (!Phase2 && Npc.HealthFraction <= Tuning.PhaseHealthFraction) { BeginPhaseTwo(); return true; }
        if (Npc.Frozen || Npc.Rooted)
        {
            if (State == PulsebreakerState.Windup || State == PulsebreakerState.Execution) Abort("control");
            EnterStaggered(Npc.Frozen ? "frozen" : "rooted");
            return true;
        }
        if (State == PulsebreakerState.Staggered)
        {
            if (Time.time < staggerEnds) { Npc.Agent.isStopped = true; return true; }
            State = PulsebreakerState.Approach; nextRepath = 0f; Npc.Agent.isStopped = false;
            nextAttackAt = Mathf.Max(nextAttackAt, Time.time + .25f);
        }
        float distance = Flat(WorldHero() - transform.position).magnitude;
        switch (State)
        {
            case PulsebreakerState.Idle:
                Npc.Agent.isStopped = true;
                FaceHero();
                if (Time.time >= arrivalEnds) { State = PulsebreakerState.Approach; nextRepath = 0f; }
                return true;
            case PulsebreakerState.Windup: TickWindup(); return true;
            case PulsebreakerState.Execution: TickExecution(dt); return true;
            case PulsebreakerState.Recovery: TickRecovery(); return true;
            case PulsebreakerState.Reposition: TickReposition(distance); return true;
            default: TickApproach(distance); return true;
        }
    }

    Vector3 WorldHero() => world != null && world.Hero != null ? world.Hero.transform.position : transform.position;

    // ------------------------------------------------------------------ movement
    void TickApproach(float distance)
    {
        Npc.Agent.isStopped = false;
        Npc.Agent.speed = Npc.Archetype.MoveSpeed * Tuning.ApproachSpeedMultiplier * (Phase2 ? Tuning.Phase2MoveSpeedMultiplier : 1f);
        if (distance > Tuning.MaxEngageDistance) { SetDestination(transform.position); return; }
        if (Time.time >= nextAttackAt && distance <= Tuning.CannonMaxRange && StartChosen(distance)) return;
        // Hold the band: close in when far, back off when too close, otherwise strafe slowly.
        Vector3 hero = WorldHero(); Vector3 away = Flat(transform.position - hero);
        float bearingDistance = away.magnitude;
        Vector3 bearing = bearingDistance > .01f ? away / bearingDistance : -Flat(transform.forward);
        if (distance > Tuning.HoldMax) SetDestination(hero, .35f);
        else if (distance < Tuning.HoldMin) SetDestination(hero + bearing * Tuning.HoldMin, .35f);
        else SetDestination(hero + Quaternion.AngleAxis(circleSign * 22f, Vector3.up) * bearing * Mathf.Clamp(distance, Tuning.HoldMin, Tuning.HoldMax), .7f);
        FaceHero();
    }
    int circleSign = 1;

    void TickReposition(float distance)
    {
        Npc.Agent.isStopped = false;
        Npc.Agent.speed = Npc.Archetype.MoveSpeed * (Phase2 ? Tuning.Phase2MoveSpeedMultiplier : 1f);
        Vector3 hero = WorldHero(); Vector3 away = Flat(transform.position - hero);
        float bearingDistance = away.magnitude;
        Vector3 bearing = bearingDistance > .01f ? away / bearingDistance : -Flat(transform.forward);
        Vector3 side = Vector3.Cross(Vector3.up, bearing) * circleSign;
        SetDestination(transform.position + side * 3f + bearing * 1.5f, .5f);
        FaceHero();
        if (Time.time >= executionEnds) { State = PulsebreakerState.Approach; nextRepath = 0f; }
    }

    void SetDestination(Vector3 point, float repath = 0f)
    {
        if (repath > 0f)
        {
            if (Time.time < nextRepath) return;
            nextRepath = Time.time + repath;
        }
        if (Npc == null || Npc.Agent == null || !Npc.Agent.enabled || !Npc.Agent.isOnNavMesh) return;
        if (NavMesh.SamplePosition(point, out var hit, 2f, Npc.Agent.areaMask)) Npc.Agent.SetDestination(hit.position);
    }

    void FaceHero()
    {
        if (Npc == null || Npc.Agent == null || !Npc.Agent.isStopped) return;   // the agent owns rotation while moving
        Vector3 facing = Flat(WorldHero() - transform.position);
        if (facing.sqrMagnitude < .0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), 320f * Time.deltaTime);
    }

    // ------------------------------------------------------------------ attack selection
    bool StartChosen(float distance)
    {
        Attack = PickAttack(distance);
        if (Attack == PulsebreakerAttack.None) return false;
        if (!world.AttackTokens.TryAcquire(Npc)) { nextAttackAt = Time.time + .35f; return false; }
        StartWindup();
        return true;
    }

    PulsebreakerAttack PickAttack(float distance)
    {
        if (forcedAttack != PulsebreakerAttack.None) return forcedAttack;

        bool slam = distance <= Tuning.SlamChooseRange;
        bool cannon = distance >= Tuning.CannonMinRange && distance <= Tuning.CannonMaxRange && ClearShot();
        bool rush = distance >= Tuning.RushMinRange && distance <= Tuning.RushMaxRange && RushLane(distance, out _, out _);
        int count = (slam ? 1 : 0) + (cannon ? 1 : 0) + (rush ? 1 : 0);
        // Close range always has an answer: the ring ignores cover, so a cramped corner can never silence the boss.
        if (count == 0 && distance <= Tuning.SlamChooseRange + 2f) { slam = true; count = 1; }
        if (count == 0) return PulsebreakerAttack.None;
        // Anti-repeat: the same attack never starts twice in a row unless it is the only legal option or this is a
        // deliberate phase-2 follow-up.
        if (count > 1 && !fastFollowup)
        {
            if (Attack == PulsebreakerAttack.Slam) slam = false;
            else if (Attack == PulsebreakerAttack.Cannon) cannon = false;
            else if (Attack == PulsebreakerAttack.Rush) rush = false;
            count = (slam ? 1 : 0) + (cannon ? 1 : 0) + (rush ? 1 : 0);
        }
        fastFollowup = false;
        int pick = rng.Next(count);
        if (slam && pick-- == 0) return PulsebreakerAttack.Slam;
        if (cannon && pick-- == 0) return PulsebreakerAttack.Cannon;
        if (rush) return PulsebreakerAttack.Rush;
        return slam ? PulsebreakerAttack.Slam : cannon ? PulsebreakerAttack.Cannon : PulsebreakerAttack.Rush;
    }

    bool ClearShot()
    {
        Vector3 from = Muzzle, to = Chest(WorldHero());
        return !Blocked(from, to - from, Vector3.Distance(from, to));
    }

    // ------------------------------------------------------------------ windup
    float WindupFor(PulsebreakerAttack attack) => attack == PulsebreakerAttack.Slam ? Tuning.SlamWindup :
        attack == PulsebreakerAttack.Cannon ? Tuning.CannonWindup : Tuning.RushWindup;

    void StartWindup()
    {
        forcedAttack = PulsebreakerAttack.None;   // consumed only once the token is held and the attack is committed
        State = PulsebreakerState.Windup; Windups++;
        LastWindupStarted = Time.time;
        Npc.Agent.isStopped = true; Npc.Agent.velocity = Vector3.zero;
        Vector3 hero = WorldHero();
        Vector3 facing = Flat(hero - transform.position);
        facing = facing.sqrMagnitude > .0001f ? facing.normalized : Flat(transform.forward);
        transform.rotation = Quaternion.LookRotation(facing);
        if (Attack == PulsebreakerAttack.Cannon)
        {
            committedPoint = Muzzle;
            Vector3 aim = Chest(hero) - committedPoint;
            committedDirection = aim.sqrMagnitude > .0001f ? aim.normalized : facing;
            Telegraph(true).ShowLine(committedPoint, committedDirection, Tuning.CannonBoltRange, Tuning.CannonTelegraphWidth);
        }
        else if (Attack == PulsebreakerAttack.Slam)
        {
            committedPoint = transform.position + facing * 1.2f;
            committedDirection = facing;
            Telegraph(false).ShowDisc(committedPoint, Tuning.SlamCoreRadius, .2f);
        }
        else
        {
            rushDirection = facing;
            RushLane(0f, out rushDistance, out Vector3 lane);
            committedDirection = rushDirection;
            Telegraph(true).ShowLine(transform.position + Vector3.up * .6f, rushDirection, Mathf.Max(2f, rushDistance), Tuning.RushLaneWidth);
        }
        windupEnds = Time.time + WindupFor(Attack);
        lastRingRadius = 0f; slamHit = false; rushHit = false; rushTravelled = 0f;
        PlayWindupClip();
    }
    void TickWindup()
    {
        Npc.Agent.isStopped = true;
        float windup = WindupFor(Attack);
        float progress = windup > 0f ? Mathf.Clamp01(1f - (windupEnds - Time.time) / windup) : 1f;
        if (Attack == PulsebreakerAttack.Slam) Telegraph(false).Tick(progress);
        else Telegraph(true).Tick(progress);
        if (Time.time < windupEnds) return;
        Release();
    }

    // ------------------------------------------------------------------ release
    void Release()
    {
        Releases++; LastReleaseAt = Time.time; LastMeasuredWindup = Time.time - LastWindupStarted;
        disc?.Hide(); line?.Hide();
        State = PulsebreakerState.Execution;
        if (Attack == PulsebreakerAttack.Slam)
        {
            SlamRings++; executionEnds = Time.time + Tuning.SlamRingSeconds;
            lastRingRadius = 0f;
            vfx.SlamRing(committedPoint, Tuning.SlamRadius, Tuning.SlamRingSeconds, CityColor.Fire);
        }
        else if (Attack == PulsebreakerAttack.Cannon)
        {
            FireBolt();
            executionEnds = Time.time + .15f;
            recoveryEnds = Time.time + .15f + RecoveryFor(Attack);
        }
        else
        {
            executionEnds = Time.time + Tuning.RushMaxSeconds;
        }
        PlayReleaseClip();
    }

    void TickExecution(float dt)
    {
        Npc.Agent.isStopped = true;
        if (Attack == PulsebreakerAttack.Slam) TickSlamRing();
        else if (Attack == PulsebreakerAttack.Rush) TickRush(dt);
        if (Time.time < executionEnds) return;
        if (Attack == PulsebreakerAttack.Slam || Attack == PulsebreakerAttack.Rush) recoveryEnds = Mathf.Max(recoveryEnds, Time.time + RecoveryFor(Attack));
        State = PulsebreakerState.Recovery;
    }

    void TickSlamRing()
    {
        if (slamHit) return;
        float radius = vfx.RingRadiusNow;
        Vector3 hero = WorldHero();
        float height = hero.y - committedPoint.y;
        float distance = Flat(hero - committedPoint).magnitude;
        if (ShockwaveHits(distance, height, lastRingRadius, radius, Tuning.SlamRingBand, Tuning.SlamJumpHeight))
        {
            slamHit = true;
            HitPlayer(Tuning.SlamDamageMultiplier, Tuning.SlamKnockback, Tuning.SlamKnockbackSeconds);
            FeelDirector.Impact(committedPoint, 1200f, 0f, 1);   // heavy camera/feel punctuation only
        }
        lastRingRadius = radius;
    }

    /// True when the travelling ring crosses the player's radius band at a survivable height. Shared with the tests so the
    /// jump and edge rules are verified without frame-rate-dependent timing.
    public static bool ShockwaveHits(float distance, float height, float previousRadius, float radius, float band, float jumpHeight)
    {
        if (height > jumpHeight || height < -jumpHeight) return false;
        return distance <= radius + band * .5f && distance >= Mathf.Min(previousRadius, radius) - band * .5f;
    }

    void TickRush(float dt)
    {
        if (rushHit) { Npc.Agent.isStopped = true; return; }
        float step = Tuning.RushSpeed * dt;
        if (!RushClear(rushDirection, step + .2f))
        {
            RushBlocked++; Npc.Agent.isStopped = true;
            executionEnds = Time.time;
            recoveryEnds = Time.time + Tuning.RushStaggerSeconds;
            vfx.SlamRing(transform.position, 2.5f, .3f, CityColor.Fire);
            return;
        }
        Npc.Agent.Move(rushDirection * step);
        rushTravelled += step;
        Vector3 delta = Flat(WorldHero() - transform.position);
        float reach = 1.15f + PlayerRadius + Tuning.RushContactRadius;
        if (delta.magnitude <= reach)
        {
            rushHit = true; RushHits++;
            HitPlayer(Tuning.RushDamageMultiplier, Tuning.RushKnockback, Tuning.RushKnockbackSeconds);
            FeelDirector.Impact(transform.position, 1100f, 0f, 1);
            executionEnds = Time.time;
            recoveryEnds = Time.time + RecoveryFor(PulsebreakerAttack.Rush);
            return;
        }
        if (rushTravelled >= rushDistance || rushTravelled >= Tuning.RushSpeed * Tuning.RushMaxSeconds)
        {
            RushMisses++;
            executionEnds = Time.time;
            recoveryEnds = Time.time + RecoveryFor(PulsebreakerAttack.Rush);
        }
    }
    float PlayerRadius
    {
        get
        {
            if (world == null || world.Hero == null) return .38f;
            var body = world.Hero.GetComponent<CharacterController>();
            return body != null ? body.radius : .38f;
        }
    }

    bool RushLane(float distance, out float length, out Vector3 end)
    {
        length = 0f; end = transform.position;
        Vector3 hero = WorldHero();
        Vector3 direction = Flat(hero - transform.position);
        if (direction.sqrMagnitude < .01f) direction = Flat(transform.forward);
        if (direction.sqrMagnitude < .01f) return false;
        direction.Normalize();
        if (Npc == null || Npc.Agent == null || !Npc.Agent.isOnNavMesh) return false;
        float want = distance > 0f ? Mathf.Clamp(distance + 2f, 3f, Tuning.RushMaxRange) : Tuning.RushMaxRange;
        // Shorten the lane until it is clear of geometry AND lands on connected navmesh at the same height. A blocked or
        // off-mesh lane simply is not a legal rush, so the boss can never teleport through a building.
        for (float lane = want; lane >= 2.5f; lane -= 1f)
        {
            if (!RushClear(direction, lane + .3f)) continue;
            Vector3 probe = transform.position + direction * lane;
            if (!NavMesh.SamplePosition(probe, out var hit, 1.5f, Npc.Agent.areaMask)) continue;
            if (Mathf.Abs(hit.position.y - transform.position.y) > 1.2f) continue;
            if (NavMesh.Raycast(transform.position, hit.position, out _, Npc.Agent.areaMask)) continue;
            float real = Flat(hit.position - transform.position).magnitude;
            if (real < 2.5f) continue;
            end = hit.position; length = real;
            return true;
        }
        return false;
    }

    bool RushClear(Vector3 direction, float distance)
    {
        var capsule = Npc != null ? Npc.GetComponent<CapsuleCollider>() : null;
        if (capsule == null || distance <= 0f) return true;
        Vector3 top = transform.position + Vector3.up * (capsule.height - capsule.radius - .05f);
        Vector3 bottom = transform.position + Vector3.up * (capsule.radius + .05f);
        int count = Physics.CapsuleCastNonAlloc(bottom, top, capsule.radius * .95f, direction, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) return false;   // fail closed on a crowded sweep
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i].collider;
            if (hit == null || hit == capsule || hit.transform.root == transform || hit.GetComponentInParent<CityNpc>() != null) continue;
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ recovery and phase
    void TickRecovery()
    {
        Npc.Agent.isStopped = true;
        if (Time.time < recoveryEnds) return;
        disc?.Hide(); line?.Hide();
        if (world != null) world.AttackTokens.Release(Npc);   // the token is held for exactly one attack
        // Phase 2 sometimes chains a second attack instead of the long gap; telegraphs are never shortened.
        fastFollowup = Phase2 && rng.NextDouble() < .4;
        nextAttackAt = Time.time + (fastFollowup ? Tuning.ComboGap : GapNow);
        // Occasional repositioning keeps the boss from orbiting on one ring the whole fight.
        if (!fastFollowup && rng.NextDouble() < .35) { State = PulsebreakerState.Reposition; executionEnds = Time.time + Tuning.RepositionSeconds; }
        else { State = PulsebreakerState.Approach; nextRepath = 0f; }
        Npc.Agent.isStopped = State != PulsebreakerState.Reposition;
    }

    float GapNow => Tuning.GapSeconds * (Phase2 ? Tuning.Phase2GapMultiplier : 1f);
    float RecoveryFor(PulsebreakerAttack attack)
    {
        float seconds = attack == PulsebreakerAttack.Slam ? Tuning.SlamRecovery :
            attack == PulsebreakerAttack.Cannon ? Tuning.CannonRecovery : Tuning.RushRecoveryMiss;
        if (attack == PulsebreakerAttack.Rush && rushHit) seconds = Tuning.RushRecoveryHit;
        return seconds * (Phase2 ? Tuning.Phase2RecoveryMultiplier : 1f);
    }

    void BeginPhaseTwo()
    {
        if (Phase2 || Npc == null || Npc.Dead) return;
        Phase2 = true; PhaseShifts++;
        world.AttackTokens.Release(Npc);
        disc?.Hide(); line?.Hide();
        State = PulsebreakerState.PhaseTransition;
        staggerEnds = Time.time + Tuning.PhaseShiftSeconds;
        Npc.Agent.isStopped = true;
        vfx.SetPhase(2);
        vfx.SlamRing(transform.position, 5f, Tuning.PhaseShiftSeconds, CityColor.Fire);
        if (world != null) world.Message = "PULSEBREAKER — OVERDRIVE";
        PlayPhaseClip();
    }

    void EnterStaggered(string reason)
    {
        if (State == PulsebreakerState.Staggered) { staggerEnds = Mathf.Max(staggerEnds, Time.time + .2f); return; }
        State = PulsebreakerState.Staggered;
        staggerEnds = Time.time + (Npc != null && Npc.Frozen ? .25f : .35f);
        disc?.Hide(); line?.Hide();
        if (world != null) world.AttackTokens.Release(Npc);
    }

    void EnterDead()
    {
        if (State == PulsebreakerState.Dead) return;
        State = PulsebreakerState.Dead;
        disc?.Hide(); line?.Hide();
        vfx.Clear();
        if (world != null) world.AttackTokens.Release(Npc);
        if (Active == this) Active = null;
        PlayDeathClip();
    }

    /// The one choke point CityNpc already calls for freeze, pause, session end, death and loss of hostility.
    public void Abort(string reason)
    {
        if (State == PulsebreakerState.Windup || State == PulsebreakerState.Execution || State == PulsebreakerState.Staggered)
        {
            Cancels++;
            disc?.Hide(); line?.Hide();
            if (world != null) world.AttackTokens.Release(Npc);
            State = PulsebreakerState.Approach;
            nextRepath = 0f;
            nextAttackAt = Mathf.Max(nextAttackAt, Time.time + .35f);
            PlayCancelClip();
        }
        else if (State != PulsebreakerState.Dead && State != PulsebreakerState.PhaseTransition)
        {
            State = PulsebreakerState.Approach;
        }
    }

    // ------------------------------------------------------------------ damage to the player
    void HitPlayer(float multiplier, float knockback, float seconds)
    {
        if (world == null || world.PlayerDead) return;
        float damage = Mathf.Min(Npc.ContactDamage * multiplier, world.MaxHealth * Tuning.DamageCapFraction);
        float before = world.Health;
        world.DamagePlayer(damage);
        float lost = before - world.Health;
        if (lost <= 0f) return;
        PlayerHits++;
        FeelDirector.PlayerHit(lost, transform.position);
        if (knockback > 0f && !world.PlayerDead) StartCoroutine(Knock(Flat(WorldHero() - transform.position), knockback, seconds));
    }

    IEnumerator Knock(Vector3 direction, float distance, float seconds)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < .0001f) yield break;
        direction.Normalize();
        var body = world.Hero.GetComponent<CharacterController>();
        float moved = 0f, speed = distance / Mathf.Max(.05f, seconds);
        while (moved < distance && body != null && body.enabled && !world.PlayerDead)
        {
            float step = Mathf.Min(distance - moved, speed * Time.deltaTime);
            if (Time.deltaTime > 0f) body.Move(direction * step + Vector3.down * world.Tuning.Movement.GroundStickSpeed * Time.deltaTime);
            moved += step;
            yield return null;
        }
    }

    // ------------------------------------------------------------------ pulse cannon bolt pool
    void FireBolt()
    {
        EnsureBolts();
        int index = boltCursor; boltCursor = (boltCursor + 1) % bolts.Length;
        bolts[index].position = committedPoint; bolts[index].rotation = Quaternion.LookRotation(committedDirection);
        boltDirection[index] = committedDirection; boltDistance[index] = 0f;
        bolts[index].gameObject.SetActive(true);
        BoltsFired++;
    }

    void EnsureBolts()
    {
        if (bolts != null) return;
        bolts = new Transform[3]; boltDirection = new Vector3[3]; boltDistance = new float[3];
        for (int i = 0; i < bolts.Length; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Pulsebreaker bolt " + i;
            var collider = go.GetComponent<Collider>(); if (collider != null) Destroy(collider);
            go.transform.SetParent(transform.parent, true);
            go.transform.localScale = new Vector3(.3f, .3f, 1.1f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CityMaterials.GetVfx(CityColor.Cyan);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            go.SetActive(false);
            bolts[i] = go.transform; BoltObjects = bolts.Length;
        }
    }

    void TickBolts(float dt)
    {
        if (bolts == null) return;
        for (int i = 0; i < bolts.Length; i++)
        {
            if (!bolts[i].gameObject.activeSelf) continue;
            float step = Tuning.CannonBoltSpeed * dt;
            Vector3 from = bolts[i].position;
            Vector3 direction = boltDirection[i];
            bool hitSomething = false;
            int count = Physics.SphereCastNonAlloc(from, Tuning.CannonBoltRadius, direction, hits, step + .05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count >= hits.Length) hitSomething = true;
            for (int h = 0; h < count; h++)
            {
                var collider = hits[h].collider;
                if (collider == null || collider.transform.root == transform || collider.GetComponentInParent<CityNpc>() != null) continue;
                if (IsHero(collider)) { HitPlayer(Tuning.CannonDamageMultiplier, Tuning.CannonKnockback, Tuning.CannonKnockbackSeconds); hitSomething = true; break; }
                hitSomething = true; break;
            }
            bolts[i].position = from + direction * step;
            boltDistance[i] += step;
            if (hitSomething || boltDistance[i] >= Tuning.CannonBoltRange) bolts[i].gameObject.SetActive(false);
        }
    }
    bool IsHero(Collider collider) => world != null && world.Hero != null && (collider.transform == world.Hero.transform || collider.transform.IsChildOf(world.Hero.transform));

    // ------------------------------------------------------------------ animation (existing shared clips)
    void PlayWindupClip()
    {
        var tuning = pose != null ? pose.Tuning : null;
        if (pose == null || tuning == null) return;
        if (Attack == PulsebreakerAttack.Slam) pose.BossAttack("Hurricane Kick", tuning.HurricaneKick, tuning.SlamStartSeconds, tuning.KickImpactSeconds, tuning.HurricaneKick.length, tuning.KickPlayback, WindupFor(Attack));
        else if (Attack == PulsebreakerAttack.Cannon) pose.BossAttack("Shoot", tuning.Shoot, tuning.ShootStartSeconds, tuning.ShootImpactSeconds, tuning.ShootEndSeconds, tuning.ShootPlayback, WindupFor(Attack));
        else pose.BossAttack("Punch", tuning.Punch, tuning.EnemyPunchStartSeconds, tuning.PunchImpactSeconds, tuning.Punch.length, tuning.PunchPlayback, WindupFor(Attack));
    }
    void PlayReleaseClip() { if (pose != null) pose.BossAttackRelease(); }
    void PlayCancelClip() { if (pose != null) pose.BossAttackCancel(); }
    void PlayPhaseClip() { if (pose != null && pose.Tuning != null && pose.Tuning.Taunt != null) pose.BossAttack("Taunt", pose.Tuning.Taunt, 0f, pose.Tuning.Taunt.length * .6f, pose.Tuning.Taunt.length, 1f, Tuning.PhaseShiftSeconds); }
    void PlayDeathClip() { }

    // ------------------------------------------------------------------ helpers
    Vector3 Muzzle => transform.position + Vector3.up * Tuning.CannonMuzzleHeight * (Npc.Archetype != null ? Npc.Archetype.VisualScale : 1f);
    Vector3 Chest(Vector3 feet) => feet + Vector3.up * (world != null ? world.Tuning.Movement.Height * .7f : 1.26f);
    static Vector3 Flat(Vector3 value) { value.y = 0f; return value; }

    bool Blocked(Vector3 from, Vector3 direction, float distance)
    {
        if (distance <= .01f || direction.sqrMagnitude < .0001f) return false;
        int count = Physics.RaycastNonAlloc(from, direction.normalized, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = hits[i].collider;
            if (collider == null || collider.transform.root == transform || collider.GetComponentInParent<CityNpc>() != null) continue;
            if (IsHero(collider)) continue;
            return true;
        }
        return false;
    }

    AttackTelegraph Telegraph(bool asLine)
    {
        if (asLine) { if (line == null) line = new AttackTelegraph(transform, true); return line; }
        if (disc == null) disc = new AttackTelegraph(transform, false);
        return disc;
    }
}
