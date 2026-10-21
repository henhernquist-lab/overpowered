using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct ParkourResolvedPoint
{
    public readonly Vector3 Position;
    public readonly Vector3 SupportPoint;
    public readonly float TriggerRadius;
    public readonly string Label;
    public readonly int BuildingIndex;
    public readonly bool Valid;

    public ParkourResolvedPoint(Vector3 position, Vector3 supportPoint, float triggerRadius, string label, int buildingIndex, bool valid)
    {
        Position = position;
        SupportPoint = supportPoint;
        TriggerRadius = triggerRadius;
        Label = label;
        BuildingIndex = buildingIndex;
        Valid = valid;
    }
}

public sealed class ParkourResolvedCourse
{
    public readonly ParkourCourseDefinition Definition;
    public readonly ParkourResolvedPoint Start;
    public readonly Vector3 StartStandingPosition;
    public readonly Quaternion StartFacing;
    public readonly ParkourResolvedPoint[] Checkpoints;
    public readonly bool Valid;
    public readonly string FailureReason;

    public ParkourResolvedCourse(
        ParkourCourseDefinition definition,
        ParkourResolvedPoint start,
        Vector3 startStandingPosition,
        Quaternion startFacing,
        ParkourResolvedPoint[] checkpoints,
        bool valid,
        string failureReason)
    {
        Definition = definition;
        Start = start;
        StartStandingPosition = startStandingPosition;
        StartFacing = startFacing;
        Checkpoints = checkpoints;
        Valid = valid;
        FailureReason = failureReason;
    }
}

/// Resolves ParkourCourseDefinition specs against the actual generated CityDistrict buildings and structures,
/// verifying physical support colliders and capsule clearance so no checkpoint is unreachable or embedded.
public static class ParkourCoursePlacement
{
    static readonly Collider[] overlapBuffer = new Collider[16];

    public static ParkourResolvedCourse Resolve(CityDistrict city, GameTuning tuning, ParkourCourseDefinition definition)
    {
        if (city == null || tuning == null || definition == null)
            return new ParkourResolvedCourse(definition, default, Vector3.zero, Quaternion.identity, Array.Empty<ParkourResolvedPoint>(), false, "Missing city, tuning, or course definition.");

        if (!definition.IsValid(out string invalidReason))
            return new ParkourResolvedCourse(definition, default, Vector3.zero, Quaternion.identity, Array.Empty<ParkourResolvedPoint>(), false, invalidReason);

        var settings = city.Art != null ? city.Art.Settings : Resources.Load<CityArtSettings>("CityArtSettings");
        var startPoint = ResolveSpec(city, tuning, settings, definition.Start, isStart: true, out string startError);
        if (!startPoint.Valid)
            return new ParkourResolvedCourse(definition, default, Vector3.zero, Quaternion.identity, Array.Empty<ParkourResolvedPoint>(), false, "Start invalid: " + startError);

        var resolvedCheckpoints = new ParkourResolvedPoint[definition.Checkpoints.Length];
        for (int i = 0; i < definition.Checkpoints.Length; i++)
        {
            var cp = ResolveSpec(city, tuning, settings, definition.Checkpoints[i], isStart: false, out string cpError);
            if (!cp.Valid)
                return new ParkourResolvedCourse(definition, startPoint, Vector3.zero, Quaternion.identity, resolvedCheckpoints, false, $"Checkpoint {i} invalid: {cpError}");
            resolvedCheckpoints[i] = cp;
        }

        Vector3 standing = startPoint.SupportPoint + Vector3.up * 0.08f;
        Vector3 toFirst = resolvedCheckpoints[0].Position - standing;
        toFirst.y = 0f;
        Quaternion facing = toFirst.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toFirst.normalized, Vector3.up) : Quaternion.identity;

        return new ParkourResolvedCourse(definition, startPoint, standing, facing, resolvedCheckpoints, true, null);
    }

    static ParkourResolvedPoint ResolveSpec(
        CityDistrict city,
        GameTuning tuning,
        CityArtSettings artSettings,
        ParkourCheckpointSpec spec,
        bool isStart,
        out string error)
    {
        error = null;
        int buildingIndex = -1;
        Vector3 baseAnchor = Vector3.zero;
        float expectedSupportY = 0f;

        switch (spec.Anchor)
        {
            case ParkourAnchorKind.WorldPoint:
                baseAnchor = new Vector3(spec.ReferencePosition.x + spec.Offset.x, spec.Offset.y, spec.ReferencePosition.y + spec.Offset.z);
                expectedSupportY = baseAnchor.y - Mathf.Max(0.5f, spec.Offset.y);
                break;

            case ParkourAnchorKind.LandmarkOffset:
            {
                Vector3 landmarkPos = new Vector3(spec.ReferencePosition.x, 0f, spec.ReferencePosition.y);
                if (city.Plan != null && !string.IsNullOrEmpty(spec.LandmarkName))
                {
                    for (int i = 0; i < city.Plan.Structures.Count; i++)
                    {
                        var s = city.Plan.Structures[i];
                        if (string.Equals(s.Recipe, spec.LandmarkName, StringComparison.OrdinalIgnoreCase))
                        {
                            landmarkPos = s.Position;
                            break;
                        }
                    }
                }
                expectedSupportY = landmarkPos.y + spec.Offset.y;
                baseAnchor = new Vector3(landmarkPos.x + spec.Offset.x, expectedSupportY, landmarkPos.z + spec.Offset.z);
                break;
            }

            case ParkourAnchorKind.BuildingRoof:
            case ParkourAnchorKind.BuildingSetback:
            case ParkourAnchorKind.BuildingBase:
            {
                buildingIndex = FindNearestBuilding(city.Buildings, spec.District, spec.ReferencePosition);
                if (buildingIndex < 0)
                {
                    error = $"No building found in district {spec.District} near {spec.ReferencePosition}.";
                    return default;
                }
                var b = city.Buildings[buildingIndex];
                BuildingStyle style = null;
                if (artSettings != null && artSettings.Styles != null && artSettings.Styles.Length > 0)
                {
                    int styleIdx = artSettings.StyleOf(tuning.City, b, buildingIndex);
                    if (styleIdx >= 0 && styleIdx < artSettings.Styles.Length) style = artSettings.Styles[styleIdx];
                }
                Vector2 footprint = style != null ? style.Footprint : Vector2.one;
                float upperWidth = style != null ? style.UpperWidth : 1f;
                float setbackAt = style != null ? style.SetbackAt : 0.65f;
                float floorHeight = artSettings != null ? artSettings.FloorHeight : 3.2f;

                float lowW = b.Size.x * footprint.x;
                float lowD = b.Size.z * footprint.y;
                float upW = lowW * upperWidth;
                float upD = lowD * upperWidth;
                float baseBottomY = b.Position.y - b.Size.y * 0.5f;

                if (spec.Anchor == ParkourAnchorKind.BuildingRoof)
                {
                    expectedSupportY = baseBottomY + b.Size.y + 0.08f;
                    float x = b.Position.x + Mathf.Clamp(spec.Offset.x, -0.78f, 0.78f) * (upW * 0.5f);
                    float z = b.Position.z + Mathf.Clamp(spec.Offset.z, -0.78f, 0.78f) * (upD * 0.5f);
                    baseAnchor = new Vector3(x, expectedSupportY + Mathf.Max(0.4f, spec.Offset.y), z);
                }
                else if (spec.Anchor == ParkourAnchorKind.BuildingSetback)
                {
                    float entry = Mathf.Min(floorHeight, b.Size.y * 0.4f);
                    float split = Mathf.Max(entry, b.Size.y * setbackAt);
                    bool hasSetback = upperWidth < 0.98f && (b.Size.y - split) > 0.5f;
                    expectedSupportY = baseBottomY + (hasSetback ? split : b.Size.y) + 0.08f;
                    float spanX = hasSetback ? lowW : upW;
                    float spanZ = hasSetback ? lowD : upD;
                    float x = b.Position.x + Mathf.Clamp(spec.Offset.x, -0.92f, 0.92f) * (spanX * 0.5f);
                    float z = b.Position.z + Mathf.Clamp(spec.Offset.z, -0.92f, 0.92f) * (spanZ * 0.5f);
                    baseAnchor = new Vector3(x, expectedSupportY + Mathf.Max(0.4f, spec.Offset.y), z);
                }
                else
                {
                    expectedSupportY = tuning.City.SidewalkHeight;
                    float x = b.Position.x + spec.Offset.x * (lowW * 0.5f);
                    float z = b.Position.z + spec.Offset.z * (lowD * 0.5f);
                    baseAnchor = new Vector3(x, expectedSupportY + Mathf.Max(0.4f, spec.Offset.y), z);
                }
                break;
            }
        }

        // Probe physical support underneath to snap accurately to real static geometry and ensure reachability
        Vector3 supportPoint = new Vector3(baseAnchor.x, expectedSupportY, baseAnchor.z);
        bool foundSupport = TryFindSupport(new Vector3(baseAnchor.x, expectedSupportY + 4f, baseAnchor.z), 12f, out Vector3 hitSupport);
        if (foundSupport)
        {
            supportPoint = hitSupport;
            float hoverOffset = spec.Anchor == ParkourAnchorKind.WorldPoint || spec.Anchor == ParkourAnchorKind.LandmarkOffset
                ? 1.25f
                : Mathf.Max(0.8f, spec.Offset.y);
            baseAnchor.y = supportPoint.y + hoverOffset;
        }
        else if (isStart)
        {
            error = $"Start anchor at {baseAnchor} has no physical support surface below it.";
            return default;
        }

        // Verify standing capsule clearance at supportPoint if start, or trigger clearance at baseAnchor
        Vector3 checkFeet = supportPoint + Vector3.up * 0.12f;
        if (!IsCapsuleClearOfStatic(checkFeet, tuning.Movement.Radius * 0.85f, tuning.Movement.Height))
        {
            // Try a small lateral nudge toward open space if a rooftop prop or parapet edge overlaps
            bool nudged = false;
            for (int a = 0; a < 8; a++)
            {
                float rad = a * Mathf.PI * 0.25f;
                Vector3 candidateXZ = supportPoint + new Vector3(Mathf.Cos(rad) * 1.6f, 0f, Mathf.Sin(rad) * 1.6f);
                if (TryFindSupport(candidateXZ + Vector3.up * 3f, 6f, out Vector3 nudgedSupport) &&
                    Mathf.Abs(nudgedSupport.y - supportPoint.y) < 1.2f &&
                    IsCapsuleClearOfStatic(nudgedSupport + Vector3.up * 0.12f, tuning.Movement.Radius * 0.85f, tuning.Movement.Height))
                {
                    supportPoint = nudgedSupport;
                    baseAnchor = new Vector3(supportPoint.x, supportPoint.y + Mathf.Max(0.8f, spec.Offset.y), supportPoint.z);
                    nudged = true;
                    break;
                }
            }
            if (!nudged && isStart)
            {
                error = $"Start standing capsule blocked at {supportPoint}.";
                return default;
            }
        }

        return new ParkourResolvedPoint(
            baseAnchor,
            supportPoint,
            Mathf.Max(1.5f, spec.TriggerRadius),
            string.IsNullOrEmpty(spec.Label) ? "Checkpoint" : spec.Label,
            buildingIndex,
            true);
    }

    static int FindNearestBuilding(IReadOnlyList<BuildingPlacement> buildings, int district, Vector2 referenceXZ)
    {
        if (buildings == null || buildings.Count == 0) return -1;
        int best = -1;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < buildings.Count; i++)
        {
            var b = buildings[i];
            if (district >= 0 && b.District != district) continue;
            float dx = b.Position.x - referenceXZ.x;
            float dz = b.Position.z - referenceXZ.y;
            float d2 = dx * dx + dz * dz;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                best = i;
            }
        }
        return best;
    }

    static bool TryFindSupport(Vector3 origin, float maxDistance, out Vector3 point)
    {
        point = origin;
        var hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null || h.collider.attachedRigidbody != null || h.normal.y < 0.65f) continue;
            if (h.collider.GetComponentInParent<CityNpc>() != null ||
                h.collider.GetComponentInParent<SuperHeroController>() != null ||
                h.collider.GetComponentInParent<CityArtProp>() != null ||
                h.collider.GetComponentInParent<BreakableProp>() != null) continue;
            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                point = h.point;
                found = true;
            }
        }
        return found;
    }

    static bool IsCapsuleClearOfStatic(Vector3 feet, float radius, float height)
    {
        Vector3 bottom = feet + Vector3.up * (radius + 0.15f);
        Vector3 top = feet + Vector3.up * Mathf.Max(radius + 0.2f, height - radius);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = overlapBuffer[i];
            if (c == null || c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<CityNpc>() != null ||
                c.GetComponentInParent<SuperHeroController>() != null ||
                c.GetComponentInParent<CityArtProp>() != null ||
                c.GetComponentInParent<BreakableProp>() != null) continue;
            // Ignore thin floor/roof deck slabs touching the bottom of the feet
            if (c.bounds.max.y <= feet.y + 0.25f) continue;
            return false;
        }
        return true;
    }
}
