using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Flight")]
public sealed class FlightEffect : PowerEffect
{
    public override bool IsFlight => true;
    public override bool Execute(PowerUser user, PowerRuntime power) { user.Message = "Hold F in the air; Space ascends."; return false; }
}
