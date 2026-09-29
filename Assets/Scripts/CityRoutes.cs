using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;

/// A street crossing offered at At (m along this route) to route To at ToAt; the straight line between them is NavMesh-clear.
public struct RouteTransfer { public float At, ToAt, Distance; public int To; }

/// One baked pedestrian / patrol route: a Unity Spline (straight runs + Bezier corner fillets) sampled at uniform arc
/// length, heights projected onto the city NavMesh. Far NPCs ride these samples analytically (no NavMesh pathing);
/// near NPCs steer their NavMeshAgent at a point ahead on them.
public sealed class CityRoute
{
    public int Index, District; public string Name, Kind; public RouteUse Use; public bool Closed;
    public Spline Spline; public float Length, Step; public Vector3[] Points; public int OffMesh;
    public readonly List<RouteTransfer> Transfers = new List<RouteTransfer>();
    public int Segments => Closed ? Points.Length : Points.Length - 1;
    public float Wrap(float s) { if (!Closed) return Mathf.Clamp(s, 0f, Length); s %= Length; return s < 0f ? s + Length : s; }
    /// Signed distance from a to b along the route (closed routes take the short way round).
    public float Delta(float a, float b)
    {
        float d = b - a; if (!Closed) return d;
        d %= Length; if (d > Length * .5f) d -= Length; else if (d < -Length * .5f) d += Length; return d;
    }
    float SegmentLength(int i) => i == Segments - 1 ? Length - i * Step : Step;
    int Segment(float s, out float u)
    {
        s = Wrap(s); int i = Mathf.Clamp((int)(s / Step), 0, Segments - 1);
        float len = SegmentLength(i); u = len > 1e-4f ? Mathf.Clamp01((s - i * Step) / len) : 0f; return i;
    }
    Vector3 P(int i) => Points[i % Points.Length];
    public Vector3 Position(float s) { int i = Segment(s, out float u); return Vector3.LerpUnclamped(P(i), P(i + 1), u); }
    /// Flat unit tangent in the direction of increasing s.
    public Vector3 Tangent(float s) { int i = Segment(s, out _); var t = P(i + 1) - P(i); t.y = 0f; return t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward; }
    /// Flat unit normal (left of the tangent).
    public Vector3 Normal(float s) { var t = Tangent(s); return new Vector3(-t.z, 0f, t.x); }
    /// Nearest route distance to p searching +/- window around hint (whole route when window >= Length). lateral = flat distance.
    public float Project(Vector3 p, float hint, float window, out float lateral)
    {
        int count = window >= Length ? Segments : Mathf.Min(Segments, Mathf.CeilToInt(2f * window / Step) + 2);
        int start = window >= Length ? 0 : Mathf.FloorToInt((hint - window) / Step);
        if (!Closed) start = Mathf.Clamp(start, 0, Segments - 1);
        float best = float.MaxValue, bestS = hint;
        for (int k = 0; k < count; k++)
        {
            int i = start + k; if (!Closed && i >= Segments) break;
            i = ((i % Segments) + Segments) % Segments;
            Vector3 a = P(i), b = P(i + 1); float ax = b.x - a.x, az = b.z - a.z, l2 = ax * ax + az * az;
            float t = l2 > 1e-8f ? Mathf.Clamp01(((p.x - a.x) * ax + (p.z - a.z) * az) / l2) : 0f;
            float dx = a.x + ax * t - p.x, dz = a.z + az * t - p.z, d = dx * dx + dz * dz;
            if (d < best) { best = d; bestS = i * Step + t * SegmentLength(i); }
        }
        lateral = Mathf.Sqrt(best); return Wrap(bestS);
    }
    public float ProjectAll(Vector3 p, out float lateral) => Project(p, 0f, float.MaxValue, out lateral);
}

/// The city's pedestrian / patrol route network (CityLayout.Routes): built once per session from CityPlan.Routes after
/// the NavMesh exists. Pure data plus one SplineContainer per district so a designer can select and inspect the splines
/// in the Editor Scene view; no runtime meshes or materials. Walkers (RouteWalker) query it.
public sealed class CityRoutes : MonoBehaviour
{
    public readonly List<CityRoute> Routes = new List<CityRoute>();
    /// Routes left out at build (too far off the NavMesh), with the reason. Reported by verification.
    public readonly List<string> Dropped = new List<string>();
    public readonly List<SplineContainer> Containers = new List<SplineContainer>();
    public RouteSettings Settings { get; private set; }
    public double BuildMilliseconds { get; private set; }
    public int TransferCount { get; private set; }
    const float Cell = 8f;
    readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    public void Build(CityPlan plan, RouteSettings settings)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Settings = settings;
        float step = Mathf.Max(.25f, settings.BakeSpacing);
        var perDistrict = new Dictionary<int, List<Spline>>();
        foreach (var r in plan.Routes)
        {
            var spline = MakeSpline(r.Points, r.Closed, settings.CornerRadius);
            var points = Bake(spline, r.Closed, step, out float length);
            if (points == null) { Dropped.Add($"{r.Name}: degenerate"); continue; }
            int off = 0;
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                if (NavMesh.SamplePosition(p, out var hit, settings.OnMeshTolerance + 1f, NavMesh.AllAreas) && Flat(hit.position - p) <= settings.OnMeshTolerance) points[i].y = hit.position.y;
                else off++;
            }
            if (off > points.Length * settings.MaxOffMeshFraction) { Dropped.Add($"{r.Name}: {off}/{points.Length} samples off the NavMesh"); continue; }
            var route = new CityRoute { Index = Routes.Count, District = r.District, Name = r.Name, Kind = r.Kind, Use = r.Use, Closed = r.Closed, Spline = spline, Length = length, Step = step, Points = points, OffMesh = off };
            Routes.Add(route);
            if (!perDistrict.TryGetValue(r.District, out var list)) perDistrict[r.District] = list = new List<Spline>();
            list.Add(spline);
        }
        for (int r = 0; r < Routes.Count; r++)
        {
            var pts = Routes[r].Points;
            for (int k = 0; k < pts.Length; k++)
            {
                var key = Key(Mathf.FloorToInt(pts[k].x / Cell), Mathf.FloorToInt(pts[k].z / Cell));
                if (!grid.TryGetValue(key, out var bucket)) grid[key] = bucket = new List<int>();
                bucket.Add(r << 16 | k);
            }
        }
        BuildTransfers(settings);
        // Editor inspection: one SplineContainer per district (world-space knots). Nothing renders them at runtime.
        foreach (var pair in perDistrict.OrderBy(p => p.Key))
        {
            var go = new GameObject("Routes: " + (pair.Key >= 0 && pair.Key < plan.Districts.Count ? plan.Districts[pair.Key].Name : "?"));
            go.transform.SetParent(transform, false);
            var container = go.AddComponent<SplineContainer>(); container.Splines = pair.Value; Containers.Add(container);
        }
        BuildMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

    /// Polyline corners -> Unity Spline: straight runs, and a Bezier fillet (quarter-circle handles) of up to radius at
    /// every corner, so walkers round street corners instead of pivoting on them.
    public static Spline MakeSpline(List<Vector3> corners, bool closed, float radius)
    {
        var pts = new List<Vector3>();
        foreach (var c in corners) if (pts.Count == 0 || (pts[pts.Count - 1] - c).sqrMagnitude > 1e-4f) pts.Add(c);
        if (closed && pts.Count > 2 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-4f) pts.RemoveAt(pts.Count - 1);
        int n = pts.Count;
        var pos = new List<Vector3>(); var tin = new List<Vector3>(); var tout = new List<Vector3>(); var fillet = new List<bool>();
        for (int i = 0; i < n; i++)
        {
            Vector3 c = pts[i];
            bool corner = n > 2 && (closed || (i > 0 && i < n - 1));
            if (corner)
            {
                Vector3 a = c - pts[(i - 1 + n) % n], b = pts[(i + 1) % n] - c; float la = a.magnitude, lb = b.magnitude;
                if (la > 1e-3f && lb > 1e-3f)
                {
                    a /= la; b /= lb; float r = Mathf.Min(radius, la * .45f, lb * .45f);
                    if (r > .05f && Vector3.Angle(a, b) > 1f)
                    {
                        float h = r * .5523f;
                        pos.Add(c - a * r); tin.Add(Vector3.zero); tout.Add(a * h); fillet.Add(true);
                        pos.Add(c + b * r); tin.Add(-b * h); tout.Add(Vector3.zero); fillet.Add(false);
                        continue;
                    }
                }
            }
            pos.Add(c); tin.Add(Vector3.zero); tout.Add(Vector3.zero); fillet.Add(false);
        }
        int m = pos.Count, curves = closed ? m : m - 1;
        for (int j = 0; j < curves; j++)
        {
            if (fillet[j]) continue;
            Vector3 d = (pos[(j + 1) % m] - pos[j]) / 3f; tout[j] = d; tin[(j + 1) % m] = -d;
        }
        if (!closed && m > 1) { tin[0] = -tout[0]; tout[m - 1] = -tin[m - 1]; }
        var spline = new Spline();
        for (int j = 0; j < m; j++) spline.Add(new BezierKnot((float3)pos[j], (float3)tin[j], (float3)tout[j], quaternion.identity), TangentMode.Broken);
        spline.Closed = closed;
        return spline;
    }

    /// Uniform arc-length samples every step metres (closed: the last sample joins the first).
    static Vector3[] Bake(Spline spline, bool closed, float step, out float length)
    {
        length = 0f;
        int knots = spline.Count, curves = closed ? knots : knots - 1; if (curves < 1) return null;
        var dense = new List<Vector3>(); var cumulative = new List<float>();
        for (int c = 0; c < curves; c++)
        {
            var curve = spline.GetCurve(c);
            int n = Mathf.Max(2, Mathf.CeilToInt(CurveUtility.ApproximateLength(curve) / .2f));
            for (int k = c == 0 ? 0 : 1; k <= n; k++)
            {
                Vector3 p = (Vector3)CurveUtility.EvaluatePosition(curve, k / (float)n);
                if (dense.Count > 0) length += Vector3.Distance(dense[dense.Count - 1], p);
                dense.Add(p); cumulative.Add(length);
            }
        }
        if (length < step) return null;
        int count = closed ? Mathf.CeilToInt(length / step - 1e-4f) : Mathf.CeilToInt(length / step - 1e-4f) + 1;
        var result = new Vector3[count]; int j = 0;
        for (int i = 0; i < count; i++)
        {
            float s = !closed && i == count - 1 ? length : i * step;
            while (j < cumulative.Count - 2 && cumulative[j + 1] < s) j++;
            float span = cumulative[j + 1] - cumulative[j];
            result[i] = Vector3.Lerp(dense[j], dense[j + 1], span > 1e-6f ? (s - cumulative[j]) / span : 0f);
        }
        return result;
    }

    void BuildTransfers(RouteSettings settings)
    {
        float reach = settings.TransferDistance;
        foreach (var a in Routes)
        {
            for (float s = settings.TransferSpacing * .5f; s < a.Length; s += settings.TransferSpacing)
            {
                Vector3 p = a.Position(s);
                var best = new Dictionary<int, (int Sample, float D)>();
                int x0 = Mathf.FloorToInt((p.x - reach) / Cell), x1 = Mathf.FloorToInt((p.x + reach) / Cell), z0 = Mathf.FloorToInt((p.z - reach) / Cell), z1 = Mathf.FloorToInt((p.z + reach) / Cell);
                for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
                {
                    if (!grid.TryGetValue(Key(x, z), out var bucket)) continue;
                    foreach (int code in bucket)
                    {
                        int r = code >> 16; if (r == a.Index || (Routes[r].Use & a.Use) == 0) continue;
                        float d = Flat(Routes[r].Points[code & 0xFFFF] - p);
                        if (d <= reach && (!best.TryGetValue(r, out var e) || d < e.D)) best[r] = (code & 0xFFFF, d);
                    }
                }
                foreach (var pair in best)
                {
                    var b = Routes[pair.Key]; float sb = b.Project(p, pair.Value.Sample * b.Step, 2f * b.Step, out float d);
                    if (d > reach) continue;
                    if (NavMesh.Raycast(p, b.Position(sb), out _, NavMesh.AllAreas)) continue; // leaves the NavMesh (water, building)
                    a.Transfers.Add(new RouteTransfer { At = s, To = b.Index, ToAt = sb, Distance = d }); TransferCount++;
                }
            }
            a.Transfers.Sort((u, v) => u.At.CompareTo(v.At));
        }
    }

    /// Nearest route of the given use within maxDistance (flat metres). Returns false when none.
    public bool Nearest(Vector3 p, RouteUse use, float maxDistance, out int route, out float s, out float distance)
    {
        route = -1; s = 0f; distance = float.MaxValue; int sample = -1;
        int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell), rings = Mathf.CeilToInt(maxDistance / Cell) + 1;
        for (int ring = 0; ring <= rings; ring++)
        {
            for (int x = cx - ring; x <= cx + ring; x++) for (int z = cz - ring; z <= cz + ring; z++)
            {
                if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Abs(z - cz)) != ring || !grid.TryGetValue(Key(x, z), out var bucket)) continue;
                foreach (int code in bucket)
                {
                    int r = code >> 16; if ((Routes[r].Use & use) == 0) continue;
                    float d = Flat(Routes[r].Points[code & 0xFFFF] - p);
                    if (d < distance) { distance = d; route = r; sample = code & 0xFFFF; }
                }
            }
            if (route >= 0 && distance <= ring * Cell) break;
        }
        if (route < 0 || distance > maxDistance + Cell) { route = -1; return false; }
        var best = Routes[route]; s = best.Project(p, sample * best.Step, 2f * best.Step, out distance);
        return distance <= maxDistance;
    }

    /// A random point on a route of the given use (routes weighted by length) accepted by the predicate.
    public bool RandomPoint(RouteUse use, System.Predicate<Vector3> accept, int attempts, out int route, out float s)
    {
        route = -1; s = 0f; float total = 0f;
        foreach (var r in Routes) if ((r.Use & use) != 0) total += r.Length;
        if (total <= 0f) return false;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float pick = UnityEngine.Random.value * total;
            foreach (var r in Routes)
            {
                if ((r.Use & use) == 0) continue;
                if (pick > r.Length) { pick -= r.Length; continue; }
                if (accept(r.Position(pick))) { route = r.Index; s = pick; return true; }
                break;
            }
        }
        return false;
    }

    /// Deterministic description of every baked route (for same-seed / different-seed checks).
    public string Fingerprint()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in Routes)
        {
            sb.Append(r.Name).Append('|').Append(r.Kind).Append('|').Append((int)r.Use).Append(r.Closed ? 'C' : 'O').Append('|').Append(r.Length.ToString("F2")).Append('|');
            for (int i = 0; i < r.Points.Length; i += 4) sb.Append(Mathf.RoundToInt(r.Points[i].x * 100)).Append(',').Append(Mathf.RoundToInt(r.Points[i].z * 100)).Append(';');
            sb.Append('#').Append(r.Transfers.Count).Append('\n');
        }
        return sb.ToString();
    }

#if UNITY_EDITOR
    /// Editor-only debug drawing (Scene view, object selected): walk routes green, patrol routes blue, transfers yellow.
    void OnDrawGizmosSelected()
    {
        foreach (var r in Routes)
        {
            Gizmos.color = r.Use == RouteUse.Patrol ? new Color(.2f, .45f, 1f) : r.Use == RouteUse.Walk ? new Color(.2f, .9f, .3f) : new Color(.2f, .9f, .9f);
            for (int i = 0; i < r.Segments; i++) Gizmos.DrawLine(r.Points[i] + Vector3.up * .3f, r.Points[(i + 1) % r.Points.Length] + Vector3.up * .3f);
            Gizmos.color = Color.yellow;
            foreach (var t in r.Transfers) Gizmos.DrawLine(r.Position(t.At) + Vector3.up * .3f, Routes[t.To].Position(t.ToAt) + Vector3.up * .3f);
        }
    }
#endif
}
