using UnityEngine;
using UnityEngine.UIElements;

/// HUD overlay card for Rooftop Parkour Time Trials.
/// Attached to GameHud.OverlayRoot (edges-only, top-left below LevelGroup, well outside the 40%x40% centre zone).
/// Displays:
/// - Active trial: COURSE NAME, TIME (MM:SS.xx), Checkpoint X / Y, Best Time, and Retry/Cancel hints.
/// - Completion summary: COURSE COMPLETE, Your Time, Best Time, NEW PERSONAL BEST badge, and Retry hint.
/// - Start prompt when standing near a course start beacon.
public sealed partial class GameHud
{
    public VisualElement ParkourCard { get; private set; }
    public Label ParkourEyebrowLabel { get; private set; }
    public Label ParkourTitleLabel { get; private set; }
    public Label ParkourTimerLabel { get; private set; }
    public Label ParkourProgressLabel { get; private set; }
    public Label ParkourSubLabel { get; private set; }
    public Label ParkourControlsLabel { get; private set; }

    string lastParkourEyebrow, lastParkourTitle, lastParkourTimer, lastParkourProgress, lastParkourSub, lastParkourControls;
    CityColor lastParkourBorderColor = CityColor.UiMuted;

    void BuildParkourHud()
    {
        if (OverlayRoot == null) return;
        var ink = C(CityColor.UiInk);
        var muted = C(CityColor.UiMuted);

        ParkourCard = Box("hud-parkour");
        Absolute(ParkourCard);
        ParkourCard.style.left = SafeMargin;
        ParkourCard.style.top = SafeMargin + 96f;
        ParkourCard.style.minWidth = 248f;
        ParkourCard.style.maxWidth = 310f;
        PanelBox(ParkourCard);
        Pad(ParkourCard, 10f, 14f);
        ParkourCard.style.borderLeftWidth = 4f;

        ParkourEyebrowLabel = Text("TIME TRIAL", 11, C(CityColor.Cyan));
        ParkourEyebrowLabel.name = "parkour-eyebrow";
        ParkourCard.Add(ParkourEyebrowLabel);

        ParkourTitleLabel = Text("DOWNTOWN DASH", 17, ink);
        ParkourTitleLabel.name = "parkour-title";
        ParkourTitleLabel.style.marginTop = 2f;
        ParkourCard.Add(ParkourTitleLabel);

        ParkourTimerLabel = Text("00:00.00", 24, ink);
        ParkourTimerLabel.name = "parkour-timer";
        ParkourTimerLabel.style.marginTop = 3f;
        ParkourCard.Add(ParkourTimerLabel);

        ParkourProgressLabel = Text("Checkpoint 1 / 7", 13, ink, false);
        ParkourProgressLabel.name = "parkour-progress";
        ParkourProgressLabel.style.marginTop = 2f;
        ParkourCard.Add(ParkourProgressLabel);

        ParkourSubLabel = Text("", 12, muted, false);
        ParkourSubLabel.name = "parkour-sub";
        ParkourSubLabel.style.marginTop = 2f;
        ParkourCard.Add(ParkourSubLabel);

        ParkourControlsLabel = Text("", 11, muted, false);
        ParkourControlsLabel.name = "parkour-controls";
        ParkourControlsLabel.style.marginTop = 5f;
        ParkourControlsLabel.style.whiteSpace = WhiteSpace.Normal;
        ParkourCard.Add(ParkourControlsLabel);

        OverlayRoot.Add(ParkourCard);
        Show(ParkourCard, false);
    }

    void UpdateParkourHud(WorldSession w, float now, bool allowed)
    {
        if (ParkourCard == null) BuildParkourHud();
        if (ParkourCard == null) return;

        var parkour = w != null ? w.Parkour : null;
        if (!allowed || parkour == null)
        {
            Show(ParkourCard, false);
            return;
        }

        // Keep below LevelGroup dynamically so they never overlap
        float topY = Shown(LevelGroup) && LevelGroup.layout.yMax > 10f ? LevelGroup.layout.yMax + 12f : SafeMargin + 96f;
        ParkourCard.style.top = topY;

        if (parkour.IsCourseActive)
        {
            var course = parkour.ActiveCourse;
            var def = course.Definition;
            int total = parkour.TotalCheckpoints;
            int currentDisplay = Mathf.Clamp(parkour.CurrentCheckpointIndex + 1, 1, Mathf.Max(1, total));
            bool checkpointFlash = now - parkour.LastCheckpointUnscaledTime < 0.65f;

            string eyebrow = checkpointFlash ? $"CHECKPOINT {parkour.CurrentCheckpointIndex} CLEARED!" : "ROOFTOP TIME TRIAL";
            string title = def.DisplayName.ToUpperInvariant();
            string timer = ParkourCourseManager.FormatTime(parkour.ElapsedSeconds);
            string progress = $"Checkpoint {currentDisplay} / {total}";
            float best = w.Progression != null ? w.Progression.BestParkourTime(def.Id) : 0f;
            string sub = best > 0f ? $"Best Time: {ParkourCourseManager.FormatTime(best)}" : $"Target Time: {ParkourCourseManager.FormatTime(def.TargetSeconds)}";
            string controls = "R Retry  ·  Backspace Cancel";

            SetParkourLabels(eyebrow, title, timer, progress, sub, controls, def.PrimaryColor);
            Show(ParkourTimerLabel, true);
            Show(ParkourProgressLabel, true);
            Show(ParkourSubLabel, true);
            Show(ParkourCard, true);
            return;
        }

        if (parkour.LastCompletion.HasValue)
        {
            var summary = parkour.LastCompletion.Value;
            if (now - summary.CompletedAtUnscaled <= ParkourCourseManager.CompletionDisplaySeconds)
            {
                var course = parkour.FindCourse(summary.CourseId);
                var accentColor = course != null ? course.Definition.PrimaryColor : CityColor.Cyan;
                string eyebrow = summary.NewPersonalBest ? "COURSE COMPLETE · NEW PERSONAL BEST!" : "COURSE COMPLETE";
                string title = summary.CourseName.ToUpperInvariant();
                string timer = $"Your Time: {ParkourCourseManager.FormatTime(summary.TimeSeconds)}";
                string progress = $"Best Time: {ParkourCourseManager.FormatTime(summary.BestSeconds)}";
                string sub = summary.XpAwarded > 0 ? $"+{summary.XpAwarded} XP Earned" : "Completed";
                string controls = "Press R to Retry";

                SetParkourLabels(eyebrow, title, timer, progress, sub, controls, accentColor);
                Show(ParkourTimerLabel, true);
                Show(ParkourProgressLabel, true);
                Show(ParkourSubLabel, true);
                Show(ParkourCard, true);
                return;
            }
        }

        if (parkour.NearbyStartCourse != null)
        {
            var course = parkour.NearbyStartCourse;
            var def = course.Definition;
            float best = w.Progression != null ? w.Progression.BestParkourTime(def.Id) : 0f;
            string eyebrow = "PARKOUR CHALLENGE NEARBY";
            string title = def.DisplayName.ToUpperInvariant();
            string timer = best > 0f ? $"Best Time: {ParkourCourseManager.FormatTime(best)}" : $"Checkpoints: {course.Checkpoints.Length}";
            string progress = best > 0f ? $"Checkpoints: {course.Checkpoints.Length}" : $"First Completion: +{def.FirstCompletionXp} XP";
            string sub = "";
            string controls = "Press R to Start Time Trial";

            SetParkourLabels(eyebrow, title, timer, progress, sub, controls, def.PrimaryColor);
            Show(ParkourTimerLabel, true);
            Show(ParkourProgressLabel, true);
            Show(ParkourSubLabel, false);
            Show(ParkourCard, true);
            return;
        }

        Show(ParkourCard, false);
    }

    void SetParkourLabels(string eyebrow, string title, string timer, string progress, string sub, string controls, CityColor themeColor)
    {
        if (lastParkourBorderColor != themeColor)
        {
            lastParkourBorderColor = themeColor;
            var c = C(themeColor);
            ParkourCard.style.borderLeftColor = c;
            ParkourEyebrowLabel.style.color = c;
        }
        if (lastParkourEyebrow != eyebrow) { lastParkourEyebrow = eyebrow; ParkourEyebrowLabel.text = eyebrow; }
        if (lastParkourTitle != title) { lastParkourTitle = title; ParkourTitleLabel.text = title; }
        if (lastParkourTimer != timer) { lastParkourTimer = timer; ParkourTimerLabel.text = timer; }
        if (lastParkourProgress != progress) { lastParkourProgress = progress; ParkourProgressLabel.text = progress; }
        if (lastParkourSub != sub) { lastParkourSub = sub; ParkourSubLabel.text = sub; }
        if (lastParkourControls != controls) { lastParkourControls = controls; ParkourControlsLabel.text = controls; }
    }
}
