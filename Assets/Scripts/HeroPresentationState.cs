using UnityEngine;

/// Presentation input only: an Animator adapter can consume the same state and events.
public readonly struct HeroPresentationState
{
    public readonly Vector3 LocalVelocity;
    public readonly bool Grounded;
    public readonly bool Flying;
    public readonly bool Climbing;

    public HeroPresentationState(Vector3 localVelocity, bool grounded, bool flying, bool climbing=false)
    {
        LocalVelocity = localVelocity;
        Grounded = grounded;
        Flying = flying;
        Climbing = climbing;
    }
}
