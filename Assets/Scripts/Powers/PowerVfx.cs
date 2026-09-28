using UnityEngine;
using UnityEngine.Rendering;

/// Fixed session pool of LineRenderers for the expanded roster (Laser Eyes beam, Lightning arcs, Darkness tendrils, synergy
/// beams). Built once, parented to the WorldSession (not the player's VisualRoot, so first person does not hide them). Every
/// line draws with a SHARED palette material from CityMaterials.Get, assigned by reference: no per-cast GameObject, Mesh or
/// Material. Particles for these powers come from the existing Feel ImpactParticlePool.
public sealed class PowerVfx : MonoBehaviour
{
    public const int PoolSize = 16, MaxPoints = 12;
    LineRenderer[] lines; float[] until; int cursor;
    readonly Vector3[] buffer = new Vector3[MaxPoints];
    public static PowerVfx Instance { get; private set; }
    public LineRenderer Beam { get; private set; }
    /// Lines handed out since the session started (test/diagnostic counter).
    public int Draws { get; private set; }
    public int PoolCount => lines?.Length ?? 0;
    public bool BeamVisible => Beam != null && Beam.gameObject.activeSelf;
    public int ActiveLines
    {
        get { int n = 0; if (lines != null) foreach (var l in lines) if (l.gameObject.activeSelf) n++; return n; }
    }
    public static PowerVfx Get()
    {
        if (Instance != null) return Instance;
        var parent = WorldSession.Instance != null ? WorldSession.Instance.transform : null;
        var root = new GameObject("Pooled power lines"); root.transform.SetParent(parent, false);
        Instance = root.AddComponent<PowerVfx>(); Instance.Build(); return Instance;
    }
    void Build()
    {
        lines = new LineRenderer[PoolSize]; until = new float[PoolSize];
        for (int i = 0; i < PoolSize; i++) lines[i] = Create("Pooled power line " + i);
        Beam = Create("Pooled power beam");
    }
    LineRenderer Create(string name)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = 2; line.widthMultiplier = .1f; line.numCapVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        go.SetActive(false); return line;
    }
    /// A short-lived line through `count` points of `points`.
    public LineRenderer Line(Vector3[] points, int count, CityColor color, float width, float seconds)
    {
        if (lines == null) return null;
        count = Mathf.Clamp(count, 2, MaxPoints);
        int i = cursor; cursor = (cursor + 1) % lines.Length;
        var line = lines[i];
        line.sharedMaterial = CityMaterials.Get(color); line.widthMultiplier = width; line.positionCount = count;
        for (int p = 0; p < count; p++) line.SetPosition(p, points[p]);
        until[i] = Time.time + seconds; line.gameObject.SetActive(true); Draws++;
        return line;
    }
    /// Jagged arc (lightning / tendril) from a to b with `segments` kinks of up to `amplitude` metres.
    public LineRenderer Arc(Vector3 a, Vector3 b, int segments, float amplitude, CityColor color, float width, float seconds)
    {
        segments = Mathf.Clamp(segments, 1, MaxPoints - 1);
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 p = Vector3.Lerp(a, b, t);
            if (i > 0 && i < segments) p += Random.insideUnitSphere * amplitude;
            buffer[i] = p;
        }
        return Line(buffer, segments + 1, color, width, seconds);
    }
    /// The one continuous beam (channeled powers). Stays until HideBeam.
    public void ShowBeam(Vector3 from, Vector3 to, CityColor color, float width)
    {
        if (Beam == null) return;
        Beam.sharedMaterial = CityMaterials.Get(color); Beam.widthMultiplier = width; Beam.positionCount = 2;
        Beam.SetPosition(0, from); Beam.SetPosition(1, to);
        if (!Beam.gameObject.activeSelf) { Beam.gameObject.SetActive(true); Draws++; }
    }
    public void HideBeam() { if (Beam != null) Beam.gameObject.SetActive(false); }
    void LateUpdate()
    {
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].gameObject.activeSelf && Time.time >= until[i]) lines[i].gameObject.SetActive(false);
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
}
