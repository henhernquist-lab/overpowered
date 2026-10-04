using UnityEngine;

/// <summary>A short blink that keeps the existing CharacterController and its collision rules.</summary>
[CreateAssetMenu(menuName = "Overpowered/Effects/Teleport")]
public sealed class TeleportEffect : PowerEffect
{
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        if (user.Hero != null && user.Hero.TryTeleport(user.Stats(power).Range)) return true;
        user.Message = "Teleport needs a clear path and landing";
        return false;
    }
}
