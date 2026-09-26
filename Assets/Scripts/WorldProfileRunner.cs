#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

/// World-scale measurement: generation time by stage, renderer/draw/batch counts, NPC counts, and single-render FPS
/// through the REAL gameplay camera (ThirdPersonCamera placement; camera disabled + one Render() per frame, the
/// "gameplay-camera single-render" method recorded in STATUS). Free Play session (24 civilians, no encounters, so no
/// combat perturbs the sample) with Heat topped to 3 stars before every sample (friendly patrols + 3-star police).
/// Diagnostic only: it measures and captures, it never changes generation.
public sealed class WorldProfileRunner : MonoBehaviour
{
    public Action<int> Finished;
    readonly List<string> lines = new List<string>();
    WorldSession W => WorldSession.Instance;
    string runTag = "baseline";
    int rounds = 3, gens = 3;
    string Folder => Path.GetFullPath("Verification/World/" + runTag);
    Camera cam;
    RenderTexture target;

    void Log(string text) { lines.Add(text); Debug.Log("[WORLD PROFILE] " + text); }
    void Check(bool ok, string text) { if (!ok) throw new Exception(text); Log("PASS " + text); }
    static string Arg(string name) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

    IEnumerator Start()
    {
        if (Arg("-worldTag") is string t) runTag = t;
        if (int.TryParse(Arg("-worldRounds"), out int r) && r > 0) rounds = r;
        if (int.TryParse(Arg("-worldGens"), out int g) && g > 0) gens = g;
        Directory.CreateDirectory(Folder);
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (stack.Count > 0)
        {
            bool more = false; object current = null;
            try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
            catch (Exception e) { Log("FAIL " + e); Finish(1); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (current is IEnumerator next) stack.Push(next); else yield return current;
        }
        Finish(0);
    }

    CityArtSettings art; StaticGeometryMode restoreStatic; bool staticOverride;
    void Finish(int code)
    {
        if (staticOverride) art.StaticGeometry = restoreStatic;
        File.WriteAllLines(Path.Combine(Folder, "results.txt"), lines);
        Finished(code);
    }

    IEnumerator Scene(string name, int settle = 8)
    {
        float until = Time.realtimeSinceStartup + 180;
        while (GameFlow.Instance.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        { if (Time.realtimeSinceStartup > until) throw new Exception("Scene load timeout " + name); yield return null; }
        for (int i = 0; i < settle; i++) yield return null;
    }

    static string Shell(string file, string args)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args) { RedirectStandardOutput = true, UseShellExecute = false });
            string text = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); return text;
        }
        catch (Exception e) { return "unavailable (" + e.Message + ")"; }
    }
    static string LoadAverage() => Shell("/usr/sbin/sysctl", "-n vm.loadavg");
    /// Unity batch-mode processes on the machine (this one included). >1 means another agent's Unity is running.
    static int BatchUnities() { string s = Shell("/usr/bin/pgrep", "-f \"^/Applications/Unity.*/MacOS/Unity -batchmode\""); return s.StartsWith("unavailable") ? -1 : s.Split('\n').Count(l => l.Trim().Length > 0); }

    IEnumerator Run()
    {
        yield return Scene(GameFlow.HomeScene);
        art = Resources.Load<CityArtSettings>("CityArtSettings"); restoreStatic = art.StaticGeometry;
        if (int.TryParse(Arg("-worldStatic"), out int forced)) { staticOverride = true; art.StaticGeometry = (StaticGeometryMode)forced; }
        Log($"STATIC GEOMETRY MODE {art.StaticGeometry}{(staticOverride ? " (command-line override for this run only; asset restored at exit)" : "")}");
        var mode = Resources.Load<GameModeDefinition>("Modes/free-play");
        Log($"ENV runTag={runTag} commit={Arg("-worldCommit") ?? "?"} GPU={SystemInfo.graphicsDeviceName} CPU={SystemInfo.processorType} quality='{QualitySettings.names[QualitySettings.GetQualityLevel()]}' shadows={QualitySettings.shadows} shadowDistance={QualitySettings.shadowDistance} load={LoadAverage()} batchUnities={BatchUnities()}");
        Log($"SCENARIO mode={mode.Id} civilians={mode.Civilians} spawnPolice={mode.SpawnPolice}; Heat topped to 3 before every sample; seed={Resources.Load<GameTuning>("GameTuning").City.Seed}");

        // ---- generation time: Select() -> session ready, and CityDistrict's own stage split.
        for (int g = 1; g <= gens; g++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (!GameFlow.Instance.Select(mode)) throw new Exception("Free Play select refused");
            while (GameFlow.Instance.Loading || SceneManager.GetActiveScene().name != GameFlow.CityScene || W == null) yield return null;
            double ready = watch.Elapsed.TotalMilliseconds;
            var t = W.City.Timings;
            double city = t.Where(p => !p.Key.StartsWith("session")).Sum(p => p.Value);
            Log($"GEN {g}: select->session ready {ready:F0} ms (includes async scene load); city build {city:F1} ms; total build incl. session {t.Sum(p => p.Value):F1} ms");
            foreach (var p in t) Log($"GEN {g} STAGE {p.Key}: {p.Value:F1} ms");
            if (g < gens) { GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene); }
        }
        yield return Scene(GameFlow.CityScene);

        // ---- structure
        Populate(); yield return new WaitForSeconds(2f);
        Structure();

        // ---- top-down capture (before FPS so it shows the populated city exactly as measured)
        TopDown();

        // ---- FPS: views
        cam = Camera.main;
        if (target == null) { target = new RenderTexture(1280, 720, 24) { name = "World profile 1280x720" }; target.Create(); }
        foreach (var view in new[] { "street", "flight" })
        {
            yield return StageView(view);
            cam.enabled = false; cam.targetTexture = target;
            var warm = System.Diagnostics.Stopwatch.StartNew();
            while (warm.Elapsed.TotalSeconds < 2) { cam.Render(); yield return null; }
            for (int r = 1; r <= rounds; r++) { Populate(); yield return Measure(view, r, false); }
            yield return Measure(view, 0, true);
            Save(target, "view-" + view);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-worldControls") >= 0) yield return Controls(view);
            cam.targetTexture = null; cam.enabled = true;
        }
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        Log($"END load={LoadAverage()} batchUnities={BatchUnities()}");
        Log("LIMIT: Editor Play Mode (batch) throughput at 1280x720, hero parked, input idle; includes ~5.8 ms batch-mode overhead per STATUS. Not a standalone-player figure.");
    }

    /// Controls at the current view, each reverted: NPC LOD off (every NPC full), and "streaming" (every district's static
    /// root except the spawn district deactivated) to price what district streaming could save.
    IEnumerator Controls(string view)
    {
        var lod = NpcLod.Current;
        if (lod != null)
        {
            lod.Settings.Enabled = false; yield return null; yield return null;
            for (int r = 1; r <= 2; r++) { Populate(); yield return Measure(view + " CONTROL npcLod OFF", r, false); }
            lod.Settings.Enabled = true; yield return null;
        }
        int home = W.City.DistrictAt(W.City.Spawn);
        var others = W.City.Art.Districts.Where(d => d != null && d.Index != home).Select(d => d.Static.gameObject).ToList();
        foreach (var go in others) go.SetActive(false);
        if (W.City.Art.BackdropRoot != null) W.City.Art.BackdropRoot.gameObject.SetActive(false);
        yield return null;
        for (int r = 1; r <= 2; r++) { Populate(); yield return Measure(view + " CONTROL only spawn district static geometry active", r, false); }
        foreach (var go in others) go.SetActive(true);
        if (W.City.Art.BackdropRoot != null) W.City.Art.BackdropRoot.gameObject.SetActive(true);
        yield return null;
        for (int r = 1; r <= 2; r++) { Populate(); yield return Measure(view + " CONTROL restored", r, false); }
    }

    void Populate() { if (W.Heat < 3) W.AddHeat(3 - W.Heat); W.ReconcilePolice(); }

    void Structure()
    {
        var all = FindObjectsByType<Renderer>();
        int staticBatched = all.Count(r => r.isPartOfStaticBatch);
        var breakable = FindObjectsByType<BreakableProp>();
        var breakableRenderers = breakable.SelectMany(b => b.GetComponentsInChildren<Renderer>()).ToArray();
        int breakableStatic = breakableRenderers.Count(r => r.isPartOfStaticBatch);
        var props = W.City.GetComponentsInChildren<CityArtProp>();
        int civilians = W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Civilian);
        int cops = W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Cop);
        var tri = NavMesh.CalculateTriangulation();
        var filters = FindObjectsByType<MeshFilter>();
        Log($"STRUCTURE renderers={all.Length} staticBatchedRenderers={staticBatched} meshFilters={filters.Length} distinctMeshes={filters.Select(f => f.sharedMesh).Where(m => m != null).Distinct().Count()} gameObjectsUnderCity={W.City.GetComponentsInChildren<Transform>(true).Length} colliders={W.City.GetComponentsInChildren<Collider>(true).Length}");
        Log($"STRUCTURE buildings={W.City.Buildings.Count} props={props.Length} breakableProps={breakable.Length} breakableRenderers={breakableRenderers.Length} breakableRenderersInStaticBatch={breakableStatic} sidewalkPoints={W.City.Sidewalks.Count}");
        Log($"STRUCTURE npcs={W.Npcs.Count(n => n != null)} civilians={civilians} cops={cops} animators={FindObjectsByType<Animator>().Length} skinned={FindObjectsByType<SkinnedMeshRenderer>().Length} navmeshVerts={tri.vertices.Length} navmeshTris={tri.indices.Length / 3}");
        var bounds = CityBounds();
        Log($"STRUCTURE districts={W.City.Plan.Districts.Count} ({string.Join(", ", W.City.Plan.Districts.Select((d, i) => $"{d.Name}: {W.City.Buildings.Count(b => b.District == i)} buildings, navmesh {W.City.NavMeshMilliseconds[i]:F0} ms"))}); staticPieces={W.City.Art.PieceCount} landmarks={string.Join("/", W.City.Plan.Landmarks)} spawn={W.City.Spawn} encounterSites={W.City.EncounterSites.Count}");
        Log($"STRUCTURE city renderer bounds min={bounds.min} max={bounds.max} size={bounds.size}; camera farClip={Camera.main.farClipPlane} fog={RenderSettings.fog} fogMode={RenderSettings.fogMode} fogDensity={RenderSettings.fogDensity}");
        Check(breakableStatic == 0, $"No BreakableProp renderer is part of a static batch ({breakableRenderers.Length} breakable renderers checked).");
    }

    Bounds CityBounds()
    {
        var rs = W.City.Art.Districts.Where(d => d != null).SelectMany(d => d.Static.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToArray();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    void TopDown()
    {
        var b = CityBounds();
        var go = new GameObject("World top-down capture"); var c = go.AddComponent<Camera>();
        c.orthographic = true; c.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) * 1.05f;
        c.transform.position = new Vector3(b.center.x, b.max.y + 50, b.center.z); c.transform.rotation = Quaternion.Euler(90, 0, 0);
        c.nearClipPlane = 1; c.farClipPlane = b.size.y + 200; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black;
        var rt = new RenderTexture(1600, 1600, 24); rt.Create(); c.targetTexture = rt;
        float shadow = QualitySettings.shadowDistance; QualitySettings.shadowDistance = Mathf.Max(shadow, b.size.magnitude + 300);
        bool fog = RenderSettings.fog; RenderSettings.fog = false;
        c.Render(); QualitySettings.shadowDistance = shadow; RenderSettings.fog = fog;
        Save(rt, "topdown");
        Log($"CAPTURE topdown.png: orthographic, north (+Z) up, east (+X) right, covers x {b.center.x - c.orthographicSize:F0}..{b.center.x + c.orthographicSize:F0}, z {b.center.z - c.orthographicSize:F0}..{b.center.z + c.orthographicSize:F0}");
        c.targetTexture = null; rt.Release(); Destroy(rt); Destroy(go);
    }

    /// Street: the densest street of the city, hero on the sidewalk, real ThirdPersonCamera placement looking along it.
    /// Flight: hero parked 45 m up at the city's south edge, camera looking north across the whole city.
    IEnumerator StageView(string view)
    {
        W.Hero.enabled = false;
        Vector3 at; float yaw, pitch; string where;
        var plan = W.City.Plan; int spawnDistrict = W.City.DistrictAt(W.City.Spawn);
        if (view == "street")
        {
            // Densest street: the spawn district's crossing with the most building height within 60 m; stand on the west
            // sidewalk of its north-south street, mid-block to the south, looking north along the street.
            var best = plan.Crossings.Where(x => x.District == spawnDistrict)
                .OrderByDescending(x => W.City.Buildings.Where(b => (new Vector2(b.Position.x, b.Position.z) - new Vector2(x.Center.x, x.Center.z)).magnitude < 60).Sum(b => b.Size.y)).First();
            at = new Vector3(best.Center.x - best.Street * .5f - 1.5f, W.Tuning.City.SidewalkHeight, best.Center.z - best.Pitch * .5f);
            yaw = 0; pitch = W.Tuning.Camera.Pitch;
            where = $"{plan.Districts[spawnDistrict].Name} west sidewalk, mid-block south of the densest crossing {best.Center} ({W.City.Buildings.Where(b => (new Vector2(b.Position.x, b.Position.z) - new Vector2(best.Center.x, best.Center.z)).magnitude < 60).Sum(b => b.Size.y):F0} m of building height within 60 m)";
        }
        else
        {
            var r = plan.Districts[spawnDistrict].Region;
            at = new Vector3(r.center.x, 70, r.yMin); yaw = 0; pitch = 10;
            where = $"70 m up over the {plan.Districts[spawnDistrict].Name} south edge, looking north across the island";
        }
        var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false; W.Hero.transform.position = at; cc.enabled = true;
        var follow = Camera.main.GetComponent<ThirdPersonCamera>(); follow.enabled = true; follow.SetLook(yaw, pitch);
        Populate();
        yield return new WaitForSeconds(1.5f);
        Log($"VIEW {view}: hero at {at} ({where}); camera {Camera.main.transform.position} fwd {Camera.main.transform.forward}");
    }

    static readonly string[] RecorderNames = { "PlayerLoop", "PostLateUpdate.BatchModeUpdate", "PostLateUpdate.UpdateAllSkinnedMeshes", "PostLateUpdate.UpdateAllRenderers",
        "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.DirectorUpdateAnimationEnd", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
        "PreUpdate.AIUpdate", "PreLateUpdate.AIUpdatePostScript", "FixedUpdate.PhysicsFixedUpdate", "Camera.Render", "Culling", "Shadows.RenderShadowMap", "Render.OpaqueGeometry" };

    /// FPS hygiene: another agent's batch-mode Unity shares this machine. Wait until this process is the only one,
    /// sample, and re-sample (up to 3 tries) when another one started during the sample.
    IEnumerator Measure(string view, int round, bool profiled)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            double waited = 0; var wait = System.Diagnostics.Stopwatch.StartNew();
            while (BatchUnities() > 1 && wait.Elapsed.TotalMinutes < 20) { yield return new WaitForSecondsRealtime(5f); Populate(); }
            waited = wait.Elapsed.TotalSeconds;
            if (waited > 1) Log($"HYGIENE waited {waited:F0}s for other batch-mode Unity processes to finish before sampling {view} r{round}");
            yield return Sample(view, round, profiled, attempt);
            if (!contaminated) yield break;
            Log($"HYGIENE sample {view} r{round} attempt {attempt} was CONTAMINATED (another batch Unity ran during it); re-sampling");
        }
    }
    bool contaminated;

    IEnumerator Sample(string view, int round, bool profiled, int attempt)
    {
        yield return new WaitForSecondsRealtime(.4f);
        int othersBefore = BatchUnities();
        List<(string Name, ProfilerRecorder Rec, double Sum)> recs = null;
        if (profiled)
        {
            var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
            recs = handles.Select(h => (h, ProfilerRecorderHandle.GetDescription(h).Name)).Where(x => RecorderNames.Contains(x.Name)).GroupBy(x => x.Name).Select(x => x.First())
                .Select(x => (x.Name, new ProfilerRecorder(x.h, 1, ProfilerRecorderOptions.Default), 0.0)).ToList();
            foreach (var x in recs) x.Rec.Start();
        }
        var times = new List<double>(); var draws = new List<int>(); var batches = new List<int>(); var pass = new List<int>();
        var stat = new List<int>(); var statB = new List<int>(); var inst = new List<int>(); var instB = new List<int>(); var dyn = new List<int>();
        var tris = new List<long>(); var sums = new double[recs?.Count ?? 0]; int frames = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew(); double prior = 0;
        while (watch.Elapsed.TotalSeconds < 4)
        {
            cam.Render();
            yield return null;
            double now = watch.Elapsed.TotalSeconds; times.Add((now - prior) * 1000); prior = now;
            draws.Add(UnityStats.drawCalls); batches.Add(UnityStats.staticBatches + UnityStats.dynamicBatches + UnityStats.instancedBatches); pass.Add(UnityStats.setPassCalls);
            stat.Add(UnityStats.staticBatchedDrawCalls); statB.Add(UnityStats.staticBatches); inst.Add(UnityStats.instancedBatchedDrawCalls); instB.Add(UnityStats.instancedBatches);
            dyn.Add(UnityStats.dynamicBatchedDrawCalls); tris.Add(UnityStats.triangles);
            if (recs != null) { for (int i = 0; i < recs.Count; i++) sums[i] += recs[i].Rec.LastValue; frames++; }
        }
        watch.Stop();
        int othersAfter = BatchUnities();
        contaminated = othersBefore > 1 || othersAfter > 1;
        var sorted = new List<double>(times); sorted.Sort();
        double fps = times.Count / watch.Elapsed.TotalSeconds;
        int visible = FindObjectsByType<Renderer>().Count(r => r.isVisible);
        string lod = NpcLodLine();
        Log($"MEASURED {view} {(profiled ? "PROFILED (recorders attached; FPS not used)" : "r" + round)} attempt{attempt}: FPS={fps:F2} mean={times.Average():F2}ms p95={sorted[(int)(sorted.Count * .95f)]:F2}ms frames={times.Count} " +
            $"drawCalls={Median(draws)} batches={Median(batches)} setPass={Median(pass)} staticBatchedDraws={Median(stat)} staticBatches={Median(statB)} instancedDraws={Median(inst)} instancedBatches={Median(instB)} dynamicBatchedDraws={Median(dyn)} tris={Median(tris)} " +
            $"visibleRenderers={visible} npcs={W.Npcs.Count(n => n != null && !n.Dead)} heat={W.Heat:F2} {lod} batchUnities before/after={othersBefore}/{othersAfter}{(othersBefore > 1 || othersAfter > 1 ? " CONTAMINATED" : "")}");
        if (recs != null)
        {
            for (int i = 0; i < recs.Count; i++) { Log($"PROFILE {view} {recs[i].Name}: {sums[i] / Math.Max(1, frames) / 1e6:F3} ms/frame"); recs[i].Rec.Dispose(); }
        }
    }

    string NpcLodLine() => NpcLod.Current == null ? "npcLod=none(all full)" : $"npcLod near(full)={NpcLod.Current.NearCount} far(cheap)={NpcLod.Current.FarCount} recycled={NpcLod.Current.Recycled}";

    static int Median(List<int> v) { var c = new List<int>(v); c.Sort(); return c.Count == 0 ? 0 : c[c.Count / 2]; }
    static long Median(List<long> v) { var c = new List<long>(v); c.Sort(); return c.Count == 0 ? 0 : c[c.Count / 2]; }

    void Save(RenderTexture rt, string name)
    {
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous; Destroy(image);
    }
}
#endif
