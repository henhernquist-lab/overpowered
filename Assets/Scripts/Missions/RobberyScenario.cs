using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// ROBBERY — the robbers run from the site to parked getaway cars, climb in, and the car DRIVES OFF along the NavMesh. A car
/// that gets EscapeDistance away with a robber inside = the robbery succeeds (mission failed). Stop them: take robbers down
/// (defeat, or hold R to cuff one that is frozen or rooted), wreck a car (BreakableProp: robbers inside are caught), or stall
/// it — freeze it with Ice, flip it, or pin it so it cannot move — and the robbers inside bail out on foot and run for it.
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
    public float CarSpeed = 11f, CarAcceleration = 9f, TurnDegreesPerSecond = 120f;
    [Tooltip("Metres from its parking spot at which a driving car has escaped.")] public float EscapeDistance = 70f;
    [Tooltip("A driving car below StallSpeed for StallSeconds, frozen, or tipped past FlipUp stalls; robbers bail out.")]
    public float StallSpeed = .8f, StallSeconds = 2.5f, FlipUp = .35f;
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
        public bool Driving, Stalled, Escaped, Wrecked; public float DepartAt = -1f, Slow;
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
            Cars.Add(new Car { Body = body, Park = body.position });
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
            if (car.Travelled >= d.EscapeDistance)
            {
                car.Escaped = true; LastEvent = "getaway car escaped";
                foreach (var a in car.Aboard) a.Escaped = true;
                continue;
            }
            var frozen = car.Body.GetComponent<FrozenBody>();
            bool tipped = car.Body.transform.up.y < d.FlipUp;
            Vector3 v = car.Body.linearVelocity; v.y = 0f;
            car.Slow = v.magnitude < d.StallSpeed ? car.Slow + dt : 0f;
            if ((frozen != null && frozen.Frozen) || tipped || car.Slow >= d.StallSeconds) Stall(car, frozen != null && frozen.Frozen ? "frozen" : tipped ? "flipped" : "stuck");
        }
        if (cuffing != null && (cuffing.Npc == null || cuffing.Captured)) { cuffing = null; cuff = 0f; }
    }
    void Depart(Car car)
    {
        car.Driving = true; car.Corner = 0; car.Slow = -1.5f;   // grace to get rolling before the stall timer counts
        Vector3 away = Flat(car.Park - Encounter.Site); if (away.sqrMagnitude < .01f) away = Vector3.forward;
        Vector3 goal = Encounter.World.City.NearestSidewalk(car.Park + away.normalized * d.EscapeDistance * 1.3f);
        var path = new NavMeshPath();
        car.Route = NavMesh.CalculatePath(car.Body.position, goal, NavMesh.AllAreas, path) && path.corners.Length > 1 ? path.corners : new[] { car.Body.position, car.Park + away.normalized * d.EscapeDistance * 1.3f };
        LastEvent = car.Body.name + " departs with " + car.Aboard.Count;
        foreach (var a in car.Assigned) if (!car.Aboard.Contains(a)) Bail(a, car);   // left behind: run for it
    }
    void Stall(Car car, string why)
    {
        car.Stalled = true; car.Driving = false; LastEvent = car.Body.name + " stalled (" + why + ")";
        foreach (var a in car.Assigned) Bail(a, car);
        car.Aboard.Clear();
    }
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
    void FixedUpdate()
    {
        if (Encounter == null || Encounter.Finished) return;
        foreach (var car in Cars)
        {
            if (!car.Driving || car.Body == null || car.Route == null) continue;
            Vector3 target = car.Route[Mathf.Min(car.Corner, car.Route.Length - 1)], to = Flat(target - car.Body.position);
            if (to.magnitude < 3f && car.Corner < car.Route.Length - 1) { car.Corner++; continue; }
            Vector3 dir = to.sqrMagnitude > .01f ? to.normalized : Flat(car.Body.transform.forward).normalized;
            Vector3 flat = Flat(car.Body.linearVelocity);
            car.Body.AddForce(Vector3.ClampMagnitude(dir * d.CarSpeed - flat, d.CarAcceleration * Time.fixedDeltaTime), ForceMode.VelocityChange);
            car.Body.MoveRotation(Quaternion.RotateTowards(car.Body.rotation, Quaternion.LookRotation(dir, Vector3.up), d.TurnDegreesPerSecond * Time.fixedDeltaTime));
        }
    }
    public override bool Complete() => Encounter.Robbers.Count > 0 && Encounter.Robbers.TrueForAll(Stopped);
    public override bool Failed(out string reason)
    {
        foreach (var car in Cars) if (car.Escaped && car.Aboard.Count > 0) { reason = "The getaway car got away with " + car.Aboard.Count + " robber(s)."; return true; }
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
