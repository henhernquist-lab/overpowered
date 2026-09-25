#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// GameHud verification. Real GameFlow scene loads, real WorldSession/PowerUser/PlayerProgression state, real power use.
/// Captures are COMPOSITED: the gameplay camera renders into a RenderTexture and the HUD panel (clearColor=false)
/// draws into the same texture later in that frame; a camera-only render of the same frame is kept for pixel checks.
public sealed class HudVerificationRunner : MonoBehaviour
{
    public string Folder;
    public Action<int> Finished;
    readonly List<string> output = new List<string>();
    WorldSession W => WorldSession.Instance;
    GameFlow Flow => GameFlow.Instance;
    GameHud hud; PrototypeHUD legacy; ModeScreens ui; RenderTexture uiTarget;
    CityPalette palette; ForgeCatalog forge;
    static readonly Vector2Int[] Resolutions = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) };

    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder); QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        var stack = new Stack<IEnumerator>(); stack.Push(Checks());
        while (stack.Count > 0)
        {
            object next = null; bool moved = false;
            try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception e) { Log("FAIL " + e); Write(); Time.timeScale = 1; Finished(1); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested); else yield return next;
        }
        Write(); Finished(0);
    }
    void Log(string text) { output.Add(text); Debug.Log("[HUD] " + text); }
    void Check(bool valid, string text) { if (!valid) throw new Exception(text); Log("PASS " + text); }
    void Write() { File.WriteAllLines(Path.Combine(Folder, "results.txt"), output); }

    // ---------------------------------------------------------------- helpers
    IEnumerator Scene(string name)
    {
        float deadline = Time.realtimeSinceStartup + 40;
        while (Flow.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && (W == null || FindAnyObjectByType<GameHud>() == null)))
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Scene timeout " + name); yield return null; }
        yield return null;
        if (name == GameFlow.CityScene) { hud = FindAnyObjectByType<GameHud>(); legacy = FindAnyObjectByType<PrototypeHUD>(); }
    }
    IEnumerator Menu()
    {
        ui = FindAnyObjectByType<ModeScreens>(); Check(ui != null, "Menu component exists in " + SceneManager.GetActiveScene().name);
        if (uiTarget != null) { uiTarget.Release(); Destroy(uiTarget); }
        uiTarget = new RenderTexture(1280, 720, 24) { name = "Verification menu UI" }; uiTarget.Create(); ui.Panel.targetTexture = uiTarget;
        for (int i = 0; i < 10; i++) yield return null;
    }
    IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    IEnumerator Realtime(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    IEnumerator Launch(GameModeDefinition mode)
    {
        if (SceneManager.GetActiveScene().name != GameFlow.HomeScene) { Flow.Home(); yield return Scene(GameFlow.HomeScene); }
        Check(Flow.Select(mode), "GameFlow.Select launches " + mode.name);
        yield return Scene(GameFlow.CityScene);
        Check(W.Mode != null && W.Mode.Definition == mode && hud != null && legacy != null, $"{mode.DisplayName}: session runs this definition; GameHud + PrototypeHUD exist.");
        yield return Frames(5);
    }
    Camera GameplayCamera() { var follow = FindAnyObjectByType<ThirdPersonCamera>(); return follow != null ? follow.GetComponent<Camera>() : Camera.main; }
    static string R(Rect r) => $"({r.x:0.#},{r.y:0.#} {r.width:0.#}x{r.height:0.#})";
    HudSlot SlotFor(string id) => hud.Slots.FirstOrDefault(s => s.Id == id);

    // ---------------------------------------------------------------- layout assertions (panel logical units)
    static IEnumerable<VisualElement> Descendants(VisualElement root) { yield return root; foreach (var child in root.Children()) foreach (var d in Descendants(child)) yield return d; }
    void AssertLayout(Vector2Int size, string label)
    {
        float scale = Mathf.Min(size.x / (float)GameHud.ReferenceResolution.x, size.y / (float)GameHud.ReferenceResolution.y);
        var panel = hud.Root.layout; Vector2 expected = new Vector2(size.x / scale, size.y / scale);
        Check(Mathf.Abs(panel.width - expected.x) < 1.5f && Mathf.Abs(panel.height - expected.y) < 1.5f,
            $"{label}: Expand scaling -> panel {panel.width:0.#}x{panel.height:0.#} logical (expected {expected.x:0.#}x{expected.y:0.#}, scale {scale:0.###}); the 1600x900 reference area fits.");
        float m = GameHud.SafeMargin - .5f; var safe = Rect.MinMaxRect(m, m, panel.width - m, panel.height - m);
        var centre = new Rect(panel.width * (1 - GameHud.CentreZone) * .5f, panel.height * (1 - GameHud.CentreZone) * .5f, panel.width * GameHud.CentreZone, panel.height * GameHud.CentreZone);
        var clipped = new List<string>(); var inCentre = new List<string>(); var truncated = new List<string>(); int count = 0;
        var shownGroups = hud.Groups.Where(GameHud.Shown).ToList();
        foreach (var group in shownGroups)
            foreach (var e in Descendants(group))
            {
                if (!GameHud.Shown(e)) continue; var r = e.worldBound; if (r.width <= .01f || r.height <= .01f) continue; count++;
                string id = group.name + "/" + (string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name) + (e is Label l0 ? "'" + l0.text + "'" : "");
                if (r.xMin < safe.xMin || r.yMin < safe.yMin || r.xMax > safe.xMax || r.yMax > safe.yMax) clipped.Add(id + R(r));
                if (r.Overlaps(centre)) inCentre.Add(id + R(r));
                if (e is Label text && !string.IsNullOrEmpty(text.text))
                {
                    var content = text.contentRect;
                    if (text.resolvedStyle.whiteSpace == WhiteSpace.NoWrap)
                    { var need = text.MeasureTextSize(text.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined); if (need.x > content.width + 1f) truncated.Add($"{id} needs {need.x:0.#} has {content.width:0.#}"); }
                    else { var need = text.MeasureTextSize(text.text, content.width + 1f, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined); if (need.y > content.height + 1f) truncated.Add($"{id} needs h{need.y:0.#} has h{content.height:0.#}"); }
                }
            }
        Check(clipped.Count == 0, $"{label}: all {count} visible HUD elements lie inside the panel with the {GameHud.SafeMargin}px safe margin" + (clipped.Count > 0 ? " — CLIPPED: " + string.Join("; ", clipped) : "."));
        Check(inCentre.Count == 0, $"{label}: no HUD element except the crosshair intersects the centre 40%x40% zone {R(centre)}" + (inCentre.Count > 0 ? " — IN CENTRE: " + string.Join("; ", inCentre) : "."));
        Check(truncated.Count == 0, $"{label}: no visible label text is truncated (measured text fits its box)" + (truncated.Count > 0 ? " — " + string.Join("; ", truncated) : "."));
        var overlaps = new List<string>();
        for (int i = 0; i < shownGroups.Count; i++) for (int j = i + 1; j < shownGroups.Count; j++) if (shownGroups[i].worldBound.Overlaps(shownGroups[j].worldBound)) overlaps.Add(shownGroups[i].name + " x " + shownGroups[j].name);
        Check(overlaps.Count == 0, $"{label}: HUD groups do not overlap each other: " + string.Join(" ", shownGroups.Select(g => g.name + R(g.worldBound))) + (overlaps.Count > 0 ? " — OVERLAP: " + string.Join(", ", overlaps) : ""));
        if (GameHud.Shown(hud.Crosshair))
        {
            var c = hud.Crosshair.worldBound.center * scale; var screen = new Vector2(size.x * .5f, size.y * .5f);
            Check(Vector2.Distance(c, screen) <= 1f, $"{label}: crosshair centre at {c.x:0.##},{c.y:0.##} px vs screen centre {screen.x:0.#},{screen.y:0.#} (|d| {Vector2.Distance(c, screen):0.###} px <= 1) — the aim ray is ViewportPointToRay(.5,.5).");
        }
    }

    // ---------------------------------------------------------------- composited capture + pixel checks
    IEnumerator Composite(Vector2Int size, string name, bool layout = true)
    {
        var cam = GameplayCamera(); Check(cam != null, "Gameplay camera found for " + name);
        var composite = new RenderTexture(size.x, size.y, 24) { name = "HUD composite " + name }; composite.Create();
        var plain = new RenderTexture(size.x, size.y, 24) { name = "Camera only " + name }; plain.Create();
        hud.Panel.targetTexture = composite;
        yield return Frames(4); // layout at the new panel size
        if (layout) AssertLayout(size, $"{name} {size.x}x{size.y}");
        // Same frame: camera-only render, then the camera into the composite target; the HUD panel draws into that
        // target without clearing later in this frame (PanelSettings.clearColor=false).
        bool wasEnabled = cam.enabled; cam.enabled = false;
        cam.targetTexture = plain; cam.Render(); cam.targetTexture = composite; cam.Render(); cam.targetTexture = null;
        hud.Root.MarkDirtyRepaint();
        yield return null;
        var a = Read(composite); var b = Read(plain);
        cam.enabled = wasEnabled;
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), a.EncodeToPNG());
        PixelChecks(a, b, size, name);
        Destroy(a); Destroy(b);
        hud.Panel.targetTexture = null; composite.Release(); plain.Release(); Destroy(composite); Destroy(plain);
        yield return Frames(2);
    }
    static Texture2D Read(RenderTexture target)
    {
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        RenderTexture.active = previous; return image;
    }
    void PixelChecks(Texture2D composite, Texture2D plain, Vector2Int size, string name)
    {
        var a = composite.GetPixels32(); var b = plain.GetPixels32(); float scale = size.x / hud.Root.layout.width;
        int Diff(int x, int y) { int i = y * size.x + x; return Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b))); }
        RectInt Pixels(Rect r)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(r.xMin * scale), 0, size.x), x1 = Mathf.Clamp(Mathf.CeilToInt(r.xMax * scale), 0, size.x);
            int y0 = Mathf.Clamp(size.y - Mathf.CeilToInt(r.yMax * scale), 0, size.y), y1 = Mathf.Clamp(size.y - Mathf.FloorToInt(r.yMin * scale), 0, size.y);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }
        float Changed(RectInt r) { int n = 0, c = 0; for (int y = r.yMin; y < r.yMax; y++) for (int x = r.xMin; x < r.xMax; x++) { n++; if (Diff(x, y) > 10) c++; } return n == 0 ? 0 : c / (float)n; }
        var parts = new List<string>();
        foreach (var group in hud.Groups.Where(GameHud.Shown))
        {
            float changed = Changed(Pixels(group.worldBound)); parts.Add($"{group.name} {changed:P0}");
            Check(changed > .15f, $"{name}: composite contains the HUD {group.name} (pixels changed vs the camera-only render: {changed:P1}).");
        }
        var cross = Pixels(hud.Crosshair.worldBound); bool crossShown = GameHud.Shown(hud.Crosshair);
        if (crossShown) Check(Changed(cross) > .02f, $"{name}: crosshair drawn into the composite ({Changed(cross):P1} of its box changed).");
        var zone = new RectInt(Mathf.RoundToInt(size.x * .3f), Mathf.RoundToInt(size.y * .3f), Mathf.RoundToInt(size.x * .4f), Mathf.RoundToInt(size.y * .4f));
        var exempt = new RectInt(cross.xMin - 2, cross.yMin - 2, cross.width + 4, cross.height + 4); int offenders = 0, maxDiff = 0, total = 0;
        for (int y = zone.yMin; y < zone.yMax; y++) for (int x = zone.xMin; x < zone.xMax; x++)
        { if (crossShown && exempt.Contains(new Vector2Int(x, y))) continue; total++; int d = Diff(x, y); maxDiff = Mathf.Max(maxDiff, d); if (d > 3) offenders++; }
        Check(offenders == 0, $"{name}: PIXEL centre-clear — {total} centre-zone pixels outside the crosshair box are identical to the camera-only render (max channel diff {maxDiff}/255, {offenders} > 3).");
        Log($"CAPTURE {name}.png {size.x}x{size.y} composited (gameplay camera + HUD panel, one render target); HUD coverage: {string.Join(", ", parts)}.");
    }

    // ---------------------------------------------------------------- visibility, driven by the definition data
    void AssertVisibility(GameModeDefinition d, string label)
    {
        var flags = d.Hud;
        bool Expect(VisualElement e, bool expected, string what) { bool shown = GameHud.Shown(e); Check(shown == expected, $"{label}: {what} {(expected ? "SHOWN" : "HIDDEN")} (data: {WhatDrives(what, d)})."); return shown; }
        Expect(hud.VitalsGroup, (flags & ModeHud.Health) != 0, "health/energy");
        Expect(hud.PowerBar, (flags & ModeHud.Powers) != 0, "power bar");
        Expect(hud.LevelGroup, (flags & ModeHud.Progression) != 0, "level/XP");
        Expect(hud.StarsRow, (flags & ModeHud.Heat) != 0, "heat stars");
        Expect(hud.TimerRow, d.SessionSeconds > 0, "session timer");
        Expect(hud.ObjectiveGroup, (flags & ModeHud.Objectives) != 0, "objective slot");
        Expect(hud.DirectorGroup, W.Mode.Director != null, "director panel");
    }
    static string WhatDrives(string what, GameModeDefinition d)
    {
        switch (what)
        {
            case "session timer": return "SessionSeconds=" + d.SessionSeconds;
            case "director panel": return "Director=" + (d.Director != null ? d.Director.name : "none");
            default: return "Hud=" + d.Hud;
        }
    }

    // ---------------------------------------------------------------- the run
    IEnumerator Checks()
    {
        palette = Resources.Load<CityPalette>("CityPalette"); forge = Resources.Load<ForgeCatalog>("ForgeCatalog");
        var catalog = Resources.LoadAll<GameModeDefinition>("Modes"); GameModeDefinition Mode(string id) => catalog.First(m => m.Id == id);
        var hero = Mode("hero"); var villain = Mode("villain"); var freePlay = Mode("free-play"); var endless = Mode("endless-fight");
        yield return Scene(GameFlow.HomeScene); yield return Menu();
        Check(ui.Profile.Data.Level == 1 && ui.Profile.Data.SessionsPlayed == 0, "Fresh isolated save CONTROL: level 1, no sessions.");
        var defaultA = ui.Profile.EquippedA; var defaultB = ui.Profile.EquippedB;
        Log($"Default loadout from the fresh save: {ui.Profile.SelectedHero.Id} / {defaultA.Id} + {defaultB.Id}.");

        // ---------------- Hero (default loadout): layout at 3 resolutions, debug toggle, heat pop, pips, radial
        yield return Launch(hero);
        yield return Realtime(1f); W.AddHeat(2.2f); yield return Realtime(.8f);
        AssertVisibility(hero, "Hero");
        Check(hud.TimerValue.text == $"{Mathf.CeilToInt(hero.SessionSeconds - W.Mode.Elapsed) / 60:00}:{Mathf.CeilToInt(hero.SessionSeconds - W.Mode.Elapsed) % 60:00}", $"Hero: timer shows remaining time {hud.TimerValue.text} (SessionSeconds {hero.SessionSeconds}, elapsed {W.Mode.Elapsed:0.0}).");
        Check(hud.StarCount == W.Tuning.Heat.MaximumStars && hud.ShownStars == W.Stars && W.Stars == 3, $"Hero: {hud.ShownStars} of {hud.StarCount} Heat stars lit = WorldSession.Stars {W.Stars} (Heat {W.Heat:0.00}).");
        Check(!string.IsNullOrEmpty(hud.ObjectiveBody.text), "Hero: objective slot text: \"" + hud.ObjectiveBody.text + "\"");
        yield return DebugPanel();
        foreach (var r in Resolutions) yield return Composite(r, $"hero-{r.x}x{r.y}");
        yield return DefaultLoadout(defaultA, defaultB);
        yield return HeatPop();

        // ---------------- Villain
        yield return Launch(villain);
        yield return Realtime(1f); W.AddHeat(2.2f); yield return Realtime(.8f);
        AssertVisibility(villain, "Villain");
        Check(GameHud.Shown(hud.TimerRow) && GameHud.Shown(hud.ObjectiveGroup), "Villain: timer and objective slot shown.");
        foreach (var r in Resolutions) yield return Composite(r, $"villain-{r.x}x{r.y}");

        // ---------------- Free Play
        yield return Launch(freePlay);
        yield return Realtime(1.5f);
        AssertVisibility(freePlay, "Free Play");
        Check(!GameHud.Shown(hud.TimerRow) && !GameHud.Shown(hud.ObjectiveGroup) && !GameHud.Shown(hud.DirectorGroup) && GameHud.Shown(hud.StarsRow), "Free Play: no timer, no objective, no director; Heat stars shown.");
        foreach (var r in Resolutions) yield return Composite(r, $"freeplay-{r.x}x{r.y}");

        // ---------------- Endless Fight (Hero)
        yield return Launch(endless);
        var waves = (EndlessWaveState)W.Mode.Director;
        float guard = 0; while (waves.Wave < 1 && guard < 30) { W.Mode.Tick(.5f); guard += .5f; yield return null; }
        yield return Realtime(1f);
        AssertVisibility(endless, "Endless");
        Check(GameHud.Shown(hud.DirectorGroup) && !GameHud.Shown(hud.ObjectiveGroup) && !GameHud.Shown(hud.HeatGroup), "Endless: wave/score panel shown, no objective, no Heat/timer group (Hud=" + endless.Hud + ", SessionSeconds=" + endless.SessionSeconds + ").");
        var list = new List<DirectorHudStat>(); waves.HudStats(list);
        Check(list.Count == hud.DirectorValues.Count && Enumerable.Range(0, list.Count).All(i => hud.DirectorValues[i].text == list[i].Value.ToString() && hud.DirectorLabels[i].text == list[i].Label),
            "Endless: HUD shows the director's structured values " + string.Join(" · ", list.Select(s => s.Label + " " + s.Value)) + " (wave " + waves.Wave + ", score " + W.Mode.Score + ", best " + waves.Best + ").");
        Check(PrototypeHUD.DirectorLine(W) == $"WAVE {waves.Wave} · LEFT {waves.Remaining} · SCORE {W.Mode.Score} · BEST {waves.Best}", "Endless: legacy HudLine unchanged: " + PrototypeHUD.DirectorLine(W));
        foreach (var r in Resolutions) yield return Composite(r, $"endless-{r.x}x{r.y}");

        // ---------------- data controls on CLONED definitions
        yield return FlagControls(hero, freePlay);

        // ---------------- loadout swap (Fire + Ice), rebuild, radial after real use, FPS A/B
        yield return SwapLoadout();

        Flow.Home(); yield return Scene(GameFlow.HomeScene);
        Log("LIMIT: no hardware keyboard in batch mode; the F3 control calls PrototypeHUD.ToggleDebug, the method the F3 handler calls. Captures come from batch-mode Metal render targets, not a physical display. The restyled Esc pause and Tab upgrade menus are still IMGUI (out of scope).");
    }

    IEnumerator DebugPanel()
    {
        Check(PrototypeHUD.DebugKey == KeyCode.F3, "Debug key is F3.");
        Check(!legacy.DebugVisible, "Debug panel HIDDEN by default.");
        int r0 = legacy.RepaintEvents, d0 = legacy.DebugRepaints; yield return Frames(20);
        int repaints = legacy.RepaintEvents - r0;
        Check(legacy.DebugRepaints == d0, $"Hidden debug panel: its IMGUI draw path ran 0 times over 20 frames ({repaints} IMGUI repaint events reached PrototypeHUD.OnGUI).");
        legacy.ToggleDebug(); Check(legacy.DebugVisible, "CONTROL: ToggleDebug (the F3 handler's method) SHOWS the debug panel.");
        r0 = legacy.RepaintEvents; d0 = legacy.DebugRepaints; yield return Frames(20);
        int shownRepaints = legacy.RepaintEvents - r0, shownDraws = legacy.DebugRepaints - d0;
        if (shownRepaints > 0) Check(shownDraws == shownRepaints, $"Visible debug panel drew on {shownDraws}/{shownRepaints} IMGUI repaints.");
        else Log($"LIMIT: batch mode dispatched {shownRepaints} IMGUI repaint events in 20 frames, so the debug draw is proven by its gate (DebugVisible) and counters, not by pixels.");
        legacy.ToggleDebug(); Check(!legacy.DebugVisible, "CONTROL: toggling again HIDES the debug panel.");
        d0 = legacy.DebugRepaints; yield return Frames(10); Check(legacy.DebugRepaints == d0, "Hidden again: debug draw path idle.");
    }

    IEnumerator DefaultLoadout(PowerDefinition a, PowerDefinition b)
    {
        var p = W.Powers;
        Check(hud.Slots.Count == 3 && hud.Slots[0].Id == a.Id && hud.Slots[1].Id == b.Id && hud.Slots[2].Synergy == p.Synergy && p.Synergy == forge.Resolve(a, b),
            $"Default loadout bar: {string.Join(" | ", hud.Slots.Select(s => s.Id + "[" + s.KeyText + "]"))} = EquippedA {a.Id} + EquippedB {b.Id} + synergy {p.Synergy?.Id}.");
        Check(!GameHud.Shown(hud.MeleeHint), "Strength equipped -> no unslotted basic-melee hint.");
        var flight = hud.Slots.FirstOrDefault(s => s.Flight);
        if (flight != null) Check(flight.FuelFill != null && flight.Pips.Count == 0 && Mathf.Abs(flight.FuelFraction - p.Flight.Fuel / p.Stats(p.Flight).Duration) < .01f, $"Flight slot shows fuel {flight.FuelFraction:P0} instead of charge pips.");
        var strength = SlotFor("strength");
        if (strength != null)
        {
            int max = p.Stats(p.Strength).Charges;
            Check(strength.Pips.Count == max && strength.LitPips == p.Strength.Charges && strength.LitPips == max && strength.CooldownFraction == 0f, $"CONTROL before use: Strength pips {strength.LitPips}/{strength.Pips.Count}, radial {strength.CooldownFraction:0.00}.");
            int impact = W.Hero.LastImpactFrame;
            Check(W.Hero.TryPunch(), "Real paid punch accepted (SuperHeroController.TryPunch).");
            yield return null;
            Check(p.Strength.Charges == max - 1 && strength.LitPips == max - 1, $"Charge pips drop on payment: {strength.LitPips}/{strength.Pips.Count} lit (runtime charges {p.Strength.Charges}).");
            Check(strength.CooldownFraction > 0f, $"Radial cooldown shows after the real punch: {strength.CooldownFraction:0.00} of {p.Stats(p.Strength).Cooldown:0.00}s.");
            yield return Composite(new Vector2Int(1920, 1080), "hero-default-loadout-after-punch", false);
            float until = Time.realtimeSinceStartup + 3; while (W.Hero.LastImpactFrame == impact && Time.realtimeSinceStartup < until) yield return null;
            Check(W.Hero.LastImpactFrame != impact, $"Punch impact landed after the windup (frame {W.Hero.LastImpactFrame}); {W.Hero.LastPunchResult}.");
            Check(strength.LitPips == p.Strength.Charges, $"After impact the pips still match the runtime: {strength.LitPips}/{strength.Pips.Count}.");
        }
        var synergy = hud.Slots.FirstOrDefault(s => s.Synergy != null);
        Check(synergy.CooldownFraction == 0f && synergy.KeyText == HudBindings.KeyName(forge.SynergyKey), $"Synergy slot key '{synergy.KeyText}' from ForgeCatalog.SynergyKey ({forge.SynergyKey}); radial 0 while ready.");
        yield return Realtime(.8f);
        if (p.SynergyRunner.TryActivate())
        {
            yield return null;
            Check(synergy.CooldownFraction > .9f, $"Synergy {p.Synergy.DisplayName} used for real -> radial {synergy.CooldownFraction:0.00} (runner cooldown {p.SynergyRunner.Cooldown:0.0}/{p.Synergy.Cooldown:0.0}s).");
            yield return Composite(new Vector2Int(1920, 1080), "hero-default-loadout-synergy-cooldown", false);
            float until = Time.realtimeSinceStartup + 8; while (p.SynergyRunner.Busy && Time.realtimeSinceStartup < until) yield return null;
        }
        else Log("NOTE: synergy refused here (" + p.SynergyRunner.Feedback + "); the radial is proven by the Strength punch above.");
    }

    IEnumerator HeatPop()
    {
        yield return Realtime(.6f);
        int before = W.Stars; Check(Enumerable.Range(0, hud.StarCount).All(i => hud.StarScale(i) == 1f), $"Stars at rest (scale 1) with {before} lit.");
        Time.timeScale = 0f; // prove the pop runs on UNSCALED time (Phase 4 hit-pause uses timeScale)
        W.AddHeat(1f); int after = W.Stars; Check(after == before + 1, $"Real Heat change: WorldSession.AddHeat(1) -> {before} -> {after} stars.");
        float start = Time.realtimeSinceStartup, peak = 1f; bool captured = false;
        while (Time.realtimeSinceStartup - start < GameHud.StarPopSeconds + .35f)
        {
            yield return null; float s = hud.StarScale(after - 1); peak = Mathf.Max(peak, s);
            if (!captured && s > 1.3f) { captured = true; yield return Composite(new Vector2Int(1920, 1080), "hero-heat-star-pop", false); }
        }
        float rest = hud.StarScale(after - 1);
        Time.timeScale = 1f;
        Check(peak > 1.2f && rest == 1f && hud.ShownStars == after, $"Star {after} popped to scale {peak:0.00} and settled at {rest:0.00} while Time.timeScale was 0 (unscaled-time animation).");
        Check(Enumerable.Range(0, after - 1).All(i => hud.StarScale(i) == 1f), "CONTROL: unchanged stars did not pop.");
    }

    IEnumerator FlagControls(GameModeDefinition hero, GameModeDefinition freePlay)
    {
        var clone = Instantiate(hero); clone.name = "hero HUD-control clone";
        yield return Launch(clone);
        yield return Realtime(.5f); W.AddHeat(1.2f); yield return Frames(3);
        AssertVisibility(clone, "Clone (unchanged flags)");
        clone.Hud &= ~(ModeHud.Objectives | ModeHud.Heat); clone.SessionSeconds = 0; yield return Frames(3);
        Check(!GameHud.Shown(hud.ObjectiveGroup) && !GameHud.Shown(hud.StarsRow) && !GameHud.Shown(hud.TimerRow) && !GameHud.Shown(hud.HeatGroup) && GameHud.Shown(hud.VitalsGroup),
            "CONTROL: clearing Objectives+Heat and SessionSeconds=0 on the CLONED definition hides objective, stars, timer (and their group); vitals stay.");
        clone.Hud &= ~(ModeHud.Health | ModeHud.Powers | ModeHud.Progression); yield return Frames(3);
        Check(!GameHud.Shown(hud.VitalsGroup) && !GameHud.Shown(hud.PowerBar) && !GameHud.Shown(hud.LevelGroup), "CONTROL: clearing Health/Powers/Progression hides vitals, power bar and level.");
        yield return Composite(new Vector2Int(1920, 1080), "control-clone-all-flags-off", false);
        clone.Hud = hero.Hud; clone.SessionSeconds = hero.SessionSeconds; yield return Frames(3);
        AssertVisibility(clone, "Clone (flags restored)");
        Check(hero.Hud == ModeHud.All && hero.SessionSeconds > 0, "Shipping hero definition untouched (Hud=" + hero.Hud + ", SessionSeconds=" + hero.SessionSeconds + ").");
        Flow.Home(); yield return Scene(GameFlow.HomeScene); Destroy(clone);

        var directed = Instantiate(freePlay); directed.name = "free-play + director HUD-control clone";
        directed.Director = Resources.Load<EndlessWaveDirector>("ModeDirectors/EndlessWaves");
        yield return Launch(directed); yield return Frames(3);
        Check(W.Mode.Director != null && GameHud.Shown(hud.DirectorGroup) && hud.DirectorValues.Count > 0, $"CONTROL: Free Play clone given a Director shows the director panel ({hud.DirectorValues.Count} values); the shipping Free Play (Director none) showed none.");
        Check(freePlay.Director == null, "Shipping Free Play definition untouched.");
        Flow.Home(); yield return Scene(GameFlow.HomeScene); Destroy(directed);
    }

    IEnumerator SwapLoadout()
    {
        if (SceneManager.GetActiveScene().name != GameFlow.HomeScene) { Flow.Home(); yield return Scene(GameFlow.HomeScene); }
        yield return Menu();
        PowerDefinition Power(string id) => Resources.Load<PowerDefinition>("Powers/" + id);
        var fire = Power("fire"); var ice = Power("ice");
        ui.Profile.AddXp(2000); foreach (var power in new[] { fire, ice }) if (!ui.Profile.Owns(power)) Check(ui.Profile.Buy(power), "Existing progression unlock " + power.Id);
        var loadout = ui.Profile.Data.Loadout; var heroDefinition = ui.Profile.SelectedHero;
        Check(ui.Profile.SetLoadout(heroDefinition, fire, ice, loadout.Primary, loadout.Secondary), $"PlayerProgression.SetLoadout -> {heroDefinition.Id} / Fire + Ice.");
        var mode = Resources.LoadAll<GameModeDefinition>("Modes").First(m => m.Id == "hero");
        yield return Launch(mode);
        var p = W.Powers; var synergy = forge.Resolve(fire, ice);
        Check(p.EquippedA == fire && p.EquippedB == ice && p.Synergy == synergy, $"New session equips Fire + Ice; synergy {synergy?.DisplayName}.");
        Check(hud.Slots.Count == 3 && hud.Slots[0].Id == fire.Id && hud.Slots[1].Id == ice.Id && hud.Slots[2].Synergy == synergy, "Rebuilt bar: " + string.Join(" | ", hud.Slots.Select(s => s.Id + "[" + s.KeyText + "]")));
        foreach (var slot in hud.Slots.Where(s => s.Power != null))
        {
            var d = slot.Power.Definition;
            Check(slot.Glyph == d.MenuIcon && slot.Icon.Glyph == d.MenuIcon && slot.Color == palette.Colors[(int)d.PaletteColor] && slot.Pips.Count == p.Stats(slot.Power).Charges && slot.KeyText == HudBindings.PowerKey(p, slot.Power),
                $"{d.Id} slot: icon {slot.Icon.Glyph} = MenuIcon, colour = palette {d.PaletteColor}, {slot.Pips.Count} pips = charges, key '{slot.KeyText}' (select key from PowerUser.Powers order).");
        }
        var s2 = hud.Slots[2];
        Check(s2.Icon.Glyph == synergy.Icon && s2.Color == palette.Colors[(int)synergy.Primary] && s2.KeyText == HudBindings.KeyName(forge.SynergyKey), $"Synergy slot: {synergy.DisplayName}, icon {synergy.Icon}, colour palette {synergy.Primary}, key '{s2.KeyText}'.");
        Check(!hud.Slots.Any(s => s.Id == "strength" || s.Id == "flight"), "CONTROL: unequipped Flight/Strength have no slot.");
        Check(GameHud.Shown(hud.MeleeHint), "Strength not equipped -> small unslotted 'E / RMB MELEE' hint shown.");
        Check(hud.BuiltSignature.StartsWith("fire|ice|" + synergy.Id), "Bar signature: " + hud.BuiltSignature);
        yield return Composite(new Vector2Int(1920, 1080), "loadout-fire-ice-1920x1080");
        var fireSlot = SlotFor("fire"); var fireRuntime = fireSlot.Power;
        Check(fireSlot.CooldownFraction == 0f && fireSlot.LitPips == p.Stats(fireRuntime).Charges, $"CONTROL before use: Fire pips {fireSlot.LitPips}/{fireSlot.Pips.Count}, radial 0.");
        Check(p.Select(fireRuntime) && p.Use(fireRuntime), "Real Fire Blast used (PowerUser.Use): " + p.Message);
        yield return null;
        Check(fireSlot.LitPips == fireRuntime.Charges && fireSlot.LitPips == fireSlot.Pips.Count - 1 && fireSlot.CooldownFraction > 0f, $"Fire slot after use: pips {fireSlot.LitPips}/{fireSlot.Pips.Count}, radial {fireSlot.CooldownFraction:0.00}, LMB tag on the selected slot: {GameHud.Shown(fireSlot.SelectedTag)}.");
        yield return Composite(new Vector2Int(1920, 1080), "loadout-fire-ice-after-fire", false);
        yield return Fps();
    }

    IEnumerator Fps()
    {
        var cam = GameplayCamera(); var target = new RenderTexture(1920, 1080, 24) { name = "FPS A/B" }; target.Create();
        cam.targetTexture = target; cam.enabled = false; hud.Panel.targetTexture = target;
        var rows = new List<(string label, double fps, double mean, double p95, int repaints)>();
        var hudMs = new double[4];
        foreach (var legacyOnly in new[] { true, false, true, false })
        {
            hud.SetShown(!legacyOnly); if (legacy.DebugVisible != legacyOnly) legacy.ToggleDebug();
            var warm = System.Diagnostics.Stopwatch.StartNew(); while (warm.Elapsed.TotalSeconds < 1) { cam.Render(); yield return null; }
            int r0 = legacy.RepaintEvents; var watch = System.Diagnostics.Stopwatch.StartNew(); var frames = new List<double>(); double last = 0;
            int phase = rows.Count;
            while (watch.Elapsed.TotalSeconds < 4)
            {
                cam.Render(); yield return null; double now = watch.Elapsed.TotalSeconds; frames.Add((now - last) * 1000); last = now;
                if (!legacyOnly) hudMs[phase] += hud.LastUpdateMs;
            }
            hudMs[phase] /= frames.Count;
            frames.Sort(); string label = legacyOnly ? "A old IMGUI debug HUD (GameHud hidden)" : "B new GameHud (IMGUI debug hidden)";
            rows.Add((label, frames.Count / watch.Elapsed.TotalSeconds, frames.Average(), frames[(int)(frames.Count * .95)], legacy.RepaintEvents - r0));
            var row = rows[rows.Count - 1];
            Log($"MEASURED {row.label}: frames={frames.Count}, FPS={row.fps:F1}, mean={row.mean:F2}ms, p95={row.p95:F2}ms, IMGUI repaints={row.repaints} (gameplay camera single render 1920x1080 + HUD panel into the same target).");
        }
        Log($"MEASURED GameHud.LateUpdate CPU per frame (B phases): {hudMs[1]:0.000} ms / {hudMs[3]:0.000} ms.");
        hud.SetShown(true); if (legacy.DebugVisible) legacy.ToggleDebug();
        cam.targetTexture = null; cam.enabled = true; hud.Panel.targetTexture = null; target.Release(); Destroy(target);
        double a = (rows[0].fps + rows[2].fps) * .5, b = (rows[1].fps + rows[3].fps) * .5;
        Log($"MEASURED A/B summary: old IMGUI mean {a:F1} FPS, new GameHud mean {b:F1} FPS ({(b - a) / a:+0.0%;-0.0%}).");
        if (rows[0].repaints + rows[2].repaints == 0) Log("LIMIT: batch mode dispatched no IMGUI repaint events, so phase A measures the frame WITHOUT the old IMGUI panel's draw cost; the A/B therefore bounds the new HUD's full cost rather than a like-for-like swap.");
    }
}
#endif
