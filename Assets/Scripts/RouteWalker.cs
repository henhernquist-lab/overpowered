using UnityEngine;
using UnityEngine.AI;

/// Route following for one city NPC: civilians walk Walk routes, police patrol Patrol routes when not responding
/// (CityLayout.Routes geometry, GameTuning.Npcs behaviour).
/// None -> Joining (a NavMeshAgent path to the nearest route point of its use) -> Following.
/// Following NEAR the hero steers the NavMeshAgent at a point RouteLookahead ahead on the route (agent keeps obstacle
/// avoidance and walks around static obstacles). Following FAR (NpcLod) is an analytic rail: the body is placed on the
/// baked spline samples (every frame while visible, every FarThinkInterval otherwise) with agent.updatePosition off, so a
/// far walker makes no NavMesh path request at all. Street crossings (transfers) are NavMesh-validated straight lines:
/// ridden analytically when far (Crossing), a short agent path when near. Any override (flee, hostility, combat,
/// encounter) calls Leave(), which hands the body back to the NavMeshAgent at exactly its current position; the next
/// free AI tick re-joins the nearest suitable route.
public sealed class RouteWalker
{
    public enum Mode { None, Joining, Following, Crossing }
    public Mode State { get; private set; }
    public RouteUse Use { get; }
    public int Route { get; private set; } = -1;
    /// Distance along Route (m) and walking direction (+1 / -1 in increasing s).
    public float S { get; private set; }
    public float Dir { get; private set; } = 1f;
    public float Lane { get; }
    public float Speed { get; }
    public bool Riding { get; private set; }
    /// Rail velocity while riding (the NavMeshAgent is not moving the body then).
    public Vector3 Velocity { get; private set; }
    public int TargetRoute { get; private set; } = -1;
    public float TargetS { get; private set; }
    public bool Active => State != Mode.None || Riding;
    // Diagnostics (verification reads these; they never change behaviour).
    public int PathRequests, RidePathRequests, RideSteps, Joins, TransfersTaken, Leaves, RailStarts, RailStops, Displaced, Unstuck;
    public float LastLeaveTime = -1f, LastFollowTime = -1f; public int LastLeaveFrame = -1, LastFollowFrame = -1;
    public float JoinDistance { get; private set; }
    /// Diagnostics: the point the near walker steers its agent at.
    public Vector3 Carrot => carrot;

    readonly CityNpc npc; readonly WorldSession world; readonly System.Random rng; bool free;
    Vector3 carrot, railPos, blendOffset, crossFrom, crossTo; float blend, crossDone, crossLength, joinStarted, nextJoinPath, progressS, progressTime; bool hasCarrot, transferring;
    float[] crossHeights = new float[0]; int crossSamples;

    /// Civilians walk Walk routes, police (Cop, PursuingHero) patrol Patrol routes; criminals keep the old wander (null).
    public static RouteWalker For(CityNpc npc, WorldSession world, NpcRole role)
    {
        if (role == NpcRole.Criminal || world.City.Routes == null) return null;
        var c = world.Tuning.Npcs; bool civilian = role == NpcRole.Civilian;
        return new RouteWalker(npc, world, civilian ? RouteUse.Walk : RouteUse.Patrol, civilian ? c.CivilianSpeed : c.PatrolSpeed);
    }
    RouteWalker(CityNpc npc, WorldSession world, RouteUse use, float baseSpeed)
    {
        this.npc = npc; this.world = world; Use = use; rng = new System.Random(npc.GetEntityId().GetHashCode());
        var c = world.Tuning.Npcs; var layout = world.City.Layout.Routes;
        Speed = baseSpeed * (1f + c.RouteSpeedJitter * (float)(rng.NextDouble() * 2 - 1));
        Lane = layout.LaneHalfWidth * (float)(rng.NextDouble() * 2 - 1);
    }
    CityRoutes Routes => world.City.Routes;
    CityRoute Current => Routes.Routes[Route];
    NpcSettings Tuning => world.Tuning.Npcs;
    /// Free to move this frame (the far rail holds still otherwise, exactly like a stopped agent).
    public bool CanMove => npc.enabled && !npc.Dead && !npc.Frozen && !world.PlayerDead && (world.Mode == null || (!world.Mode.Ended && !world.Mode.Paused));

    /// CityNpc hook, every AI tick before it acts: responding = fleeing, hostile in range, fighting or encounter-driven.
    /// True when that response just took the body off a route (the caller re-paths at once).
    public bool Override(bool responding)
    {
        free = !responding && world.City.RoutesActive;
        if (free || !Active) return false;
        Leave(); return true;
    }
    /// CityNpc hook for ordinary movement: walk the route this tick. False = nothing to walk (the caller wanders as before).
    public bool WalkRoute() => free && Tick();
    /// NpcLod recycling hook: with routes active, move this unseen civilian to an accepted walk-route point (moved) and
    /// return true (handled); false = routes off, the caller uses its sidewalk points.
    public bool Recycle(System.Predicate<Vector3> accept, out bool moved)
    {
        moved = false; if (!world.City.RoutesActive) return false;
        if (Routes.RandomPoint(RouteUse.Walk, accept, 12, out int r, out float s)) { Place(r, s); moved = true; }
        return true;
    }
    bool Far => NpcLod.Current != null && NpcLod.Current.IsFar(npc);
    Vector3 LanePoint(CityRoute r, float s) => r.Position(s) + r.Normal(s) * Lane;

    /// One AI tick with nothing overriding the route. False = no usable route (the caller falls back to wandering).
    public bool Tick()
    {
        var routes = Routes; if (routes == null || routes.Routes.Count == 0) return false;
        if (Riding) { SyncAgent(); return true; }
        npc.Agent.speed = Speed;
        switch (State)
        {
            case Mode.None: return Join();
            case Mode.Joining: TickJoin(); return true;
            default: TickFollow(); return true;
        }
    }

    bool Join()
    {
        var c = Tuning; Vector3 p = npc.transform.position;
        if (!Routes.Nearest(p, Use, c.RouteSearchRadius, out int r, out float s, out float d)) return false;
        Joins++; JoinDistance = d; TargetRoute = r; TargetS = s; transferring = false;
        ChooseDirection(Routes.Routes[r], s, npc.transform.forward);
        if (d <= c.RejoinRadius) { StartFollowing(r, s); return true; }
        State = Mode.Joining; joinStarted = Time.time; nextJoinPath = 0f; PathTo(LanePoint(Routes.Routes[r], s));
        return true;
    }
    void ChooseDirection(CityRoute route, float s, Vector3 heading)
    {
        heading.y = 0f; float dot = heading.sqrMagnitude > .01f ? Vector3.Dot(route.Tangent(s), heading.normalized) : 0f;
        Dir = Mathf.Abs(dot) > .2f ? Mathf.Sign(dot) : rng.Next(2) == 0 ? 1f : -1f;
        if (!route.Closed) { if (s <= route.Step) Dir = 1f; else if (s >= route.Length - route.Step) Dir = -1f; }
    }
    void TickJoin()
    {
        var c = Tuning; var route = Routes.Routes[TargetRoute];
        float s = route.Project(npc.transform.position, TargetS, c.RouteLookahead * 4f, out float lateral);
        if (lateral <= c.RejoinRadius) { StartFollowing(TargetRoute, s); return; }
        if (Time.time - joinStarted > c.JoinTimeout) { State = Mode.None; Join(); return; }
        var agent = npc.Agent;
        if (Time.time >= nextJoinPath && !agent.pathPending && (!agent.hasPath || agent.remainingDistance < .3f)) { nextJoinPath = Time.time + c.RepathSeconds; PathTo(LanePoint(route, TargetS)); }
    }
    void StartFollowing(int r, float s)
    {
        State = Mode.Following; Route = r; S = s; hasCarrot = false; transferring = false; progressS = s; progressTime = Time.time;
        LastFollowTime = Time.time; LastFollowFrame = Time.frameCount;
        if (Far) StartRide(); else SteerAhead(true);
    }
    void TickFollow()
    {
        var c = Tuning; var route = Current; float prev = S;
        S = route.Project(npc.transform.position, S, Mathf.Max(6f, Speed * 1.5f + c.RouteLookahead), out float lateral);
        if (lateral > c.RouteLeaveDistance)
        {   // pushed off (crowd, knockback): walk back to this route at the nearest point.
            State = Mode.Joining; TargetRoute = Route; TargetS = S; joinStarted = Time.time; nextJoinPath = 0f; PathTo(LanePoint(route, S)); return;
        }
        if (Events(route, prev, S)) return;
        // Progress guard: a near walker held in place (a crowd knot, a prop on the line) turns around instead of standing.
        if (Mathf.Abs(route.Delta(progressS, S)) > 1f) { progressS = S; progressTime = Time.time; }
        else if (Time.time - progressTime > c.RouteStuckSeconds && CanMove) { Unstuck++; Dir = -Dir; progressS = S; progressTime = Time.time; SteerAhead(true); return; }
        SteerAhead(false);
    }
    void SteerAhead(bool force)
    {
        var c = Tuning; var route = Current;
        float ahead = S + Dir * c.RouteLookahead; if (!route.Closed) ahead = Mathf.Clamp(ahead, 0f, route.Length);
        Vector3 target = LanePoint(route, ahead); var agent = npc.Agent;
        if (force || !hasCarrot || (!agent.hasPath && !agent.pathPending) || (target - carrot).sqrMagnitude > c.RouteRepathDistance * c.RouteRepathDistance)
        { PathTo(target); carrot = target; hasCarrot = true; }
    }
    /// Open-route ends (turn back) and street-crossing transfers passed between prev and cur. True when a transfer began.
    bool Events(CityRoute route, float prev, float cur)
    {
        if (!route.Closed)
        {   // turn back at an open end; a near agent stops stoppingDistance short of its (clamped) steering point
            float end = route.Step + (Riding ? 0f : npc.Agent.stoppingDistance);
            if (Dir > 0f && cur >= route.Length - end) Dir = -1f; else if (Dir < 0f && cur <= end) Dir = 1f;
        }
        float moved = route.Delta(prev, cur); if (moved * Dir <= 0f && route.Closed) return false;
        float span = Mathf.Abs(moved); if (span <= 0f) return false;
        var c = Tuning;
        foreach (var t in route.Transfers)
        {
            float rel = route.Delta(prev, t.At);
            if (rel * Mathf.Sign(moved) <= 0f || Mathf.Abs(rel) > span) continue;
            if ((Routes.Routes[t.To].Use & Use) == 0 || rng.NextDouble() >= c.TransferChance) continue;
            Transfer(t, route.Tangent(t.At) * Mathf.Sign(moved)); return true;
        }
        return false;
    }
    void Transfer(RouteTransfer t, Vector3 heading)
    {
        TransfersTaken++;
        var target = Routes.Routes[t.To]; TargetRoute = t.To; TargetS = t.ToAt;
        ChooseDirection(target, t.ToAt, heading);
        Vector3 to = LanePoint(target, t.ToAt); transferring = true; crossFrom = npc.transform.position; crossTo = to;
        if (Riding) { State = Mode.Crossing; crossFrom = railPos; crossTo = to; crossDone = 0f; crossLength = Vector3.Distance(crossFrom, crossTo); CrossProfile(); }
        else { State = Mode.Joining; joinStarted = Time.time; nextJoinPath = Time.time + Tuning.RepathSeconds; PathTo(to); }
    }
    /// Far street crossing: the NavMesh surface height along the straight link, sampled once per crossing (1 m spacing), so
    /// the rider steps down the kerb onto the road and back up instead of floating at kerb height (no per-frame queries).
    void CrossProfile()
    {
        crossSamples = Mathf.Max(2, Mathf.CeilToInt(crossLength) + 1);
        if (crossHeights.Length < crossSamples) crossHeights = new float[crossSamples];
        for (int i = 0; i < crossSamples; i++)
        {
            Vector3 p = Vector3.Lerp(crossFrom, crossTo, i / (float)(crossSamples - 1));
            crossHeights[i] = NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position.y : p.y;
        }
    }
    float CrossHeight(float t)
    {
        float f = Mathf.Clamp01(t) * (crossSamples - 1); int i = Mathf.Min((int)f, crossSamples - 2);
        return Mathf.Lerp(crossHeights[i], crossHeights[i + 1], f - i);
    }
    void PathTo(Vector3 point)
    {
        var agent = npc.Agent; if (!agent.enabled || !agent.isOnNavMesh) return;
        PathRequests++; if (Riding) RidePathRequests++;
        if (NavMesh.SamplePosition(point, out var hit, Tuning.NavSampleRadius, NavMesh.AllAreas)) agent.SetDestination(hit.position);
    }

    // ------------------------------------------------------------------ far rail (NpcLod)
    /// NpcLod tier change: far starts the analytic rail when following; near hands the body back to the agent.
    public void OnLod(bool far)
    {
        if (far) { if (State == Mode.Following) StartRide(); }
        else if (Riding) StopRide(true);
    }
    void StartRide()
    {
        var agent = npc.Agent; if (Riding || !agent.enabled) return;
        Riding = true; RailStarts++;
        blendOffset = npc.transform.position - LanePoint(Current, S); blend = 1f;
        railPos = npc.transform.position; Velocity = Vector3.zero;
        if (agent.isOnNavMesh) agent.ResetPath();
        agent.updatePosition = false; agent.updateRotation = false;
    }
    /// Back to the NavMeshAgent at the body's exact current position (Warp syncs the agent; no visible move).
    void StopRide(bool resume)
    {
        if (!Riding) return;
        Riding = false; RailStops++; Velocity = Vector3.zero;
        var agent = npc.Agent; if (agent == null) return;
        if (agent.enabled) agent.Warp(npc.transform.position);
        agent.updatePosition = true; agent.updateRotation = true; hasCarrot = false;
        if (!resume) return;
        if (State == Mode.Crossing) { State = Mode.Joining; joinStarted = Time.time; nextJoinPath = Time.time + Tuning.RepathSeconds; PathTo(crossTo); }
    }
    /// Far AI tick while riding: keep the (non-moving) crowd agent at the body so near agents avoid the real position.
    void SyncAgent() { var agent = npc.Agent; if (agent.enabled && agent.isOnNavMesh) agent.nextPosition = npc.transform.position; }
    /// Advance the rail by dt (called by NpcLod). Holds still while the NPC cannot move (frozen, dead, paused, suspended).
    public void Ride(float dt)
    {
        if (!Riding || dt <= 0f) return;
        if (!CanMove) { Velocity = Vector3.zero; return; }
        var c = Tuning;
        if ((npc.transform.position - railPos).sqrMagnitude > c.RailDisplacement * c.RailDisplacement)
        { Displaced++; StopRide(false); State = Mode.None; return; }   // something else moved the body (knockback, lift): the agent takes over
        Vector3 next;
        if (State == Mode.Crossing)
        {
            crossDone = Mathf.Min(crossLength, crossDone + Speed * dt);
            float t = crossLength > 1e-3f ? crossDone / crossLength : 1f;
            next = Vector3.Lerp(crossFrom, crossTo, t); next.y = CrossHeight(t);
            if (crossDone >= crossLength) { Route = TargetRoute; S = TargetS; State = Mode.Following; blend = 0f; }
        }
        else
        {
            var route = Current; float prev = S; S = route.Wrap(S + Dir * Speed * dt);
            if (Events(route, prev, S)) next = railPos;
            else next = LanePoint(route, S);
        }
        if (blend > 0f) { next += blendOffset * blend; blend = Mathf.Max(0f, blend - dt / Mathf.Max(.01f, c.RailBlendSeconds)); }
        Velocity = (next - railPos) / dt;
        var body = npc.transform; body.position = next;
        var flat = new Vector3(Velocity.x, 0f, Velocity.z);
        if (flat.sqrMagnitude > .01f) body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(flat), c.AngularSpeed * dt);
        railPos = next; RideSteps++;
    }

    // ------------------------------------------------------------------ overrides and placement
    /// An override (flee, hostility, combat, encounter, disable) takes the body now: leave the route immediately.
    public void Leave()
    {
        if (!Active) return;
        Leaves++; LastLeaveTime = Time.time; LastLeaveFrame = Time.frameCount;
        StopRide(false); State = Mode.None; hasCarrot = false; transferring = false;
        var agent = npc.Agent; if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
    }
    /// Recycling: put this walker on route r at s (the caller checked the point is unseen).
    public void Place(int r, float s)
    {
        if (Riding) { Riding = false; npc.Agent.updatePosition = true; npc.Agent.updateRotation = true; }
        var route = Routes.Routes[r]; Vector3 p = LanePoint(route, s);
        npc.Agent.Warp(p); npc.Agent.ResetPath();
        Route = r; S = s; Dir = rng.Next(2) == 0 ? 1f : -1f; if (!route.Closed) Dir = s < route.Length * .5f ? 1f : -1f;
        State = Mode.Following; hasCarrot = false; LastFollowTime = Time.time; LastFollowFrame = Time.frameCount;
        if (Far) StartRide();
    }
    /// Flat distance from the body to the current route line (lane ignored), or -1 when not following.
    public float Deviation()
    {
        if (State != Mode.Following || Route < 0) return -1f;
        Current.Project(npc.transform.position, S, 8f, out float lateral); return lateral;
    }
    /// Distance from the body to the path it is walking right now: the route line while following, the straight
    /// street-crossing link during a transfer; -1 while joining/rejoining after an override (not route walking).
    public float PathDeviation()
    {
        if (State == Mode.Following) return Deviation();
        if (!transferring || (State != Mode.Crossing && State != Mode.Joining)) return -1f;
        Vector3 p = npc.transform.position, a = crossFrom, b = crossTo; a.y = b.y = p.y = 0f;
        Vector3 ab = b - a; float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }
    public bool Transferring => transferring && (State == Mode.Crossing || State == Mode.Joining);
}
