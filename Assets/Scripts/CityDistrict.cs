using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class CityDistrict : MonoBehaviour
{
    public readonly List<Vector3> Sidewalks = new List<Vector3>();
    public readonly List<BuildingPlacement> Buildings = new List<BuildingPlacement>();
    public Vector3 Spawn { get; private set; }
    public CityArt Art {get;private set;}
    NavMeshDataInstance navmesh;
    public void Build(GameTuning tuning, CityLayout layout)
    {
        Art=gameObject.AddComponent<CityArt>();Art.Initialize(tuning);
        var c = tuning.City; var p = tuning.Props;
        float pitch = c.BlockSize + c.StreetWidth;
        float span = c.Blocks * pitch + c.StreetWidth;
        Box("Road foundation", new Vector3(0,-c.FloorThickness * .5f,0), new Vector3(span,c.FloorThickness,span), CityColor.Road);
        Spawn = new Vector3(-pitch * .5f, c.SidewalkHeight, -pitch);
        for (int x = 0; x < c.Blocks; x++) for (int z = 0; z < c.Blocks; z++)
        {
            Vector3 center = new Vector3((x-(c.Blocks-1)*.5f)*pitch,0,(z-(c.Blocks-1)*.5f)*pitch);
            Box("Block sidewalk", center + Vector3.up * c.SidewalkHeight * .5f, new Vector3(c.BlockSize,c.SidewalkHeight,c.BlockSize), CityColor.Pavement);
            foreach (Vector3 corner in new[] { new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1) })
                Sidewalks.Add(center + corner * (c.BlockSize-c.SidewalkWidth) * .5f + Vector3.up * c.SidewalkHeight);
            float edge = (c.BlockSize-c.SidewalkWidth) * .5f;
            MakeProp("Crate", PrimitiveType.Cube, center + new Vector3(-edge,0,0), p.CrateSize, p.CrateMass, tuning);
            MakeProp("Barrel", PrimitiveType.Cylinder, center + new Vector3(-edge,0,c.SidewalkWidth), p.BarrelSize, p.BarrelMass, tuning);
        }
        Buildings.AddRange(layout.Generate(c));
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
        Art.Streets();Art.FinishStaticGeometry();Art.Populate(Buildings);
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        sources.RemoveAll(s => s.component != null && s.component.GetComponent<Rigidbody>() != null);
        var settings = NavMesh.GetSettingsByIndex(0); settings.agentRadius = tuning.Npcs.Radius; settings.agentHeight = tuning.Npcs.Height;
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(span, c.LandmarkHeight*2, span)), Vector3.zero, Quaternion.identity);
        if (data == null) throw new System.InvalidOperationException("City NavMesh build failed");
        navmesh = NavMesh.AddNavMeshData(data);
    }
    void Box(string name, Vector3 position, Vector3 size, CityColor color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(transform);
        go.transform.position = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(color);
    }
    void MakeProp(string name, PrimitiveType type, Vector3 ground, Vector3 size, float mass, GameTuning tuning)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(transform);
        go.transform.localScale = size; go.transform.position = ground + Vector3.up * (tuning.City.SidewalkHeight + size.y * (type == PrimitiveType.Cylinder ? 1f : .5f));
        go.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Wood);
        var body = go.AddComponent<Rigidbody>(); body.mass = mass; body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<BreakableProp>().Configure(tuning.Props);
    }
    public Vector3 NearestSidewalk(Vector3 position)
    {
        Vector3 best = Sidewalks[0]; float distance = float.MaxValue;
        foreach (var point in Sidewalks) { float d = (point-position).sqrMagnitude; if (d < distance) { distance=d; best=point; } }
        return best;
    }
    void OnDestroy() { if (navmesh.valid) navmesh.Remove(); }
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
