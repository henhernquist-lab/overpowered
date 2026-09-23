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
    /// Adds director-owned facts (wave, enemies defeated) to the result before it is recorded.
    public virtual void Describe(SessionResult result) { }
}
