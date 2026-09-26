using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

/// The generated city (the whole island of districts). Name kept for the existing gameplay contract (WorldSession.City).
public sealed class CityDistrict : MonoBehaviour
{
    public readonly List<Vector3> Sidewalks = new List<Vector3>();
    /// District index of each Sidewalks entry (parallel list).
    public readonly List<int> SidewalkDistrict = new List<int>();
    public readonly List<BuildingPlacement> Buildings = new List<BuildingPlacement>();
    /// Encounter sites (street intersections, park/dock squares) and their district index (parallel lists).
    public readonly List<Vector3> EncounterSites = new List<Vector3>();
    public readonly List<int> EncounterSiteDistrict = new List<int>();
    public Vector3 Spawn { get; private set; }
    public CityArt Art {get;private set;}
    public CityPlan Plan {get;private set;}
    public CityLayout Layout {get;private set;}
    public IReadOnlyList<DistrictDefinition> DistrictDefinitions => Plan.Districts;
    /// NavMesh build time per district region (ms), same order as the districts.
    public readonly List<double> NavMeshMilliseconds = new List<double>();
    readonly List<NavMeshDataInstance> navmeshes = new List<NavMeshDataInstance>();
    /// Generation stage timings (ms, in build order). Timing only: measuring never changes what is generated.
    public readonly List<KeyValuePair<string,double>> Timings = new List<KeyValuePair<string,double>>();
    readonly System.Diagnostics.Stopwatch stageWatch = new System.Diagnostics.Stopwatch();
    public void Stage(string name) { Timings.Add(new KeyValuePair<string,double>(name, stageWatch.Elapsed.TotalMilliseconds)); stageWatch.Restart(); }
    public void Build(GameTuning tuning, CityLayout layout)
    {
        stageWatch.Restart();
        Layout=layout;
        Art=gameObject.AddComponent<CityArt>();Art.Initialize(tuning);
        var c = tuning.City; var p = tuning.Props;
        Plan = layout.Plan(c);
        if (layout.UseAuthoredBuildings) { Plan.Buildings.Clear(); Plan.Buildings.AddRange(layout.Buildings); }
        Buildings.AddRange(Plan.Buildings);
        Sidewalks.AddRange(Plan.Sidewalks); SidewalkDistrict.AddRange(Plan.SidewalkDistrict);
        EncounterSites.AddRange(Plan.Sites); EncounterSiteDistrict.AddRange(Plan.SiteDistrict);
        Spawn = Plan.Spawn;
        Stage("plan (layout generate)");
        Art.Ground(Plan); Art.Sea(Plan, layout);
        Stage("ground, water holes, decks, sea");
        for (int i = 0; i < Buildings.Count; i++)
        {
            var b = Buildings[i];
            Art.Building(b,i);
            if (b.RooftopReward)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name = "Rooftop discovery";
                marker.transform.SetParent(transform); marker.transform.position = b.Position + Vector3.up * (b.Size.y*.5f+c.RoofMarkerHeight);
                marker.transform.localScale = Vector3.one * c.RooftopPickupSize;
                marker.GetComponent<Collider>().enabled = false; marker.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Cyan);
                marker.AddComponent<RooftopDiscovery>().Id = c.Seed + ":roof:" + i;
            }
        }
        Stage("buildings (art) + rooftop markers");
        Art.Streets(Plan); Stage("streets (curbs, dashes, crossings)");
        Art.Structures(Plan); Art.Backdrop(Plan, layout.Backdrop); Stage("structures (trees, containers, cranes, landmarks) + backdrop");
        Art.FinishStaticGeometry(); Stage("static finalize (per-district StaticBatchingUtility)");
        foreach (var block in Plan.Blocks)
        {
            if (!Plan.Districts[block.District].BlockProps) continue;
            var center = new Vector3(block.Area.center.x, 0, block.Area.center.y);
            float edge = (block.Area.width - c.SidewalkWidth) * .5f;
            MakeProp("Crate", PrimitiveType.Cube, center + new Vector3(-edge,0,0), p.CrateSize, p.CrateMass, tuning);
            MakeProp("Barrel", PrimitiveType.Cylinder, center + new Vector3(-edge,0,c.SidewalkWidth), p.BarrelSize, p.BarrelMass, tuning);
        }
        Art.Populate(Plan); Stage("street/rooftop/feature props");
        BuildNavMesh(tuning, layout);
    }
    /// Runtime NavMesh, built district by district into ONE NavMeshData over the whole island: each step adds that district's
    /// sources and calls NavMeshBuilder.UpdateNavMeshData, which rebuilds only the tiles whose inputs changed (that district's
    /// tiles plus the seam tiles it shares). Measured first: separate NavMeshData instances per district do NOT stitch (a
    /// spawn->Park path ended at the Downtown/Park seam), and UpdateNavMeshData with a per-region bounds replaces the data
    /// with that region only. Regions are tile-aligned; sources are static colliders only (props/rigidbodies excluded).
    void BuildNavMesh(GameTuning tuning, CityLayout layout)
    {
        var perDistrict = new List<List<NavMeshBuildSource>>(); var markups = new List<NavMeshBuildMarkup>();
        foreach (var root in Art.Districts)
        {
            var list = new List<NavMeshBuildSource>();
            if (root != null) NavMeshBuilder.CollectSources(root.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, list);
            list.RemoveAll(s => s.component != null && s.component.GetComponentInParent<Rigidbody>() != null);
            perDistrict.Add(list);
        }
        var sources = new List<NavMeshBuildSource>();
        Stage("navmesh collect sources");
        var settings = NavMesh.GetSettingsByIndex(0); settings.agentRadius = tuning.Npcs.Radius; settings.agentHeight = tuning.Npcs.Height;
        settings.overrideVoxelSize = true; settings.voxelSize = layout.NavVoxelSize; settings.overrideTileSize = true; settings.tileSize = layout.NavTileVoxels;
        float top = Buildings.Count > 0 ? Buildings.Max(b => b.Position.y + b.Size.y * .5f) : 30f;
        float low = layout.SeabedLevel - 2f, high = Mathf.Max(top, 170f) + 10f;
        var all = Plan.Districts.Select(x => x.Region).Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
        var bounds = new Bounds(new Vector3(all.center.x, (low + high) * .5f, all.center.y), new Vector3(all.width, high - low, all.height));
        var data = new NavMeshData(settings.agentTypeID) { position = Vector3.zero, rotation = Quaternion.identity };
        for (int d = 0; d < Plan.Districts.Count; d++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (d < perDistrict.Count) sources.AddRange(perDistrict[d]);
            if (!NavMeshBuilder.UpdateNavMeshData(data, settings, sources, bounds)) throw new System.InvalidOperationException("City NavMesh build failed for district " + Plan.Districts[d].Name);
            NavMeshMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
            Stage("navmesh build: " + Plan.Districts[d].Name);
        }
        navmeshes.Add(NavMesh.AddNavMeshData(data));
        Stage("navmesh add (one data, all districts)");
    }
    void MakeProp(string name, PrimitiveType type, Vector3 ground, Vector3 size, float mass, GameTuning tuning)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(transform);
        go.transform.localScale = size; go.transform.position = ground + Vector3.up * (tuning.City.SidewalkHeight + size.y * (type == PrimitiveType.Cylinder ? 1f : .5f));
        go.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Wood); go.layer = Art.Rendering.PropLayer;
        var body = go.AddComponent<Rigidbody>(); body.mass = mass; body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<BreakableProp>().Configure(tuning.Props);
    }
    public Vector3 NearestSidewalk(Vector3 position)
    {
        Vector3 best = Sidewalks[0]; float distance = float.MaxValue;
        foreach (var point in Sidewalks) { float d = (point-position).sqrMagnitude; if (d < distance) { distance=d; best=point; } }
        return best;
    }
    /// A random sidewalk point within radius of position (falls back to the nearest one). Keeps civilian wandering local.
    public Vector3 RandomSidewalkNear(Vector3 position, float radius)
    {
        float r2 = radius * radius; int count = 0, pick = -1;
        for (int i = 0; i < Sidewalks.Count; i++)
            if ((Sidewalks[i] - position).sqrMagnitude <= r2 && Random.Range(0, ++count) == 0) pick = i;
        return pick >= 0 ? Sidewalks[pick] : NearestSidewalk(position);
    }
    /// Encounter placement (CityLayout.EncounterSites): the next district, round robin from nextDistrict, that has a free site
    /// within MaxDistance of the hero; inside it, the nearest site at least PreferredMinDistance away (else the farthest).
    /// Advances nextDistrict past the district used. Returns false when no site is free.
    public bool PickEncounterSite(Vector3 hero, System.Predicate<Vector3> free, ref int nextDistrict, out Vector3 site)
    {
        var rule = Layout.EncounterSites; int n = Plan.Districts.Count; site = default;
        if (nextDistrict < 0) nextDistrict = DistrictAt(Spawn);
        for (int k = 0; k < (rule.RotateDistricts ? n : 1); k++)
        {
            int d = (nextDistrict + k) % n; int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < EncounterSites.Count; i++)
            {
                if (rule.RotateDistricts && EncounterSiteDistrict[i] != d) continue;
                float distance = Vector3.Distance(hero, EncounterSites[i]);
                if (distance > rule.MaxDistance || !free(EncounterSites[i])) continue;
                // Preferred band first (nearest in it), then anything nearer (farthest first).
                float score = distance >= rule.PreferredMinDistance ? distance : 100000f - distance;
                if (score < bestScore || (score == bestScore && i < best)) { bestScore = score; best = i; }
            }
            if (best < 0) continue;
            site = EncounterSites[best]; nextDistrict = (d + 1) % n; return true;
        }
        return false;
    }
    /// Endless Fight arena: the encounter site of the spawn district nearest the spawn (an open street crossing or square).
    public Vector3 ArenaSite()
    {
        int d = DistrictAt(Spawn); Vector3 best = Spawn; float bestDistance = float.MaxValue;
        for (int i = 0; i < EncounterSites.Count; i++)
            if (EncounterSiteDistrict[i] == d) { float e = (EncounterSites[i] - Spawn).sqrMagnitude; if (e < bestDistance) { bestDistance = e; best = EncounterSites[i]; } }
        return best;
    }
    /// Index of the district whose region contains the point (nearest region otherwise).
    public int DistrictAt(Vector3 position) => Plan.DistrictAt(position);
    void OnDestroy() { foreach (var n in navmeshes) if (n.valid) n.Remove(); }
}
public sealed class RooftopDiscovery : MonoBehaviour
{
    public string Id;
    void Update()
    {
        var world = WorldSession.Instance; if (world == null) return;
        if (world.Progression.Data.Rooftops.Contains(Id)) { Destroy(gameObject); return; }
        if (Vector3.Distance(transform.position, world.Hero.transform.position) < world.Tuning.City.RooftopPickupRadius)
        { world.Progression.ClaimRoof(Id); Destroy(gameObject); }
    }
}
