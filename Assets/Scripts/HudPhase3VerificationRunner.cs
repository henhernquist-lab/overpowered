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

/// HUD Phase 3 verification (see HudPhase3Verification). Real GameFlow sessions, real encounters and NPCs, and the real
/// gameplay entry points: CityNpc.Damage(amount, player PowerUser) (what every player power hit calls),
/// BreakableProp.TakeDamage(amount, player PowerUser), CrimeEncounter.Interact (the R hold), GameModeSession.SpawnNext,
/// PlayerProgression.ClaimRoof (the rooftop pickup) and SuperHeroController.TryJump.
/// XP is accounted INDEPENDENTLY of the HUD (level + XP converted to a lifetime total with the GameTuning formula, plus an
/// XpAwarded listener). Captures are composited (camera + main HUD panel + overlay panel) like Phase 2.
public sealed class HudPhase3VerificationRunner : MonoBehaviour
{
    public string Folder, MainSave, ExpectedFile;
    public bool ReloadMode;
    public Action<int> Finished;
    readonly List<string> output = new List<string>();
    WorldSession W => WorldSession.Instance;
    GameFlow Flow => GameFlow.Instance;
    GameHud hud; ModeScreens ui; CityPalette palette;
    static readonly Vector2Int[] Resolutions = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) };
    GameModeDefinition hero, villain, freePlay, endless;
    // Independent XP accounting for the running session
    int awardedSum, awardedEvents, totalAtLaunch; PlayerProgression listened;
    readonly List<GameModeDefinition> clones = new List<GameModeDefinition>();

    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder); QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        var stack = new Stack<IEnumerator>(); stack.Push(TransientVerdict()); if (!ReloadMode) stack.Push(PopupNudgeControl()); stack.Push(ReloadMode ? ReloadChecks() : Checks());
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
    void Log(string text) { output.Add(text); Debug.Log("[HUD3] " + text); }
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
    /// POSITIVE CONTROL for the crosshair keep-out: XP granted at a world point whose popup anchor projects EXACTLY onto
    /// the crosshair must be nudged off it (its box clear of the keep-out circle), not drawn over it. Own Hero session,
    /// after every accounted check, so the extra +1 XP touches no honesty sum.
    IEnumerator PopupNudgeControl()
    {
        var hero = Resources.LoadAll<GameModeDefinition>("Modes").First(m => m.Id == "hero");
        string nudgeSave = Path.Combine(Path.GetDirectoryName(MainSave), "save-nudge.json");   // isolated: MainSave + ExpectedFile stay as Run left them for Reload
        foreach (var f in new[] { nudgeSave, nudgeSave + ".bak", nudgeSave + ".tmp" }) if (File.Exists(f)) File.Delete(f);
        yield return Home(); WorldSession.VerificationSavePath = nudgeSave;
        yield return Launch(hero);
        Check(W.Progression.SavePath == nudgeSave, "Nudge control runs on its own isolated save.");
        var cam = Cam(); var target = new RenderTexture(1920, 1080, 24) { name = "HUD3 nudge" }; target.Create();
        hud.Panel.targetTexture = target; hud.OverlayPanel.targetTexture = target; cam.aspect = 16f / 9f; yield return Frames(3);
        var ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0)); Vector3 anchor = ray.GetPoint(9f) - Vector3.up * GameHud.PopupHeight;
        W.Progression.AddXp(1, anchor, "crosshair nudge control");
        yield return Frames(3);
        var p = hud.Popups.FirstOrDefault(x => x.Active && x.Reason == "crosshair nudge control");
        var o = hud.OverlayRoot.layout; var centre = new Vector2(o.width * .5f, o.height * .5f); var at = cam.WorldToViewportPoint(anchor + Vector3.up * GameHud.PopupHeight);
        var box = p?.Element.worldBound ?? default; var near = new Vector2(Mathf.Clamp(centre.x, box.xMin, box.xMax), Mathf.Clamp(centre.y, box.yMin, box.yMax));
        Check(p != null && Mathf.Abs(at.x - .5f) < .002f && Mathf.Abs(at.y - .5f) < .01f && p.Nudged && !box.Overlaps(hud.Crosshair.worldBound) && (near - centre).magnitude >= GameHud.PopupCrosshairClearRadius - 1f,
            $"CONTROL: a popup anchored ON the crosshair (anchor viewport {at.x:0.000},{at.y:0.000}) is nudged off it: box {R(box)}, nearest edge {(near - centre).magnitude:0.0} px from centre (keep-out {GameHud.PopupCrosshairClearRadius:0.0}), crosshair {R(hud.Crosshair.worldBound)}.");
        cam.ResetAspect(); hud.Panel.targetTexture = null; hud.OverlayPanel.targetTexture = null; target.Release(); Destroy(target);
        yield return Home(); WorldSession.VerificationSavePath = MainSave;
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
    IEnumerator Home() { if (SceneManager.GetActiveScene().name != GameFlow.HomeScene || W != null) { Unlisten(); Flow.Home(); yield return Scene(GameFlow.HomeScene); } }
    IEnumerator Launch(GameModeDefinition mode, bool dismissBriefing = true)
    {
        yield return Home();
        Check(Flow.Select(mode), "GameFlow.Select launches " + mode.name);
        yield return Scene(GameFlow.CityScene);
        Check(W.Mode != null && W.Mode.Definition == mode && hud != null && hud.OverlayRoot != null, $"{mode.name}: session runs this definition; GameHud + overlay exist.");
        Listen();
        yield return Frames(3);
        if (dismissBriefing && hud.BriefingActive)
        {
            yield return Grounded(); Check(W.Hero.TryJump(), "Real jump dismisses the briefing (SuperHeroController.TryJump)."); yield return null;
            Check(!hud.BriefingActive, "Briefing dismissed by " + hud.BriefingDismissedBy); yield return Realtime(GameHud.BriefingFadeSeconds + .1f); yield return Grounded();
        }
    }
    void Listen() { Unlisten(); listened = W.Progression; listened.XpAwarded += OnAwarded; awardedSum = 0; awardedEvents = 0; totalAtLaunch = Total(); }
    void Unlisten() { if (listened != null) listened.XpAwarded -= OnAwarded; listened = null; }
    void OnAwarded(int amount) { awardedSum += amount; if (amount > 0) awardedEvents++; }
    IEnumerator Grounded() { yield return Until(() => W.Hero.GetComponent<CharacterController>().isGrounded && !W.Hero.BackflipActive, 6, "hero grounded"); yield return Frames(2); }
    Camera Cam() { var follow = FindAnyObjectByType<ThirdPersonCamera>(); return follow != null ? follow.GetComponent<Camera>() : Camera.main; }
    void Move(Vector3 position) { var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false; W.Hero.transform.position = position; cc.enabled = true; W.Hero.ResetMotion(); Physics.SyncTransforms(); }
    Color Accent(PlayerSide side) => palette.Colors[(int)(side == PlayerSide.Hero ? CityColor.HeroAccent : CityColor.VillainAccent)];
    Color Muted => palette.Colors[(int)CityColor.UiMuted];
    static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < .01f && Mathf.Abs(a.g - b.g) < .01f && Mathf.Abs(a.b - b.b) < .01f;
    static string R(Rect r) => $"({r.x:0.#},{r.y:0.#} {r.width:0.#}x{r.height:0.#})";
    static string V(Vector3 v) => $"({v.x:0.0},{v.y:0.0},{v.z:0.0})";
    CrimeEncounter FirstEncounter() => W.Crimes.Where(c => c != null && !c.Resolved && c.Encounter != null).Select(c => c.Encounter).FirstOrDefault();
    /// TEST HARNESS (logged where used): hold an encounter's actors still so placements are deterministic.
    void Hold(CrimeEncounter e, float seconds) { foreach (var a in e.Robbers.Concat(e.Civilians)) if (a.Npc != null && !a.Npc.Dead) a.Npc.Freeze(seconds); }
    GameModeDefinition Clone(GameModeDefinition d, string name, bool briefing = true)
    {
        var c = Instantiate(d); c.name = name; c.SpawnInterval = 100000f; if (!briefing) c.BriefingGoal = c.BriefingWin = c.BriefingLose = ""; clones.Add(c); return c;
    }

    // ---- independent XP accounting (GameTuning formula, same as PlayerProgression.RequiredXp)
    int Required(int level) { var p = W != null ? W.Tuning.Progression : Resources.Load<GameTuning>("GameTuning").Progression; return Mathf.Max(1, Mathf.RoundToInt(p.BaseLevelXp * Mathf.Pow(p.LevelXpGrowth, level - 1))); }
    int Total(int level, int xp) { int t = xp; for (int l = 1; l < level; l++) t += Required(l); return t; }
    int Total() => Total(W.Progression.Data.Level, W.Progression.Data.Xp);
    GameHud.XpPopup NewestSince(int id) => hud.Popups.Where(p => p.Active && p.Id > id).OrderByDescending(p => p.Id).FirstOrDefault();
    int LastPopupId() => hud.PopupHistory.Count > 0 ? hud.PopupHistory[hud.PopupHistory.Count - 1].Id : 0;
    Vector2 Project(Vector3 world, out float depth)
    {
        var vp = Cam().WorldToViewportPoint(world + Vector3.up * GameHud.PopupHeight); var o = hud.OverlayRoot.layout; depth = vp.z;
        return new Vector2(vp.x * o.width, (1 - vp.y) * o.height);
    }
    /// The popup is where its event happened: base point == the projection of (event + PopupHeight up), and the drawn
    /// label sits there minus the rise (unless clamped inside the safe margin at a screen edge).
    void ProjectionCheck(GameHud.XpPopup p, Vector3 where, string label)
    {
        var at = Project(where, out float depth);
        if (depth <= 0f || !p.OnScreen) { Log($"NOTE {label}: event point is off-screen (depth {depth:0.0}); the popup is counted but not drawn."); return; }
        Check(Vector2.Distance(at, p.BasePoint) < 1.5f, $"{label}: popup base point {p.BasePoint} == projection of the event's world position {V(where)} + {GameHud.PopupHeight} m ({at}).");
        var centre = p.Element.worldBound.center;
        if (!p.Clamped) Check(Vector2.Distance(centre, at - new Vector2(0, p.Rise)) < 3f && GameHud.Shown(p.Element), $"{label}: label drawn at the projection minus its rise {p.Rise:0.#} (label centre {centre}).");
        else Log($"NOTE {label}: popup clamped inside the safe margin at {p.DrawnPoint} (projection {at}).");
    }
    /// One real XP event -> exactly one new popup whose amount == the XP actually granted, anchored at the event.
    IEnumerator RealAward(string label, Action trigger, Func<Vector3> where, int expected)
    {
        yield return Realtime(GameHud.PopupMergeSeconds + .05f);   // outside the merge window of any earlier grant
        int total0 = Total(), started0 = hud.PopupsStarted, shown0 = hud.PopupXpShown, merges0 = hud.PopupMerges, id0 = LastPopupId();
        Vector3 at = where(); trigger(); int granted = Total() - total0;
        Check(granted == expected, $"{label}: XP actually granted {granted} (Progression delta; tuning value {expected}).");
        yield return null; yield return null;
        var p = NewestSince(id0);
        Check(p != null && hud.PopupsStarted == started0 + 1 && hud.PopupMerges == merges0 && p.Amount == granted && p.Grants == 1 && p.Element.text == $"+{granted} XP" && hud.PopupXpShown - shown0 == granted,
            $"{label}: ONE new popup \"{p?.Element.text}\" == XP granted ({granted}); popups started +{hud.PopupsStarted - started0}, merges +{hud.PopupMerges - merges0}.");
        Check(p.Anchored && Vector3.Distance(p.Anchor, at) < .01f, $"{label}: popup anchored at the event's world position {V(at)} (reason \"{p.Reason}\"; anchor {p.Anchor.x:0.000},{p.Anchor.y:0.000},{p.Anchor.z:0.000}, |d| {Vector3.Distance(p.Anchor, at):0.0000} m; timeScale {Time.timeScale}).");
        ProjectionCheck(p, at, label);
        lastPopup = p;
    }
    GameHud.XpPopup lastPopup;
    CityNpc Criminal(Vector3 near)
    {
        for (int i = 0; i < 12; i++)
        {
            Vector3 p = near + Quaternion.Euler(0, i * 30f, 0) * Vector3.forward * (i == 0 ? 0f : .8f);
            if (!NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) continue;
            var npc = CityNpc.Spawn(W, hit.position, NpcRole.Criminal); if (npc == null) continue; npc.Freeze(120f); return npc;
        }
        throw new Exception("No NavMesh spot near " + V(near));
    }
    Vector3 Ahead(float metres) { var f = Cam().transform.forward; f.y = 0; return W.Hero.transform.position + f.normalized * metres; }
    Vector3 CamRight() { var r = Cam().transform.right; r.y = 0; return r.normalized; }
    Vector3 CamForward() { var f = Cam().transform.forward; f.y = 0; return f.normalized; }
    void Defeat(CityNpc npc) => npc.Damage(100000f, W.Powers);
    /// TEST HARNESS (Villain side only, logged): police hunting the villain are frozen so gunfire cannot kill the player
    /// mid-check (a death respawns and resets Heat, which would pollute the Heat and XP accounting).
    void CalmPolice() { foreach (var n in W.Npcs) if (n != null && !n.Dead && n.Hostile && n.Encounter == null) n.Freeze(20f); }
    string HeatDelta(float before, float add) { float after = Mathf.Clamp(before + add, 0, W.Tuning.Heat.MaximumStars), d = after - before; return (d >= 0 ? "+" : hud.Minus) + Mathf.Abs(d).ToString("0.##"); }
    void GlyphCheck(Label label, string what)
    {
        var font = hud.Root.resolvedStyle.unityFont; var missing = label.text.Where(c => c != ' ' && !font.HasCharacter(c)).Distinct().ToArray();
        Check(missing.Length == 0, $"{what}: every glyph of \"{label.text}\" exists in the HUD font {font.name}" + (missing.Length > 0 ? " — MISSING: " + new string(missing) : "."));
    }
    /// Honesty for the whole session so far: sum of popup amounts == XP gained == XpAwarded sum.
    void Honesty(string label)
    {
        int gained = Total() - totalAtLaunch, popupSum = hud.PopupHistory.Sum(r => r.Amount);
        Check(gained == awardedSum && hud.PopupXpShown == gained && popupSum == gained && hud.XpGrantsReceived == awardedEvents,
            $"HONESTY {label}: SUM(popup XP) {popupSum} == HUD shown total {hud.PopupXpShown} == XP gained {gained} (level/XP, GameTuning formula) == XpAwarded sum {awardedSum}; {hud.PopupsStarted} popups for {awardedEvents} grants ({hud.PopupMerges} merged).");
    }
    void TimelineCheck(string label)
    {
        var t = hud.Timeline; float now = Time.unscaledTime; var bad = new List<string>();
        for (int i = 0; i < t.Count; i++) for (int j = i + 1; j < t.Count; j++)
        {
            float ai = t[i].StartedAt, bi = t[i].EndedAt < 0 ? now : t[i].EndedAt, aj = t[j].StartedAt, bj = t[j].EndedAt < 0 ? now : t[j].EndedAt;
            if (ai < bj - .0001f && aj < bi - .0001f) bad.Add($"{t[i].Kind}:{t[i].Name} x {t[j].Kind}:{t[j].Name}");
        }
        Check(bad.Count == 0, $"{label}: alerts and banners never overlapped in time: " + string.Join(" | ", t.Select(e => $"{e.Kind} '{e.Name}' {e.StartedAt:0.00}-{(e.EndedAt < 0 ? "now" : e.EndedAt.ToString("0.00"))}")) + (bad.Count > 0 ? " — OVERLAP: " + string.Join(", ", bad) : ""));
    }

    // ---------------------------------------------------------------- layout assertions (panel logical units)
    static IEnumerable<VisualElement> Descendants(VisualElement root) { yield return root; foreach (var child in root.Children()) foreach (var d in Descendants(child)) yield return d; }
    IEnumerable<VisualElement> Overlay() { yield return hud.AlertCard; yield return hud.PromptCard; yield return hud.Waypoint; yield return hud.WaypointEdge; yield return hud.Briefing; yield return hud.Banner; }
    IEnumerable<VisualElement> ShownPopups() => hud.Popups.Where(p => p.Active && GameHud.Shown(p.Element)).Select(p => (VisualElement)p.Element);
    bool IsWaypoint(VisualElement g) => g == hud.Waypoint || g == hud.WaypointEdge;
    void AssertLayout(Vector2Int size, string label)
    {
        float scale = Mathf.Min(size.x / (float)GameHud.ReferenceResolution.x, size.y / (float)GameHud.ReferenceResolution.y);
        var panel = hud.Root.layout; var overlay = hud.OverlayRoot.layout; Vector2 expected = new Vector2(size.x / scale, size.y / scale);
        Check(Mathf.Abs(panel.width - expected.x) < 1.5f && Mathf.Abs(panel.height - expected.y) < 1.5f && Mathf.Abs(overlay.width - panel.width) < .5f && Mathf.Abs(overlay.height - panel.height) < .5f,
            $"{label}: both panels {panel.width:0.#}x{panel.height:0.#} logical (expected {expected.x:0.#}x{expected.y:0.#}).");
        float m = GameHud.SafeMargin - .5f; var safe = Rect.MinMaxRect(m, m, panel.width - m, panel.height - m);
        var centre = new Rect(panel.width * (1 - GameHud.CentreZone) * .5f, panel.height * (1 - GameHud.CentreZone) * .5f, panel.width * GameHud.CentreZone, panel.height * GameHud.CentreZone);
        var cross = hud.Crosshair.worldBound;
        var groups = hud.Groups.Concat(Overlay()).Where(GameHud.Shown).ToList(); var popups = ShownPopups().ToList();
        var clipped = new List<string>(); var inCentre = new List<string>(); var truncated = new List<string>(); int count = 0;
        foreach (var group in groups.Concat(popups))
            foreach (var e in Descendants(group))
            {
                if (!GameHud.Shown(e)) continue; var r = e.worldBound; if (r.width <= .01f || r.height <= .01f) continue; count++;
                string id = group.name + "/" + (string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name) + (e is Label l0 ? "'" + l0.text + "'" : "");
                if (r.xMin < safe.xMin || r.yMin < safe.yMin || r.xMax > safe.xMax || r.yMax > safe.yMax) clipped.Add(id + R(r));
                bool centreAllowed = group == hud.Briefing || IsWaypoint(group) || popups.Contains(group);
                if (!centreAllowed && r.Overlaps(centre)) inCentre.Add(id + R(r));
                if (e is Label text && !string.IsNullOrEmpty(text.text))
                {
                    var content = text.contentRect;
                    if (text.resolvedStyle.whiteSpace == WhiteSpace.NoWrap)
                    { var need = text.MeasureTextSize(text.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined); if (need.x > content.width + 1f) truncated.Add($"{id} needs {need.x:0.#} has {content.width:0.#}"); }
                    else { var need = text.MeasureTextSize(text.text, content.width + 1f, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined); if (need.y > content.height + 1f) truncated.Add($"{id} needs h{need.y:0.#} has h{content.height:0.#}"); }
                }
            }
        Check(clipped.Count == 0, $"{label}: all {count} visible HUD / overlay / popup elements inside the {GameHud.SafeMargin}px safe margin" + (clipped.Count > 0 ? " — CLIPPED: " + string.Join("; ", clipped) : "."));
        Check(inCentre.Count == 0, $"{label}: centre 40%x40% {R(centre)} holds only the crosshair, waypoint, popups (transients) and a briefing" + (inCentre.Count > 0 ? " — IN CENTRE: " + string.Join("; ", inCentre) : "."));
        Check(truncated.Count == 0, $"{label}: no visible label truncated" + (truncated.Count > 0 ? " — " + string.Join("; ", truncated) : "."));
        var overlaps = new List<string>(); var solid = groups.Where(g => g != hud.Briefing).ToList();
        for (int i = 0; i < solid.Count; i++) for (int j = i + 1; j < solid.Count; j++) if (solid[i].worldBound.Overlaps(solid[j].worldBound)) overlaps.Add(solid[i].name + " x " + solid[j].name);
        Check(overlaps.Count == 0, $"{label}: no two HUD groups/cards/banner overlap: " + string.Join(" ", solid.Select(g => g.name + R(g.worldBound))) + (overlaps.Count > 0 ? " — OVERLAP: " + string.Join(", ", overlaps) : ""));
        foreach (var w in groups.Where(IsWaypoint)) Check(!w.worldBound.Overlaps(cross), $"{label}: waypoint {w.name}{R(w.worldBound)} does not cover the crosshair box {R(cross)}.");
        var edge = new[] { hud.LevelGroup, hud.HeatGroup, hud.TopCentre, hud.Banner, hud.VitalsGroup, hud.BottomStack }.Where(GameHud.Shown).ToList();
        var covered = popups.SelectMany(p => edge.Where(g => p.worldBound.Overlaps(g.worldBound)).Select(g => $"'{((Label)p).text}'{R(p.worldBound)} x {g.name}{R(g.worldBound)}")).ToList();
        Check(covered.Count == 0, $"{label}: no XP popup covers an edge HUD group or the banner" + (covered.Count > 0 ? " — " + string.Join("; ", covered) : $" ({popups.Count} popup(s) shown)."));
        foreach (var p in popups) foreach (var g in solid.Where(g => !edge.Contains(g) && p.worldBound.Overlaps(g.worldBound))) Log($"NOTE {label}: popup '{((Label)p).text}'{R(p.worldBound)} passes over {g.name} (transient, world-anchored).");
        if (GameHud.Shown(hud.Crosshair))
        {
            var c = cross.center * scale; var screen = new Vector2(size.x * .5f, size.y * .5f);
            Check(Vector2.Distance(c, screen) <= 1f, $"{label}: crosshair centre {c.x:0.##},{c.y:0.##} px vs screen centre.");
        }
    }

    // ---------------------------------------------------------------- composited capture + pixel checks
    /// mustDraw: transient elements this capture exists to show (asserted shown and visibly drawn in the image).
    IEnumerator Composite(Vector2Int size, string name, params VisualElement[] mustDraw)
    {
        if (mustDraw.Contains(hud.Banner)) yield return Until(() => hud.Banner.resolvedStyle.opacity > .95f, GameHud.BannerInSeconds + .5f, "banner faded in");
        float began = Time.realtimeSinceStartup;
        var cam = Cam(); Check(cam != null, "Gameplay camera found for " + name);
        var composite = new RenderTexture(size.x, size.y, 24) { name = "HUD3 composite " + name }; composite.Create();
        var plain = new RenderTexture(size.x, size.y, 24) { name = "HUD3 camera only " + name }; plain.Create();
        hud.Panel.targetTexture = composite; hud.OverlayPanel.targetTexture = composite;
        cam.aspect = size.x / (float)size.y;
        yield return Frames(3);
        AssertLayout(size, $"{name} {size.x}x{size.y}");
        foreach (var e in mustDraw) Check(GameHud.Shown(e) && e.resolvedStyle.opacity > .5f, $"{name}: {e.name} '{(e is Label l ? l.text : (e == hud.Banner ? hud.BannerTitleLabel.text : ""))}' is shown in this capture (opacity {e.resolvedStyle.opacity:0.00}).");
        var transients = ShownPopups().Select(p => $"popup '{((Label)p).text}' at {R(p.worldBound)}").ToList();
        if (GameHud.Shown(hud.Banner)) transients.Add($"banner '{hud.BannerEyebrowLabel.text} / {hud.BannerTitleLabel.text} / {hud.BannerRewardLabel.text}'");
        if (GameHud.Shown(hud.HeatDelta)) transients.Add($"heat delta '{hud.HeatDeltaText}'");
        if (hud.LevelBursting) transients.Add("level burst");
        Log($"STATE {name}: hero {V(W.Hero.transform.position)}, cam {V(cam.transform.position)}; waypoint {(hud.WaypointVisible ? (hud.WaypointOnScreen ? "marker" : "edge " + hud.WaypointArrowAngle.ToString("0.0") + "°") : "hidden")}; transients: {(transients.Count == 0 ? "none" : string.Join("; ", transients))}.");
        bool wasEnabled = cam.enabled; cam.enabled = false;
        cam.targetTexture = plain; cam.Render(); cam.targetTexture = composite; cam.Render(); cam.targetTexture = null;
        hud.Root.MarkDirtyRepaint(); hud.OverlayRoot.MarkDirtyRepaint();
        yield return null;
        var a = Read(composite); var b = Read(plain); cam.enabled = wasEnabled;
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), a.EncodeToPNG());
        PixelChecks(a, b, size, name, mustDraw);
        Destroy(a); Destroy(b);
        cam.ResetAspect(); hud.Panel.targetTexture = null; hud.OverlayPanel.targetTexture = null; composite.Release(); plain.Release(); Destroy(composite); Destroy(plain);
        yield return Frames(1);
        Log($"CAPTURE {name}.png took {Time.realtimeSinceStartup - began:0.00}s real time.");
    }
    static Texture2D Read(RenderTexture target)
    {
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        RenderTexture.active = previous; return image;
    }
    void PixelChecks(Texture2D composite, Texture2D plain, Vector2Int size, string name, VisualElement[] mustDraw)
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
        foreach (var e in mustDraw)
        {
            float changed = Changed(Pixels(e.worldBound));
            Check(changed > .04f, $"{name}: {e.name} visibly drawn in the image ({changed:P1} of its box changed vs camera-only).");
        }
        var exempt = new List<RectInt>();
        void Exempt(VisualElement e, int pad) { var r = Pixels(e.worldBound); exempt.Add(new RectInt(r.xMin - pad, r.yMin - pad, r.width + 2 * pad, r.height + 2 * pad)); }
        if (GameHud.Shown(hud.Crosshair)) Exempt(hud.Crosshair, 2);
        var transients = Transients().ToList();
        Check(transients.All(t => !t.worldBound.Overlaps(hud.Crosshair.worldBound)), $"{name}: {transients.Count} transient feedback element(s) shown, none overlapping the crosshair box {R(hud.Crosshair.worldBound)}" + (transients.Count > 0 ? ": " + string.Join(" ", transients.Select(t => t.name + R(t.worldBound))) : "."));
        foreach (var w in Overlay().Where(e => (IsWaypoint(e) || e == hud.Briefing) && GameHud.Shown(e))) Exempt(w, 4);
        foreach (var p in ShownPopups()) Exempt(p, 6);
        var zone = new RectInt(Mathf.RoundToInt(size.x * .3f), Mathf.RoundToInt(size.y * .3f), Mathf.RoundToInt(size.x * .4f), Mathf.RoundToInt(size.y * .4f));
        int offenders = 0, maxDiff = 0, total = 0;
        for (int y = zone.yMin; y < zone.yMax; y++) for (int x = zone.xMin; x < zone.xMax; x++)
        {
            var p = new Vector2Int(x, y); if (exempt.Any(r => r.Contains(p))) continue;
            total++; int d = Diff(x, y); maxDiff = Mathf.Max(maxDiff, d); if (d > 3) offenders++;
        }
        Check(offenders == 0, $"{name}: PIXEL centre-clear — {total} centre-zone pixels outside the crosshair/waypoint/popup boxes identical to the camera-only render (max diff {maxDiff}, {offenders} > 3).");
        Log($"CAPTURE {name}.png {size.x}x{size.y}; coverage: {string.Join(", ", parts)}.");
    }

    // ---------------------------------------------------------------- the run
    void LoadModes()
    {
        palette = Resources.Load<CityPalette>("CityPalette");
        var catalog = Resources.LoadAll<GameModeDefinition>("Modes"); GameModeDefinition Mode(string id) => catalog.First(m => m.Id == id);
        hero = Mode("hero"); villain = Mode("villain"); freePlay = Mode("free-play"); endless = Mode("endless-fight");
    }
    IEnumerator Checks()
    {
        LoadModes();
        yield return Scene(GameFlow.HomeScene);
        Check(ui != null && ui.Profile.Data.Level == 1 && ui.Profile.Data.Xp == 0 && ui.Profile.Data.SessionsPlayed == 0, "Fresh isolated main save CONTROL: level 1, 0 XP, no sessions.");
        Log($"Constants: popup {GameHud.PopupSeconds}s (fade {GameHud.PopupFadeSeconds}s, rise {GameHud.PopupRise}px, +{GameHud.PopupHeight} m, font {GameHud.PopupFont}, pool {GameHud.PopupPoolSize}); merge {GameHud.PopupMergeSeconds}s within {GameHud.PopupMergeRadius} m; " +
            $"heat flash {GameHud.HeatFlashSeconds}s (merge {GameHud.HeatMergeSeconds}s); level burst {GameHud.LevelBurstSeconds}s; banner in {GameHud.BannerInSeconds} / hold {GameHud.BannerHoldSeconds} / out {GameHud.BannerOutSeconds}s; " +
            $"waypoint diamond {GameHud.WaypointMarkerSize}, distance font {GameHud.WaypointDistanceFont}, count font {GameHud.WaypointCountFont}, arrow {GameHud.WaypointArrowSize}, edge font {GameHud.WaypointEdgeFont}, crosshair clear radius {GameHud.CrosshairClearRadius}.");
        yield return HeroFlow();
        yield return VillainFlow();
        yield return EndlessFlow();
        yield return IdleControl();
        yield return Home();
        var d = ui.Profile.Data; File.WriteAllText(ExpectedFile, $"{d.Level} {d.Xp} {d.Points}");
        Log($"Expected state for the separate-process Reload: level {d.Level}, XP {d.Xp}, points {d.Points} (save {MainSave}).");
        foreach (var c in clones) if (c != null) Destroy(c);
        Log("LIMIT: no hardware keyboard/mouse in batch mode; defeats use CityNpc.Damage(amount, player PowerUser) and destruction BreakableProp.TakeDamage(amount, player PowerUser) — the entry points every player power hit calls. Captures are batch-mode Metal render targets, not a physical display. Nothing here measures how the rhythm FEELS.");
    }

    // ---- Hero: rescue/destruction CONTROLS (0 XP -> 0 popups), heat flash, single / spaced / radius / merged defeats,
    //      level-up (non-pausing), objective complete banner -> queued alert order, failure banner, enlarged waypoint.
    IEnumerator HeroFlow()
    {
        var mode = Clone(hero, "hero phase3 clone (no timed spawns)");
        yield return Launch(mode);
        Log("TEST HARNESS: cloned hero definition with SpawnInterval=100000 (no timed spawns); the initial encounter's actors are held with CityNpc.Freeze and its clock paused (enabled=false).");
        var e = FirstEncounter(); Check(e != null, "Hero: initial encounter " + e.Definition.DisplayName + $" (robbers {e.Robbers.Count}, civilians {e.Civilians.Count}, hazards {e.Hazards.Count})."); Hold(e, 600f); e.enabled = false;
        GlyphCheck(new Label($"{hud.Minus}0.35 {hud.Dash} +105 XP LEVEL UP TAB TO UPGRADE OBJECTIVE COMPLETE FAILED WAVE CLEARED SCORE POINTS 0123456789"), $"HUD glyphs (minus '{hud.Minus}', dash '{hud.Dash}')");

        // Destruction on the Hero side: Heat flash with the delta; 0 XP -> NO popup (negative control)
        var civ = e.Civilians.First(a => a.Blockade != null);
        Move(civ.Blockade.position - CamForward() * 7f + CamRight() * 1.5f); yield return Grounded(); yield return Realtime(.5f);
        int flashes0 = hud.HeatFlashes, started0 = hud.PopupsStarted, total0 = Total(); float heat0 = W.Heat; string expectText = HeatDelta(heat0, W.Tuning.Heat.DestructionHeat);
        civ.Blockade.GetComponent<BreakableProp>().TakeDamage(100000f, W.Powers); yield return null; yield return null;
        Check(W.Heat > heat0 && hud.HeatFlashes == flashes0 + 1 && hud.HeatFlashing && GameHud.Shown(hud.HeatDelta) && hud.HeatDeltaText == expectText,
            $"Real destruction (blockade, BreakableProp.TakeDamage) -> Heat {heat0:0.00} -> {W.Heat:0.00}: ONE heat flash showing \"{hud.HeatDeltaText}\" (expected {expectText}).");
        GlyphCheck(hud.HeatDeltaLabel, "Heat delta");
        Check(Total() == total0 && hud.PopupsStarted == started0, "CONTROL: Hero-side destruction grants 0 XP (GameTuning: Villain only) -> NO popup.");
        yield return Composite(new Vector2Int(1920, 1080), "heat-flash-hero-destruction-1920x1080", hud.HeatDelta);
        foreach (var other in e.Civilians.Where(a => a.Blockade != null)) other.Blockade.GetComponent<BreakableProp>().TakeDamage(100000f, W.Powers);
        yield return Frames(3);

        // Rescue: 0 XP -> no popup (the rescue grants score only)
        var rescue = e.Civilians[0]; Move(rescue.Npc.transform.position + CamRight() * 1.5f); yield return Frames(2);
        total0 = Total(); started0 = hud.PopupsStarted; int rescues0 = W.Mode.Rescues;
        Check(e.Interact(e.Definition.HoldSeconds + .01f) && rescue.Saved && W.Mode.Rescues == rescues0 + 1, "Real rescue (CrimeEncounter.Interact, the R hold): civilian saved, GameModeSession.Rescues +1.");
        yield return Frames(2);
        Check(Total() == total0 && hud.PopupsStarted == started0, $"CONTROL: a rescue grants 0 XP today (score +{W.Mode.Definition.RescueScore} only) -> NO XP popup (popups only show XP actually granted).");

        // Single real defeat: +EnemyXp at the robber
        int enemyXp = W.Tuning.Progression.EnemyXp;
        var robber = e.Robbers.First(a => a.Npc != null && !a.Npc.Dead);
        Move(robber.Npc.transform.position - CamForward() * 6f - CamRight() * 1.5f); yield return Grounded(); yield return Realtime(.4f);
        yield return RealAward("Real defeat (robber)", () => Defeat(robber.Npc), () => robber.Npc.transform.position, enemyXp);
        yield return Realtime(.12f);
        Check(lastPopup.Active && lastPopup.Rise > 1f && lastPopup.Rise < GameHud.PopupRise, $"Popup mid-flight: risen {lastPopup.Rise:0.#} of {GameHud.PopupRise} px, opacity {lastPopup.Element.resolvedStyle.opacity:0.00}.");
        GlyphCheck(lastPopup.Element, "Popup");
        yield return Composite(new Vector2Int(1280, 720), "popup-defeat-1280x720", lastPopup.Element);
        yield return Until(() => hud.ActivePopups == 0, 4f, "popups finished");

        // SPACED CONTROL: two defeats 1 s apart at nearly the same spot -> two popups
        Vector3 spot = Ahead(9f); var a1 = Criminal(spot); var a2 = Criminal(spot + CamRight() * 1.2f); yield return Frames(3);
        int merges0 = hud.PopupMerges; started0 = hud.PopupsStarted; total0 = Total(); int id0 = LastPopupId();
        Defeat(a1); yield return Realtime(1f); Defeat(a2); yield return Frames(2);
        var spaced = hud.PopupHistory.Where(r => r.Id > id0).ToList();
        Check(spaced.Count == 2 && spaced.All(r => r.Amount == enemyXp && r.Grants == 1) && hud.PopupMerges == merges0 && Total() - total0 == 2 * enemyXp,
            $"CONTROL: defeats 1.0 s apart ({Vector3.Distance(a1.transform.position, a2.transform.position):0.0} m apart) -> {spaced.Count} separate popups [{string.Join(", ", spaced.Select(r => "+" + r.Amount))}], no merge; XP +{Total() - total0}.");
        yield return LevelUpIfAny("spaced control");
        yield return Until(() => hud.ActivePopups == 0, 4f, "popups finished");

        // RADIUS CONTROL: two defeats in the SAME frame but far apart -> two popups
        var far1 = Criminal(Ahead(7f) - CamRight() * 6f); var far2 = Criminal(Ahead(7f) + CamRight() * 6f); yield return Frames(3);
        merges0 = hud.PopupMerges; id0 = LastPopupId(); float apart = Vector3.Distance(far1.transform.position, far2.transform.position);
        Defeat(far1); Defeat(far2); yield return Frames(2);
        var far = hud.PopupHistory.Where(r => r.Id > id0).ToList();
        Check(apart > GameHud.PopupMergeRadius && far.Count == 2 && hud.PopupMerges == merges0, $"CONTROL: two defeats in one frame {apart:0.0} m apart (> {GameHud.PopupMergeRadius} m) -> {far.Count} popups, no merge.");
        yield return LevelUpIfAny("radius control");
        yield return Until(() => hud.ActivePopups == 0 && hud.BannerIdle, 8f, "popups + banners finished");

        // MERGE: three defeats within 0.3 s at one spot -> ONE popup with the sum (crosses a level -> level-up checks)
        spot = Ahead(9f); var crowd = new[] { Criminal(spot), Criminal(spot + CamRight() * 1f), Criminal(spot - CamRight() * 1f) }; yield return Frames(3);
        Log($"XP before the merge: level {W.Progression.Data.Level}, {W.Progression.Data.Xp}/{W.Progression.RequiredXp}.");
        merges0 = hud.PopupMerges; started0 = hud.PopupsStarted; total0 = Total(); id0 = LastPopupId(); int levelUps0 = hud.LevelUps, level0 = W.Progression.Data.Level;
        var first = crowd[0].transform.position; float t0 = Time.realtimeSinceStartup;
        Defeat(crowd[0]); yield return null; Defeat(crowd[1]); yield return null; Defeat(crowd[2]); float t1 = Time.realtimeSinceStartup;
        yield return null; yield return null;
        var merged = hud.PopupHistory.Where(r => r.Id > id0).ToList(); var mp = NewestSince(id0);
        Check(t1 - t0 < GameHud.PopupMergeSeconds && merged.Count == 1 && merged[0].Amount == 3 * enemyXp && merged[0].Grants == 3 && hud.PopupMerges == merges0 + 2 && mp.Element.text == $"+{3 * enemyXp} XP" && Total() - total0 == 3 * enemyXp,
            $"MERGE: three defeats within {t1 - t0:0.000}s (< {GameHud.PopupMergeSeconds}s) -> ONE popup \"{mp?.Element.text}\" (3 grants) == XP granted {Total() - total0}.");
        ProjectionCheck(mp, first, "Merged popup (anchored at the first defeat)");
        yield return Composite(new Vector2Int(1920, 1080), "popup-merged-1920x1080", mp.Element);
        yield return LevelUpChecks(level0, levelUps0, true);
        Honesty("Hero after the fights");

        // Objective complete: finish the encounter for real (remaining robbers, hazards, last rescue)
        yield return Until(() => hud.BannerIdle && hud.ActivePopups == 0, 8f, "banners idle");
        foreach (var r in e.Robbers.Where(a => a.Npc != null && !a.Npc.Dead && !a.Captured)) { Move(r.Npc.transform.position - CamForward() * 7f); yield return Frames(2); yield return RealAward("Real defeat (robber)", () => Defeat(r.Npc), () => r.Npc.transform.position, enemyXp); yield return LevelUpIfAny("robber"); }
        foreach (var h in e.Hazards) { Move(h.Visual.transform.position); yield return Frames(1); Check(e.Interact(e.Definition.HoldSeconds + .01f) && h.Done, "Real hazard (R hold)."); }
        yield return Until(() => hud.BannerIdle && hud.ActivePopups == 0, 8f, "banners idle");
        var last = e.Civilians.Last(a => !a.Saved); Move(last.Npc.transform.position - CamForward() * 2.5f + CamRight() * .5f); yield return Frames(2);
        Check(!e.Finished && hero.Rules.Complete(e) == false, "Encounter still open before the last rescue.");
        total0 = Total(); id0 = LastPopupId(); flashes0 = hud.HeatFlashes; heat0 = W.Heat; levelUps0 = hud.LevelUps; level0 = W.Progression.Data.Level; int banners0 = hud.BannersStarted;
        var site = e.Site; string name = e.Definition.DisplayName.ToUpperInvariant(); int successXp = mode.SuccessXp;
        Check(e.Interact(e.Definition.HoldSeconds + .01f) && e.Finished, "Real last rescue (R hold) completes the encounter (CrimeEncounter.TryComplete -> GameModeSession.EncounterEnded success).");
        var crime = W.Mode.SpawnNext(); var e2 = crime.Encounter; Hold(e2, 600f);
        Log($"Immediately after the success: GameModeSession.SpawnNext -> {e2.Definition.DisplayName}; its alert must wait for the banner(s).");
        yield return null; yield return null;
        Check(Total() - total0 == successXp && NewestSince(id0) != null && NewestSince(id0).Amount == successXp && Vector3.Distance(NewestSince(id0).Anchor, site) < .01f,
            $"Objective XP popup \"+{successXp} XP\" anchored at the encounter site {V(site)} == XP granted {Total() - total0} (GameModeDefinition.SuccessXp).");
        ProjectionCheck(NewestSince(id0), site, "Objective popup");
        var b = hud.CurrentBanner;
        Check(b != null && b.Kind == GameHud.BannerKind.ObjectiveComplete && hud.BannerTitleLabel.text == name && hud.BannerRewardLabel.text == $"+{successXp} XP" && hud.BannerEyebrowLabel.text == "OBJECTIVE COMPLETE",
            $"Objective-complete banner: \"{hud.BannerEyebrowLabel.text} / {hud.BannerTitleLabel.text} / {hud.BannerRewardLabel.text}\" (name from EncounterDefinition.DisplayName, XP from the definition).");
        Check(Same(hud.BannerEyebrowLabel.resolvedStyle.color, Accent(PlayerSide.Hero)) && Same(hud.Banner.resolvedStyle.borderBottomColor, Accent(PlayerSide.Hero)), "Banner in the Hero side colour.");
        string successHeat = HeatDelta(heat0, mode.SuccessHeat);
        if (heat0 > 0f) Check(hud.HeatFlashes == flashes0 + 1 && hud.HeatDeltaText == successHeat, $"Success Heat {mode.SuccessHeat} -> flash \"{hud.HeatDeltaText}\" (expected {successHeat}).");
        Check(!hud.AlertShowing && hud.AlertsStarted == 0, "The new encounter's alert is QUEUED behind the banner (not showing).");
        foreach (var r in Resolutions) yield return Composite(r, $"objective-complete-banner-{r.x}x{r.y}", hud.Banner);
        int overlapFrames = 0; float until = Time.realtimeSinceStartup + 12f;
        while (!hud.AlertShowing && Time.realtimeSinceStartup < until) { yield return null; if (hud.AlertShowing && hud.CurrentBanner != null) overlapFrames++; }
        var objEntry = hud.Timeline.First(t => t.Kind == "banner:ObjectiveComplete"); var alertEntry = hud.Timeline.First(t => t.Kind == "alert");
        Check(hud.AlertShowing && hud.AlertEncounter == e2 && overlapFrames == 0 && objEntry.EndedAt > 0 && alertEntry.StartedAt >= objEntry.EndedAt && objEntry.EndedAt - objEntry.StartedAt >= GameHud.BannerHoldSeconds,
            $"ORDER: objective banner {objEntry.StartedAt:0.00}-{objEntry.EndedAt:0.00} (held {objEntry.EndedAt - objEntry.StartedAt:0.00}s), THEN the alert '{alertEntry.Name}' at {alertEntry.StartedAt:0.00}; 0 frames with both.");
        if (hud.LevelUps > levelUps0) Log("NOTE: the success XP also levelled up; that banner is part of the checked order.");
        TimelineCheck("Hero after success");
        yield return Until(() => hud.AlertsFinished == 1, 8f, "alert finished");
        Honesty("Hero after the objective");

        // Failure banner (muted): a lost civilian fails the new encounter through its own rules
        var lost = e2.Civilians.First(a => a.Npc != null && !a.Npc.Dead);
        yield return Until(() => hud.BannerIdle && !hud.AlertShowing, 8f, "banners idle");
        flashes0 = hud.HeatFlashes; heat0 = W.Heat; total0 = Total(); started0 = hud.PopupsStarted;
        lost.Npc.Damage(100000f, null); Log("TEST HARNESS: one of the new encounter's civilians is killed with CityNpc.Damage(source null) — standing in for the civilian-danger timer — so HeroModeRules.Failed ends it on its next Tick.");
        yield return Until(() => e2 == null || e2.Finished, 3f, "encounter failure");
        yield return null; yield return null;
        b = hud.CurrentBanner;
        Check(b != null && b.Kind == GameHud.BannerKind.ObjectiveFailed && hud.BannerEyebrowLabel.text == "OBJECTIVE FAILED" && hud.BannerTitleLabel.text == e2.Definition.DisplayName.ToUpperInvariant() && !GameHud.Shown(hud.BannerRewardLabel) && hud.BannerDetailLabel.text.Length > 0,
            $"Failure banner: \"{hud.BannerEyebrowLabel.text} / {hud.BannerTitleLabel.text} / {hud.BannerDetailLabel.text}\", no reward chip.");
        Check(Same(hud.BannerEyebrowLabel.resolvedStyle.color, Muted) && Same(hud.Banner.resolvedStyle.borderBottomColor, Muted), "Failure banner is MUTED (UiMuted), not the side colour.");
        yield return Realtime(.3f);
        Check(!GameHud.Shown(hud.ObjectiveBody) || !hud.ObjectiveBody.text.Contains(hud.BannerDetailLabel.text.Length > 0 ? W.Mode.Feedback : "\u0000"), $"While the result banner is up the objective line does not repeat the result (body \"{hud.ObjectiveBody.text}\"; message line {(GameHud.Shown(hud.MessageLine) ? "\"" + hud.MessageLine.text + "\"" : "hidden")}).");
        Check(!GameHud.Shown(hud.MessageLine) || hud.MessageLine.text != W.Mode.Feedback, "The bottom message line does not repeat the result either.");
        Check(Total() == total0 && hud.PopupsStarted == started0, "CONTROL: failure grants no XP -> no popup.");
        Check(hud.HeatFlashes == flashes0 + 1 && hud.HeatDeltaText == HeatDelta(heat0, mode.FailureHeat), $"Failure Heat -> flash \"{hud.HeatDeltaText}\".");
        yield return Composite(new Vector2Int(1920, 1080), "objective-failed-banner-1920x1080", hud.Banner);
        yield return Until(() => hud.BannerIdle, 6f, "failure banner finished");

        // Enlarged waypoint on a fresh encounter (after its alert)
        yield return WaypointChecks();
        TimelineCheck("Hero session");
        Honesty("Hero session");
        yield return Home();
    }

    IEnumerator LevelUpIfAny(string label) { if (hud.LevelUps > seenLevelUps) yield return LevelUpChecks(W.Progression.Data.Level - 1, seenLevelUps, false); }
    int seenLevelUps;
    /// Level-up: burst on the badge + "LEVEL UP — TAB TO UPGRADE" banner; timeScale stays 1, the session keeps running.
    IEnumerator LevelUpChecks(int levelBefore, int levelUpsBefore, bool capture)
    {
        Check(hud.LevelUps == levelUpsBefore + 1 && W.Progression.Data.Level == levelBefore + 1, $"Real XP crossed a level: {levelBefore} -> {W.Progression.Data.Level} (PlayerProgression.LevelUp raised once).");
        seenLevelUps = hud.LevelUps;
        float peak = 1f; bool ring = false; float until = Time.realtimeSinceStartup + GameHud.LevelBurstSeconds + .2f;
        while (Time.realtimeSinceStartup < until) { peak = Mathf.Max(peak, hud.LevelBadge.resolvedStyle.scale.value.x); ring |= GameHud.Shown(hud.LevelRing); if (Time.timeScale != 1f) throw new Exception("timeScale changed during the level burst: " + Time.timeScale); yield return null; }
        Check(peak > 1.1f && ring, $"Level badge burst: badge scale peaked at {peak:0.00}, ring shown.");
        yield return Until(() => hud.CurrentBanner != null && hud.CurrentBanner.Kind == GameHud.BannerKind.LevelUp, 8f, "level-up banner");
        int points = W.Progression.Data.Points;
        string title = $"LEVEL UP {hud.Dash} {HudBindings.KeyName(HudBindings.MenuKey)} TO UPGRADE";
        Check(hud.BannerTitleLabel.text == title && hud.BannerTitleLabel.text.Contains("TAB") && hud.BannerEyebrowLabel.text == $"LEVEL {W.Progression.Data.Level}" && hud.BannerRewardLabel.text == $"{points} POINT{(points == 1 ? "" : "S")}",
            $"Level-up banner \"{hud.BannerEyebrowLabel.text} / {hud.BannerTitleLabel.text} / {hud.BannerRewardLabel.text}\" (key from HudBindings.MenuKey = WorldSession's Tab toggle).");
        GlyphCheck(hud.BannerTitleLabel, "Level-up banner");
        // The session keeps running: timeScale 1 every frame, not paused, the session clock and unfrozen NPCs keep moving.
        var movers = W.Npcs.Where(n => n != null && !n.Dead && n.Encounter == null && n.Agent != null && n.Agent.enabled && !n.Agent.isStopped).ToList();
        var from = movers.ToDictionary(n => n, n => n.transform.position); float elapsed0 = W.Mode.Elapsed, real0 = Time.realtimeSinceStartup; int frames = 0, bad = 0;
        if (capture) foreach (var r in Resolutions) yield return Composite(r, $"level-up-banner-{r.x}x{r.y}", hud.Banner);
        until = Time.realtimeSinceStartup + .8f;
        while (Time.realtimeSinceStartup < until) { frames++; if (Time.timeScale != 1f || W.Mode.Paused || W.MenuOpen) bad++; yield return null; }
        int moved = movers.Count(n => n != null && Vector3.Distance(from[n], n.transform.position) > .3f);
        float sim = W.Mode.Elapsed - elapsed0, real = Time.realtimeSinceStartup - real0;
        Check(bad == 0 && Time.timeScale == 1f && !W.Mode.Paused && moved > 0 && sim > real * .5f,
            $"Never pauses: timeScale 1 on all {frames} sampled frames, session not paused, session clock advanced {sim:0.00}s in {real:0.00}s real, {moved}/{movers.Count} free NPCs moved > 0.3 m during the banner.");
    }

    // ---- enlarged waypoint (on/off-screen) + analytic crosshair clearance
    IEnumerator WaypointChecks()
    {
        var crime = W.Mode.SpawnNext(); Check(crime != null, "Real spawn for the waypoint checks: " + crime?.Encounter?.Definition.DisplayName);
        var e = crime.Encounter; Hold(e, 600f); e.enabled = false;
        yield return Until(() => hud.AlertsFinished >= 2 && hud.ObjectiveEncounter == e, 12f, "alert done + objective on the new encounter");
        yield return Realtime(.4f);
        EncounterActor Nearest(Vector3 from) => e.Robbers.Where(a => !a.Captured && !a.Escaped && a.Npc != null && !a.Npc.Dead).OrderBy(a => (a.Npc.transform.position - from).sqrMagnitude).FirstOrDefault();
        var near = Nearest(W.Hero.transform.position);
        Move(near.Npc.transform.position - Vector3.forward * 6f - Vector3.right * 2.5f); yield return Grounded(); yield return Realtime(.5f);
        Check(hud.WaypointVisible && hud.WaypointOnScreen && GameHud.Shown(hud.Waypoint), "Waypoint ON-SCREEN marker shown.");
        foreach (var r in Resolutions)
        {
            yield return Composite(r, $"waypoint-onscreen-{r.x}x{r.y}", hud.Waypoint);
            if (r.x == 1280) WaypointSizeCheck(r, true);
        }
        // Marker box honesty: the drawn marker fits the box used for group-avoidance and the crosshair radius
        // Evaluated with the panels at a real capture resolution (1920x1080, settled layout). Right after a capture the
        // panels fall back to the 4:3 batch-mode screen, where one physical pixel is > 1 logical px, so pixel snapping
        // alone can exceed the 0.5 px tolerance (seen once the Phase 4 camera moved the marker: 935 vs 936.2).
        var honesty = new RenderTexture(1920, 1080, 24) { name = "HUD3 marker-box check" }; honesty.Create(); var hcam = Cam();
        hud.Panel.targetTexture = honesty; hud.OverlayPanel.targetTexture = honesty; hcam.aspect = 16f / 9f; yield return Frames(3);
        var box = GameHud.WaypointMarkerBox; var drawn = hud.Waypoint.worldBound; var declared = new Rect(hud.WaypointPoint + box.position, box.size);
        Check(drawn.xMin >= declared.xMin - .5f && drawn.xMax <= declared.xMax + .5f && drawn.yMin >= declared.yMin - .5f && drawn.yMax <= declared.yMax + .5f, $"Drawn marker {R(drawn)} fits its declared box {R(declared)} (1920x1080 panels).");
        hcam.ResetAspect(); hud.Panel.targetTexture = null; hud.OverlayPanel.targetTexture = null; honesty.Release(); Destroy(honesty); yield return Frames(2);
        var cross = hud.Crosshair.worldBound; var c = cross.center; int hits = 0;
        for (int i = 0; i < 72; i++)
        {
            var dir = new Vector2(Mathf.Sin(i * 5f * Mathf.Deg2Rad), -Mathf.Cos(i * 5f * Mathf.Deg2Rad)); var p = c + dir * GameHud.CrosshairClearRadius;
            if (new Rect(p + box.position, box.size).Overlaps(cross)) hits++;
        }
        Check(hits == 0 && cross.width <= 2f * GameHud.CrosshairHalf, $"Crosshair clearance: the enlarged marker box {R(box)} pushed to radius {GameHud.CrosshairClearRadius} clears the crosshair {R(cross)} in all 72 directions (5° steps).");
        near = Nearest(W.Hero.transform.position);
        Move(near.Npc.transform.position + Vector3.forward * 14f + Vector3.right * 3f); yield return Grounded(); yield return Realtime(.5f);
        Check(hud.WaypointVisible && !hud.WaypointOnScreen && GameHud.Shown(hud.WaypointEdge), $"Target behind: edge arrow shown ({hud.WaypointArrowAngle:0.0}°).");
        foreach (var r in Resolutions)
        {
            yield return Composite(r, $"waypoint-offscreen-{r.x}x{r.y}", hud.WaypointEdge);
            if (r.x == 1280) WaypointSizeCheck(r, false);
        }
    }
    void WaypointSizeCheck(Vector2Int size, bool marker)
    {
        // Measured at the capture resolution: panel units x the panel scale = screen pixels.
        float scale = Mathf.Min(size.x / (float)GameHud.ReferenceResolution.x, size.y / (float)GameHud.ReferenceResolution.y);
        if (marker)
        {
            float font = hud.WaypointDistanceLabel.resolvedStyle.fontSize * scale, glyph = hud.WaypointDistanceLabel.worldBound.height * scale, diamond = hud.WaypointMarkerIcon.worldBound.width * scale;
            Check(font >= 16f && diamond >= 34f, $"{size.x}x{size.y}: distance label \"{hud.WaypointDistanceLabel.text}\" {font:0.#} px font ({glyph:0.#} px line; Phase 2 was 14 x {scale:0.##} = {14 * scale:0.#} px), count {hud.WaypointCountLabel.resolvedStyle.fontSize * scale:0.#} px, diamond {diamond:0.#} px (was {30 * scale:0.#}).");
        }
        else
        {
            var edgeLabel = hud.WaypointEdge.Q<Label>("waypoint-edge-distance");
            float font = edgeLabel.resolvedStyle.fontSize * scale, arrow = hud.WaypointArrow.worldBound.width * scale;
            Check(font >= 16f && arrow >= 40f, $"{size.x}x{size.y}: edge label \"{edgeLabel.text}\" {font:0.#} px font (was {12 * scale:0.#}), arrow {arrow:0.#} px (was {34 * scale:0.#}).");
        }
    }

    // ---- Villain: destruction popups (+12) with heat flash at 1280 + 2560, same-frame double destruction (merged popup
    //      and merged heat delta), positionless fallback (rooftop) near the level badge, villain objective banner colour.
    IEnumerator VillainFlow()
    {
        var mode = Clone(villain, "villain phase3 clone (no timed spawns)");
        yield return Launch(mode);
        var e = FirstEncounter(); Hold(e, 600f); e.enabled = false; Log("TEST HARNESS: villain encounter clock paused (enabled=false).");
        int destructionXp = W.Tuning.Progression.DestructionXp;
        var props = e.Props.Where(p => p != null).ToList(); Check(props.Count >= 3, $"Villain encounter {e.Definition.DisplayName} has {props.Count} breakable props.");
        Log("TEST HARNESS: hostile police are frozen (CityNpc.Freeze) before each Villain step so gunfire cannot kill the player mid-check.");
        foreach (var r in new[] { Resolutions[0], Resolutions[2] })
        {
            CalmPolice();
            var prop = e.Props.Where(p => p != null).OrderBy(p => (p.position - W.Hero.transform.position).sqrMagnitude).First();
            Move(prop.position - CamForward() * 9f + CamRight() * 2f); yield return Grounded(); yield return Realtime(.6f);
            int flashes0 = hud.HeatFlashes; float heat0 = W.Heat; string expect = HeatDelta(heat0, W.Tuning.Heat.DestructionHeat);
            // Event position = what BreakableProp reports (its transform). A still-settling prop's interpolated transform can
            // trail Rigidbody.position by centimetres (seen: 0.046 m), so the rigidbody position is not the event position.
            Log($"{prop.name}: rigidbody vs transform before the break: {Vector3.Distance(prop.position, prop.transform.position):0.0000} m, speed {prop.linearVelocity.magnitude:0.000} m/s.");
            yield return RealAward($"Real destruction ({prop.name})", () => prop.GetComponent<BreakableProp>().TakeDamage(100000f, W.Powers), () => prop.transform.position, destructionXp);
            Check(hud.HeatFlashes == flashes0 + 1 && hud.HeatDeltaText == expect && GameHud.Shown(hud.HeatDelta), $"Same destruction: Heat flash \"{hud.HeatDeltaText}\" (Heat {heat0:0.00} -> {W.Heat:0.00}).");
            yield return Realtime(.1f);
            yield return Composite(r, $"popup-heat-destruction-{r.x}x{r.y}", lastPopup.Element, hud.HeatDelta);
            yield return LevelUpIfAny("destruction");
            yield return Until(() => hud.ActivePopups == 0 && !hud.HeatFlashing, 4f, "popup + flash finished");
        }
        // Same-frame double destruction next to each other: ONE popup +24, ONE heat flash with the summed delta
        var city = FindObjectsByType<BreakableProp>().Where(p => p != null && p.GetComponent<EncounterProp>() == null).ToList();
        var pair = city.SelectMany(p => city.Where(q => q != p && Vector3.Distance(p.transform.position, q.transform.position) < GameHud.PopupMergeRadius * .8f).Select(q => (p, q)))
            .OrderBy(t => (t.p.transform.position - W.Hero.transform.position).sqrMagnitude).FirstOrDefault();
        CalmPolice();
        if (pair.p != null)
        {
            Move(pair.p.transform.position - CamForward() * 9f); yield return Grounded(); yield return Realtime(.6f);
            int started0 = hud.PopupsStarted, merges0 = hud.PopupMerges, flashes0 = hud.HeatFlashes, heatMerges0 = hud.HeatFlashMerges, total0 = Total(), id0 = LastPopupId(); float heat0 = W.Heat;
            float expectHeat = Mathf.Min(heat0 + 2 * W.Tuning.Heat.DestructionHeat, W.Tuning.Heat.MaximumStars) - heat0;
            float pairApart = Vector3.Distance(pair.p.transform.position, pair.q.transform.position);
            pair.p.TakeDamage(100000f, W.Powers); pair.q.TakeDamage(100000f, W.Powers); yield return null; yield return null;
            var rec = hud.PopupHistory.Where(r => r.Id > id0).ToList();
            Check(rec.Count == 1 && rec[0].Amount == 2 * destructionXp && hud.PopupMerges == merges0 + 1 && Total() - total0 == 2 * destructionXp,
                $"Two props broken in the same frame {pairApart:0.0} m apart -> ONE popup +{rec.FirstOrDefault().Amount} XP == granted {Total() - total0}.");
            Check(hud.HeatFlashes == flashes0 + 1 && hud.HeatFlashMerges == heatMerges0 + 1 && hud.HeatDeltaText == "+" + expectHeat.ToString("0.##"), $"...and ONE heat flash with the summed delta \"{hud.HeatDeltaText}\" (two AddHeat calls).");
            yield return LevelUpIfAny("double destruction");
        }
        else Log("NOTE: no two city props within the merge radius; the double-destruction merge is not exercised.");
        yield return Until(() => hud.ActivePopups == 0, 4f, "popups finished");

        // Positionless fallback: the rooftop pickup (PlayerProgression.ClaimRoof, called by CityDistrict's roof trigger)
        CalmPolice(); Move(W.City.Spawn + Vector3.up * W.Tuning.Movement.Height); yield return Grounded(); yield return Realtime(.6f);   // open street, camera clear of walls
        int before = hud.PopupsStarted, t0x = Total(), idx = LastPopupId();
        Check(W.Progression.ClaimRoof("hud-phase3-roof"), "Real rooftop claim (PlayerProgression.ClaimRoof).");
        yield return null; yield return null;
        var fp = NewestSince(idx); var g = hud.LevelGroup.layout;
        Check(fp != null && !fp.Anchored && fp.Amount == Total() - t0x && fp.Amount == W.Tuning.Progression.RooftopXp && fp.Element.worldBound.xMin > g.xMax && Mathf.Abs(fp.BasePoint.y - g.center.y) < .5f && GameHud.Shown(fp.Element),
            $"Positionless XP (+{fp?.Amount}) -> fallback popup right of the level group at {fp?.BasePoint}, label {R(fp.Element.worldBound)} clear of the group {R(g)}.");
        yield return Composite(new Vector2Int(1920, 1080), "popup-fallback-level-badge-1920x1080", fp.Element);
        yield return LevelUpIfAny("rooftop");
        yield return Until(() => hud.ActivePopups == 0 && hud.BannerIdle, 8f, "popups finished");

        // Villain objective: loot, wreck, escape -> banner in the villain colour + objective popup
        CalmPolice(); Check(!W.PlayerDead, $"Player alive (health {W.Health:0}).");
        foreach (var node in e.Loot) { CalmPolice(); Move(node.Visual.transform.position); yield return Frames(1); Check(e.Interact(e.Definition.HoldSeconds + .01f) && node.Done, "Real loot grab (R hold)."); }
        while (e.DestroyedProps < e.Definition.DestructionGoal)
        {
            var prop = e.Props.First(p => p != null); var at = prop.position;
            yield return RealAward("Real destruction (wreck task)", () => prop.GetComponent<BreakableProp>().TakeDamage(100000f, W.Powers), () => at, destructionXp);
            yield return LevelUpIfAny("wreck");
        }
        yield return Until(() => hud.BannerIdle, 8f, "banners idle");
        CalmPolice(); Move(W.City.NearestSidewalk(e.Site + Vector3.right * (e.Definition.PlayerEscapeDistance + 6f))); yield return Grounded();
        yield return Realtime(GameHud.PopupMergeSeconds + .05f);   // not merged into the last wreck popup
        int total1 = Total(), id1 = LastPopupId();
        Check(e.TryComplete() && e.Finished, $"Escape past {e.Definition.PlayerEscapeDistance} m -> CrimeEncounter.TryComplete (what its Tick calls) succeeds.");
        yield return null; yield return null;
        Check(Total() - total1 == mode.SuccessXp && hud.PopupHistory.Where(r => r.Id > id1).Sum(r => r.Amount) == mode.SuccessXp, $"Villain objective XP +{mode.SuccessXp} shown as a popup at the site (on screen: {NewestSince(id1)?.OnScreen}).");
        Check(hud.CurrentBanner != null && hud.CurrentBanner.Kind == GameHud.BannerKind.ObjectiveComplete && hud.BannerTitleLabel.text == e.Definition.DisplayName.ToUpperInvariant() && hud.BannerRewardLabel.text == $"+{mode.SuccessXp} XP"
            && Same(hud.BannerEyebrowLabel.resolvedStyle.color, Accent(PlayerSide.Villain)), $"Villain objective banner \"{hud.BannerTitleLabel.text} {hud.BannerRewardLabel.text}\" in the VILLAIN colour.");
        yield return Composite(new Vector2Int(1920, 1080), "objective-complete-villain-1920x1080", hud.Banner);
        yield return LevelUpIfAny("villain objective");
        TimelineCheck("Villain session");
        Honesty("Villain session");
        yield return Home();
    }

    // ---- Endless: separate popups for spread kills, ONE popup for a packed crowd, wave-clear banner from director values
    IEnumerator EndlessFlow()
    {
        yield return Launch(endless);
        var state = W.Mode.Director as EndlessWaveState; Check(state != null, "Endless director state.");
        yield return Until(() => !state.Intermission && state.Wave == 1 && state.Alive.Count == state.Tuning.WaveSize(1), 12f, "wave 1");
        foreach (var n in state.Alive) n.Freeze(60f);
        int id0 = LastPopupId(), total0 = Total(), score0 = W.Mode.Score, banners0 = hud.BannersStarted;
        var wave1 = state.Alive.ToList(); for (int i = 0; i < wave1.Count; i++) { Defeat(wave1[i]); yield return null; }
        yield return Frames(2);
        var recs = hud.PopupHistory.Where(r => r.Id > id0).ToList();
        Check(recs.Sum(r => r.Amount) == Total() - total0 && Total() - total0 == wave1.Count * W.Tuning.Progression.EnemyXp, $"Wave 1: {wave1.Count} spread-out defeats -> popups [{string.Join(", ", recs.Select(r => "+" + r.Amount + "x" + r.Grants))}] sum == XP granted {Total() - total0}.");
        yield return Until(() => hud.CurrentBanner != null && hud.CurrentBanner.Kind == GameHud.BannerKind.Milestone, 6f, "wave-clear banner");
        int wave = state.Wave, bonus = state.Tuning.WaveClearBonus * wave;
        Check(hud.BannerTitleLabel.text == $"WAVE {wave} CLEARED" && hud.BannerRewardLabel.text == $"+{bonus} SCORE" && hud.BannerEyebrowLabel.text == $"{wave1.Count} DEFEATED" && W.Mode.Score - score0 >= bonus,
            $"Wave-clear banner \"{hud.BannerEyebrowLabel.text} / {hud.BannerTitleLabel.text} / {hud.BannerRewardLabel.text}\" == director values (WaveClearBonus {state.Tuning.WaveClearBonus} x wave {wave}); score +{W.Mode.Score - score0}.");
        yield return Composite(new Vector2Int(1920, 1080), "wave-clear-banner-1920x1080", hud.Banner);
        yield return LevelUpIfAny("wave 1");

        // Wave 2: pack the crowd at one spot (harness) and defeat it within 0.3 s -> ONE popup
        yield return Until(() => !state.Intermission && state.Wave == 2 && state.Alive.Count == state.Tuning.AliveTarget(2), 15f, "wave 2");
        Vector3 spot = Ahead(10f); NavMesh.SamplePosition(spot, out var hit, 3f, NavMesh.AllAreas);
        var crowd = state.Alive.ToList(); for (int i = 0; i < crowd.Count; i++) { crowd[i].Freeze(60f); crowd[i].Agent.Warp(hit.position + Quaternion.Euler(0, i * 72f, 0) * Vector3.forward * 1.2f); }
        Log($"TEST HARNESS: wave-2 crowd ({crowd.Count}) frozen and warped (NavMeshAgent.Warp) into a 1.2 m ring {V(hit.position)} in front of the player.");
        yield return Frames(2);
        id0 = LastPopupId(); total0 = Total(); int merges0 = hud.PopupMerges; float t0 = Time.realtimeSinceStartup;
        foreach (var n in crowd) { Defeat(n); yield return null; }
        float took = Time.realtimeSinceStartup - t0; yield return null;
        recs = hud.PopupHistory.Where(r => r.Id > id0).ToList();
        Check(took < GameHud.PopupMergeSeconds && recs.Count == 1 && recs[0].Amount == Total() - total0 && recs[0].Grants == crowd.Count && hud.PopupMerges - merges0 == crowd.Count - 1,
            $"Endless crowd: {crowd.Count} defeats in {took:0.000}s at one spot -> ONE popup +{recs.FirstOrDefault().Amount} XP ({recs.FirstOrDefault().Grants} grants) == XP granted {Total() - total0}; no spam.");
        var cp = NewestSince(id0); yield return Realtime(.1f);
        yield return Composite(new Vector2Int(2560, 1080), "popup-endless-crowd-2560x1080", cp.Element);
        yield return LevelUpIfAny("wave 2 crowd");
        TimelineCheck("Endless session");
        Honesty("Endless session");
        yield return Home();
    }

    // ---- IDLE CONTROL: 30 s with no input and no events; Heat decays; no popups, no banners, no alerts; the only heat
    //      flashes are star-count drops (exactly one each), never the continuous per-frame decay.
    IEnumerator IdleControl()
    {
        var mode = Clone(hero, "hero idle clone (no encounters, no briefing)", false); mode.InitialEncounters = 0;
        yield return Launch(mode, false);
        Check(!hud.BriefingActive && W.Crimes.Count == 0, "Idle clone: no briefing, no encounters.");
        yield return Grounded();
        var props = FindObjectsByType<BreakableProp>().OrderBy(p => (p.transform.position - W.Hero.transform.position).sqrMagnitude).Take(2).ToList();
        foreach (var p in props) p.TakeDamage(100000f, W.Powers);
        yield return Frames(2);
        Log($"Heat raised by {props.Count} real Hero-side destructions (0 XP): Heat {W.Heat:0.00} ({W.Stars} star(s)); decay starts {W.Tuning.Heat.DecayDelay}s later at {W.Tuning.Heat.DecayPerSecond}/s.");
        yield return Until(() => !hud.HeatFlashing, 3f, "flash over");
        int popups0 = hud.PopupsStarted, banners0 = hud.BannersStarted, alerts0 = hud.AlertsStarted, flashes0 = hud.HeatFlashes, xp0 = Total(), queued0 = hud.BannersQueued;
        float heatStart = W.Heat, until = Time.realtimeSinceStartup + 30f; int frames = 0, decayFrames = 0, quietDecayFrames = 0, drops = 0, flashOnDrop = 0, flashOffDrop = 0, lastStars = W.Stars; float lastHeat = W.Heat; int lastFlashes = hud.HeatFlashes;
        var dropTexts = new List<string>(); int sinceDrop = 1000; bool awaitingFlash = false;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null; frames++;
            // The HUD reacts in LateUpdate, after this coroutine has sampled the frame, so a drop's flash may be seen up to
            // two samples later; any flash outside that window counts against decay.
            bool decayed = W.Heat < lastHeat - 1e-6f, dropped = W.Stars < lastStars; int newFlashes = hud.HeatFlashes - lastFlashes;
            if (dropped) { drops++; sinceDrop = 0; awaitingFlash = true; } else sinceDrop++;
            if (decayed) decayFrames++;
            if (newFlashes > 0)
            {
                if (awaitingFlash && sinceDrop <= 2 && newFlashes == 1) { flashOnDrop++; awaitingFlash = false; dropTexts.Add(hud.HeatDeltaText); }
                else flashOffDrop += newFlashes;
            }
            else if (decayed && !dropped && sinceDrop > 2) quietDecayFrames++;
            lastHeat = W.Heat; lastStars = W.Stars; lastFlashes = hud.HeatFlashes;
        }
        Check(hud.PopupsStarted == popups0 && Total() == xp0, $"IDLE CONTROL: 30 s, {frames} frames, no input -> ZERO popups (XP unchanged).");
        Check(hud.BannersStarted == banners0 && hud.BannersQueued == queued0 && hud.AlertsStarted == alerts0, "IDLE CONTROL: ZERO banners, ZERO alerts.");
        Check(W.Heat < heatStart && decayFrames > 100, $"IDLE CONTROL: Heat decayed {heatStart:0.00} -> {W.Heat:0.00} over {decayFrames} frames.");
        Check(quietDecayFrames > 100 && flashOffDrop == 0, $"IDLE CONTROL: {quietDecayFrames} frames of decay WITHOUT a star change produced ZERO heat flashes (flashes outside a star drop: {flashOffDrop}).");
        Check(flashOnDrop == drops && hud.HeatFlashes - flashes0 == drops && dropTexts.All(t => t == hud.Minus + "1*"),
            $"IDLE CONTROL: the only flashes are the {drops} star-count drop(s) through decay, exactly one each (\"{string.Join(", ", dropTexts)}\" — '*' = the star glyph element). " +
            $"NOTE: with DecayDelay {W.Tuning.Heat.DecayDelay}s and {W.Tuning.Heat.DecayPerSecond}/s, ANY 30 s window that decays loses >= {(30f - W.Tuning.Heat.DecayDelay) * W.Tuning.Heat.DecayPerSecond:0.0} Heat and must cross a star boundary.");
        Check(drops >= 1, "A star-count drop via decay flashed once.");
        yield return Home();
    }

    // ---------------------------------------------------------------- Reload (separate Unity process)
    IEnumerator ReloadChecks()
    {
        LoadModes();
        yield return Scene(GameFlow.HomeScene);
        Log("Separate process: pid " + System.Diagnostics.Process.GetCurrentProcess().Id + ", save " + MainSave);
        var expected = File.ReadAllText(ExpectedFile).Split(' ').Select(int.Parse).ToArray(); var d = ui.Profile.Data;
        Check(ui.Profile.LastError == null && d.Level == expected[0] && d.Xp == expected[1] && d.Points == expected[2] && d.Level > 1,
            $"Reloaded save carries the progression Run earned: level {d.Level}, XP {d.Xp}, points {d.Points} (expected {string.Join(" ", expected)}).");
        var mode = Clone(hero, "hero reload clone", false); mode.InitialEncounters = 0;
        yield return Launch(mode, false);
        yield return Grounded(); yield return Realtime(3f);
        Check(hud.LevelUps == 0 && hud.BannersStarted == 0 && hud.PopupsStarted == 0 && !hud.LevelBursting, "CONTROL: loading a levelled save replays NO level-up burst/banner and NO popup (3 s).");
        var npc = Criminal(Ahead(8f)); yield return Frames(3);
        yield return RealAward("Reload process: real defeat", () => Defeat(npc), () => npc.transform.position, W.Tuning.Progression.EnemyXp);
        Honesty("Reload session");
        yield return Home();
        foreach (var c in clones) if (c != null) Destroy(c);
    }
}
#endif
