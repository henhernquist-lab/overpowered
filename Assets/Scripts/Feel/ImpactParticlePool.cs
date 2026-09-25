using UnityEngine;

/// Fixed pool of mesh-mode ParticleSystems for impact debris. Built once per session: every system renders the SHARED
/// built-in cube mesh with a SHARED CityMaterials palette material (assigned by reference per burst), and bursts use
/// Emit() on a round-robin slot — no per-hit Instantiate, no runtime Material. This is VFX only: BreakableProp's shards
/// are untouched. Particles simulate in scaled time, so debris holds still during a hit pause.
public sealed class ImpactParticlePool : MonoBehaviour
{
    ParticleSystem[] slots; ParticleSystemRenderer[] renderers; int cursor;
    FeelSettings settings;
    public int PoolCount => slots?.Length ?? 0;
    public int Bursts { get; private set; }
    public int ParticlesEmitted { get; private set; }
    public Mesh SharedMesh { get; private set; }
    public ParticleSystem Slot(int i) => slots[i];
    public ParticleSystemRenderer SlotRenderer(int i) => renderers[i];
    public int LastSlot { get; private set; } = -1;

    public void Initialize(FeelSettings feel)
    {
        settings = feel;
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SharedMesh = template.GetComponent<MeshFilter>().sharedMesh;
        template.SetActive(false); Destroy(template);
        // Resolve the palette materials now (the palette owns them; created once per city if not already in use).
        CityMaterials.Get(feel.HitDebris); CityMaterials.Get(feel.HeavyDebris); CityMaterials.Get(feel.BreakDebris); CityMaterials.Get(feel.LandingDebris);
        int count = Mathf.Max(1, feel.ParticlePoolSize);
        slots = new ParticleSystem[count]; renderers = new ParticleSystemRenderer[count];
        int maxPerBurst = Mathf.Max(1, Mathf.Max(Mathf.Max(feel.LightHitParticles, feel.HeavyHitParticles), Mathf.Max(feel.BreakParticles, feel.LandingParticles)));
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Pooled impact debris " + i); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.maxParticles = maxPerBurst * 2;
            main.duration = Mathf.Max(.05f, feel.ParticleLifetime);
            main.startLifetime = new ParticleSystem.MinMaxCurve(feel.ParticleLifetime * .6f, feel.ParticleLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(feel.ParticleSpeed * .45f, feel.ParticleSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(feel.ParticleSize * (1f - feel.ParticleSizeJitter), feel.ParticleSize);
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = feel.ParticleGravity; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local; main.useUnscaledTime = false;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = .25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // hemisphere opens upward
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .2f));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = SharedMesh;
            r.sharedMaterial = CityMaterials.Get(feel.HitDebris);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            slots[i] = ps; renderers[i] = r;
        }
    }
    public void Burst(Vector3 position, CityColor color, int count)
    {
        if (slots == null || count <= 0) return;
        int i = cursor; cursor = (cursor + 1) % slots.Length; LastSlot = i;
        var ps = slots[i];
        ps.transform.position = position;
        var material = CityMaterials.Get(color);
        if (renderers[i].sharedMaterial != material) { ps.Clear(); renderers[i].sharedMaterial = material; }   // a recoloured slot drops its old debris
        if (!ps.isPlaying) ps.Play();
        ps.Emit(count); Bursts++; ParticlesEmitted += count;
    }
}
