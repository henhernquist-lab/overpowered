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
using UnityEngine.UIElements;

/// Visual polish BEFORE / AFTER evidence in one run (see PolishCapture). Captures the sprint's required views at 1920x1080
/// through the REAL gameplay camera plus the HUD panels (menus through their own panel), one shot per power, and a
/// performance sample (FPS, frame time, draw calls, batches, SetPass, triangles, visible renderers, distinct materials,
/// GC collections / managed growth) at the street, flight and combat views. Writes Verification/Polish/<tag>/*.png,
/// perf.csv and results.txt. It only captures and measures: it never edits assets, scenes or the real save.
public sealed class PolishCaptureRunner : MonoBehaviour
{
    public Action<int> Finished;
    public string Tag = "Before";
    readonly List<string> lines = new List<string>();
    readonly List<string> perf = new List<string> { "view,preset,fps,mean_ms,p95_ms,frames,draw_calls,batches,setpass,triangles,visible_renderers,distinct_materials,gc_gen0,managed_growth_kb" };
    WorldSession W => WorldSession.Instance;
    string Folder => Path.GetFullPath("Verification/Polish/" + Tag);
    const int Width = 1920, Height = 1080;
    int shots;

    void Log(string text) { lines.Add(text); Debug.Log("[POLISH] " + text); }
    IEnumerator Start()
    {
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
    void Finish(int code) { File.WriteAllLines(Path.Combine(Folder, "results.txt"), lines); File.WriteAllLines(Path.Combine(Folder, "perf.csv"), perf); Finished(code); }

    string PresetState => VisualPreset.Active(VisualPreset.Current) ? "on" : "off";
    IEnumerator Scene(string name, int settle = 10)
    {
        float until = Time.realtimeSinceStartup + 180;
        while (GameFlow.Instance.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        { if (Time.realtimeSinceStartup > until) throw new Exception("Scene load timeout " + name); yield return null; }
        for (int i = 0; i < settle; i++) yield return null;
    }
    IEnumerator Seconds(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }
    static PowerDefinition Power(string id) => Resources.Load<PowerDefinition>("Powers/" + id);

    IEnumerator Run()
    {
        yield return Scene(GameFlow.HomeScene);
        Log($"ENV tag={Tag} preset={PresetState} GPU={SystemInfo.graphicsDeviceName} CPU={SystemInfo.processorType} quality='{QualitySettings.names[QualitySettings.GetQualityLevel()]}' shadowDistance={QualitySettings.shadowDistance} unity={Application.unityVersion}");
        var catalog = Resources.Load<ForgeCatalog>("ForgeCatalog");
        var heroes = catalog != null ? catalog.Heroes.Where(h => h != null).ToArray() : new HeroDefinition[0];
        var hero = heroes.FirstOrDefault();
        Log($"Heroes: {string.Join(", ", heroes.Select(h => h.DisplayName))}; powers: {string.Join(", ", Resources.LoadAll<PowerDefinition>("Powers").Select(p => p.Id))}.");

        // ---- 1-2 menus
        yield return Menu("01-home");
        var ui = FindAnyObjectByType<ModeScreens>();
        if (ui.ForgeButton != null)
        {
            Submit(ui.ForgeButton); yield return Seconds(1.2f);
            yield return Menu("02-forge");
            if (ui.ForgeScreen != null) ui.ForgeScreen.Close();
        }
        else Log("SKIP 02-forge: no ForgeCatalog / Forge button.");

        // ---- 3-7, 10 world views (Free Play: no timer, no encounter pressure)
        yield return Enter(hero, null, null, "free-play");
        yield return View("03-downtown-street", Street("Downtown"));
        yield return Measure("downtown-street");
        yield return View("04-downtown-rooftop", Rooftop("Downtown"));
        yield return View("05-docks", Street("Docks"));
        yield return View("06-park", Street("Park"));
        yield return View("07-residential", Street("Residential"));
        yield return View("10-flight", Flight());
        yield return Measure("flight");
        Log("SKIP 11-night: the build has no night / dusk lighting mode.");

        // ---- 8 hero combat, 9 villain combat
        yield return Enter(hero, null, null, "hero");
        yield return Combat("08-hero-combat", true);
        yield return Measure("combat-hero");
        yield return Enter(hero, null, null, "villain");
        yield return Combat("09-villain-combat", false);
        yield return Measure("combat-villain");

        // ---- 12 endless
        yield return Enter(hero, null, null, "endless-fight");
        var endless = W.Mode.Director as EndlessWaveState;
        float until = Time.realtimeSinceStartup + 20; while (endless != null && endless.Alive.Count < 3 && Time.realtimeSinceStartup < until) yield return null;
        if (endless != null) { var arena = endless.Arena; yield return View("12-endless", (arena + new Vector3(0, 0, -8), 0f, W.Tuning.Camera.Pitch, "Endless arena, wave " + endless.Wave)); }
        yield return Measure("endless");

        // ---- per power (loadout set through PlayerProgression.SetLoadout; verification save only)
        foreach (var power in Resources.LoadAll<PowerDefinition>("Powers").OrderBy(p => p.Id))
        {
            var owner = heroes.FirstOrDefault(h => h.AvailablePowers != null && h.AvailablePowers.Contains(power)) ?? hero;
            var other = owner?.AvailablePowers?.FirstOrDefault(p => p != null && p != power) ?? Resources.LoadAll<PowerDefinition>("Powers").First(p => p != power);
            yield return Enter(owner, power, other, "hero");
            yield return PowerShot(power);
        }

        // ---- 13 results: end a Hero session as a win
        yield return Enter(hero, null, null, "hero");
        W.Mode.Finish(SessionOutcome.Won, "Polish capture");
        yield return Scene(GameFlow.ResultsScene); yield return Seconds(1.5f);
        yield return Menu("13-results");

        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        Log($"END {shots} captures in {Folder}. Compare Before/ and After/ side by side; perf.csv rows share view names.");
        Log("LIMIT: Editor Play Mode at 1920x1080 render targets; FPS is single-render Editor throughput (as in STATUS), not a standalone-player figure. Views are placed programmatically; judge composition by eye.");
    }

    // ------------------------------------------------------------------ sessions and placement
    IEnumerator Enter(HeroDefinition hero, PowerDefinition a, PowerDefinition b, string mode)
    {
        if (SceneManager.GetActiveScene().name != GameFlow.HomeScene) { GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene); }
        var ui = FindAnyObjectByType<ModeScreens>();
        if (hero != null && ui != null && ui.Profile != null)
        {
            a ??= hero.DefaultA; b ??= hero.DefaultB;
            if (a != null && b != null && !ui.Profile.SetLoadout(hero, a, b, hero.Primary, hero.Secondary)) Log($"NOTE loadout {hero.DisplayName} {a?.Id}+{b?.Id} refused; using the saved loadout.");
        }
        if (!GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/" + mode))) throw new Exception("Mode select refused: " + mode);
        yield return Scene(GameFlow.CityScene, 20);
        foreach (var npc in W.Npcs) if (npc != null && npc.Role == NpcRole.Civilian) npc.Freeze(5f);
    }
    int DistrictNamed(string name) { var d = W.City.Plan.Districts; for (int i = 0; i < d.Count; i++) if (string.Equals(d[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i; return W.City.DistrictAt(W.City.Spawn); }
    /// Mid-block sidewalk south of the district crossing with the most building height within 60 m, looking north along
    /// the street (the WorldProfile "street" rule, per district). Districts without crossings use their region centre.
    (Vector3 at, float yaw, float pitch, string where) Street(string district)
    {
        int d = DistrictNamed(district); var plan = W.City.Plan;
        var crossings = plan.Crossings.Where(x => x.District == d).ToList();
        if (crossings.Count == 0)
        {
            var c = plan.Districts[d].Region.center; var p = new Vector3(c.x, 0, c.y);
            if (NavMesh.SamplePosition(p, out var hit, 60f, NavMesh.AllAreas)) p = hit.position;
            return (p, 0f, W.Tuning.Camera.Pitch, $"{plan.Districts[d].Name} region centre (no street grid)");
        }
        float Height(CityPlan.Crossing x) => W.City.Buildings.Where(b => (new Vector2(b.Position.x, b.Position.z) - new Vector2(x.Center.x, x.Center.z)).magnitude < 60).Sum(b => b.Size.y);
        var best = crossings.OrderByDescending(x => Height(x)).First();
        var at = new Vector3(best.Center.x - best.Street * .5f - 1.5f, W.Tuning.City.SidewalkHeight, best.Center.z - best.Pitch * .5f);
        return (at, 0f, W.Tuning.Camera.Pitch, $"{plan.Districts[d].Name} west sidewalk south of its densest crossing {best.Center}");
    }
    (Vector3, float, float, string) Rooftop(string district)
    {
        int d = DistrictNamed(district);
        var b = W.City.Buildings.Where(x => x.District == d).OrderByDescending(x => x.Size.y).Skip(1).FirstOrDefault() ?? W.City.Buildings.OrderByDescending(x => x.Size.y).First();
        var centre = W.City.Plan.Districts[d].Region.center; var toward = new Vector3(centre.x - b.Position.x, 0, centre.y - b.Position.z);
        float yaw = toward.sqrMagnitude > 1 ? Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg : 0f;
        var roof = b.Position + Vector3.up * (b.Size.y * .5f + .1f) - toward.normalized * Mathf.Min(b.Size.x, b.Size.z) * .3f;
        return (roof, yaw, 12f, $"roof of the second-tallest {W.City.Plan.Districts[d].Name} building ({b.Size.y:F0} m) looking toward the district centre");
    }
    (Vector3, float, float, string) Flight()
    {
        var r = W.City.Plan.Districts[DistrictNamed("Downtown")].Region;
        return (new Vector3(r.center.x, 70, r.yMin), 0f, 10f, "70 m up over the Downtown south edge looking north (WorldProfile flight view)");
    }
    void Park((Vector3 at, float yaw, float pitch, string where) v)
    {
        W.Hero.enabled = false; var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false; W.Hero.transform.position = v.at; W.Hero.transform.rotation = Quaternion.Euler(0, v.yaw, 0); cc.enabled = true; Physics.SyncTransforms();
        var follow = Camera.main.GetComponent<ThirdPersonCamera>(); if (follow != null) { follow.enabled = true; follow.SetLook(v.yaw, v.pitch); }
    }
    IEnumerator View(string name, (Vector3 at, float yaw, float pitch, string where) v)
    {
        Park(v); yield return Seconds(1.2f);
        yield return Shot(name, v.where);
    }

    // ------------------------------------------------------------------ combat and powers
    readonly List<CityNpc> spawned = new List<CityNpc>();
    void Clear() { foreach (var n in spawned) if (n != null) Destroy(n.gameObject); spawned.Clear(); }
    /// The three base roles in a row ahead of the player (hero: criminals; villain: cops at 3 stars).
    IEnumerator Combat(string name, bool hero)
    {
        var v = Street("Downtown"); Park(v);
        var forward = Quaternion.Euler(0, v.Item2, 0) * Vector3.forward; var right = Quaternion.Euler(0, v.Item2, 0) * Vector3.right;
        var roles = new[] { "Rusher", "Gunner", "Brute" };
        if (!hero) { W.AddHeat(3f - W.Heat); W.ReconcilePolice(); }
        for (int i = 0; i < roles.Length; i++)
        {
            var at = v.Item1 + forward * 9f + right * ((i - 1) * 3f);
            if (NavMesh.SamplePosition(at, out var hit, 6f, NavMesh.AllAreas)) at = hit.position;
            var npc = CityNpc.Spawn(W, at, hero ? NpcRole.Criminal : NpcRole.Cop, Resources.Load<EnemyArchetype>("Enemies/" + roles[i]));
            if (npc != null) spawned.Add(npc);
        }
        yield return Seconds(1.5f);
        var selected = W.Powers.Selected;
        if (selected != null) { selected.Cooldown = 0; W.Powers.Use(selected); }
        yield return Seconds(.25f);
        yield return Shot(name, $"{(hero ? "hero" : "villain")} session, Rusher / Gunner / Brute spawned 9 m ahead, {(selected != null ? selected.Definition.DisplayName + " cast" : "no power selected")} 0.25 s before capture");
        Clear();
    }
    IEnumerator PowerShot(PowerDefinition power)
    {
        var v = Street("Downtown"); Park(v);
        var forward = Quaternion.Euler(0, v.Item2, 0) * Vector3.forward; var at = v.Item1 + forward * 10f;
        if (NavMesh.SamplePosition(at, out var hit, 6f, NavMesh.AllAreas)) at = hit.position;
        var target = CityNpc.Spawn(W, at, NpcRole.Criminal, Resources.Load<EnemyArchetype>("Enemies/Brute")); if (target != null) spawned.Add(target);
        yield return Seconds(.8f);
        var rt = W.Powers.Powers.FirstOrDefault(p => p.Definition == power);
        string what;
        if (rt == null) what = "not equipped (loadout refused)";
        else
        {
            rt.Cooldown = 0; rt.Charges = W.Powers.Stats(rt).Charges;
            if (power.Effect != null && power.Effect.IsFlight) { W.Hero.enabled = true; what = "flight: controller enabled, " + (W.Powers.Use(rt) ? "activated" : "Use refused: " + W.Powers.Message); yield return Seconds(.6f); }
            else what = (W.Powers.Select(rt) || rt == W.Powers.Strength) && W.Powers.Use(rt) ? "cast" : "Use refused: " + W.Powers.Message;
        }
        yield return Seconds(.3f);
        yield return Shot("power-" + power.Id, $"{power.DisplayName}: {what}; Brute 10 m ahead");
        Clear();
    }

    // ------------------------------------------------------------------ capture and measurement
    IEnumerator Shot(string name, string where)
    {
        var cam = Camera.main; var hud = FindAnyObjectByType<GameHud>();
        var target = new RenderTexture(Width, Height, 24) { name = "Polish " + name }; target.Create();
        if (hud != null) { hud.Panel.targetTexture = target; if (hud.OverlayPanel != null) hud.OverlayPanel.targetTexture = target; }
        cam.aspect = Width / (float)Height;
        yield return null; yield return null;
        bool was = cam.enabled; cam.enabled = false; cam.targetTexture = target; cam.Render(); cam.targetTexture = null;
        if (hud != null) { hud.Root.MarkDirtyRepaint(); if (hud.OverlayRoot != null) hud.OverlayRoot.MarkDirtyRepaint(); }
        yield return null;
        Save(target, name); cam.enabled = was; cam.ResetAspect();
        if (hud != null) { hud.Panel.targetTexture = null; if (hud.OverlayPanel != null) hud.OverlayPanel.targetTexture = null; }
        target.Release(); Destroy(target);
        Log($"CAPTURE {name}.png: {where}; hero {W.Hero.transform.position}, camera {cam.transform.position} fwd {cam.transform.forward}; preset {PresetState}.");
    }
    IEnumerator Menu(string name)
    {
        var ui = FindAnyObjectByType<ModeScreens>(); if (ui == null) { Log("SKIP " + name + ": no ModeScreens"); yield break; }
        var target = new RenderTexture(Width, Height, 24) { name = "Polish " + name }; target.Create();
        var previous = ui.Panel.targetTexture; ui.Panel.targetTexture = target;
        for (int i = 0; i < 15; i++) yield return null;
        Save(target, name); ui.Panel.targetTexture = previous; target.Release(); Destroy(target);
        Log($"CAPTURE {name}.png: {SceneManager.GetActiveScene().name} menu panel.");
    }
    void Save(RenderTexture rt, string name)
    {
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
        RenderTexture.active = previous; File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG()); Destroy(image); shots++;
    }
    static void Submit(Button button) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); } }
    /// Gameplay camera single-render sample at the CURRENT view (the STATUS method): 1 s warm-up, 4 s measured.
    IEnumerator Measure(string view)
    {
        var cam = Camera.main; var target = new RenderTexture(Width, Height, 24) { name = "Polish perf" }; target.Create();
        bool was = cam.enabled; cam.enabled = false; cam.targetTexture = target;
        var warm = System.Diagnostics.Stopwatch.StartNew(); while (warm.Elapsed.TotalSeconds < 1) { cam.Render(); yield return null; }
        var times = new List<double>(); var draws = new List<int>(); var batches = new List<int>(); var pass = new List<int>(); var tris = new List<long>();
        int gc0 = GC.CollectionCount(0); long managed = GC.GetTotalMemory(false);
        var watch = System.Diagnostics.Stopwatch.StartNew(); double prior = 0;
        while (watch.Elapsed.TotalSeconds < 4)
        {
            cam.Render(); yield return null;
            double now = watch.Elapsed.TotalSeconds; times.Add((now - prior) * 1000); prior = now;
            draws.Add(UnityStats.drawCalls); batches.Add(UnityStats.staticBatches + UnityStats.dynamicBatches + UnityStats.instancedBatches); pass.Add(UnityStats.setPassCalls); tris.Add(UnityStats.triangles);
        }
        double fps = times.Count / watch.Elapsed.TotalSeconds; int gc = GC.CollectionCount(0) - gc0; long growth = (GC.GetTotalMemory(false) - managed) / 1024;
        var visible = FindObjectsByType<Renderer>().Where(r => r.isVisible).ToList();
        int materials = visible.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
        var sorted = times.OrderBy(t => t).ToList();
        int Median(List<int> v) { var c = v.OrderBy(x => x).ToList(); return c.Count == 0 ? 0 : c[c.Count / 2]; }
        long MedianL(List<long> v) { var c = v.OrderBy(x => x).ToList(); return c.Count == 0 ? 0 : c[c.Count / 2]; }
        perf.Add(string.Join(",", view, PresetState, fps.ToString("F2"), times.Average().ToString("F2"), sorted[(int)(sorted.Count * .95f)].ToString("F2"), times.Count, Median(draws), Median(batches), Median(pass), MedianL(tris), visible.Count, materials, gc, growth));
        Log($"MEASURED {view} preset={PresetState}: FPS={fps:F2} mean={times.Average():F2}ms p95={sorted[(int)(sorted.Count * .95f)]:F2}ms draws={Median(draws)} batches={Median(batches)} setPass={Median(pass)} tris={MedianL(tris)} visibleRenderers={visible.Count} materials={materials} gcGen0={gc} managedGrowth={growth}KB");
        cam.targetTexture = null; cam.enabled = was; target.Release(); Destroy(target);
    }
}
#endif
