using UnityEngine;

/// Brief evasion through the existing secondary activation/payment path.
[CreateAssetMenu(menuName="Overpowered/Effects/Phase Shift")]
public sealed class PhaseShiftEffect : PowerEffect
{
    public override bool Execute(PowerUser user, PowerRuntime power)
        => user.Phase != null && user.Phase.Begin(user.Stats(power).Duration);
}
