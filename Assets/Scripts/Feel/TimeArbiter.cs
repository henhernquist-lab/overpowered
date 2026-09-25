using UnityEngine;

/// The one place gameplay changes Time.timeScale during a session. Two clients:
/// - the pause menu (GameModeSession.SetPaused): timeScale 0 until unpaused. It always WINS: pausing cancels a running
///   hit pause, and a hit pause requested while paused (or while anything else holds timeScale at 0) is refused, so a
///   hit pause can never unpause the game.
/// - hit pause (FeelDirector): a short freeze measured in REAL time, rate-limited by a minimum real-time interval, that
///   restores the timeScale it found when it started — unless someone else changed timeScale meanwhile, in which case
///   that owner's value is left alone.
public static class TimeArbiter
{
    public static bool MenuPaused { get; private set; }
    public static bool HitPaused => hitPauseEnd > 0d;
    /// Verification counters (per process).
    public static int HitPausesStarted { get; private set; }
    public static int HitPausesRefusedRate { get; private set; }
    public static int HitPausesRefusedPaused { get; private set; }
    public static double LastHitPauseStart { get; private set; } = -1e9;
    /// Real seconds the last completed hit pause actually held timeScale (start to restore, frame-granular).
    public static double LastHitPauseHeld { get; private set; }
    static double hitPauseEnd;
    static float restoreScale = 1f, pauseScale;
    static double Now => Time.realtimeSinceStartupAsDouble;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { MenuPaused = false; hitPauseEnd = 0d; LastHitPauseStart = -1e9; HitPausesStarted = HitPausesRefusedRate = HitPausesRefusedPaused = 0; }

    public static void SetMenuPaused(bool pause)
    {
        MenuPaused = pause;
        if (pause)
        {
            if (HitPaused) { hitPauseEnd = 0d; LastHitPauseHeld = Now - LastHitPauseStart; }   // the menu wins; the hit pause is dropped
            Time.timeScale = 0f;
        }
        else Time.timeScale = 1f;
    }
    /// Request a hit pause of `seconds` real time at `scale`. Returns false when refused (menu paused, time already
    /// frozen by another owner, or inside `minInterval` real seconds of the previous hit pause's start).
    public static bool RequestHitPause(float seconds, float minInterval, float scale)
    {
        if (seconds <= 0f) return false;
        if (MenuPaused || (!HitPaused && Time.timeScale == 0f)) { HitPausesRefusedPaused++; return false; }
        double now = Now;
        if (HitPaused || now - LastHitPauseStart < minInterval) { HitPausesRefusedRate++; return false; }
        restoreScale = Time.timeScale; pauseScale = Mathf.Max(0f, scale);
        Time.timeScale = pauseScale; LastHitPauseStart = now; hitPauseEnd = now + seconds; HitPausesStarted++;
        return true;
    }
    /// Ends a due hit pause. Called every frame (FeelDirector, early execution order), in unscaled time.
    public static void Tick()
    {
        if (!HitPaused || Now < hitPauseEnd) return;
        End();
    }
    static void End()
    {
        hitPauseEnd = 0d; LastHitPauseHeld = Now - LastHitPauseStart;
        if (!MenuPaused && Time.timeScale == pauseScale) Time.timeScale = restoreScale;
    }
    /// Session teardown: forget menu/hit-pause state (the session itself restores timeScale 1).
    public static void Reset()
    {
        if (HitPaused) End();
        MenuPaused = false;
    }
}
