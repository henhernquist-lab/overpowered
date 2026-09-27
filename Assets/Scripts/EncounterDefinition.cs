using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Encounter")]
public sealed class EncounterDefinition : ScriptableObject
{
    public string DisplayName="Bank break-out";
    public CrimeKind Kind=CrimeKind.Robbery;
    public int Robbers=3, Civilians=2, RespondingCops=2, Cars=2, LooseProps=4, Loot=2, Hazards;
    public int DestructionGoal=3;
    public float Deadline=210f, RunForExitAfter=45f, CivilianDangerAfter=90f, CivilianDamagePerSecond=1f;
    public float RunnerSpeed=3f, RunnerEscapeDistance=40f, PlayerEscapeDistance=22f, EscapeReach=2f;
    public float Spacing=3f, PropMoveDistance=2f, InteractRadius=3f, HoldSeconds=1.2f;
    public float BlockadeOffset=4.5f;
    public float CopSuppressSeconds=.6f, CopSuppressDamage=2f;
    public float MarkerSize=.65f, MarkerHeight=1f;
    [Tooltip("Optional mission (robbery getaway, hostage rescue, building fire, vault heist). Null = the original mixed encounter; with a scenario the counts above that the scenario does not use should be 0.")]
    public EncounterScenario Scenario;
    public Color LootColor=new Color(1f,.8f,.1f), RescueColor=Color.cyan, HazardColor=new Color(1f,.25f,.04f);
}
