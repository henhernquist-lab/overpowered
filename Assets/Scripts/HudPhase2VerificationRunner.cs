#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// HUD Phase 2 verification (see HudPhase2Verification). Real GameFlow sessions, real encounters, real NPCs and the real
/// gameplay methods the input handlers call (TryJump / TryPunch / TryBackflip / CrimeEncounter.Interact / SpawnNext).
/// Captures are COMPOSITED: gameplay camera + main HUD panel + overlay panel into one render target, with a camera-only
/// render of the same frame kept for pixel checks.
public sealed class HudPhase2VerificationRunner : MonoBehaviour
{
    public string Folder, MainSave, PromptSave, FreshSave, LegacySave;
    public bool ReloadMode;
    public Action<int> Finished;
    readonly List<string> output = new List<string>();
    WorldSession W => WorldSession.Instance;
    GameFlow Flow => GameFlow.Instance;
    GameHud hud; ModeScreens ui; CityPalette palette;
    static readonly Vector2Int[] Resolutions = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) };
    GameModeDefinition hero, villain, freePlay, endless, endlessVillain;

    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder); QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        var stack = new Stack<IEnumerator>(); stack.Push(TransientVerdict()); stack.Push(ReloadMode ? ReloadChecks() : Checks());
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
    void Log(string text) { output.Add(text); Debug.Log("[HUD2] " + text); }
    void Check(bool valid, string text) { if (!valid) throw new Exception(text); Log("PASS " + text); }
    void Write() { File.WriteAllLines(Path.Combine(Folder, ReloadMode ? "reload.txt" : "results.txt"), output); }

    // ---- Transient feedback (XP popups, heat delta) may sit near the centre but must NEVER overlap the crosshair box.
    //      Checked every frame of the whole run (layout of the previous panel update), plus at every capture.
    int transientFrames, transientSamples; readonly List<string> crosshairHits = new List<string>();
    IEnumerable<VisualElement> Transients() { foreach (var p in hud.Popups) if (p.Active && GameHud.Shown(p.Element)) yield return p.Element; if (GameHud.Shown(hud.HeatDelta)) yield return hud.HeatDelta; }
    void Update()
    {
        if (hud == null || hud.OverlayRoot == null || !GameHud.Shown(hud.OverlayRoot) || !GameHud.Shown(hud.Crosshair)) return;
        var cross = hud.Crosshair.worldBound; if (cross.width <= 0f) return; transientFrames++;
        foreach (var e in Transients()) { transientSamples++; if (e.worldBound.Overlaps(cross) && crosshairHits.Count < 20) crosshairHits.Add($"frame {Time.frameCount} {e.name}'{(e as Label)?.text}' {e.worldBound} x crosshair {cross}"); }
    }
    IEnumerator TransientVerdict()
    {
        Check(crosshairHits.Count == 0, $"Transient feedback NEVER overlapped the crosshair box: {transientSamples} popup/heat-delta samples over {transientFrames} HUD frames (keep-out radius {GameHud.PopupCrosshairClearRadius:0.#})" + (crosshairHits.Count > 0 ? " — OVERLAPS: " + string.Join("; ", crosshairHits) : "."));
        yield break;
    }
    // ---------------------------------------------------------------- helpers
    IEnumerator Scene(string name)
    {
        float deadline = Time.realtimeSinceStartup + 40;
        while (Flow.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && (W == null || FindAnyObjectByType<GameHud>() == null)))
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Scene timeout " + name); yield return null; }
        yield return null;
        if (name == GameFlow.CityScene) hud = FindAnyObjectByType<GameHud>();
        if (name == GameFlow.HomeScene) ui = FindAnyObjectByType<ModeScreens>();
    }
    IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    IEnumerator Realtime(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    IEnumerator Until(Func<bool> condition, float seconds, string what)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition()) { if (Time.realtimeSinceStartup > until) throw new Exception("Timed out waiting for " + what); yield return null; }
    }
    IEnumerator Home() { if (SceneManager.GetActiveScene().name != GameFlow.HomeScene || W != null) { Flow.Home(); yield return Scene(GameFlow.HomeScene); } }
    IEnumerator Launch(GameModeDefinition mode)
    {
        yield return Home();
        Check(Flow.Select(mode), "GameFlow.Select launches " + mode.name);
        yield return Scene(GameFlow.CityScene);
        Check(W.Mode != null && W.Mode.Definition == mode && hud != null && hud.OverlayRoot != null, $"{mode.name}: session runs this definition; GameHud + overlay panel exist.");
        yield return Frames(3);
    }
    IEnumerator Grounded() { yield return Until(() => W.Hero.GetComponent<CharacterController>().isGrounded && !W.Hero.BackflipActive, 6, "hero grounded"); yield return Frames(2); }
    Camera Cam() { var follow = FindAnyObjectByType<ThirdPersonCamera>(); return follow != null ? follow.GetComponent<Camera>() : Camera.main; }
    void Move(Vector3 position) { var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false; W.Hero.transform.position = position; cc.enabled = true; W.Hero.ResetMotion(); Physics.SyncTransforms(); }
    Color Accent(PlayerSide side) => palette.Colors[(int)(side == PlayerSide.Hero ? CityColor.HeroAccent : CityColor.VillainAccent)];
    static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < .01f && Mathf.Abs(a.g - b.g) < .01f && Mathf.Abs(a.b - b.b) < .01f;
    static string R(Rect r) => $"({r.x:0.#},{r.y:0.#} {r.width:0.#}x{r.height:0.#})";
    static string V(Vector3 v) => $"({v.x:0.0},{v.y:0.0},{v.z:0.0})";
    CrimeEncounter FirstEncounter() => W.Crimes.Where(c => c != null && !c.Resolved && c.Encounter != null).Select(c => c.Encounter).FirstOrDefault();
    /// TEST HARNESS (logged where used): hold an encounter's actors still so camera-relative placements are deterministic.
    void Hold(CrimeEncounter e, float seconds) { foreach (var a in e.Robbers.Concat(e.Civilians)) if (a.Npc != null && !a.Npc.Dead) a.Npc.Freeze(seconds); }
    string SavedHints(string path) { var s = JsonUtility.FromJson<ProgressSave>(File.ReadAllText(path)); return s.SeenHints == null ? "<null>" : string.Join(",", s.SeenHints); }
    /// Independent screen bearing of a world point (degrees clockwise from screen-up): the signed angle, about the view
    /// axis, from the camera's up vector to the point's offset projected onto the image plane.
    static float Bearing(Camera cam, Vector3 world)
    {
        Vector3 offset = Vector3.ProjectOnPlane(world - cam.transform.position, cam.transform.forward);
        return -Vector3.SignedAngle(cam.transform.up, offset, cam.transform.forward);
    }
    static float HeadingIndependent(Camera cam, Vector3 from, Vector3 to)
    { Vector3 f = cam.transform.forward; f.y = 0; Vector3 d = to - from; d.y = 0; return Vector3.SignedAngle(f, d, Vector3.up); }

    // ---------------------------------------------------------------- layout assertions (panel logical units)
    static IEnumerable<VisualElement> Descendants(VisualElement root) { yield return root; foreach (var child in root.Children()) foreach (var d in Descendants(child)) yield return d; }
    IEnumerable<VisualElement> Overlay() { yield return hud.AlertCard; yield return hud.PromptCard; yield return hud.Waypoint; yield return hud.WaypointEdge; yield return hud.Briefing; }
    bool IsWaypoint(VisualElement g) => g == hud.Waypoint || g == hud.WaypointEdge;
    void AssertLayout(Vector2Int size, string label)
    {
        float scale = Mathf.Min(size.x / (float)GameHud.ReferenceResolution.x, size.y / (float)GameHud.ReferenceResolution.y);
        var panel = hud.Root.layout; var overlay = hud.OverlayRoot.layout; Vector2 expected = new Vector2(size.x / scale, size.y / scale);
        Check(Mathf.Abs(panel.width - expected.x) < 1.5f && Mathf.Abs(panel.height - expected.y) < 1.5f && Mathf.Abs(overlay.width - panel.width) < .5f && Mathf.Abs(overlay.height - panel.height) < .5f,
            $"{label}: both panels {panel.width:0.#}x{panel.height:0.#} logical (overlay {overlay.width:0.#}x{overlay.height:0.#}; expected {expected.x:0.#}x{expected.y:0.#}).");
        float m = GameHud.SafeMargin - .5f; var safe = Rect.MinMaxRect(m, m, panel.width - m, panel.height - m);
        var centre = new Rect(panel.width * (1 - GameHud.CentreZone) * .5f, panel.height * (1 - GameHud.CentreZone) * .5f, panel.width * GameHud.CentreZone, panel.height * GameHud.CentreZone);
        var cross = hud.Crosshair.worldBound;
        var groups = hud.Groups.Concat(Overlay()).Where(GameHud.Shown).ToList();
        var clipped = new List<string>(); var inCentre = new List<string>(); var truncated = new List<string>(); int count = 0;
        foreach (var group in groups)
            foreach (var e in Descendants(group))
            {
                if (!GameHud.Shown(e)) continue; var r = e.worldBound; if (r.width <= .01f || r.height <= .01f) continue; count++;
                string id = group.name + "/" + (string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name) + (e is Label l0 ? "'" + l0.text + "'" : "");
                if (r.xMin < safe.xMin || r.yMin < safe.yMin || r.xMax > safe.xMax || r.yMax > safe.yMax) clipped.Add(id + R(r));
                bool centreAllowed = group == hud.Briefing || IsWaypoint(group);
                if (!centreAllowed && r.Overlaps(centre)) inCentre.Add(id + R(r));
                if (e is Label text && !string.IsNullOrEmpty(text.text))
                {
                    var content = text.contentRect;
                    if (text.resolvedStyle.whiteSpace == WhiteSpace.NoWrap)
                    { var need = text.MeasureTextSize(text.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined); if (need.x > content.width + 1f) truncated.Add($"{id} needs {need.x:0.#} has {content.width:0.#}"); }
                    else { var need = text.MeasureTextSize(text.text, content.width + 1f, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined); if (need.y > content.height + 1f) truncated.Add($"{id} needs h{need.y:0.#} has h{content.height:0.#}"); }
                }
            }
        Check(clipped.Count == 0, $"{label}: all {count} visible HUD + overlay elements inside the {GameHud.SafeMargin}px safe margin" + (clipped.Count > 0 ? " — CLIPPED: " + string.Join("; ", clipped) : "."));
        Check(inCentre.Count == 0, $"{label}: centre 40%x40% {R(centre)} holds only the crosshair, the waypoint and (while shown) the briefing" + (inCentre.Count > 0 ? " — IN CENTRE: " + string.Join("; ", inCentre) : "."));
        Check(truncated.Count == 0, $"{label}: no visible label truncated" + (truncated.Count > 0 ? " — " + string.Join("; ", truncated) : "."));
        var overlaps = new List<string>(); var solid = groups.Where(g => g != hud.Briefing).ToList();
        for (int i = 0; i < solid.Count; i++) for (int j = i + 1; j < solid.Count; j++) if (solid[i].worldBound.Overlaps(solid[j].worldBound)) overlaps.Add(solid[i].name + " x " + solid[j].name);
        Check(overlaps.Count == 0, $"{label}: no two HUD groups/cards overlap: " + string.Join(" ", solid.Select(g => g.name + R(g.worldBound))) + (overlaps.Count > 0 ? " — OVERLAP: " + string.Join(", ", overlaps) : ""));
        foreach (var w in groups.Where(IsWaypoint))
            Check(!w.worldBound.Overlaps(cross), $"{label}: waypoint {w.name}{R(w.worldBound)} does not cover the crosshair box {R(cross)}.");
        if (GameHud.Shown(hud.Crosshair))
        {
            var c = cross.center * scale; var screen = new Vector2(size.x * .5f, size.y * .5f);
            Check(Vector2.Distance(c, screen) <= 1f, $"{label}: crosshair centre {c.x:0.##},{c.y:0.##} px vs screen centre (|d| {Vector2.Distance(c, screen):0.###} px).");
        }
    }

    // ---------------------------------------------------------------- composited capture + pixel checks
    IEnumerator Composite(Vector2Int size, string name, bool layout = true)
    {
        var cam = Cam(); Check(cam != null, "Gameplay camera found for " + name);
        Log($"STATE {name}: hero {V(W.Hero.transform.position)}, cam {V(cam.transform.position)} fwd {V(cam.transform.forward)}, waypoint {(hud.WaypointVisible ? (hud.WaypointOnScreen ? "marker" : "edge " + hud.WaypointArrowAngle.ToString("0.0") + "°") : "hidden")} target {V(hud.WaypointTarget)} at {hud.WaypointPoint}{(hud.WaypointShifted ? " (shifted off a group)" : "")}.");
        var composite = new RenderTexture(size.x, size.y, 24) { name = "HUD2 composite " + name }; composite.Create();
        var plain = new RenderTexture(size.x, size.y, 24) { name = "HUD2 camera only " + name }; plain.Create();
        hud.Panel.targetTexture = composite; hud.OverlayPanel.targetTexture = composite;
        // The HUD projects with the gameplay camera: give it the capture's aspect (batch-mode screen is 4:3) so the
        // waypoint is placed exactly where the capture renders its target.
        cam.aspect = size.x / (float)size.y;
        yield return Frames(4);
        if (layout) AssertLayout(size, $"{name} {size.x}x{size.y}");
        if (hud.WaypointVisible && hud.WaypointOnScreen && !hud.WaypointPushed && !hud.WaypointShifted)
        {
            var vp = cam.WorldToViewportPoint(hud.WaypointTarget + Vector3.up * GameHud.WaypointHeight); var o = hud.OverlayRoot.layout;
            var at = new Vector2(vp.x * o.width, (1 - vp.y) * o.height);
            Check(Vector2.Distance(at, hud.WaypointPoint) < 1.5f && Mathf.Abs(hud.Waypoint.worldBound.center.x - at.x) < 2f, $"{name}: marker at the target's projection {at} in this capture (HUD {hud.WaypointPoint}).");
        }
        bool wasEnabled = cam.enabled; cam.enabled = false;
        cam.targetTexture = plain; cam.Render(); cam.targetTexture = composite; cam.Render(); cam.targetTexture = null;
        hud.Root.MarkDirtyRepaint(); hud.OverlayRoot.MarkDirtyRepaint();
        yield return null;
        var a = Read(composite); var b = Read(plain); cam.enabled = wasEnabled;
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), a.EncodeToPNG());
        PixelChecks(a, b, size, name);
        Destroy(a); Destroy(b);
        cam.ResetAspect(); hud.Panel.targetTexture = null; hud.OverlayPanel.targetTexture = null; composite.Release(); plain.Release(); Destroy(composite); Destroy(plain);
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
        foreach (var group in hud.Groups.Concat(Overlay()).Where(GameHud.Shown))
        {
            float changed = Changed(Pixels(group.worldBound)); parts.Add($"{group.name} {changed:P0}");
            float need = IsWaypoint(group) ? .04f : .15f;
            Check(changed > need, $"{name}: composite contains {group.name} (pixels changed vs camera-only: {changed:P1}).");
        }
        var exempt = new List<RectInt>();
        void Exempt(VisualElement e, int pad) { var r = Pixels(e.worldBound); exempt.Add(new RectInt(r.xMin - pad, r.yMin - pad, r.width + 2 * pad, r.height + 2 * pad)); }
        if (GameHud.Shown(hud.Crosshair)) Exempt(hud.Crosshair, 2);
        // Transient feedback (popups at the event, the heat delta) is exempt by its element bounds, like the waypoint;
        // it must never touch the crosshair box.
        var transients = Transients().ToList();
        foreach (var t in transients) Exempt(t, 6);
        Check(transients.All(t => !t.worldBound.Overlaps(hud.Crosshair.worldBound)), $"{name}: {transients.Count} transient feedback element(s) shown, none overlapping the crosshair box {R(hud.Crosshair.worldBound)}" + (transients.Count > 0 ? ": " + string.Join(" ", transients.Select(t => t.name + R(t.worldBound))) : "."));
        foreach (var w in Overlay().Where(e => (IsWaypoint(e) || e == hud.Briefing) && GameHud.Shown(e))) Exempt(w, 4);
        var zone = new RectInt(Mathf.RoundToInt(size.x * .3f), Mathf.RoundToInt(size.y * .3f), Mathf.RoundToInt(size.x * .4f), Mathf.RoundToInt(size.y * .4f));
        int offenders = 0, maxDiff = 0, total = 0;
        for (int y = zone.yMin; y < zone.yMax; y++) for (int x = zone.xMin; x < zone.xMax; x++)
        {
            var p = new Vector2Int(x, y); if (exempt.Any(r => r.Contains(p))) continue;
            total++; int d = Diff(x, y); maxDiff = Mathf.Max(maxDiff, d); if (d > 3) offenders++;
        }
        Check(offenders == 0, $"{name}: PIXEL centre-clear — {total} centre-zone pixels outside the crosshair/waypoint{(GameHud.Shown(hud.Briefing) ? "/briefing" : "")}{(transients.Count > 0 ? "/transient-feedback" : "")} boxes identical to the camera-only render (max diff {maxDiff}, {offenders} > 3).");
        Log($"CAPTURE {name}.png {size.x}x{size.y} (gameplay camera + HUD + overlay, one target); coverage: {string.Join(", ", parts)}.");
    }

    // ---------------------------------------------------------------- the run
    void LoadModes()
    {
        palette = Resources.Load<CityPalette>("CityPalette");
        var catalog = Resources.LoadAll<GameModeDefinition>("Modes"); GameModeDefinition Mode(string id) => catalog.First(m => m.Id == id);
        hero = Mode("hero"); villain = Mode("villain"); freePlay = Mode("free-play"); endless = Mode("endless-fight"); endlessVillain = Mode("endless-fight-villain");
    }
    IEnumerator Checks()
    {
        LoadModes();
        yield return Scene(GameFlow.HomeScene);
        Check(ui != null && ui.Profile.Data.Level == 1 && ui.Profile.Data.SessionsPlayed == 0 && ui.Profile.Data.SeenHints != null && ui.Profile.Data.SeenHints.Count == 0, "Fresh isolated main save CONTROL: level 1, no sessions, no seen prompts.");
        yield return HeroBriefingObjectiveWaypoint();
        yield return VillainBriefingObjective();
        yield return FreePlayAndEndless();
        yield return EmptyBriefingAndLabelControl();
        yield return Alerts();
        yield return Prompts();
        yield return Home();
        Log("LIMIT: no hardware keyboard/mouse in batch mode. Briefing dismissal and prompt completion use the methods the input handlers call (TryJump/TryPunch/TryBackflip, CrimeEncounter.Interact, GameModeSession.SpawnNext); the HUD's Input.anyKeyDown path is not exercised. Captures are batch-mode Metal render targets, not a physical display.");
    }

    // ---- Hero: briefing (3 res), input dismissal, objective line from data, waypoint on/off screen, retarget on a real stop
    IEnumerator HeroBriefingObjectiveWaypoint()
    {
        yield return Launch(hero);
        var e = FirstEncounter(); Check(e != null, "Hero: the initial encounter exists: " + e.Definition.DisplayName);
        Hold(e, 300f); e.enabled = false; Log("TEST HARNESS: hero encounter actors held with CityNpc.Freeze and the encounter clock paused (enabled=false) so placements are deterministic; the HUD reads them live.");
        Check(hud.AlertsStarted == 0, "Hero: the initial encounter (spawned before the HUD binds, covered by the briefing) raises no alert.");
        yield return BriefingShown(hero, "Hero");
        foreach (var r in Resolutions) yield return Composite(r, $"briefing-hero-{r.x}x{r.y}");
        Check(!hud.WaypointVisible && !GameHud.Shown(hud.PromptCard), "Hero: while the briefing is up, no waypoint and no prompt are drawn.");
        yield return Grounded();
        Check(W.Hero.TryJump(), "Real jump (SuperHeroController.TryJump, the Jump-button handler).");
        yield return null;
        Check(!hud.BriefingActive && hud.BriefingDismissedBy == "input: jump", $"Briefing dismissed by the gameplay input ({hud.BriefingDismissedBy}) after {hud.BriefingShows} show(s).");
        yield return Realtime(GameHud.BriefingFadeSeconds + .1f);
        Check(!GameHud.Shown(hud.Briefing), "Briefing card hidden after its fade.");
        yield return Grounded(); yield return Realtime(.4f);

        // Objective line = rules-asset label + live progress
        var rules = hero.Rules; var robbersLabel = rules.Tasks.First(t => t.Task == ObjectiveTask.Robbers).Label;
        Check(hud.ObjectiveEncounter == e && hud.CurrentStep.Task == ObjectiveTask.Robbers, $"Objective follows the nearest encounter ({e.Definition.DisplayName}); first incomplete task = {hud.CurrentStep.Task}.");
        string expected = $"{robbersLabel} {e.StoppedRobbers}/{e.Robbers.Count}";
        Check(hud.ObjectiveBody.text == expected, $"Objective line == rules-asset label + progress: \"{hud.ObjectiveBody.text}\" (Hero.asset Tasks: {string.Join(" | ", rules.Tasks.Select(t => t.Task + "=" + t.Label))}).");
        int more = rules.Tasks.Count(t => t.Task != ObjectiveTask.Robbers && ModeRules.Progress(e, t.Task, out int d, out int tot) && tot > 0 && d < tot);
        Check(hud.ObjectiveMeta.text.StartsWith($"+{more} MORE TASK"), $"'+N more' hint: \"{hud.ObjectiveMeta.text}\" (N={more} from data: civilians {e.Civilians.Count}, hazards {e.Hazards.Count}).");
        Check(hud.ObjectiveHeader.text.StartsWith(e.Definition.DisplayName.ToUpperInvariant()), $"Objective header carries EncounterDefinition.DisplayName: \"{hud.ObjectiveHeader.text}\".");
        Check(e.Objective == $"{robbersLabel} 0/{e.Robbers.Count} · {rules.Tasks.First(t => t.Task == ObjectiveTask.Civilians).Label} 0/{e.Civilians.Count}", $"ModeRules.Objective() (F3 panel / suites) is built from the same data: \"{e.Objective}\".");

        // Waypoint ON-SCREEN (target ahead-right, not pushed)
        var cam = Cam();
        EncounterActor NearestRobber(Vector3 from) => e.Robbers.Where(a => !a.Captured && !a.Escaped && a.Npc != null && !a.Npc.Dead).OrderBy(a => (a.Npc.transform.position - from).sqrMagnitude).FirstOrDefault();
        var first = NearestRobber(W.Hero.transform.position); var at = first.Npc.transform.position;
        Move(at - Vector3.forward * 8f - Vector3.right * 3f); yield return Frames(4); yield return null;
        var near = NearestRobber(W.Hero.transform.position);
        Check(hud.WaypointVisible && hud.WaypointOnScreen && GameHud.Shown(hud.Waypoint) && !GameHud.Shown(hud.WaypointEdge), $"Waypoint ON-SCREEN marker shown (target ahead-right).");
        Check(Vector3.Distance(hud.WaypointTarget, near.Npc.transform.position) < .01f, $"Waypoint targets the NEAREST unresolved robber {V(near.Npc.transform.position)} (HUD {V(hud.WaypointTarget)}).");
        Vector3 vp = cam.WorldToViewportPoint(hud.WaypointTarget + Vector3.up * GameHud.WaypointHeight); var panel = hud.OverlayRoot.layout;
        Log($"Overlay panel outside captures: {panel.width:0.#}x{panel.height:0.#} logical (batch-mode screen {Screen.width}x{Screen.height}).");
        var projected = new Vector2(vp.x * panel.width, (1 - vp.y) * panel.height);
        Check(!hud.WaypointPushed && !hud.WaypointShifted && Vector2.Distance(hud.WaypointPoint, projected) < 1f && Mathf.Abs(hud.Waypoint.worldBound.center.x - projected.x) < 2f,
            $"Marker drawn at the target's screen position {projected} (HUD point {hud.WaypointPoint}, marker box {R(hud.Waypoint.worldBound)}).");
        float metres = Vector3.Distance(W.Hero.transform.position, hud.WaypointTarget);
        Check(hud.WaypointDistanceLabel.text == $"{metres:F0} M" && hud.WaypointCountLabel.text == $"{e.Robbers.Count - e.StoppedRobbers} LEFT",
            $"Marker text: distance \"{hud.WaypointDistanceLabel.text}\" ({metres:0.0} m), count \"{hud.WaypointCountLabel.text}\".");
        Check(!hud.Waypoint.worldBound.Overlaps(hud.Crosshair.worldBound), "Marker does not cover the crosshair.");
        foreach (var r in Resolutions) yield return Composite(r, $"objective-waypoint-onscreen-{r.x}x{r.y}");

        // Crosshair clearance: target dead ahead projects near the centre -> pushed out of the radius
        // The camera looks down at the hero, so a target projects onto the screen centre only when the hero is above it.
        // TEST HARNESS: the hero controller is paused (no gravity) while the hero is placed 5 m up, on a line of spots.
        near = NearestRobber(W.Hero.transform.position); var robberAt = near.Npc.transform.position; float fromCentre = float.MaxValue;
        W.Hero.enabled = false;
        for (float dz = 2f; dz <= 16f && fromCentre >= GameHud.CrosshairClearRadius * .6f; dz += .5f)
        {
            Move(robberAt - Vector3.forward * dz + Vector3.right * .4f + Vector3.up * 5f); yield return Frames(3);
            vp = cam.WorldToViewportPoint(hud.WaypointTarget + Vector3.up * GameHud.WaypointHeight); projected = new Vector2(vp.x * panel.width, (1 - vp.y) * panel.height);
            fromCentre = vp.z > 0 ? Vector2.Distance(projected, panel.center) : float.MaxValue;
        }
        Log($"Crosshair-clearance probe: target projects {fromCentre:0.#} logical px from centre (radius {GameHud.CrosshairClearRadius}).");
        if (fromCentre < GameHud.CrosshairClearRadius)
            Check(hud.WaypointPushed && Mathf.Abs(Vector2.Distance(hud.WaypointPoint, panel.center) - GameHud.CrosshairClearRadius) < .5f && !hud.Waypoint.worldBound.Overlaps(hud.Crosshair.worldBound),
                $"Target inside the crosshair radius: marker pushed to {Vector2.Distance(hud.WaypointPoint, panel.center):0.#} px, box {R(hud.Waypoint.worldBound)} clear of crosshair {R(hud.Crosshair.worldBound)}.");
        else Log("NOTE: the dead-ahead probe did not land inside the radius; the push is not exercised by this placement.");
        yield return Composite(new Vector2Int(1920, 1080), "waypoint-crosshair-clearance-1920x1080");
        W.Hero.enabled = true; Move(robberAt - Vector3.forward * 12f); yield return Grounded();

        // Waypoint OFF-SCREEN: target behind the camera -> edge arrow; angle vs an independent bearing
        near = NearestRobber(W.Hero.transform.position);
        Move(near.Npc.transform.position + Vector3.forward * 14f + Vector3.right * 3f); yield return Frames(4); yield return null;
        var local = cam.transform.InverseTransformPoint(hud.WaypointTarget + Vector3.up * GameHud.WaypointHeight);
        float bearing = Bearing(cam, hud.WaypointTarget + Vector3.up * GameHud.WaypointHeight);
        Check(hud.WaypointVisible && !hud.WaypointOnScreen && GameHud.Shown(hud.WaypointEdge) && !GameHud.Shown(hud.Waypoint) && local.z < 0, $"Target BEHIND the camera (camera-local z {local.z:0.0}): edge arrow shown, marker hidden.");
        Check(Mathf.Abs(Mathf.DeltaAngle(hud.WaypointArrowAngle, bearing)) < 2f && Mathf.Abs(Mathf.DeltaAngle(hud.WaypointArrow.resolvedStyle.rotate.angle.ToDegrees(), hud.WaypointArrowAngle)) < .5f,
            $"Edge arrow angle {hud.WaypointArrowAngle:0.0}° (drawn {hud.WaypointArrow.resolvedStyle.rotate.angle.ToDegrees():0.0}°) matches the independent camera-local bearing {bearing:0.0}° (|d| {Mathf.Abs(Mathf.DeltaAngle(hud.WaypointArrowAngle, bearing)):0.00}° < 2°).");
        Check(Mathf.Abs(Mathf.DeltaAngle(bearing, 180f)) < 60f && hud.WaypointPoint.y > hud.OverlayRoot.layout.height * .72f, $"Behind -> arrow points down ({bearing:0.0}°) and sits in the bottom band (y {hud.WaypointPoint.y:0.#} of {hud.OverlayRoot.layout.height:0.#}{(hud.WaypointShifted ? ", slid clear of an edge group" : "")}).");
        Check(!hud.WaypointEdge.worldBound.Overlaps(hud.Crosshair.worldBound), "Edge arrow away from the crosshair.");
        foreach (var r in Resolutions) yield return Composite(r, $"objective-waypoint-offscreen-{r.x}x{r.y}");
        // ... and to the right (off-screen sideways)
        near = NearestRobber(W.Hero.transform.position);
        Move(near.Npc.transform.position - Vector3.right * 18f); yield return Frames(4); yield return null;
        local = cam.transform.InverseTransformPoint(hud.WaypointTarget + Vector3.up * GameHud.WaypointHeight); bearing = Bearing(cam, hud.WaypointTarget + Vector3.up * GameHud.WaypointHeight);
        if (!hud.WaypointOnScreen)
        {
            Check(Mathf.Abs(Mathf.DeltaAngle(hud.WaypointArrowAngle, bearing)) < 2f && bearing > 30f && bearing < 150f && hud.WaypointPoint.x > hud.OverlayRoot.layout.width * .8f,
                $"Target to the RIGHT (local {V(local)}): arrow {hud.WaypointArrowAngle:0.0}° vs bearing {bearing:0.0}°, on the right edge (x {hud.WaypointPoint.x:0.#} of {hud.OverlayRoot.layout.width:0.#}).");
            yield return Composite(new Vector2Int(1920, 1080), "objective-waypoint-offscreen-right-1920x1080");
        }
        else Log($"NOTE: sideways probe landed on-screen (local {V(local)}); right-edge case not exercised.");

        // Real robber stop -> progress advances, waypoint retargets, count drops
        yield return Frames(2); yield return null;
        var targeted = e.Robbers.First(a => a.Npc != null && Vector3.Distance(a.Npc.transform.position, hud.WaypointTarget) < .01f);
        int before = hud.WaypointRemaining;
        Move(targeted.Npc.transform.position + Vector3.right * 1.2f); yield return Frames(3);
        yield return null;
        Check(Vector3.Distance(hud.WaypointTarget, targeted.Npc.transform.position) < .01f, "Waypoint is on the robber about to be stopped.");
        Check(e.Interact(e.Definition.HoldSeconds + .01f) && targeted.Captured && e.StoppedRobbers == 1, $"Real stop: CrimeEncounter.Interact (the R-hold path) captured the targeted robber; stopped {e.StoppedRobbers}/{e.Robbers.Count}.");
        yield return null; yield return null;
        var next = NearestRobber(W.Hero.transform.position);
        Check(next != targeted && Vector3.Distance(hud.WaypointTarget, next.Npc.transform.position) < .01f && hud.WaypointRemaining == before - 1,
            $"Waypoint RETARGETED in one frame to the next-nearest unresolved robber {V(next.Npc.transform.position)}; count {before} -> {hud.WaypointRemaining}.");
        yield return Realtime(.35f);
        Check(hud.ObjectiveBody.text == $"{robbersLabel} 1/{e.Robbers.Count}", $"Progress advanced on the objective line: \"{hud.ObjectiveBody.text}\".");
    }

    IEnumerator BriefingShown(GameModeDefinition d, string label)
    {
        yield return Frames(2);
        var side = W.Progression.Data.Side; var accent = Accent(side);
        Check(hud.BriefingActive && GameHud.Shown(hud.Briefing), $"{label}: mission briefing card shown at session start.");
        Check(hud.BriefingGoalLabel.text == d.BriefingGoal && hud.BriefingWinLabel.text == (d.BriefingWin ?? "") && hud.BriefingLoseLabel.text == (d.BriefingLose ?? ""),
            $"{label}: briefing text == definition data: GOAL \"{hud.BriefingGoalLabel.text}\" / WIN \"{hud.BriefingWinLabel.text}\" / LOSE \"{hud.BriefingLoseLabel.text}\".");
        Check(GameHud.Shown(hud.BriefingWinRow) == !string.IsNullOrWhiteSpace(d.BriefingWin) && GameHud.Shown(hud.BriefingLoseRow) == !string.IsNullOrWhiteSpace(d.BriefingLose), $"{label}: empty WIN/LOSE rows hidden, filled rows shown.");
        Check(Same(hud.BriefingAccent, accent) && Same(hud.BriefingEyebrow.resolvedStyle.color, accent) && Same(hud.Briefing.resolvedStyle.borderTopColor, new Color(accent.r, accent.g, accent.b)),
            $"{label}: card in the {side} side colour ({(side == PlayerSide.Hero ? "HeroAccent" : "VillainAccent")} #{ColorUtility.ToHtmlStringRGB(accent)}).");
    }

    // ---- Villain: briefing timeout CONTROL, loot -> wreck -> escape objective + waypoints
    IEnumerator VillainBriefingObjective()
    {
        yield return Launch(villain);
        var e = FirstEncounter(); e.enabled = false; Log("TEST HARNESS: villain encounter clock paused (enabled=false).");
        yield return BriefingShown(villain, "Villain");
        yield return Composite(new Vector2Int(1920, 1080), "briefing-villain-1920x1080");
        float opened = Time.realtimeSinceStartup; yield return Realtime(villain.BriefingSeconds - 1.5f);
        Check(hud.BriefingActive && GameHud.Shown(hud.Briefing), $"CONTROL: with no input the briefing is still up after {Time.realtimeSinceStartup - opened:0.0}s (< BriefingSeconds {villain.BriefingSeconds}).");
        yield return Until(() => !hud.BriefingActive, 4f, "briefing timeout");
        Check(hud.BriefingDismissedBy == "timeout", $"CONTROL: no input -> dismissed by TIMEOUT after {Time.realtimeSinceStartup - opened:0.0}s (unscaled, BriefingSeconds {villain.BriefingSeconds}).");
        yield return Realtime(.5f);

        var rules = villain.Rules; string L(ObjectiveTask t) => rules.Tasks.First(x => x.Task == t).Label;
        Check(hud.ObjectiveBody.text == $"{L(ObjectiveTask.Loot)} 0/{e.Loot.Count}", $"Villain objective line from Villain.asset: \"{hud.ObjectiveBody.text}\" · meta \"{hud.ObjectiveMeta.text}\".");
        yield return null;
        var nearestLoot = e.Loot.Where(n => !n.Done).OrderBy(n => (n.Visual.transform.position - W.Hero.transform.position).sqrMagnitude).First();
        Check(Vector3.Distance(hud.WaypointTarget, nearestLoot.Visual.transform.position) < .01f && hud.WaypointRemaining == e.Loot.Count, $"Villain waypoint on the nearest loot node; {hud.WaypointRemaining} left.");
        foreach (var node in e.Loot) { Move(node.Visual.transform.position); e.Interact(0); Check(e.Interact(e.Definition.HoldSeconds + .01f) && node.Done, "Real loot grab (R-hold path)."); }
        yield return Realtime(.35f);
        Check(hud.ObjectiveBody.text == $"{L(ObjectiveTask.Wreck)} 0/{e.Definition.DestructionGoal}" && hud.CurrentStep.Task == ObjectiveTask.Wreck, $"Loot done -> next task from data: \"{hud.ObjectiveBody.text}\".");
        yield return null;
        Check(e.Props.Any(p => p != null && Vector3.Distance(p.position, hud.WaypointTarget) < .01f), "Waypoint moved to an intact encounter prop.");
        foreach (var prop in e.Props.ToArray()) if (prop != null) prop.GetComponent<BreakableProp>()?.TakeDamage(10000, W.Powers);
        yield return Frames(2); Move(e.Site + Vector3.right * 2f); yield return Realtime(.35f); yield return null;
        Check(hud.CurrentStep.Task == ObjectiveTask.Escape && hud.ObjectiveBody.text.StartsWith(L(ObjectiveTask.Escape)), $"Props wrecked ({e.DestroyedProps}) -> \"{hud.ObjectiveBody.text}\".");
        Vector3 flat = hud.WaypointTarget - e.Site; flat.y = 0;
        Check(Mathf.Abs(flat.magnitude - e.Definition.PlayerEscapeDistance) < .05f && Vector3.Angle(flat, Vector3.right) < 1f, $"Escape waypoint on the PlayerEscapeDistance circle ({flat.magnitude:0.00} m of {e.Definition.PlayerEscapeDistance}) straight out from the site past the player.");
        yield return Composite(new Vector2Int(1920, 1080), "villain-escape-objective-1920x1080");
    }

    // ---- Free Play + Endless: briefings; no objective line, no waypoint
    IEnumerator FreePlayAndEndless()
    {
        yield return Launch(freePlay);
        yield return BriefingShown(freePlay, "Free Play");
        Check(hud.BriefingGoalLabel.text == "No rules. Go wild." && !GameHud.Shown(hud.BriefingWinRow) && !GameHud.Shown(hud.BriefingLoseRow), "Free Play briefing: \"No rules. Go wild.\" only.");
        yield return Composite(new Vector2Int(1920, 1080), "briefing-freeplay-1920x1080");
        yield return Grounded(); Check(W.Hero.TryPunch(), "Real punch (TryPunch, the E handler)."); yield return null;
        Check(!hud.BriefingActive && hud.BriefingDismissedBy == "input: melee", "Free Play briefing dismissed by the punch input.");
        yield return Realtime(1f);
        Check(!GameHud.Shown(hud.ObjectiveGroup) && !hud.WaypointVisible && !GameHud.Shown(hud.AlertCard), "Free Play: no objective line, no waypoint, no alert.");

        yield return Launch(endless);
        yield return BriefingShown(endless, "Endless");
        yield return Composite(new Vector2Int(1920, 1080), "briefing-endless-1920x1080");
        yield return Grounded(); Check(W.Hero.TryBackflip(), "Real backflip (TryBackflip, the Q handler)."); yield return null;
        Check(!hud.BriefingActive && hud.BriefingDismissedBy == "input: backflip", "Endless briefing dismissed by the backflip input.");
        yield return Realtime(.6f);
        Check(GameHud.Shown(hud.DirectorGroup) && !GameHud.Shown(hud.ObjectiveGroup) && !hud.WaypointVisible, "Endless keeps its wave panel; no objective line or waypoint.");

        yield return Launch(endlessVillain);
        yield return BriefingShown(endlessVillain, "Endless (Villain)");
        yield return Home();
    }

    // ---- CONTROLS: empty briefing fields -> no card; objective labels are DATA (cloned rules asset)
    IEnumerator EmptyBriefingAndLabelControl()
    {
        var rules = Instantiate(hero.Rules); rules.name = "Hero rules label-control clone";
        var clone = Instantiate(hero); clone.name = "hero empty-briefing + label-control clone"; clone.Rules = rules;
        clone.BriefingGoal = clone.BriefingWin = clone.BriefingLose = "";
        var robbers = rules.Tasks.First(t => t.Task == ObjectiveTask.Robbers); robbers.Label = "CATCH THE CROOKS";
        yield return Launch(clone);
        yield return Frames(5);
        Check(!hud.BriefingActive && !GameHud.Shown(hud.Briefing) && hud.BriefingShows == 0, "CONTROL: a cloned definition with empty briefing fields shows NO card.");
        var e = FirstEncounter(); Hold(e, 60f); e.enabled = false;
        yield return Realtime(.4f);
        Check(hud.ObjectiveBody.text == $"CATCH THE CROOKS 0/{e.Robbers.Count}" && e.Objective.StartsWith("CATCH THE CROOKS"), $"CONTROL: label edited on a CLONED rules asset -> objective line \"{hud.ObjectiveBody.text}\"; Objective() \"{e.Objective}\".");
        robbers.Label = "NAB THE THIEVES"; yield return Realtime(.4f);
        Check(hud.ObjectiveBody.text == $"NAB THE THIEVES 0/{e.Robbers.Count}", $"CONTROL: live edit of the cloned label -> \"{hud.ObjectiveBody.text}\".");
        yield return Composite(new Vector2Int(1920, 1080), "control-cloned-label-no-briefing-1920x1080");
        Check(hero.Rules.Tasks.First(t => t.Task == ObjectiveTask.Robbers).Label == "STOP THE ROBBERS" && !string.IsNullOrEmpty(hero.BriefingGoal), "Shipping Hero.asset label and hero briefing untouched.");
        yield return Home(); Destroy(clone); Destroy(rules);
    }

    // ---- Alerts: CONTROL 30 s idle with no spawn; then a real spawn -> alert, name, bearing; objective updates after
    IEnumerator Alerts()
    {
        var clone = Instantiate(hero); clone.name = "hero alert clone (no timed spawns)"; clone.SpawnInterval = 100000f;
        yield return Launch(clone);
        int spawned = 0; W.EncounterSpawned += _ => spawned++;
        var e0 = FirstEncounter(); Hold(e0, 400f); e0.enabled = false;
        Log("TEST HARNESS: cloned hero definition with SpawnInterval=100000 (no timed spawns); initial encounter held/paused.");
        yield return Grounded(); W.Hero.TryJump(); yield return Grounded();
        // Stand at the intersection farthest from the first encounter so the next SpawnNext() lands nearest the player.
        var city = W.Tuning.City; float pitch = city.BlockSize + city.StreetWidth; var sites = new List<Vector3>();
        for (int x = 0; x < city.Blocks - 1; x++) for (int z = 0; z < city.Blocks - 1; z++) sites.Add(new Vector3((x - (city.Blocks - 2) * .5f) * pitch, 0, (z - (city.Blocks - 2) * .5f) * pitch));
        var far = sites.OrderByDescending(s => (s - e0.Site).sqrMagnitude).First();
        Move(W.City.NearestSidewalk(far + new Vector3(4f, 0, -6f))); yield return Grounded();
        int shownFrames = 0, frames = 0; float until = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < until) { yield return null; frames++; if (GameHud.Shown(hud.AlertCard) || hud.AlertShowing) shownFrames++; }
        Check(spawned == 0 && hud.AlertsStarted == 0 && shownFrames == 0, $"CONTROL: 30 s of idle with no spawn ({frames} frames): EncounterSpawned {spawned}, alerts {hud.AlertsStarted}, alert card shown on {shownFrames} frames.");

        var crime = W.Mode.SpawnNext(); Check(crime != null && crime.Encounter != null, "Real spawn through GameModeSession.SpawnNext (the path the spawn timer uses): " + crime?.Encounter?.Definition.DisplayName);
        var e1 = crime.Encounter; Hold(e1, 400f); e1.enabled = false;
        yield return null; yield return null;
        var cam = Cam();
        Check(spawned == 1 && hud.AlertShowing && hud.AlertEncounter == e1 && GameHud.Shown(hud.AlertCard), "Alert card slides in for the new encounter (EncounterSpawned raised once).");
        Check(hud.AlertNameLabel.text == e1.Definition.DisplayName.ToUpperInvariant(), $"Alert name \"{hud.AlertNameLabel.text}\" == EncounterDefinition.DisplayName.");
        float bearing = HeadingIndependent(cam, W.Hero.transform.position, e1.Site);
        Check(Mathf.Abs(Mathf.DeltaAngle(hud.AlertArrowAngle, bearing)) < 1f && Mathf.Abs(Mathf.DeltaAngle(hud.AlertArrow.resolvedStyle.rotate.angle.ToDegrees(), hud.AlertArrowAngle)) < 1f,
            $"Alert DIRECTION arrow {hud.AlertArrowAngle:0.0}° matches the independent camera-relative bearing {bearing:0.0}° (Vector3.SignedAngle).");
        Check(hud.AlertDistanceLabel.text == $"{Vector3.Distance(W.Hero.transform.position, e1.Site):F0} M AWAY", $"Alert distance \"{hud.AlertDistanceLabel.text}\".");
        bool e1Nearer = (e1.Site - W.Hero.transform.position).sqrMagnitude < (e0.Site - W.Hero.transform.position).sqrMagnitude;
        Check(e1Nearer && hud.ObjectiveEncounter == e0, $"While the alert plays the objective line still follows {e0.Definition.DisplayName} although {e1.Definition.DisplayName} is nearer (it updates after the alert).");
        float tx0 = hud.AlertCard.resolvedStyle.translate.x;
        yield return Realtime(GameHud.AlertSlideSeconds + .2f);
        float tx1 = hud.AlertCard.resolvedStyle.translate.x;
        Check(tx0 > 1f && Mathf.Abs(tx1) < .5f, $"Slide-in: translate x {tx0:0.#} -> {tx1:0.#} (held on screen).");
        foreach (var r in Resolutions) yield return Composite(r, $"alert-{r.x}x{r.y}");
        yield return Until(() => hud.AlertsFinished == 1, GameHud.AlertHoldSeconds + 2f, "alert slide-out");
        Check(!GameHud.Shown(hud.AlertCard), $"Alert slid out after ~{GameHud.AlertSlideSeconds * 2 + GameHud.AlertHoldSeconds:0.0}s.");
        yield return Frames(2); yield return Realtime(.1f);
        Check(hud.ObjectiveEncounter == e1 && hud.ObjectiveHeader.text.StartsWith(e1.Definition.DisplayName.ToUpperInvariant()) && hud.ObjectivePulsing,
            $"THEN the objective line updates to the new, nearer encounter: \"{hud.ObjectiveHeader.text}\" / \"{hud.ObjectiveBody.text}\" (pulsing).");
        Check(hud.AlertsStarted == 1, "Exactly one alert for one spawn.");
        yield return Home(); Destroy(clone);
    }

    // ---- First-time prompts on a dedicated fresh save; seen state persisted; fresh-save CONTROL
    GameModeDefinition PromptMode()
    {
        var clone = Instantiate(hero); clone.name = "hero prompt clone"; clone.BriefingGoal = clone.BriefingWin = clone.BriefingLose = "";
        clone.InitialEncounters = 0; clone.SpawnInterval = 100000f; return clone;
    }
    /// A roster Criminal (Rusher) on the NavMesh about `metres` from the player (first free direction of eight).
    CityNpc Hostile(float metres, bool aggro)
    {
        Vector3 at = W.Hero.transform.position;
        for (int i = 0; i < 16; i++)
        {
            Vector3 p = at + Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward * metres; p.y = at.y;
            if (!NavMesh.SamplePosition(p, out var hit, 1.2f, NavMesh.AllAreas)) continue;
            Vector3 flat = hit.position - at; flat.y = 0; if (Mathf.Abs(flat.magnitude - metres) > 1.2f) continue;
            var npc = CityNpc.Spawn(W, hit.position, NpcRole.Criminal); if (npc == null) continue;
            npc.AlwaysAggro = aggro; Log($"Spawned hostile {npc.Archetype?.name} {flat.magnitude:0.0} m from the player (aggro {aggro})."); return npc;
        }
        throw new Exception("No NavMesh spot " + metres + " m from the player");
    }
    /// TEST HARNESS (Task 0 root cause): the hero is TELEPORTED next to the robber. The old fixed spot (+2.4 m along +X)
    /// sometimes overlapped the encounter's supply crate AND a wandering civilian; the CharacterController then
    /// depenetrated the hero up onto the crate, 3.1-3.2 m from the robber (outside InteractRadius 3), so no prompt could
    /// show. A player walking up can never stand inside a crate, so this picks the first direction (starting at +X) whose
    /// hero capsule overlaps nothing at BOTH `metres` and `hold` metres, and holds nearby civilians still.
    Vector3 FreeDirectionNear(Vector3 at, float metres, float hold)
    {
        var cc = W.Hero.GetComponent<CharacterController>(); float h = cc.height * .5f - cc.radius;
        bool Free(Vector3 p) { Vector3 c = p + cc.center + Vector3.up * .05f; return !Physics.OverlapCapsule(c + Vector3.up * h, c - Vector3.up * h, cc.radius + .05f).Any(o => !o.isTrigger && o.transform.root != W.Hero.transform); }
        foreach (var n in W.Npcs) if (n != null && !n.Dead && n.Role == NpcRole.Civilian && Vector3.Distance(n.transform.position, at) < 8f) n.Freeze(10f);
        for (int i = 0; i < 16; i++)
        {
            Vector3 dir = Quaternion.Euler(0, 90f + i * 22.5f, 0) * Vector3.forward;
            if (Free(at + dir * metres) && Free(at + dir * hold)) { Log($"TEST HARNESS: free standing spot {metres} m from the robber at bearing {90f + i * 22.5f:0.#} deg (candidate {i + 1}/16); civilians within 8 m held still."); return dir; }
        }
        throw new Exception("No free standing spot next to the robber");
    }
    float backflipAt = -100f;
    /// DIAGNOSTIC (Task 0): logs, every frame for `seconds`, the inputs GameHudGuidance.PromptCandidate/UpdatePrompts use.
    IEnumerator TracePromptInputs(CrimeEvent crime, EncounterActor robber, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds; var e = crime.Encounter;
        Log($"TRACE setup: encounter '{e.Definition.DisplayName}' site {V(e.Site)}; crimes {string.Join(" | ", W.Crimes.Where(c => c != null && c.Encounter != null).Select(c => c.Encounter.Definition.DisplayName + "@" + V(c.Encounter.Site) + (c.Resolved ? " resolved" : "")))}; props near the hero: {string.Join(" ", e.Props.Where(b => b != null && Vector3.Distance(b.position, W.Hero.transform.position) < 4f).Select(b => b.name + V(b.position)))}.");
        while (true)
        {
            var cc = W.Hero.GetComponent<CharacterController>(); Vector3 c0 = W.Hero.transform.position + cc.center;
            var overlaps = Physics.OverlapCapsule(c0 + Vector3.up * (cc.height * .5f - cc.radius), c0 - Vector3.up * (cc.height * .5f - cc.radius), cc.radius).Where(o => o.transform.root != W.Hero.transform && !o.isTrigger).Select(o => o.name);
            if (overlaps.Any()) Log("TRACE hero capsule overlaps: " + string.Join(",", overlaps));
            var near = W.Npcs.Where(n => n != null && !n.Dead && n.Hostile && Vector3.Distance(n.transform.position, W.Hero.transform.position) < 10f).Select(n => $"{n.Archetype?.name}{(n.Encounter == e ? "[this]" : n.Encounter != null ? "[other enc]" : "")} {Vector3.Distance(n.transform.position, W.Hero.transform.position):0.0}m {n.Phase}{(n.Frozen ? " frozen" : "")}");
            Log($"TRACE health {W.Health:0.0}; hostiles <10 m: {string.Join("; ", near)}");
            Vector3 p = W.Hero.transform.position, f = W.Hero.transform.forward;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object candidate = typeof(GameHud).GetField("promptCandidate", flags)?.GetValue(hud), scan = typeof(GameHud).GetField("nextPromptScan", flags)?.GetValue(hud);
            Log($"TRACE interact: frame {Time.frameCount} real {Time.realtimeSinceStartup:0.000} unscaledDt {Time.unscaledDeltaTime:0.000} unscaled {Time.unscaledTime:0.000} nextScan {scan:0.000} candidate '{candidate}' t-backflip {Time.time - backflipAt:0.000}s BackflipActive {W.Hero.BackflipActive} hero {V(p)} fwd ({f.x:0.00},{f.z:0.00}) robber {V(robber.Npc.transform.position)} dist {Vector3.Distance(p, robber.Npc.transform.position):0.00}/{e.Definition.InteractRadius} InteractableNear {e.InteractableNear(p)} resolved {crime.Resolved} grounded {W.Hero.GetComponent<CharacterController>().isGrounded} menu {W.MenuOpen} dead {W.PlayerDead} paused {W.Mode.Paused} seen {W.Progression.HintSeen("interact")} prompt '{hud.PromptId}'");
            if (Time.realtimeSinceStartup >= until) yield break;
            yield return null;
        }
    }
    IEnumerator Prompts()
    {
        yield return Home(); WorldSession.VerificationSavePath = PromptSave;
        var mode = PromptMode(); yield return Launch(mode);
        Check(W.Progression.SavePath == PromptSave && W.Progression.Data.SeenHints.Count == 0, "Prompt save is fresh (no seen prompts).");
        yield return Grounded(); yield return Realtime(.3f);
        // move
        Check(hud.PromptId == "move" && GameHud.Shown(hud.PromptCard) && hud.PromptText == $"{HudBindings.JumpKey} JUMP · {HudBindings.FlightKey} HOLD IN THE AIR TO FLY",
            $"Grounded at start -> MOVE prompt \"{hud.PromptText}\" (keys from HudBindings; flight equipped).");
        yield return Composite(new Vector2Int(1920, 1080), "prompt-move-1920x1080");
        yield return Realtime(1.2f); Check(hud.PromptId == "move", "CONTROL: without the action the prompt stays up.");
        Check(W.Hero.TryJump(), "Real jump."); yield return null;
        Check(hud.PromptId == null && !GameHud.Shown(hud.PromptCard) && W.Progression.HintSeen("move") && SavedHints(PromptSave).Contains("move"), $"Jump performed -> prompt gone; saved SeenHints: {SavedHints(PromptSave)}.");
        yield return Grounded(); yield return Realtime(.5f);
        Check(hud.PromptId == null, "CONTROL: nothing hostile near, no telegraph, no interactable -> no prompt.");
        // punch
        var rusher = Hostile(5f, false); rusher.Freeze(30f); Log("TEST HARNESS: a roster Rusher (Criminal) spawned 5 m away and frozen, as the 'hostile nearby' context.");
        yield return Realtime(.3f);
        Check(hud.PromptId == "punch" && hud.PromptText == $"{HudBindings.MeleeKey(W.Powers)} PUNCH", $"Hostile within {GameHud.PunchPromptRange} m -> PUNCH prompt \"{hud.PromptText}\".");
        yield return Composite(new Vector2Int(1920, 1080), "prompt-punch-1920x1080");
        Check(W.Hero.TryPunch(), "Real punch."); yield return null;
        Check(hud.PromptId != "punch" && W.Progression.HintSeen("punch") && SavedHints(PromptSave).Contains("punch"), $"Punch performed -> prompt gone; saved: {SavedHints(PromptSave)}.");
        yield return Realtime(.4f);
        Check(hud.PromptId == null, "CONTROL: frozen hostile (no telegraph) -> no dodge prompt.");
        if (rusher != null && !rusher.Dead) rusher.Damage(10000, null);
        // dodge
        yield return Grounded();
        var attacker = Hostile(4f, true); Log("TEST HARNESS: an aggro Rusher spawned 4 m away for a real telegraphed attack.");
        yield return Until(() => attacker.Phase == AttackPhase.Windup, 8f, "rusher windup");
        yield return Realtime(.3f);   // past the prompt's .22 s fade-in, still inside the .45 s windup
        Check(hud.PromptId == "dodge" && hud.PromptText == $"{HudBindings.BackflipKey} BACKFLIP TO DODGE", $"Enemy telegraph targets the player -> DODGE prompt \"{hud.PromptText}\".");
        yield return Composite(new Vector2Int(1920, 1080), "prompt-dodge-1920x1080", false);
        Check(hud.PromptId == "dodge", "Dodge prompt still up right before the action (lingers " + GameHud.PromptMinSeconds + " s past the telegraph).");
        Check(W.Hero.TryBackflip(), "Real backflip: " + W.Hero.LastBackflipResult); backflipAt = Time.time; yield return null;
        Check(hud.PromptId != "dodge" && W.Progression.HintSeen("dodge") && SavedHints(PromptSave).Contains("dodge"), $"Backflip performed -> prompt gone; saved: {SavedHints(PromptSave)}.");
        attacker.Damage(10000, null);
        // interact
        yield return Realtime(.3f);
        var crime = W.Mode.SpawnNext(); var e = crime.Encounter; Hold(e, 120f); e.enabled = false;
        var robber = e.Robbers.First(a => a.Npc != null);
        Move(robber.Npc.transform.position + Vector3.right * 10f); yield return Realtime(.3f);
        Check(hud.PromptId == null, "CONTROL: 10 m from the robber (outside InteractRadius) -> no interact prompt.");
        var side = FreeDirectionNear(robber.Npc.transform.position, 2.4f, 2f);
        Move(robber.Npc.transform.position + side * 2.4f); yield return TracePromptInputs(crime, robber, .3f);
        Check(hud.PromptId == "interact" && hud.PromptText == $"{HudBindings.InteractKey} HOLD TO INTERACT", $"Next to an interactable objective -> \"{hud.PromptText}\".");
        yield return Composite(new Vector2Int(1920, 1080), "prompt-interact-1920x1080");
        Move(robber.Npc.transform.position + side * 2f); yield return null;   // the player can slide after the capture frames
        Check(hud.PromptId == "interact", "Interact prompt still up right before the hold.");
        Log($"Before the hold: player {Vector3.Distance(W.Hero.transform.position, robber.Npc.transform.position):0.00} m from the robber, InteractableNear {e.InteractableNear(W.Hero.transform.position)}, dt {Time.deltaTime:0.0000}.");
        bool accepted = e.Interact(Time.deltaTime); float held = e.HoldFraction;
        Log($"R hold for one frame: Interact returned {accepted}, HoldFraction {held:0.000}, hint \"{e.InteractionHint}\".");
        yield return null;
        Check(hud.PromptId != "interact" && W.Progression.HintSeen("interact") && SavedHints(PromptSave).Contains("interact"), $"R hold progressing (CrimeEncounter.Interact) -> prompt gone; saved: {SavedHints(PromptSave)}.");
        Check(GameHud.PromptIds.All(id => hud.PromptsShown.Contains(id) && hud.PromptsCompleted.Contains(id)), $"All four prompts shown in context and completed by the real action: {string.Join(",", hud.PromptsCompleted)}.");
        yield return Home(); Destroy(mode);

        // CONTROL: a fresh save shows them again
        WorldSession.VerificationSavePath = FreshSave; mode = PromptMode(); yield return Launch(mode);
        yield return Grounded(); yield return Realtime(.3f);
        Check(hud.PromptId == "move", "Fresh-save CONTROL: MOVE prompt shows again.");
        rusher = Hostile(5f, false); rusher.Freeze(30f); yield return Realtime(.3f);
        Check(hud.PromptId == "punch", "Fresh-save CONTROL: PUNCH prompt shows again.");
        rusher.Damage(10000, null);
        attacker = Hostile(4f, true); yield return Until(() => attacker.Phase == AttackPhase.Windup, 8f, "rusher windup"); yield return Realtime(.15f);
        Check(hud.PromptId == "dodge", "Fresh-save CONTROL: DODGE prompt shows again.");
        attacker.Damage(10000, null); yield return Realtime(.3f);
        e = W.Mode.SpawnNext().Encounter; Hold(e, 60f); e.enabled = false; robber = e.Robbers.First(a => a.Npc != null);
        Move(robber.Npc.transform.position + FreeDirectionNear(robber.Npc.transform.position, 2.4f, 2.4f) * 2.4f); yield return Realtime(.3f);
        Check(hud.PromptId == "interact", "Fresh-save CONTROL: INTERACT prompt shows again.");
        Check(SavedHints(PromptSave).Split(',').Length == 4, "The prompt save still holds its four seen prompts for the separate-process Reload: " + SavedHints(PromptSave));
        yield return Home(); Destroy(mode); WorldSession.VerificationSavePath = MainSave;
    }

    // ---------------------------------------------------------------- Reload (separate Unity process)
    IEnumerator ReloadChecks()
    {
        LoadModes();
        yield return Scene(GameFlow.HomeScene);
        Log("Separate process: pid " + System.Diagnostics.Process.GetCurrentProcess().Id + ", save " + PromptSave);
        Check(ui.Profile.Data.SeenHints != null && GameHud.PromptIds.All(ui.Profile.Data.SeenHints.Contains), "Reloaded prompt save carries SeenHints: " + string.Join(",", ui.Profile.Data.SeenHints));
        var mode = PromptMode(); yield return Launch(mode);
        yield return Grounded(); yield return Realtime(1.5f);
        Check(hud.PromptId == null && hud.PromptsShown.Count == 0, "Seen MOVE prompt does not reappear (grounded 1.5 s).");
        var rusher = Hostile(5f, false); rusher.Freeze(30f); yield return Realtime(.6f);
        Check(hud.PromptId == null, "Seen PUNCH prompt does not reappear next to a hostile.");
        rusher.Damage(10000, null);
        var attacker = Hostile(4f, true); yield return Until(() => attacker.Phase == AttackPhase.Windup, 8f, "rusher windup"); yield return Realtime(.15f);
        Check(hud.PromptId == null, "Seen DODGE prompt does not reappear during a telegraph.");
        attacker.Damage(10000, null); yield return Realtime(.3f);
        var e = W.Mode.SpawnNext().Encounter; Hold(e, 60f); e.enabled = false; var robber = e.Robbers.First(a => a.Npc != null);
        Move(robber.Npc.transform.position + FreeDirectionNear(robber.Npc.transform.position, 2.4f, 2.4f) * 2.4f); yield return Realtime(.4f);
        Check(e.InteractableNear(W.Hero.transform.position), $"The hero really stands inside InteractRadius ({Vector3.Distance(W.Hero.transform.position, robber.Npc.transform.position):0.00} m), so the next check is not vacuous.");
        Check(hud.PromptId == null && hud.PromptsShown.Count == 0, "Seen INTERACT prompt does not reappear; no prompt shown at all this session.");
        yield return Home(); Destroy(mode);

        // Legacy save without SeenHints (Verification/ModeExpansion/legacy-save-fixture.json) loads; prompts show.
        WorldSession.VerificationSavePath = LegacySave;
        Check(!File.ReadAllText(LegacySave).Contains("SeenHints"), "Legacy fixture copy has no SeenHints field.");
        mode = PromptMode(); yield return Launch(mode);
        var d = W.Progression.Data;
        Check(W.Progression.LastError == null && d.Level == 2 && d.Xp == 70 && d.SessionsPlayed == 4 && d.BestSessionScore == 420 && d.SeenHints != null && d.SeenHints.Count == 0,
            $"Legacy save loads: level {d.Level}, XP {d.Xp}, sessions {d.SessionsPlayed}, best {d.BestSessionScore}, SeenHints [] (null-guarded), error {W.Progression.LastError ?? "none"}.");
        yield return Grounded(); yield return Realtime(.3f);
        Check(hud.PromptId == "move", "Legacy save: MOVE prompt shows (nothing seen yet) — prompts work in this process.");
        yield return Composite(new Vector2Int(1920, 1080), "reload-legacy-move-prompt-1920x1080");
        Check(W.Hero.TryJump(), "Real jump."); yield return null;
        Check(SavedHints(LegacySave) == "move", "Legacy save rewritten with the additive field: SeenHints=" + SavedHints(LegacySave));
        yield return Home(); Destroy(mode);
    }
}
#endif
