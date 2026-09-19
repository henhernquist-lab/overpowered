using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Projectile")]
public sealed class FireBlastEffect : PowerEffect
{
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var d = power.Definition;
        GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = d.DisplayName + " projectile";
        shot.transform.position = user.AimOrigin + user.AimDirection * d.OriginOffset;
        shot.transform.localScale = Vector3.one * d.ProjectileSize;
        shot.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(d.PaletteColor);
        Rigidbody rb = shot.AddComponent<Rigidbody>(); rb.useGravity = false; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = user.AimDirection * d.ProjectileSpeed;
        Physics.IgnoreCollision(shot.GetComponent<Collider>(), user.GetComponent<CharacterController>());
        shot.AddComponent<PowerProjectile>().Initialize(user, power);
        return true;
    }
}
public sealed class PowerProjectile : MonoBehaviour
{
    PowerUser owner; PowerDefinition definition; PowerStats stats; bool exploded;
    public void Initialize(PowerUser user, PowerRuntime power) { owner = user; definition = power.Definition; stats = user.Stats(power); Destroy(gameObject, stats.Duration); }
    void OnCollisionEnter(Collision collision)
    {
        if (exploded) return; exploded = true;
        CombatImpact.Blast(owner, transform.position, stats.Radius, stats.Force, stats.Damage, definition.UpwardForce);
        Destroy(gameObject);
    }
}
