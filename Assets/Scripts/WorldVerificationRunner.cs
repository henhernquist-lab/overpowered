#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// District world verification. Positive checks with negative CONTROLS; real captures; NPC LOD; rooftop save contract
/// (claimed here, re-read by a separate process in Reload). Results: Verification/World/verify/results.txt (reload-results.txt).
public sealed class WorldVerificationRunner : MonoBehaviour
{
    public string Mode, Folder;
    public Action<int> Finished;
    readonly List<string> lines = new List<string>();
    WorldSession W => WorldSession.Instance;
    GameFlow Flow => GameFlow.Instance;
    GameTuning tuning; int originalSeed;

    void Log(string text) { lines.Add(text); Debug.Log("[WORLD VERIFY] " + text); }
    void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL " + text); Log("PASS " + text); }
    static string V(Vector3 v) => $"({v.x:0.0},{v.y:0.0},{v.z:0.0})";

    IEnumerator Start()
    {
        tuning = Resources.Load<GameTuning>("GameTuning"); originalSeed = tuning.City.Seed;
        var stack = new Stack<IEnumerator>(); stack.Push(Mode == "reload" ? Reload() : Run());
        while (stack.Count > 0)
        {
            bool more = false; object current = null;
            try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
            catch (Exception e) { Log((e.Message.StartsWith("FAIL") ? e.Message : "FAIL EXCEPTION " + e)); Finish(1); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (current is IEnumerator next) stack.Push(next); else yield return current;
        }
        Finish(0);
    }
    void Finish(int code)
    {
        tuning.City.Seed = originalSeed;
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
    IEnumerator Launch(GameModeDefinition mode) { yield return Home(); Check(Flow.Select(mode), "GameFlow.Select " + mode.name); yield return Scene(GameFlow.CityScene); }
    void Move(Vector3 p) { var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false; W.Hero.transform.position = p; cc.enabled = true; W.Hero.ResetMotion(); Physics.SyncTransforms(); }
    IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    IEnumerator Realtime(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }

    GameModeDefinition HeroClone(int maxEncounters)
    {
        var clone = Instantiate(Resources.Load<GameModeDefinition>("Modes/hero")); clone.name = "hero (world verification copy)";
        clone.MaximumEncounters = maxEncounters; clone.SpawnInterval = 100000f; return clone;
    }

    // ------------------------------------------------------------------------------------------------ run
    IEnumerator Run()
    {
        yield return Scene(GameFlow.HomeScene);
        var layout = Resources.Load<CityLayout>("CityLayout");
        int seedA = originalSeed, seedB = originalSeed + 817;
        Log($"SEEDS A={seedA} B={seedB}; districts in CityLayout: {string.Join(", ", layout.Districts.Select(d => d.Name))}");
        var prints = new Dictionary<string, string>();
        foreach (var (seed, tag) in new[] { (seedA, "A1"), (seedA, "A2"), (seedB, "B") })
        {
            tuning.City.Seed = seed;
            yield return Launch(HeroClone(8));
            W.Hero.enabled = false;
            prints[tag] = Fingerprint();
            yield return SeedChecks(seed, tag, layout);
            if (tag != "A2") TopDown($"seed-{seed}-topdown");
        }
        Check(prints["A1"] == prints["A2"], $"DETERMINISM CONTROL: seed {seedA} built twice gives an identical plan, prop placement and static-geometry fingerprint ({prints["A1"].Length} chars, hash {prints["A1"].GetHashCode():X8}).");
        Check(prints["A1"] != prints["B"], $"Different seed {seedB} gives a different layout (hash {prints["B"].GetHashCode():X8} vs {prints["A1"].GetHashCode():X8}).");

        tuning.City.Seed = seedA;
        yield return Launch(HeroClone(2));
        yield return Captures();
        yield return TravelTimes();
        yield return Waypoint();
        yield return Rooftop();
        yield return Lod();
        yield return Home();
        Log("LIMIT: no human playtest of traversal feel; travel times are NavMesh path length / RunSpeed and a flight estimate from the real flight numbers, not a driven run (batch mode has no input).");
    }

    string Fingerprint()
    {
        var p = W.City.Plan; var sb = new System.Text.StringBuilder();
        foreach (var b in W.City.Buildings) sb.Append(JsonUtility.ToJson(b)).Append('|');
        foreach (var s in p.Structures) sb.Append(s.Recipe).Append(V(s.Position)).Append(s.Yaw).Append(s.Variant).Append('|');
        foreach (var s in p.Slabs) sb.Append(s.Area).Append(s.Top).Append(s.Color).Append('|');
        foreach (var s in W.City.EncounterSites) sb.Append(V(s));
        sb.Append(V(W.City.Spawn));
        foreach (var a in W.City.Art.Placements) sb.Append(JsonUtility.ToJson(a));
        var statics = W.City.Art.StaticGeometryRenderers.ToList();
        sb.Append($"#static renderers {statics.Count} verts {statics.Sum(r => (long)(r.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount ?? 0))} pieces {W.City.Art.PieceCount}");
        return sb.ToString();
    }

    IEnumerator SeedChecks(int seed, string tag, CityLayout layout)
    {
        var city = W.City; var plan = city.Plan;
        // Districts present, each with its own content and NavMesh.
        for (int d = 0; d < plan.Districts.Count; d++)
        {
            var def = plan.Districts[d];
            int buildings = city.Buildings.Count(b => b.District == d), structures = plan.Structures.Count(s => s.District == d), sites = city.EncounterSiteDistrict.Count(x => x == d);
            bool mesh = city.EncounterSites.Where((s, i) => city.EncounterSiteDistrict[i] == d).Any(s => NavMesh.SamplePosition(s, out _, 2f, NavMesh.AllAreas));
            Check(buildings + structures > 0 && sites > 0 && mesh && def.Name == layout.Districts[d].Name,
                $"[{tag}] District {def.Name}: {buildings} buildings, {structures} static structures, {sites} encounter sites on NavMesh; NavMesh build {city.NavMeshMilliseconds[d]:F0} ms.");
        }
        // No overlapping buildings (footprint AABB, 5 cm tolerance), none in water, none on a landmark.
        int overlaps = 0; string first = "";
        var bs = city.Buildings;
        for (int i = 0; i < bs.Count; i++) for (int j = i + 1; j < bs.Count; j++)
            if (Overlap(bs[i], bs[j]))
            { overlaps++; if (first == "") first = $"{i}/{j}"; }
        var landmarks = plan.Structures.Where(s => layout.Landmarks.Any(l => l.Recipe == s.Recipe)).ToList();
        int wet = bs.Count(b => plan.Water.Any(w => w.Overlaps(new Rect(b.Position.x - b.Size.x * .5f, b.Position.z - b.Size.z * .5f, b.Size.x, b.Size.z))));
        int onLandmark = bs.Count(b => landmarks.Any(l => new Rect(b.Position.x - b.Size.x * .5f - 13, b.Position.z - b.Size.z * .5f - 13, b.Size.x + 26, b.Size.z + 26).Contains(new Vector2(l.Position.x, l.Position.z))));
        Check(overlaps == 0 && wet == 0 && onLandmark == 0, $"[{tag}] {bs.Count} buildings: 0 overlapping footprints ({overlaps}{(first != "" ? " first " + first : "")}), {wet} in water, {onLandmark} within 13 m of a landmark centre; {landmarks.Count} landmarks.");
        // CONTROL for the overlap test itself: a deliberately duplicated footprint is detected.
        var shifted = new BuildingPlacement { Position = bs[0].Position + Vector3.right, Size = bs[0].Size };
        var apart = new BuildingPlacement { Position = bs[0].Position + Vector3.right * (bs[0].Size.x + 1), Size = bs[0].Size };
        Check(Overlap(bs[0], shifted) && !Overlap(bs[0], apart), $"[{tag}] Overlap-test CONTROL: building 0 vs a copy shifted 1 m = overlap; shifted by its width + 1 m = no overlap.");
        // Spawn on walkable NavMesh; every district reachable.
        Check(NavMesh.SamplePosition(city.Spawn, out var spawnHit, .6f, NavMesh.AllAreas), $"[{tag}] Spawn {V(city.Spawn)} is on walkable NavMesh (nearest {V(spawnHit.position)}, {Vector3.Distance(spawnHit.position, city.Spawn):F2} m).");
        var path = new NavMeshPath();
        for (int d = 0; d < plan.Districts.Count; d++)
        {
            var targets = city.EncounterSites.Where((s, i) => city.EncounterSiteDistrict[i] == d).OrderByDescending(s => (s - city.Spawn).sqrMagnitude).ToList();
            var target = targets[0]; NavMesh.SamplePosition(target, out var th, 3f, NavMesh.AllAreas);
            bool ok = NavMesh.CalculatePath(spawnHit.position, th.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Check(ok, $"[{tag}] {plan.Districts[d].Name} reachable on NavMesh from spawn: farthest site {V(target)}, path complete, {Length(path):F0} m, {path.corners.Length} corners.");
        }
        // CONTROL: a point in the open sea has no NavMesh, so an unreachable target is really reported.
        var sea = new Vector3(plan.Island.xMax - 60, layout.WaterLevel, plan.Island.yMin - 50);
        Check(!NavMesh.SamplePosition(sea, out _, 3f, NavMesh.AllAreas) && !(NavMesh.CalculatePath(spawnHit.position, sea, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete),
            $"[{tag}] Reachability CONTROL: open sea at {V(sea)} (50 m off the south shore) has no NavMesh and no complete path.");
        // Crimes across districts: spawn set-pieces through the real GameModeSession.SpawnNext path.
        var districts = new List<string>();
        for (int k = 0; k < 7; k++)
        {
            var crime = W.Mode.SpawnNext(); if (crime == null || crime.Encounter == null) break;
            districts.Add(plan.Districts[city.DistrictAt(crime.Encounter.Site)].Name);
            foreach (var a in crime.Encounter.Robbers.Concat(crime.Encounter.Civilians)) if (a.Npc != null) a.Npc.Freeze(60);
        }
        var initial = W.Crimes.Where(c => c != null && c.Encounter != null).Select(c => plan.Districts[city.DistrictAt(c.Encounter.Site)].Name).ToList();
        Check(initial.Distinct().Count() >= 3, $"[{tag}] Encounters spread across districts: {initial.Count} active set-pieces in {initial.Distinct().Count()} districts ({string.Join(", ", initial)}).");
        yield return null;
    }
    static bool Overlap(BuildingPlacement a, BuildingPlacement b) =>
        Mathf.Abs(a.Position.x - b.Position.x) < (a.Size.x + b.Size.x) * .5f - .05f && Mathf.Abs(a.Position.z - b.Position.z) < (a.Size.z + b.Size.z) * .5f - .05f;
    static float Length(NavMeshPath p) { float l = 0; for (int i = 1; i < p.corners.Length; i++) l += Vector3.Distance(p.corners[i - 1], p.corners[i]); return l; }

    void TopDown(string name)
    {
        var rs = W.City.Art.Districts.Where(d => d != null).SelectMany(d => d.Static.GetComponentsInChildren<Renderer>()).ToArray();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        var go = new GameObject("capture"); var c = go.AddComponent<Camera>(); c.orthographic = true; c.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) * 1.04f;
        c.transform.position = new Vector3(b.center.x, b.max.y + 50, b.center.z); c.transform.rotation = Quaternion.Euler(90, 0, 0); c.farClipPlane = b.size.y + 200;
        c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black;
        bool fog = RenderSettings.fog; RenderSettings.fog = false; float shadow = QualitySettings.shadowDistance; QualitySettings.shadowDistance = 1200;
        Render(c, name, 1600, 1600);
        RenderSettings.fog = fog; QualitySettings.shadowDistance = shadow; Destroy(go);
        Log($"CAPTURE {name}.png: top-down orthographic, north up, x {b.center.x - c.orthographicSize:F0}..{b.center.x + c.orthographicSize:F0}, z {b.center.z - c.orthographicSize:F0}..{b.center.z + c.orthographicSize:F0}.");
    }
    void Render(Camera c, string name, int w = 1280, int h = 720)
    {
        var rt = new RenderTexture(w, h, 24); rt.Create(); c.targetTexture = rt; c.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), tex.EncodeToPNG()); RenderTexture.active = prev; c.targetTexture = null; rt.Release(); Destroy(rt); Destroy(tex);
    }
    /// Gameplay-look capture (fog, far clip and layer culls copied from the real camera) + which landmarks are in view
    /// and unoccluded from that viewpoint (line of sight to the landmark's upper third).
    void View(string name, Vector3 from, Vector3 look)
    {
        var go = new GameObject("view"); var c = go.AddComponent<Camera>(); c.CopyFrom(Camera.main); c.enabled = false;
        c.transform.position = from; c.transform.LookAt(look);
        var seen = new List<string>();
        foreach (var l in Resources.Load<CityLayout>("CityLayout").Landmarks)
        {
            var s = W.City.Plan.Structures.FirstOrDefault(x => x.Recipe == l.Recipe); if (s == null) continue;
            var recipe = W.City.Art.Settings.Recipe(l.Recipe); float top = recipe.Pieces.Max(p => p.Position.y + p.Size.y * .5f);
            var point = s.Position + Vector3.up * top * .8f; var vp = c.WorldToViewportPoint(point);
            bool inFrame = vp.z > 0 && vp.x > 0 && vp.x < 1 && vp.y > 0 && vp.y < 1;
            bool clear = !Physics.Linecast(from, point, out var hit) || Vector3.Distance(new Vector3(hit.point.x, 0, hit.point.z), new Vector3(s.Position.x, 0, s.Position.z)) < 16;
            if (inFrame && clear) seen.Add($"{l.Name} ({Vector3.Distance(from, point):F0} m)");
        }
        Render(c, name); Destroy(go);
        Log($"CAPTURE {name}.png from {V(from)} looking at {V(look)}; landmarks in frame with clear line of sight: {(seen.Count == 0 ? "none" : string.Join(", ", seen))}.");
    }

    IEnumerator Captures()
    {
        foreach (var npc in W.Npcs) if (npc != null) npc.Freeze(30);
        var plan = W.City.Plan; var layout = Resources.Load<CityLayout>("CityLayout");
        var downtown = W.City.Buildings.Where(b => b.District == W.City.DistrictAt(W.City.Spawn)).OrderByDescending(b => b.Size.y).First();
        Vector3 roof = downtown.Position + Vector3.up * (downtown.Size.y * .5f + 2.5f);
        Vector2 L(string recipe) { var s = plan.Structures.First(x => x.Recipe == recipe); return new Vector2(s.Position.x, s.Position.z); }
        Vector3 At(Vector2 p, float y) => new Vector3(p.x, y, p.y);
        Log($"Downtown rooftop viewpoint: tallest spawn-district building, height {downtown.Size.y:F1} m, roof {V(roof)}.");
        View("downtown-rooftop-west-park-tower", roof, At(L("Lookout Tower"), 60));
        View("downtown-rooftop-south-harbour-light", roof, At(L("Harbour Light"), 30));
        View("downtown-rooftop-north-spire", roof + Vector3.back * 2, At(L("Meridian Spire"), 110));
        View("waterfront-quay", new Vector3(-120, 7, -193), At(L("Harbour Light"), 25));
        View("park-pond-and-tower", new Vector3(-150, 5, -20), At(L("Lookout Tower"), 45));
        View("bridge-grand-canal", new Vector3(128, 3, -22), new Vector3(128, 2, 32));
        View("edge-east-sea", new Vector3(282, 12, 40), new Vector3(900, 30, 60));
        View("edge-north-from-spire-level", new Vector3(0, 120, 100), new Vector3(0, 40, 700));
        yield return null;
    }

    IEnumerator TravelTimes()
    {
        var m = W.Tuning.Movement; var flight = W.Powers.Flight; float fuel = W.Powers.Stats(flight).Duration, recharge = flight.Definition.GroundRecharge;
        float flySpeed = m.RunSpeed * m.FlightForwardBoost, tank = fuel * flySpeed;
        Log($"TRAVEL model: run {m.RunSpeed} m/s; flight horizontal {flySpeed} m/s (RunSpeed x FlightForwardBoost) for {fuel:F1} s per tank ({tank:F0} m), ground recharge {recharge}/s ({fuel / recharge:F1} s to refill); encounter deadline {Resources.Load<EncounterDefinition>("Encounters/bank").Deadline} s.");
        var path = new NavMeshPath(); NavMesh.SamplePosition(W.City.Spawn, out var from, 1, NavMesh.AllAreas);
        for (int d = 0; d < W.City.Plan.Districts.Count; d++)
        {
            var sites = W.City.EncounterSites.Where((s, i) => W.City.EncounterSiteDistrict[i] == d).ToList();
            foreach (var target in new[] { sites.OrderBy(s => (s - W.City.Spawn).sqrMagnitude).First(), sites.OrderByDescending(s => (s - W.City.Spawn).sqrMagnitude).First() })
            {
                NavMesh.SamplePosition(target, out var to, 3, NavMesh.AllAreas);
                NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
                float walk = Length(path), straight = Vector3.Distance(new Vector3(from.position.x, 0, from.position.z), new Vector3(to.position.x, 0, to.position.z));
                int tanks = Mathf.CeilToInt(straight / Mathf.Max(1, tank));
                float fly = straight / flySpeed + (tanks - 1) * (fuel / recharge + 1f);
                Log($"TRAVEL spawn -> {W.City.Plan.Districts[d].Name} site {V(target)}: NavMesh path {walk:F0} m = {walk / m.RunSpeed:F1} s running; straight {straight:F0} m = ~{fly:F1} s flying ({tanks} tank(s)).");
            }
        }
        yield return null;
    }

    IEnumerator Waypoint()
    {
        var hud = FindAnyObjectByType<GameHud>(); Check(hud != null, "GameHud present.");
        W.Hero.enabled = true; foreach (var npc in W.Npcs) if (npc != null) npc.Freeze(60);
        var e = W.Crimes.Where(c => c != null && c.Encounter != null).Select(c => c.Encounter).First(); e.enabled = false;
        float until = Time.realtimeSinceStartup + 6; while (!W.Hero.GetComponent<CharacterController>().isGrounded && Time.realtimeSinceStartup < until) yield return null;
        W.Hero.TryJump(); yield return Realtime(GameHud.BriefingFadeSeconds + .4f);
        // Stand ~200 m from the encounter (the farthest sidewalk from it within 260 m) and face it, then face away.
        var spot = W.City.Sidewalks.Where(s => Vector3.Distance(s, e.Site) < 260).OrderByDescending(s => Vector3.Distance(s, e.Site)).First();
        Move(spot + Vector3.up * .1f); yield return Frames(3);
        var follow = FindAnyObjectByType<ThirdPersonCamera>(); var dir = e.Site - spot;
        follow.SetLook(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 10); yield return Frames(6);
        float metres = Vector3.Distance(W.Hero.transform.position, hud.WaypointTarget);
        Check(hud.WaypointVisible && hud.WaypointMetres > 120 && Mathf.Abs(hud.WaypointMetres - metres) < 1f,
            $"HUD waypoint at the new distances: target {V(hud.WaypointTarget)} of {e.Definition.DisplayName} at {hud.WaypointMetres:F0} m (independent {metres:F0} m), on-screen={hud.WaypointOnScreen}, label \"{(hud.WaypointOnScreen ? hud.WaypointDistanceLabel.text : "(edge arrow)")}\".");
        if (hud.WaypointOnScreen) Check(hud.WaypointDistanceLabel.text == $"{hud.WaypointMetres:F0} M", "Waypoint distance label reads the long distance.");
        follow.SetLook(Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, 10); yield return Frames(6);
        Check(hud.WaypointVisible && !hud.WaypointOnScreen && GameHud.Shown(hud.WaypointEdge), $"Facing away at {hud.WaypointMetres:F0} m: the edge arrow points back to the target (angle {hud.WaypointArrowAngle:F0}).");
        W.Hero.enabled = false; e.enabled = true;
    }

    IEnumerator Rooftop()
    {
        var markers = FindObjectsByType<RooftopDiscovery>().OrderBy(r => r.Id).ToList();
        var ids = Enumerable.Range(0, W.City.Buildings.Count(b => b.RooftopReward)).Select(i => W.Tuning.City.Seed + ":roof:" + i).ToList();
        Check(markers.Count == ids.Count && ids.All(id => markers.Any(m => m.Id == id)) && W.City.Buildings.Take(ids.Count).All(b => b.RooftopReward),
            $"Rooftop contract: {markers.Count} discovery markers with ids {string.Join(", ", ids)} on the first {ids.Count} buildings (districts {string.Join(",", W.City.Buildings.Take(ids.Count).Select(b => W.City.Plan.Districts[b.District].Name))}).");
        var target = markers.First(); string id = target.Id; var pos = target.transform.position;
        Move(pos + Vector3.down * .5f); yield return Frames(4);
        Check(W.Progression.Data.Rooftops.Contains(id) && target == null, $"Standing on the roof claims {id} into the Rooftops save field; marker removed.");
        File.WriteAllText(Path.Combine(Folder, "saves", "expected-roof.txt"), id);
    }

    IEnumerator Lod()
    {
        var lod = NpcLod.Current; Check(lod != null, "NpcLod active.");
        foreach (var npc in W.Npcs) if (npc != null) npc.Freeze(0);
        W.Hero.enabled = false; Move(W.City.Spawn + Vector3.up * .1f); yield return Realtime(1f);
        int minNear = int.MaxValue, maxNear = 0, minFar = int.MaxValue, maxFar = 0, frames = 0;
        float until = Time.realtimeSinceStartup + 3;
        while (Time.realtimeSinceStartup < until) { yield return null; frames++; minNear = Mathf.Min(minNear, lod.NearCount); maxNear = Mathf.Max(maxNear, lod.NearCount); minFar = Mathf.Min(minFar, lod.FarCount); maxFar = Mathf.Max(maxFar, lod.FarCount); }
        Log($"LOD counts over {frames} frames at spawn: full (near) {minNear}..{maxNear}, cheap (far) {minFar}..{maxFar}; promotions {lod.Promotions}, demotions {lod.Demotions}, civilians recycled {lod.Recycled}.");
        Check(maxNear > 0 && maxFar > 0, "Both tiers populated in a live session.");

        // A hostile criminal 130 m away: cheap tier.
        var farPoint = W.City.Sidewalks.Where(s => { float d = Vector3.Distance(s, W.Hero.transform.position); return d > 120 && d < 160; }).First();
        var crook = CityNpc.Spawn(W, farPoint, NpcRole.Criminal); Check(crook != null && crook.Hostile, $"Hostile criminal spawned {Vector3.Distance(farPoint, W.Hero.transform.position):F0} m from the hero.");
        var anim = crook.GetComponent<HumanoidPresentation>();
        yield return Frames(2);
        int thinks0 = lod.Thinks(crook), steps0 = lod.AnimatorSteps(crook), f0 = Time.frameCount; float t0 = Time.time;
        yield return Realtime(2f);
        int thinks = lod.Thinks(crook) - thinks0, steps = lod.AnimatorSteps(crook) - steps0, frameCount = Time.frameCount - f0; float secs = Time.time - t0;
        Check(lod.IsFar(crook) && !anim.Animator.enabled && !anim.enabled && crook.Agent.obstacleAvoidanceType == ObstacleAvoidanceType.NoObstacleAvoidance && thinks < frameCount / 3 && steps > 0 && crook.Windups == 0,
            $"FAR tier: animator off (manual steps {steps} in {secs:F1} s), presentation off, avoidance off, AI ticks {thinks} over {frameCount} frames (~{thinks / Mathf.Max(.01f, secs):F1}/s), windups {crook.Windups}.");
        // CONTROL: promote by moving the hero next to it -> full behaviour resumes and it fights.
        Move(crook.transform.position + crook.transform.forward * 3f + Vector3.up * .1f); W.Hero.enabled = true;
        yield return Frames(2);
        Check(!lod.IsFar(crook) && anim.Animator.enabled && anim.enabled && crook.Agent.obstacleAvoidanceType != ObstacleAvoidanceType.NoObstacleAvoidance, "CONTROL promoted: within 2 frames animator, presentation and obstacle avoidance are back on.");
        int nearThinks = lod.Thinks(crook), nf = Time.frameCount;
        until = Time.realtimeSinceStartup + 10;
        while (crook != null && !crook.Dead && crook.Releases == 0 && Time.realtimeSinceStartup < until) yield return null;
        // The presentation evaluates the clip's impact marker in LateUpdate around the release; give it up to 1 s.
        until = Time.realtimeSinceStartup + 1f;
        while (crook != null && anim.LastAttackImpactTime < 0 && Time.realtimeSinceStartup < until) yield return null;
        Check(crook != null && crook.Windups > 0 && crook.Releases > 0 && anim.LastAttackImpactTime > 0,
            $"CONTROL promoted NPC fights again: windups {crook.Windups}, releases {crook.Releases}, hits {crook.Hits}, attack clip impact marker at t={anim.LastAttackImpactTime:F2} vs release t={crook.LastReleaseTime:F2}; AI ticked {lod.Thinks(crook) - nearThinks} times in {Time.frameCount - nf} frames (every frame).");
        W.Hero.enabled = false;
        Destroy(crook.gameObject); yield return null;
    }

    // ------------------------------------------------------------------------------------------------ reload
    IEnumerator Reload()
    {
        yield return Scene(GameFlow.HomeScene);
        string id = File.ReadAllText(Path.Combine(Folder, "saves", "expected-roof.txt")).Trim();
        yield return Launch(Resources.Load<GameModeDefinition>("Modes/free-play"));
        yield return Frames(3);
        var markers = FindObjectsByType<RooftopDiscovery>();
        Check(W.Progression.Data.Rooftops.Contains(id) && markers.All(m => m.Id != id) && markers.Length == W.City.Buildings.Count(b => b.RooftopReward) - 1,
            $"Separate process: the save still holds rooftop {id}; that marker is not respawned; {markers.Length} unclaimed markers remain.");
        var fresh = new GameObject("fresh").AddComponent<PlayerProgression>();
        fresh.Initialize(W.Tuning.Progression, Resources.LoadAll<PowerDefinition>("Powers"), Path.Combine(Folder, "saves", "fresh-" + Guid.NewGuid().ToString("N") + ".json"));
        Check(!fresh.Data.Rooftops.Contains(id), "Fresh-save CONTROL: a new save has no claimed rooftops.");
        yield return Home();
    }
}
#endif
