using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Punch")]
public sealed class PunchEffect : PowerEffect
{
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        user.Hero.PerformPunch(power.Definition, user.Stats(power));
        return true;
    }
}
