using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum ParkourMarkerState
{
    Hidden,
    StartIdle,
    StartActive,
    Inactive,
    Next,
    Completed,
    FinishInactive,
    FinishNext
}

/// Fixed session visual pool for rooftop parkour start markers, checkpoint gates, directional chevrons,
/// finish beacons, and confirmation bursts. Uses ONE shared cube mesh and shared CityMaterials palette swatches.
/// Zero runtime Material or Mesh creation after session initialization; zero Collider attachments (no camera collision glitches).
public sealed class ParkourVfx : MonoBehaviour
{
    public const int MaxCheckpointSlots = 12;
    public const int MaxStartSlots = 4;
    public const int RingSegments = 8;

    public sealed class GateNode
    {
        public Transform Root;
        public Transform RingHolder;
        public readonly Transform[] Segments = new Transform[RingSegments];
        public readonly MeshRenderer[] SegmentRenderers = new MeshRenderer[RingSegments];
        public TransformPylon LeftPylon, RightPylon;
        public Transform ChevronHolder, ChevronLeft, ChevronRight;
        public MeshRenderer ChevronLeftRenderer, ChevronRightRenderer;
        public Transform Crown;
        public MeshRenderer CrownRenderer;
        public ParkourMarkerState State = ParkourMarkerState.Hidden;
        public Vector3 BasePosition;
        public Vector3 ForwardDir = Vector3.forward;
        public CityColor Primary = CityColor.Cyan;
        public CityColor Secondary = CityColor.Blue;
        public CityColor Finish = CityColor.Cream;
        public float StateChangedAt;
        public float TriggerRadius = 3f;
    }

    public struct TransformPylon
    {
        public Transform Transform;
        public MeshRenderer Renderer;
    }

    static ParkourVfx instance;
    public static ParkourVfx Existing => instance;

    Mesh sharedCube;
    readonly GateNode[] startNodes = new GateNode[MaxStartSlots];
    readonly GateNode[] checkpointNodes = new GateNode[MaxCheckpointSlots];
    ParticleSystem burstSystem;
    ParticleSystemRenderer burstRenderer;

    public int CreatedRenderers { get; private set; }
    public int BurstsPlayed { get; private set; }
    public Mesh SharedMesh => sharedCube;
    public IReadOnlyList<GateNode> StartNodes => startNodes;
    public IReadOnlyList<GateNode> CheckpointNodes => checkpointNodes;

    public static ParkourVfx Get()
    {
        if (instance != null) return instance;
        var parent = WorldSession.Instance != null ? WorldSession.Instance.transform : null;
        var go = new GameObject("Pooled parkour markers");
        if (parent != null) go.transform.SetParent(parent, false);
        instance = go.AddComponent<ParkourVfx>();
        instance.Build();
        return instance;
    }

    void Build()
    {
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sharedCube = template.GetComponent<MeshFilter>().sharedMesh;
        template.SetActive(false);
        Destroy(template);

        // Pre-warm shared palette materials used by parkour themes
        CityMaterials.Get(CityColor.Cyan);
        CityMaterials.Get(CityColor.Blue);
        CityMaterials.Get(CityColor.UiPurple);
        CityMaterials.Get(CityColor.Amber);
        CityMaterials.Get(CityColor.LitWindow);
        CityMaterials.Get(CityColor.Neon);
        CityMaterials.Get(CityColor.Cream);
        CityMaterials.Get(CityColor.Slate);

        for (int i = 0; i < MaxStartSlots; i++)
            startNodes[i] = CreateGateNode("Parkour start marker " + i, isStart: true);
        for (int i = 0; i < MaxCheckpointSlots; i++)
            checkpointNodes[i] = CreateGateNode("Parkour checkpoint " + i, isStart: false);

        BuildBurstPool();
    }

    GateNode CreateGateNode(string name, bool isStart)
    {
        var node = new GateNode();
        var rootGo = new GameObject(name);
        rootGo.transform.SetParent(transform, false);
        node.Root = rootGo.transform;

        var ringGo = new GameObject("Ring");
        ringGo.transform.SetParent(node.Root, false);
        node.RingHolder = ringGo.transform;

        for (int s = 0; s < RingSegments; s++)
        {
            var seg = MakePiece(node.RingHolder, "Seg " + s, CityColor.Cyan);
            node.Segments[s] = seg.Transform;
            node.SegmentRenderers[s] = seg.Renderer;
        }

        node.LeftPylon = MakePiece(node.Root, "Left pylon", CityColor.Blue);
        node.RightPylon = MakePiece(node.Root, "Right pylon", CityColor.Blue);

        var chevGo = new GameObject("Chevron");
        chevGo.transform.SetParent(node.Root, false);
        node.ChevronHolder = chevGo.transform;
        var cLeft = MakePiece(node.ChevronHolder, "Chevron L", CityColor.Neon);
        var cRight = MakePiece(node.ChevronHolder, "Chevron R", CityColor.Neon);
        node.ChevronLeft = cLeft.Transform;
        node.ChevronLeftRenderer = cLeft.Renderer;
        node.ChevronRight = cRight.Transform;
        node.ChevronRightRenderer = cRight.Renderer;

        var crown = MakePiece(node.Root, "Finish crown", CityColor.LitWindow);
        node.Crown = crown.Transform;
        node.CrownRenderer = crown.Renderer;
        node.Crown.gameObject.SetActive(isStart);

        rootGo.SetActive(false);
        return node;
    }

    TransformPylon MakePiece(Transform parent, string name, CityColor color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = sharedCube;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = CityMaterials.Get(color);
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        CreatedRenderers++;
        return new TransformPylon { Transform = go.transform, Renderer = mr };
    }

    void BuildBurstPool()
    {
        var go = new GameObject("Parkour burst particles");
        go.transform.SetParent(transform, false);
        burstSystem = go.AddComponent<ParticleSystem>();
        burstSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = burstSystem.main;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = 32;
        main.duration = 0.45f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        main.gravityModifier = 0.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        var emission = burstSystem.emission;
        emission.enabled = false;
        var shape = burstSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1.1f;
        var sizeOverLifetime = burstSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
        burstRenderer = go.GetComponent<ParticleSystemRenderer>();
        burstRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        burstRenderer.mesh = sharedCube;
        burstRenderer.sharedMaterial = CityMaterials.Get(CityColor.Cyan);
        burstRenderer.shadowCastingMode = ShadowCastingMode.Off;
        burstRenderer.receiveShadows = false;
        CreatedRenderers++;
    }

    public void ConfigureStartMarker(
        int slot,
        Vector3 position,
        Vector3 forward,
        CityColor primary,
        CityColor secondary,
        ParkourMarkerState state)
    {
        if (slot < 0 || slot >= startNodes.Length) return;
        var node = startNodes[slot];
        if (state == ParkourMarkerState.Hidden)
        {
            node.State = ParkourMarkerState.Hidden;
            if (node.Root.gameObject.activeSelf) node.Root.gameObject.SetActive(false);
            return;
        }
        node.BasePosition = position;
        Vector3 flatFwd = Vector3.ProjectOnPlane(forward, Vector3.up);
        node.ForwardDir = flatFwd.sqrMagnitude > 0.001f ? flatFwd.normalized : Vector3.forward;
        node.Primary = primary;
        node.Secondary = secondary;
        node.Finish = CityColor.Neon;
        if (node.State != state)
        {
            node.State = state;
            node.StateChangedAt = Time.unscaledTime;
        }
        ApplyNodeVisuals(node, isStart: true);
        if (!node.Root.gameObject.activeSelf) node.Root.gameObject.SetActive(true);
    }

    public void ConfigureCheckpoint(
        int index,
        Vector3 position,
        Vector3 nextDirection,
        float radius,
        CityColor primary,
        CityColor secondary,
        CityColor finishColor,
        ParkourMarkerState state)
    {
        if (index < 0 || index >= checkpointNodes.Length) return;
        var node = checkpointNodes[index];
        if (state == ParkourMarkerState.Hidden)
        {
            node.State = ParkourMarkerState.Hidden;
            if (node.Root.gameObject.activeSelf) node.Root.gameObject.SetActive(false);
            return;
        }
        node.BasePosition = position;
        node.TriggerRadius = Mathf.Max(1.5f, radius);
        Vector3 flatDir = Vector3.ProjectOnPlane(nextDirection, Vector3.up);
        node.ForwardDir = flatDir.sqrMagnitude > 0.001f ? flatDir.normalized : Vector3.forward;
        node.Primary = primary;
        node.Secondary = secondary;
        node.Finish = finishColor;
        if (node.State != state)
        {
            node.State = state;
            node.StateChangedAt = Time.unscaledTime;
        }
        ApplyNodeVisuals(node, isStart: false);
        if (!node.Root.gameObject.activeSelf) node.Root.gameObject.SetActive(true);
    }

    public void HideAllCheckpoints()
    {
        for (int i = 0; i < checkpointNodes.Length; i++)
        {
            checkpointNodes[i].State = ParkourMarkerState.Hidden;
            if (checkpointNodes[i].Root != null && checkpointNodes[i].Root.gameObject.activeSelf)
                checkpointNodes[i].Root.gameObject.SetActive(false);
        }
    }

    public void EmitBurst(Vector3 position, CityColor color, int count = 12)
    {
        if (burstSystem == null || count <= 0) return;
        burstSystem.transform.position = position;
        var mat = CityMaterials.Get(color);
        if (burstRenderer.sharedMaterial != mat)
        {
            burstSystem.Clear();
            burstRenderer.sharedMaterial = mat;
        }
        if (!burstSystem.isPlaying) burstSystem.Play();
        burstSystem.Emit(Mathf.Clamp(count, 1, 24));
        BurstsPlayed++;
    }

    void ApplyNodeVisuals(GateNode node, bool isStart)
    {
        node.Root.position = node.BasePosition;
        node.Root.rotation = Quaternion.LookRotation(node.ForwardDir, Vector3.up);

        bool isFinish = node.State == ParkourMarkerState.FinishInactive || node.State == ParkourMarkerState.FinishNext;
        bool isHighlighted = node.State == ParkourMarkerState.Next || node.State == ParkourMarkerState.FinishNext || node.State == ParkourMarkerState.StartIdle;
        bool isCompleted = node.State == ParkourMarkerState.Completed;

        CityColor ringColor = isCompleted ? CityColor.Slate
            : isFinish ? (node.State == ParkourMarkerState.FinishNext ? node.Finish : node.Primary)
            : isHighlighted ? node.Primary : node.Secondary;

        CityColor pylonColor = isCompleted ? CityColor.Slate
            : isFinish ? node.Primary : node.Secondary;

        CityColor accentColor = isFinish ? CityColor.LitWindow : CityColor.Neon;

        var ringMat = CityMaterials.Get(ringColor);
        var pylonMat = CityMaterials.Get(pylonColor);
        var accentMat = CityMaterials.Get(accentColor);

        float ringRadius = isStart ? 1.85f : (isFinish ? 2.35f : 1.95f);
        float segThickness = isHighlighted ? 0.22f : (isCompleted ? 0.10f : 0.14f);
        float segLength = (2f * Mathf.PI * ringRadius / RingSegments) * 0.92f;

        node.RingHolder.localPosition = isStart ? new Vector3(0f, 0.15f, 0f) : Vector3.zero;
        node.RingHolder.localRotation = isStart ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;

        for (int s = 0; s < RingSegments; s++)
        {
            float angle = s * Mathf.PI * 2f / RingSegments;
            var seg = node.Segments[s];
            seg.localPosition = new Vector3(Mathf.Cos(angle) * ringRadius, Mathf.Sin(angle) * ringRadius, 0f);
            seg.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
            seg.localScale = new Vector3(segLength, segThickness, segThickness);
            node.SegmentRenderers[s].sharedMaterial = ringMat;
        }

        float pylonX = ringRadius + 0.25f;
        float pylonH = isStart ? 2.6f : (isFinish ? 3.2f : 2.2f);
        float pylonW = isHighlighted ? 0.24f : 0.16f;
        node.LeftPylon.Transform.localPosition = new Vector3(-pylonX, isStart ? pylonH * 0.35f : 0f, 0f);
        node.LeftPylon.Transform.localScale = new Vector3(pylonW, pylonH, pylonW);
        node.LeftPylon.Renderer.sharedMaterial = pylonMat;

        node.RightPylon.Transform.localPosition = new Vector3(pylonX, isStart ? pylonH * 0.35f : 0f, 0f);
        node.RightPylon.Transform.localScale = new Vector3(pylonW, pylonH, pylonW);
        node.RightPylon.Renderer.sharedMaterial = pylonMat;

        bool showChevrons = !isCompleted && (isHighlighted || isStart);
        node.ChevronHolder.gameObject.SetActive(showChevrons);
        if (showChevrons)
        {
            node.ChevronHolder.localPosition = isStart ? new Vector3(0f, 1.35f, 0f) : Vector3.zero;
            node.ChevronLeft.localPosition = new Vector3(-0.32f, 0f, 0.25f);
            node.ChevronLeft.localRotation = Quaternion.Euler(0f, 38f, 0f);
            node.ChevronLeft.localScale = new Vector3(0.16f, 0.16f, 0.78f);
            node.ChevronLeftRenderer.sharedMaterial = accentMat;

            node.ChevronRight.localPosition = new Vector3(0.32f, 0f, 0.25f);
            node.ChevronRight.localRotation = Quaternion.Euler(0f, -38f, 0f);
            node.ChevronRight.localScale = new Vector3(0.16f, 0.16f, 0.78f);
            node.ChevronRightRenderer.sharedMaterial = accentMat;
        }

        bool showCrown = isStart || isFinish;
        node.Crown.gameObject.SetActive(showCrown);
        if (showCrown)
        {
            node.Crown.localPosition = new Vector3(0f, isStart ? 2.4f : ringRadius + 0.75f, 0f);
            node.Crown.localRotation = Quaternion.Euler(45f, 45f, 0f);
            float crownSize = isFinish ? 0.62f : 0.45f;
            node.Crown.localScale = new Vector3(crownSize, crownSize, crownSize);
            node.CrownRenderer.sharedMaterial = accentMat;
        }
    }

    void LateUpdate()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < startNodes.Length; i++)
        {
            var n = startNodes[i];
            if (n == null || n.State == ParkourMarkerState.Hidden || !n.Root.gameObject.activeSelf) continue;
            AnimateNode(n, now, isStart: true);
        }
        for (int i = 0; i < checkpointNodes.Length; i++)
        {
            var n = checkpointNodes[i];
            if (n == null || n.State == ParkourMarkerState.Hidden || !n.Root.gameObject.activeSelf) continue;
            AnimateNode(n, now, isStart: false);
        }
    }

    void AnimateNode(GateNode node, float now, bool isStart)
    {
        bool active = node.State == ParkourMarkerState.Next || node.State == ParkourMarkerState.FinishNext || node.State == ParkourMarkerState.StartIdle;
        float spinSpeed = active ? 55f : 18f;
        if (isStart)
        {
            node.RingHolder.localRotation = Quaternion.Euler(90f, now * spinSpeed, 0f);
        }
        else
        {
            node.RingHolder.localRotation = Quaternion.Euler(0f, 0f, now * spinSpeed);
            float bob = active ? Mathf.Sin(now * 3.2f) * 0.18f : 0f;
            node.Root.position = node.BasePosition + Vector3.up * bob;
        }

        if (node.ChevronHolder.gameObject.activeSelf)
        {
            float surge = Mathf.Sin(now * 5.5f) * 0.28f;
            node.ChevronHolder.localPosition = (isStart ? new Vector3(0f, 1.35f, 0f) : Vector3.zero) + new Vector3(0f, 0f, surge);
        }

        if (node.Crown.gameObject.activeSelf)
        {
            node.Crown.localRotation = Quaternion.Euler(45f, now * 90f, 45f);
        }

        if (node.State == ParkourMarkerState.Completed)
        {
            float age = now - node.StateChangedAt;
            float scale = age < 0.25f ? Mathf.Lerp(1.25f, 0.72f, age / 0.25f) : 0.72f;
            node.RingHolder.localScale = Vector3.one * scale;
        }
        else
        {
            float pulse = active ? 1f + 0.06f * Mathf.Sin(now * 6f) : 0.88f;
            node.RingHolder.localScale = Vector3.one * pulse;
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
