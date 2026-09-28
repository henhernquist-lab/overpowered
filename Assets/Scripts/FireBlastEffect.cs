using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Projectile")]
public sealed class FireBlastEffect : PowerEffect
{
    static Mesh sphere;
    /// VisualPreset.ProjectileTail: three shrinking spheres trailing the shot in its own palette colour (a comet tail, so the
    /// projectile and its direction read in one frame). Visual only: no colliders, no shadows, destroyed with the shot.
    static void Tail(Transform shot, Vector3 direction, Material material)
    {
        if (sphere == null) sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        shot.rotation = Quaternion.LookRotation(direction);
        float[] back = { .7f, 1.3f, 1.8f }, size = { .72f, .48f, .3f };
        for (int i = 0; i < back.Length; i++)
        {
            var go = new GameObject("Projectile tail " + i); go.transform.SetParent(shot, false);
            go.transform.localPosition = Vector3.back * back[i]; go.transform.localScale = Vector3.one * size[i];
            go.AddComponent<MeshFilter>().sharedMesh = sphere; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }
    }
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var d = power.Definition;
        // Resolve before creating the sphere (otherwise the aim query can hit its own projectile).
        Vector3 direction=user.AimDirection,origin=user.AimOrigin;
        float radius=d.ProjectileSize*.5f;
        foreach(var hit in Physics.OverlapSphere(origin,radius,~0,QueryTriggerInteraction.Ignore))
            if(hit.transform.root!=user.transform)return false;
        float offset=d.OriginOffset;
        foreach(var hit in Physics.SphereCastAll(origin,radius,direction,offset,~0,QueryTriggerInteraction.Ignore))
            if(hit.transform.root!=user.transform)offset=Mathf.Min(offset,Mathf.Max(0,hit.distance-.01f));
        GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = d.DisplayName + " projectile";
        shot.transform.position = origin + direction * offset;
        shot.transform.localScale = Vector3.one * d.ProjectileSize;
        shot.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(d.PaletteColor);
        Rigidbody rb = shot.AddComponent<Rigidbody>(); rb.useGravity = false; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = direction * d.ProjectileSpeed;
        Physics.IgnoreCollision(shot.GetComponent<Collider>(), user.GetComponent<CharacterController>());
        shot.AddComponent<PowerProjectile>().Initialize(user, power);
        var preset = VisualPreset.Current;
        if (VisualPreset.Active(preset) && preset.ProjectileTail) Tail(shot.transform, direction, CityMaterials.Get(d.PaletteColor));
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
        CombatImpact.Blast(owner, transform.position, stats.Radius, stats.Force, stats.Damage, definition.UpwardForce,stats.Duration);
        Destroy(gameObject);
    }
}
