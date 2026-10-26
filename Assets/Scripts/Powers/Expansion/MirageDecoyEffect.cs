using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/MirageDecoy")]
public sealed class MirageDecoyEffect : PowerEffect
{
    public override bool Execute(PowerUser user,PowerRuntime power)=>user.Expansion.SummonDecoy(power);
}
