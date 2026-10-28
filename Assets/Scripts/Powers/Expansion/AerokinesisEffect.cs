using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/AerokinesisEffect")]
public sealed class AerokinesisEffect : PowerEffect
{public override bool Execute(PowerUser user,PowerRuntime power)=>user.Expansion.Directional(power,false);}
