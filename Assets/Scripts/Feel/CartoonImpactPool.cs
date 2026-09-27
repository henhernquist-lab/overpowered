using UnityEngine;

public sealed class CartoonImpactPool : MonoBehaviour
{
    sealed class Slot { public GameObject Root; public ParticleSystem[] Systems; public float Until; }
    Slot[] slots;
    FeelSettings settings;
    float nextBurst;
    int cursor;
    public int PoolCount => slots == null ? 0 : slots.Length;
    public int Bursts { get; private set; }
    public int ActiveCount { get { int count = 0; if (slots != null) foreach (var slot in slots) if (slot.Root.activeSelf) count++; return count; } }
    public void Initialize(FeelSettings tuning)
    {
        settings = tuning;
        if (tuning.CartoonImpact == null) return;
        slots = new Slot[Mathf.Clamp(tuning.CartoonPoolSize, 1, 6)];
        for (int i = 0; i < slots.Length; i++)
        {
            var root = Instantiate(tuning.CartoonImpact, transform); root.name = "Pooled Cartoon impact " + i;
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var system in systems)
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main; main.playOnAwake = false; main.loop = false; main.useUnscaledTime = false;
                main.maxParticles = Mathf.Min(main.maxParticles, 48);
                main.startColor = CityMaterials.Current.Palette.Colors[(int)tuning.HeavyDebris];
                main.stopAction = ParticleSystemStopAction.None;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
            root.SetActive(false); slots[i] = new Slot { Root = root, Systems = systems };
        }
    }
    public void Burst(Vector3 position)
    {
        if (slots == null || Time.time < nextBurst) return;
        nextBurst = Time.time + Mathf.Max(.1f, settings.CartoonMinimumInterval);
        var slot = slots[cursor]; cursor = (cursor + 1) % slots.Length;
        foreach (var system in slot.Systems) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        slot.Root.transform.position = position; slot.Root.transform.rotation = Quaternion.identity;
        slot.Root.SetActive(true); slot.Until = Time.time + settings.CartoonLifetime;
        foreach (var system in slot.Systems) system.Play(false);
        Bursts++;
    }
    void Update()
    {
        if (slots == null) return;
        foreach (var slot in slots) if (slot.Root.activeSelf && Time.time >= slot.Until)
        {
            foreach (var system in slot.Systems) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            slot.Root.SetActive(false);
        }
    }
}
