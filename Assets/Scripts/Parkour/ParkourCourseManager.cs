using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ParkourCourseRecord
{
    public string Id;
    public float BestSeconds;
    public int Completions;
    public bool Completed;
    public bool RewardClaimed;
}

public readonly struct ParkourCompletionSummary
{
    public readonly string CourseId;
    public readonly string CourseName;
    public readonly float TimeSeconds;
    public readonly float BestSeconds;
    public readonly bool NewPersonalBest;
    public readonly bool FirstCompletion;
    public readonly int XpAwarded;
    public readonly float CompletedAtUnscaled;

    public ParkourCompletionSummary(
        string courseId,
        string courseName,
        float timeSeconds,
        float bestSeconds,
        bool newPersonalBest,
        bool firstCompletion,
        int xpAwarded,
        float completedAtUnscaled)
    {
        CourseId = courseId;
        CourseName = courseName;
        TimeSeconds = timeSeconds;
        BestSeconds = bestSeconds;
        NewPersonalBest = newPersonalBest;
        FirstCompletion = firstCompletion;
        XpAwarded = xpAwarded;
        CompletedAtUnscaled = completedAtUnscaled;
    }
}

/// Session runtime manager for rooftop parkour time trials.
/// Handles course placement validation, start proximity/interaction, ordered checkpoint progression,
/// active timer, retry/cancel controls, completion rewards, and progression persistence.
public sealed class ParkourCourseManager : MonoBehaviour
{
    public const KeyCode StartAndRetryKey = KeyCode.R;
    public const KeyCode CancelKey = KeyCode.Backspace;
    public const float CompletionDisplaySeconds = 8.5f;

    public static ParkourCourseManager Instance { get; private set; }

    public WorldSession World { get; private set; }
    public ParkourVfx Vfx { get; private set; }
    public readonly List<ParkourResolvedCourse> Courses = new List<ParkourResolvedCourse>();

    public ParkourResolvedCourse ActiveCourse { get; private set; }
    public ParkourResolvedCourse NearbyStartCourse { get; private set; }
    public ParkourResolvedCourse LastCompletedCourse { get; private set; }
    public ParkourCompletionSummary? LastCompletion { get; private set; }

    public bool IsCourseActive => ActiveCourse != null;
    public float ElapsedSeconds { get; private set; }
    public int CurrentCheckpointIndex { get; private set; }
    public int TotalCheckpoints => ActiveCourse != null ? ActiveCourse.Checkpoints.Length : 0;

    /// Verification / scripted input hooks
    public bool ScriptedInteractPress;
    public bool ScriptedRetryPress;
    public bool ScriptedCancelPress;

    public event Action<ParkourResolvedCourse> CourseStarted;
    public event Action<ParkourResolvedCourse, int, int> CheckpointReached;
    public event Action<ParkourCompletionSummary> CourseCompleted;
    public event Action<ParkourResolvedCourse, string> CourseCancelled;

    float lastCheckpointAt = -100f;
    public float LastCheckpointUnscaledTime => lastCheckpointAt;

    public void Initialize(WorldSession world, ParkourCatalog catalogOverride = null)
    {
        Instance = this;
        World = world;
        Vfx = ParkourVfx.Get();
        var catalog = catalogOverride != null ? catalogOverride : ParkourCatalog.Current;
        BuildCourses(catalog != null && catalog.Enabled ? catalog.Courses : Array.Empty<ParkourCourseDefinition>());
        if (world != null) world.PlayerRespawned += OnPlayerRespawned;
    }

    public void BuildCourses(IEnumerable<ParkourCourseDefinition> definitions)
    {
        CancelCourse("rebuilding courses");
        Courses.Clear();
        var seen = new HashSet<string>();
        if (definitions != null && World != null && World.City != null)
        {
            foreach (var def in definitions)
            {
                if (def == null || string.IsNullOrWhiteSpace(def.Id) || !seen.Add(def.Id)) continue;
                var resolved = ParkourCoursePlacement.Resolve(World.City, World.Tuning, def);
                if (resolved.Valid)
                    Courses.Add(resolved);
                else
                    Debug.LogWarning($"[Parkour] Course '{def.Id}' skipped: {resolved.FailureReason}");
            }
        }
        RefreshStartMarkers();
    }

    public ParkourResolvedCourse FindCourse(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < Courses.Count; i++)
            if (Courses[i].Definition.Id == id) return Courses[i];
        return null;
    }

    void RefreshStartMarkers()
    {
        if (Vfx == null) return;
        for (int i = 0; i < ParkourVfx.MaxStartSlots; i++)
        {
            if (i < Courses.Count)
            {
                var c = Courses[i];
                var state = ActiveCourse == c ? ParkourMarkerState.StartActive : ParkourMarkerState.StartIdle;
                Vector3 fwd = c.Checkpoints.Length > 0 ? (c.Checkpoints[0].Position - c.Start.Position) : Vector3.forward;
                Vfx.ConfigureStartMarker(i, c.Start.Position, fwd, c.Definition.PrimaryColor, c.Definition.SecondaryColor, state);
            }
            else
            {
                Vfx.ConfigureStartMarker(i, Vector3.zero, Vector3.forward, CityColor.Cyan, CityColor.Blue, ParkourMarkerState.Hidden);
            }
        }
    }

    void RefreshCheckpointMarkers()
    {
        if (Vfx == null) return;
        if (ActiveCourse == null)
        {
            Vfx.HideAllCheckpoints();
            return;
        }
        var c = ActiveCourse;
        var def = c.Definition;
        int count = Mathf.Min(c.Checkpoints.Length, ParkourVfx.MaxCheckpointSlots);
        for (int i = 0; i < ParkourVfx.MaxCheckpointSlots; i++)
        {
            if (i >= count)
            {
                Vfx.ConfigureCheckpoint(i, Vector3.zero, Vector3.forward, 3f, def.PrimaryColor, def.SecondaryColor, def.FinishColor, ParkourMarkerState.Hidden);
                continue;
            }
            bool isLast = i == count - 1;
            ParkourMarkerState state;
            if (i < CurrentCheckpointIndex)
                state = ParkourMarkerState.Completed;
            else if (i == CurrentCheckpointIndex)
                state = isLast ? ParkourMarkerState.FinishNext : ParkourMarkerState.Next;
            else
                state = isLast ? ParkourMarkerState.FinishInactive : ParkourMarkerState.Inactive;

            Vector3 pos = c.Checkpoints[i].Position;
            Vector3 nextDir = i + 1 < count ? (c.Checkpoints[i + 1].Position - pos) : (pos - (i > 0 ? c.Checkpoints[i - 1].Position : c.Start.Position));
            Vfx.ConfigureCheckpoint(i, pos, nextDir, c.Checkpoints[i].TriggerRadius, def.PrimaryColor, def.SecondaryColor, def.FinishColor, state);
        }
    }

    /// Starts the specified course if no other course is currently active.
    public bool StartCourse(ParkourResolvedCourse course, bool repositionToStart = false)
    {
        if (course == null || !course.Valid || World == null || World.PlayerDead || (World.Mode != null && World.Mode.Ended))
            return false;
        if (ActiveCourse != null && ActiveCourse != course)
            return false;

        ActiveCourse = course;
        LastCompletion = null;
        ElapsedSeconds = 0f;
        CurrentCheckpointIndex = 0;
        lastCheckpointAt = -100f;

        if (repositionToStart && World.Hero != null)
        {
            var cc = World.Hero.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            World.Hero.transform.SetPositionAndRotation(course.StartStandingPosition, course.StartFacing);
            if (cc != null) cc.enabled = true;
            World.Hero.ResetMotion();
            Physics.SyncTransforms();
        }

        RefreshStartMarkers();
        RefreshCheckpointMarkers();
        Vfx?.EmitBurst(course.Start.Position, course.Definition.PrimaryColor, 14);
        if (AudioDirector.Instance != null) AudioDirector.Instance.Play(AudioCue.UiClick, course.Start.Position);
        CourseStarted?.Invoke(course);
        return true;
    }

    public bool StartCourseById(string courseId, bool repositionToStart = false)
    {
        return StartCourse(FindCourse(courseId), repositionToStart);
    }

    /// Resets progress and timer on the active (or most recently completed) course and places the hero at its start.
    public bool RetryCourse()
    {
        var target = ActiveCourse ?? LastCompletedCourse ?? NearbyStartCourse;
        if (target == null) return false;
        ActiveCourse = null;
        return StartCourse(target, repositionToStart: true);
    }

    /// Cleanly aborts the active course without corrupting progression or world state.
    public bool CancelCourse(string reason = "cancelled")
    {
        if (ActiveCourse == null) return false;
        var cancelled = ActiveCourse;
        ActiveCourse = null;
        ElapsedSeconds = 0f;
        CurrentCheckpointIndex = 0;
        RefreshStartMarkers();
        RefreshCheckpointMarkers();
        CourseCancelled?.Invoke(cancelled, reason);
        return true;
    }

    void OnPlayerRespawned()
    {
        if (ActiveCourse != null) CancelCourse("player defeated");
    }

    void Update()
    {
        if (World == null || World.Hero == null || World.Tuning == null) return;
        if (World.Mode != null && (World.Mode.Ended || World.Mode.Paused)) return;
        if (World.MenuOpen || World.PlayerDead) return;

        Vector3 heroFeet = World.Hero.transform.position;
        Vector3 heroCenter = heroFeet + Vector3.up * (World.Tuning.Movement.Height * 0.5f);

        UpdateNearbyStart(heroFeet, heroCenter);

        bool interactPressed = ScriptedInteractPress || Input.GetKeyDown(StartAndRetryKey);
        bool retryPressed = ScriptedRetryPress;
        bool cancelPressed = ScriptedCancelPress || Input.GetKeyDown(CancelKey);
        ScriptedInteractPress = false;
        ScriptedRetryPress = false;
        ScriptedCancelPress = false;

        if (ActiveCourse != null)
        {
            if (cancelPressed)
            {
                CancelCourse("player cancelled");
                return;
            }
            if (retryPressed || interactPressed)
            {
                // Avoid conflicting with an objective robber/civilian R-hold right next to the player
                if (!IsEncounterInteractableNear(heroFeet))
                {
                    RetryCourse();
                    return;
                }
            }
            TickActiveCourse(heroFeet, heroCenter, Time.deltaTime);
        }
        else
        {
            if (retryPressed && LastCompletedCourse != null)
            {
                RetryCourse();
                return;
            }
            if (interactPressed)
            {
                if (NearbyStartCourse != null && !IsEncounterInteractableNear(heroFeet))
                {
                    StartCourse(NearbyStartCourse, repositionToStart: false);
                }
                else if (LastCompletion.HasValue && Time.unscaledTime - LastCompletion.Value.CompletedAtUnscaled <= CompletionDisplaySeconds && !IsEncounterInteractableNear(heroFeet))
                {
                    RetryCourse();
                }
            }
        }
    }

    bool IsEncounterInteractableNear(Vector3 heroPosition)
    {
        if (World == null || World.Crimes == null) return false;
        for (int i = 0; i < World.Crimes.Count; i++)
        {
            var crime = World.Crimes[i];
            if (crime != null && !crime.Resolved && crime.Encounter != null && crime.Encounter.InteractableNear(heroPosition))
                return true;
        }
        return false;
    }

    void UpdateNearbyStart(Vector3 heroFeet, Vector3 heroCenter)
    {
        NearbyStartCourse = null;
        if (ActiveCourse != null) return;
        float bestSq = float.MaxValue;
        for (int i = 0; i < Courses.Count; i++)
        {
            var c = Courses[i];
            float r = Mathf.Max(2f, c.Definition.StartInteractRadius);
            float d2 = PointToSegmentDistanceSq(c.Start.Position, heroFeet, heroCenter + Vector3.up * 0.4f);
            if (d2 <= r * r && d2 < bestSq)
            {
                bestSq = d2;
                NearbyStartCourse = c;
            }
        }
    }

    public void TickActiveCourse(Vector3 heroFeet, Vector3 heroCenter, float dt)
    {
        if (ActiveCourse == null) return;
        if (dt > 0f) ElapsedSeconds += dt;

        // Enforced checkpoint order: only CurrentCheckpointIndex can be triggered.
        // Entering later checkpoints (including Finish) out of order or re-entering completed ones is ignored.
        if (CurrentCheckpointIndex < 0 || CurrentCheckpointIndex >= ActiveCourse.Checkpoints.Length) return;

        var cp = ActiveCourse.Checkpoints[CurrentCheckpointIndex];
        float r = cp.TriggerRadius;
        float distSq = PointToSegmentDistanceSq(cp.Position, heroFeet, heroCenter + Vector3.up * 0.5f);
        if (distSq <= r * r)
        {
            AdvanceCheckpoint();
        }
    }

    void AdvanceCheckpoint()
    {
        var course = ActiveCourse;
        if (course == null) return;
        int reachedIndex = CurrentCheckpointIndex;
        int total = course.Checkpoints.Length;
        Vector3 cpPos = course.Checkpoints[reachedIndex].Position;
        CurrentCheckpointIndex++;
        lastCheckpointAt = Time.unscaledTime;

        Vfx?.EmitBurst(cpPos, reachedIndex == total - 1 ? course.Definition.FinishColor : course.Definition.PrimaryColor, reachedIndex == total - 1 ? 20 : 12);
        if (AudioDirector.Instance != null) AudioDirector.Instance.Play(AudioCue.UiClick, cpPos);
        CheckpointReached?.Invoke(course, reachedIndex + 1, total);

        if (CurrentCheckpointIndex >= total)
        {
            CompleteActiveCourse(cpPos);
        }
        else
        {
            RefreshCheckpointMarkers();
        }
    }

    void CompleteActiveCourse(Vector3 finishPosition)
    {
        var course = ActiveCourse;
        float finishTime = Mathf.Max(0.01f, ElapsedSeconds);
        ActiveCourse = null;
        LastCompletedCourse = course;
        CurrentCheckpointIndex = 0;
        ElapsedSeconds = 0f;

        RefreshStartMarkers();
        RefreshCheckpointMarkers();

        var summary = World.Progression != null
            ? World.Progression.RecordParkourCompletion(course.Definition, finishTime, finishPosition)
            : new ParkourCompletionSummary(course.Definition.Id, course.Definition.DisplayName, finishTime, finishTime, true, true, 0, Time.unscaledTime);

        LastCompletion = summary;
        CourseCompleted?.Invoke(summary);
    }

    static float PointToSegmentDistanceSq(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f) return (p - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
        Vector3 closest = a + ab * t;
        return (p - closest).sqrMagnitude;
    }

    public static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int mins = Mathf.FloorToInt(seconds / 60f);
        float rem = seconds - mins * 60f;
        return $"{mins:00}:{rem:00.00}";
    }

    void OnDestroy()
    {
        if (World != null) World.PlayerRespawned -= OnPlayerRespawned;
        if (Instance == this) Instance = null;
    }
}
