using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// A small, inspectable mission-stage layer on top of EncounterScenario. A StagedScenario asset is DATA: named points around
/// the site, actor groups (real CityNpcs), target groups (hardpoints, crates, pickups, driving vehicles) and an ordered list
/// of stages. Exactly one stage is active; it completes or fails, its OnComplete actions run once, the next stage starts and
/// runs its OnStart actions once. The first failure (stage rule or timeout) latches the mission as FAILED; the last stage's
/// completion latches it as COMPLETE. After a latch nothing transitions again, and rewards stay with CrimeEncounter's single
/// End(). No graph, no scripting: a list of stages.
///
/// Stage kinds (what completes / what fails; every stage may also time out):
///  DefeatTargets   defeat or cuff Count (0 = all) members of Group                      | -
///  ReachArea       player within Radius of Point (or of the first live member of Group) | -
///  ProtectActors   Group survives Seconds                                               | more than AllowedLosses lost
///  EscortActors    Group (following the player) all within Radius of Point              | more than AllowedLosses lost
///  InteractTargets hold R HoldSeconds at Count (0 = all) targets of target Group        | too few targets left
///  DestroyTargets  destroy Count (0 = all) targets of target Group (only ARMED while this stage is active)
///  CollectItems    walk over Count (0 = all) pickups of target Group (collectable only while active) | too few left
///  Survive         Seconds pass                                                         | -
///  EscapeRadius    player farther than Radius from Point (default: the site)            | -
///  ChaseExit       defeat/cuff every member of Group, who flee to Point                 | any member reaches Point
///  StopVehicles    stop (freeze / heavy hit) or wreck every vehicle of target Group, driving from stage start | any escapes
///  RaiseHeat       Heat stars >= Count                                                  | -
[CreateAssetMenu(menuName = "Overpowered/Missions/Staged mission")]
public sealed class StagedScenario : EncounterScenario
{
    public MissionPoint[] Points = new MissionPoint[0];
    public ActorGroupSpec[] Actors = new ActorGroupSpec[0];
    public TargetGroupSpec[] Targets = new TargetGroupSpec[0];
    public MissionStageSpec[] Stages = new MissionStageSpec[0];
    [Header("Shared rules")]
    public float InteractRadius = 3f, HoldSeconds = 1.5f, CuffSeconds = 1f, PickupRadius = 1.8f, FollowRadius = 3.5f, FollowGap = 1.8f;
    [Tooltip("A fleeing actor that has moved less than this in StuckSeconds (or has no complete path) switches to its next exit.")]
    public float StuckDistance = .6f, StuckSeconds = 2f, ExitReach = 2.5f;
    [Tooltip("Harassing actors within HarassRange hurt protected actors (per second) or undo a restored target after HarassSeconds.")]
    public float HarassRange = 2.2f, HarassDamagePerSecond = 5f, HarassSeconds = 2.5f;
    [Tooltip("A hit of at least this impulse stops a driving mission vehicle (Ice always does).")] public float VehicleStopImpulse = 1200f;
    public float VehicleSpeed = 10f, VehicleEscapeDistance = 80f;
    [Header("Variation per spawn (replayability). All off = the authored layout, counts and timers every time.")]
    [Tooltip("Rotate every point about the site by a random yaw.")] public bool RandomYaw;
    [Tooltip("Mirror every point's angle (left / right) at random.")] public bool RandomMirror;
    [Tooltip("Difficulty band = the session's successes / DifficultyStep.")] [Min(1)] public int DifficultyStep = 2;
    [Tooltip("Extra members per band for actor groups marked ScaleWithDifficulty (floored), capped at MaxExtraActors.")] public float ExtraActorsPerBand;
    public int MaxExtraActors = 3;
    [Tooltip("Stage timeouts x this per band (0.95 = 5% tighter each band), never below MinTimeoutScale.")] public float TimeoutScalePerBand = 1f;
    public float MinTimeoutScale = .7f;
    public override ScenarioState Begin(CrimeEncounter encounter)
    {
        var state = encounter.gameObject.AddComponent<StagedState>(); state.Setup(encounter, this); return state;
    }
}
public enum StageKind { DefeatTargets, ReachArea, ProtectActors, EscortActors, InteractTargets, DestroyTargets, CollectItems, Survive, EscapeRadius, ChaseExit, StopVehicles, RaiseHeat }
public enum ActorBehavior { Default, Idle, HoldPost, Flee, Follow, Pursue, Harass }
public enum TargetKind { Hardpoint, Crate, Vehicle, Pickup }
public enum StageActionKind { SpawnActors, SpawnTargets, SetBehavior, AddHeat, Hint }
/// A named place relative to the site: Angle (degrees, 0 = +Z) and Distance; optionally snapped to the nearest sidewalk.
[Serializable] public sealed class MissionPoint { public string Id; public float Angle, Distance; public bool Sidewalk = true; }
[Serializable] public sealed class ActorGroupSpec
{
    public string Id; public NpcRole Role = NpcRole.Criminal;
    [Tooltip("Null = the roster archetype for Role.")] public EnemyArchetype Archetype;
    public int Count = 1; public string AtPoint; public float Ring = 2f, HealthMultiplier = 1f, Speed = 4f;
    public ActorBehavior Behavior = ActorBehavior.Default;
    [Tooltip("Flee: comma-separated exit point ids (first = preferred). Harass: the actor or target group to harass.")] public string BehaviorArgument;
    public bool SpawnAtStart = true;
    [Tooltip("Gets the scenario's difficulty-band extra members (hostile groups: yes; people you protect or escort: no).")] public bool ScaleWithDifficulty = true;
}
[Serializable] public sealed class TargetGroupSpec
{
    public string Id; public TargetKind Kind = TargetKind.Hardpoint; public int Count = 1; public string AtPoint; public float Ring = 2f;
    [Tooltip("Hardpoint health.")] public float Health = 150f;
    public bool SpawnAtStart = true;
}
[Serializable] public sealed class StageAction
{
    public StageActionKind Kind; public string Group; public ActorBehavior Behavior; public float Amount;
    [Tooltip("Hint: the text. SpawnActors / SpawnTargets: optional ANCHOR - an actor or target group (spawn around its first live member, else its last known position) or a point id. Empty = the group's own AtPoint.")]
    public string Text;
}
[Serializable] public sealed class MissionStageSpec
{
    public string Label = "OBJECTIVE"; public StageKind Kind; public string Group; public string Point;
    public int Count; public float Seconds, Radius = 4f, Timeout; public int AllowedLosses;
    [Tooltip("While this stage is active, spawn one more member of this actor group every RepeatSeconds, up to RepeatMaxAlive alive.")]
    public string RepeatGroup; public float RepeatSeconds; public int RepeatMaxAlive = 4;
    public StageAction[] OnStart = new StageAction[0], OnComplete = new StageAction[0];
}
public enum MissionTerminal { Running, Complete, Failed }
public sealed class StagedState : ScenarioState
{
    public sealed class Actor { public CityNpc Npc; public Vector3 Post; public Vector3? LastSeen; public bool Captured, Escaped; public int ExitIndex; public float StuckTimer; public Vector3 StuckFrom; public bool Following; public float HarassTimer; }
    public sealed class Target { public GameObject Go; public DamageTarget Hardpoint; public MissionVehicle Vehicle; public EncounterNode Pickup; public bool Done; public float Hold; public bool Missing => Go == null && Pickup == null; }
    StagedScenario d;
    readonly Dictionary<string, Vector3> points = new Dictionary<string, Vector3>();
    public readonly Dictionary<string, List<Actor>> Actors = new Dictionary<string, List<Actor>>();
    public readonly Dictionary<string, List<Target>> TargetsById = new Dictionary<string, List<Target>>();
    public int StageIndex { get; private set; } = -1;
    public MissionStageSpec Stage => StageIndex >= 0 && StageIndex < d.Stages.Length ? d.Stages[StageIndex] : null;
    public MissionTerminal Terminal { get; private set; }
    public string FailReason { get; private set; }
    public float StageElapsed { get; private set; }
    /// Times each stage index has started / completed (verification: exactly once each).
    public int[] Started { get; private set; }
    public int[] Completed { get; private set; }
    public event Action<int> StageStarted, StageCompleted;
    float repeatClock, startDistance; Actor cuffing; float cuff;
    /// This spawn's variation (see StagedScenario "Variation"): seed, layout yaw / mirror, difficulty band, extra hostile
    /// members per scaling group, and the timeout scale. Verification may fix the next seed with SeedOverride.
    public int Seed { get; private set; }
    public float Yaw { get; private set; }
    public bool Mirrored { get; private set; }
    public int Band { get; private set; }
    public int ExtraActors { get; private set; }
    public float TimeoutScale { get; private set; } = 1f;
    public static int? SeedOverride;
    public float TimeoutOf(MissionStageSpec s) => s.Timeout * TimeoutScale;
    public void Setup(CrimeEncounter e, StagedScenario scenario)
    {
        Attach(e, scenario); d = scenario;
        Started = new int[d.Stages.Length]; Completed = new int[d.Stages.Length];
        Seed = SeedOverride ?? UnityEngine.Random.Range(int.MinValue, int.MaxValue); SeedOverride = null;
        var random = new System.Random(Seed);
        Yaw = d.RandomYaw ? (float)(random.NextDouble() * 360d) : 0f;
        Mirrored = d.RandomMirror && random.Next(2) == 1;
        Band = e.World.Mode != null ? e.World.Mode.Successes / Mathf.Max(1, d.DifficultyStep) : 0;
        ExtraActors = Mathf.Clamp(Mathf.FloorToInt(Band * d.ExtraActorsPerBand + 1e-4f), 0, Mathf.Max(0, d.MaxExtraActors));
        TimeoutScale = Mathf.Clamp(Mathf.Pow(d.TimeoutScalePerBand, Band), Mathf.Min(1f, d.MinTimeoutScale), 1f);
        foreach (var p in d.Points)
        {
            float angle = (Mirrored ? -p.Angle : p.Angle) + Yaw;
            Vector3 at = e.Site + Quaternion.Euler(0, angle, 0) * Vector3.forward * p.Distance;
            points[p.Id] = p.Sidewalk ? e.World.City.NearestSidewalk(at) : at;
        }
        foreach (var g in d.Actors) { Actors[g.Id] = new List<Actor>(); if (g.SpawnAtStart) SpawnActors(g.Id, g.Count + Extra(g)); }
        foreach (var t in d.Targets) { TargetsById[t.Id] = new List<Target>(); if (t.SpawnAtStart) SpawnTargets(t.Id); }
        if (d.Stages.Length == 0) { Terminal = MissionTerminal.Complete; return; }
        BeginStage(0);
    }
    // ---------------------------------------------------------------- lookups and spawning
    public Vector3 Point(string id) => !string.IsNullOrEmpty(id) && points.TryGetValue(id, out var p) ? p : Encounter.Site;
    // Looked up every frame per group (TickActors) and per NPC repath (Drive): plain loops, no closures.
    public ActorGroupSpec ActorSpec(string id) { foreach (var g in d.Actors) if (g.Id == id) return g; return null; }
    TargetGroupSpec TargetSpec(string id) { foreach (var t in d.Targets) if (t.Id == id) return t; return null; }
    public List<Actor> Group(string id) => id != null && Actors.TryGetValue(id, out var list) ? list : null;
    public List<Target> TargetGroup(string id) => id != null && TargetsById.TryGetValue(id, out var list) ? list : null;
    static bool Gone(Actor a) => a.Npc == null || a.Npc.Dead || a.Captured;
    static bool Lost(Actor a) => !a.Captured && (a.Npc == null || a.Npc.Dead);
    int Extra(ActorGroupSpec g) => g != null && g.ScaleWithDifficulty && g.Count > 0 ? ExtraActors : 0;
    public int SpawnActors(string id, int count, Vector3? around = null)
    {
        var g = ActorSpec(id); var list = Group(id); if (g == null || list == null) return 0;
        int made = 0; Vector3 centre = around ?? Point(g.AtPoint);
        for (int i = 0; i < count; i++)
        {
            Vector3 at = centre + Quaternion.Euler(0, (list.Count + i) * 137.5f, 0) * Vector3.forward * g.Ring;
            var npc = Encounter.SpawnActor(at, g.Role, g.Archetype);
            if (npc == null) continue;
            if (g.HealthMultiplier != 1f) npc.SetCombatStats(npc.MaxHealth * g.HealthMultiplier, npc.ContactDamage);
            if (g.Role == NpcRole.Civilian) npc.GetComponentInChildren<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Cyan);
            // Each member starts on its own exit (a fleeing group scatters); blocked, it moves on to the next one.
            list.Add(new Actor { Npc = npc, Post = npc.transform.position, StuckFrom = npc.transform.position, ExitIndex = list.Count }); made++;
        }
        return made;
    }
    public void SpawnTargets(string id, Vector3? around = null)
    {
        var t = TargetSpec(id); var list = TargetGroup(id); if (t == null || list == null) return;
        Vector3 centre = around ?? Point(t.AtPoint); var props = World.Tuning.Props;
        for (int i = 0; i < t.Count; i++)
        {
            Vector3 at = centre + Quaternion.Euler(0, (list.Count + i) * 137.5f, 0) * Vector3.forward * (t.Count > 1 ? t.Ring : 0f);
            var item = new Target();
            switch (t.Kind)
            {
                case TargetKind.Hardpoint:
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Mission hardpoint " + id; go.transform.SetParent(Encounter.transform, false);
                    go.transform.position = at + Vector3.up * .8f; go.transform.localScale = new Vector3(1.4f, 1.6f, 1.4f);
                    go.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Metal);
                    item.Go = go; item.Hardpoint = go.AddComponent<DamageTarget>(); item.Hardpoint.Setup(t.Health); break;
                case TargetKind.Crate:
                    item.Go = Encounter.SpawnProp("Mission crate " + id, at, props.CrateSize, props.CrateMass).gameObject; break;
                case TargetKind.Vehicle:
                    var car = Encounter.SpawnProp("Encounter throwable car", at, props.CarSize, props.CarMass); car.gameObject.name = "Mission vehicle " + id;
                    item.Go = car.gameObject; item.Vehicle = car.gameObject.AddComponent<MissionVehicle>(); item.Vehicle.Setup(d.VehicleSpeed, d.VehicleStopImpulse); break;
                case TargetKind.Pickup:
                    item.Pickup = Encounter.SpawnNode("Mission pickup " + id, at, CityColor.Amber); break;
            }
            list.Add(item);
        }
    }
    // ---------------------------------------------------------------- stage machine
    void BeginStage(int index)
    {
        StageIndex = index; StageElapsed = 0f; repeatClock = 0f; Started[index]++;
        var s = d.Stages[index];
        Run(s.OnStart);
        if (s.Kind == StageKind.StopVehicles) foreach (var t in TargetGroup(s.Group) ?? new List<Target>()) if (t.Vehicle != null && !t.Vehicle.Driving && !t.Vehicle.Stopped) DriveAway(t.Vehicle);
        if (s.Kind == StageKind.ReachArea || s.Kind == StageKind.EscapeRadius) startDistance = Vector3.Distance(World.Hero.transform.position, ReachPoint(s));
        foreach (var t in AllTargets()) if (t.Hardpoint != null) t.Hardpoint.Armed = s.Kind == StageKind.DestroyTargets && TargetGroup(s.Group) != null && TargetGroup(s.Group).Contains(t);
        StageStarted?.Invoke(index);
    }
    IEnumerable<Target> AllTargets() { foreach (var list in TargetsById.Values) foreach (var t in list) yield return t; }
    void DriveAway(MissionVehicle v)
    {
        Vector3 start = v.transform.position, away = start - Encounter.Site; away.y = 0; if (away.sqrMagnitude < .01f) away = Vector3.forward;
        Vector3 far = start + away.normalized * d.VehicleEscapeDistance * 1.3f, goal = World.City.NearestSidewalk(far);
        var path = new NavMeshPath();
        bool routed = NavMesh.SamplePosition(start, out var from, 4f, NavMesh.AllAreas) && NavMesh.CalculatePath(from.position, goal, NavMesh.AllAreas, path) && path.corners.Length > 1;
        v.Drive(routed ? path.corners : new[] { start, far }, d.VehicleEscapeDistance);
    }
    void Run(StageAction[] actions)
    {
        if (actions == null) return;
        foreach (var a in actions)
            switch (a.Kind)
            {
                case StageActionKind.SpawnActors: SpawnActors(a.Group, Mathf.Max(1, Mathf.RoundToInt(a.Amount)) + Extra(ActorSpec(a.Group)), Anchor(a.Text)); break;
                case StageActionKind.SpawnTargets: SpawnTargets(a.Group, Anchor(a.Text)); break;
                case StageActionKind.SetBehavior: SetBehavior(a.Group, a.Behavior); break;
                case StageActionKind.AddHeat: World.AddHeat(a.Amount); break;
                case StageActionKind.Hint: Encounter.InteractionHint = a.Text; break;
            }
    }
    /// Where an anchored spawn goes: the first live member of an actor group, else any member's last position (a runner
    /// that was just taken down drops its bag where it fell); the first target of a target group; a point id; null = none.
    public Vector3? Anchor(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var g = Group(id);
        if (g != null)
        {
            foreach (var a in g) if (!Gone(a) && !a.Escaped) return a.Npc.transform.position;
            for (int i = g.Count - 1; i >= 0; i--) if (g[i].LastSeen.HasValue) return g[i].LastSeen;
        }
        var t = TargetGroup(id); if (t != null) foreach (var x in t) if (x.Go != null) return x.Go.transform.position;
        if (points.TryGetValue(id, out var p)) return p;
        return null;
    }
    readonly Dictionary<string, ActorBehavior> behaviorOverride = new Dictionary<string, ActorBehavior>();
    void SetBehavior(string group, ActorBehavior behavior) { if (group != null) behaviorOverride[group] = behavior; }
    ActorBehavior BehaviorOf(string group, ActorGroupSpec spec) => behaviorOverride.TryGetValue(group, out var b) ? b : spec.Behavior;
    void Fail(string reason) { if (Terminal != MissionTerminal.Running) return; Terminal = MissionTerminal.Failed; FailReason = reason; }
    public override void Tick(float dt)
    {
        if (Terminal != MissionTerminal.Running) return;
        var s = Stage; if (s == null) return;
        StageElapsed += dt;
        TickActors(dt);
        if (Terminal != MissionTerminal.Running) return;
        if (!string.IsNullOrEmpty(s.RepeatGroup) && s.RepeatSeconds > 0f)
        {
            repeatClock += dt;
            if (repeatClock >= s.RepeatSeconds) { repeatClock = 0f; int alive = 0; foreach (var a in Group(s.RepeatGroup) ?? new List<Actor>()) if (!Gone(a)) alive++; if (alive < s.RepeatMaxAlive) SpawnActors(s.RepeatGroup, 1); }
        }
        TickPickups();
        if (Evaluate(s, out bool failed, out string why)) { CompleteStage(); return; }
        if (failed) { Fail(why); return; }
        if (s.Timeout > 0f && StageElapsed >= TimeoutOf(s)) Fail("Timed out: " + s.Label.ToLowerInvariant());
    }
    void CompleteStage()
    {
        int index = StageIndex; var s = d.Stages[index];
        Completed[index]++; Run(s.OnComplete); StageCompleted?.Invoke(index);
        if (Terminal != MissionTerminal.Running) return;
        if (index + 1 >= d.Stages.Length) { Terminal = MissionTerminal.Complete; Encounter.TryComplete(); return; }
        BeginStage(index + 1);
    }
    public Vector3 ReachPoint(MissionStageSpec s)
    {
        if (!string.IsNullOrEmpty(s.Point)) return Point(s.Point);
        var g = Group(s.Group); if (g != null) foreach (var a in g) if (!Gone(a)) return a.Npc.transform.position;
        var t = TargetGroup(s.Group); if (t != null) foreach (var x in t) if (x.Go != null) return x.Go.transform.position;
        return Encounter.Site;
    }
    /// True when the stage is complete now; otherwise `failed` says whether its own rule failed.
    bool Evaluate(MissionStageSpec s, out bool failed, out string why)
    {
        failed = false; why = null; Vector3 hero = World.Hero.transform.position;
        switch (s.Kind)
        {
            case StageKind.DefeatTargets:
            {
                var g = Group(s.Group); if (g == null) return true; int gone = 0; foreach (var a in g) if (Gone(a)) gone++;
                return gone >= (s.Count > 0 ? Mathf.Min(s.Count, g.Count) : g.Count);
            }
            case StageKind.ReachArea: return Vector3.Distance(hero, ReachPoint(s)) <= s.Radius;
            case StageKind.EscapeRadius: return Vector3.Distance(hero, ReachPoint(s)) > s.Radius;
            case StageKind.Survive: return StageElapsed >= s.Seconds;
            case StageKind.RaiseHeat: return World.Stars >= s.Count;
            case StageKind.ProtectActors:
            {
                var g = Group(s.Group); int lost = 0; if (g != null) foreach (var a in g) if (Lost(a)) lost++;
                if (lost > s.AllowedLosses) { failed = true; why = $"{s.Label.ToLowerInvariant()}: {lost} lost"; return false; }
                return StageElapsed >= s.Seconds;
            }
            case StageKind.EscortActors:
            {
                var g = Group(s.Group); if (g == null) return true; int lost = 0, arrived = 0, alive = 0; Vector3 at = Point(s.Point);
                foreach (var a in g) { if (Lost(a)) { lost++; continue; } if (a.Captured) continue; alive++; if (Vector3.Distance(a.Npc.transform.position, at) <= s.Radius) arrived++; }
                if (lost > s.AllowedLosses) { failed = true; why = $"{s.Label.ToLowerInvariant()}: {lost} lost"; return false; }
                return alive > 0 && arrived == alive;
            }
            case StageKind.InteractTargets: case StageKind.DestroyTargets: case StageKind.CollectItems:
            {
                var t = TargetGroup(s.Group); if (t == null) return true; int done = 0, possible = 0;
                foreach (var x in t)
                {
                    bool finished = s.Kind == StageKind.DestroyTargets ? Destroyed(x) : x.Done;
                    if (finished) done++; else if (!x.Missing || s.Kind == StageKind.DestroyTargets) possible++;
                }
                int need = s.Count > 0 ? Mathf.Min(s.Count, t.Count) : t.Count;
                if (done >= need) return true;
                if (done + possible < need) { failed = true; why = $"{s.Label.ToLowerInvariant()}: objective lost"; }
                return false;
            }
            case StageKind.ChaseExit:
            {
                var g = Group(s.Group); if (g == null) return true; bool all = true;
                foreach (var a in g) { if (a.Escaped) { failed = true; why = $"{s.Label.ToLowerInvariant()}: the target got away"; return false; } if (!Gone(a)) all = false; }
                return all;
            }
            case StageKind.StopVehicles:
            {
                var t = TargetGroup(s.Group); if (t == null) return true; bool all = true;
                foreach (var x in t)
                {
                    if (x.Vehicle != null && x.Vehicle.Escaped) { failed = true; why = $"{s.Label.ToLowerInvariant()}: a vehicle got away"; return false; }
                    if (!(x.Go == null || (x.Vehicle != null && x.Vehicle.Stopped))) all = false;
                }
                return all;
            }
        }
        return false;
    }
    static bool Destroyed(Target x) => x.Hardpoint != null ? x.Hardpoint == null || x.Hardpoint.Destroyed : x.Go == null;
    // ---------------------------------------------------------------- actors
    void TickActors(float dt)
    {
        foreach (var pair in Actors)
        {
            var spec = ActorSpec(pair.Key); var behavior = BehaviorOf(pair.Key, spec);
            foreach (var a in pair.Value)
            {
                if (a.Npc != null) a.LastSeen = a.Npc.transform.position;
                if (Gone(a) || a.Escaped) continue;
                if (behavior == ActorBehavior.Pursue) a.Npc.AlwaysAggro = true;
                if (behavior == ActorBehavior.Harass) Harass(a, spec, dt);
                if (behavior == ActorBehavior.Flee)
                {
                    var exits = ExitPoints(spec);
                    if (exits.Length > 0 && Vector3.Distance(a.Npc.transform.position, exits[a.ExitIndex % exits.Length]) <= d.ExitReach)
                    { a.Escaped = true; a.Npc.gameObject.SetActive(false); }
                }
            }
        }
    }
    readonly Dictionary<ActorGroupSpec, Vector3[]> exitCache = new Dictionary<ActorGroupSpec, Vector3[]>();
    /// A Flee group's exits (resolved once; points never move during a mission).
    public Vector3[] ExitPoints(ActorGroupSpec spec)
    {
        if (exitCache.TryGetValue(spec, out var cached)) return cached;
        var ids = string.IsNullOrEmpty(spec.BehaviorArgument) ? new string[0] : spec.BehaviorArgument.Split(',');
        var r = new Vector3[ids.Length]; for (int i = 0; i < ids.Length; i++) r[i] = Point(ids[i].Trim());
        exitCache[spec] = r; return r;
    }
    void Harass(Actor a, ActorGroupSpec spec, float dt)
    {
        // Harass an actor group (hurt its members) or a target group (undo restored targets).
        var victims = Group(spec.BehaviorArgument);
        if (victims != null)
        {
            foreach (var v in victims)
                if (!Gone(v) && Vector3.Distance(v.Npc.transform.position, a.Npc.transform.position) <= d.HarassRange) { v.Npc.Damage(d.HarassDamagePerSecond * dt, null); return; }
            return;
        }
        var targets = TargetGroup(spec.BehaviorArgument); if (targets == null) return;
        foreach (var t in targets)
            if (t.Done && t.Go != null && Vector3.Distance(t.Go.transform.position, a.Npc.transform.position) <= d.HarassRange)
            { a.HarassTimer += dt; if (a.HarassTimer >= d.HarassSeconds) { t.Done = false; t.Hold = 0f; a.HarassTimer = 0f; Encounter.InteractionHint = "A saboteur undid your work!"; } return; }
        a.HarassTimer = 0f;
    }
    public override bool Drive(CityNpc npc)
    {
        foreach (var pair in Actors)
        {
            Actor a = null; foreach (var x in pair.Value) if (x.Npc == npc) { a = x; break; }
            if (a == null) continue;
            var spec = ActorSpec(pair.Key);
            switch (BehaviorOf(pair.Key, spec))
            {
                case ActorBehavior.Idle: npc.Agent.isStopped = true; return true;
                case ActorBehavior.HoldPost: npc.DirectTo(a.Post, spec.Speed); return true;
                case ActorBehavior.Flee:
                {
                    var exits = ExitPoints(spec); if (exits.Length == 0) return false;
                    Vector3 exit = exits[a.ExitIndex % exits.Length];
                    npc.Agent.isStopped = npc.Rooted; npc.DirectTo(exit, spec.Speed);
                    // Blocked or stuck: take the next exit (the route changes when the way is cut off).
                    a.StuckTimer += Time.deltaTime;
                    if (a.StuckTimer >= d.StuckSeconds)
                    {
                        bool stuck = Vector3.Distance(a.StuckFrom, npc.transform.position) < d.StuckDistance || (npc.Agent.hasPath && npc.Agent.pathStatus != NavMeshPathStatus.PathComplete);
                        if (stuck && exits.Length > 1) a.ExitIndex++;
                        a.StuckTimer = 0f; a.StuckFrom = npc.transform.position;
                    }
                    return true;
                }
                case ActorBehavior.Follow:
                {
                    Vector3 hero = World.Hero.transform.position;
                    if (!a.Following && Vector3.Distance(hero, npc.transform.position) < d.FollowRadius) a.Following = true;
                    if (!a.Following) { npc.Agent.isStopped = true; return true; }
                    npc.Agent.isStopped = npc.Rooted; npc.DirectTo(hero - (hero - npc.transform.position).normalized * d.FollowGap, spec.Speed); return true;
                }
                case ActorBehavior.Harass:
                {
                    Vector3? goal = null; float best = float.MaxValue;
                    var victims = Group(spec.BehaviorArgument);
                    if (victims != null) foreach (var v in victims) { if (Gone(v)) continue; float dist = Vector3.Distance(v.Npc.transform.position, npc.transform.position); if (dist < best) { best = dist; goal = v.Npc.transform.position; } }
                    var targets = TargetGroup(spec.BehaviorArgument);
                    if (targets != null) foreach (var t in targets) { if (!t.Done || t.Go == null) continue; float dist = Vector3.Distance(t.Go.transform.position, npc.transform.position); if (dist < best) { best = dist; goal = t.Go.transform.position; } }
                    if (goal == null) return false;
                    npc.Agent.isStopped = npc.Rooted; npc.DirectTo(goal.Value, spec.Speed); return true;
                }
                default: return false;
            }
        }
        return false;
    }
    // ---------------------------------------------------------------- pickups / interaction
    void TickPickups()
    {
        var s = Stage; if (s == null || s.Kind != StageKind.CollectItems) return;
        var t = TargetGroup(s.Group); if (t == null) return; Vector3 hero = World.Hero.transform.position;
        foreach (var x in t)
            if (!x.Done && x.Pickup != null && x.Pickup.Visual != null && Vector3.Distance(hero, x.Pickup.Visual.transform.position) < d.PickupRadius)
            { x.Done = true; x.Pickup.Done = true; x.Pickup.Visual.SetActive(false); }
    }
    Target InteractCandidate(Vector3 at)
    {
        var s = Stage; if (s == null || s.Kind != StageKind.InteractTargets) return null;
        var t = TargetGroup(s.Group); if (t == null) return null; Target best = null; float nearest = d.InteractRadius;
        foreach (var x in t) { if (x.Done || x.Go == null) continue; float dist = Vector3.Distance(at, x.Go.transform.position); if (dist < nearest) { nearest = dist; best = x; } }
        return best;
    }
    Actor CuffCandidate(Vector3 at)
    {
        var s = Stage; if (s == null || (s.Kind != StageKind.ChaseExit && s.Kind != StageKind.DefeatTargets)) return null;
        var g = Group(s.Group); if (g == null) return null; Actor best = null; float nearest = d.InteractRadius;
        foreach (var a in g) { if (Gone(a) || a.Escaped || !a.Npc.gameObject.activeInHierarchy || !(a.Npc.Frozen || a.Npc.Rooted)) continue; float dist = Vector3.Distance(at, a.Npc.transform.position); if (dist < nearest) { nearest = dist; best = a; } }
        return best;
    }
    public override bool InteractableNear(Vector3 point) => Terminal == MissionTerminal.Running && (InteractCandidate(point) != null || CuffCandidate(point) != null);
    public override bool Interact(float dt)
    {
        if (Terminal != MissionTerminal.Running) return false;
        Vector3 hero = World.Hero.transform.position;
        var target = dt > 0f ? InteractCandidate(hero) : null;
        // Runs every frame: plain loops, no iterator allocation.
        foreach (var list in TargetsById.Values) foreach (var x in list) if (x != target) x.Hold = 0f;
        if (target != null)
        {
            target.Hold += dt; Encounter.InteractionHint = $"Working… {Mathf.Clamp01(target.Hold / d.HoldSeconds):P0}";
            if (target.Hold >= d.HoldSeconds) { target.Done = true; target.Hold = 0f; return true; }
            return false;
        }
        var subdued = dt > 0f ? CuffCandidate(hero) : null;
        if (subdued != cuffing) { cuffing = subdued; cuff = 0f; }
        if (subdued == null) return false;
        cuff += dt; if (cuff < d.CuffSeconds) return false;
        subdued.Captured = true; subdued.Npc.gameObject.SetActive(false); cuffing = null; cuff = 0f; return true;
    }
    // ---------------------------------------------------------------- scenario contract
    public override bool Complete() => Terminal == MissionTerminal.Complete;
    public override bool Failed(out string reason) { reason = FailReason; return Terminal == MissionTerminal.Failed; }
    public override bool TryCurrent(out ObjectiveStep step)
    {
        var s = Stage; step = default;
        if (s == null || Terminal != MissionTerminal.Running) return false;
        int more = d.Stages.Length - StageIndex - 1; int done = 0, total = 1; bool distance = false; Vector3 hero = World.Hero.transform.position;
        switch (s.Kind)
        {
            case StageKind.DefeatTargets: case StageKind.ChaseExit: { var g = Group(s.Group); total = g != null ? (s.Count > 0 && s.Kind == StageKind.DefeatTargets ? Mathf.Min(s.Count, g.Count) : g.Count) : 0; if (g != null) foreach (var a in g) if (Gone(a)) done++; done = Mathf.Min(done, total); break; }
            case StageKind.ProtectActors: case StageKind.Survive: total = Mathf.CeilToInt(s.Seconds); done = Mathf.Min(total, Mathf.FloorToInt(StageElapsed)); break;
            case StageKind.EscortActors: { var g = Group(s.Group); Vector3 at = Point(s.Point); if (g != null) foreach (var a in g) { if (Lost(a) || a.Captured) continue; total++; if (Vector3.Distance(a.Npc.transform.position, at) <= s.Radius) done++; } total = Mathf.Max(0, total - 1); break; }
            case StageKind.InteractTargets: case StageKind.DestroyTargets: case StageKind.CollectItems:
            { var t = TargetGroup(s.Group); total = t != null ? (s.Count > 0 ? Mathf.Min(s.Count, t.Count) : t.Count) : 0; if (t != null) foreach (var x in t) if (s.Kind == StageKind.DestroyTargets ? Destroyed(x) : x.Done) done++; done = Mathf.Min(done, total); break; }
            case StageKind.StopVehicles: { var t = TargetGroup(s.Group); total = t != null ? t.Count : 0; if (t != null) foreach (var x in t) if (x.Go == null || (x.Vehicle != null && x.Vehicle.Stopped)) done++; break; }
            case StageKind.RaiseHeat: total = s.Count; done = Mathf.Min(total, World.Stars); break;
            case StageKind.ReachArea: distance = true; total = Mathf.Max(1, Mathf.CeilToInt(startDistance)); done = Mathf.Clamp(total - Mathf.CeilToInt(Vector3.Distance(hero, ReachPoint(s))), 0, total); break;
            case StageKind.EscapeRadius: distance = true; total = Mathf.CeilToInt(s.Radius); done = Mathf.Clamp(Mathf.FloorToInt(Vector3.Distance(hero, ReachPoint(s))), 0, total); break;
        }
        step = new ObjectiveStep(s.Label, done, total, more, distance); return true;
    }
    public override void Targets(ObjectiveTask task, List<Vector3> into)
    {
        var s = Stage; if (s == null || Terminal != MissionTerminal.Running) return;
        switch (s.Kind)
        {
            case StageKind.ReachArea: case StageKind.EscapeRadius: into.Add(ReachPoint(s)); break;
            case StageKind.EscortActors: into.Add(Point(s.Point)); break;
            case StageKind.Survive: case StageKind.RaiseHeat: into.Add(Encounter.Site); break;
            default:
                var g = Group(s.Group); if (g != null) foreach (var a in g) if (!Gone(a) && !a.Escaped) into.Add(a.Npc.transform.position);
                var t = TargetGroup(s.Group); if (t != null) foreach (var x in t)
                {
                    if (s.Kind == StageKind.DestroyTargets ? Destroyed(x) : x.Done) continue;
                    if (x.Go != null) into.Add(x.Go.transform.position); else if (x.Pickup != null && x.Pickup.Visual != null) into.Add(x.Pickup.Visual.transform.position);
                }
                break;
        }
    }
}
/// A mission hardpoint (armoured door, relay, generator): soaks blast / beam damage while ARMED (its stage is active);
/// hits while disarmed are ignored, which is what makes stage order matter. Ice does nothing to it.
public sealed class DamageTarget : MissionTarget
{
    public float Health { get; private set; }
    public float MaxHealth { get; private set; }
    public bool Armed;
    public int Hits { get; private set; }
    public int IgnoredHits { get; private set; }
    public bool Destroyed => Health <= 0f;
    public void Setup(float health) { Health = MaxHealth = Mathf.Max(1f, health); }
    public override void Hit(float damage, float impulse, PowerUser source)
    {
        if (Destroyed || damage <= 0f) return;
        if (!Armed) { IgnoredHits++; return; }
        Health = Mathf.Max(0f, Health - damage); Hits++;
        FeelDirector.Instance?.Particles.Burst(transform.position + Vector3.up * .5f, CityColor.Metal, Destroyed ? 14 : 4);
        if (Destroyed) { GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Slate); var c = GetComponent<Collider>(); if (c != null) c.enabled = false; }
    }
}
/// A mission vehicle: a parked physics prop until told to Drive, then KINEMATIC along its route (same drive as the robbery
/// getaway). Ice stops it (frozen in place), a hit >= StopImpulse knocks it out of its drive; stopped it is dynamic again.
/// Escaped after EscapeDistance driven or at the route's end. Wrecking it (BreakableProp) removes it.
public sealed class MissionVehicle : MissionTarget
{
    Rigidbody body; Vector3[] route; int corner; float speed, stopImpulse, escapeDistance; Vector3 velocity;
    public bool Driving { get; private set; }
    public bool Stopped { get; private set; }
    public bool Escaped { get; private set; }
    public float Driven { get; private set; }
    public string StopReason { get; private set; } = "";
    public void Setup(float driveSpeed, float impulse) { body = GetComponent<Rigidbody>(); speed = driveSpeed; stopImpulse = impulse; }
    public void Drive(Vector3[] path, float escapeAfter)
    {
        if (Driving || Stopped || Escaped || body == null) return;
        var frozen = GetComponent<FrozenBody>(); if (frozen != null && frozen.Frozen) { Stop("frozen before it could leave"); return; }
        route = path; corner = 0; Driven = 0f; escapeDistance = escapeAfter; Driving = true; body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate;
    }
    public void Stop(string why)
    {
        if (Stopped || Escaped) return;
        bool was = Driving; Driving = false; Stopped = true; StopReason = why;
        if (was && body != null) { body.isKinematic = false; body.linearVelocity = velocity; }
    }
    public override bool Freeze(PowerUser source, PowerStats stats)
    {
        if (!Driving) return false;
        Stop("frozen");
        var frozen = GetComponent<FrozenBody>(); if (frozen == null) frozen = gameObject.AddComponent<FrozenBody>();
        frozen.Apply(stats.Duration, CityColor.Cyan); return true;
    }
    public override void Hit(float damage, float impulse, PowerUser source) { if (Driving && impulse >= stopImpulse) Stop($"rammed ({impulse:0} N.s)"); }
    void FixedUpdate()
    {
        if (!Driving || route == null) return;
        Vector3 before = body.position, pos = before; float step = speed * Time.fixedDeltaTime;
        while (step > 0f && corner < route.Length)
        {
            Vector3 to = route[corner] - pos; float dist = to.magnitude;
            if (dist <= step) { pos = route[corner]; step -= dist; Driven += dist; corner++; continue; }
            pos += to / dist * step; Driven += step; step = 0f;
        }
        velocity = (pos - before) / Time.fixedDeltaTime; body.MovePosition(pos);
        Vector3 heading = pos - before; heading.y = 0f;
        if (heading.sqrMagnitude > 1e-6f) body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(heading.normalized, Vector3.up), 160f * Time.fixedDeltaTime));
        if (Driven >= escapeDistance || corner >= route.Length) { Driving = false; Escaped = true; }
    }
}
