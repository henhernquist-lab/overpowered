#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// Phase 8 route verification (real Play Mode, Free Play sessions): routes per district for two seeds + determinism;
/// civilians' distance to the route network vs the old-wander CONTROL; flee leaves the route at once and rejoins the
/// nearest route; a patrol cop responds to Heat from a real prop break and returns; NpcLod far riders are cheap, and
/// promotion/demotion never moves a body in camera view; recycling still relocates unseen civilians. A camera-frustum
/// teleport monitor runs every frame of every session (with a positive CONTROL). Results: Verification/Splines/verify/.
[DefaultExecutionOrder(200)]
public sealed class SplineRouteVerificationRunner : MonoBehaviour
{
    public string Mode, Folder;
    public Action<int> Finished;
    readonly List<string> lines = new List<string>();
    WorldSession W => WorldSession.Instance;
    GameFlow Flow => GameFlow.Instance;
    GameTuning tuning; CityLayout layout; int originalSeed; bool originalEnabled;
    GameModeDefinition freePlay;

    void Log(string text) { lines.Add(text); Debug.Log("[SPLINE VERIFY] " + text); }
    void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL " + text); Log("PASS " + text); }
    static string V(Vector3 v) => $"({v.x:0.0},{v.y:0.00},{v.z:0.0})";
    static string Arg(string name) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    string Diag(CityNpc x)
    {
        var w = x.Walker; var ag = x.Agent; string route = w != null && w.Route >= 0 ? Routes.Routes[w.Route].Name : "-";
        return $"{x.Role} at {V(x.transform.position)} walker {w?.State} route '{route}' S {w?.S:F1}/{(w != null && w.Route >= 0 ? Routes.Routes[w.Route].Length : 0):F1} dir {w?.Dir} riding {w?.Riding} carrot {V(w != null ? w.Carrot : Vector3.zero)} target '{(w != null && w.TargetRoute >= 0 ? Routes.Routes[w.TargetRoute].Name : "-")}'@{w?.TargetS:F1} paths {w?.PathRequests}; agent onMesh {ag.isOnNavMesh} hasPath {ag.hasPath} pending {ag.pathPending} status {ag.pathStatus} remaining {(ag.hasPath ? ag.remainingDistance : -1):F2} dest {V(ag.destination)} vel {ag.velocity.magnitude:F2} speed {ag.speed:F2} stopped {ag.isStopped}; far {NpcLod.Current.IsFar(x)} hostile {x.Hostile} fleeing {x.Fleeing}";
    }

    IEnumerator Start()
    {
        tuning = Resources.Load<GameTuning>("GameTuning"); layout = Resources.Load<CityLayout>("CityLayout");
        originalSeed = tuning.City.Seed; originalEnabled = layout.Routes.Enabled;
        freePlay = Resources.Load<GameModeDefinition>("Modes/free-play");
        var stack = new Stack<IEnumerator>(); stack.Push(Mode == "reload" ? Reload() : Run());
        while (stack.Count > 0)
        {
            bool more = false; object current = null;
            try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
            catch (Exception e) { Log(e.Message.StartsWith("FAIL") ? e.Message : "FAIL EXCEPTION " + e); Finish(1); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (current is IEnumerator next) stack.Push(next); else yield return current;
        }
        Finish(0);
    }
    void Finish(int code)
    {
        tuning.City.Seed = originalSeed; layout.Routes.Enabled = originalEnabled;
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(Path.Combine(Folder, Mode == "reload" ? "reload-results.txt" : "results.txt"), lines);
        Finished(code);
    }
    IEnumerator Scene(string name, int settle = 8)
    {
        float until = Time.realtimeSinceStartup + 120;
        while (Flow.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        { if (Time.realtimeSinceStartup > until) throw new Exception("Scene timeout " + name); yield return null; }
        for (int i = 0; i < settle; i++) yield return null;
    }
    IEnumerator Home() { if (SceneManager.GetActiveScene().name != GameFlow.HomeScene || W != null) { Flow.Home(); yield return Scene(GameFlow.HomeScene); } }
    IEnumerator Launch(int seed)
    {
        yield return Home(); tuning.City.Seed = seed;
        Check(Flow.Select(freePlay), $"Free Play selected (seed {seed}, routes {(layout.Routes.Enabled ? "ON" : "OFF (CONTROL: old wander)")})");
        yield return Scene(GameFlow.CityScene);
        W.Hero.enabled = false; monitoring = true; renderMain = true;   // the gameplay camera renders every frame, as on a player's screen
    }
    void Move(Vector3 p) { var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false; W.Hero.transform.position = p; cc.enabled = true; W.Hero.ResetMotion(); Physics.SyncTransforms(); }
    IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    IEnumerator Realtime(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }
    IEnumerator Until(Func<bool> done, float seconds) { float until = Time.realtimeSinceStartup + seconds; while (!done() && Time.realtimeSinceStartup < until) yield return null; }
    CityRoutes Routes => W.City.Routes;
    ThirdPersonCamera Follow => Camera.main.GetComponent<ThirdPersonCamera>();
    void Look(Vector3 at) { var d = at - W.Hero.transform.position; Follow.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 12f); }

    // ------------------------------------------------------------------ camera-frustum teleport monitor (every frame)
    sealed class Track { public Vector3 Pos; public bool Visible; public int Frame; }
    readonly Dictionary<CityNpc, Track> tracks = new Dictionary<CityNpc, Track>();
    bool monitoring; int suppressUntilFrame = -1;
    int monitorFrames, visibleSteps, hitchFrames, violations, unseenJumps; float maxVisibleStep, maxVisibleSpeed, maxRiderStep, maxAgentStep; string worst = "none", worstStep = "none";
    const float TeleportLimit = 1f, HitchLimit = 2.5f; // m per frame while visible in both frames (hitch: dt > .2 s)
    bool Visible(Camera cam, Plane[] planes, Vector3 p) => (p - cam.transform.position).magnitude < W.City.Art.Rendering.ActorCull
        && GeometryUtility.TestPlanesAABB(planes, new Bounds(p + Vector3.up * .9f, new Vector3(.9f, 1.8f, .9f)));
    bool VisibleNow(CityNpc npc) { var cam = Camera.main; return cam != null && Visible(cam, GeometryUtility.CalculateFrustumPlanes(cam), npc.transform.position); }
    /// Batch mode has no Game view, so the gameplay camera never renders by itself and Renderer.isVisible stays false.
    /// While on, the runner renders it once per frame (after ThirdPersonCamera placed it), as a player's screen would.
    bool renderMain; RenderTexture mainTarget;
    void LateUpdate()
    {
        if (renderMain && W != null && Camera.main != null && Flow != null && !Flow.Loading)
        {
            if (mainTarget == null) { mainTarget = new RenderTexture(1280, 720, 24); mainTarget.Create(); }
            var view = Camera.main; view.targetTexture = mainTarget; view.Render(); view.targetTexture = null;
        }
        if (!monitoring || W == null || W.City == null || Camera.main == null || Flow == null || Flow.Loading) { tracks.Clear(); return; }
        var cam = Camera.main; var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        float dt = Time.deltaTime; bool hitch = dt > .2f; monitorFrames++; if (hitch) hitchFrames++;
        foreach (var npc in W.Npcs)
        {
            if (npc == null || npc.Dead) continue;
            var p = npc.transform.position; bool vis = Visible(cam, planes, p);
            if (tracks.TryGetValue(npc, out var t) && t.Frame == Time.frameCount - 1)
            {
                float step = Vector3.Distance(p, t.Pos);
                if (t.Visible && vis)
                {
                    visibleSteps++;
                    if (!hitch && Time.frameCount > suppressUntilFrame)
                    {
                        bool rider = npc.Walker != null && npc.Walker.Riding;
                        if (step > maxVisibleStep) worstStep = $"{npc.Role} {(rider ? "riding" : "agent")} {npc.Walker?.State} step {step:F3} m dt {dt:F3} at {V(p)}";
                        maxVisibleStep = Mathf.Max(maxVisibleStep, step); if (dt > 0) maxVisibleSpeed = Mathf.Max(maxVisibleSpeed, step / dt);
                        if (rider) maxRiderStep = Mathf.Max(maxRiderStep, step); else maxAgentStep = Mathf.Max(maxAgentStep, step);
                    }
                    if (Time.frameCount > suppressUntilFrame && step > (hitch ? HitchLimit : TeleportLimit))
                    { violations++; worst = $"{npc.Role} {npc.name} moved {step:F2} m in one frame (dt {dt:F3}) while visible, walker {npc.Walker?.State}/{(npc.Walker != null && npc.Walker.Riding ? "riding" : "agent")} at {V(p)}"; }
                }
                else if (step > 2f) unseenJumps++;
            }
            tracks[npc] = new Track { Pos = p, Visible = vis, Frame = Time.frameCount };
        }
    }

    // ------------------------------------------------------------------ run
    IEnumerator Run()
    {
        yield return Scene(GameFlow.HomeScene);
        int seedA = originalSeed, seedB = originalSeed + 817;
        var s = layout.Routes; var n = tuning.Npcs;
        var sections = (Arg("-splineSections") ?? "seeds,deviation,districts,flee,lod,cops,control").Split(',');
        bool On(string name) => Array.IndexOf(sections, name) >= 0;
        Log($"SECTIONS {string.Join(",", sections)}");
        Log($"TUNABLES CityLayout.Routes: enabled={s.Enabled} sidewalkInset={s.SidewalkInset} patrolBlocks={s.PatrolBlocks} boulevardOffset={s.BoulevardOffset} cornerRadius={s.CornerRadius} bakeSpacing={s.BakeSpacing} transferSpacing={s.TransferSpacing} transferDistance={s.TransferDistance} laneHalfWidth={s.LaneHalfWidth} minLength={s.MinLength}");
        Log($"TUNABLES GameTuning.Npcs: civilianSpeed={n.CivilianSpeed} patrolSpeed={n.PatrolSpeed} speedJitter={n.RouteSpeedJitter} lookahead={n.RouteLookahead} repath={n.RouteRepathDistance} rejoinRadius={n.RejoinRadius} leaveDistance={n.RouteLeaveDistance} transferChance={n.TransferChance} railBlend={n.RailBlendSeconds}s; NpcLod near={layout.NpcLod.NearRadius}+/-{layout.NpcLod.Hysteresis} farTick={layout.NpcLod.FarThinkInterval}s recycle>{layout.NpcLod.RecycleRadius} m -> {layout.NpcLod.RecycleDistance}");
        // Plan-level determinism (pure data, no Play Mode state involved).
        var planA1 = PlanPrint(seedA); var planA2 = PlanPrint(seedA); var planB = PlanPrint(seedB);
        Check(planA1 == planA2 && planA1 != planB, $"CityLayout.Plan routes are pure data: seed {seedA} twice identical ({planA1.Length} chars), seed {seedB} different.");

        var prints = new Dictionary<string, string>();
        if (On("seeds")) foreach (var (seed, tag) in new[] { (seedA, "A1"), (seedA, "A2"), (seedB, "B") })
        {
            yield return Launch(seed);
            prints[tag] = Routes.Fingerprint();
            RouteStats(tag, seed);
            if (tag != "A2") { yield return Realtime(4f); TopDown($"routes-seed-{seed}-topdown"); }
            File.WriteAllText(Path.Combine(Folder, "saves", $"route-fingerprint-{seed}.txt"), prints[tag]);
        }
        if (On("seeds")) Check(prints["A1"] == prints["A2"], $"DETERMINISM: seed {seedA} built twice in Play Mode gives identical baked routes ({Routes.Routes.Count} routes, fingerprint {prints["A1"].Length} chars, hash {prints["A1"].GetHashCode():X8}).");
        if (On("seeds")) Check(prints["A1"] != prints["B"], $"Different seed {seedB} gives different routes (hash {prints["B"].GetHashCode():X8} vs {prints["A1"].GetHashCode():X8}).");

        if (On("deviation")) yield return Deviation(seedA);
        if (On("districts")) yield return Districts(seedA);
        if (On("flee")) yield return Flee(seedA);
        if (On("lod")) yield return Lod(seedA);
        if (On("cops")) yield return Cops(seedA);
        if (On("control")) yield return MonitorControl();
        Log($"TELEPORT MONITOR over {monitorFrames} frames ({hitchFrames} hitch frames dt>.2 s): {visibleSteps} NPC steps visible to the gameplay camera in both frames; max visible step {(beforeControlStep >= 0 ? beforeControlStep : maxVisibleStep):F3} m, max visible speed {(beforeControlSpeed >= 0 ? beforeControlSpeed : maxVisibleSpeed):F2} m/s (before the CONTROL warp; largest: {worstStep}); max visible step by far-rail riders {maxRiderStep:F3} m, by agent-driven bodies {maxAgentStep:F3} m; relocations > 2 m while NOT visible (recycling/spawn): {unseenJumps}; violations (> {TeleportLimit} m, > {HitchLimit} m on hitch frames) {violations - controlViolations} excluding the CONTROL; worst: {worst}.");
        Check(violations - controlViolations == 0, "No NPC moved more than the teleport limit in one frame while inside the gameplay camera frustum (all sessions of this run).");
        yield return Home();
        Log("LIMIT: batch-mode Play Mode, hero parked (no input); deviation is measured to the route network, not judged by eye; no human playtest of crowd feel.");
    }

    string PlanPrint(int seed)
    {
        int keep = tuning.City.Seed; tuning.City.Seed = seed;
        var plan = layout.Plan(tuning.City); tuning.City.Seed = keep;
        var sb = new System.Text.StringBuilder();
        foreach (var r in plan.Routes) { sb.Append(r.Name).Append(r.Kind).Append((int)r.Use).Append(r.Closed); foreach (var p in r.Points) sb.Append(V(p)); sb.Append('\n'); }
        return sb.ToString();
    }

    void RouteStats(string tag, int seed)
    {
        var routes = Routes; var plan = W.City.Plan;
        var stage = W.City.Timings.FirstOrDefault(t => t.Key.StartsWith("routes"));
        Log($"[{tag}] seed {seed}: {routes.Routes.Count}/{plan.Routes.Count} planned routes baked in {routes.BuildMilliseconds:F1} ms (stage '{stage.Key}' {stage.Value:F1} ms), dropped {routes.Dropped.Count}{(routes.Dropped.Count > 0 ? ": " + string.Join("; ", routes.Dropped) : "")}; {routes.TransferCount} street-crossing transfers; {routes.Containers.Count} SplineContainers holding {routes.Containers.Sum(c => c.Splines.Count)} Unity Splines.");
        for (int d = 0; d < plan.Districts.Count; d++)
        {
            var rs = routes.Routes.Where(r => r.District == d).ToList();
            int walk = rs.Count(r => (r.Use & RouteUse.Walk) != 0), patrol = rs.Count(r => (r.Use & RouteUse.Patrol) != 0);
            string kinds = string.Join(", ", rs.GroupBy(r => r.Kind).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
            Check(walk > 0 && patrol > 0, $"[{tag}] {plan.Districts[d].Name}: {rs.Count} routes ({kinds}); walk {walk} / patrol {patrol}; {rs.Sum(r => r.Length):F0} m; off-NavMesh samples {rs.Sum(r => r.OffMesh)}/{rs.Sum(r => r.Points.Length)}; transfers {rs.Sum(r => r.Transfers.Count)}.");
        }
        float worstLength = routes.Routes.Max(r => Mathf.Abs(r.Spline.GetLength() - r.Length) / r.Length);
        Check(worstLength < .01f, $"[{tag}] Every route is a Unity Spline (Bezier corner fillets): baked arc length within {worstLength:P2} of Spline.GetLength() for the worst route.");
        int onRoute = plan.Sidewalks.Count(p => Routes.Nearest(p, RouteUse.Walk, 3f, out _, out _, out _));
        Log($"[{tag}] {onRoute}/{plan.Sidewalks.Count} legacy sidewalk points lie within 3 m of a walk route.");
    }

    // ------------------------------------------------------------------ captures with a pixel overlay (verification only)
    static Color32 ColorOf(RouteUse use) => use == RouteUse.Patrol ? new Color32(40, 90, 255, 255) : use == RouteUse.Walk ? new Color32(30, 220, 70, 255) : new Color32(20, 230, 230, 255);
    static void Plot(Color32[] px, int w, int h, int x, int y, Color32 c, int r)
    { for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++) { int u = x + i, v = y + j; if (u >= 0 && v >= 0 && u < w && v < h) px[v * w + u] = c; } }
    static void Line(Color32[] px, int w, int h, Vector2 a, Vector2 b, Color32 c, int r)
    {
        int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))) + 1; if (steps > 20000) return;
        for (int i = 0; i <= steps; i++) { var p = Vector2.Lerp(a, b, i / (float)steps); Plot(px, w, h, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), c, r); }
    }
    Texture2D Render(Camera c, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24); rt.Create(); c.targetTexture = rt;
        float shadow = QualitySettings.shadowDistance; bool fog = RenderSettings.fog;
        if (c.orthographic) { QualitySettings.shadowDistance = Mathf.Max(shadow, 1400f); RenderSettings.fog = false; }
        c.Render(); QualitySettings.shadowDistance = shadow; RenderSettings.fog = fog;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; c.targetTexture = null; rt.Release(); Destroy(rt);
        return tex;
    }
    void Overlay(Texture2D tex, Func<Vector3, Vector2?> toPx, bool transfers, string name)
    {
        int w = tex.width, h = tex.height; var px = tex.GetPixels32();
        foreach (var r in Routes.Routes)
        {
            var c = ColorOf(r.Use);
            for (int i = 0; i < r.Segments; i++) { var a = toPx(r.Points[i]); var b = toPx(r.Points[(i + 1) % r.Points.Length]); if (a.HasValue && b.HasValue) Line(px, w, h, a.Value, b.Value, c, 1); }
            if (!transfers) continue;
            foreach (var t in r.Transfers) { var a = toPx(r.Position(t.At)); var b = toPx(Routes.Routes[t.To].Position(t.ToAt)); if (a.HasValue && b.HasValue) Line(px, w, h, a.Value, b.Value, new Color32(255, 220, 0, 255), 0); }
        }
        foreach (var npc in W.Npcs)
        {
            if (npc == null || npc.Dead) continue; var p = toPx(npc.transform.position); if (!p.HasValue) continue;
            var c = npc.Role == NpcRole.Civilian ? new Color32(255, 140, 0, 255) : npc.Role == NpcRole.Criminal ? new Color32(220, 0, 0, 255) : new Color32(10, 10, 120, 255);
            Plot(px, w, h, Mathf.RoundToInt(p.Value.x), Mathf.RoundToInt(p.Value.y), c, 3);
            Plot(px, w, h, Mathf.RoundToInt(p.Value.x), Mathf.RoundToInt(p.Value.y), new Color32(255, 255, 255, 255), 1);
        }
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), tex.EncodeToPNG()); Destroy(tex);
    }
    void TopDown(string name)
    {
        var island = W.City.Plan.Island; float half = Mathf.Max(island.width, island.height) * .5f + 12f; var centre = island.center;
        var go = new GameObject("route overlay camera"); var c = go.AddComponent<Camera>();
        c.orthographic = true; c.orthographicSize = half; c.transform.position = new Vector3(centre.x, 450f, centre.y); c.transform.rotation = Quaternion.Euler(90, 0, 0);
        c.nearClipPlane = 1; c.farClipPlane = 900; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black; c.enabled = false;
        const int N = 1600; var tex = Render(c, N, N); Destroy(go);
        Overlay(tex, p => new Vector2((p.x - (centre.x - half)) / (2 * half) * N, (p.z - (centre.y - half)) / (2 * half) * N), true, name);
        Log($"CAPTURE {name}.png: orthographic top-down, north (+Z) up, x {centre.x - half:F0}..{centre.x + half:F0}; overlay: green = walk routes, blue = patrol loops, cyan = walk+patrol (boulevard promenades, paths, quay), yellow = street-crossing transfers; orange dots civilians, dark blue police.");
    }
    /// Orthographic top-down crop (half-size m) around focus with the same overlay (streets and sidewalks readable).
    void Crop(string name, Vector3 focus, float half)
    {
        var go = new GameObject("route crop camera"); var c = go.AddComponent<Camera>(); c.enabled = false;
        c.orthographic = true; c.orthographicSize = half; c.transform.position = new Vector3(focus.x, 300f, focus.z); c.transform.rotation = Quaternion.Euler(90, 0, 0);
        c.nearClipPlane = 1; c.farClipPlane = 600; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black;
        const int N = 1200; var tex = Render(c, N, N); Destroy(go);
        Overlay(tex, p => new Vector2((p.x - (focus.x - half)) / (2 * half) * N, (p.z - (focus.z - half)) / (2 * half) * N), true, name);
        Log($"CAPTURE {name}.png: orthographic top-down {2 * half:F0} m square centred {V(focus)}, north up; overlay: green walk, blue patrol, cyan both, yellow crossings; orange civilians, dark blue police (white centre).");
    }
    /// The real gameplay camera (ThirdPersonCamera placement behind the parked hero), no overlay: what a player sees.
    IEnumerator Street(string name, Vector3 focus)
    {
        var cam = Camera.main; var near = W.Npcs.Where(x => x != null && !x.Dead && x.Role == NpcRole.Civilian).OrderBy(x => Vector3.Distance(x.transform.position, focus)).FirstOrDefault();
        if (near != null) Look(near.transform.position);
        cam.GetComponent<ThirdPersonCamera>().enabled = true; yield return Frames(3);
        const int w = 1280, h = 720; var tex = Render(cam, w, h);
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), tex.EncodeToPNG()); Destroy(tex);
        Log($"CAPTURE {name}.png: gameplay camera behind the parked hero at {V(W.Hero.transform.position)}, looking toward the nearest civilian ({(near != null ? Vector3.Distance(near.transform.position, focus).ToString("F0") + " m" : "none")}); no overlay.");
    }

    // ------------------------------------------------------------------ deviation vs CONTROL
    sealed class Dev
    {
        public readonly List<float> Nearest = new List<float>(), Own = new List<float>(), NearTier = new List<float>(), FarTier = new List<float>(), Path = new List<float>();
        public int Samples, Following, Riding, Joining, Idle, Crossing; public float Travelled, Time;
        public readonly HashSet<int> Routes = new HashSet<int>(); public readonly HashSet<CityNpc> Npcs = new HashSet<CityNpc>();
    }
    static float Q(List<float> v, float q) { if (v.Count == 0) return float.NaN; var c = v.OrderBy(x => x).ToList(); return c[Mathf.Clamp(Mathf.FloorToInt(q * (c.Count - 1) + .5f), 0, c.Count - 1)]; }
    string Stats(List<float> v) => v.Count == 0 ? "n/a" : $"median {Q(v, .5f):F2} m, p95 {Q(v, .95f):F2} m, max {v.Max():F2} m (n={v.Count})";
    IEnumerator Sample(float seconds, Dev dev, Func<CityNpc, bool> filter, RouteUse use = RouteUse.Walk, NpcRole role = NpcRole.Civilian, Dev cops = null)
    {
        var lod = NpcLod.Current; var last = new Dictionary<CityNpc, (Vector3 P, float T)>(); var target = dev;
        float until = Time.realtimeSinceStartup + seconds, next = 0f;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
            if (Time.time < next) continue; next = Time.time + .25f;
            foreach (var npc in W.Npcs)
            {
                if (npc == null || npc.Dead || npc.Encounter != null || npc.Fleeing || npc.Hostile) continue;
                if (filter != null && !filter(npc)) continue;
                var bucket = npc.Role == role ? target : cops != null && npc.Role == NpcRole.Cop ? cops : null; if (bucket == null) continue;
                var p = npc.transform.position; Routes.Nearest(p, bucket == cops ? RouteUse.Patrol : use, 400f, out _, out _, out float d);
                bucket.Nearest.Add(d); (lod != null && lod.IsFar(npc) ? bucket.FarTier : bucket.NearTier).Add(d); bucket.Samples++; bucket.Npcs.Add(npc);
                var w = npc.Walker;
                if (w != null && W.City.RoutesActive)
                {
                    if (w.State == RouteWalker.Mode.Following) { bucket.Following++; bucket.Routes.Add(w.Route); float own = w.Deviation(); if (own >= 0) bucket.Own.Add(own); }
                    else if (w.Transferring) bucket.Crossing++;
                    else if (w.State == RouteWalker.Mode.Joining || w.State == RouteWalker.Mode.Crossing) bucket.Joining++; else bucket.Idle++;
                    float path = w.PathDeviation(); if (path >= 0) bucket.Path.Add(path);
                    if (w.Riding) bucket.Riding++;
                }
                if (last.TryGetValue(npc, out var q)) { float step = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(q.P.x, 0, q.P.z)); if (step < 5f) { bucket.Travelled += step; bucket.Time += Time.time - q.T; } }
                last[npc] = (p, Time.time);
            }
        }
    }
    string Describe(Dev d) => $"{d.Samples} samples of {d.Npcs.Count} NPCs: ROUTE-WALKING deviation (route line while following, crossing link while crossing a street) {Stats(d.Path)}; own-route deviation while following {Stats(d.Own)}; distance to nearest route line {Stats(d.Nearest)} (near tier {Stats(d.NearTier)}; far tier {Stats(d.FarTier)}; within rejoin radius {Frac(d.Nearest, tuning.Npcs.RejoinRadius):P0}); states following {Share(d.Following, d.Samples)}, crossing a street {Share(d.Crossing, d.Samples)}, joining/rejoining {Share(d.Joining, d.Samples)}, none {Share(d.Idle, d.Samples)}, riding the far rail {Share(d.Riding, d.Samples)}; mean speed {(d.Time > 0 ? d.Travelled / d.Time : 0):F2} m/s; {d.Routes.Count} distinct routes walked";
    static float Frac(List<float> v, float r) => v.Count == 0 ? 0 : v.Count(x => x <= r) / (float)v.Count;
    static string Share(int a, int b) => b == 0 ? "0%" : (a / (float)b).ToString("P0");

    IEnumerator Deviation(int seed)
    {
        var on = new Dev(); var control = new Dev();
        yield return Launch(seed); Move(W.City.Spawn + Vector3.up * .1f);
        yield return Realtime(6f);
        yield return Sample(40f, on, null);
        Log($"DEVIATION routes ON (seed {seed}, hero parked at spawn, 40 s real time, 24 civilians): {Describe(on)}.");
        Crop("downtown-spawn-routes-on", W.City.Spawn, 90f);
        layout.Routes.Enabled = false;
        yield return Launch(seed); Move(W.City.Spawn + Vector3.up * .1f);
        Check(!W.City.RoutesActive && W.Npcs.All(x => x == null || x.Walker == null || !x.Walker.Active), "CONTROL session: routes disabled, no walker active (old RandomSidewalkNear wander).");
        yield return Realtime(6f);
        yield return Sample(40f, control, null);
        Log($"DEVIATION CONTROL old wander (same seed/spawn/time): {Describe(control)}.");
        Crop("downtown-spawn-control-wander", W.City.Spawn, 90f);
        layout.Routes.Enabled = true;
        float onMedian = Q(on.Nearest, .5f), onP95 = Q(on.Nearest, .95f), cMedian = Q(control.Nearest, .5f), cP95 = Q(control.Nearest, .95f);
        float pMedian = Q(on.Path, .5f), pP95 = Q(on.Path, .95f);
        Check(on.Following + on.Crossing >= on.Samples * .9f && pP95 <= 2f && pMedian <= .8f,
            $"Civilians stay on their routes: {Share(on.Following + on.Crossing, on.Samples)} of samples walking a route or a street-crossing link; route-walking deviation median {pMedian:F2} m / p95 {pP95:F2} m / max {on.Path.Max():F2} m (limits: >= 90%, <= 0.8 m, <= 2 m).");
        Check(on.Time > 0 && on.Travelled / on.Time >= tuning.Npcs.CivilianSpeed * .6f, $"Route walkers keep walking: mean speed {on.Travelled / Mathf.Max(.01f, on.Time):F2} m/s (CivilianSpeed {tuning.Npcs.CivilianSpeed}).");
        Check(cMedian > onMedian && cP95 > onP95 && Frac(control.Nearest, tuning.Npcs.RejoinRadius) < Frac(on.Nearest, tuning.Npcs.RejoinRadius),
            $"CONTROL old wander is farther from the route lines: median {cMedian:F2} vs {onMedian:F2} m, p95 {cP95:F2} vs {onP95:F2} m, within {tuning.Npcs.RejoinRadius} m {Frac(control.Nearest, tuning.Npcs.RejoinRadius):P0} vs {Frac(on.Nearest, tuning.Npcs.RejoinRadius):P0}.");
    }

    // ------------------------------------------------------------------ per district
    IEnumerator Districts(int seed)
    {
        yield return Launch(seed);
        var plan = W.City.Plan;
        for (int d = 0; d < plan.Districts.Count; d++)
        {
            var def = plan.Districts[d]; var c = def.Area.center; Vector3 centre = new Vector3(c.x, 0, c.y);
            Check(Routes.Nearest(centre, RouteUse.Walk, 200f, out int r, out float s, out _), $"{def.Name}: a walk route near the district centre.");
            Vector3 spot = Routes.Routes[r].Position(s);
            Move(spot + Vector3.up * .15f); Look(spot + Vector3.forward * 20f);
            if (W.Heat < 3) W.AddHeat(3 - W.Heat); W.ReconcilePolice();       // Heat-spawned patrol police near the hero (hero side: not hostile)
            int recycled0 = NpcLod.Current.Recycled;
            yield return Realtime(14f);
            var civ = new Dev();
            float band = layout.NpcLod.RecycleDistance.y + 10f;
            var copDev = new Dev(); yield return Sample(14f, civ, x => Vector3.Distance(x.transform.position, spot) < band, RouteUse.Walk, NpcRole.Civilian, copDev);
            int inDistrict = civ.Routes.Count(x => Routes.Routes[x].District == d), copRoutes = copDev.Routes.Count(x => Routes.Routes[x].District == d);
            Log($"DISTRICT {def.Name}: hero at {V(spot)} (route '{Routes.Routes[r].Name}'); recycled {NpcLod.Current.Recycled - recycled0} civilians into the area; civilians: {Describe(civ)}; routes walked in this district {inDistrict}.");
            Log($"DISTRICT {def.Name} police (Heat 3, hero side = patrol, not hostile): {Describe(copDev)}; patrol routes in this district {copRoutes}.");
            foreach (var x in copDev.Npcs) if (x != null) Log($"DIAG {def.Name} police: {Diag(x)}");
            Check(civ.Npcs.Count >= 4 && civ.Following + civ.Crossing >= civ.Samples * .8f && Q(civ.Path, .95f) <= 2.5f && inDistrict > 0,
                $"{def.Name}: {civ.Npcs.Count} civilians on {inDistrict} of this district's walk routes, on a route or crossing {Share(civ.Following + civ.Crossing, civ.Samples)}, route-walking deviation p95 {Q(civ.Path, .95f):F2} m.");
            Check(copDev.Npcs.Count > 0 && copDev.Following > 0 && Q(copDev.Path, .5f) <= 1.5f,
                $"{def.Name}: {copDev.Npcs.Count} police patrolling ({Share(copDev.Following, copDev.Samples)} following patrol routes, {copDev.Routes.Count} routes, route-walking deviation median {Q(copDev.Path, .5f):F2} m).");
            Crop($"district-{def.Name.ToLowerInvariant()}-routes", spot, 90f);
            yield return Street($"district-{def.Name.ToLowerInvariant()}-street", spot);
            W.AddHeat(-W.Heat); W.ReconcilePolice(); yield return Frames(2);   // back to the patrol count, so the next district's Heat spawns police there
        }
    }

    // ------------------------------------------------------------------ flee -> rejoin nearest
    IEnumerator Flee(int seed)
    {
        yield return Launch(seed); Move(W.City.Spawn + Vector3.up * .1f);
        yield return Realtime(6f);
        var lod = NpcLod.Current; var hero = W.Hero.transform.position;
        CityNpc target = null;
        float until = Time.realtimeSinceStartup + 20f;
        while (target == null && Time.realtimeSinceStartup < until)
        {
            target = W.Npcs.Where(x => x != null && !x.Dead && x.Role == NpcRole.Civilian && x.Walker != null && x.Walker.State == RouteWalker.Mode.Following && !lod.IsFar(x)
                && Vector3.Distance(x.transform.position, hero) < 45f).OrderBy(x => Vector3.Distance(x.transform.position, hero)).FirstOrDefault();
            if (target != null) Look(target.transform.position);
            yield return Frames(3);
            if (target != null && !VisibleNow(target)) target = null;
        }
        Check(target != null, "A near civilian following a route inside the camera view.");
        var w = target.Walker; var route0 = Routes.Routes[w.Route]; float s0 = w.S;
        var bystander = W.Npcs.Where(x => x != null && x != target && x.Role == NpcRole.Civilian && x.Walker != null && x.Walker.State == RouteWalker.Mode.Following
            && Vector3.Distance(x.transform.position, target.transform.position) > tuning.Npcs.AlarmRadius + 10f).FirstOrDefault();
        int bystanderLeaves = bystander != null ? bystander.Walker.Leaves : 0;
        int leaves0 = w.Leaves, frame0 = Time.frameCount, joins0 = w.Joins; float t0 = Time.time;
        Vector3 alarm = target.transform.position + route0.Normal(s0) * 3f;
        W.Alarm(alarm);
        yield return Until(() => w.Leaves > leaves0, 3f);
        Check(target.Fleeing && w.Leaves > leaves0 && !w.Active && w.LastLeaveFrame - frame0 <= 2,
            $"Scared civilian leaves its route immediately: alarm at frame {frame0}, route '{route0.Name}' released at frame {w.LastLeaveFrame} ({w.LastLeaveFrame - frame0} frame(s), {w.LastLeaveTime - t0:F3} s), fleeing={target.Fleeing}.");
        float maxOff = 0f; var start = target.transform.position;
        until = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < until) { route0.Project(target.transform.position, s0, 30f, out float lat); maxOff = Mathf.Max(maxOff, lat); yield return null; }
        Check(maxOff > tuning.Npcs.RejoinRadius && Vector3.Distance(target.transform.position, alarm) > Vector3.Distance(start, alarm) + 3f,
            $"It really runs off the route: up to {maxOff:F1} m from '{route0.Name}' in 3 s, {Vector3.Distance(start, alarm):F1} -> {Vector3.Distance(target.transform.position, alarm):F1} m from the scare.");
        if (bystander != null) Check(bystander.Walker.Leaves == bystanderLeaves && bystander.Walker.State != RouteWalker.Mode.None, $"CONTROL: a civilian {Vector3.Distance(bystander.transform.position, alarm):F0} m from the scare (outside AlarmRadius {tuning.Npcs.AlarmRadius} m) keeps walking its route ({bystander.Walker.State}).");
        yield return Until(() => !target.Fleeing, tuning.Npcs.FleeSeconds + 2f);
        Check(!target.Fleeing, $"Flee ends after {Time.time - t0:F1} s.");
        Vector3 end = target.transform.position; Routes.Nearest(end, RouteUse.Walk, 400f, out int rStar, out float sStar, out float dStar);
        float tEnd = Time.time;
        yield return Until(() => w.State == RouteWalker.Mode.Following, 30f);
        Check(w.State == RouteWalker.Mode.Following && w.Joins > joins0 && (w.TargetRoute == rStar || w.JoinDistance <= dStar + .5f),
            $"Afterwards it rejoins the NEAREST walk route: flee ended at {V(end)}, nearest '{Routes.Routes[rStar].Name}' {dStar:F1} m away; joined '{Routes.Routes[w.Route].Name}' (join distance {w.JoinDistance:F1} m) and was following again {Time.time - tEnd:F1} s later.");
        yield return Realtime(3f);
        Check(w.State == RouteWalker.Mode.Following && w.Deviation() <= tuning.Npcs.RejoinRadius + .5f, $"Back on the route 3 s later: deviation {w.Deviation():F2} m, state {w.State}.");

        // Far rider scared: leaves the rail at its next (reduced-rate) AI tick.
        var rider = W.Npcs.Where(x => x != null && x.Role == NpcRole.Civilian && x.Walker != null && x.Walker.Riding && Vector3.Distance(x.transform.position, hero) > 75f).FirstOrDefault();
        if (rider == null) { Log("NOTE no far civilian riding at this moment; far-flee check skipped."); yield break; }
        var rw = rider.Walker; int rl = rw.Leaves; float rt = Time.time;
        W.Alarm(rider.transform.position + Vector3.right * 2f);
        yield return Until(() => rw.Leaves > rl, 3f);
        Check(!rw.Riding && rider.Fleeing && rw.LastLeaveTime - rt <= layout.NpcLod.FarThinkInterval + .1f, $"FAR rider ({Vector3.Distance(rider.transform.position, hero):F0} m) scared: left the rail after {rw.LastLeaveTime - rt:F3} s (far AI tick {layout.NpcLod.FarThinkInterval} s); body back on its NavMeshAgent (updatePosition={rider.Agent.updatePosition}).");
    }

    // ------------------------------------------------------------------ NpcLod
    IEnumerator Lod(int seed)
    {
        yield return Launch(seed); Move(W.City.Spawn + Vector3.up * .1f);
        yield return Realtime(8f);
        var lod = NpcLod.Current; var hero = W.Hero.transform.position;
        var riders = W.Npcs.Where(x => x != null && !x.Dead && x.Walker != null && x.Walker.Riding).ToList();
        Check(riders.Count >= 3, $"{riders.Count} far NPCs are riding their routes (near {lod.NearCount}, far {lod.FarCount}).");
        var before = riders.ToDictionary(x => x, x => (x.Walker.PathRequests, x.Walker.RideSteps, lod.Thinks(x), x.Walker.RailStops, x.Walker.Route, x.Walker.S, x.Walker.TransfersTaken, x.Walker.State));
        int f0 = Time.frameCount; float t0 = Time.time;
        yield return Realtime(4f);
        float secs = Time.time - t0; int frames = Time.frameCount - f0;
        var steady = riders.Where(x => x != null && x.Walker.Riding && x.Walker.RailStops == before[x].RailStops).ToList();
        int pathReq = steady.Sum(x => x.Walker.PathRequests - before[x].PathRequests), thinks = steady.Sum(x => lod.Thinks(x) - before[x].Item3), steps = steady.Sum(x => x.Walker.RideSteps - before[x].RideSteps);
        var speeds = steady.Where(x => x.Walker.Route == before[x].Route && x.Walker.TransfersTaken == before[x].TransfersTaken && Routes.Routes[x.Walker.Route].Closed
                && before[x].State == RouteWalker.Mode.Following && x.Walker.State == RouteWalker.Mode.Following)
            .Select(x => Mathf.Abs(Routes.Routes[x.Walker.Route].Delta(before[x].S, x.Walker.S)) / secs / x.Walker.Speed).ToList();
        var seen = steady.Where(x => x.GetComponentsInChildren<Renderer>().Any(r => r.isVisible)).ToList();
        Log($"FAR riders on screen now (Renderer.isVisible with the gameplay camera rendering every frame): {seen.Count}/{steady.Count}; their rail steps {string.Join(", ", seen.Select(x => ((x.Walker.RideSteps - before[x].RideSteps) / secs).ToString("F0") + "/s"))} vs {frames / secs:F0} frames/s.");
        Check(steady.Count > 0 && pathReq == 0 && riders.Sum(x => x == null ? 0 : x.Walker.RidePathRequests) == 0 && thinks <= steady.Count * (secs / layout.NpcLod.FarThinkInterval + 2),
            $"FAR riders are cheap: {steady.Count} riders over {frames} frames / {secs:F1} s made {pathReq} NavMesh path requests, {thinks} AI ticks (~{thinks / Mathf.Max(1, steady.Count) / secs:F1}/s each; far tick {layout.NpcLod.FarThinkInterval} s), {steps} rail steps (~{steps / (float)Mathf.Max(1, steady.Count) / secs:F1}/s each: per frame while visible, per far tick otherwise); animators off: {steady.All(x => !x.GetComponent<HumanoidPresentation>().Animator.enabled)}.");
        if (speeds.Count > 0) Check(speeds.All(v => v > .8f && v < 1.2f), $"FAR riders advance along the spline at their walking speed: progress/speed ratio {string.Join(", ", speeds.Select(v => v.ToString("F2")))}.");

        // Promotion in camera view: the hero moves 30 m from a far rider and looks at it.
        var r = W.Npcs.Where(x => x != null && x.Walker != null && x.Walker.Riding && x.Role == NpcRole.Civilian).OrderBy(x => Mathf.Abs(Vector3.Distance(x.transform.position, hero) - 85f)).FirstOrDefault();
        Check(r != null, "A far civilian rider for the promotion test.");
        var away = hero - r.transform.position; away.y = 0; away = away.normalized;
        NavMesh.SamplePosition(r.transform.position + away * 30f, out var stand, 8f, NavMesh.AllAreas);
        var trace = new List<(int F, Vector3 P, bool Far, bool Riding, bool Vis)>();
        Move(stand.position + Vector3.up * .1f); Look(r.transform.position);
        for (int i = 0; i < 40; i++) { trace.Add((Time.frameCount, r.transform.position, lod.IsFar(r), r.Walker.Riding, VisibleNow(r))); yield return null; }
        int promotedAt = trace.FindIndex(x => !x.Far);
        float maxStep = 0f; for (int i = 1; i < trace.Count; i++) maxStep = Mathf.Max(maxStep, Vector3.Distance(trace[i].P, trace[i - 1].P));
        Check(promotedAt >= 0 && !r.Walker.Riding && r.Agent.updatePosition && trace.Skip(promotedAt).Any(x => x.Vis) && maxStep < .5f,
            $"PROMOTION in view is seamless: promoted {promotedAt} frame(s) after the hero arrived ({Vector3.Distance(r.transform.position, W.Hero.transform.position):F0} m away), visible in the camera frustum at promotion {trace[Mathf.Max(0, promotedAt)].Vis}; largest per-frame move over 40 frames {maxStep:F3} m; agent owns the body again (updatePosition {r.Agent.updatePosition}), walker {r.Walker.State}.");
        yield return Realtime(3f);
        Check((r.Walker.State == RouteWalker.Mode.Following || r.Walker.Transferring) && r.Walker.PathDeviation() >= 0 && r.Walker.PathDeviation() <= tuning.Npcs.RejoinRadius,
            $"After promotion it keeps walking the same network near the hero: {r.Walker.State}{(r.Walker.Transferring ? " (street crossing)" : "")}, route-walking deviation {r.Walker.PathDeviation():F2} m.");
        // Demotion in view: the hero backs off to ~110 m, still looking at it.
        var back = W.Hero.transform.position - r.transform.position; back.y = 0; back = back.normalized;
        NavMesh.SamplePosition(r.transform.position + back * 110f, out var far, 12f, NavMesh.AllAreas);
        trace.Clear(); Move(far.position + Vector3.up * .1f); Look(r.transform.position);
        for (int i = 0; i < 60; i++) { trace.Add((Time.frameCount, r.transform.position, lod.IsFar(r), r.Walker.Riding, VisibleNow(r))); yield return null; }
        int demotedAt = trace.FindIndex(x => x.Far);
        maxStep = 0f; for (int i = 1; i < trace.Count; i++) maxStep = Mathf.Max(maxStep, Vector3.Distance(trace[i].P, trace[i - 1].P));
        Check(demotedAt >= 0 && maxStep < .25f && (r.Walker.Riding || r.Walker.State != RouteWalker.Mode.Following),
            $"DEMOTION is seamless too: demoted {demotedAt} frame(s) after the hero left ({Vector3.Distance(r.transform.position, W.Hero.transform.position):F0} m), visible at demotion {trace[Mathf.Max(0, demotedAt)].Vis}; rail started with an offset blend (riding {r.Walker.Riding}); largest per-frame move {maxStep:F3} m.");

        // Recycling: the hero goes to the far east of the docks; far unseen civilians are relocated onto unseen walk-route points.
        var quay = Routes.Routes.Where(x => x.Kind == "route" || x.Kind == "boulevard").OrderByDescending(x => x.Points.Max(p => p.x)).First();
        Vector3 east = quay.Points.OrderByDescending(p => p.x).First();
        int rec0 = lod.Recycled; var civilians = W.Npcs.Where(x => x != null && x.Role == NpcRole.Civilian && x.Encounter == null).ToList();
        var pos0 = civilians.ToDictionary(x => x, x => x.transform.position);
        Move(east + Vector3.up * .15f); Look(east + Vector3.left * 30f);
        var placed = new List<(CityNpc N, float Hero, bool Following, float Dev)>();
        {
            float end = Time.realtimeSinceStartup + 12f; var lastPos = civilians.ToDictionary(x => x, x => x.transform.position);
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                foreach (var c in civilians)
                {
                    if (c == null) continue; var p = c.transform.position;
                    if (Vector3.Distance(p, lastPos[c]) > 20f) { var wk = c.Walker; placed.Add((c, Vector3.Distance(p, W.Hero.transform.position), wk.State == RouteWalker.Mode.Following, wk.Deviation())); }
                    lastPos[c] = p;
                }
            }
        }
        Check(lod.Recycled - rec0 >= 5 && placed.Count >= 5 && placed.All(x => x.Following && x.Dev <= layout.Routes.LaneHalfWidth + .25f && x.Hero >= layout.NpcLod.RecycleDistance.x - 1f && x.Hero <= layout.NpcLod.RecycleDistance.y + 1f),
            $"RECYCLING still moves unseen far civilians: {lod.Recycled - rec0} recycled in 12 s; {placed.Count} observed relocations, all placed ON a walk route (deviation max {(placed.Count > 0 ? placed.Max(x => x.Dev) : 0):F2} m) {(placed.Count > 0 ? placed.Min(x => x.Hero) : 0):F0}-{(placed.Count > 0 ? placed.Max(x => x.Hero) : 0):F0} m from the hero (band {layout.NpcLod.RecycleDistance}), following immediately; none of these jumps was inside the camera view (teleport monitor below).");
    }

    // ------------------------------------------------------------------ police patrol -> respond -> return
    IEnumerator Cops(int seed)
    {
        yield return Launch(seed); Move(W.City.Spawn + Vector3.up * .1f);
        Check(W.Progression.Data.Side == PlayerSide.Villain || W.Progression.SwitchSide(), "Villain side for the police response (Free Play allows the H switch).");
        var villain = tuning.Heat.Police(PlayerSide.Villain);
        yield return Realtime(8f);
        var cops = W.Npcs.Where(x => x != null && !x.Dead && x.Role == NpcRole.Cop && x.Encounter == null).ToList();
        var patrol = new Dev(); yield return Sample(6f, patrol, null, RouteUse.Patrol, NpcRole.Cop);
        Check(cops.Count >= villain.PatrolCount && patrol.Following + patrol.Crossing >= patrol.Samples * .9f && Q(patrol.Path, .95f) <= 2f && patrol.Time > 0 && patrol.Travelled / patrol.Time > tuning.Npcs.PatrolSpeed * .5f,
            $"Police PATROL at 0 Heat ({cops.Count} cops, villain side, not hostile): {Describe(patrol)}.");
        var cop = cops.Where(x => x != null && x.Walker != null && x.Walker.State == RouteWalker.Mode.Following && Vector3.Distance(x.transform.position, W.Hero.transform.position) < tuning.Npcs.DetectionRange * .8f)
            .OrderBy(x => W.Npcs.IndexOf(x)).FirstOrDefault();
        Check(cop != null && !cop.Hostile, "A patrolling cop within detection range, not hostile.");
        var patrolRoute = Routes.Routes[cop.Walker.Route];
        // A real crime: the hero's side breaks a city prop (BreakableProp -> OnDestruction -> Heat + alarm).
        var prop = FindObjectsByType<BreakableProp>().Where(p => p.GetComponent<EncounterProp>() == null).OrderBy(p => Vector3.Distance(p.transform.position, W.Hero.transform.position)).First();
        int leaves0 = cop.Walker.Leaves, frame0 = Time.frameCount; float t0 = Time.time, heat0 = W.Heat, d0 = Vector3.Distance(cop.transform.position, W.Hero.transform.position);
        prop.TakeDamage(100000f, null);
        Check(W.Stars >= villain.HostileFromStars && cop.Hostile, $"Prop broken {Vector3.Distance(prop.transform.position, W.Hero.transform.position):F1} m from the hero: Heat {heat0:F2} -> {W.Heat:F2} ({W.Stars} star(s)); villain police hostile from {villain.HostileFromStars}.");
        bool far = NpcLod.Current.IsFar(cop);
        yield return Until(() => cop.Walker.Leaves > leaves0, 3f);
        Check(cop.Walker.Leaves > leaves0 && !cop.Walker.Active && (far ? cop.Walker.LastLeaveTime - t0 <= layout.NpcLod.FarThinkInterval + .1f : cop.Walker.LastLeaveFrame - frame0 <= 2),
            $"The cop RESPONDS immediately: left patrol '{patrolRoute.Name}' {cop.Walker.LastLeaveFrame - frame0} frame(s) / {cop.Walker.LastLeaveTime - t0:F3} s after the crime ({(far ? "far tier" : "near tier")}).");
        float closest = d0; int windups0 = cop.Windups;
        float until = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < until && !W.PlayerDead) { closest = Mathf.Min(closest, Vector3.Distance(cop.transform.position, W.Hero.transform.position)); yield return null; }
        Check(closest < d0 - 3f || cop.Windups > windups0 || cop.Phase != AttackPhase.Approach, $"It goes for the villain: {d0:F1} -> {closest:F1} m, windups {cop.Windups - windups0}, phase {cop.Phase}.");
        W.AddHeat(-W.Heat);
        Check(!cop.Hostile, "Heat cleared (0 stars): the cop is no longer hostile.");
        yield return Frames(2);
        Vector3 end = cop.transform.position; Routes.Nearest(end, RouteUse.Patrol, 400f, out int rStar, out _, out float dStar);
        float tEnd = Time.time;
        yield return Until(() => cop != null && cop.Walker.State == RouteWalker.Mode.Following, 30f);
        Check(cop != null && cop.Walker.State == RouteWalker.Mode.Following && (cop.Walker.TargetRoute == rStar || cop.Walker.JoinDistance <= dStar + .5f),
            $"Then it RETURNS to the nearest patrol route: response ended at {V(end)}, nearest patrol route '{Routes.Routes[rStar].Name}' {dStar:F1} m; joined '{Routes.Routes[cop.Walker.Route].Name}' and patrolling {Time.time - tEnd:F1} s later.");
        yield return Realtime(4f);
        Check(cop != null && cop.Walker.State == RouteWalker.Mode.Following && cop.Walker.Deviation() <= tuning.Npcs.RejoinRadius + .5f, $"Still patrolling 4 s later, deviation {cop.Walker.Deviation():F2} m; police alive {W.Npcs.Count(x => x != null && x.Role == NpcRole.Cop)} (ReconcilePolice removed the 1-star extra).");
    }

    // ------------------------------------------------------------------ monitor positive control
    int controlViolations; float beforeControlStep = -1f, beforeControlSpeed = -1f;
    IEnumerator MonitorControl()
    {
        yield return Launch(originalSeed); Move(W.City.Spawn + Vector3.up * .1f); yield return Realtime(3f);
        var npc = W.Npcs.Where(x => x != null && x.Role == NpcRole.Civilian && !NpcLod.Current.IsFar(x) && VisibleNow(x)).FirstOrDefault();
        if (npc == null) { foreach (var x in W.Npcs) if (x != null && x.Role == NpcRole.Civilian) { Look(x.transform.position); yield return Frames(2); if (VisibleNow(x)) { npc = x; break; } } }
        Check(npc != null, "A visible NPC for the teleport-monitor CONTROL.");
        yield return Frames(2);
        int v0 = violations; beforeControlStep = maxVisibleStep; beforeControlSpeed = maxVisibleSpeed; suppressUntilFrame = -1;
        string stepBefore = worstStep; float riderBefore = maxRiderStep, agentBefore = maxAgentStep;
        NavMesh.SamplePosition(npc.transform.position + npc.transform.right * 5f, out var hit, 3f, NavMesh.AllAreas);
        float jump = Vector3.Distance(hit.position, npc.transform.position); npc.Agent.Warp(hit.position);
        yield return Frames(2);
        controlViolations = violations - v0;
        Check(controlViolations == 1, $"Teleport-monitor CONTROL: a deliberate {jump:F1} m warp of a visible civilian is flagged ({controlViolations} violation).");
        worstStep = stepBefore; maxRiderStep = riderBefore; maxAgentStep = agentBefore;
    }

    // ------------------------------------------------------------------ reload (separate process)
    IEnumerator Reload()
    {
        yield return Scene(GameFlow.HomeScene);
        foreach (int seed in new[] { originalSeed, originalSeed + 817 })
        {
            string file = Path.Combine(Folder, "saves", $"route-fingerprint-{seed}.txt");
            Check(File.Exists(file), $"Run's route fingerprint for seed {seed} exists.");
            string expected = File.ReadAllText(file);
            yield return Launch(seed);
            string now = Routes.Fingerprint();
            Check(now == expected, $"SEPARATE PROCESS: seed {seed} rebuilds byte-identical routes ({Routes.Routes.Count} routes, {Routes.TransferCount} transfers, hash {now.GetHashCode():X8}).");
            yield return Realtime(3f);
            int following = W.Npcs.Count(x => x != null && x.Walker != null && x.Walker.State == RouteWalker.Mode.Following);
            Check(following > 0, $"Walkers join routes in the new process ({following} following after 3 s).");
        }
        yield return Home();
    }
}
#endif
