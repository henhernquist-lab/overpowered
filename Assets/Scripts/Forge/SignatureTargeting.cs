using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Bounded queries shared by the final four signatures. Saturated queries fail closed.
public static class SignatureTargeting
{
    static readonly RaycastHit[] sight = new RaycastHit[64];
    public static bool Clear(Vector3 from, Vector3 to, Transform owner, bool propsBlock = false)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .0001f) return true;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, sight, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (count == sight.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var c = sight[i].collider;
            if (c.transform.root == owner || c.GetComponentInParent<CityNpc>() != null) continue;
            if (!propsBlock && c.attachedRigidbody != null) continue;
            return false;
        }
        return true;
    }
    public static bool Aim(SynergyRunner r, out RaycastHit hit)
    {
        hit = default;
        Vector3 origin = r.User.AimOrigin;
        Vector3 delta = r.User.AimPoint(r.Definition.Range) - origin;
        int count = Physics.RaycastNonAlloc(origin, delta.normalized, sight, Mathf.Min(delta.magnitude + .05f, r.Definition.Range), ~0, QueryTriggerInteraction.Ignore);
        if (count == sight.Length) return false;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (sight[i].transform.root == r.transform || sight[i].distance >= nearest) continue;
            nearest = sight[i].distance; hit = sight[i];
        }
        return nearest < float.PositiveInfinity;
    }
    public static bool Ground(SynergyRunner r)
    {
        if (!Aim(r, out var hit)) return false;
        Vector3 point = hit.point;
        var npc = hit.collider.GetComponentInParent<CityNpc>();
        if (npc != null)
        {
            point = npc.transform.position;
            if (!Physics.Raycast(point + Vector3.up * .4f, Vector3.down, out hit, 3f, ~0, QueryTriggerInteraction.Ignore)) return false;
        }
        if (hit.normal.y < .65f || hit.rigidbody != null || hit.collider.GetComponentInParent<CityNpc>() != null) return false;
        point = hit.point;
        if (Vector3.Distance(r.User.AimOrigin, point) > r.Definition.Range + .1f ||
            !Clear(r.User.AimOrigin, point + Vector3.up * .12f, r.transform, true)) return false;
        r.Target = point;
        return true;
    }
    public static int Gather(SynergyRunner r, Vector3 center, float radius, List<CityNpc> into, int limit)
    {
        into.Clear(); limit = Mathf.Clamp(limit, 1, 16);
        int count = Physics.OverlapSphereNonAlloc(center + Vector3.up, radius + 1f, r.Hits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var npc = r.Hits[i].GetComponentInParent<CityNpc>();
            if (npc == null || npc.Dead || !npc.Hostile || into.Contains(npc)) continue;
            Vector3 delta = npc.transform.position - center;
            if (Mathf.Abs(delta.y) > 2.5f || new Vector2(delta.x, delta.z).sqrMagnitude > radius * radius ||
                !Clear(center + Vector3.up, npc.transform.position + Vector3.up, r.transform)) continue;
            float distance = delta.sqrMagnitude; int at = into.Count;
            while (at > 0 && (into[at - 1].transform.position - center).sqrMagnitude > distance) at--;
            if (at >= limit) continue;
            into.Insert(at, npc);
            if (into.Count > limit) into.RemoveAt(limit);
        }
        return into.Count;
    }
    public static bool Brute(CityNpc npc) => npc.Archetype != null &&
        (npc.Archetype.Kind == AttackKind.Slam || npc.Archetype.HealthMultiplier >= 2.5f);

    // Never warp across a NavMesh boundary or solid geometry. No fallback teleport for live agents.
    public static bool MoveOnGround(CityNpc npc, Vector3 desired, Transform owner)
    {
        Vector3 from = npc.transform.position;
        if (!Clear(from + Vector3.up * .6f, desired + Vector3.up * .6f, owner, true)) return false;
        var agent = npc.Agent;
        if (agent != null && agent.enabled)
        {
            if (!agent.isOnNavMesh || !NavMesh.SamplePosition(desired, out var nav, .35f, agent.areaMask) ||
                Mathf.Abs(nav.position.y - from.y) > .5f || NavMesh.Raycast(from, nav.position, out _, agent.areaMask)) return false;
            agent.Move(nav.position - from);
            return true;
        }
        // Isolated verification actors have navigation disabled; require real solid ground here too.
        if (!Physics.Raycast(desired + Vector3.up * .3f, Vector3.down, out var floor, .8f, ~0, QueryTriggerInteraction.Ignore) ||
            floor.normal.y < .65f || floor.rigidbody != null || Mathf.Abs(floor.point.y - from.y) > .5f) return false;
        npc.transform.position = floor.point;
        return true;
    }
}
