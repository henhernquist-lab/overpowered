using UnityEngine;

/// Presentation input only: an Animator adapter can consume the same state and events.
public readonly struct HeroPresentationState
{
    public readonly Vector3 LocalVelocity;
    public readonly bool Grounded;
    public readonly bool Flying;

    public HeroPresentationState(Vector3 localVelocity, bool grounded, bool flying)
    {
        LocalVelocity = localVelocity;
        Grounded = grounded;
        Flying = flying;
    }
}
