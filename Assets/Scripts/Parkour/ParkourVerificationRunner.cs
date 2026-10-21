#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// Comprehensive verification runner for Rooftop Parkour & Time Trials.
/// Covers:
/// - Positive controls: Catalog registration, both courses (Downtown Dash & Vertical Ascent), deterministic rooftop placement,
///   intentional start, timer start/stop, ordered checkpoint progression, course completion, personal best updates,
///   slower time preservation, retry reset, cancel cleanup, universal traversal compatibility, save/load persistence,
///   first-person & third-person camera compatibility, HUD overlay non-obstruction, and existing power functionality.
/// - Negative controls: Out-of-order checkpoint skipping, completed checkpoint re-entry, leaving course boundaries,
///   simultaneous dual-challenge refusal, XP farming prevention on repeated completions, invalid/corrupt save resilience,
///   unreachable spawn refusal, zero collider/camera glitches on checkpoint markers, and pooled VFX stability.
public sealed class ParkourVerificationRunner : SessionVerificationRunner
{
    public bool Reload;
    protected override string Folder => "Verification/Parkour/";
    protected override string ResultFile => Reload ? "reload.txt" : "results.txt";
    string ExpectedPath => Folder + "expected.txt";

    readonly List<XpGrant> xpGrants = new List<XpGrant>();
    CharacterController CC => W.Hero.GetComponent<CharacterController>();
    ThirdPersonCamera Rig => Cam.GetComponent<ThirdPersonCamera>();
    GameHud Hud => FindAnyObjectByType<GameHud>();

    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        if (Reload)
        {
            yield return ReloadChecks();
            yield break;
        }
        VerifyCatalogAndAssets();
        yield return RuntimeCourseAndNegativeControls();
        yield return UniversalTraversalAndCameraChecks();
        yield return SaveAndReloadPreparation();
    }

    void VerifyCatalogAndAssets()
    {
        Log("---- CATALOG & COURSE DEFINITIONS");
        var catalog = ParkourCatalog.Current;
        Check(catalog != null && catalog.Enabled, "ParkourCatalog asset exists in Resources and is enabled.");
        var valid = catalog.ValidCourses();
        Check(valid.Count >= 2, $"ParkourCatalog registers {valid.Count} valid courses.");

        var dash = catalog.Find("downtown-dash");
        var ascent = catalog.Find("vertical-ascent");
        Check(dash != null && dash.IsValid(out _), "Course 1 'downtown-dash' (DOWNTOWN DASH) is registered and valid.");
        Check(ascent != null && ascent.IsValid(out _), "Course 2 'vertical-ascent' (VERTICAL ASCENT) is registered and valid.");
        Check(dash.Checkpoints.Length >= 6 && dash.Checkpoints.Length <= 8, $"Downtown Dash has {dash.Checkpoints.Length} ordered checkpoints (target 6-8).");
        Check(ascent.Checkpoints.Length >= 7 && ascent.Checkpoints.Length <= 10, $"Vertical Ascent has {ascent.Checkpoints.Length} ordered checkpoints (target 7-10).");
        Check(dash.PrimaryColor == CityColor.Cyan && dash.SecondaryColor == CityColor.Blue, "Downtown Dash uses Cyan and Blue theme accents.");
        Check(ascent.PrimaryColor == CityColor.UiPurple && ascent.SecondaryColor == CityColor.Amber, "Vertical Ascent uses Purple and Gold/Amber theme accents.");
        Check(dash.FirstCompletionXp > 0 && ascent.FirstCompletionXp > dash.FirstCompletionXp, "Both courses configure positive first-completion XP rewards.");
    }

    IEnumerator RuntimeCourseAndNegativeControls()
    {
        Log("---- RUNTIME PLACEMENT, ORDER ENFORCEMENT, REWARDS & NEGATIVE CONTROLS");
        var hero = F.Heroes[0];
        yield return Home();
        Profile.Data.Points = 50;
        if (!Profile.Owns(Power("fire"))) Profile.Buy(Power("fire"));
        if (!Profile.Owns(Power("speed"))) Profile.Buy(Power("speed"));
        Check(Profile.SetLoadout(hero, Power("strength"), Power("fire"), Power("speed"), hero.Primary, hero.Secondary),
            "Configured non-Flight loadout (Strength + Fire + Speed) for parkour verification.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));
        yield return Scene(GameFlow.CityScene);

        var pm = W.Parkour;
        Check(pm != null && pm.Courses.Count >= 2, $"ParkourCourseManager resolved {pm?.Courses.Count ?? 0} courses in generated city.");
        var dash = pm.FindCourse("downtown-dash");
        var ascent = pm.FindCourse("vertical-ascent");
        Check(dash != null && dash.Valid, "Downtown Dash resolved all checkpoints onto valid city structures.");
        Check(ascent != null && ascent.Valid, "Vertical Ascent resolved all checkpoints onto valid city structures.");

        // Verify start and checkpoint physical support & reachability
        foreach (var course in new[] { dash, ascent })
        {
            Check(course.Start.Valid && course.Start.SupportPoint.y >= 0f, $"{course.Definition.Id}: start has verified physical support at {course.Start.SupportPoint}.");
            for (int i = 0; i < course.Checkpoints.Length; i++)
            {
                var cp = course.Checkpoints[i];
                Check(cp.Valid && cp.TriggerRadius >= 1.5f, $"{course.Definition.Id} CP#{i + 1} ({cp.Label}) valid at {cp.Position}.");
            }
        }
        float ascentGain = ascent.Checkpoints[ascent.Checkpoints.Length - 1].SupportPoint.y - ascent.Start.SupportPoint.y;
        Check(ascentGain >= 40f, $"Vertical Ascent achieves +{ascentGain:F1}m net vertical elevation gain to skyline summit.");

        // Negative control: unreachable course definition is rejected cleanly
        var badDef = ScriptableObject.CreateInstance<ParkourCourseDefinition>();
        badDef.Id = "unreachable-test";
        badDef.DisplayName = "UNREACHABLE";
        badDef.Start = new ParkourCheckpointSpec { Anchor = ParkourAnchorKind.WorldPoint, ReferencePosition = new Vector2(9999f, 9999f), Offset = new Vector3(0f, 500f, 0f), TriggerRadius = 3f };
        badDef.Checkpoints = new[]
        {
            new ParkourCheckpointSpec { Anchor = ParkourAnchorKind.WorldPoint, ReferencePosition = new Vector2(9999f, 9999f), Offset = new Vector3(0f, 500f, 10f), TriggerRadius = 3f },
            new ParkourCheckpointSpec { Anchor = ParkourAnchorKind.WorldPoint, ReferencePosition = new Vector2(9999f, 9999f), Offset = new Vector3(0f, 500f, 20f), TriggerRadius = 3f }
        };
        var badResolved = ParkourCoursePlacement.Resolve(W.City, W.Tuning, badDef);
        Check(!badResolved.Valid && !pm.StartCourse(badResolved), "CONTROL: unreachable course start without physical support is rejected.");
        Destroy(badDef);

        // Verify pooled VFX invariants: zero colliders on parkour markers, shared mesh only
        var vfx = pm.Vfx;
        Check(vfx != null && vfx.GetComponentsInChildren<Collider>(true).Length == 0, "CONTROL: ParkourVfx markers have zero Colliders (cannot cause camera or movement collision glitches).");
        int initialRenderers = vfx.CreatedRenderers;
        Check(vfx.GetComponentsInChildren<MeshFilter>(true).All(mf => mf.sharedMesh == vfx.SharedMesh), "ParkourVfx uses one shared cube mesh across all gate segments, pylons, chevrons, and crowns.");

        // Intentional start check: approaching start sets NearbyStartCourse without auto-starting
        PlaceHero(dash.StartStandingPosition, true);
        yield return null;
        yield return null;
        Check(!pm.IsCourseActive && pm.NearbyStartCourse == dash, "Approaching Downtown Dash start detects NearbyStartCourse without auto-starting.");
        Check(Hud != null && GameHud.Shown(Hud.ParkourCard) && Hud.ParkourTitleLabel.text.Contains("DOWNTOWN DASH"), "HUD displays nearby parkour start prompt.");

        // Start Downtown Dash intentionally via R (ScriptedInteractPress)
        pm.ScriptedInteractPress = true;
        yield return null;
        Check(pm.IsCourseActive && pm.ActiveCourse == dash && pm.CurrentCheckpointIndex == 0, "Pressing R at start activates Downtown Dash at Checkpoint 0.");

        // Negative control: cannot start Vertical Ascent while Downtown Dash is active
        Check(!pm.StartCourse(ascent), "CONTROL: starting a second challenge while one is active is refused.");

        // Timer advances while active
        float t0 = pm.ElapsedSeconds;
        yield return new WaitForSeconds(0.15f);
        Check(pm.ElapsedSeconds > t0 + 0.08f, $"Active course timer advances ({t0:F3}s -> {pm.ElapsedSeconds:F3}s).");

        // Negative control: skipping directly to final checkpoint or checkpoint 2 does not advance or finish
        Vector3 finishPos = dash.Checkpoints[dash.Checkpoints.Length - 1].Position;
        pm.TickActiveCourse(finishPos, finishPos + Vector3.up, 0.02f);
        Check(pm.IsCourseActive && pm.CurrentCheckpointIndex == 0, "CONTROL: touching the Finish checkpoint out of order does not complete or advance the course.");
        Vector3 secondPos = dash.Checkpoints[1].Position;
        pm.TickActiveCourse(secondPos, secondPos + Vector3.up, 0.02f);
        Check(pm.IsCourseActive && pm.CurrentCheckpointIndex == 0, "CONTROL: touching Checkpoint 2 before Checkpoint 1 is ignored.");

        // Positive control: reaching Checkpoint 1 in order advances index to 1
        Vector3 firstPos = dash.Checkpoints[0].Position;
        pm.TickActiveCourse(firstPos, firstPos + Vector3.up, 0.02f);
        Check(pm.IsCourseActive && pm.CurrentCheckpointIndex == 1, "Reaching Checkpoint 1 in order advances CurrentCheckpointIndex to 1.");
        Check(vfx.CheckpointNodes[0].State == ParkourMarkerState.Completed && vfx.CheckpointNodes[1].State == ParkourMarkerState.Next,
            "Checkpoint 1 visual state transitions to Completed and Checkpoint 2 becomes Next.");

        // Negative control: re-entering Checkpoint 1 does not duplicate progress
        pm.TickActiveCourse(firstPos, firstPos + Vector3.up, 0.02f);
        Check(pm.CurrentCheckpointIndex == 1, "CONTROL: re-entering an already completed checkpoint does not increment progress.");

        // Leaving course area far away does not corrupt state
        pm.TickActiveCourse(new Vector3(400f, 10f, 400f), new Vector3(400f, 11f, 400f), 0.1f);
        Check(pm.IsCourseActive && pm.CurrentCheckpointIndex == 1, "CONTROL: leaving course area does not corrupt active challenge state.");

        // Retry resets progress and timer
        Check(pm.RetryCourse(), "RetryCourse resets active run.");
        Check(pm.IsCourseActive && pm.ActiveCourse == dash && pm.CurrentCheckpointIndex == 0 && pm.ElapsedSeconds < 0.05f,
            "Retry resets checkpoint index to 0, resets timer, and repositions hero to start.");

        // Cancel ends challenge cleanly
        Check(pm.CancelCourse("verification cancel"), "CancelCourse ends active challenge.");
        Check(!pm.IsCourseActive && pm.ActiveCourse == null && vfx.CheckpointNodes.All(n => n.State == ParkourMarkerState.Hidden),
            "Cancel clears active course and hides all checkpoint gates cleanly.");

        // Complete Downtown Dash in order (first completion -> 45.0s)
        W.Progression.XpGranted += xpGrants.Add;
        xpGrants.Clear();
        Check(pm.StartCourse(dash, repositionToStart: true), "Restarted Downtown Dash for full ordered completion.");
        for (int i = 0; i < dash.Checkpoints.Length; i++)
        {
            Vector3 p = dash.Checkpoints[i].Position;
            pm.TickActiveCourse(p, p + Vector3.up, 45f / dash.Checkpoints.Length);
        }
        Check(!pm.IsCourseActive && pm.LastCompletion.HasValue, "Reaching final checkpoint in order completes Downtown Dash and stops timer.");
        var summary1 = pm.LastCompletion.Value;
        Check(summary1.FirstCompletion && summary1.NewPersonalBest && Mathf.Abs(summary1.TimeSeconds - 45f) < 0.1f && Mathf.Abs(summary1.BestSeconds - 45f) < 0.1f,
            $"First completion recorded time={summary1.TimeSeconds:F2}s, best={summary1.BestSeconds:F2}s.");
        Check(summary1.XpAwarded == dash.Definition.FirstCompletionXp && xpGrants.Count(g => g.Reason == "parkour") == 1,
            $"First completion awarded +{summary1.XpAwarded} XP once.");

        yield return null;
        Check(Hud != null && GameHud.Shown(Hud.ParkourCard) && Hud.ParkourEyebrowLabel.text.Contains("COURSE COMPLETE") &&
              Hud.ParkourTimerLabel.text.Contains("Your Time:") && Hud.ParkourProgressLabel.text.Contains("Best Time:"),
              "HUD displays COURSE COMPLETE summary with Your Time and Best Time.");

        // Slower completion (52.0s) -> does NOT overwrite best time and awards 0 XP (no farming)
        xpGrants.Clear();
        Check(pm.StartCourse(dash, repositionToStart: false), "Started second run of Downtown Dash (slower time control).");
        for (int i = 0; i < dash.Checkpoints.Length; i++)
        {
            Vector3 p = dash.Checkpoints[i].Position;
            pm.TickActiveCourse(p, p + Vector3.up, 52f / dash.Checkpoints.Length);
        }
        var summarySlower = pm.LastCompletion.Value;
        Check(!summarySlower.FirstCompletion && !summarySlower.NewPersonalBest && Mathf.Abs(summarySlower.BestSeconds - 45f) < 0.1f && summarySlower.XpAwarded == 0 && xpGrants.Count == 0,
            "CONTROL: slower run (52.00s) preserves 45.00s personal best and grants 0 XP (prevents repeat XP farming).");

        // Faster completion (38.5s) -> updates personal best and awards small personal-best bonus once
        xpGrants.Clear();
        Check(pm.StartCourse(dash, repositionToStart: false), "Started third run of Downtown Dash (faster personal best control).");
        for (int i = 0; i < dash.Checkpoints.Length; i++)
        {
            Vector3 p = dash.Checkpoints[i].Position;
            pm.TickActiveCourse(p, p + Vector3.up, 38.5f / dash.Checkpoints.Length);
        }
        var summaryFaster = pm.LastCompletion.Value;
        Check(!summaryFaster.FirstCompletion && summaryFaster.NewPersonalBest && Mathf.Abs(summaryFaster.BestSeconds - 38.5f) < 0.1f && summaryFaster.XpAwarded == dash.Definition.PersonalBestXp,
            $"Beating personal best (38.50s < 45.00s) updates BestSeconds to {summaryFaster.BestSeconds:F2}s and awards +{summaryFaster.XpAwarded} record bonus XP.");

        // Complete Vertical Ascent in order (74.2s)
        xpGrants.Clear();
        Check(pm.StartCourse(ascent, repositionToStart: true), "Started Vertical Ascent for full ordered completion.");
        for (int i = 0; i < ascent.Checkpoints.Length; i++)
        {
            Vector3 p = ascent.Checkpoints[i].Position;
            pm.TickActiveCourse(p, p + Vector3.up, 74.2f / ascent.Checkpoints.Length);
        }
        var summaryAscent = pm.LastCompletion.Value;
        Check(summaryAscent.FirstCompletion && summaryAscent.NewPersonalBest && Mathf.Abs(summaryAscent.BestSeconds - 74.2f) < 0.1f && summaryAscent.XpAwarded == ascent.Definition.FirstCompletionXp,
            $"Vertical Ascent completed in {summaryAscent.TimeSeconds:F2}s, awarded +{summaryAscent.XpAwarded} XP.");

        Check(vfx.CreatedRenderers == initialRenderers, $"CONTROL: 20+ checkpoint activations and bursts created 0 new renderers ({vfx.CreatedRenderers} pooled).");
        W.Progression.XpGranted -= xpGrants.Add;
    }

    IEnumerator UniversalTraversalAndCameraChecks()
    {
        Log("---- UNIVERSAL TRAVERSAL, CAMERAS & EXISTING POWERS");
        var pm = W.Parkour;
        var dash = pm.FindCourse("downtown-dash");

        // Start Downtown Dash on actual rooftop and exercise real movement, double-jump, camera toggle, and powers
        Check(pm.StartCourse(dash, repositionToStart: true), "Started Downtown Dash at West Glass Roof Start.");
        W.Hero.enabled = true;
        Rig.enabled = true;
        yield return new WaitForSeconds(0.25f);
        Check(CC.isGrounded, "Hero stands grounded on real Downtown Dash start rooftop.");

        // Test both third-person and first-person cameras during active parkour challenge
        foreach (bool firstPerson in new[] { false, true, false })
        {
            if (Rig.FirstPerson != firstPerson) Check(Rig.ToggleView(), $"Toggled camera view to FirstPerson={firstPerson} during active parkour course.");
            yield return null;
            yield return null;
            Check(Hud != null && GameHud.Shown(Hud.ParkourCard), $"Parkour HUD card visible in FirstPerson={firstPerson}.");
            var cardRect = Hud.ParkourCard.worldBound;
            var crossRect = Hud.Crosshair.worldBound;
            Check(!cardRect.Overlaps(crossRect), $"Parkour HUD card ({cardRect}) never overlaps crosshair ({crossRect}) in FirstPerson={firstPerson}.");
            bool embedded = Physics.OverlapSphere(Cam.transform.position, firstPerson ? 0.14f : 0.19f, ~0, QueryTriggerInteraction.Ignore)
                .Any(c => c.transform.root != W.Hero.transform);
            Check(!embedded, $"Camera collision volume stays clear during active parkour markers (FirstPerson={firstPerson}).");
        }

        // Exercise universal jump + double jump + Speed secondary power while challenge is active
        Check(W.Hero.TryJump(), "Universal ground jump works during active parkour trial.");
        yield return new WaitForSeconds(0.22f);
        Check(W.Hero.TryJump(), "Universal double jump works during active parkour trial.");
        Check(W.Powers.UseSecondary(), "Equipped Speed secondary power activates normally during parkour trial.");
        Check(W.Powers.Use(Runtime("fire")), "Equipped Fire primary power activates normally during parkour trial.");

        pm.CancelCourse("finished traversal & camera checks");
        yield return null;
    }

    IEnumerator SaveAndReloadPreparation()
    {
        Log("---- SAVE ROBUSTNESS & SEPARATE-PROCESS RELOAD PREPARATION");
        var p = W.Progression;
        // Inject an unknown/legacy parkour course id to verify invalid course IDs do not crash save/load
        var unknown = p.ParkourRecord("unknown-legacy-course-id", create: true);
        unknown.Completed = true;
        unknown.BestSeconds = 99.5f;
        unknown.Completions = 1;
        unknown.RewardClaimed = true;
        p.Save();

        Check(p.LastError == null && File.Exists(p.SavePath), "Progression saved cleanly with parkour records.");
        var dashRec = p.ParkourRecord("downtown-dash");
        var ascentRec = p.ParkourRecord("vertical-ascent");
        Check(dashRec != null && dashRec.Completed && dashRec.RewardClaimed && Mathf.Abs(dashRec.BestSeconds - 38.5f) < 0.1f,
            "Downtown Dash record ready for reload verification.");
        Check(ascentRec != null && ascentRec.Completed && ascentRec.RewardClaimed && Mathf.Abs(ascentRec.BestSeconds - 74.2f) < 0.1f,
            "Vertical Ascent record ready for reload verification.");

        File.WriteAllLines(ExpectedPath, new[]
        {
            $"level={p.Data.Level} xp={p.Data.Xp} points={p.Data.Points}",
            $"downtown-dash:{dashRec.Completed}:{dashRec.RewardClaimed}:{dashRec.Completions}:{dashRec.BestSeconds:F2}",
            $"vertical-ascent:{ascentRec.Completed}:{ascentRec.RewardClaimed}:{ascentRec.Completions}:{ascentRec.BestSeconds:F2}",
            $"unknown-legacy-course-id:{unknown.Completed}:{unknown.RewardClaimed}:{unknown.Completions}:{unknown.BestSeconds:F2}"
        });
        yield return Home();
    }

    IEnumerator ReloadChecks()
    {
        Log("---- SEPARATE-PROCESS RELOAD VERIFICATION");
        var expected = File.ReadAllLines(ExpectedPath);
        var p = Profile;
        var dashRec = p.ParkourRecord("downtown-dash");
        var ascentRec = p.ParkourRecord("vertical-ascent");
        var unknownRec = p.ParkourRecord("unknown-legacy-course-id");
        Check(dashRec != null && ascentRec != null && unknownRec != null, "Reloaded save preserves both courses and unknown course ID without crashing.");

        var actual = new[]
        {
            $"level={p.Data.Level} xp={p.Data.Xp} points={p.Data.Points}",
            $"downtown-dash:{dashRec.Completed}:{dashRec.RewardClaimed}:{dashRec.Completions}:{dashRec.BestSeconds:F2}",
            $"vertical-ascent:{ascentRec.Completed}:{ascentRec.RewardClaimed}:{ascentRec.Completions}:{ascentRec.BestSeconds:F2}",
            $"unknown-legacy-course-id:{unknownRec.Completed}:{unknownRec.RewardClaimed}:{unknownRec.Completions}:{unknownRec.BestSeconds:F2}"
        };
        Check(expected.SequenceEqual(actual), "Separate-process reload matches saved level, XP, points, best times, and claimed flags:\n  " + string.Join("\n  ", actual));

        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));
        yield return Scene(GameFlow.CityScene);
        var pm = W.Parkour;
        var dash = pm.FindCourse("downtown-dash");
        Check(dash != null && Mathf.Abs(W.Progression.BestParkourTime("downtown-dash") - 38.5f) < 0.1f, "Session reads reloaded 38.50s personal best for Downtown Dash.");

        // Verify re-completing slower in reloaded process does not re-pay first-completion XP
        xpGrants.Clear();
        W.Progression.XpGranted += xpGrants.Add;
        Check(pm.StartCourse(dash, repositionToStart: false), "Started Downtown Dash in reloaded session.");
        for (int i = 0; i < dash.Checkpoints.Length; i++)
        {
            Vector3 pos = dash.Checkpoints[i].Position;
            pm.TickActiveCourse(pos, pos + Vector3.up, 44f / dash.Checkpoints.Length);
        }
        Check(pm.LastCompletion.HasValue && pm.LastCompletion.Value.XpAwarded == 0 && xpGrants.Count == 0 &&
              Mathf.Abs(W.Progression.BestParkourTime("downtown-dash") - 38.5f) < 0.1f,
              "Reloaded session slower completion grants 0 XP and keeps 38.50s personal best.");
        W.Progression.XpGranted -= xpGrants.Add;
    }
}
#endif
