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

/// Diagnostic only: measures the shipping city with one hypothesis toggled at a time.
/// Changes nothing permanently; every control reverts and the run ends at the shipping state.
/// Sampling matches CityArtVerificationRunner.Benchmark so numbers stay comparable to history.
public sealed class PerformanceProfileRunner : MonoBehaviour
{
    public Action<int> Finished;
    readonly List<string> lines = new List<string>();
    readonly List<Sample> samples = new List<Sample>();
    string Folder => Path.GetFullPath("Verification/Performance");
    WorldSession W => WorldSession.Instance;
    Camera cam;
    Behaviour follow;
    RenderTexture target;
    bool restoreCombine;
    CityArtSettings artSettings;

    sealed class Sample
    {
        public string Label;
        public double Fps, Mean, P95;
        public int Draws, Batches, SetPass, Static, Dynamic, Instanced, Renderers;
        public long Tris, Verts;
    }

    void Log(string text) { lines.Add(text); Debug.Log("[PERF] " + text); }
    void Check(bool ok, string text) { if (!ok) throw new Exception(text); Log("PASS " + text); }

    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        artSettings = Resources.Load<CityArtSettings>("CityArtSettings");
        restoreCombine = artSettings.CombineMeshes;
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

    void Finish(int code)
    {
        artSettings.CombineMeshes = restoreCombine;
        Report();
        File.WriteAllLines(Path.Combine(Folder, "results.txt"), lines);
        Finished(code);
    }

    IEnumerator Scene(string name)
    {
        float until = Time.realtimeSinceStartup + 120;
        while (GameFlow.Instance.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        { if (Time.realtimeSinceStartup > until) throw new Exception("Scene load timeout " + name); yield return null; }
        for (int i = 0; i < 8; i++) yield return null;
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
    IEnumerator Measure(string label, bool doubleRender = true)
    {
        cam.enabled = doubleRender;
        yield return new WaitForSecondsRealtime(.6f);
        var times = new List<double>();
        var draws = new List<int>(); var batches = new List<int>(); var pass = new List<int>();
        var stat = new List<int>(); var dyn = new List<int>(); var inst = new List<int>();
        var tris = new List<long>(); var verts = new List<long>();
        var watch = System.Diagnostics.Stopwatch.StartNew(); double prior = 0;
        while (watch.Elapsed.TotalSeconds < 4)
        {
            cam.Render();
            yield return null;
            double now = watch.Elapsed.TotalSeconds; times.Add((now - prior) * 1000); prior = now;
            draws.Add(UnityStats.drawCalls); batches.Add(UnityStats.staticBatches + UnityStats.dynamicBatches + UnityStats.instancedBatches); pass.Add(UnityStats.setPassCalls);
            stat.Add(UnityStats.staticBatchedDrawCalls); dyn.Add(UnityStats.dynamicBatchedDrawCalls); inst.Add(UnityStats.instancedBatchedDrawCalls);
            tris.Add(UnityStats.triangles); verts.Add(UnityStats.vertices);
        }
        watch.Stop(); cam.enabled = true;
        var sorted = new List<double>(times); sorted.Sort();
        int live = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.enabled && r.gameObject.activeInHierarchy);
        var s = new Sample
        {
            Label = label,
            Fps = times.Count / watch.Elapsed.TotalSeconds,
            Mean = times.Average(),
            P95 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * .95f))],
            Draws = Median(draws), Batches = Median(batches), SetPass = Median(pass),
            Static = Median(stat), Dynamic = Median(dyn), Instanced = Median(inst),
            Tris = Median(tris), Verts = Median(verts), Renderers = live
        };
        samples.Add(s);
        Log($"MEASURED {label}: FPS={s.Fps:F2}, mean={s.Mean:F2}ms, p95={s.P95:F2}ms, frames={times.Count}, " +
            $"renderers={s.Renderers}, drawCalls={s.Draws}, batches={s.Batches}, setPass={s.SetPass}, " +
            $"staticBatched={s.Static}, dynamicBatched={s.Dynamic}, instanced={s.Instanced}, tris={s.Tris}, verts={s.Verts}");
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

    IEnumerator Run()
    {
        yield return Scene(GameFlow.HomeScene);
        Check(GraphicsSettings.currentRenderPipeline == null,
            "RENDER PIPELINE: Built-in confirmed (currentRenderPipeline==null). No URP, no ScriptableRendererFeature, no outline feature exists in this project, so the requested outline on/off control CANNOT be run.");
        Log("LIMIT: the briefed 'outline Renderer Feature' control is not applicable. Packages/manifest.json has no URP package; GraphicsSettings has no pipeline asset; the project has zero custom shaders and uses Shader.Find(\"Standard\").");

        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));
        yield return Scene(GameFlow.CityScene);
        yield return Stage();

        var buildings = W.City.GetComponentsInChildren<ArtBuilding>();
        var props = W.City.GetComponentsInChildren<CityArtProp>();
        int civilians = W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Civilian);
        int cops = W.Npcs.Count(n => n != null && !n.Dead && n.Role == NpcRole.Cop);
        var animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
        var skins = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var buildingRenderers = buildings.SelectMany(b => b.GetComponentsInChildren<Renderer>()).ToArray();
        var propRenderers = props.SelectMany(p => p.GetComponentsInChildren<Renderer>()).ToArray();
        var propBodies = props.SelectMany(p => p.GetComponentsInChildren<Rigidbody>()).ToArray();
        Log($"POPULATION: buildings={buildings.Length} ({buildingRenderers.Length} renderers), props={props.Length} ({propRenderers.Length} renderers), " +
            $"civilians={civilians}, cops={cops}, animators={animators.Length}, skinnedRenderers={skins.Length}, propRigidbodies={propBodies.Length}, " +
            $"totalRenderers={FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length}");
        Log($"SETTINGS: qualityLevel={QualitySettings.GetQualityLevel()} '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', shadows={QualitySettings.shadows}, " +
            $"pixelLightCount={QualitySettings.pixelLightCount}, lodBias={QualitySettings.lodBias}, shadowDistance={QualitySettings.shadowDistance}, " +
            $"GPU={SystemInfo.graphicsDeviceName}, CPU={SystemInfo.processorType}");
        int instancedMaterials = CityMaterials.Current.All.Count(m => m.enableInstancing);
        Log($"MATERIALS: palette materials={CityMaterials.Current.All.Count()}, enableInstancing=true on {instancedMaterials} of them, shader={CityMaterials.Get(CityColor.Road).shader.name}");
        int uniqueMeshes = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Select(f => f.sharedMesh).Where(m => m != null).Distinct().Count();
        int meshFilters = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Length;
        Log($"MESHES: {meshFilters} MeshFilters reference {uniqueMeshes} DISTINCT sharedMeshes. A ratio near 1:1 means instancing and static batching cannot merge anything.");

        // 0 — reference
        yield return Measure("00 baseline (as shipped, double-render like all recorded history)");
        // Harness artifact control
        yield return Measure("01 baseline SINGLE-render (camera.enabled=false; quantifies harness double-render cost)", false);

        // Rendering hypotheses — renderers only, so physics/AI cost stays constant.
        yield return Control("02 city PROP renderers disabled",
            () => { foreach (var r in propRenderers) if (r) r.enabled = false; },
            () => { foreach (var r in propRenderers) if (r) r.enabled = true; });

        yield return Control("03 BUILDING renderers disabled",
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

        // Per-frame material writes
        var materialsComponent = CityMaterials.Current;
        yield return Control("09 CityMaterials per-frame Apply() disabled",
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

        // Whole-city rebuild control: does the existing CombineMeshes toggle help or hurt?
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        artSettings.CombineMeshes = false;
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));
        yield return Scene(GameFlow.CityScene);
        yield return Stage();
        int rawRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
        int rawUnique = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Select(f => f.sharedMesh).Where(m => m != null).Distinct().Count();
        Log($"REBUILD CombineMeshes=false: renderers={rawRenderers}, distinct sharedMeshes={rawUnique} (primitive cubes/cylinders are SHARED meshes, so batching CAN apply here)");
        yield return Measure("13 CombineMeshes=FALSE rebuild (uncombined primitives, shared meshes)");
        Save("city-uncombined");
        artSettings.CombineMeshes = restoreCombine;

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
}
#endif
