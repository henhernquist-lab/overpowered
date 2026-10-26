using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/GrappleLine")]
public sealed class GrappleLineEffect : PowerEffect
{
    public override bool Execute(PowerUser user,PowerRuntime power)=>user.Expansion.BeginGrapple(power);
}
