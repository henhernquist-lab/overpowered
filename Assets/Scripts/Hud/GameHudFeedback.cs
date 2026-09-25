using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// HUD Phase 3, "connect the dots": every meaningful action visibly feeds a system.
/// - Floating "+XP" popups (overlay panel) at the world position of the event that granted the XP. HONEST: one popup
///   amount per real grant (PlayerProgression.XpGranted), grants landing within PopupMergeSeconds near the same spot merge
///   into one popup, so the shown total always equals the XP granted. Positionless grants float next to the level badge.
///   Popup elements are pooled.
/// - Heat feedback (main panel): a flash + delta ("+0.35") on DISCRETE changes only (WorldSession.HeatAdded) and a
///   "-1 star" flash when the star count changes for any other reason (decay, respawn). Continuous decay never flashes.
/// - Level-up: a burst on the level badge + a "LEVEL UP — TAB TO UPGRADE" banner. Nothing pauses; timeScale is untouched.
/// - Banners (overlay, top-centre under the objective/director slot): objective complete / failed, level up, director
///   milestones (Endless wave cleared). One banner at a time, queued; alerts never overlap a banner and wait for queued ones.
/// Every animation uses unscaled time.
public sealed partial class GameHud
{
    // ---- Phase 3 timing / size constants (all in one place; panel units at the 1600x900 reference)
    /// XP popups: lifetime, fade-out tail, rise (panel px over the lifetime), world height above the event, merge window
    /// and radius, font size, pop-in scale, pool size.
    public const float PopupSeconds = 1.3f, PopupFadeSeconds = .4f, PopupRise = 64f, PopupHeight = 2.2f, PopupMergeSeconds = .3f, PopupMergeRadius = 4f,
        PopupFont = 28f, PopupPopScale = .35f, PopupPopSeconds = .18f;
    public const int PopupPoolSize = 16;
    /// Heat: flash length, window in which several AddHeat calls add into one shown delta, star-row pulse, delta font.
    public const float HeatFlashSeconds = .9f, HeatMergeSeconds = .3f, HeatFlashScale = .16f, HeatDeltaFont = 20f;
    /// Level-up burst on the badge: length, badge pulse, expanding ring scale.
    public const float LevelBurstSeconds = .8f, LevelBurstScale = .25f, LevelRingScale = .3f;
    /// Banners: slide/fade in, hold, fade out, gap under the top-centre group, title font.
    public const float BannerInSeconds = .25f, BannerHoldSeconds = 2f, BannerOutSeconds = .3f, BannerGap = 14f, BannerTitleFont = 26f;

    public sealed class XpPopup
    {
        public Label Element; public bool Active, Anchored; public Vector3 Anchor; public int Amount, Grants, Id;
        public float StartedAt, LastGrantAt, PoppedAt; public string Reason;
        /// Panel point of the anchor projection (or the badge fallback) before the rise, and the rise applied this frame.
        public Vector2 BasePoint, DrawnPoint; public float Rise; public bool OnScreen, Clamped;
    }
    public readonly struct XpPopupRecord
    {
        public readonly int Id, Amount, Grants; public readonly bool Anchored; public readonly Vector3 Anchor; public readonly string Reason; public readonly float StartedAt;
        public XpPopupRecord(XpPopup p) { Id = p.Id; Amount = p.Amount; Grants = p.Grants; Anchored = p.Anchored; Anchor = p.Anchor; Reason = p.Reason; StartedAt = p.StartedAt; }
    }
    public enum BannerKind { ObjectiveComplete, ObjectiveFailed, LevelUp, Milestone }
    public sealed class HudBanner { public BannerKind Kind; public string Eyebrow, Title, Reward, Detail; public int Id; }
    /// One alert or banner on the transient timeline (verification asserts they never overlap and keep their order).
    public struct TimelineEntry { public string Kind, Name; public float StartedAt, EndedAt; }

    // ---- popups
    public readonly List<XpPopup> Popups = new List<XpPopup>();
    /// Finished and live popups, oldest first (bounded; the verification reads the amounts).
    public readonly List<XpPopupRecord> PopupHistory = new List<XpPopupRecord>();
    public int PopupsStarted { get; private set; }
    public int PopupMerges { get; private set; }
    /// Sum of every amount ever shown in a popup (merged grants included) and of every grant received.
    public int PopupXpShown { get; private set; }
    public int XpGrantsReceived { get; private set; }
    public int PopupElementsCreated { get; private set; }
    // ---- heat
    public VisualElement HeatDelta { get; private set; }
    public Label HeatDeltaLabel { get; private set; }
    public int HeatFlashes { get; private set; }
    public int HeatFlashMerges { get; private set; }
    public float HeatFlashAt { get; private set; } = -100f;
    /// The delta text as drawn; a star-count flash draws a star glyph element after the number (shown here as "*").
    public string HeatDeltaText => HeatDeltaLabel.text + (heatDeltaIsStars ? "*" : "");
    public bool HeatDeltaIsStars => heatDeltaIsStars;
    public bool HeatFlashing => Time.unscaledTime - HeatFlashAt < HeatFlashSeconds;
    // ---- level
    public VisualElement LevelRing { get; private set; }
    public int LevelUps { get; private set; }
    public float LevelBurstAt { get; private set; } = -100f;
    public bool LevelBursting => Time.unscaledTime - LevelBurstAt < LevelBurstSeconds;
    public VisualElement LevelBadge => levelBadge;
    // ---- banners
    public VisualElement Banner { get; private set; }
    public Label BannerEyebrowLabel { get; private set; }
    public Label BannerTitleLabel { get; private set; }
    public Label BannerRewardLabel { get; private set; }
    public Label BannerDetailLabel { get; private set; }
    public HudBanner CurrentBanner { get; private set; }
    public int BannersStarted { get; private set; }
    public int BannersQueued => bannerQueue.Count;
    /// No banner showing and none waiting (alerts may start).
    public bool BannerIdle => CurrentBanner == null && bannerQueue.Count == 0;
    public readonly List<TimelineEntry> Timeline = new List<TimelineEntry>();
    /// An objective-complete / failed banner is showing or queued.
    public bool ResultBannerPending
    {
        get
        {
            if (CurrentBanner != null && CurrentBanner.Kind <= BannerKind.ObjectiveFailed) return true;
            foreach (var b in bannerQueue) if (b.Kind <= BannerKind.ObjectiveFailed) return true;
            return false;
        }
    }
    /// Glyphs the HUD font may lack fall back to ASCII (checked once).
    public string Minus { get; private set; } = "-";
    public string Dash { get; private set; } = "-";

    HudStar heatDeltaStar; float heatDeltaValue, heatAddedAt = -100f; bool heatAddedThisRefresh, heatDeltaIsStars, heatBorderLit, levelBurstLit;
    readonly Queue<HudBanner> bannerQueue = new Queue<HudBanner>();
    float bannerStartedAt; int bannerTimelineIndex = -1, bannerIds, popupIds;
    PlayerProgression boundProgression; GameModeSession boundSession; ModeDirectorState boundDirector;

    // ------------------------------------------------------------------ construction
    void BuildFeedback()
    {
        var font = Root.style.unityFont.value;
        if (font != null) { Minus = font.HasCharacter('−') ? "−" : "-"; Dash = font.HasCharacter('—') ? "—" : "-"; }
        var ink = C(CityColor.UiInk); var muted = C(CityColor.UiMuted); var amber = C(CityColor.Amber);

        // ---- heat delta, beside the stars (main panel, child of the Heat group so it follows it)
        HeatDelta = Box("heat-delta", FlexDirection.Row); Absolute(HeatDelta); HeatDelta.style.right = Length.Percent(100); HeatDelta.style.marginRight = 10;
        HeatDelta.style.top = 8; HeatDelta.style.alignItems = Align.Center; HeatGroup.Add(HeatDelta);
        HeatDeltaLabel = Text("", (int)HeatDeltaFont, amber); HeatDeltaLabel.name = "heat-delta-value"; Outline(HeatDeltaLabel); HeatDelta.Add(HeatDeltaLabel);
        heatDeltaStar = new HudStar { name = "heat-delta-star" }; heatDeltaStar.style.width = 20; heatDeltaStar.style.height = 20; heatDeltaStar.style.marginLeft = 2;
        heatDeltaStar.Set(amber, Alpha(C(CityColor.UiNavy), .9f)); HeatDelta.Add(heatDeltaStar);
        Show(HeatDelta, false);

        // ---- level-up ring over the badge (sibling of the badge so the badge pulse does not compound it)
        LevelRing = Box("level-ring"); Absolute(LevelRing); Round(LevelRing, 13); Border(LevelRing, Accent(PlayerSide.Hero), 3); LevelGroup.Add(LevelRing); Show(LevelRing, false);

        // ---- banner (overlay, top-centre under the objective / director slot)
        Banner = Box("hud-banner"); Absolute(Banner); Banner.style.left = Length.Percent(50); Banner.style.top = 150;
        Banner.style.translate = new Translate(Length.Percent(-50), 0); PanelBox(Banner); Pad(Banner, 10, 24); Banner.style.alignItems = Align.Center;
        Banner.style.minWidth = 340; Banner.style.borderBottomWidth = 3; OverlayRoot.Add(Banner);
        BannerEyebrowLabel = Text("", 13, Accent(PlayerSide.Hero)); BannerEyebrowLabel.name = "banner-eyebrow"; Banner.Add(BannerEyebrowLabel);
        var row = Box("banner-row", FlexDirection.Row); row.style.alignItems = Align.Center; row.style.marginTop = 3; Banner.Add(row);
        BannerTitleLabel = Text("", (int)BannerTitleFont, ink); BannerTitleLabel.name = "banner-title"; row.Add(BannerTitleLabel);
        BannerRewardLabel = Text("", 17, C(CityColor.UiNavy)); BannerRewardLabel.name = "banner-reward"; BannerRewardLabel.style.marginLeft = 14; Round(BannerRewardLabel, 6); Pad(BannerRewardLabel, 3, 10); row.Add(BannerRewardLabel);
        BannerDetailLabel = Text("", 13, muted, false); BannerDetailLabel.name = "banner-detail"; BannerDetailLabel.style.marginTop = 3; Banner.Add(BannerDetailLabel);
        Show(Banner, false);
        // ---- XP popup pool (overlay; built after the banner so a popup draws above it)
        for (int i = 0; i < PopupPoolSize; i++)
        {
            var label = Text("", (int)PopupFont, ink); label.name = "xp-popup"; Absolute(label); Outline(label); label.style.unityTextOutlineWidth = .22f;
            label.style.backgroundColor = Alpha(C(CityColor.UiNavy), .72f); Round(label, 9); Pad(label, 2, 10);   // dark pill: readable over busy streets
            label.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50)); Show(label, false);
            OverlayRoot.Add(label); Popups.Add(new XpPopup { Element = label }); PopupElementsCreated++;
        }

    }

    // ------------------------------------------------------------------ binding (additive events)
    void BindFeedback(WorldSession w)
    {
        var progression = w != null ? w.Progression : null; var session = w != null ? w.Mode : null; var director = session != null ? session.Director : null;
        if (progression != boundProgression)
        {
            if (boundProgression != null) { boundProgression.XpGranted -= OnXpGranted; boundProgression.LevelUp -= OnLevelUp; }
            boundProgression = progression;
            if (progression != null) { progression.XpGranted += OnXpGranted; progression.LevelUp += OnLevelUp; }
        }
        if (session != boundSession)
        {
            if (boundSession != null) boundSession.EncounterResolved -= OnEncounterResolved;
            boundSession = session; if (session != null) session.EncounterResolved += OnEncounterResolved;
        }
        if (director != boundDirector)
        {
            if (boundDirector != null) boundDirector.Announced -= OnAnnounced;
            boundDirector = director; if (director != null) director.Announced += OnAnnounced;
        }
    }
    void BindHeat(WorldSession previous, WorldSession next)
    {
        if (previous != null) previous.HeatAdded -= OnHeatAdded;
        if (next != null) next.HeatAdded += OnHeatAdded;
    }

    // ------------------------------------------------------------------ XP popups
    void OnXpGranted(XpGrant grant)
    {
        if (grant.Amount <= 0) return;
        XpGrantsReceived++; float now = Time.unscaledTime;
        XpPopup target = null;
        foreach (var p in Popups)
        {
            if (!p.Active || p.Anchored != grant.HasPosition || now - p.LastGrantAt > PopupMergeSeconds) continue;
            if (grant.HasPosition && (p.Anchor - grant.Position).sqrMagnitude > PopupMergeRadius * PopupMergeRadius) continue;
            target = p; break;
        }
        if (target != null) { target.Amount += grant.Amount; target.Grants++; PopupMerges++; }
        else
        {
            target = Popups[0];
            foreach (var p in Popups) { if (!p.Active) { target = p; break; } if (p.StartedAt < target.StartedAt) target = p; }
            if (target.Active) Retire(target);   // pool exhausted: the oldest popup gives up its element early (its amount is already recorded)
            target.Active = true; target.Anchored = grant.HasPosition; target.Anchor = grant.Position; target.Amount = grant.Amount; target.Grants = 1;
            target.StartedAt = now; target.Reason = grant.Reason; target.Id = ++popupIds; PopupsStarted++;
            target.Element.style.color = Accent(boundProgression != null ? boundProgression.Data.Side : PlayerSide.Hero);
            PopupHistory.Add(new XpPopupRecord(target)); if (PopupHistory.Count > 256) PopupHistory.RemoveAt(0);
        }
        target.LastGrantAt = now; target.PoppedAt = now; PopupXpShown += grant.Amount;
        target.Element.text = $"+{target.Amount} XP";
        for (int i = PopupHistory.Count - 1; i >= 0; i--) if (PopupHistory[i].Id == target.Id) { PopupHistory[i] = new XpPopupRecord(target); break; }
    }
    void Retire(XpPopup p) { p.Active = false; Show(p.Element, false); }
    void UpdatePopups(float now)
    {
        var cam = ViewCamera(); float width = OverlayRoot.layout.width, height = OverlayRoot.layout.height;
        foreach (var p in Popups)
        {
            if (!p.Active) continue;
            float age = now - p.StartedAt, end = Mathf.Max(PopupSeconds, p.LastGrantAt - p.StartedAt + PopupSeconds * .75f);
            if (age >= end) { Retire(p); continue; }
            if (p.Anchored)
            {
                p.OnScreen = false;
                if (cam != null && width > 1f)
                {
                    Vector3 vp = cam.WorldToViewportPoint(p.Anchor + Vector3.up * PopupHeight);
                    p.OnScreen = vp.z > 0f && vp.x > -.02f && vp.x < 1.02f && vp.y > -.02f && vp.y < 1.02f;   // off-screen events still count, unseen
                    p.BasePoint = new Vector2(vp.x * width, (1f - vp.y) * height);
                }
            }
            else { var g = LevelGroup.layout; p.BasePoint = new Vector2(g.xMax + 16f + Mathf.Max(p.Element.layout.width, PopupFont * 3.2f) * .5f * (1f + PopupPopScale), g.center.y); p.OnScreen = Shown(LevelGroup); }
            p.Rise = PopupRise * Ease(age / end);
            float pop = (now - p.PoppedAt) / PopupPopSeconds, scale = pop < 1f ? 1f + PopupPopScale * (1f - pop) : 1f;
            // Kept whole inside the safe margin near the screen edges (Clamped says the drawn point left the projection).
            float halfW = Mathf.Max(p.Element.layout.width, PopupFont * 3.2f) * .5f * scale + 2f, halfH = (PopupFont * .75f + 2f) * scale;
            var drawn = new Vector2(p.BasePoint.x, p.BasePoint.y - p.Rise);
            var clamped = new Vector2(Mathf.Clamp(drawn.x, SafeMargin + halfW, Mathf.Max(SafeMargin + halfW, width - SafeMargin - halfW)),
                                      Mathf.Clamp(drawn.y, SafeMargin + halfH, Mathf.Max(SafeMargin + halfH, height - SafeMargin - halfH)));
            // Popups never cover the edge HUD or a banner: one that would pass over a top group / the banner is drawn just
            // below it, over a bottom group just above it (Clamped then says it left its projection).
            foreach (var g in PopupAvoid())
            {
                if (!Shown(g)) continue; var band = g.worldBound; bool bottom = g == VitalsGroup || g == BottomStack;
                if (clamped.x + halfW > band.xMin && clamped.x - halfW < band.xMax && clamped.y - halfH < band.yMax && clamped.y + halfH > band.yMin)
                    clamped.y = bottom ? band.yMin - halfH - 4f : band.yMax + halfH + 4f;
            }
            p.Clamped = (clamped - drawn).sqrMagnitude > .01f; p.DrawnPoint = clamped;
            p.Element.style.left = clamped.x; p.Element.style.top = clamped.y;
            p.Element.style.scale = new Scale(new Vector3(scale, scale, 1f));
            p.Element.style.opacity = Mathf.Clamp01((end - age) / PopupFadeSeconds);
            Show(p.Element, p.OnScreen);
        }
    }
    IEnumerable<VisualElement> PopupAvoid() { yield return LevelGroup; yield return HeatGroup; yield return TopCentre; yield return Banner; yield return VitalsGroup; yield return BottomStack; }
    public int ActivePopups { get { int n = 0; foreach (var p in Popups) if (p.Active) n++; return n; } }

    // ------------------------------------------------------------------ heat flash
    void OnHeatAdded(float delta)
    {
        float now = Time.unscaledTime; heatAddedThisRefresh = true;
        if (now - heatAddedAt <= HeatMergeSeconds && HeatFlashing && !heatDeltaIsStars) { heatDeltaValue += delta; HeatFlashMerges++; }
        else { heatDeltaValue = delta; HeatFlashes++; }
        heatAddedAt = now; HeatFlashAt = now;
        HeatDeltaLabel.text = (heatDeltaValue >= 0f ? "+" : Minus) + Mathf.Abs(heatDeltaValue).ToString("0.##");
        heatDeltaIsStars = false; Show(heatDeltaStar, false);
    }
    /// Called by UpdateStars when the lit star count changed. A change caused by AddHeat is already flashing with its
    /// heat delta; any other change (decay, respawn reset) flashes "-N star" once.
    void StarsChanged(int from, int to, float now)
    {
        if (heatAddedThisRefresh || from < 0) return;
        HeatFlashes++; HeatFlashAt = now; int d = to - from;
        HeatDeltaLabel.text = (d >= 0 ? "+" : Minus) + Mathf.Abs(d); heatDeltaIsStars = true; Show(heatDeltaStar, true);
    }
    void UpdateHeatFlash(float now, bool heatShown)
    {
        heatAddedThisRefresh = false;
        float t = (now - HeatFlashAt) / HeatFlashSeconds; bool on = heatShown && t >= 0f && t < 1f;
        Show(HeatDelta, on);
        float pulse = on ? Mathf.Sin(Mathf.Clamp01(t * 2.5f) * Mathf.PI) : 0f, scale = 1f + HeatFlashScale * pulse;
        if (!Mathf.Approximately(starsBox.resolvedStyle.scale.value.x, scale)) starsBox.style.scale = new Scale(new Vector3(scale, scale, 1f));
        if (on || heatBorderLit)
        {
            heatBorderLit = on;
            HeatGroup.style.borderTopColor = HeatGroup.style.borderBottomColor = HeatGroup.style.borderLeftColor = HeatGroup.style.borderRightColor =
                Color.Lerp(Alpha(C(CityColor.UiMuted), .18f), C(CityColor.Amber), on ? 1f - t : 0f);
        }
        if (on) { HeatDelta.style.opacity = Mathf.Clamp01((1f - t) / .3f); HeatDelta.style.translate = new Translate(0, -6f * Ease(t)); }
    }

    // ------------------------------------------------------------------ level-up burst + banner
    void OnLevelUp(int from, int to)
    {
        LevelUps++; LevelBurstAt = Time.unscaledTime;
        var p = boundProgression; int points = p != null ? p.Data.Points : 0;
        Enqueue(new HudBanner { Kind = BannerKind.LevelUp, Eyebrow = $"LEVEL {to}", Title = $"LEVEL UP {Dash} {HudBindings.KeyName(HudBindings.MenuKey)} TO UPGRADE",
            Reward = points > 0 ? $"{points} POINT{(points == 1 ? "" : "S")}" : "" });
    }
    void UpdateLevelBurst(float now, Color accent)
    {
        float t = (now - LevelBurstAt) / LevelBurstSeconds; bool on = t >= 0f && t < 1f && Shown(LevelGroup);
        float scale = on ? 1f + LevelBurstScale * Mathf.Sin(Mathf.Clamp01(t * 1.6f) * Mathf.PI) : 1f;
        if (!Mathf.Approximately(levelBadge.resolvedStyle.scale.value.x, scale)) levelBadge.style.scale = new Scale(new Vector3(scale, scale, 1f));
        if (on || levelBurstLit) { levelBurstLit = on; levelBadge.style.backgroundColor = Alpha(accent, on ? Mathf.Lerp(.16f, .75f, 1f - t) : .16f); }
        Show(LevelRing, on);
        if (on)
        {
            var b = levelBadge.layout; LevelRing.style.left = b.x; LevelRing.style.top = b.y; LevelRing.style.width = b.width; LevelRing.style.height = b.height;
            float ring = 1f + LevelRingScale * Ease(t); LevelRing.style.scale = new Scale(new Vector3(ring, ring, 1f)); LevelRing.style.opacity = 1f - t;
            LevelRing.style.borderTopColor = LevelRing.style.borderBottomColor = LevelRing.style.borderLeftColor = LevelRing.style.borderRightColor = accent;
        }
    }

    // ------------------------------------------------------------------ objective / milestone banners
    /// True when the world message is an encounter result (GameModeSession.Feedback after EncounterEnded) or starts with the
    /// title of the last director announcement: the banner already says it.
    bool BannerCovers(WorldSession w)
    {
        if (string.IsNullOrEmpty(w.Message)) return false;
        if (w.Mode != null && w.Message == w.Mode.Feedback && w.Mode.Feedback != w.Mode.Definition.Description && resolvedSeen) return true;
        return !string.IsNullOrEmpty(lastAnnouncement) && w.Message.StartsWith(lastAnnouncement);
    }
    bool resolvedSeen; string lastAnnouncement;
    void OnEncounterResolved(EncounterOutcome outcome)
    {
        resolvedSeen = true; nextStep = 0f; nextObjective = 0f;   // the objective line drops the finished encounter at once
        string name = outcome.Definition != null ? outcome.Definition.DisplayName.ToUpperInvariant() : "OBJECTIVE";
        if (outcome.Success) Enqueue(new HudBanner { Kind = BannerKind.ObjectiveComplete, Eyebrow = "OBJECTIVE COMPLETE", Title = name, Reward = outcome.Xp > 0 ? $"+{outcome.Xp} XP" : "" });
        else Enqueue(new HudBanner { Kind = BannerKind.ObjectiveFailed, Eyebrow = "OBJECTIVE FAILED", Title = name, Detail = (outcome.Reason ?? "").ToUpperInvariant() });
    }
    void OnAnnounced(DirectorAnnouncement a) { lastAnnouncement = a.Title; Enqueue(new HudBanner { Kind = BannerKind.Milestone, Eyebrow = a.Eyebrow ?? "", Title = a.Title ?? "", Reward = a.Reward ?? "" }); }
    void Enqueue(HudBanner banner) { banner.Id = ++bannerIds; bannerQueue.Enqueue(banner); }
    void UpdateBanner(float now, Color accent, bool allowed)
    {
        if (CurrentBanner == null)
        {
            if (bannerQueue.Count == 0 || !allowed || AlertShowing) { Show(Banner, false); return; }
            CurrentBanner = bannerQueue.Dequeue(); bannerStartedAt = now; BannersStarted++; StyleBanner(CurrentBanner, accent);
            Timeline.Add(new TimelineEntry { Kind = "banner:" + CurrentBanner.Kind, Name = CurrentBanner.Title, StartedAt = now, EndedAt = -1f }); bannerTimelineIndex = Timeline.Count - 1;
            Show(Banner, true);
        }
        float t = now - bannerStartedAt, total = BannerInSeconds + BannerHoldSeconds + BannerOutSeconds;
        if (t >= total)
        {
            var entry = Timeline[bannerTimelineIndex]; entry.EndedAt = now; Timeline[bannerTimelineIndex] = entry;
            CurrentBanner = null; Show(Banner, false); return;
        }
        float fade = t < BannerInSeconds ? Ease(t / BannerInSeconds) : t > BannerInSeconds + BannerHoldSeconds ? 1f - (t - BannerInSeconds - BannerHoldSeconds) / BannerOutSeconds : 1f;
        Banner.style.opacity = Mathf.Clamp01(fade);
        Banner.style.translate = new Translate(Length.Percent(-50), -14f * (1f - Mathf.Clamp01(fade)));
        Banner.style.top = Shown(TopCentre) ? TopCentre.layout.yMax + BannerGap : SafeMargin + 40f;
    }
    void StyleBanner(HudBanner b, Color accent)
    {
        bool failed = b.Kind == BannerKind.ObjectiveFailed; var colour = failed ? C(CityColor.UiMuted) : accent;
        BannerEyebrowLabel.text = b.Eyebrow ?? ""; BannerEyebrowLabel.style.color = colour;
        BannerTitleLabel.text = b.Title ?? ""; BannerTitleLabel.style.color = failed ? C(CityColor.UiMuted) : C(CityColor.UiInk);
        BannerRewardLabel.text = b.Reward ?? ""; BannerRewardLabel.style.backgroundColor = accent; Show(BannerRewardLabel, !string.IsNullOrEmpty(b.Reward));
        BannerDetailLabel.text = b.Detail ?? ""; Show(BannerDetailLabel, !string.IsNullOrEmpty(b.Detail));
        Banner.style.borderBottomColor = colour;
        Banner.style.backgroundColor = Alpha(failed ? C(CityColor.UiPanel) : Color.Lerp(C(CityColor.UiPanel), accent, .14f), failed ? .85f : .94f);
    }

    // ------------------------------------------------------------------ per-frame (called from UpdateGuidance)
    void UpdateFeedback(float now, Color accent, bool free)
    {
        UpdatePopups(now);
        UpdateBanner(now, accent, free && !briefingActive && !Shown(Briefing));
    }
    void RecordAlertStart(string name, float now) => Timeline.Add(new TimelineEntry { Kind = "alert", Name = name, StartedAt = now, EndedAt = -1f });
    void RecordAlertEnd(float now)
    {
        for (int i = Timeline.Count - 1; i >= 0; i--) if (Timeline[i].Kind == "alert" && Timeline[i].EndedAt < 0f) { var e = Timeline[i]; e.EndedAt = now; Timeline[i] = e; return; }
    }
}
