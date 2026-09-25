using UnityEngine;

/// Session director strategy, referenced by GameModeDefinition.Director (same pattern as ModeRules).
/// Director assets are SHARED data that persist across Editor play sessions, so they hold tuning only:
/// Begin() adds a runtime ModeDirectorState component to the session object, and that component owns all run state.
public abstract class ModeDirector : ScriptableObject
{
    public abstract ModeDirectorState Begin(GameModeSession session);
}

/// Runtime half of a director. Driven only through GameModeSession.Tick, so pause/ended handling stays in one place.
public abstract class ModeDirectorState : MonoBehaviour
{
    public GameModeSession Session { get; private set; }
    public WorldSession World => Session.World;
    protected void Attach(GameModeSession session) { Session = session; }
    public abstract void Tick(float dt);
    /// One status line for the HUD, or null.
    public virtual string HudLine => null;
    /// Structured HUD values (e.g. WAVE 3, SCORE 120, BEST 300) for GameHud; the HUD renders whatever a director
    /// supplies, so it never parses HudLine or switches on the director type. Default: none.
    public virtual void HudStats(System.Collections.Generic.List<DirectorHudStat> into) { }
    /// Optional short status under the stats (e.g. "WAVE 2 IN 3s"), or null.
    public virtual string HudCaption => null;
    /// Adds director-owned facts (wave, enemies defeated) to the result before it is recorded.
    public virtual void Describe(SessionResult result) { }
    /// Director milestones worth a HUD banner (e.g. a cleared wave), with text built from the director's own values.
    public event System.Action<DirectorAnnouncement> Announced;
    protected void Announce(string eyebrow, string title, string reward) { Announced?.Invoke(new DirectorAnnouncement(eyebrow, title, reward)); }
}

/// One director milestone for the HUD banner: small eyebrow, title, reward chip (any may be empty).
public readonly struct DirectorAnnouncement
{
    public readonly string Eyebrow, Title, Reward;
    public DirectorAnnouncement(string eyebrow, string title, string reward) { Eyebrow = eyebrow; Title = title; Reward = reward; }
}

/// One labelled HUD value supplied by a ModeDirectorState (label is shown uppercase under the value).
public readonly struct DirectorHudStat
{
    public readonly string Label; public readonly int Value;
    public DirectorHudStat(string label, int value) { Label = label; Value = value; }
}
