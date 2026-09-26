using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable] public sealed class BuildingPlacement { public Vector3 Position, Size; public bool RooftopReward; public int District=-1, Style=-1; }

/// Generic ground/structure features a district can contain. Each kind is a geometry generator, not a district:
/// what a district looks like is entirely its data (which features, where, with which recipes/colours).
public enum FeatureKind
{
    Slab,    // raised ground slab (lawn, quay apron) over Area, top at Height
    Water,   // a hole in every land/ground slab over Area: canal, basin, pond (the shared sea plane shows through)
    Deck,    // walkable deck over water at Height (bridge, pier), railings along the long sides, optional arches below
    Path,    // pavement strip of Width along Points (polyline), street props every Spacing from Props
    Scatter  // seeded static recipes (or props) inside Area with minimum Spacing; Rows packs a grid; Stack stacks recipes
}
[Serializable] public sealed class FeatureDefinition
{
    public string Name;
    public FeatureKind Kind;
    [Tooltip("World rect (x, z, width, depth).")] public Rect Area;
    public float Height;
    public CityColor Color=CityColor.Pavement;
    [Tooltip("Scatter: CityArtSettings structure recipes, picked by seed. Scatter/Path: street prop kinds when Props is set.")]
    public string[] Recipes=new string[0];
    public CityPropKind[] Props=new CityPropKind[0];
    public int Count;
    public float Spacing=8, Width=4, Railing=1.1f;
    [Tooltip("Scatter: 1..N stacked copies (y range). Deck: arch count under the span.")] public Vector2Int Stack=new Vector2Int(1,1);
    public bool Rows;
    [Tooltip("Scatter: yaw choices in degrees (seeded); Path/Deck ignore.")] public float[] Yaws=new float[]{0};
    [Tooltip("Path polyline or explicit Scatter positions (world x, z).")] public Vector2[] Points=new Vector2[0];
}
[Serializable] public sealed class DistrictDefinition
{
    public string Name;
    [Tooltip("Ownership + NavMesh region (x, z, width, depth). Regions partition the island on the NavMesh tile grid.")] public Rect Region;
    [Tooltip("Street-grid area. The strip between Region and Area is boulevard/promenade road.")] public Rect Area;
    [Header("Street grid (BlockSize.x <= 0: open district, features only)")]
    public Vector2 BlockSize=new Vector2(34,40);
    public float StreetWidth=14;
    public int LotsPerSide=2;
    [Tooltip("Building footprint as a fraction of its lot (seeded within range).")] public Vector2 LotFill=new Vector2(.75f,.9f);
    [Range(0,1)] public float Occupancy=1;
    public Vector2 Height=new Vector2(18,50);
    [Range(0,1)] public float TallChance;
    public Vector2 TallHeight=new Vector2(60,80);
    [Tooltip("Skyline shaping: buildings within CoreRadius of Core get up to (1+CoreBoost) x taller.")] public Vector2 Core;
    public float CoreRadius, CoreBoost;
    [Range(0,1), Tooltip("Chance an internal intersection becomes a plaza (the four corner lots around it stay open).")] public float PlazaChance;
    [Tooltip("Indices into CityArtSettings.Styles.")] public int[] Styles={0,1,2,3};
    public bool StreetProps=true, RooftopProps=true, BlockProps=true;
    [Tooltip("Optional static recipe planted along block edges (inside the sidewalk) every SidewalkRecipeSpacing m.")] public string SidewalkRecipe="";
    public float SidewalkRecipeSpacing=12;
    public int RooftopPickups;
    [Tooltip("Encounter/sidewalk sites for open areas (world x, z). Grid districts add their internal intersections.")] public Vector2[] Sites=new Vector2[0];
    public List<FeatureDefinition> Features=new List<FeatureDefinition>();
}
[Serializable] public sealed class BoulevardDefinition
{
    public string Name; public Rect Area; public float Median=3; public string MedianRecipe="Street tree"; public float RecipeSpacing=14;
}
[Serializable] public sealed class LandmarkDefinition
{
    public string Name, Recipe; public Vector2 Position; public float ClearRadius=16; public bool SnapToBlock;
}
[Serializable] public sealed class BackdropSettings
{
    public int Count=64; public Vector2 Radius=new Vector2(820,1050), Height=new Vector2(30,140), Width=new Vector2(40,110);
    public CityColor Color=CityColor.Slate, ShoreColor=CityColor.Roof; public float SeaMargin=2600;
    [Tooltip("Haze skirt: a square of fog-coloured walls this far from the island centre, taller than any camera, so an elevated view never sees the sky's ground half below the sea horizon.")]
    public float SkirtRadius=1500, SkirtHeight=600; public CityColor SkirtColor=CityColor.Haze;
}
[Serializable] public sealed class NpcLodSettings
{
    [Tooltip("Within this distance of the hero an NPC runs full AI + Animator + presentation.")] public float NearRadius=60;
    public float Hysteresis=8;
    [Tooltip("Far NPCs: AI tick interval (s), animator manual update interval (s).")] public float FarThinkInterval=.25f, FarAnimatorInterval=.2f;
    [Tooltip("Civilians farther than this (and unseen) are moved to a sidewalk RecycleDistance from the hero.")] public float RecycleRadius=150;
    public Vector2 RecycleDistance=new Vector2(45,110);
    public float RecyclePerSecond=4, WanderRadius=70;
    public bool Enabled=true;
}
[Serializable] public sealed class EncounterSiteRule
{
    [Tooltip("Spread set-pieces across districts: each new one goes to the next district (round robin) that has a free site.")] public bool RotateDistricts=true;
    [Tooltip("Within the chosen district, prefer sites at least this far from the hero (travel purpose), else the farthest available.")] public float PreferredMinDistance=45;
    public float MaxDistance=320;
}

/// Everything the generator decided: pure data, deterministic for a seed (CityArt turns it into geometry).
public sealed class CityPlan
{
    public sealed class Block { public int District; public Rect Area; }
    public sealed class Slab { public int District; public Rect Area; public float Top, Bottom; public CityColor Color; public bool Detail; }
    public sealed class Deck { public int District; public Rect Area; public float Top, Railing; public int Arches; public CityColor Color; }
    public sealed class Structure { public int District; public string Recipe; public Vector3 Position; public float Yaw; public int Variant; }
    public sealed class Line { public int District; public Vector3 From, To; }
    public sealed class Crossing { public int District; public Vector3 Center; public float Street, Pitch; }
    public readonly List<BuildingPlacement> Buildings=new List<BuildingPlacement>();
    public readonly List<Block> Blocks=new List<Block>();
    public readonly List<Slab> Slabs=new List<Slab>();
    public readonly List<Deck> Decks=new List<Deck>();
    public readonly List<Structure> Structures=new List<Structure>();
    public readonly List<Line> Dashes=new List<Line>();
    public readonly List<Crossing> Crossings=new List<Crossing>();
    public readonly List<Rect> Water=new List<Rect>();
    public readonly List<Vector3> Sidewalks=new List<Vector3>(), Sites=new List<Vector3>();
    public readonly List<int> SidewalkDistrict=new List<int>(), SiteDistrict=new List<int>();
    public readonly List<ArtPlacement> FeatureProps=new List<ArtPlacement>();
    public readonly List<string> Landmarks=new List<string>();
    public List<DistrictDefinition> Districts;
    public Rect Island;
    public Vector3 Spawn;
    public float WaterLevel, Seabed;
    public int DistrictAt(Vector3 p){for(int i=0;i<Districts.Count;i++)if(Districts[i].Region.Contains(new Vector2(p.x,p.z)))return i;
        int best=0;float d=float.MaxValue;for(int i=0;i<Districts.Count;i++){float e=(Districts[i].Region.center-new Vector2(p.x,p.z)).sqrMagnitude;if(e<d){d=e;best=i;}}return best;}
}

[CreateAssetMenu(menuName = "Overpowered/City Layout")]
public sealed class CityLayout : ScriptableObject
{
    public bool UseAuthoredBuildings;
    public List<BuildingPlacement> Buildings = new List<BuildingPlacement>();
    [Header("World (districts). CityLayout is the layout authority; CityArtSettings owns styles, recipes and rendering.")]
    [Tooltip("Island land rects (x, z, width, depth); Water features cut holes, the sea surrounds it.")] public List<Rect> Land=new List<Rect>{new Rect(-288,-208,576,368)};
    public List<DistrictDefinition> Districts=DefaultDistricts();
    public List<BoulevardDefinition> Boulevards=new List<BoulevardDefinition>{
        new BoulevardDefinition{Name="West Boulevard",Area=new Rect(-108,-108,24,268)},
        new BoulevardDefinition{Name="East Boulevard",Area=new Rect(84,-108,24,268)},
        new BoulevardDefinition{Name="Harbour Boulevard",Area=new Rect(-288,-108,576,24)}};
    public List<LandmarkDefinition> Landmarks=new List<LandmarkDefinition>{
        new LandmarkDefinition{Name="Meridian Spire",Recipe="Meridian Spire",Position=new Vector2(0,40),ClearRadius=4,SnapToBlock=true},
        new LandmarkDefinition{Name="Harbour Light",Recipe="Harbour Light",Position=new Vector2(-14,-258),ClearRadius=10},
        new LandmarkDefinition{Name="Park Lookout Tower",Recipe="Lookout Tower",Position=new Vector2(-196,104),ClearRadius=16}};
    public int SpawnDistrict;
    public float WaterLevel=-.6f, SeabedLevel=-1.3f, LandBottom=-1.3f, BoundaryMargin=90, BoundaryHeight=400;
    public BackdropSettings Backdrop=new BackdropSettings();
    [Header("Runtime NavMesh: one NavMeshData per district region, all on one tile grid so neighbouring regions stitch.")]
    public float NavVoxelSize=1f/6f; public int NavTileVoxels=192;
    public NpcLodSettings NpcLod=new NpcLodSettings();
    public EncounterSiteRule EncounterSites=new EncounterSiteRule();

    /// Building placements only (rooftop-reward buildings first). Kept for the menu skyline and existing callers.
    public List<BuildingPlacement> Generate(CitySettings config) => UseAuthoredBuildings ? new List<BuildingPlacement>(Buildings) : Plan(config).Buildings;

    public CityPlan Plan(CitySettings config)
    {
        var plan=new CityPlan{Districts=Districts,WaterLevel=WaterLevel,Seabed=SeabedLevel};
        plan.Island=Land.Aggregate((a,b)=>Rect.MinMaxRect(Mathf.Min(a.xMin,b.xMin),Mathf.Min(a.yMin,b.yMin),Mathf.Max(a.xMax,b.xMax),Mathf.Max(a.yMax,b.yMax)));
        foreach(var d in Districts)foreach(var f in d.Features)if(f.Kind==FeatureKind.Water)plan.Water.Add(f.Area);
        foreach(var land in Land)Slab(plan,land,0,LandBottom,CityColor.Road,false);
        var buildings=new List<BuildingPlacement>();
        var clear=new List<(Vector2 At,float Radius)>();
        var landmarkAt=new Dictionary<LandmarkDefinition,Vector2>();
        for(int d=0;d<Districts.Count;d++)
        {
            var district=Districts[d];var rng=new System.Random(unchecked(config.Seed*7919+d*104729+17));
            if(district.BlockSize.x>0)Grid(plan,d,district,config,rng,buildings,landmarkAt);
            foreach(var f in district.Features)Feature(plan,d,f,config,rng,clear);
            foreach(var s in district.Sites){var p=new Vector3(s.x,config.SidewalkHeight,s.y);plan.Sites.Add(p);plan.SiteDistrict.Add(d);plan.Sidewalks.Add(p);plan.SidewalkDistrict.Add(d);}
        }
        // Landmarks: authored positions (optionally snapped to the nearest block, which becomes its plaza).
        foreach(var l in Landmarks)
        {
            Vector2 at=landmarkAt.TryGetValue(l,out var snapped)?snapped:l.Position;
            plan.Structures.Add(new CityPlan.Structure{District=plan.DistrictAt(new Vector3(at.x,0,at.y)),Recipe=l.Recipe,Position=new Vector3(at.x,0,at.y)});
            plan.Landmarks.Add(l.Name);
        }
        Boulevard(plan,config);
        ScatterAll(plan,config,clear,landmarkAt);
        // Rooftop pickups: per district, the tallest candidates chosen by seed; reward buildings go first (index 0..N-1),
        // so the Rooftops save ids ("seed:roof:index") keep their meaning of "the first N buildings".
        var rewards=new List<BuildingPlacement>();
        for(int d=0;d<Districts.Count;d++)
        {
            var rng=new System.Random(unchecked(config.Seed*31+d*977));
            var pool=buildings.Where(b=>b.District==d).OrderByDescending(b=>b.Size.y).Take(Mathf.Max(Districts[d].RooftopPickups*3,Districts[d].RooftopPickups)).ToList();
            for(int i=0;i<Districts[d].RooftopPickups&&pool.Count>0;i++){var pick=pool[rng.Next(pool.Count)];pool.Remove(pick);pick.RooftopReward=true;rewards.Add(pick);}
        }
        plan.Buildings.AddRange(rewards);plan.Buildings.AddRange(buildings.Where(b=>!b.RooftopReward));
        // Spawn: mid-block on the street beside the internal intersection nearest the spawn district's centre.
        var spawnDistrict=Districts[Mathf.Clamp(SpawnDistrict,0,Districts.Count-1)];
        var near=plan.Crossings.Where(c=>c.District==SpawnDistrict).OrderBy(c=>(new Vector2(c.Center.x,c.Center.z)-spawnDistrict.Area.center).sqrMagnitude).FirstOrDefault();
        plan.Spawn=near!=null?new Vector3(near.Center.x,config.SidewalkHeight,near.Center.z-near.Pitch*.5f)
            :new Vector3(spawnDistrict.Area.center.x,config.SidewalkHeight,spawnDistrict.Area.center.y);
        // Sidewalks ordered by distance from spawn: early civilians/crimes start where the player does.
        var order=Enumerable.Range(0,plan.Sidewalks.Count).OrderBy(i=>(plan.Sidewalks[i]-plan.Spawn).sqrMagnitude).ThenBy(i=>i).ToList();
        var walks=order.Select(i=>plan.Sidewalks[i]).ToList();var owners=order.Select(i=>plan.SidewalkDistrict[i]).ToList();
        plan.Sidewalks.Clear();plan.Sidewalks.AddRange(walks);plan.SidewalkDistrict.Clear();plan.SidewalkDistrict.AddRange(owners);
        return plan;
    }

    void Grid(CityPlan plan,int d,DistrictDefinition district,CitySettings config,System.Random rng,List<BuildingPlacement> buildings,Dictionary<LandmarkDefinition,Vector2> landmarkAt)
    {
        var a=district.Area;float street=district.StreetWidth;
        float block=Mathf.Round(Mathf.Lerp(district.BlockSize.x,district.BlockSize.y,(float)rng.NextDouble()));
        int nx=Mathf.Max(1,Mathf.FloorToInt((a.width+street)/(block+street))),nz=Mathf.Max(1,Mathf.FloorToInt((a.height+street)/(block+street)));
        float x0=a.xMin+(a.width-(nx*block+(nx-1)*street))*.5f,z0=a.yMin+(a.height-(nz*block+(nz-1)*street))*.5f,pitch=block+street;
        Rect BlockRect(int i,int j)=>new Rect(x0+i*pitch,z0+j*pitch,block,block);
        bool Wet(Rect r)=>plan.Water.Any(w=>w.Overlaps(r));
        // Landmarks that snap to a block claim the nearest dry block of this district as a plaza.
        var plazaBlocks=new HashSet<(int,int)>();
        foreach(var l in Landmarks)if(l.SnapToBlock&&district.Region.Contains(l.Position))
        {
            var best=(-1,-1);float bd=float.MaxValue;
            for(int i=0;i<nx;i++)for(int j=0;j<nz;j++){var r=BlockRect(i,j);if(Wet(r))continue;float e=(r.center-l.Position).sqrMagnitude;if(e<bd){bd=e;best=(i,j);}}
            if(best.Item1>=0){plazaBlocks.Add(best);landmarkAt[l]=BlockRect(best.Item1,best.Item2).center;}
        }
        // Intersection plazas (seeded): the four corner lots touching the intersection stay open.
        var openCorners=new HashSet<(int,int,int,int)>();
        for(int i=0;i<nx-1;i++)for(int j=0;j<nz-1;j++)
        {
            var c=new Vector3(x0+(i+1)*block+i*street+street*.5f,.025f,z0+(j+1)*block+j*street+street*.5f);
            if(Wet(new Rect(c.x-street*.5f,c.z-street*.5f,street,street)))continue;
            plan.Crossings.Add(new CityPlan.Crossing{District=d,Center=c,Street=street,Pitch=pitch});
            plan.Sites.Add(new Vector3(c.x,0,c.z));plan.SiteDistrict.Add(d);
            if(rng.NextDouble()<district.PlazaChance){int L=district.LotsPerSide-1;openCorners.Add((i,j,L,L));openCorners.Add((i+1,j,0,L));openCorners.Add((i,j+1,L,0));openCorners.Add((i+1,j+1,0,0));}
        }
        float walk=config.SidewalkWidth;
        for(int i=0;i<nx;i++)for(int j=0;j<nz;j++)
        {
            var r=BlockRect(i,j);if(Wet(r))continue;
            plan.Blocks.Add(new CityPlan.Block{District=d,Area=r});
            Slab(plan,r,config.SidewalkHeight,0,CityColor.Pavement,false,d);
            var center=new Vector3(r.center.x,0,r.center.y);
            foreach(var corner in new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)})
            {plan.Sidewalks.Add(center+corner*(block-walk)*.5f+Vector3.up*config.SidewalkHeight);plan.SidewalkDistrict.Add(d);}
            // Lane dashes along the street east and north of this block (inside the grid only).
            if(i<nx-1)plan.Dashes.Add(new CityPlan.Line{District=d,From=new Vector3(r.xMax+street*.5f,.012f,r.yMin+2),To=new Vector3(r.xMax+street*.5f,.012f,r.yMax-2)});
            if(j<nz-1)plan.Dashes.Add(new CityPlan.Line{District=d,From=new Vector3(r.xMin+2,.012f,r.yMax+street*.5f),To=new Vector3(r.xMax-2,.012f,r.yMax+street*.5f)});
            if(!string.IsNullOrEmpty(district.SidewalkRecipe))
                foreach(var (from,dir,inward) in new[]{(new Vector2(r.xMin,r.yMin),Vector2.right,Vector2.up),(new Vector2(r.xMin,r.yMax),Vector2.right,Vector2.down),
                    (new Vector2(r.xMin,r.yMin),Vector2.up,Vector2.right),(new Vector2(r.xMax,r.yMin),Vector2.up,Vector2.left)})
                    for(float t=district.SidewalkRecipeSpacing;t<=block-district.SidewalkRecipeSpacing+.01f;t+=district.SidewalkRecipeSpacing)
                    {var at=from+dir*t+inward*1.2f;plan.Structures.Add(new CityPlan.Structure{District=d,Recipe=district.SidewalkRecipe,Position=new Vector3(at.x,config.SidewalkHeight,at.y),Variant=Mathf.RoundToInt(t)});}
            if(plazaBlocks.Contains((i,j)))continue;
            int lots=Mathf.Max(1,district.LotsPerSide);float inner=block-2*walk,lot=inner/lots;
            for(int u=0;u<lots;u++)for(int v=0;v<lots;v++)
            {
                double occupied=rng.NextDouble(),fillX=rng.NextDouble(),fillZ=rng.NextDouble(),h=rng.NextDouble(),tall=rng.NextDouble(),style=rng.NextDouble();
                if(openCorners.Contains((i,j,u,v))||occupied>district.Occupancy)continue;
                var lc=new Vector2(r.xMin+walk+(u+.5f)*lot,r.yMin+walk+(v+.5f)*lot);
                float w=lot*Mathf.Lerp(district.LotFill.x,district.LotFill.y,(float)fillX),dep=lot*Mathf.Lerp(district.LotFill.x,district.LotFill.y,(float)fillZ);
                float height=tall<district.TallChance?Mathf.Lerp(district.TallHeight.x,district.TallHeight.y,(float)h):Mathf.Lerp(district.Height.x,district.Height.y,(float)h);
                if(district.CoreRadius>0)height*=1+district.CoreBoost*Mathf.Clamp01(1-(lc-district.Core).magnitude/district.CoreRadius);
                height=Mathf.Round(height*10)/10;
                buildings.Add(new BuildingPlacement{Position=new Vector3(lc.x,height*.5f+config.SidewalkHeight,lc.y),Size=new Vector3(w,height,dep),District=d,
                    Style=district.Styles.Length==0?-1:district.Styles[Mathf.Min(district.Styles.Length-1,(int)(style*district.Styles.Length))]});
            }
        }
    }

    void Feature(CityPlan plan,int d,FeatureDefinition f,CitySettings config,System.Random rng,List<(Vector2,float)> clear)
    {
        switch(f.Kind)
        {
            case FeatureKind.Slab: Slab(plan,f.Area,f.Height,0,f.Color,false,d);break;
            case FeatureKind.Water: break; // collected up front; every slab subtracts it
            case FeatureKind.Deck: plan.Decks.Add(new CityPlan.Deck{District=d,Area=f.Area,Top=f.Height,Railing=f.Railing,Arches=f.Stack.y,Color=f.Color});
                var mid=new Vector3(f.Area.center.x,f.Height,f.Area.center.y);plan.Sidewalks.Add(mid);plan.SidewalkDistrict.Add(d);break;
            case FeatureKind.Path:
                for(int i=0;i+1<f.Points.Length;i++)
                {
                    Vector2 p=f.Points[i],q=f.Points[i+1];var dir=(q-p).normalized;var side=new Vector2(-dir.y,dir.x);
                    var r=Rect.MinMaxRect(Mathf.Min(p.x,q.x)-f.Width*.5f,Mathf.Min(p.y,q.y)-f.Width*.5f,Mathf.Max(p.x,q.x)+f.Width*.5f,Mathf.Max(p.y,q.y)+f.Width*.5f);
                    Slab(plan,r,f.Height,0,f.Color,false,d);clear.Add((p,f.Width*.5f+3));
                    float len=(q-p).magnitude;
                    for(float t=f.Spacing*.5f;t<len;t+=f.Spacing)
                    {
                        var at=p+dir*t;clear.Add((at,f.Width*.5f+3));
                        plan.Sidewalks.Add(new Vector3(at.x,f.Height,at.y));plan.SidewalkDistrict.Add(d);
                        if(f.Props.Length>0)
                        {
                            int k=(int)(t/f.Spacing)%f.Props.Length;float s=((int)(t/f.Spacing)%2==0?1:-1);
                            var pos=at+side*s*(f.Width*.5f+.9f);
                            plan.FeatureProps.Add(new ArtPlacement{Kind=f.Props[k],Position=new Vector3(pos.x,f.Height,pos.y),Yaw=Mathf.Atan2(side.x*s,side.y*s)*Mathf.Rad2Deg});
                        }
                    }
                }
                break;
            case FeatureKind.Scatter: pendingScatter.Add((d,f)); break;
        }
    }
    readonly List<(int District,FeatureDefinition Feature)> pendingScatter=new List<(int,FeatureDefinition)>();

    /// Scatter runs after paths/landmarks/sites exist so it can keep clear of them.
    void ScatterAll(CityPlan plan,CitySettings config,List<(Vector2 At,float Radius)> clear,Dictionary<LandmarkDefinition,Vector2> landmarkAt)
    {
        foreach(var l in Landmarks)clear.Add((landmarkAt.TryGetValue(l,out var at)?at:l.Position,l.ClearRadius));
        foreach(var s in plan.Sites)clear.Add((new Vector2(s.x,s.z),11));
        var scatter=pendingScatter.ToList();pendingScatter.Clear();
        foreach(var (d,f) in scatter)
        {
            var rng=new System.Random(unchecked(config.Seed*4099+d*131+(f.Name??"").GetHashCode()));
            var placed=new List<Vector2>();
            bool Free(Vector2 p,float margin)=>!plan.Water.Any(w=>Expand(w,margin).Contains(p))&&!clear.Any(c=>(c.At-p).sqrMagnitude<(c.Radius+margin)*(c.Radius+margin))&&!placed.Any(q=>(q-p).sqrMagnitude<f.Spacing*f.Spacing)
                &&!plan.Decks.Any(k=>Expand(k.Area,margin).Contains(p));
            IEnumerable<Vector2> candidates;
            if(f.Points.Length>0)candidates=f.Points;
            else if(f.Rows)
            {
                var list=new List<Vector2>();
                for(float z=f.Area.yMin+f.Spacing*.5f;z<f.Area.yMax;z+=f.Spacing*1.6f)for(float x=f.Area.xMin+f.Spacing*.5f;x<f.Area.xMax;x+=f.Spacing)list.Add(new Vector2(x,z));
                candidates=list;
            }
            else candidates=Enumerable.Range(0,Mathf.Max(1,f.Count)*12).Select(_=>new Vector2(Mathf.Lerp(f.Area.xMin,f.Area.xMax,(float)rng.NextDouble()),Mathf.Lerp(f.Area.yMin,f.Area.yMax,(float)rng.NextDouble()))).ToList();
            int made=0;
            foreach(var p in candidates)
            {
                if(f.Count>0&&made>=f.Count)break;
                if(f.Points.Length==0&&!Free(p,1.5f))continue;
                if(f.Rows&&rng.NextDouble()>.78)continue; // gaps between container stacks read as a working yard
                placed.Add(p);made++;
                float yaw=f.Yaws.Length==0?0:f.Yaws[rng.Next(f.Yaws.Length)];
                if(f.Props.Length>0){plan.FeatureProps.Add(new ArtPlacement{Kind=f.Props[rng.Next(f.Props.Length)],Position=new Vector3(p.x,f.Height,p.y),Yaw=yaw});continue;}
                int stack=rng.Next(f.Stack.x,f.Stack.y+1);
                for(int level=0;level<stack;level++)
                    plan.Structures.Add(new CityPlan.Structure{District=d,Recipe=f.Recipes[rng.Next(f.Recipes.Length)],Position=new Vector3(p.x,f.Height+level*StackHeight,p.y),Yaw=yaw,Variant=rng.Next(1<<16)});
            }
            // Later scatters (and nothing else) keep clear of what this one placed.
            foreach(var q in placed)clear.Add((q,f.Spacing*.5f));
        }
    }
    /// Stacked scatter recipes (containers) are this tall per level.
    public float StackHeight=2.6f;

    void Boulevard(CityPlan plan,CitySettings config)
    {
        foreach(var b in Boulevards)
        {
            bool alongX=b.Area.width>b.Area.height;
            var median=alongX?new Rect(b.Area.xMin,b.Area.center.y-b.Median*.5f,b.Area.width,b.Median):new Rect(b.Area.center.x-b.Median*.5f,b.Area.yMin,b.Median,b.Area.height);
            // A median stops short of every crossing boulevard and all water, leaving the junctions open.
            var pieces=new List<Rect>{median};
            foreach(var other in Boulevards)if(other!=b)pieces=pieces.SelectMany(p=>Subtract(p,Expand(other.Area,4))).ToList();
            foreach(var w in plan.Water)pieces=pieces.SelectMany(p=>Subtract(p,Expand(w,4))).ToList();
            foreach(var p in pieces)
            {
                if(p.width<2||p.height<2)continue;
                int d=plan.DistrictAt(new Vector3(p.center.x,0,p.center.y));
                plan.Slabs.Add(new CityPlan.Slab{District=d,Area=p,Top=.3f,Bottom=0,Color=CityColor.Lawn});
                float len=alongX?p.width:p.height;
                for(float t=b.RecipeSpacing*.5f;t<len;t+=b.RecipeSpacing)
                {
                    var at=alongX?new Vector3(p.xMin+t,.3f,p.center.y):new Vector3(p.center.x,.3f,p.yMin+t);
                    if(!string.IsNullOrEmpty(b.MedianRecipe))plan.Structures.Add(new CityPlan.Structure{District=plan.DistrictAt(at),Recipe=b.MedianRecipe,Position=at,Variant=Mathf.RoundToInt(t)});
                }
                foreach(float side in new[]{-1f,1f})
                {
                    float offset=b.Median*.5f+(alongX?b.Area.height:b.Area.width)*.25f;
                    var from=alongX?new Vector3(p.xMin+2,.012f,p.center.y+side*offset):new Vector3(p.center.x+side*offset,.012f,p.yMin+2);
                    var to=alongX?new Vector3(p.xMax-2,.012f,p.center.y+side*offset):new Vector3(p.center.x+side*offset,.012f,p.yMax-2);
                    plan.Dashes.Add(new CityPlan.Line{District=plan.DistrictAt((from+to)*.5f),From=from,To=to});
                }
            }
        }
    }

    /// Adds a ground slab, cut around every water rect and split along district regions (per-district batching/NavMesh).
    void Slab(CityPlan plan,Rect area,float top,float bottom,CityColor color,bool detail,int district=-1)
    {
        var pieces=new List<Rect>{area};
        foreach(var w in plan.Water)pieces=pieces.SelectMany(p=>Subtract(p,w)).ToList();
        foreach(var p in pieces)
        {
            if(p.width<.05f||p.height<.05f)continue;
            if(district>=0){plan.Slabs.Add(new CityPlan.Slab{District=district,Area=p,Top=top,Bottom=bottom,Color=color,Detail=detail});continue;}
            for(int d=0;d<Districts.Count;d++)
            {
                var r=Districts[d].Region;var cut=Rect.MinMaxRect(Mathf.Max(p.xMin,r.xMin),Mathf.Max(p.yMin,r.yMin),Mathf.Min(p.xMax,r.xMax),Mathf.Min(p.yMax,r.yMax));
                if(cut.width>.05f&&cut.height>.05f)plan.Slabs.Add(new CityPlan.Slab{District=d,Area=cut,Top=top,Bottom=bottom,Color=color,Detail=detail});
            }
        }
    }
    public static Rect Expand(Rect r,float m)=>Rect.MinMaxRect(r.xMin-m,r.yMin-m,r.xMax+m,r.yMax+m);
    /// a minus b as up to four rects.
    public static IEnumerable<Rect> Subtract(Rect a,Rect b)
    {
        if(!a.Overlaps(b)){yield return a;yield break;}
        if(b.yMin>a.yMin)yield return Rect.MinMaxRect(a.xMin,a.yMin,a.xMax,b.yMin);
        if(b.yMax<a.yMax)yield return Rect.MinMaxRect(a.xMin,b.yMax,a.xMax,a.yMax);
        float y0=Mathf.Max(a.yMin,b.yMin),y1=Mathf.Min(a.yMax,b.yMax);
        if(b.xMin>a.xMin)yield return Rect.MinMaxRect(a.xMin,y0,b.xMin,y1);
        if(b.xMax<a.xMax)yield return Rect.MinMaxRect(b.xMax,y0,a.xMax,y1);
    }

    static List<DistrictDefinition> DefaultDistricts()=>new List<DistrictDefinition>
    {
        new DistrictDefinition{Name="Downtown",Region=new Rect(-96,-96,192,256),Area=new Rect(-84,-84,168,232),
            BlockSize=new Vector2(36,42),StreetWidth=14,LotsPerSide=2,LotFill=new Vector2(.8f,.94f),Occupancy=1,Height=new Vector2(20,46),TallChance=.28f,TallHeight=new Vector2(56,84),
            Core=new Vector2(0,40),CoreRadius=120,CoreBoost=.45f,PlazaChance=.3f,Styles=new[]{2,3,4,0},RooftopPickups=3},
        new DistrictDefinition{Name="Park",Region=new Rect(-288,-96,192,256),Area=new Rect(-276,-84,168,232),BlockSize=Vector2.zero,
            Sites=new[]{new Vector2(-250,-40),new Vector2(-150,120),new Vector2(-180,60),new Vector2(-128,-40)},
            Features=new List<FeatureDefinition>{
                new FeatureDefinition{Name="Park lawn",Kind=FeatureKind.Slab,Area=new Rect(-276,-84,168,232),Height=.14f,Color=CityColor.Lawn},
                new FeatureDefinition{Name="Pond",Kind=FeatureKind.Water,Area=new Rect(-236,6,52,34)},
                new FeatureDefinition{Name="Pond footbridge",Kind=FeatureKind.Deck,Area=new Rect(-213,2,6,42),Height=.3f,Color=CityColor.Wood,Railing=1f,Stack=new Vector2Int(1,1)},
                new FeatureDefinition{Name="Loop path",Kind=FeatureKind.Path,Height=.16f,Width=5,Spacing=16,Color=CityColor.Sand,
                    Points=new[]{new Vector2(-108,-40),new Vector2(-250,-40),new Vector2(-250,120),new Vector2(-150,120),new Vector2(-150,60),new Vector2(-108,60)},
                    Props=new[]{CityPropKind.Bench,CityPropKind.Lamp,CityPropKind.Trash,CityPropKind.Lamp}},
                new FeatureDefinition{Name="South gate path",Kind=FeatureKind.Path,Height=.16f,Width=4,Spacing=18,Color=CityColor.Sand,
                    Points=new[]{new Vector2(-192,-84),new Vector2(-192,-40)},Props=new[]{CityPropKind.Lamp}},
                new FeatureDefinition{Name="Pond path south",Kind=FeatureKind.Path,Height=.16f,Width=4,Spacing=18,Color=CityColor.Sand,
                    Points=new[]{new Vector2(-210,-40),new Vector2(-210,4)},Props=new[]{CityPropKind.Bench}},
                new FeatureDefinition{Name="Pond path north",Kind=FeatureKind.Path,Height=.16f,Width=4,Spacing=18,Color=CityColor.Sand,
                    Points=new[]{new Vector2(-210,42),new Vector2(-210,60),new Vector2(-150,60)},Props=new[]{CityPropKind.Bench,CityPropKind.Lamp}},
                new FeatureDefinition{Name="Tower path",Kind=FeatureKind.Path,Height=.16f,Width=4,Spacing=18,Color=CityColor.Sand,
                    Points=new[]{new Vector2(-196,60),new Vector2(-196,120)},Props=new[]{CityPropKind.Lamp}},
                new FeatureDefinition{Name="Park trees",Kind=FeatureKind.Scatter,Area=new Rect(-274,-82,164,228),Height=.14f,Count=130,Spacing=8,Recipes=new[]{"Park tree","Park tree","Pine tree"},Yaws=new[]{0f,30,60,90}}}},
        new DistrictDefinition{Name="Residential",Region=new Rect(96,-96,192,256),Area=new Rect(148,-84,128,232),
            BlockSize=new Vector2(40,46),StreetWidth=12,LotsPerSide=2,LotFill=new Vector2(.5f,.72f),Occupancy=.82f,Height=new Vector2(6,13),TallChance=.1f,TallHeight=new Vector2(16,22),
            PlazaChance=.15f,Styles=new[]{6,1,0},RooftopPickups=1,SidewalkRecipe="Street tree",SidewalkRecipeSpacing=11,
            Features=new List<FeatureDefinition>{
                new FeatureDefinition{Name="Canal",Kind=FeatureKind.Water,Area=new Rect(120,-208,16,368)},
                new FeatureDefinition{Name="Grand Canal Bridge",Kind=FeatureKind.Deck,Area=new Rect(112,20,32,24),Height=0,Color=CityColor.Cream,Railing=1.2f,Stack=new Vector2Int(1,2)},
                new FeatureDefinition{Name="North footbridge",Kind=FeatureKind.Deck,Area=new Rect(114,112,28,10),Height=0,Color=CityColor.Wood,Railing=1.1f,Stack=new Vector2Int(1,1)}}},
        new DistrictDefinition{Name="Docks",Region=new Rect(-288,-288,576,192),Area=new Rect(-270,-160,540,50),
            BlockSize=new Vector2(40,48),StreetWidth=18,LotsPerSide=1,LotFill=new Vector2(.62f,.82f),Occupancy=.8f,Height=new Vector2(7,12),TallChance=.12f,TallHeight=new Vector2(14,18),
            Styles=new[]{5,5,0},StreetProps=false,RooftopPickups=1,
            Sites=new[]{new Vector2(-200,-184),new Vector2(-40,-186),new Vector2(80,-186),new Vector2(212,-150)},
            Features=new List<FeatureDefinition>{
                new FeatureDefinition{Name="Quay apron",Kind=FeatureKind.Slab,Area=new Rect(-288,-208,576,44),Height=.08f,Color=CityColor.Pavement},
                new FeatureDefinition{Name="East basin",Kind=FeatureKind.Water,Area=new Rect(176,-208,64,30)},
                new FeatureDefinition{Name="Harbour Boulevard bridge",Kind=FeatureKind.Deck,Area=new Rect(112,-108,32,24),Height=0,Color=CityColor.Cream,Railing=1.2f,Stack=new Vector2Int(1,1)},
                new FeatureDefinition{Name="Quay bridge",Kind=FeatureKind.Deck,Area=new Rect(112,-200,32,18),Height=.08f,Color=CityColor.Slate,Railing=1.1f,Stack=new Vector2Int(1,1)},
                new FeatureDefinition{Name="Lighthouse pier",Kind=FeatureKind.Deck,Area=new Rect(-20,-250,12,44),Height=.08f,Color=CityColor.Wood,Railing=1f},
                new FeatureDefinition{Name="Lighthouse platform",Kind=FeatureKind.Deck,Area=new Rect(-28,-272,28,24),Height=.08f,Color=CityColor.Slate,Railing=1f},
                new FeatureDefinition{Name="Fishing pier",Kind=FeatureKind.Deck,Area=new Rect(56,-238,10,32),Height=.08f,Color=CityColor.Wood,Railing=1f},
                new FeatureDefinition{Name="Gantry cranes",Kind=FeatureKind.Scatter,Height=.08f,Spacing=24,Recipes=new[]{"Gantry crane"},Points=new[]{new Vector2(-150,-202),new Vector2(-90,-202),new Vector2(20,-202),new Vector2(200,-172)}},
                new FeatureDefinition{Name="Container yard",Kind=FeatureKind.Scatter,Area=new Rect(-270,-200,330,30),Height=.08f,Rows=true,Spacing=7,Stack=new Vector2Int(1,3),Recipes=new[]{"Shipping container"},Yaws=new[]{90f}},
                new FeatureDefinition{Name="Dock crates",Kind=FeatureKind.Scatter,Area=new Rect(150,-205,130,40),Height=.08f,Count=14,Spacing=5,Props=new[]{CityPropKind.Planter,CityPropKind.Trash,CityPropKind.Newspaper}}}}
    };
}
