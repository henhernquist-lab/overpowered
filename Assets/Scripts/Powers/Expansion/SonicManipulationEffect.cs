using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/SonicManipulationEffect")]
public sealed class SonicManipulationEffect : PowerEffect
{public override bool Execute(PowerUser user,PowerRuntime power)=>user.Expansion.Directional(power,true);}
