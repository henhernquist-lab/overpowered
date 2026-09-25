using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Effects/Thermal shock")]
public sealed class ThermalShockEffect : SynergyEffect
{
    public override bool CanBegin(SynergyRunner r)
    {
        if(!r.User.FindTarget(r.Definition.Range,out var hit))return false;r.Target=hit.point;return true;
    }
    public override IEnumerator Execute(SynergyRunner r)
    {
        r.User.GetComponent<HumanoidPresentation>().Cast();
        yield return new WaitForSeconds(r.Definition.LiftSeconds);
        r.AffectNearby(r.Target,r.Definition.Radius,r.Definition.Damage*(r.Definition.BonusMultiplier-1),r.Definition.FreezeSeconds,true);
        r.Impact(r.Target);
    }
}
