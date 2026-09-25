using UnityEngine;
/// Objective text is data: the task labels live on Resources/ModeRules/Villain.asset (ModeRules.Tasks).
[CreateAssetMenu(menuName="Overpowered/Mode Rules/Villain Heist")]
public sealed class VillainModeRules : ModeRules
{
    public override bool Complete(CrimeEncounter e) => e.LootTaken==e.Loot.Count && e.DestroyedProps>=e.Definition.DestructionGoal && e.HazardsDone==e.Hazards.Count &&
        Vector3.Distance(e.World.Hero.transform.position,e.Site)>e.Definition.PlayerEscapeDistance;
    public override bool Failed(CrimeEncounter e) => false; // Deadline and defeat limits still apply.
}
