using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Telekinesis")]
public sealed class TelekinesisEffect : PowerEffect
{
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        if (!user.FindTarget(user.Stats(power).Range, out RaycastHit hit)) return false;
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic || body.mass > power.Definition.HoldMaxMass) { user.Message = "Aim at a movable prop."; return false; }
        user.Grab(body, power);
        return true;
    }
}
