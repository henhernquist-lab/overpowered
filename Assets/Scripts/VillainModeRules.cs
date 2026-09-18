using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Mode Rules/Villain Heist")]
public sealed class VillainModeRules : ModeRules
{
    public override string Objective(CrimeEncounter e) => $"Steal loot {e.LootTaken}/{e.Loot.Count} · wreck props {e.DestroyedProps}/{e.Definition.DestructionGoal} · sabotage {e.HazardsDone}/{e.Hazards.Count} · then escape {e.Definition.PlayerEscapeDistance:0}m";
    public override bool Complete(CrimeEncounter e) => e.LootTaken==e.Loot.Count && e.DestroyedProps>=e.Definition.DestructionGoal && e.HazardsDone==e.Hazards.Count &&
        Vector3.Distance(e.World.Hero.transform.position,e.Site)>e.Definition.PlayerEscapeDistance;
    public override bool Failed(CrimeEncounter e) => false; // Deadline and defeat limits still apply.
}
