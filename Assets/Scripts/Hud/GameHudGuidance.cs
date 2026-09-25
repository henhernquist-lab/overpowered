using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// HUD Phase 2, "make the goal obvious": mission briefing, data-driven objective line, waypoint, encounter alerts and
/// first-time control prompts.
/// The briefing, waypoint, alerts and prompts live on a second OVERLAY panel (same scaling, drawn above the edge HUD):
/// the briefing is the one element allowed centre-screen (only while shown) and the waypoint tracks the world, so neither
/// belongs to the edges-only main panel. The objective line stays in the main panel's top-centre slot.
/// Every animation uses unscaled time.
public sealed partial class GameHud
{
    public const float BriefingMinSeconds = .3f, BriefingFadeSeconds = .25f;
    public const float AlertSlideSeconds = .35f, AlertHoldSeconds = 3.2f, ObjectivePulseSeconds = .6f;
    /// Prompt contexts: a hostile this close shows the melee prompt.
    public const float PunchPromptRange = 8f;
    /// A shown prompt stays at least this long after its context ends (unless its action is performed), so it can be read.
    public const float PromptMinSeconds = 1.5f;
    /// Waypoint: world height above the target, the radius around screen centre it is pushed out of (crosshair), and
    /// the inset band the on-screen marker / edge arrow is kept inside (clear of the corner and top/bottom groups).
    public const float WaypointHeight = 2.3f, CrosshairClearRadius = 72f, WaypointInsetX = 80f, WaypointInsetTop = 150f, WaypointInsetBottom = 175f;
    public static readonly string[] PromptIds = { "move", "punch", "dodge", "interact" };

    public PanelSettings OverlayPanel { get; private set; }
    public UIDocument OverlayDocument { get; private set; }
    public VisualElement OverlayRoot { get; private set; }

    // ---- briefing
    public VisualElement Briefing { get; private set; }
    public Label BriefingEyebrow { get; private set; }
    public Label BriefingGoalLabel { get; private set; }
    public Label BriefingWinLabel { get; private set; }
    public Label BriefingLoseLabel { get; private set; }
    public VisualElement BriefingWinRow { get; private set; }
    public VisualElement BriefingLoseRow { get; private set; }
    public VisualElement BriefingTimerFill { get; private set; }
    public Color BriefingAccent { get; private set; }
    /// How the last briefing ended ("input: ...", "timeout", "session ended") and how many briefings were shown.
    public string BriefingDismissedBy { get; private set; }
    public int BriefingShows { get; private set; }
    public bool BriefingActive => briefingActive;

    // ---- objective (main panel) + waypoint (overlay)
    public CrimeEncounter ObjectiveEncounter { get; private set; }
    public ObjectiveStep CurrentStep { get; private set; }
    public Label ObjectiveHeader => objectiveHeader;
    public Label ObjectiveMeta => objectiveMeta;
    public VisualElement Waypoint { get; private set; }
    public VisualElement WaypointEdge { get; private set; }
    public VisualElement WaypointMarkerIcon { get; private set; }
    public Label WaypointDistanceLabel { get; private set; }
    public Label WaypointCountLabel { get; private set; }
    public HudArrow WaypointArrow { get; private set; }
    public bool WaypointVisible { get; private set; }
    public bool WaypointOnScreen { get; private set; }
    public Vector3 WaypointTarget { get; private set; }
    /// Edge-arrow direction in screen space: degrees clockwise from straight up.
    public float WaypointArrowAngle { get; private set; }
    public int WaypointRemaining { get; private set; }
    public float WaypointMetres { get; private set; }
    /// Panel-space point the marker / arrow is drawn at, and whether it was pushed out of the crosshair radius.
    public Vector2 WaypointPoint { get; private set; }
    public bool WaypointPushed { get; private set; }
    /// True when the marker / arrow was slid toward the centre to keep it off an edge HUD group or card.
    public bool WaypointShifted { get; private set; }

    // ---- alerts (overlay)
    public VisualElement AlertCard { get; private set; }
    public Label AlertNameLabel { get; private set; }
    public Label AlertDistanceLabel { get; private set; }
    public HudArrow AlertArrow { get; private set; }
    public CrimeEncounter AlertEncounter { get; private set; }
    /// Direction to the alerted encounter relative to the camera's heading: degrees clockwise, 0 = straight ahead.
    public float AlertArrowAngle { get; private set; }
    public int AlertsStarted { get; private set; }
    public int AlertsFinished { get; private set; }
    public bool AlertShowing => AlertEncounter != null;

    // ---- prompts (overlay)
    public VisualElement PromptCard { get; private set; }
    public string PromptId { get; private set; }
    public string PromptText { get; private set; }
    /// Prompt ids shown (and completed by the real action) during this HUD's lifetime.
    public readonly List<string> PromptsShown = new List<string>(), PromptsCompleted = new List<string>();

    Label briefingMode; MenuIcon briefingGhost;
    HudDiamond waypointDiamond; Label waypointEdgeDistance;
    Label alertEyebrow;
    VisualElement promptRow;
    bool briefingActive; float briefingElapsed, briefingOpenedAt, briefingClosedAt = -100f; GameModeSession briefedSession;
    readonly Queue<CrimeEncounter> alertQueue = new Queue<CrimeEncounter>();
    readonly HashSet<CrimeEncounter> alertPending = new HashSet<CrimeEncounter>();
    float alertStartedAt, objectivePulseAt = -100f, nextStep, nextPromptScan, promptShownAt;
    readonly List<Vector3> targets = new List<Vector3>();
    SuperHeroController boundHero; string promptCandidate; GameObject overlayHost;

    // ------------------------------------------------------------------ construction
    void BuildOverlay()
    {
        // A separate root object: a UIDocument parented under another UIDocument is forced onto the parent's panel.
        overlayHost = new GameObject("Game HUD overlay"); var host = overlayHost;
        OverlayPanel = ScriptableObject.CreateInstance<PanelSettings>(); OverlayPanel.name = "Game HUD overlay panel";
        OverlayPanel.themeStyleSheet = Panel.themeStyleSheet; OverlayPanel.scaleMode = Panel.scaleMode; OverlayPanel.referenceResolution = Panel.referenceResolution;
        OverlayPanel.screenMatchMode = Panel.screenMatchMode; OverlayPanel.clearColor = false; OverlayPanel.sortingOrder = Panel.sortingOrder + 1;
        OverlayDocument = host.AddComponent<UIDocument>(); OverlayDocument.panelSettings = OverlayPanel;
        OverlayRoot = OverlayDocument.rootVisualElement; OverlayRoot.name = "game-hud-overlay"; OverlayRoot.pickingMode = PickingMode.Ignore;
        OverlayRoot.style.position = Position.Absolute; OverlayRoot.style.left = OverlayRoot.style.right = OverlayRoot.style.top = OverlayRoot.style.bottom = 0;
        OverlayRoot.style.unityFont = Root.style.unityFont; OverlayRoot.style.color = C(CityColor.UiInk);
        var ink = C(CityColor.UiInk); var muted = C(CityColor.UiMuted); var navy = C(CityColor.UiNavy);

        // ---- waypoint: on-screen marker (diamond + distance + remaining count) and the edge arrow
        Waypoint = Box("hud-waypoint"); Absolute(Waypoint); Waypoint.style.alignItems = Align.Center; Waypoint.style.width = 96;
        Waypoint.style.translate = new Translate(Length.Percent(-50), 0); OverlayRoot.Add(Waypoint);
        waypointDiamond = new HudDiamond(Accent(PlayerSide.Hero), Alpha(navy, .9f)) { name = "waypoint-diamond" }; waypointDiamond.style.width = 30; waypointDiamond.style.height = 30;
        WaypointMarkerIcon = waypointDiamond; Waypoint.Add(waypointDiamond);
        WaypointDistanceLabel = Text("", 14, ink); WaypointDistanceLabel.name = "waypoint-distance"; WaypointDistanceLabel.style.marginTop = 1; Outline(WaypointDistanceLabel); Waypoint.Add(WaypointDistanceLabel);
        WaypointCountLabel = Text("", 11, ink); WaypointCountLabel.name = "waypoint-count"; Outline(WaypointCountLabel); Waypoint.Add(WaypointCountLabel);
        WaypointEdge = Box("hud-waypoint-edge"); Absolute(WaypointEdge); WaypointEdge.style.width = 44; WaypointEdge.style.height = 44; OverlayRoot.Add(WaypointEdge);
        WaypointArrow = new HudArrow(Accent(PlayerSide.Hero), Alpha(navy, .9f)) { name = "waypoint-arrow" }; Absolute(WaypointArrow);
        WaypointArrow.style.left = 5; WaypointArrow.style.top = 5; WaypointArrow.style.width = 34; WaypointArrow.style.height = 34; WaypointEdge.Add(WaypointArrow);
        waypointEdgeDistance = Text("", 12, ink); waypointEdgeDistance.name = "waypoint-edge-distance"; Absolute(waypointEdgeDistance); waypointEdgeDistance.style.top = 44;
        waypointEdgeDistance.style.left = -20; waypointEdgeDistance.style.width = 84; waypointEdgeDistance.style.unityTextAlign = TextAnchor.MiddleCenter; Outline(waypointEdgeDistance); WaypointEdge.Add(waypointEdgeDistance);
        Show(Waypoint, false); Show(WaypointEdge, false);

        // ---- alert card (top-right, under the Heat group)
        AlertCard = Box("hud-alert", FlexDirection.Row); Absolute(AlertCard); AlertCard.style.right = SafeMargin; AlertCard.style.top = SafeMargin + 100;
        PanelBox(AlertCard); Pad(AlertCard, 10, 14); AlertCard.style.borderLeftWidth = 4; AlertCard.style.alignItems = Align.Center; AlertCard.style.minWidth = 300; OverlayRoot.Add(AlertCard);
        var arrowBox = Box("alert-arrow-box"); arrowBox.style.width = 46; arrowBox.style.height = 46; Round(arrowBox, 23); arrowBox.style.backgroundColor = Alpha(navy, .85f);
        arrowBox.style.alignItems = Align.Center; arrowBox.style.justifyContent = Justify.Center; AlertCard.Add(arrowBox);
        AlertArrow = new HudArrow(Accent(PlayerSide.Hero), Alpha(navy, .9f)) { name = "alert-arrow" }; AlertArrow.style.width = 30; AlertArrow.style.height = 30; arrowBox.Add(AlertArrow);
        var alertText = Box("alert-text"); alertText.style.marginLeft = 12; AlertCard.Add(alertText);
        alertEyebrow = Text("NEW ENCOUNTER", 11, Accent(PlayerSide.Hero)); alertText.Add(alertEyebrow);
        AlertNameLabel = Text("", 18, ink); AlertNameLabel.name = "alert-name"; AlertNameLabel.style.marginTop = 2; alertText.Add(AlertNameLabel);
        AlertDistanceLabel = Text("", 12, muted, false); AlertDistanceLabel.name = "alert-distance"; AlertDistanceLabel.style.marginTop = 2; alertText.Add(AlertDistanceLabel);
        Show(AlertCard, false);

        // ---- first-time control prompt (bottom-right)
        PromptCard = Box("hud-prompt"); Absolute(PromptCard); PromptCard.style.right = SafeMargin; PromptCard.style.bottom = SafeMargin;
        PanelBox(PromptCard); Pad(PromptCard, 10, 14); PromptCard.style.borderLeftWidth = 4; OverlayRoot.Add(PromptCard);
        var tip = Text("TRY IT", 11, muted); tip.name = "prompt-eyebrow"; PromptCard.Add(tip);
        promptRow = Box("prompt-row", FlexDirection.Row); promptRow.style.alignItems = Align.Center; promptRow.style.marginTop = 6; PromptCard.Add(promptRow);
        Show(PromptCard, false);

        // ---- mission briefing card (centre, only while shown)
        Briefing = Box("hud-briefing"); Absolute(Briefing); Briefing.style.left = Length.Percent(50); Briefing.style.top = Length.Percent(46);
        Briefing.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50)); Briefing.style.width = 640; Round(Briefing, 18); Pad(Briefing, 24, 32); Briefing.style.overflow = Overflow.Hidden;
        OverlayRoot.Add(Briefing);
        briefingGhost = new MenuIcon(MenuGlyph.Shield, Alpha(Accent(PlayerSide.Hero), .075f)) { name = "briefing-ghost" }; Absolute(briefingGhost);
        briefingGhost.style.width = 260; briefingGhost.style.height = 260; briefingGhost.style.right = -40; briefingGhost.style.top = -46; Briefing.Add(briefingGhost);
        var top = Box("briefing-top", FlexDirection.Row); top.style.justifyContent = Justify.SpaceBetween; Briefing.Add(top);
        BriefingEyebrow = Text("MISSION BRIEFING", 13, Accent(PlayerSide.Hero)); BriefingEyebrow.name = "briefing-eyebrow"; top.Add(BriefingEyebrow);
        briefingMode = Text("", 13, muted); briefingMode.name = "briefing-mode"; top.Add(briefingMode);
        BriefingGoalLabel = Text("", 30, ink); BriefingGoalLabel.name = "briefing-goal"; BriefingGoalLabel.style.whiteSpace = WhiteSpace.Normal; BriefingGoalLabel.style.marginTop = 12; Briefing.Add(BriefingGoalLabel);
        BriefingWinRow = BriefingRow("WIN", out var win); BriefingWinLabel = win; BriefingWinRow.style.marginTop = 16; Briefing.Add(BriefingWinRow);
        BriefingLoseRow = BriefingRow("LOSE", out var lose); BriefingLoseLabel = lose; BriefingLoseRow.style.marginTop = 8; Briefing.Add(BriefingLoseRow);
        var footer = Box("briefing-footer", FlexDirection.Row); footer.style.alignItems = Align.Center; footer.style.marginTop = 20; Briefing.Add(footer);
        var any = Text("ANY KEY TO START", 11, muted); any.name = "briefing-any-key"; footer.Add(any);
        var track = Box("briefing-timer"); track.style.flexGrow = 1; track.style.height = 4; track.style.marginLeft = 12; Round(track, 2); track.style.backgroundColor = Alpha(muted, .25f); footer.Add(track);
        BriefingTimerFill = Box("briefing-timer-fill"); BriefingTimerFill.style.height = Length.Percent(100); Round(BriefingTimerFill, 2); track.Add(BriefingTimerFill);
        Show(Briefing, false);
    }
    VisualElement BriefingRow(string chip, out Label text)
    {
        var row = Box("briefing-" + chip.ToLowerInvariant(), FlexDirection.Row); row.style.alignItems = Align.Center;
        var tag = Text(chip, 11, C(CityColor.UiNavy)); tag.name = "briefing-chip"; tag.style.width = 48; tag.style.unityTextAlign = TextAnchor.MiddleCenter; Round(tag, 4); Pad(tag, 3, 0); row.Add(tag);
        text = Text("", 17, C(CityColor.UiInk), false); text.style.marginLeft = 12; text.style.whiteSpace = WhiteSpace.Normal; text.style.flexShrink = 1; row.Add(text);
        return row;
    }
    void Outline(Label label) { label.style.unityTextOutlineColor = Alpha(C(CityColor.UiNavy), .95f); label.style.unityTextOutlineWidth = .18f; }

    // ------------------------------------------------------------------ binding (additive events)
    void BindGuidance(WorldSession previous, WorldSession next)
    {
        if (previous != null) previous.EncounterSpawned -= OnEncounterSpawned;
        if (next != null) next.EncounterSpawned += OnEncounterSpawned;
        BindHero(next != null ? next.Hero : null);
    }
    void BindHero(SuperHeroController hero)
    {
        if (boundHero == hero) return;
        if (boundHero != null) { boundHero.Jumped -= OnJumped; boundHero.PunchStarted -= OnMelee; boundHero.HurricaneKickStarted -= OnMelee; boundHero.BackflipStarted -= OnBackflip; }
        boundHero = hero;
        if (hero != null) { hero.Jumped += OnJumped; hero.PunchStarted += OnMelee; hero.HurricaneKickStarted += OnMelee; hero.BackflipStarted += OnBackflip; }
    }
    void OnJumped() { DismissBriefing("input: jump"); Performed("move"); }
    void OnMelee() { DismissBriefing("input: melee"); Performed("punch"); }
    void OnBackflip() { DismissBriefing("input: backflip"); Performed("dodge"); }
    void OnEncounterSpawned(CrimeEncounter encounter)
    {
        if (encounter == null) return;
        alertQueue.Enqueue(encounter); alertPending.Add(encounter);
    }

    // ------------------------------------------------------------------ per-frame (called from Refresh)
    void HideGuidance()
    {
        Show(OverlayRoot, false);
    }
    void UpdateGuidance(WorldSession w, GameModeDefinition definition, bool objectives, float now)
    {
        Show(OverlayRoot, true);
        var side = w.Progression.Data.Side; var accent = Accent(side);
        UpdateBriefing(w, definition, accent, now);
        bool free = !w.MenuOpen && !w.PlayerDead && (w.Mode == null || !w.Mode.Paused);
        UpdateAlert(w, accent, now, free && !briefingActive);
        UpdateWaypoint(w, accent, objectives && free && !briefingActive && !GameHud.Shown(Briefing));
        UpdatePrompts(w, accent, now, free && !briefingActive && !GameHud.Shown(Briefing));
    }

    // ---- briefing
    void UpdateBriefing(WorldSession w, GameModeDefinition d, Color accent, float now)
    {
        var session = w.Mode;
        if (session != briefedSession)
        {
            briefedSession = session;
            if (d != null && d.HasBriefing) OpenBriefing(d, accent, now);
        }
        if (briefingActive)
        {
            bool running = !w.MenuOpen && (session == null || !session.Paused);
            if (running) briefingElapsed += Time.unscaledDeltaTime;
            if (now - briefingOpenedAt >= BriefingMinSeconds && running && Input.anyKeyDown) DismissBriefing("input: key");
            else if (d != null && briefingElapsed >= d.BriefingSeconds) DismissBriefing("timeout");
            float left = d != null && d.BriefingSeconds > 0 ? Mathf.Clamp01(1f - briefingElapsed / d.BriefingSeconds) : 0f;
            BriefingTimerFill.style.width = Length.Percent(left * 100f);
            Briefing.style.opacity = Mathf.Clamp01((now - briefingOpenedAt) / BriefingFadeSeconds);
        }
        else if (GameHud.Shown(Briefing))
        {
            float fade = 1f - (now - briefingClosedAt) / BriefingFadeSeconds;
            if (fade <= 0f) Show(Briefing, false); else Briefing.style.opacity = fade;
        }
    }
    void OpenBriefing(GameModeDefinition d, Color accent, float now)
    {
        briefingActive = true; briefingElapsed = 0f; briefingOpenedAt = now; BriefingShows++; BriefingDismissedBy = null; BriefingAccent = accent;
        bool hero = accent == Accent(PlayerSide.Hero);
        BriefingEyebrow.text = "MISSION BRIEFING"; briefingMode.text = d.DisplayName.ToUpperInvariant();
        BriefingGoalLabel.text = d.BriefingGoal ?? ""; Show(BriefingGoalLabel, !string.IsNullOrWhiteSpace(d.BriefingGoal));
        BriefingWinLabel.text = d.BriefingWin ?? ""; Show(BriefingWinRow, !string.IsNullOrWhiteSpace(d.BriefingWin));
        BriefingLoseLabel.text = d.BriefingLose ?? ""; Show(BriefingLoseRow, !string.IsNullOrWhiteSpace(d.BriefingLose));
        BriefingEyebrow.style.color = accent; BriefingTimerFill.style.backgroundColor = accent;
        Border(Briefing, Alpha(accent, .8f), 2); Briefing.style.backgroundColor = Alpha(Color.Lerp(C(CityColor.UiPanel), accent, .13f), .96f);
        briefingGhost.Glyph = hero ? MenuGlyph.Shield : MenuGlyph.Flame; briefingGhost.Ink = Alpha(accent, .075f); briefingGhost.MarkDirtyRepaint();
        foreach (var row in new[] { BriefingWinRow, BriefingLoseRow }) { var chip = row.Q<Label>("briefing-chip"); chip.style.backgroundColor = row == BriefingWinRow ? accent : C(CityColor.UiMuted); }
        Briefing.style.opacity = 0f; Show(Briefing, true);
    }
    /// Ends the briefing (the gameplay-input handlers and the timeout call this). No-op when no briefing is up.
    public void DismissBriefing(string reason)
    {
        if (!briefingActive) return;
        briefingActive = false; BriefingDismissedBy = reason; briefingClosedAt = Time.unscaledTime;
    }

    // ---- objective line (main panel top-centre). Replaces the Phase 1 raw objective text.
    /// The encounter the objective line and waypoint follow: nearest active one whose alert has finished playing.
    CrimeEncounter PickObjectiveEncounter(WorldSession w)
    {
        CrimeEncounter nearest = null; float best = float.MaxValue; Vector3 at = w.Hero.transform.position;
        foreach (var crime in w.Crimes)
        {
            if (crime == null || crime.Encounter == null || crime.Resolved || crime.Encounter.Finished || alertPending.Contains(crime.Encounter)) continue;
            float distance = (crime.Encounter.Site - at).sqrMagnitude; if (distance < best) { best = distance; nearest = crime.Encounter; }
        }
        return nearest;
    }
    void UpdateObjectiveLine(WorldSession w, float now)
    {
        var s = w.Mode; var d = s.Definition; var e = ObjectiveEncounter;
        string header, body, meta;
        if (e != null && d.Rules != null)
        {
            var step = CurrentStep;
            header = e.Definition.DisplayName.ToUpperInvariant() + (d.SuccessGoal > 0 ? $"  ·  {s.Successes}/{d.SuccessGoal} DONE" : "");
            body = step.Valid ? step.Text : "";
            meta = (step.More > 0 ? $"+{step.More} MORE TASK{(step.More == 1 ? "" : "S")}  ·  " : "") + $"{Mathf.Max(0, e.Definition.Deadline - e.Elapsed):F0}s LEFT";
        }
        else
        {
            header = d.DisplayName.ToUpperInvariant() + (d.SuccessGoal > 0 ? $"  ·  {s.Successes}/{d.SuccessGoal} DONE" : "");
            // Between encounters: the last encounter result; never the mode Description (the briefing covers that).
            body = s.Feedback != null && s.Feedback != d.Description ? s.Feedback : ""; meta = "";
        }
        if (objectiveHeader.text != header) objectiveHeader.text = header;
        if (ObjectiveBody.text != body) ObjectiveBody.text = body;
        Show(ObjectiveBody, body.Length > 0);
        if (objectiveMeta.text != meta) objectiveMeta.text = meta;
        Show(objectiveMeta, meta.Length > 0);
    }
    void PulseObjective(float now)
    {
        float t = (now - objectivePulseAt) / ObjectivePulseSeconds;
        float scale = t < 0f || t >= 1f ? 1f : 1f + .08f * Mathf.Sin(t * Mathf.PI);
        if (!Mathf.Approximately(ObjectiveGroup.resolvedStyle.scale.value.x, scale)) ObjectiveGroup.style.scale = new Scale(new Vector3(scale, scale, 1f));
    }
    public bool ObjectivePulsing => Time.unscaledTime - objectivePulseAt < ObjectivePulseSeconds;

    // ---- waypoint
    /// The gameplay camera; cached so a camera that is momentarily disabled (e.g. rendered manually into a target)
    /// still drives the waypoint and alert arrows.
    Camera ViewCamera() { var main = Camera.main; if (main != null) viewCamera = main; return viewCamera; }
    Camera viewCamera;
    void UpdateWaypoint(WorldSession w, Color accent, bool allowed)
    {
        var e = ObjectiveEncounter; var step = CurrentStep; bool show = false; var cam = ViewCamera();
        if (allowed && e != null && step.Valid && cam != null && OverlayRoot.layout.width > 1f)
        {
            ModeRules.Targets(e, step.Task, targets);
            Vector3 hero = w.Hero.transform.position; int nearest = -1; float best = float.MaxValue;
            for (int i = 0; i < targets.Count; i++) { float dd = (targets[i] - hero).sqrMagnitude; if (dd < best) { best = dd; nearest = i; } }
            if (nearest >= 0)
            {
                show = true; WaypointTarget = targets[nearest]; WaypointMetres = Mathf.Sqrt(best);
                WaypointRemaining = step.Distance ? 1 : Mathf.Min(targets.Count, step.Total - step.Done);
                PlaceWaypoint(cam, accent);
            }
        }
        WaypointVisible = show;
        Show(Waypoint, show && WaypointOnScreen); Show(WaypointEdge, show && !WaypointOnScreen);
    }
    void PlaceWaypoint(Camera cam, Color accent)
    {
        float width = OverlayRoot.layout.width, height = OverlayRoot.layout.height; var centre = new Vector2(width * .5f, height * .5f);
        Vector3 vp = cam.WorldToViewportPoint(WaypointTarget + Vector3.up * WaypointHeight);
        var inset = Rect.MinMaxRect(WaypointInsetX, WaypointInsetTop, width - WaypointInsetX, height - WaypointInsetBottom);
        var point = new Vector2(vp.x * width, (1f - vp.y) * height);
        string metres = $"{WaypointMetres:F0} M";
        waypointDiamond.SetFill(accent); WaypointArrow.SetFill(accent);
        if (vp.z > 0f && inset.Contains(point))
        {
            WaypointOnScreen = true; WaypointPushed = false;
            point = AvoidGroups(point, new Rect(-48f, -15f, 96f, 62f), centre);
            var offset = point - centre;
            if (offset.magnitude < CrosshairClearRadius) { WaypointPushed = true; point = centre + (offset.sqrMagnitude > .01f ? offset.normalized : Vector2.down) * CrosshairClearRadius; }
            WaypointPoint = point;
            Waypoint.style.left = point.x; Waypoint.style.top = point.y - 15f;
            if (WaypointDistanceLabel.text != metres) WaypointDistanceLabel.text = metres;
            string count = WaypointRemaining > 1 ? $"{WaypointRemaining} LEFT" : "";
            if (WaypointCountLabel.text != count) WaypointCountLabel.text = count;
            Show(WaypointCountLabel, count.Length > 0);
        }
        else
        {
            WaypointOnScreen = false;
            // Direction from screen centre in pixels, y up. With square pixels the perspective divide scales x and y
            // alike, so it is the camera-local (x, y) of the target; this also holds behind the camera, where the
            // viewport projection is mirrored.
            var local = cam.transform.InverseTransformPoint(WaypointTarget + Vector3.up * WaypointHeight);
            var dir = new Vector2(local.x, local.y);
            if (dir.sqrMagnitude < .0001f) dir = Vector2.down;                     // dead behind: point down ("behind you")
            WaypointArrowAngle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;          // clockwise from up
            float halfW = inset.width * .5f, halfH = inset.height * .5f, insetCx = inset.center.x, insetCy = inset.center.y;
            float t = Mathf.Min(Mathf.Abs(dir.x) > .0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue, Mathf.Abs(dir.y) > .0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue);
            point = new Vector2(insetCx + dir.x * t, insetCy - dir.y * t); WaypointPushed = false;
            point = AvoidGroups(point, new Rect(-42f, -22f, 84f, 66f), centre); WaypointPoint = point;
            WaypointEdge.style.left = point.x - 22f; WaypointEdge.style.top = point.y - 22f;
            WaypointArrow.style.rotate = new Rotate(Angle.Degrees(WaypointArrowAngle));
            string label = WaypointRemaining > 1 ? $"{metres} · {WaypointRemaining}" : metres;
            if (waypointEdgeDistance.text != label) waypointEdgeDistance.text = label;
        }
    }
    /// Slides a waypoint box (relative to its point) toward the centre until it clears every shown edge group and card.
    Vector2 AvoidGroups(Vector2 point, Rect box, Vector2 centre)
    {
        WaypointShifted = false;
        for (int i = 0; i < 60; i++)
        {
            var at = new Rect(point + box.position, box.size); bool hit = false;
            foreach (var g in AvoidList()) if (GameHud.Shown(g) && g.worldBound.Overlaps(at)) { hit = true; break; }
            if (!hit) return point;
            var step = centre - point; if (step.sqrMagnitude < 64f) return point;
            point += step.normalized * 8f; WaypointShifted = true;
        }
        return point;
    }
    IEnumerable<VisualElement> AvoidList() { yield return LevelGroup; yield return HeatGroup; yield return TopCentre; yield return VitalsGroup; yield return BottomStack; yield return AlertCard; yield return PromptCard; }
    /// Horizontal direction to a world point relative to the camera heading (degrees clockwise, 0 = ahead).
    public static float HeadingBearing(Camera cam, Vector3 from, Vector3 world)
    {
        Vector3 forward = cam.transform.forward; forward.y = 0f; Vector3 right = cam.transform.right; right.y = 0f; Vector3 to = world - from; to.y = 0f;
        if (forward.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return 0f;
        return Mathf.Atan2(Vector3.Dot(to, right.normalized), Vector3.Dot(to, forward.normalized)) * Mathf.Rad2Deg;
    }

    // ---- alerts
    void UpdateAlert(WorldSession w, Color accent, float now, bool allowed)
    {
        alertPending.RemoveWhere(e => e == null || e.Finished);
        if (AlertEncounter == null)
        {
            while (alertQueue.Count > 0 && (alertQueue.Peek() == null || alertQueue.Peek().Finished)) alertQueue.Dequeue();
            if (alertQueue.Count == 0 || !allowed) { Show(AlertCard, false); return; }
            AlertEncounter = alertQueue.Dequeue(); alertStartedAt = now; AlertsStarted++;
            AlertNameLabel.text = AlertEncounter.Definition.DisplayName.ToUpperInvariant();
            alertEyebrow.style.color = accent; AlertCard.style.borderLeftColor = accent; AlertArrow.SetFill(accent);
            Show(AlertCard, true);
        }
        var e = AlertEncounter; float t = now - alertStartedAt, total = AlertSlideSeconds * 2f + AlertHoldSeconds;
        if (e == null || e.Finished || t >= total)
        {
            if (e != null) alertPending.Remove(e);
            AlertEncounter = null; AlertsFinished++; Show(AlertCard, false);
            objectivePulseAt = now; nextStep = 0f; nextObjective = 0f;   // the objective line updates now, with a pulse
            return;
        }
        float slide = t < AlertSlideSeconds ? 1f - Ease(t / AlertSlideSeconds) : t > AlertSlideSeconds + AlertHoldSeconds ? Ease((t - AlertSlideSeconds - AlertHoldSeconds) / AlertSlideSeconds) : 0f;
        AlertCard.style.translate = new Translate(Length.Percent(115f * slide), 0);
        AlertCard.style.top = GameHud.Shown(HeatGroup) ? HeatGroup.layout.yMax + 12f : SafeMargin;
        var cam = ViewCamera();
        if (cam != null) { AlertArrowAngle = HeadingBearing(cam, w.Hero.transform.position, e.Site); AlertArrow.style.rotate = new Rotate(Angle.Degrees(AlertArrowAngle)); }
        string distance = $"{Vector3.Distance(w.Hero.transform.position, e.Site):F0} M AWAY";
        if (AlertDistanceLabel.text != distance) AlertDistanceLabel.text = distance;
    }
    static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }

    // ---- first-time prompts
    void Performed(string id)
    {
        var w = WorldSession.Instance; if (w == null || w.Progression == null) return;
        w.Progression.MarkHintSeen(id);
        if (PromptId == id) { PromptsCompleted.Add(id); PromptId = null; PromptText = null; Show(PromptCard, false); }
    }
    void UpdatePrompts(WorldSession w, Color accent, float now, bool allowed)
    {
        // Real-action hooks that are states rather than events: flight started, an R hold is progressing.
        if (w.Hero.PresentationState.Flying) Performed("move");
        foreach (var crime in w.Crimes) if (crime != null && crime.Encounter != null && crime.Encounter.HoldFraction > 0f) { Performed("interact"); break; }
        if (!allowed) { if (PromptId != null) { PromptId = null; Show(PromptCard, false); } return; }
        if (now >= nextPromptScan) { nextPromptScan = now + .1f; promptCandidate = PromptCandidate(w); }
        string id = promptCandidate != null && !w.Progression.HintSeen(promptCandidate) ? promptCandidate : null;
        if (id == null && PromptId != null && !w.Progression.HintSeen(PromptId) && now - promptShownAt < PromptMinSeconds) id = PromptId; // linger
        if (id != PromptId)
        {
            PromptId = id;
            if (id == null) { Show(PromptCard, false); PromptText = null; return; }
            BuildPrompt(id, w.Powers, accent); promptShownAt = now; if (!PromptsShown.Contains(id)) PromptsShown.Add(id);
            Show(PromptCard, true);
        }
        if (id != null)
        {
            float t = Mathf.Clamp01((now - promptShownAt) / .22f), scale = .9f + .1f * Ease(t);
            PromptCard.style.scale = new Scale(new Vector3(scale, scale, 1f)); PromptCard.style.opacity = t;
        }
    }
    /// Highest-priority prompt whose context holds right now (dodge > interact > punch > move), unseen ones only.
    /// Interact outranks punch: next to an objective robber, holding R is the objective action.
    string PromptCandidate(WorldSession w)
    {
        var p = w.Progression; Vector3 at = w.Hero.transform.position;
        bool windup = false, hostileNear = false;
        foreach (var npc in w.Npcs)
        {
            if (npc == null || npc.Dead || !npc.Hostile) continue;
            if (npc.Phase == AttackPhase.Windup) windup = true;
            if ((npc.transform.position - at).sqrMagnitude < PunchPromptRange * PunchPromptRange) hostileNear = true;
        }
        if (windup && !p.HintSeen("dodge")) return "dodge";
        if (!p.HintSeen("interact")) foreach (var crime in w.Crimes) if (crime != null && crime.Encounter != null && !crime.Resolved && crime.Encounter.InteractableNear(at)) return "interact";
        if (hostileNear && !p.HintSeen("punch")) return "punch";
        if (!p.HintSeen("move") && w.Hero.PresentationState.Grounded) return "move";
        return null;
    }
    void BuildPrompt(string id, PowerUser user, Color accent)
    {
        promptRow.Clear(); PromptCard.style.borderLeftColor = accent; var parts = new List<string>();
        void Pair(string key, string action)
        {
            if (promptRow.childCount > 0) { var dot = Text("·", 14, C(CityColor.UiMuted)); dot.style.marginLeft = dot.style.marginRight = 10; promptRow.Add(dot); }
            var cap = Keycap(key); cap.style.fontSize = 14; Pad(cap, 3, 8); promptRow.Add(cap);
            var label = Text(action, 15, C(CityColor.UiInk)); label.name = "prompt-action"; label.style.marginLeft = 8; promptRow.Add(label);
            parts.Add(key + " " + action);
        }
        switch (id)
        {
            case "move": Pair(HudBindings.JumpKey, "JUMP"); if (HudBindings.FlightEquipped(user)) Pair(HudBindings.FlightKey, "HOLD IN THE AIR TO FLY"); break;
            case "punch": Pair(HudBindings.MeleeKey(user), "PUNCH"); break;
            case "dodge": Pair(HudBindings.BackflipKey, "BACKFLIP TO DODGE"); break;
            case "interact": Pair(HudBindings.InteractKey, "HOLD TO INTERACT"); break;
        }
        PromptText = string.Join(" · ", parts);
    }

    // ------------------------------------------------------------------ objective state (step + encounter) for line + waypoint
    void UpdateObjectiveState(WorldSession w, float now)
    {
        if (now < nextStep && ObjectiveEncounter != null && !ObjectiveEncounter.Finished) return;
        nextStep = now + .1f;
        ObjectiveEncounter = PickObjectiveEncounter(w);
        var rules = w.Mode != null ? w.Mode.Definition.Rules : null;
        CurrentStep = ObjectiveEncounter != null && rules != null ? rules.Current(ObjectiveEncounter) : default;
    }
}
