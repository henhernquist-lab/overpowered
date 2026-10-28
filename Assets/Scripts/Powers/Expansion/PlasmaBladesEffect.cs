using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/PlasmaBlades")]
public sealed class PlasmaBladesEffect : PowerEffect
{
    public override bool IsMelee=>true;
    public override bool HasRuntimePresentation=>true;
    public override bool Execute(PowerUser user,PowerRuntime power)=>user.Expansion.Melee(power,true);
}
