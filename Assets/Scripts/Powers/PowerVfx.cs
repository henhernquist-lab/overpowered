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
    LineRenderer laserRight, laserHaloLeft, laserHaloRight, laserImpactRing, laserImpactCrossH, laserImpactCrossV;
    public LineRenderer LaserRight => laserRight;
    public LineRenderer LaserHaloLeft => laserHaloLeft;
    public LineRenderer LaserHaloRight => laserHaloRight;
    public int LaserRendererCount => Beam == null ? 0 : 7;
    public bool LaserImpactVisible => laserImpactRing != null && laserImpactRing.gameObject.activeSelf;
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
        laserRight=Create("Laser right core");laserHaloLeft=Create("Laser left halo");laserHaloRight=Create("Laser right halo");
        laserImpactRing=Create("Laser impact ring");laserImpactCrossH=Create("Laser impact horizontal");laserImpactCrossV=Create("Laser impact vertical");
        // Built-in unlit palette swatches are shared for the session, including across repeated channels.
        CityMaterials.GetVfx(CityColor.Cream);CityMaterials.GetVfx(CityColor.Red);CityMaterials.GetVfx(CityColor.Blue);CityMaterials.GetVfx(CityColor.Leaf);
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
        line.sharedMaterial = CityMaterials.Get(color); line.sortingOrder=0; line.widthMultiplier = width; line.positionCount = count;
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
        HideLaserExtras();
        Beam.sharedMaterial = CityMaterials.Get(color); Beam.widthMultiplier = width; Beam.positionCount = 2;
        Beam.SetPosition(0, from); Beam.SetPosition(1, to);
        if (!Beam.gameObject.activeSelf) { Beam.gameObject.SetActive(true); Draws++; }
    }
    static void Segment(LineRenderer line,Material material,float width,Vector3 from,Vector3 to)
    {
        line.sharedMaterial=material;line.widthMultiplier=width;line.positionCount=2;
        line.SetPosition(0,from);line.SetPosition(1,to);line.gameObject.SetActive(true);
    }
    /// Presentation only: the endpoint is supplied by LaserEyesEffect's existing gameplay ray.
    public void ShowLaser(Vector3 left,Vector3 right,Vector3 merge,Vector3 hitPoint,bool firstPerson,bool contact,
        float coreWidth,float haloWidth,float impactRadius,Vector3 viewRight,Vector3 viewUp)
    {
        var core=CityMaterials.GetVfx(CityColor.Cream);var halo=CityMaterials.GetVfx(CityColor.Red);
        float pulse=1f+.075f*Mathf.Sin(Time.time*17f);
        if(firstPerson)
        {
            // Separate straight segments avoid the large camera-facing join wedge of a three-point LineRenderer.
            Segment(Beam,core,coreWidth*.5f*pulse,left,merge);
            Segment(laserHaloLeft,halo,haloWidth*.3f*pulse,left,merge);
            Segment(laserRight,core,coreWidth*.35f*pulse,merge,hitPoint);
            Segment(laserHaloRight,halo,haloWidth*.24f*pulse,merge,hitPoint);
        }
        else
        {
            Segment(Beam,core,coreWidth*pulse,left,hitPoint);
            Segment(laserRight,core,coreWidth*pulse,right,hitPoint);
            Segment(laserHaloLeft,halo,haloWidth*pulse,left,hitPoint);
            Segment(laserHaloRight,halo,haloWidth*pulse,right,hitPoint);
        }
        if(!contact){HideLaserImpact();return;}
        float radius=impactRadius*(.9f+.1f*Mathf.Sin(Time.time*23f));
        laserImpactRing.sharedMaterial=halo;laserImpactRing.widthMultiplier=coreWidth*.8f;laserImpactRing.positionCount=9;
        for(int i=0;i<9;i++)
        {
            float angle=i*Mathf.PI*.25f;
            laserImpactRing.SetPosition(i,hitPoint+(viewRight*Mathf.Cos(angle)+viewUp*Mathf.Sin(angle))*radius);
        }
        laserImpactRing.gameObject.SetActive(true);
        Segment(laserImpactCrossH,core,coreWidth*.8f,hitPoint-viewRight*radius*.45f,hitPoint+viewRight*radius*.45f);
        Segment(laserImpactCrossV,core,coreWidth*.8f,hitPoint-viewUp*radius*.45f,hitPoint+viewUp*radius*.45f);
    }
    void HideLaserImpact()
    {laserImpactRing.gameObject.SetActive(false);laserImpactCrossH.gameObject.SetActive(false);laserImpactCrossV.gameObject.SetActive(false);}
    void HideLaserExtras()
    {
        if(laserRight==null)return;
        laserRight.gameObject.SetActive(false);laserHaloLeft.gameObject.SetActive(false);laserHaloRight.gameObject.SetActive(false);
        HideLaserImpact();
    }
    public void HideBeam() { if (Beam != null) Beam.gameObject.SetActive(false);HideLaserExtras(); }
    void LateUpdate()
    {
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].gameObject.activeSelf && Time.time >= until[i]) lines[i].gameObject.SetActive(false);
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
}
