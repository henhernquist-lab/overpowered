using System.Collections.Generic;
using UnityEngine;

/// A readable mission for one encounter (robbery getaway, hostage rescue, building fire, vault heist). Optional:
/// EncounterDefinition.Scenario == null keeps the original mixed encounter exactly. Same split as ModeDirector: this asset is
/// SHARED tuning; Begin() adds the per-encounter ScenarioState component that owns all run state. The scenario supplies its own
/// objective task labels (shown by the existing HUD objective line / waypoint through ModeRules.Current / Targets), its own
/// win and fail conditions, and its own interactions; CrimeEncounter keeps deadline, rewards and ending.
public abstract class EncounterScenario : ScriptableObject
{
    [Tooltip("Player-facing tasks in order; the HUD objective line shows the first incomplete one.")]
    public ObjectiveTaskLabel[] Tasks = new ObjectiveTaskLabel[0];
    [Tooltip("Shown as the encounter's interaction hint while it runs.")]
    [TextArea] public string Hint;
    public abstract ScenarioState Begin(CrimeEncounter encounter);
}
/// Runtime half of a scenario, on the encounter's GameObject (destroyed with it).
public abstract class ScenarioState : MonoBehaviour
{
    public CrimeEncounter Encounter { get; private set; }
    protected WorldSession World => Encounter.World;
    protected void Attach(CrimeEncounter encounter, EncounterScenario scenario) { Encounter = encounter; encounter.InteractionHint = scenario.Hint; }
    /// Called from CrimeEncounter.Tick (never while paused/ended).
    public virtual void Tick(float dt) { }
    /// Owns this NPC's movement this frame (true) or leaves it to the default behaviour.
    public virtual bool Drive(CityNpc npc) => false;
    public abstract bool Complete();
    public abstract bool Failed(out string reason);
    /// Progress of a scenario task kind (ObjectiveTask values the scenario defines); false = not handled here.
    public virtual bool Progress(ObjectiveTask task, out int done, out int total) { done = total = 0; return false; }
    public virtual void Targets(ObjectiveTask task, List<Vector3> into) { }
    /// Hold-R style interaction for this frame (dt 0 = key not held). True when something completed.
    public virtual bool Interact(float dt) => false;
    public virtual bool InteractableNear(Vector3 point) => false;
}
/// A world object a mission needs the player's powers to act on (fire spot, vault). CombatImpact.Blast (punch, kick, pound,
/// Fire Blast, synergy impacts), the Ice cast and the Laser Eyes beam report to it; it decides what counts.
public abstract class MissionTarget : MonoBehaviour
{
    /// Area/melee hit or beam tick: damage and outgoing impulse (N.s). Default: ignored.
    public virtual void Hit(float damage, float impulse, PowerUser source) { }
    /// Ice cast on it: true when the freeze did something (the cast is then paid). Default: not an Ice target.
    public virtual bool Freeze(PowerUser source, PowerStats stats) => false;
}
