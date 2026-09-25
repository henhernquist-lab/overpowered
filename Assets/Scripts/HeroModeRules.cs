using UnityEngine;
/// Objective text is data: the task labels live on Resources/ModeRules/Hero.asset (ModeRules.Tasks).
[CreateAssetMenu(menuName="Overpowered/Mode Rules/Hero Rescue")]
public sealed class HeroModeRules : ModeRules
{
    public override bool Complete(CrimeEncounter e) => e.StoppedRobbers==e.Robbers.Count && e.Rescued==e.Civilians.Count && e.HazardsDone==e.Hazards.Count;
    public override bool Failed(CrimeEncounter e) => e.EscapedRobbers>0 || e.LostCivilians>0;
}
