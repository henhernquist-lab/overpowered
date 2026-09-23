#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

/// Diagnostic only: measures the shipping city with one hypothesis toggled at a time.
/// Changes nothing permanently; every control reverts and the run ends at the shipping state.
/// Sampling matches CityArtVerificationRunner.Benchmark so numbers stay comparable to history.
/// Comparison=true instead rebuilds the city in each geometry mode (interleaved LEGACY/candidate rounds),
/// measures each twice (single and historical double render) and diffs fixed-camera captures.
public sealed class PerformanceProfileRunner : MonoBehaviour
{
    public Action<int> Finished;
    public bool Comparison;
    readonly List<string> lines = new List<string>();
    readonly List<Sample> samples = new List<Sample>();
    string Folder => Path.GetFullPath("Verification/Performance");
    string IdentityFolder => Path.Combine(Folder, "identity");
    WorldSession W => WorldSession.Instance;
    Camera cam;
    Behaviour follow;
    RenderTexture target;
    PropMeshMode restoreProps;
    StaticGeometryMode restoreStatic;
    CityArtSettings artSettings;

    sealed class Sample
    {
        public string Label, Config, View = "overview";
        public int Round = -1, Visible;
        public bool Double, Profiled;
        public double Fps, Mean, P95;
        public int Draws, Batches, SetPass, Static, Dynamic, Instanced, InstancedBatches, StaticBatches, Renderers;
        public long Tris, Verts;
    }

    sealed class Build
    {
        public string Config;
        public int Round;
        public double BuildMs, FinalizeMs;
        public int Renderers, PropRenderers, StaticRenderers, StaticBatchedRenderers, MeshFilters, DistinctMeshes, SharedPropMeshes;
    }
    readonly List<Build> builds = new List<Build>();

    sealed class Config
    {
        public string Name; public PropMeshMode Props; public StaticGeometryMode Static;
        public Config(string name, PropMeshMode props, StaticGeometryMode stat) { Name = name; Props = props; Static = stat; }
    }
    static readonly Config Legacy = new Config("LEGACY", PropMeshMode.PerPropCombine, StaticGeometryMode.PerRootCombine);
    static readonly Config[] Candidates =
    {
        new Config("P2+S0", PropMeshMode.SharedPerKind, StaticGeometryMode.PerRootCombine),
        new Config("P2+S1", PropMeshMode.SharedPerKind, StaticGeometryMode.StaticBatching),
        new Config("P2+S2", PropMeshMode.SharedPerKind, StaticGeometryMode.CityCombine),
        new Config("P1+S1", PropMeshMode.SharedPerKindPerMaterial, StaticGeometryMode.StaticBatching),
    };
    const int Rounds = 3;
    // Optional command-line narrowing for a focused head-to-head run:
    // -perfConfigs "P2+S1,P2+S2" -perfRounds 5 -perfSkipIdentity -perfOut comparison-s1-vs-s2.txt
    Config[] active = Candidates;
    int rounds = Rounds;
    string outFile = "comparison.txt";
    string[] views = { "overview" };
    bool profileViews => views.Length > 1 || views[0] != "overview";

    sealed class Recorded { public string Name; public ProfilerMarkerDataUnit Unit; public ProfilerRecorder Recorder; public double Sum; public int Frames; }
    sealed class Profile
    {
        public string Config, View; public int Round; public double Fps, Mean;
        public readonly Dictionary<string, double> Values = new Dictionary<string, double>();
        public readonly HashSet<string> NonTime = new HashSet<string>();
    }
    readonly List<Profile> profiles = new List<Profile>();
    List<(ProfilerRecorderHandle Handle, string Name, ProfilerMarkerDataUnit Unit)> selected;
    static readonly string[] LoopPhases = { "Initialization.", "EarlyUpdate.", "FixedUpdate.", "PreUpdate.", "Update.", "PreLateUpdate.", "PostLateUpdate.", "TimeUpdate." };
    static readonly string[] ExactNames = { "Main Thread", "Render Thread", "PlayerLoop", "EditorLoop", "Camera.Render", "GPU Frame Time", "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "CPU Total Frame Time", "BehaviourUpdate", "LateBehaviourUpdate", "FixedBehaviourUpdate" };
    static readonly string[] Keywords = { "Physics.Simulate", "Physics.Processing", "Physics.FetchResults", "Animators.", "Director.ProcessFrame", "Director.PrepareFrame", "MeshSkinning", "NavMeshManager", "Culling", "Shadows.RenderShadowMap", "Gfx.WaitFor", "Render.OpaqueGeometry", "RenderForward.RenderLoopJob", "Inl_" };

    /// Enumerates every recorder Unity exposes (first call only), logs what exists, and starts recorders for the
    /// top-level PlayerLoop phases plus a short list of per-system markers - only names that were actually enumerated.
    List<Recorded> StartRecorders()
    {
        if (selected == null)
        {
            var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
            var all = handles.Select(h => (Handle: h, Desc: ProfilerRecorderHandle.GetDescription(h))).ToList();
            File.WriteAllLines(Path.Combine(Folder, "profiler-available.txt"),
                all.Select(a => $"{a.Desc.Name} | category={a.Desc.Category.Name} | unit={a.Desc.UnitType} | type={a.Desc.DataType} | flags={a.Desc.Flags}").OrderBy(x => x, StringComparer.Ordinal));
            selected = all.Where(a => LoopPhases.Any(p => a.Desc.Name.StartsWith(p, StringComparison.Ordinal)) || ExactNames.Contains(a.Desc.Name) || Keywords.Any(k => a.Desc.Name.Contains(k)))
                .GroupBy(a => a.Desc.Name).Select(g => g.First()).Take(400)
                .Select(a => (a.Handle, a.Desc.Name, a.Desc.UnitType)).ToList();
            Log($"PROFILER: {all.Count} recorder handles available (full list: profiler-available.txt); recording {selected.Count} of them.");
            Log("PROFILER top-level PlayerLoop markers found: " + string.Join(", ", selected.Where(t => LoopPhases.Any(p => t.Name.StartsWith(p, StringComparison.Ordinal))).Select(t => t.Name)));
            Log("PROFILER frame/extra markers found: " + string.Join(", ", selected.Where(t => !LoopPhases.Any(p => t.Name.StartsWith(p, StringComparison.Ordinal))).Select(t => $"{t.Name} [{t.Unit}]")));
            Log("PROFILER requested-but-absent exact names: " + string.Join(", ", ExactNames.Where(n => !selected.Any(t => t.Name == n))));
            Log($"FRAMETIMING: FrameTimingManager.IsFeatureEnabled()={FrameTimingManager.IsFeatureEnabled()}, GPU timer frequency={FrameTimingManager.GetGpuTimerFrequency()}");
        }
        var list = selected.Select(t => new Recorded { Name = t.Name, Unit = t.Unit, Recorder = new ProfilerRecorder(t.Handle, 1, ProfilerRecorderOptions.Default) }).ToList();
        foreach (var r in list) if (!r.Recorder.IsRunning) r.Recorder.Start();
        return list;
    }
    static string Arg(string name) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

    void Log(string text) { lines.Add(text); Debug.Log("[PERF] " + text); }
    void Check(bool ok, string text) { if (!ok) throw new Exception(text); Log("PASS " + text); }

    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        artSettings = Resources.Load<CityArtSettings>("CityArtSettings");
        restoreProps = artSettings.PropMeshes; restoreStatic = artSettings.StaticGeometry;
        var stack = new Stack<IEnumerator>(); stack.Push(Comparison ? Compare() : Run());
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

    void Finish(int code)
    {
        artSettings.PropMeshes = restoreProps; artSettings.StaticGeometry = restoreStatic;
        if (Comparison) CompareReport(); else Report();
        File.WriteAllLines(Path.Combine(Folder, Comparison ? outFile : "results.txt"), lines);
        Finished(code);
    }

    IEnumerator Scene(string name, int settleFrames = 8)
    {
        float until = Time.realtimeSinceStartup + 120;
        while (GameFlow.Instance.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        { if (Time.realtimeSinceStartup > until) throw new Exception("Scene load timeout " + name); yield return null; }
        for (int i = 0; i < settleFrames; i++) yield return null;
    }

    /// Identical staging to the recorded 155/50/40 FPS benchmarks: populated city,
    /// player parked airborne, historical fixed camera, mouse-follow disabled.
    IEnumerator Stage()
    {
        W.Hero.enabled = false;
        W.AddHeat(3); W.ReconcilePolice();
        var cc = W.Hero.GetComponent<CharacterController>();
        cc.enabled = false; W.Hero.transform.position = W.City.Spawn + Vector3.up * 35; cc.enabled = true;
        cam = Camera.main;
        follow = cam.GetComponent("ThirdPersonCamera") as Behaviour;
        if (follow != null) follow.enabled = false;
        cam.transform.position = new Vector3(-65, 60, -80); cam.transform.LookAt(Vector3.zero);
        if (target == null) { target = new RenderTexture(1280, 720, 24) { name = "Perf 1280x720" }; target.Create(); }
        cam.targetTexture = target;
        yield return new WaitForSeconds(1.5f);
    }

    /// One 4s wall-clock sample. doubleRender reproduces the historical harness bug
    /// (enabled camera renders once itself, then Render() renders it again).
    IEnumerator Measure(string label, bool doubleRender = true, string config = null, int round = -1, string view = "overview", bool profile = false)
    {
        cam.enabled = doubleRender;
        yield return new WaitForSecondsRealtime(.6f);
        var recorders = profile ? StartRecorders() : null;
        var frameTimings = new FrameTiming[1];
        double gpuSum = 0, ftMainSum = 0, ftRenderSum = 0, ftCpuSum = 0; int gpuFrames = 0, ftFrames = 0;
        var times = new List<double>();
        var draws = new List<int>(); var batches = new List<int>(); var pass = new List<int>();
        var stat = new List<int>(); var dyn = new List<int>(); var inst = new List<int>();
        var instBatches = new List<int>(); var statBatches = new List<int>();
        var tris = new List<long>(); var verts = new List<long>();
        var watch = System.Diagnostics.Stopwatch.StartNew(); double prior = 0;
        while (watch.Elapsed.TotalSeconds < 4)
        {
            cam.Render();
            yield return null;
            double now = watch.Elapsed.TotalSeconds; times.Add((now - prior) * 1000); prior = now;
            draws.Add(UnityStats.drawCalls); batches.Add(UnityStats.staticBatches + UnityStats.dynamicBatches + UnityStats.instancedBatches); pass.Add(UnityStats.setPassCalls);
            stat.Add(UnityStats.staticBatchedDrawCalls); dyn.Add(UnityStats.dynamicBatchedDrawCalls); inst.Add(UnityStats.instancedBatchedDrawCalls);
            instBatches.Add(UnityStats.instancedBatches); statBatches.Add(UnityStats.staticBatches);
            tris.Add(UnityStats.triangles); verts.Add(UnityStats.vertices);
            if (recorders != null)
            {
                foreach (var r in recorders) if (r.Recorder.Valid) { r.Sum += r.Recorder.DataType == ProfilerMarkerDataType.Double || r.Recorder.DataType == ProfilerMarkerDataType.Float ? r.Recorder.LastValueAsDouble : r.Recorder.LastValue; r.Frames++; }
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, frameTimings) > 0)
                {
                    ftFrames++; ftMainSum += frameTimings[0].cpuMainThreadFrameTime; ftRenderSum += frameTimings[0].cpuRenderThreadFrameTime; ftCpuSum += frameTimings[0].cpuFrameTime;
                    if (frameTimings[0].gpuFrameTime > 0) { gpuFrames++; gpuSum += frameTimings[0].gpuFrameTime; }
                }
            }
        }
        watch.Stop(); cam.enabled = true;
        int visible = FindObjectsByType<Renderer>().Count(r => r.isVisible);
        var sorted = new List<double>(times); sorted.Sort();
        int live = FindObjectsByType<Renderer>().Count(r => r.enabled && r.gameObject.activeInHierarchy);
        var s = new Sample
        {
            Label = label, Config = config, Round = round, Double = doubleRender,
            Fps = times.Count / watch.Elapsed.TotalSeconds,
            Mean = times.Average(),
            P95 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * .95f))],
            Draws = Median(draws), Batches = Median(batches), SetPass = Median(pass),
            Static = Median(stat), Dynamic = Median(dyn), Instanced = Median(inst),
            InstancedBatches = Median(instBatches), StaticBatches = Median(statBatches),
            Tris = Median(tris), Verts = Median(verts), Renderers = live,
            View = view, Profiled = profile, Visible = visible
        };
        samples.Add(s);
        if (recorders != null)
        {
            var result = new Profile { Config = config, Round = round, View = view, Fps = s.Fps, Mean = s.Mean };
            foreach (var r in recorders)
            {
                if (r.Frames > 0) result.Values[r.Name] = r.Unit == ProfilerMarkerDataUnit.TimeNanoseconds ? r.Sum / r.Frames / 1e6 : r.Sum / r.Frames;
                if (r.Unit != ProfilerMarkerDataUnit.TimeNanoseconds) result.NonTime.Add(r.Name);
                r.Recorder.Dispose();
            }
            result.Values["[FrameTimingManager] frames with data"] = ftFrames; result.NonTime.Add("[FrameTimingManager] frames with data");
            result.Values["[FrameTimingManager] frames with gpuFrameTime>0"] = gpuFrames; result.NonTime.Add("[FrameTimingManager] frames with gpuFrameTime>0");
            if (ftFrames > 0)
            {
                result.Values["[FrameTimingManager] cpuFrameTime"] = ftCpuSum / ftFrames;
                result.Values["[FrameTimingManager] cpuMainThreadFrameTime"] = ftMainSum / ftFrames;
                result.Values["[FrameTimingManager] cpuRenderThreadFrameTime"] = ftRenderSum / ftFrames;
            }
            if (gpuFrames > 0) result.Values["[FrameTimingManager] gpuFrameTime"] = gpuSum / gpuFrames;
            result.Values["[wall clock] frame time (this harness)"] = s.Mean;
            profiles.Add(result);
        }
        Log($"MEASURED {label}: FPS={s.Fps:F2}, mean={s.Mean:F2}ms, p95={s.P95:F2}ms, frames={times.Count}, " +
            $"renderers={s.Renderers}, drawCalls={s.Draws}, batches={s.Batches}, setPass={s.SetPass}, " +
            $"staticBatched={s.Static}, dynamicBatched={s.Dynamic}, instanced={s.Instanced}, instancedBatches={s.InstancedBatches}, staticBatches={s.StaticBatches}, tris={s.Tris}, verts={s.Verts}, visibleRenderers={s.Visible}");
    }

    static int Median(List<int> v) { var c = new List<int>(v); c.Sort(); return c.Count == 0 ? 0 : c[c.Count / 2]; }
    static long Median(List<long> v) { var c = new List<long>(v); c.Sort(); return c.Count == 0 ? 0 : c[c.Count / 2]; }

    /// Toggle a control, measure, then always revert.
    IEnumerator Control(string label, Action apply, Action revert)
    {
        apply();
        yield return null; yield return null;
        IEnumerator m = Measure(label);
        while (true)
        {
            bool more;
            try { more = m.MoveNext(); }
            catch { revert(); throw; }
            if (!more) break;
            yield return m.Current;
        }
        revert();
        yield return null;
    }

    /// Returns Home (if needed), applies both geometry modes, and loads a fresh Hero session city.
    IEnumerator Rebuild(PropMeshMode props, StaticGeometryMode stat, int settleFrames = 8)
    {
        if (SceneManager.GetActiveScene().name != GameFlow.HomeScene) { GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene); }
        artSettings.PropMeshes = props; artSettings.StaticGeometry = stat;
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));
        yield return Scene(GameFlow.CityScene, settleFrames);
        if (W.City.Art.PropMode != props || W.City.Art.StaticMode != stat)
            throw new Exception($"City was built with {W.City.Art.PropMode}/{W.City.Art.StaticMode}, expected {props}/{stat}");
    }

    int DistinctMeshes() => FindObjectsByType<MeshFilter>().Select(f => f.sharedMesh).Where(m => m != null).Distinct().Count();

    IEnumerator Run()
    {
        yield return Scene(GameFlow.HomeScene);
        Check(GraphicsSettings.currentRenderPipeline == null,
            "RENDER PIPELINE: Built-in confirmed (currentRenderPipeline==null). No URP, no ScriptableRendererFeature, no outline feature exists in this project, so the requested outline on/off control CANNOT be run.");
        Log("LIMIT: the briefed 'outline Renderer Feature' control is not applicable. Packages/manifest.json has no URP package; GraphicsSettings has no pipeline asset; the project has zero custom shaders and uses Shader.Find(\"Standard\").");

        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));
        yield return Scene(GameFlow.CityScene);
        yield return Stage();

        var art = W.City.Art;
        var buildings = W.City.GetComponentsInChildren<ArtBuilding>();
        var props = W.City.GetComponentsInChildren<CityArtProp>();
        int civilians = W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Civilian);
        int cops = W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Cop);
        var animators = FindObjectsByType<Animator>();
        var skins = FindObjectsByType<SkinnedMeshRenderer>();
        var buildingRenderers = buildings.SelectMany(b => b.GetComponentsInChildren<Renderer>()).ToArray();
        string buildingLabel = "03 BUILDING renderers disabled";
        if (buildingRenderers.Length == 0)
        {
            // CityCombine draws buildings through city-wide meshes, so the building roots own no renderers.
            buildingRenderers = art.StaticGeometryRenderers.ToArray();
            buildingLabel = "03 STATIC city-combined renderers disabled (buildings AND streets; no per-building renderers exist)";
        }
        var propRenderers = props.SelectMany(p => p.GetComponentsInChildren<Renderer>()).ToArray();
        var propBodies = props.SelectMany(p => p.GetComponentsInChildren<Rigidbody>()).ToArray();
        Log($"MODES: props={art.PropMode}, static={art.StaticMode}; city art build={art.BuildMilliseconds:F1}ms (static finalize {art.StaticFinalizeMilliseconds:F1}ms); shared prop meshes={art.SharedPropMeshes}");
        Log($"POPULATION: buildings={buildings.Length} ({buildingRenderers.Length} renderers), props={props.Length} ({propRenderers.Length} renderers), " +
            $"civilians={civilians}, cops={cops}, animators={animators.Length}, skinnedRenderers={skins.Length}, propRigidbodies={propBodies.Length}, " +
            $"totalRenderers={FindObjectsByType<Renderer>().Length}, staticBatchedRenderers={FindObjectsByType<Renderer>().Count(r => r.isPartOfStaticBatch)}");
        Log($"SETTINGS: qualityLevel={QualitySettings.GetQualityLevel()} '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', shadows={QualitySettings.shadows}, " +
            $"pixelLightCount={QualitySettings.pixelLightCount}, lodBias={QualitySettings.lodBias}, shadowDistance={QualitySettings.shadowDistance}, " +
            $"GPU={SystemInfo.graphicsDeviceName}, CPU={SystemInfo.processorType}");
        int instancedMaterials = CityMaterials.Current.All.Count(m => m.enableInstancing);
        Log($"MATERIALS: palette materials={CityMaterials.Current.All.Count()}, enableInstancing=true on {instancedMaterials} of them, shader={CityMaterials.Get(CityColor.Road).shader.name}");
        int uniqueMeshes = DistinctMeshes();
        int meshFilters = FindObjectsByType<MeshFilter>().Length;
        Log($"MESHES: {meshFilters} MeshFilters reference {uniqueMeshes} DISTINCT sharedMeshes. A ratio near 1:1 means instancing and static batching cannot merge anything.");

        // 0 — reference
        yield return Measure("00 baseline (as shipped, double-render like all recorded history)");
        // Harness artifact control
        yield return Measure("01 baseline SINGLE-render (camera.enabled=false; quantifies harness double-render cost)", false);

        // Rendering hypotheses — renderers only, so physics/AI cost stays constant.
        yield return Control("02 city PROP renderers disabled",
            () => { foreach (var r in propRenderers) if (r) r.enabled = false; },
            () => { foreach (var r in propRenderers) if (r) r.enabled = true; });

        yield return Control(buildingLabel,
            () => { foreach (var r in buildingRenderers) if (r) r.enabled = false; },
            () => { foreach (var r in buildingRenderers) if (r) r.enabled = true; });

        var shadowState = QualitySettings.shadows;
        yield return Control("04 SHADOWS off (QualitySettings.shadows=Disable)",
            () => QualitySettings.shadows = ShadowQuality.Disable,
            () => QualitySettings.shadows = shadowState);

        var casters = propRenderers.Concat(buildingRenderers).Where(r => r).ToArray();
        var casterModes = casters.Select(r => r.shadowCastingMode).ToArray();
        yield return Control("05 city geometry stops CASTING shadows (receives only)",
            () => { foreach (var r in casters) if (r) r.shadowCastingMode = ShadowCastingMode.Off; },
            () => { for (int i = 0; i < casters.Length; i++) if (casters[i]) casters[i].shadowCastingMode = casterModes[i]; });

        // Animation / skinning hypotheses
        var npcAnimators = animators.Where(a => a != null && W.Hero != null && !a.transform.IsChildOf(W.Hero.transform)).ToArray();
        yield return Control("06 NPC Animators disabled (" + npcAnimators.Length + " of " + animators.Length + ")",
            () => { foreach (var a in npcAnimators) if (a) a.enabled = false; },
            () => { foreach (var a in npcAnimators) if (a) a.enabled = true; });

        yield return Control("07 skinned updateWhenOffscreen=false (" + skins.Length + " renderers)",
            () => { foreach (var s in skins) if (s) s.updateWhenOffscreen = false; },
            () => { foreach (var s in skins) if (s) s.updateWhenOffscreen = true; });

        var liveAnimators = animators.Where(a => a).ToArray();
        var cullModes = liveAnimators.Select(a => a.cullingMode).ToArray();
        yield return Control("08 Animator cullingMode=CullUpdateTransforms (was AlwaysAnimate)",
            () => { foreach (var a in liveAnimators) if (a) a.cullingMode = AnimatorCullingMode.CullUpdateTransforms; },
            () => { for (int i = 0; i < liveAnimators.Length; i++) if (liveAnimators[i]) liveAnimators[i].cullingMode = cullModes[i]; });

        // Per-frame material writes (post-fix: LateUpdate only compares; Apply() runs on palette changes)
        var materialsComponent = CityMaterials.Current;
        yield return Control("09 CityMaterials component disabled (its LateUpdate now only change-checks)",
            () => materialsComponent.enabled = false,
            () => materialsComponent.enabled = true);

        // Physics
        var liveBodies = propBodies.Where(b => b).ToArray();
        var bodyModes = liveBodies.Select(b => b.collisionDetectionMode).ToArray();
        yield return Control("10 prop Rigidbodies -> CollisionDetectionMode.Discrete (" + liveBodies.Length + " bodies)",
            () => { foreach (var b in liveBodies) if (b) b.collisionDetectionMode = CollisionDetectionMode.Discrete; },
            () => { for (int i = 0; i < liveBodies.Length; i++) if (liveBodies[i]) liveBodies[i].collisionDetectionMode = bodyModes[i]; });

        // Combined heaviest-two control: do the top render suspects stack?
        yield return Control("11 COMBINED: shadows off + prop renderers off",
            () => { QualitySettings.shadows = ShadowQuality.Disable; foreach (var r in propRenderers) if (r) r.enabled = false; },
            () => { QualitySettings.shadows = shadowState; foreach (var r in propRenderers) if (r) r.enabled = true; });

        // Drift control — must land near sample 00 or the whole table is suspect.
        yield return Measure("12 baseline RE-MEASURE (drift control; compare to 00)");
        Save("city");

        // Whole-city rebuild controls in the same process.
        yield return Rebuild(PropMeshMode.Uncombined, StaticGeometryMode.Uncombined);
        yield return Stage();
        Log($"REBUILD Uncombined/Uncombined (old CombineMeshes=false): renderers={FindObjectsByType<Renderer>().Length}, distinct sharedMeshes={DistinctMeshes()} (primitive cubes/cylinders are SHARED meshes, so batching CAN apply here)");
        yield return Measure("13 UNCOMBINED rebuild (old CombineMeshes=false: uncombined primitives, shared meshes)");
        Save("city-uncombined");

        yield return Rebuild(Legacy.Props, Legacy.Static);
        yield return Stage();
        Log($"REBUILD LEGACY PerPropCombine/PerRootCombine (pre-fix shipping state): renderers={FindObjectsByType<Renderer>().Length}, distinct sharedMeshes={DistinctMeshes()}");
        yield return Measure("14 LEGACY rebuild (pre-fix PerPropCombine + PerRootCombine; the old shipping state)");
        artSettings.PropMeshes = restoreProps; artSettings.StaticGeometry = restoreStatic;

        Log("LIMIT: Editor Play Mode throughput at 1280x720 with a fixed camera and the player parked airborne. Not a standalone-player or hands-on FPS figure. Controls toggle renderers/components in place, so each isolates its own subsystem while all other simulation keeps running.");
    }

    void Save(string name)
    {
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous; Destroy(image);
    }

    /// Ranked attribution table. A control only earns a cause if it actually moved the number.
    void Report()
    {
        var baseline = samples.FirstOrDefault(s => s.Label.StartsWith("00"));
        if (baseline == null) return;
        lines.Add("");
        lines.Add("=== ATTRIBUTION vs sample 00 baseline (" + baseline.Fps.ToString("F2") + " FPS, " + baseline.Draws + " draw calls) ===");
        lines.Add("control | FPS | dFPS% | drawCalls | dDraws | batches | tris");
        foreach (var s in samples)
        {
            double df = (s.Fps / baseline.Fps - 1) * 100;
            lines.Add($"{s.Label} | {s.Fps:F2} | {df:+0.0;-0.0;0.0}% | {s.Draws} | {s.Draws - baseline.Draws:+#;-#;0} | {s.Batches} | {s.Tris}");
        }
        var drift = samples.FirstOrDefault(s => s.Label.StartsWith("12"));
        if (drift != null)
        {
            double d = Math.Abs(drift.Fps / baseline.Fps - 1) * 100;
            lines.Add($"DRIFT CONTROL: re-measured baseline is {d:F1}% from the opening baseline. " +
                      (d < 8 ? "Within tolerance; per-control deltas above are trustworthy." : "TOO LARGE — treat individual deltas smaller than this as noise, not signal."));
        }
    }

    // ---------------------------------------------------------------- comparison mode

    IEnumerator Compare()
    {
        yield return Scene(GameFlow.HomeScene);
        Directory.CreateDirectory(IdentityFolder);
        Log($"COMPARISON: same seed ({Resources.Load<GameTuning>("GameTuning").City.Seed}), one process, GPU={SystemInfo.graphicsDeviceName}, CPU={SystemInfo.processorType}, quality='{QualitySettings.names[QualitySettings.GetQualityLevel()]}', shadows={QualitySettings.shadows}");
        Log("CONFIGS: LEGACY=PerPropCombine+PerRootCombine; P2=SharedPerKind (one renderer per prop, submesh per material); P1=SharedPerKindPerMaterial (child renderer per material); S0=PerRootCombine; S1=StaticBatching; S2=CityCombine");
        if (Arg("-perfConfigs") is string only) active = Candidates.Where(c => only.Split(',').Contains(c.Name)).ToArray();
        if (int.TryParse(Arg("-perfRounds"), out int requested) && requested > 0) rounds = requested;
        if (Arg("-perfOut") is string output) outFile = Path.GetFileName(output);
        bool identityPhase = Array.IndexOf(Environment.GetCommandLineArgs(), "-perfSkipIdentity") < 0;
        if (Arg("-perfViews") is string viewArg) views = viewArg.Split(',').Select(v => v.Trim().ToLowerInvariant()).Where(v => v == "overview" || v == "street" || v == "rooftop").Distinct().ToArray();
        if (views.Length == 0) throw new Exception("-perfViews must list overview, street and/or rooftop");
        Log($"RUN: candidates={string.Join(",", active.Select(c => c.Name))}, rounds={rounds}, views={string.Join(",", views)}, identity phase={(identityPhase ? "yes" : "SKIPPED")}");
        Directory.CreateDirectory(Path.Combine(Folder, "gameplay"));
        if (profileViews) Log("LIMIT: ProfilerRecorder values are per-frame sums of each marker across ALL threads (ProfilerRecorderOptions.Default). Top-level PlayerLoop phase markers only run on the main thread; job/render-thread markers (culling jobs, skinning, Gfx waits) can include other threads. Profiled samples are separate from the FPS samples.");

        // Phase 1 — visual identity. LEGACY twice (noise floor), then every candidate.
        var identity = identityPhase ? new List<Config> { Legacy, Legacy }.Concat(active).ToList() : new List<Config>();
        for (int i = 0; i < identity.Count; i++)
        {
            string tag = i == 0 ? "legacy-a" : i == 1 ? "legacy-b" : identity[i].Name.Replace("+", "");
            yield return CaptureIdentity(identity[i], tag);
        }
        if (identityPhase)
        {
            foreach (var view in Views) Diff("legacy-a", "legacy-b", view, "NOISE FLOOR (LEGACY build 1 vs LEGACY build 2)");
            Diff("legacy-a", "legacy-b", "skyline", "NOISE FLOOR (LEGACY build 1 vs LEGACY build 2)");
            foreach (var c in active)
            {
                string tag = c.Name.Replace("+", "");
                foreach (var view in Views) Diff("legacy-a", tag, view, "LEGACY vs " + c.Name);
                Diff("legacy-a", tag, "skyline", "LEGACY vs " + c.Name);
            }
        }

        // Phase 2 — performance, interleaved LEGACY -> candidates in each round, closing LEGACY for drift.
        Log($"LOADAVG at start of FPS phase: {LoadAverage()}");
        for (int round = 1; round <= rounds; round++)
        {
            Log($"LOADAVG at start of round {round}: {LoadAverage()}");
            yield return MeasureConfig(Legacy, round);
            foreach (var c in active) yield return MeasureConfig(c, round);
        }
        yield return MeasureConfig(Legacy, rounds + 1);
        Log($"LOADAVG at end of FPS phase: {LoadAverage()}");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
    }

    static string LoadAverage()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/sbin/sysctl", "-n vm.loadavg") { RedirectStandardOutput = true, UseShellExecute = false });
            string text = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); return text;
        }
        catch (Exception e) { return "unavailable (" + e.Message + ")"; }
    }

    IEnumerator MeasureConfig(Config c, int round)
    {
        yield return Rebuild(c.Props, c.Static);
        var art = W.City.Art;
        var all = FindObjectsByType<Renderer>();
        var b = new Build
        {
            Config = c.Name, Round = round, BuildMs = art.BuildMilliseconds, FinalizeMs = art.StaticFinalizeMilliseconds,
            Renderers = all.Length, StaticBatchedRenderers = all.Count(r => r.isPartOfStaticBatch),
            PropRenderers = W.City.GetComponentsInChildren<CityArtProp>().Sum(p => p.GetComponentsInChildren<Renderer>().Length),
            StaticRenderers = art.StaticGeometryRenderers.Count(),
            MeshFilters = FindObjectsByType<MeshFilter>().Length, DistinctMeshes = DistinctMeshes(), SharedPropMeshes = art.SharedPropMeshes
        };
        builds.Add(b);
        Log($"BUILD {c.Name} round {round}: modes={art.PropMode}/{art.StaticMode}, cityArtBuild={b.BuildMs:F1}ms (static finalize {b.FinalizeMs:F1}ms), renderers={b.Renderers}, propRenderers={b.PropRenderers}, staticRenderers={b.StaticRenderers}, staticBatchedRenderers={b.StaticBatchedRenderers}, meshFilters={b.MeshFilters}, distinctMeshes={b.DistinctMeshes}, sharedPropMeshes={b.SharedPropMeshes}");
        if (views.Length == 1 && views[0] == "overview") yield return Stage();
        foreach (var view in views)
        {
            if (!(views.Length == 1 && view == "overview")) yield return StageView(view, c.Name, round);
            Vector3 heroAt = W.Hero.transform.position;
            // Discarded warm-up so post-build hitches (mesh uploads, GC of build garbage) do not land in the first sample.
            cam.enabled = false;
            var warm = System.Diagnostics.Stopwatch.StartNew();
            while (warm.Elapsed.TotalSeconds < 2) { cam.Render(); yield return null; }
            cam.enabled = true;
            // Alternate which render mode is sampled first so neither always gets the earlier slot.
            bool singleFirst = round % 2 == 1;
            string tag = views.Length == 1 && view == "overview" ? "" : " " + view.ToUpperInvariant();
            yield return Measure($"{c.Name} r{round}{tag} {(singleFirst ? "SINGLE" : "DOUBLE")}", !singleFirst, c.Name, round, view);
            yield return Measure($"{c.Name} r{round}{tag} {(singleFirst ? "DOUBLE" : "SINGLE")}", singleFirst, c.Name, round, view);
            // Separate profiled sample so ProfilerRecorder overhead never touches the FPS samples above.
            if (profileViews && (view == "street" || (view == "overview" && round == 1)))
                yield return Measure($"{c.Name} r{round}{tag} PROFILED SINGLE (ProfilerRecorder attached; FPS not used for comparison)", false, c.Name, round, view, true);
            if (round == 1) Save("gameplay/" + view + "-" + c.Name.Replace("+", ""));
            Log($"VIEW CHECK {c.Name} r{round} {view}: hero moved {Vector3.Distance(heroAt, W.Hero.transform.position):F3}m during samples, playerDead={W.PlayerDead}, health={W.Health:F0}, heat={W.Heat:F2}, " +
                $"civilians={W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Civilian)}, cops={W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Cop)}, camera={cam.transform.position} fwd={cam.transform.forward}");
        }
    }

    /// Gameplay staging: same population staging as Stage() (Heat topped up to 3 + ReconcilePolice), but the hero stands
    /// at a gameplay spot and the REAL ThirdPersonCamera places the camera (tuning offset/pitch, yaw 0 = looking +Z, sphere-cast pull-in).
    IEnumerator StageView(string view, string config, int round)
    {
        W.Hero.enabled = false;
        cam = Camera.main;
        follow = cam.GetComponent("ThirdPersonCamera") as Behaviour;
        var c = W.Tuning.City;
        Vector3 heroAt;
        string where;
        if (view == "street")
        {
            // West sidewalk of the central block, level with the middle of the north-south street at x=-pitch/2.
            heroAt = new Vector3(-c.BlockSize * .5f + 1, c.SidewalkHeight, 0);
            where = "sidewalk beside the centre of the x=" + (-(c.BlockSize + c.StreetWidth) * .5f) + " street";
        }
        else if (view == "rooftop")
        {
            var south = W.City.Buildings.Select((b, i) => (b, i)).Where(t => t.i >= c.RooftopPickups && t.b.Position.z < 0).OrderBy(t => t.b.Size.y).ToList();
            var pick = south[south.Count / 2];
            heroAt = pick.b.Position + Vector3.up * (pick.b.Size.y * .5f + .09f);
            where = $"roof of building {pick.i} (height {pick.b.Size.y:F1}m, median of the southern-half buildings, no rooftop pickup)";
        }
        else { heroAt = W.City.Spawn + Vector3.up * 35; where = "airborne over spawn (historical)"; }
        var cc = W.Hero.GetComponent<CharacterController>();
        cc.enabled = false; W.Hero.transform.position = heroAt; cc.enabled = true;
        if (W.Heat < 3) W.AddHeat(3 - W.Heat);
        W.ReconcilePolice();
        if (view == "overview")
        {
            if (follow != null) follow.enabled = false;
            cam.transform.position = new Vector3(-65, 60, -80); cam.transform.LookAt(Vector3.zero);
        }
        else if (follow != null) follow.enabled = true;
        if (target == null) { target = new RenderTexture(1280, 720, 24) { name = "Perf 1280x720" }; target.Create(); }
        cam.targetTexture = target;
        yield return new WaitForSeconds(1.5f);
        if (round == 1) Log($"VIEW {view} ({config}): hero at {heroAt} on {where}; camera {cam.transform.position} looking {cam.transform.forward}; ThirdPersonCamera {(follow != null && follow.enabled ? "ENABLED (real placement)" : "disabled (fixed camera)")}");
    }

    static readonly string[] Views = { "overview", "street", "roof" };
    sealed class Shot { public int Width, Height; public Color32[] Pixels; }
    readonly Dictionary<string, Shot> shots = new Dictionary<string, Shot>();

    /// Fresh city, actors frozen before they can disturb anything, props settled, only city geometry drawn.
    IEnumerator CaptureIdentity(Config c, string tag)
    {
        // Skyline first: it is the one-time menu capture of the same factory, built with these modes.
        // A private MenuSkyline instance so every capture is a fresh build (the menu's own one caches by settings key).
        if (SceneManager.GetActiveScene().name != GameFlow.HomeScene) { GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene); }
        artSettings.PropMeshes = c.Props; artSettings.StaticGeometry = c.Static;
        var skyline = new GameObject("Identity skyline capture").AddComponent<MenuSkyline>();
        var sky = skyline.Get(Resources.Load<MenuPresentationTuning>("MenuPresentationTuning"));
        Grab(sky, tag + "-skyline");
        Log($"SKYLINE {tag} ({c.Name}): buildings={skyline.BuildingCount}, capture={skyline.CaptureMilliseconds:F1}ms, {sky.width}x{sky.height}");
        Destroy(skyline.gameObject);
        yield return null;

        yield return Rebuild(c.Props, c.Static, 0); // freeze actors on the first frame the session exists
        foreach (var npc in FindObjectsByType<CityNpc>()) npc.gameObject.SetActive(false);
        foreach (var e in FindObjectsByType<CrimeEncounter>()) e.enabled = false;
        foreach (var e in FindObjectsByType<CrimeEvent>()) e.enabled = false;
        if (W.Mode != null) W.Mode.enabled = false;
        W.enabled = false;
        W.Hero.enabled = false;
        var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false; W.Hero.transform.position = W.City.Spawn + Vector3.up * 35; cc.enabled = true;
        var bodies = W.City.GetComponentsInChildren<Rigidbody>();
        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 1.5f || (bodies.Any(r => r && !r.IsSleeping()) && Time.realtimeSinceStartup - start < 8)) yield return null;
        int awake = bodies.Count(r => r && !r.IsSleeping());
        var hidden = FindObjectsByType<Renderer>().Where(r => r.enabled && !r.transform.IsChildOf(W.City.transform)).ToArray();
        foreach (var r in hidden) r.enabled = false;
        Vector3 spawn = W.City.Spawn;
        var tallest = W.City.Buildings.OrderByDescending(b => b.Size.y).First(); Vector3 top = tallest.Position + Vector3.up * tallest.Size.y * .5f;
        CaptureView(tag, "overview", new Vector3(-65, 60, -80), Vector3.zero);
        CaptureView(tag, "street", spawn + new Vector3(3, 3, -7), spawn + Vector3.up * 2);
        CaptureView(tag, "roof", top + new Vector3(10, 9, -12), top);
        foreach (var r in hidden) if (r) r.enabled = true;
        Log($"IDENTITY CAPTURE {tag} ({c.Name}): settled after {Time.realtimeSinceStartup - start:F2}s, awake bodies={awake}/{bodies.Length}, hidden non-city renderers={hidden.Length}, city renderers={FindObjectsByType<Renderer>().Count(r => r.enabled && r.transform.IsChildOf(W.City.transform))}");
    }

    void CaptureView(string tag, string view, Vector3 position, Vector3 look)
    {
        var camera = new GameObject("Identity capture").AddComponent<Camera>(); camera.transform.position = position; camera.transform.LookAt(look);
        var rt = new RenderTexture(1280, 720, 24); rt.Create(); camera.targetTexture = rt;
        camera.Render(); Grab(rt, tag + "-" + view);
        camera.targetTexture = null; rt.Release(); Destroy(rt); Destroy(camera.gameObject);
    }

    void Grab(RenderTexture rt, string name)
    {
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
        RenderTexture.active = previous;
        shots[name] = new Shot { Width = rt.width, Height = rt.height, Pixels = image.GetPixels32() };
        File.WriteAllBytes(Path.Combine(IdentityFolder, name + ".png"), image.EncodeToPNG());
        Destroy(image);
    }

    /// % of pixels whose max channel delta exceeds 8/255, the max delta, the differing region, and an amplified
    /// diff PNG (dim grey = identical, red = delta 1..8, yellow = delta > 8; brightness scales with delta).
    void Diff(string a, string b, string view, string label)
    {
        var A = shots[a + "-" + view]; var B = shots[b + "-" + view];
        if (A.Width != B.Width || A.Height != B.Height) throw new Exception("Capture size mismatch " + a + "/" + b + " " + view);
        int n = A.Pixels.Length, over = 0, any = 0, max = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        var output = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            var p = A.Pixels[i]; var q = B.Pixels[i];
            int d = Math.Max(Math.Abs(p.r - q.r), Math.Max(Math.Abs(p.g - q.g), Math.Abs(p.b - q.b)));
            if (d > max) max = d;
            if (d == 0) { byte g = (byte)((p.r + p.g + p.b) / 12); output[i] = new Color32(g, g, g, 255); continue; }
            any++;
            byte v = (byte)Math.Min(255, 96 + d * 16);
            if (d > 8)
            {
                over++; int x = i % A.Width, y = i / A.Width;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                output[i] = new Color32(v, v, 0, 255);
            }
            else output[i] = new Color32(v, 0, 0, 255);
        }
        var image = new Texture2D(A.Width, A.Height, TextureFormat.RGB24, false); image.SetPixels32(output); image.Apply();
        string file = $"diff-{a}-vs-{b}-{view}.png";
        File.WriteAllBytes(Path.Combine(IdentityFolder, file), image.EncodeToPNG()); Destroy(image);
        // Texture rows start at the bottom; report the box in image (top-left origin) coordinates.
        string box = over == 0 ? "none" : $"x={x0}..{x1}, y={A.Height - 1 - y1}..{A.Height - 1 - y0}";
        Log($"IDENTITY {label} [{view}]: pixels with max channel delta>8/255 = {over} ({100.0 * over / n:F4}%), any delta = {any} ({100.0 * any / n:F4}%), max delta = {max}/255, >8 region(top-left origin) {box}; {file}");
    }

    void CompareReport()
    {
        if (builds.Count == 0) return;
        var names = new[] { Legacy.Name }.Concat(active.Select(c => c.Name)).ToArray();
        lines.Add("");
        lines.Add("=== BUILD / STRUCTURE per configuration (deterministic; every round listed) ===");
        lines.Add("config | rounds | cityArtBuild ms mean (min..max) | static finalize ms | renderers | prop renderers | static renderers | static-batched renderers | MeshFilters | distinct meshes | shared prop meshes");
        foreach (var name in names)
        {
            var set = builds.Where(b => b.Config == name).ToList(); if (set.Count == 0) continue;
            var f = set[0];
            lines.Add($"{name} | {set.Count} | {set.Average(b => b.BuildMs):F1} ({set.Min(b => b.BuildMs):F1}..{set.Max(b => b.BuildMs):F1}) | {set.Average(b => b.FinalizeMs):F1} | " +
                      $"{string.Join("/", set.Select(b => b.Renderers).Distinct())} | {f.PropRenderers} | {f.StaticRenderers} | {f.StaticBatchedRenderers} | {f.MeshFilters} | {string.Join("/", set.Select(b => b.DistinctMeshes).Distinct())} | {f.SharedPropMeshes}");
        }
        foreach (var view in views)
        foreach (bool dbl in new[] { false, true })
        {
            string mode = dbl ? "DOUBLE-render (historical harness; comparable with 155/50/40 history)" : "SINGLE-render (camera disabled + one Render per frame; the TRUE number)";
            var pool = samples.Where(s => s.View == view && !s.Profiled).ToList();
            lines.Add("");
            lines.Add($"=== VIEW {view.ToUpperInvariant()} — {mode} ===");
            lines.Add("config | FPS per round | FPS mean | ratio vs same-round LEGACY per round | ratio mean (min..max) | mean ms | p95 ms | drawCalls | batches | setPass | instanced draws | instanced batches | static-batched draws | static batches | tris | live renderers | visible renderers (isVisible, incl. shadow casters)");
            foreach (var name in names)
            {
                var set = pool.Where(s => s.Config == name && s.Double == dbl).OrderBy(s => s.Round).ToList(); if (set.Count == 0) continue;
                var ratios = new List<double>();
                foreach (var s in set)
                {
                    var legacy = pool.FirstOrDefault(l => l.Config == Legacy.Name && l.Double == dbl && l.Round == s.Round);
                    if (legacy != null && name != Legacy.Name) ratios.Add(s.Fps / legacy.Fps);
                }
                var m = set[set.Count / 2];
                string ratioText = ratios.Count == 0 ? "-" : string.Join(" / ", ratios.Select(r => r.ToString("F3")));
                string ratioMean = ratios.Count == 0 ? "-" : $"{ratios.Average():F3} ({ratios.Min():F3}..{ratios.Max():F3})";
                lines.Add($"{name} | {string.Join(" / ", set.Select(s => s.Fps.ToString("F2")))} | {set.Average(s => s.Fps):F2} | {ratioText} | {ratioMean} | {set.Average(s => s.Mean):F2} | {set.Average(s => s.P95):F2} | " +
                          $"{m.Draws} | {m.Batches} | {m.SetPass} | {m.Instanced} | {m.InstancedBatches} | {m.Static} | {m.StaticBatches} | {m.Tris} | {m.Renderers} | {m.Visible}");
            }
            var legacySet = pool.Where(s => s.Config == Legacy.Name && s.Double == dbl).Select(s => s.Fps).ToList();
            if (legacySet.Count > 1)
                lines.Add($"LEGACY drift across rounds ({(dbl ? "double" : "single")}): {string.Join(" / ", legacySet.Select(v => v.ToString("F2")))} FPS; spread {(legacySet.Max() / legacySet.Min() - 1) * 100:F1}%");
            // Paired S1 vs S2 when both ran.
            var s1 = pool.Where(x => x.Config == "P2+S1" && x.Double == dbl).ToList(); var s2 = pool.Where(x => x.Config == "P2+S2" && x.Double == dbl).ToList();
            var pairs = s1.Select(a => (a, b: s2.FirstOrDefault(b => b.Round == a.Round))).Where(t => t.b != null).OrderBy(t => t.a.Round).ToList();
            if (pairs.Count > 0)
                lines.Add($"PAIRED S2/S1 ({view}, {(dbl ? "double" : "single")}): {string.Join(" / ", pairs.Select(t => (t.b.Fps / t.a.Fps).ToString("F3")))}; mean {pairs.Average(t => t.b.Fps / t.a.Fps):F3}; S2 faster in {pairs.Count(t => t.b.Fps > t.a.Fps)}/{pairs.Count} rounds");
        }
        if (profiles.Count == 0) return;
        foreach (var view in profiles.Select(p => p.View).Distinct())
        {
            var set = profiles.Where(p => p.View == view).ToList();
            var keys = set.SelectMany(p => p.Values.Keys).Distinct().ToList();
            var rows = keys.Select(k => (k, vals: names.Select(n => { var v = set.Where(p => p.Config == n && p.Values.ContainsKey(k)).Select(p => p.Values[k]).ToList(); return v.Count == 0 ? double.NaN : v.Average(); }).ToArray()))
                .Where(r => r.k.StartsWith("[") || r.vals.Any(v => !double.IsNaN(v) && v >= .05) || ExactNames.Contains(r.k))
                .OrderByDescending(r => r.k.StartsWith("[")).ThenByDescending(r => r.vals.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max()).ToList();
            lines.Add("");
            lines.Add($"=== PROFILER {view.ToUpperInvariant()} — per-frame average, SINGLE-render, separate profiled sample; rounds averaged: {string.Join(", ", names.Select(n => n + " x" + set.Count(p => p.Config == n)))} ===");
            lines.Add($"profiled-sample FPS: {string.Join(", ", names.Select(n => n + "=" + (set.Any(p => p.Config == n) ? set.Where(p => p.Config == n).Average(p => p.Fps).ToString("F2") : "-")))}");
            lines.Add("marker / counter (ms unless marked) | " + string.Join(" | ", names));
            foreach (var r in rows)
            {
                bool count = set.Any(p => p.NonTime.Contains(r.k));
                lines.Add($"{r.k}{(count ? " (count/raw)" : "")} | " + string.Join(" | ", r.vals.Select(v => double.IsNaN(v) ? "-" : count ? v.ToString("F1") : v.ToString("F3"))));
            }
        }
    }
}
#endif
