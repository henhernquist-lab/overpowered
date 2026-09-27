using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Telekinesis")]
public sealed class TelekinesisEffect : PowerEffect
{
    [Header("Directed throw")]
    public float MinimumThrowSpeed = 22f, MaximumThrowSpeed = 40f;
    public float DamageReferenceMass = 45f, MinimumDamageScale = .5f, MaximumDamageScale = 3f;
    public float MomentumTransfer = .65f, MaxImpactImpulse = 2400f, ImpactLift = .3f;
    public float ImpactSeparationSeconds = .2f;
    public void Throw(PowerUser user, PowerRuntime power, Rigidbody body)
    {
        var stats=user.Stats(power);
        Vector3 destination=user.AimPoint(stats.Range,body);
        // Same crosshair target as Fire, converged from the actual prop. Compensate gravity and existing orbit velocity
        // with an impulse, never a teleport or a per-frame homing correction. Heavy props get a usable launch speed.
        float speed=Mathf.Clamp(stats.Force/body.mass,MinimumThrowSpeed,MaximumThrowSpeed);
        float seconds=Mathf.Max(Time.fixedDeltaTime,Vector3.Distance(body.worldCenterOfMass,destination)/speed);
        Vector3 velocity=(destination-body.worldCenterOfMass)/seconds;
        if(body.useGravity)velocity-=Physics.gravity*(seconds+Time.fixedDeltaTime)*.5f;
        Vector3 impulse=(velocity-body.linearVelocity)*body.mass;
        body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        body.AddForce(impulse,ForceMode.Impulse);
        var impact=body.GetComponent<ThrownProp>();if(impact==null)impact=body.gameObject.AddComponent<ThrownProp>();
        float scale=Mathf.Clamp(Mathf.Sqrt(body.mass/Mathf.Max(1,DamageReferenceMass)),MinimumDamageScale,MaximumDamageScale);
        impact.Initialize(user,stats.Damage*scale,stats.Duration,this,impulse);
    }
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        if (!user.FindTarget(user.Stats(power).Range, out RaycastHit hit)) return false;
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic || body.mass > power.Definition.HoldMaxMass) { user.Message = "Aim at a movable prop."; return false; }
        user.Grab(body, power);
        return true;
    }
}
