using UnityEngine;
using UnityEngine.Rendering;

/// Fixed session pool for Synergies 2.0 signature presentations (Seismic Slam cracks/wave, Solar Flare sweeping
/// arc + residual heat wake, Glacial Prison 3D ice pillars + frost ring, Thunderstorm overhead cloud + danger ring).
/// Built once per session with shared primitive mesh and shared CityMaterials palette swatches — zero per-cast
/// GameObject, Mesh, or Material allocations.
public sealed partial class SignatureVfx : MonoBehaviour
{
    public const int CrackCount = 6, PillarCount = 8, RingSegments = 28, WaveSegments = 16;
    static SignatureVfx instance;
    public static SignatureVfx Existing => instance;

    LineRenderer seismicWave;
    readonly LineRenderer[] seismicCracks = new LineRenderer[CrackCount];
    float seismicUntil;

    LineRenderer solarLead, solarTrail, solarWake;
    float solarWaveUntil, solarWakeUntil;

    Transform prisonRoot;
    readonly Transform[] pillars = new Transform[PillarCount];
    readonly Renderer[] pillarRenderers = new Renderer[PillarCount];
    LineRenderer prisonRing;
    float prisonUntil;

    LineRenderer stormCloudOuter, stormCloudInner, stormGroundRing;
    float stormUntil;

    public int SeismicShows { get; private set; }
    public int SolarWaveShows { get; private set; }
    public int PrisonShows { get; private set; }
    public int StormShows { get; private set; }

    public bool PrisonVisible => prisonRoot != null && prisonRoot.gameObject.activeSelf;
    public bool StormVisible => stormGroundRing != null && stormGroundRing.gameObject.activeSelf;
    public bool SolarWakeVisible => solarWake != null && solarWake.gameObject.activeSelf;

    public static SignatureVfx Get()
    {
        if (instance != null) return instance;
        var parent = WorldSession.Instance != null ? WorldSession.Instance.transform : null;
        var go = new GameObject("Pooled signature VFX");
        if (parent != null) go.transform.SetParent(parent, false);
        instance = go.AddComponent<SignatureVfx>();
        instance.Build();
        return instance;
    }

    LineRenderer MakeLine(string label, CityColor swatch, int count, bool loop, float width)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = count;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.sharedMaterial = CityMaterials.GetVfx(swatch);
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        go.SetActive(false);
        return line;
    }

    void Build()
    {
        CityMaterials.GetVfx(CityColor.Amber);
        CityMaterials.GetVfx(CityColor.Cream);
        CityMaterials.GetVfx(CityColor.Fire);
        CityMaterials.GetVfx(CityColor.Red);
        CityMaterials.GetVfx(CityColor.Cyan);
        CityMaterials.GetVfx(CityColor.Blue);
        CityMaterials.GetVfx(CityColor.UiNavy);
        CityMaterials.Get(CityColor.Cyan);
        CityMaterials.Get(CityColor.Blue);

        seismicWave = MakeLine("Seismic wave ring", CityColor.Amber, RingSegments, true, .18f);
        for (int i = 0; i < CrackCount; i++)
            seismicCracks[i] = MakeLine("Seismic crack " + i, i % 2 == 0 ? CityColor.Cream : CityColor.Amber, 5, false, .1f);

        solarLead = MakeLine("Solar flare lead arc", CityColor.Cream, WaveSegments, false, .26f);
        solarTrail = MakeLine("Solar flare trail arc", CityColor.Fire, WaveSegments, false, .42f);
        solarWake = MakeLine("Solar flare residual wake", CityColor.Fire, RingSegments, true, .11f);

        prisonRoot = new GameObject("Glacial prison pillars").transform;
        prisonRoot.SetParent(transform, false);
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh sharedCube = template.GetComponent<MeshFilter>().sharedMesh;
        template.SetActive(false);
        Destroy(template);
        for (int i = 0; i < PillarCount; i++)
        {
            var pillar = new GameObject("Ice pillar " + i);
            pillar.transform.SetParent(prisonRoot, false);
            var mf = pillar.AddComponent<MeshFilter>();
            mf.sharedMesh = sharedCube;
            var mr = pillar.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CityMaterials.Get(i % 2 == 0 ? CityColor.Cyan : CityColor.Blue);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            pillars[i] = pillar.transform;
            pillarRenderers[i] = mr;
        }
        prisonRoot.gameObject.SetActive(false);
        prisonRing = MakeLine("Glacial prison ring", CityColor.Cyan, RingSegments, true, .14f);

        BuildFinalFour(sharedCube);

        stormCloudOuter = MakeLine("Thunderstorm cloud outer", CityColor.UiNavy, RingSegments, true, .45f);
        stormCloudInner = MakeLine("Thunderstorm cloud inner", CityColor.Cyan, RingSegments, true, .22f);
        stormGroundRing = MakeLine("Thunderstorm danger ring", CityColor.Cream, RingSegments, true, .12f);
    }

    static void SetCircle(LineRenderer line, Vector3 center, float radius, float yOffset, float wobble = 0f, float phase = 0f)
    {
        int n = line.positionCount;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            float r = radius * (1f + wobble * Mathf.Sin(a * 4f + phase));
            line.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, yOffset, Mathf.Sin(a) * r));
        }
    }

    public void ShowSeismicSlam(Vector3 origin, float maxRadius, float duration)
    {
        SeismicShows++;
        seismicUntil = Time.time + duration + .25f;
        SetCircle(seismicWave, origin, .4f, .08f);
        seismicWave.widthMultiplier = .22f;
        seismicWave.gameObject.SetActive(true);
        for (int i = 0; i < CrackCount; i++)
        {
            var crack = seismicCracks[i];
            float baseAngle = i * Mathf.PI * 2f / CrackCount + .2f;
            for (int p = 0; p < 5; p++)
            {
                float t = p / 4f;
                float r = Mathf.Lerp(.35f, maxRadius * .92f, t);
                float jitter = (p == 0 || p == 4) ? 0f : ((i + p) % 2 == 0 ? .18f : -.18f);
                float a = baseAngle + jitter;
                crack.SetPosition(p, origin + new Vector3(Mathf.Cos(a) * r, .06f, Mathf.Sin(a) * r));
            }
            crack.gameObject.SetActive(true);
        }
    }

    public void UpdateSeismicSlam(Vector3 origin, float currentRadius, float progress)
    {
        if (seismicWave == null) return;
        SetCircle(seismicWave, origin, Mathf.Max(.35f, currentRadius), .08f, .04f, progress * 10f);
        seismicWave.widthMultiplier = Mathf.Lerp(.24f, .08f, progress);
    }

    public void HideSeismicSlam()
    {
        if (seismicWave != null) seismicWave.gameObject.SetActive(false);
        for (int i = 0; i < CrackCount; i++)
            if (seismicCracks[i] != null) seismicCracks[i].gameObject.SetActive(false);
    }

    public void ShowSolarWave(Vector3 origin, Vector3 forward, float distance, float halfWidth, float progress, float duration)
    {
        if (Mathf.Approximately(progress, 0f)) SolarWaveShows++;
        solarWaveUntil = Time.time + duration;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float arcAngle = 36f * Mathf.Deg2Rad;
        for (int i = 0; i < WaveSegments; i++)
        {
            float u = (i / (float)(WaveSegments - 1)) * 2f - 1f;
            float a = u * arcAngle;
            Vector3 dir = (forward * Mathf.Cos(a) + right * Mathf.Sin(a)).normalized;
            float lateralScale = halfWidth / Mathf.Max(.5f, distance * Mathf.Sin(arcAngle));
            Vector3 leadPos = origin + forward * (distance * Mathf.Cos(a)) + right * (Mathf.Sin(a) * distance * lateralScale) + Vector3.up * .75f;
            Vector3 trailPos = leadPos - dir * .85f - Vector3.up * .2f;
            solarLead.SetPosition(i, leadPos);
            solarTrail.SetPosition(i, trailPos);
        }
        solarLead.widthMultiplier = Mathf.Lerp(.28f, .16f, progress);
        solarTrail.widthMultiplier = Mathf.Lerp(.46f, .24f, progress);
        solarLead.gameObject.SetActive(true);
        solarTrail.gameObject.SetActive(true);
    }

    public void HideSolarWave()
    {
        if (solarLead != null) solarLead.gameObject.SetActive(false);
        if (solarTrail != null) solarTrail.gameObject.SetActive(false);
    }

    public void ShowSolarWake(Vector3 center, float radius, float duration)
    {
        solarWakeUntil = Time.time + duration;
        SetCircle(solarWake, center, radius, .07f, .06f, Time.time * 4f);
        solarWake.gameObject.SetActive(true);
    }

    public void HideSolarWake()
    {
        if (solarWake != null) solarWake.gameObject.SetActive(false);
    }

    public void ShowGlacialPrison(Vector3 center, float ringRadius, float duration)
    {
        PrisonShows++;
        prisonUntil = Time.time + duration + .2f;
        prisonRoot.position = center;
        prisonRoot.rotation = Quaternion.identity;
        for (int i = 0; i < PillarCount; i++)
        {
            float a = i * Mathf.PI * 2f / PillarCount;
            Vector3 offset = new Vector3(Mathf.Cos(a) * ringRadius, 1.15f, Mathf.Sin(a) * ringRadius);
            var t = pillars[i];
            t.localPosition = offset;
            t.localRotation = Quaternion.Euler(14f * Mathf.Cos(a), -a * Mathf.Rad2Deg, 14f * Mathf.Sin(a));
            t.localScale = new Vector3(.42f, 2.3f, .42f);
            pillarRenderers[i].sharedMaterial = CityMaterials.Get(i % 2 == 0 ? CityColor.Cyan : CityColor.Blue);
        }
        prisonRoot.gameObject.SetActive(true);
        SetCircle(prisonRing, center, ringRadius, .08f, .05f, 1.2f);
        prisonRing.gameObject.SetActive(true);
    }

    public void UpdateGlacialPrison(Vector3 center, float ringRadius, float riseProgress)
    {
        if (prisonRoot == null || !prisonRoot.gameObject.activeSelf) return;
        float h = Mathf.Lerp(.25f, 2.3f, Mathf.Clamp01(riseProgress));
        for (int i = 0; i < PillarCount; i++)
        {
            float a = i * Mathf.PI * 2f / PillarCount;
            var t = pillars[i];
            t.localPosition = new Vector3(Mathf.Cos(a) * ringRadius, h * .5f, Mathf.Sin(a) * ringRadius);
            t.localScale = new Vector3(.42f, h, .42f);
        }
    }

    public void HideGlacialPrison()
    {
        if (prisonRoot != null) prisonRoot.gameObject.SetActive(false);
        if (prisonRing != null) prisonRing.gameObject.SetActive(false);
    }

    public void ShowThunderstorm(Vector3 center, float radius, float cloudHeight, float duration)
    {
        StormShows++;
        stormUntil = Time.time + duration + .25f;
        Vector3 sky = center + Vector3.up * cloudHeight;
        SetCircle(stormCloudOuter, sky, radius * .88f, 0f, .12f, Time.time * 2f);
        SetCircle(stormCloudInner, sky + Vector3.down * .25f, radius * .52f, 0f, .15f, Time.time * 3.5f);
        SetCircle(stormGroundRing, center, radius, .07f, .03f, Time.time * 4f);
        stormCloudOuter.gameObject.SetActive(true);
        stormCloudInner.gameObject.SetActive(true);
        stormGroundRing.gameObject.SetActive(true);
    }

    public void PulseThunderstorm(Vector3 center, float radius, float cloudHeight, bool striking)
    {
        if (stormGroundRing == null) return;
        Vector3 sky = center + Vector3.up * cloudHeight;
        SetCircle(stormCloudOuter, sky, radius * .88f, 0f, .12f, Time.time * 3f);
        SetCircle(stormCloudInner, sky + Vector3.down * .25f, radius * .52f, 0f, .15f, Time.time * 5f);
        SetCircle(stormGroundRing, center, radius, .07f, striking ? .06f : .02f, Time.time * 8f);
        stormGroundRing.widthMultiplier = striking ? .2f : .1f;
        stormGroundRing.sharedMaterial = CityMaterials.GetVfx(striking ? CityColor.Cream : CityColor.Cyan);
    }

    public void HideThunderstorm()
    {
        if (stormCloudOuter != null) stormCloudOuter.gameObject.SetActive(false);
        if (stormCloudInner != null) stormCloudInner.gameObject.SetActive(false);
        if (stormGroundRing != null) stormGroundRing.gameObject.SetActive(false);
    }

    public void HideAll()
    {
        HideSeismicSlam();
        HideSolarWave();
        HideSolarWake();
        HideGlacialPrison();
        HideThunderstorm();
        HideFinalFour();
    }

    void LateUpdate()
    {
        UpdateFinalFour();
        float now = Time.time;
        if (seismicWave != null && seismicWave.gameObject.activeSelf && now >= seismicUntil) HideSeismicSlam();
        if (solarLead != null && solarLead.gameObject.activeSelf && now >= solarWaveUntil) HideSolarWave();
        if (solarWake != null && solarWake.gameObject.activeSelf && now >= solarWakeUntil) HideSolarWake();
        if (prisonRoot != null && prisonRoot.gameObject.activeSelf && now >= prisonUntil) HideGlacialPrison();
        if (stormGroundRing != null && stormGroundRing.gameObject.activeSelf && now >= stormUntil) HideThunderstorm();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
