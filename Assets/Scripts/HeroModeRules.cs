using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Mode Rules/Hero Rescue")]
public sealed class HeroModeRules : ModeRules
{
    public override string Objective(CrimeEncounter e) => $"Stop robbers {e.StoppedRobbers}/{e.Robbers.Count} · rescue {e.Rescued}/{e.Civilians.Count} · extinguish {e.HazardsDone}/{e.Hazards.Count}";
    public override bool Complete(CrimeEncounter e) => e.StoppedRobbers==e.Robbers.Count && e.Rescued==e.Civilians.Count && e.HazardsDone==e.Hazards.Count;
    public override bool Failed(CrimeEncounter e) => e.EscapedRobbers>0 || e.LostCivilians>0;
}
