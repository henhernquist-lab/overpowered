using UnityEngine;
using UnityEngine.Rendering;

/// PULSEBREAKER presentation: a chest reactor, two powered gauntlets and one expanding seismic ring.
/// Everything uses built-in primitive meshes plus SHARED CityMaterials palette materials, so a boss adds no material and
/// no per-frame allocation. The single ring mesh is generated once for the whole process and reused by every boss.
public sealed class PulsebreakerVfx
{
    /// One ring mesh for the entire game (24 triangles per segment pair, built on first use).
    static Mesh ringMesh;

    readonly Transform owner;
    Transform ring;
    MeshRenderer ringRenderer;
    Transform core;
    MeshRenderer coreRenderer;
    Transform leftGauntlet, rightGauntlet;
    float ringRadius, ringSeconds, ringElapsed;
    float pulse;

    public bool RingVisible => ring != null && ring.gameObject.activeSelf;
    public int GauntletsBuilt { get; private set; }
    public int CoreBuilt => core != null ? 1 : 0;

    public PulsebreakerVfx(Transform owner) { this.owner = owner; }

    public void Build(HumanoidPresentation pose, float bodyHeight, float visualScale)
    {
        if (pose == null) return;
        var root = pose.VisualRoot != null ? pose.VisualRoot : owner;
        float rootScale = Mathf.Max(.01f, root.lossyScale.x);
        // Reactor: a small sphere at chest height on the visual root (follows the body, not the physics root).
        core = Primitive(PrimitiveType.Sphere, "Pulsebreaker reactor", root);
        core.localPosition = new Vector3(0f, bodyHeight * .62f / rootScale, .1f);
        core.localScale = Vector3.one * (.34f / rootScale);
        coreRenderer = core.GetComponent<MeshRenderer>();
        coreRenderer.sharedMaterial = CityMaterials.GetVfx(CityColor.Cyan);
        coreRenderer.shadowCastingMode = ShadowCastingMode.Off; coreRenderer.receiveShadows = false;
        // Gauntlets: cubes on the hand bones. Local scale is compensated by the bone's world scale so the visible size is
        // the same on any rig; they are children of the skeleton, never of the physics root.
        leftGauntlet = Gauntlet(pose, HumanBodyBones.LeftHand, "Left power gauntlet");
        rightGauntlet = Gauntlet(pose, HumanBodyBones.RightHand, "Right power gauntlet");
        // Seismic ring: built once, inactive until a slam / phase shift / death.
        EnsureRing(owner);
        core.gameObject.SetActive(true);
    }

    Transform Gauntlet(HumanoidPresentation pose, HumanBodyBones bone, string name)
    {
        var hand = pose.Animator != null ? pose.Animator.GetBoneTransform(bone) : null;
        if (hand == null) return null;
        var gauntlet = Primitive(PrimitiveType.Cube, name, hand);
        float world = Mathf.Max(.001f, hand.lossyScale.x);
        gauntlet.localScale = Vector3.one * (.30f / world);
        gauntlet.localPosition = Vector3.zero;
        var renderer = gauntlet.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = CityMaterials.Get(CityColor.Metal);
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        GauntletsBuilt++;
        return gauntlet;
    }

    static Transform Primitive(PrimitiveType type, string name, Transform parent)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var collider = go.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);   // presentation only: never a physics body or a camera collider
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    void EnsureRing(Transform parent)
    {
        if (ring != null) return;
        if (ringMesh == null) ringMesh = BuildRingMesh();
        var go = new GameObject("Pulsebreaker seismic ring");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
        ringRenderer = go.AddComponent<MeshRenderer>();
        ringRenderer.sharedMaterial = CityMaterials.GetVfx(CityColor.Fire);
        ringRenderer.shadowCastingMode = ShadowCastingMode.Off; ringRenderer.receiveShadows = false;
        ringRenderer.lightProbeUsage = LightProbeUsage.Off; ringRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        ring = go.transform;
        go.SetActive(false);
    }

    /// A flat annulus of outer radius 1, inner radius .86, in the XZ plane. Both windings, so it reads from any camera.
    static Mesh BuildRingMesh()
    {
        const int segments = 48;
        var vertices = new Vector3[(segments + 1) * 2];
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
            vertices[i * 2] = new Vector3(x * .86f, 0f, z * .86f);
            vertices[i * 2 + 1] = new Vector3(x, 0f, z);
        }
        var triangles = new int[segments * 12];
        int t = 0;
        for (int i = 0; i < segments; i++)
        {
            int inner = i * 2, outerOuter = i * 2 + 1, nextInner = (i + 1) * 2, nextOuter = (i + 1) * 2 + 1;
            triangles[t++] = inner; triangles[t++] = outerOuter; triangles[t++] = nextOuter;
            triangles[t++] = inner; triangles[t++] = nextOuter; triangles[t++] = nextInner;
            triangles[t++] = inner; triangles[t++] = nextOuter; triangles[t++] = outerOuter;
            triangles[t++] = inner; triangles[t++] = nextInner; triangles[t++] = nextOuter;
        }
        var mesh = new Mesh { name = "Pulsebreaker ring" };
        mesh.vertices = vertices; mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// Starts the expanding seismic ring at a ground point. The ring's current radius is read by gameplay, so the visible
    /// danger and the damaging band are always the same thing.
    public void SlamRing(Vector3 at, float radius, float seconds, CityColor color)
    {
        if (ring == null) return;
        ringRadius = Mathf.Max(.1f, radius); ringSeconds = Mathf.Max(.05f, seconds); ringElapsed = 0f;
        ringRenderer.sharedMaterial = CityMaterials.GetVfx(color);
        ring.position = at + Vector3.up * .07f;
        ring.localScale = new Vector3(.1f, 1f, .1f);
        ring.gameObject.SetActive(true);
    }

    /// Gameplay-facing: how far the ring has travelled, 0..radius.
    public float RingProgress => ringSeconds > 0f ? Mathf.Clamp01(ringElapsed / ringSeconds) : 1f;
    public float RingRadiusNow => ringRadius * RingProgress;

    public void Tick(float dt)
    {
        if (ring != null && ring.gameObject.activeSelf)
        {
            ringElapsed += dt;
            float radius = RingRadiusNow;
            ring.localScale = new Vector3(Mathf.Max(.1f, radius), 1f, Mathf.Max(.1f, radius));
            if (ringElapsed >= ringSeconds) ring.gameObject.SetActive(false);
        }
        // Reactor breathes slightly faster in phase 2, which also reads as "the reactor is running hot".
        pulse += dt * (core != null && ring != null ? 3.2f : 2.2f);
        if (core != null)
        {
            float scale = (.34f + Mathf.Sin(pulse) * .02f) / Mathf.Max(.01f, core.parent != null ? core.parent.lossyScale.x : 1f);
            core.localScale = Vector3.one * scale;
        }
    }

    /// Phase 2: the reactor and gauntlets escalate from cyan to fire (both shared, already-emissive palette swatches).
    public void SetPhase(int phase)
    {
        var accent = phase >= 2 ? CityColor.Fire : CityColor.Cyan;
        if (coreRenderer != null) coreRenderer.sharedMaterial = CityMaterials.GetVfx(accent);
        foreach (var gauntlet in new[] { leftGauntlet, rightGauntlet })
            if (gauntlet != null) gauntlet.GetComponent<MeshRenderer>().sharedMaterial = CityMaterials.Get(accent);
    }

    public void Clear()
    {
        if (ring != null) ring.gameObject.SetActive(false);
        if (core != null) core.gameObject.SetActive(false);
        if (leftGauntlet != null) leftGauntlet.gameObject.SetActive(false);
        if (rightGauntlet != null) rightGauntlet.gameObject.SetActive(false);
    }
}
