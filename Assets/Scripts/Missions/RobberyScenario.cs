using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// ROBBERY — the robbers run from the site to parked getaway cars, climb in, and the car DRIVES OFF along a NavMesh route.
/// A car that drives EscapeDistance (or reaches its route's end) with a robber inside = the robbery succeeds (mission failed,
/// "getaway car got away"); a bailed robber reaching his exit on foot is a separate fail ("escaped on foot"). Stop them: take
/// robbers down (defeat, or hold R to cuff one that is frozen or rooted), wreck a car (BreakableProp damage: robbers inside
/// are caught), or stop a driving car — Ice freezes it, a heavy hit (>= StopImpulse) knocks it out of its drive — and the
/// robbers inside bail out on foot and run for it.
/// The parked car is an ordinary physics prop. While driving it is KINEMATIC and moved with MovePosition along the route,
/// so curbs and ground friction cannot hold it (integration #1: a force-driven 400 kg box never left its curb); stopped, it
/// becomes a dynamic prop again with its drive velocity.
[CreateAssetMenu(menuName = "Overpowered/Missions/Robbery getaway")]
public sealed class RobberyScenario : EncounterScenario
{
    [Header("Cast")]
    public int Robbers = 3, Cars = 2;
    [Tooltip("Metres from the site to each parked getaway car.")] public float CarDistance = 18f;
    public float RobberSpeed = 4.2f, BoardRadius = 2.4f;
    [Header("Getaway car")]
    [Tooltip("Seconds after the first robber climbs in before the car leaves (it also leaves once every assigned robber is in).")]
    public float DepartAfterFirstBoard = 5f;
    public float CarSpeed = 11f, TurnDegreesPerSecond = 160f;
    [Tooltip("Metres driven along its route after which the car has escaped.")] public float EscapeDistance = 70f;
    [Tooltip("A hit of at least this impulse (N.s) knocks a driving car out of its drive (Strength punch 1350; Fire Blast 450 does not).")]
    public float StopImpulse = 1200f;
    [Tooltip("A car tipped past this (transform.up.y) when it should leave cannot drive; its robbers bail.")] public float FlipUp = .35f;
    [Header("On foot after a stall")]
    public float BailRunSpeed = 4f, BailEscapeDistance = 40f;
    [Header("Cuffing a subdued (frozen/rooted) robber")]
    public float CuffRadius = 3f, CuffSeconds = 1f;
    public override ScenarioState Begin(CrimeEncounter encounter)
    {
        var state = encounter.gameObject.AddComponent<RobberyState>(); state.Setup(encounter, this); return state;
    }
}
public sealed class RobberyState : ScenarioState
{
    public sealed class Car
    {
        public Rigidbody Body; public Vector3 Park; public Vector3[] Route; public int Corner;
        public readonly List<EncounterActor> Assigned = new List<EncounterActor>(), Aboard = new List<EncounterActor>();
        public bool Driving, Stalled, Escaped, Wrecked, ReachedEnd; public float DepartAt = -1f, Driven;
        public Vector3 Velocity; public string StopReason = "";
        public float Travelled => Body != null ? Vector3.Distance(Flat(Body.position), Flat(Park)) : 0f;
    }
    RobberyScenario d;
    public readonly List<Car> Cars = new List<Car>();
    readonly HashSet<EncounterActor> bailed = new HashSet<EncounterActor>();
    EncounterActor cuffing; float cuff;
    public string LastEvent { get; private set; } = "";
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    public void Setup(CrimeEncounter e, RobberyScenario scenario)
    {
        Attach(e, scenario); d = scenario;
        var props = e.World.Tuning.Props;
        for (int i = 0; i < Mathf.Max(1, d.Cars); i++)
        {
            Vector3 dir = Quaternion.Euler(0, 45f + i * 360f / Mathf.Max(1, d.Cars), 0) * Vector3.forward;
            Vector3 park = e.World.City.NearestSidewalk(e.Site + dir * d.CarDistance);
            // SpawnProp builds the shared-palette car art (BreakableProp, rigidbody) for this exact name.
            var body = e.SpawnProp("Encounter throwable car", park, props.CarSize, props.CarMass); body.gameObject.name = "Getaway car " + i;
            var car = new Car { Body = body, Park = body.position }; Cars.Add(car);
            var tag = body.gameObject.AddComponent<GetawayCar>(); tag.State = this; tag.Car = car;
        }
        for (int i = 0; i < d.Robbers; i++)
        {
            var npc = e.SpawnActor(e.Site + Quaternion.Euler(0, i * 360f / Mathf.Max(1, d.Robbers), 0) * Vector3.forward * 1.5f, NpcRole.Criminal);
            var actor = new EncounterActor { Npc = npc, Exit = Cars[i % Cars.Count].Park };
            e.Robbers.Add(actor); Cars[i % Cars.Count].Assigned.Add(actor);
        }
    }
    Car CarOf(EncounterActor a) { foreach (var c in Cars) if (c.Assigned.Contains(a)) return c; return null; }
    bool Stopped(EncounterActor a) => !a.Escaped && (a.Captured || a.Npc == null || a.Npc.Dead);
    public override bool Drive(CityNpc npc)
    {
        var a = Encounter.Robbers.Find(x => x.Npc == npc); if (a == null) return false;
        if (a.Captured || a.Escaped) return true;
        var car = CarOf(a);
        // No car to reach any more (gone, wrecked, stalled, or it left without them): bail to a far exit on foot.
        if (!bailed.Contains(a) && (car == null || car.Wrecked || car.Stalled || car.Driving || car.Escaped || car.Body == null)) Bail(a, car);
        if (bailed.Contains(a))
        {
            // On foot: run for the far exit. Reaching it = escaped.
            npc.DirectTo(a.Exit, d.BailRunSpeed);
            if (Vector3.Distance(Flat(npc.transform.position), Flat(a.Exit)) < Encounter.Definition.EscapeReach) { a.Escaped = true; npc.gameObject.SetActive(false); LastEvent = "robber escaped on foot"; }
            return true;
        }
        npc.DirectTo(car.Body.position, d.RobberSpeed);
        if (Vector3.Distance(Flat(npc.transform.position), Flat(car.Body.position)) < d.BoardRadius)
        {
            car.Aboard.Add(a); npc.gameObject.SetActive(false); npc.transform.SetParent(car.Body.transform, true);
            if (car.DepartAt < 0f) car.DepartAt = Time.time + d.DepartAfterFirstBoard;
            LastEvent = "robber boarded " + car.Body.name;
        }
        return true;
    }
    public override void Tick(float dt)
    {
        foreach (var car in Cars)
        {
            if (car.Escaped) continue;
            if (car.Body == null && !car.Wrecked)
            {
                // Wrecked (BreakableProp destroyed the car with everyone inside): the robbers aboard are caught.
                car.Wrecked = true; LastEvent = "getaway car wrecked";
                foreach (var a in car.Aboard) a.Captured = true;
                foreach (var a in car.Assigned) if (!car.Aboard.Contains(a)) Bail(a, car);
                continue;
            }
            if (car.Wrecked || car.Stalled) continue;
            if (!car.Driving)
            {
                bool everyoneIn = car.Assigned.TrueForAll(a => car.Aboard.Contains(a) || Stopped(a));
                if (car.Aboard.Count > 0 && (everyoneIn || (car.DepartAt > 0f && Time.time >= car.DepartAt))) Depart(car);
                continue;
            }
            if (car.Driven >= d.EscapeDistance || car.ReachedEnd)
            {
                car.Escaped = true; car.Driving = false; LastEvent = $"getaway car escaped ({car.Driven:F1} m driven)";
                foreach (var a in car.Aboard) a.Escaped = true;
            }
        }
        if (cuffing != null && (cuffing.Npc == null || cuffing.Captured)) { cuffing = null; cuff = 0f; }
    }
    void Depart(Car car)
    {
        var frozen = car.Body.GetComponent<FrozenBody>();
        if (frozen != null && frozen.Frozen) { Stall(car, "frozen before it could leave"); return; }
        if (car.Body.transform.up.y < d.FlipUp) { Stall(car, "flipped before it could leave"); return; }
        car.Driving = true; car.Corner = 0; car.Driven = 0f; car.ReachedEnd = false;
        Vector3 start = car.Body.position, away = Flat(start - Encounter.Site); if (away.sqrMagnitude < .01f) away = Vector3.forward;
        Vector3 far = start + away.normalized * d.EscapeDistance * 1.3f;
        Vector3 goal = Encounter.World.City.NearestSidewalk(far);
        var path = new NavMeshPath();
        bool routed = NavMesh.SamplePosition(start, out var from, 4f, NavMesh.AllAreas) && NavMesh.CalculatePath(from.position, goal, NavMesh.AllAreas, path) && path.corners.Length > 1;
        car.Route = routed ? path.corners : new[] { start, far };
        car.Body.isKinematic = true; car.Body.interpolation = RigidbodyInterpolation.Interpolate;
        LastEvent = $"{car.Body.name} departs with {car.Aboard.Count} ({(routed ? "NavMesh route, " + car.Route.Length + " corners" : "straight fallback")})";
        foreach (var a in car.Assigned) if (!car.Aboard.Contains(a)) Bail(a, car);   // left behind: run for it
    }
    /// A driving car is stopped: it becomes a dynamic prop again with its drive velocity, and everyone assigned bails out.
    public void Stall(Car car, string why)
    {
        if (car.Stalled || car.Wrecked || car.Escaped) return;
        bool wasDriving = car.Driving;
        car.Stalled = true; car.Driving = false; car.StopReason = why; LastEvent = car.Body.name + " stopped (" + why + ")";
        if (wasDriving && car.Body != null) { car.Body.isKinematic = false; car.Body.linearVelocity = car.Velocity; }
        foreach (var a in car.Assigned) Bail(a, car);
        car.Aboard.Clear();
    }
    public float StopImpulse => d.StopImpulse;
    void Bail(EncounterActor a, Car car)
    {
        if (Stopped(a) || a.Escaped) return;
        bailed.Add(a);
        Vector3 from = car != null && car.Body != null ? car.Body.position : car != null ? car.Park : a.Npc.transform.position;
        Vector3 away = Flat(from - Encounter.Site); if (away.sqrMagnitude < .01f) away = Vector3.right;
        a.Exit = Encounter.World.City.NearestSidewalk(from + away.normalized * d.BailEscapeDistance);
        if (!a.Npc.gameObject.activeSelf)
        {
            a.Npc.transform.SetParent(Encounter.transform, true);
            Vector3 spot = NavMesh.SamplePosition(from + Vector3.right * 2f, out var hit, 6f, NavMesh.AllAreas) ? hit.position : from;
            a.Npc.gameObject.SetActive(true); a.Npc.transform.rotation = Quaternion.identity;
            if (a.Npc.Agent.enabled) a.Npc.Agent.Warp(spot); else a.Npc.transform.position = spot;
        }
    }
    /// Kinematic drive: advance CarSpeed x dt along the route corners (heights follow the NavMesh), face the heading.
    void FixedUpdate()
    {
        if (Encounter == null || Encounter.Finished) return;
        foreach (var car in Cars)
        {
            if (!car.Driving || car.Body == null || car.Route == null || car.ReachedEnd) continue;
            Vector3 before = car.Body.position, pos = before; float step = d.CarSpeed * Time.fixedDeltaTime;
            while (step > 0f && car.Corner < car.Route.Length)
            {
                Vector3 to = car.Route[car.Corner] - pos; float dist = to.magnitude;
                if (dist <= step) { pos = car.Route[car.Corner]; step -= dist; car.Driven += dist; car.Corner++; continue; }
                pos += to / dist * step; car.Driven += step; step = 0f;
            }
            if (car.Corner >= car.Route.Length) car.ReachedEnd = true;
            car.Velocity = (pos - before) / Time.fixedDeltaTime;
            car.Body.MovePosition(pos);
            Vector3 heading = Flat(pos - before);
            if (heading.sqrMagnitude > 1e-6f)
                car.Body.MoveRotation(Quaternion.RotateTowards(car.Body.rotation, Quaternion.LookRotation(heading.normalized, Vector3.up), d.TurnDegreesPerSecond * Time.fixedDeltaTime));
        }
    }
    public override bool Complete() => Encounter.Robbers.Count > 0 && Encounter.Robbers.TrueForAll(Stopped);
    public override bool Failed(out string reason)
    {
        foreach (var car in Cars) if (car.Escaped && car.Aboard.Count > 0) { reason = "The getaway car got away with " + car.Aboard.Count + " robber(s) aboard."; return true; }
        if (Encounter.EscapedRobbers > 0) { reason = "A robber escaped on foot."; return true; }
        reason = null; return false;
    }
    public override void Targets(ObjectiveTask task, List<Vector3> into)
    {
        if (task != ObjectiveTask.Robbers) return;
        foreach (var a in Encounter.Robbers)
        {
            if (Stopped(a) || a.Escaped) continue;
            var car = CarOf(a);
            if (car != null && car.Aboard.Contains(a) && car.Body != null) into.Add(car.Body.position);
            else if (a.Npc != null) into.Add(a.Npc.transform.position);
        }
    }
    EncounterActor Subdued(Vector3 at)
    {
        EncounterActor best = null; float nearest = d.CuffRadius;
        foreach (var a in Encounter.Robbers)
        {
            if (Stopped(a) || a.Escaped || a.Npc == null || !a.Npc.gameObject.activeInHierarchy || !(a.Npc.Frozen || a.Npc.Rooted)) continue;
            float dist = Vector3.Distance(at, a.Npc.transform.position); if (dist < nearest) { nearest = dist; best = a; }
        }
        return best;
    }
    public override bool InteractableNear(Vector3 point) => Subdued(point) != null;
    public override bool Interact(float dt)
    {
        var target = dt > 0f ? Subdued(Encounter.World.Hero.transform.position) : null;
        if (target != cuffing) { cuffing = target; cuff = 0f; }
        if (target == null) return false;
        cuff += dt; Encounter.InteractionHint = $"Cuffing… {Mathf.Clamp01(cuff / d.CuffSeconds):P0}";
        if (cuff < d.CuffSeconds) return false;
        target.Captured = true; target.Npc.gameObject.SetActive(false); cuffing = null; cuff = 0f; LastEvent = "robber cuffed";
        Encounter.TryComplete(); return true;
    }
}

/// Tag on each getaway car: Ice (MissionTarget.Freeze) freezes a DRIVING car in place and stops it; any hit of at least
/// StopImpulse (CombatImpact.Blast, beam ticks report their per-tick impulse) knocks it out of its drive. A parked car is
/// left to the ordinary Ice / physics paths (a car frozen before leaving cannot depart). Damage is not handled here:
/// CombatImpact.Blast damages a kinematic breakable directly, the beam damages BreakableProp itself.
public sealed class GetawayCar : MissionTarget
{
    public RobberyState State; public RobberyState.Car Car;
    public override bool Freeze(PowerUser source, PowerStats stats)
    {
        if (State == null || Car == null || !Car.Driving) return false;
        State.Stall(Car, "frozen");
        var frozen = GetComponent<FrozenBody>(); if (frozen == null) frozen = gameObject.AddComponent<FrozenBody>();
        frozen.Apply(stats.Duration, CityColor.Cyan);
        return true;
    }
    public override void Hit(float damage, float impulse, PowerUser source)
    {
        if (State != null && Car != null && Car.Driving && impulse >= State.StopImpulse) State.Stall(Car, $"rammed ({impulse:0} N.s)");
    }
}
