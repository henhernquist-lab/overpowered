using UnityEngine;
using UnityEngine.Rendering;

/// The Force Field on the player: a pool of absorb points that incoming damage drains before health
/// (WorldSession.DamagePlayer -> PowerUser.AbsorbIncoming). It deals no damage. One component per player, created on first
/// use; its three rings are built once, parented to the player ROOT (not the VisualRoot, so first person keeps them) and use
/// one shared palette material.
public sealed class PlayerShield : MonoBehaviour
{
    public const int RingCount = 3, RingPoints = 32;
    public float Capacity { get; private set; }
    public float Remaining { get; private set; }
    public float Until { get; private set; }
    public float AbsorbFraction { get; private set; } = 1f;
    public float TotalAbsorbed { get; private set; }
    public int HitsAbsorbed { get; private set; }
    public int Raised { get; private set; }
    /// Last way the field went down: "broken" (capacity spent) or "expired".
    public string LastEnd { get; private set; } = "";
    public bool Up => raised && Remaining > 0f && Time.time < Until;
    public bool RingsVisible => rings != null && rings[0].gameObject.activeSelf;
    public event System.Action<bool> Ended;   // true = broken by damage
    LineRenderer[] rings; Transform pivot; CityColor color; bool raised; float radius = 1.1f;
    public void Raise(float capacity, float seconds, float absorbFraction, float ringRadius, CityColor ringColor)
    {
        Capacity = Remaining = Mathf.Max(0f, capacity); Until = Time.time + Mathf.Max(0f, seconds);
        AbsorbFraction = Mathf.Clamp01(absorbFraction); radius = ringRadius; color = ringColor; raised = true; Raised++;
        if (rings == null) Build();
        var material = CityMaterials.Get(color);
        for (int i = 0; i < rings.Length; i++) { rings[i].sharedMaterial = material; rings[i].gameObject.SetActive(true); }
        pivot.localScale = Vector3.one * radius;
        FeelDirector.Instance?.Particles.Burst(transform.position + Vector3.up, color, 10);
    }
    /// Returns the part of `damage` that still reaches health.
    public float Absorb(float damage)
    {
        if (!Up || damage <= 0f) return damage;
        float taken = Mathf.Min(Remaining, damage * AbsorbFraction);
        Remaining -= taken; TotalAbsorbed += taken; HitsAbsorbed++;
        FeelDirector.Instance?.Particles.Burst(transform.position + Vector3.up, color, 6);
        if (Remaining <= 0f) Drop(true);
        return damage - taken;
    }
    public void Drop(bool broken)
    {
        if (!raised) return;
        raised = false; Remaining = 0f; LastEnd = broken ? "broken" : "expired";
        if (rings != null) foreach (var r in rings) r.gameObject.SetActive(false);
        if (broken) FeelDirector.Instance?.Particles.Burst(transform.position + Vector3.up, color, 14);
        Ended?.Invoke(broken);
    }
    void Build()
    {
        pivot = new GameObject("Force field").transform; pivot.SetParent(transform, false); pivot.localPosition = Vector3.up * .95f;
        rings = new LineRenderer[RingCount];
        for (int i = 0; i < RingCount; i++)
        {
            var go = new GameObject("Force field ring " + i); go.transform.SetParent(pivot, false);
            go.transform.localRotation = Quaternion.Euler(i * 60f, i * 45f, 0f);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.loop = true; line.positionCount = RingPoints; line.widthMultiplier = .045f;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            for (int n = 0; n < RingPoints; n++) { float a = n * Mathf.PI * 2f / RingPoints; line.SetPosition(n, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))); }
            go.SetActive(false); rings[i] = line;
        }
    }
    void Update()
    {
        if (raised && !Up) Drop(Remaining <= 0f);
        if (raised && pivot != null) pivot.Rotate(0f, 90f * Time.deltaTime, 0f, Space.Self);
    }
}
