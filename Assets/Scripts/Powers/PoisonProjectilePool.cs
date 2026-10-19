using UnityEngine;
using UnityEngine.Rendering;

/// Session-owned Poison dart/glob projectiles and contact feedback.
/// The physics collider is gameplay; every visible mesh, renderer, and pooled instance is created once and reused.
public sealed class PoisonProjectilePool : MonoBehaviour
{
    public const int Capacity = 12;
    PoisonProjectile[] shots;
    Mesh dartMesh, tipMesh;
    public static PoisonProjectilePool Instance { get; private set; }
    public int Recycled { get; set; }
    public int ActiveCount
    {
        get
        {
            int n = 0;
            if (shots != null) foreach (var shot in shots) if (shot.gameObject.activeSelf) n++;
            return n;
        }
    }
    public PoisonProjectile Slot(int i) => shots[i];

    public static PoisonProjectilePool Create(WorldSession world)
    {
        if (Instance != null) return Instance;
        var root = new GameObject("Pooled Poison projectiles");
        root.transform.SetParent(world.transform, false);
        var pool = root.AddComponent<PoisonProjectilePool>();
        pool.Build();
        return pool;
    }

    void Build()
    {
        Instance = this;
        dartMesh = Shape("Poison needle body",
            new[] { -.28f, -.16f, -.02f, .12f, .22f, .28f },
            new[] { .012f, .035f, .052f, .048f, .026f, 0f });
        tipMesh = Shape("Poison needle tip",
            new[] { .10f, .20f, .26f },
            new[] { .035f, .022f, 0f });

        var toxicBody = CityMaterials.Get(CityColor.Leaf);
        var toxicGlow = CityMaterials.GetVfx(CityColor.Leaf);
        var tipGlow = CityMaterials.GetVfx(CityColor.Cream);

        shots = new PoisonProjectile[Capacity];
        for (int i = 0; i < shots.Length; i++)
        {
            var root = new GameObject("Pooled Poison projectile " + i);
            root.transform.SetParent(transform, false);
            root.SetActive(false);

            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = .10f;

            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var visual = new GameObject("Directional poison dart");
            visual.transform.SetParent(root.transform, false);
            var presentation = visual.AddComponent<PoisonProjectileVisual>();
            presentation.Build(dartMesh, tipMesh, toxicBody, toxicGlow, tipGlow);

            var projectile = root.AddComponent<PoisonProjectile>();
            projectile.Bind(this, presentation);
            shots[i] = projectile;
        }
    }

    public bool Launch(PowerUser user, PowerRuntime power, PoisonEffect effect, Vector3 position, Vector3 direction)
    {
        foreach (var shot in shots)
        {
            if (!shot.gameObject.activeSelf)
            {
                shot.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
                shot.Visual.gameObject.SetActive(true);
                shot.gameObject.SetActive(true);
                shot.Initialize(user, power, effect, direction);
                Physics.IgnoreCollision(shot.GetComponent<SphereCollider>(), user.GetComponent<CharacterController>());
                FeelDirector.Instance?.Particles?.Burst(position, power.Definition.PaletteColor, 6);
                return true;
            }
        }
        Debug.LogWarning("Poison projectile pool capacity reached; cast refused before resource payment.");
        return false;
    }

    static Mesh Shape(string name, float[] z, float[] radius)
    {
        const int sides = 8;
        var vertices = new Vector3[z.Length * sides];
        var triangles = new int[(z.Length - 1) * sides * 6];
        for (int i = 0; i < z.Length; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                float a = j * Mathf.PI * 2f / sides;
                vertices[i * sides + j] = new Vector3(Mathf.Cos(a) * radius[i], Mathf.Sin(a) * radius[i], z[i]);
            }
        }
        int t = 0;
        for (int i = 0; i < z.Length - 1; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = i * sides + j, b = i * sides + (j + 1) % sides, c = (i + 1) * sides + j, d = (i + 1) * sides + (j + 1) % sides;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }
        }
        var mesh = new Mesh { name = name };
        mesh.vertices = vertices; mesh.triangles = triangles;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (dartMesh != null) Destroy(dartMesh);
        if (tipMesh != null) Destroy(tipMesh);
    }
}

public sealed class PoisonProjectileVisual : MonoBehaviour
{
    float size = 1f;
    bool firstPerson;
    public MeshRenderer Body { get; private set; }
    public MeshRenderer Glow { get; private set; }
    public MeshRenderer Tip { get; private set; }

    static MeshRenderer Layer(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    public void Build(Mesh dart, Mesh tip, Material bodyMat, Material glowMat, Material tipMat)
    {
        Body = Layer("Toxic dart body", transform, dart, bodyMat, Vector3.zero, Vector3.one);
        Glow = Layer("Toxic dart glow", transform, dart, glowMat, Vector3.zero, Vector3.one * 1.15f);
        Tip = Layer("Toxic dart tip", transform, tip, tipMat, new Vector3(0, 0, .05f), Vector3.one * .7f);
    }

    public void SetSize(float diameter)
    {
        size = diameter / .18f;
        transform.localScale = Vector3.one * size;
    }

    public void SetFirstPerson(bool value)
    {
        firstPerson = value;
        transform.localPosition = value ? new Vector3(.18f, -.12f, -.06f) : Vector3.zero;
    }

    public void Present(float age, float clearance)
    {
        float t = Mathf.Clamp01(age / .12f);
        transform.localScale = Vector3.one * (size * clearance * (firstPerson ? Mathf.Lerp(.65f, 1f, t) : 1f));
        if (firstPerson) transform.localPosition = Vector3.Lerp(new Vector3(.18f, -.12f, -.06f), Vector3.zero, t);
    }
}

public sealed class PoisonProjectile : MonoBehaviour
{
    PowerUser owner;
    PowerDefinition definition;
    PowerStats stats;
    PoisonEffect poisonSettings;
    PoisonProjectilePool pool;
    Rigidbody body;
    SphereCollider sphere;
    PoisonProjectileVisual visual;
    bool hitResolved, returnPending;
    float expires, launchedAt, dropEmberAt;
    string credit;
    readonly RaycastHit[] visualHits = new RaycastHit[8];

    public PoisonProjectileVisual Visual => visual;
    public Vector3 LaunchDirection { get; private set; }
    public Vector3 LastImpactPoint { get; private set; }
    public bool HitResolved => hitResolved;

    public void Bind(PoisonProjectilePool parent, PoisonProjectileVisual presentation)
    {
        pool = parent;
        visual = presentation;
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
    }

    public void Initialize(PowerUser user, PowerRuntime power, PoisonEffect effect, Vector3 direction)
    {
        owner = user;
        definition = power.Definition;
        stats = user.Stats(power);
        poisonSettings = effect;
        credit = user.Crediting;
        LaunchDirection = direction;
        hitResolved = false;
        returnPending = false;
        launchedAt = Time.time;
        // Range-limited lifetime: travels up to stats.Range before dissipating as a miss
        float flightSeconds = Mathf.Max(.05f, stats.Range / Mathf.Max(1f, definition.ProjectileSpeed));
        expires = Time.time + flightSeconds;
        dropEmberAt = Time.time + .06f;

        sphere.radius = Mathf.Max(.05f, definition.ProjectileSize * .5f);
        sphere.enabled = true;
        visual.SetSize(definition.ProjectileSize);

        var camera = Camera.main;
        var rig = camera != null ? camera.GetComponent<ThirdPersonCamera>() : null;
        visual.SetFirstPerson(rig != null && rig.FirstPerson);

        body.linearVelocity = direction * definition.ProjectileSpeed;
        UpdatePresentation();
    }

    void Update()
    {
        if (returnPending) { Return(); return; }
        if (Time.time >= expires)
        {
            // Reached maximum range without hitting an eligible target: dissipates
            FeelDirector.Instance?.Particles?.Burst(transform.position, definition.PaletteColor, 2);
            Return();
            return;
        }
        if (Time.time >= dropEmberAt)
        {
            dropEmberAt = Time.time + .08f;
            FeelDirector.Instance?.Particles?.Burst(transform.position - transform.forward * .1f, CityColor.Leaf, 1);
        }
    }

    void FixedUpdate()
    {
        if (!hitResolved && body.linearVelocity.sqrMagnitude > .01f)
        {
            body.MoveRotation(Quaternion.LookRotation(body.linearVelocity.normalized));
            UpdatePresentation();
        }
    }

    void UpdatePresentation()
    {
        float clearance = .45f;
        int count = Physics.RaycastNonAlloc(transform.position, body.linearVelocity.normalized, visualHits, .45f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (visualHits[i].collider != sphere && (owner == null || visualHits[i].collider.transform.root != owner.transform))
                clearance = Mathf.Min(clearance, visualHits[i].distance);
        }
        visual.Present(Time.time - launchedAt, Mathf.Clamp((clearance - .02f) / .35f, .3f, 1f));
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hitResolved) return;
        hitResolved = true;
        LastImpactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

        var npc = collision.collider.GetComponentInParent<CityNpc>();
        if (npc != null && !npc.Dead)
        {
            // Direct hit on actor: apply poison strictly to the struck individual
            using (owner != null ? owner.Credit(credit) : default)
            {
                Poisoned.Apply(npc, owner, poisonSettings, stats.Damage, stats.Duration, 0, definition.PaletteColor);
            }
            FeelDirector.Instance?.Particles?.Burst(LastImpactPoint, definition.PaletteColor, poisonSettings != null ? poisonSettings.ApplyParticles : 10);
        }
        else
        {
            // Hit wall, floor, prop: stops and bursts small contamination without AoE poison
            FeelDirector.Instance?.Particles?.Burst(LastImpactPoint, definition.PaletteColor, 4);
        }

        sphere.enabled = false;
        body.linearVelocity = Vector3.zero;
        visual.gameObject.SetActive(false);
        returnPending = true;
    }

    void Return()
    {
        returnPending = false;
        owner = null;
        credit = null;
        body.linearVelocity = Vector3.zero;
        gameObject.SetActive(false);
        pool.Recycled++;
    }
}
