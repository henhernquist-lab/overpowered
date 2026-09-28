using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class CityArtProp : MonoBehaviour { public CityPropKind Kind; public bool Rooftop; }
public sealed class ArtBuilding : MonoBehaviour { public int Style; public string Archetype; public int District=-1; }
/// One district of the generated world. Its "Static" child is static-batched on its own; its NavMesh is its own NavMeshData.
public sealed class CityDistrictRoot : MonoBehaviour { public int Index; public string DistrictName; public Rect Region; public Transform Static; }
public sealed class CityArt : MonoBehaviour
{
    public CityArtSettings Settings {get;private set;}
    public WorldRendering Rendering=>Settings.Rendering;
    public readonly List<ArtPlacement> Placements=new List<ArtPlacement>();
    // Modes are read once so every prop (including runtime CreateProp calls) matches the rest of this city.
    public PropMeshMode PropMode {get;private set;}
    public StaticGeometryMode StaticMode {get;private set;}
    public double BuildMilliseconds {get;private set;}
    public double StaticFinalizeMilliseconds {get;private set;}
    public int SharedPropMeshes=>sharedProps.Count;
    /// Static primitive pieces generated (every mode), and static renderers/GameObjects that exist after finishing.
    public int PieceCount {get;private set;}
    public IReadOnlyList<CityDistrictRoot> Districts=>districtRoots;
    public Transform BackdropRoot {get;private set;}
    /// Renderers that draw buildings, ground, streets, structures and backdrop (combined children, pieces or city meshes).
    public IEnumerable<Renderer> StaticGeometryRenderers=>districtRoots.Where(r=>r!=null).SelectMany(r=>r.Static.GetComponentsInChildren<Renderer>())
        .Concat(BackdropRoot!=null?BackdropRoot.GetComponentsInChildren<Renderer>():new Renderer[0]).Concat(cityRenderers.Where(r=>r!=null)).Distinct();
    readonly List<Mesh> ownedMeshes=new List<Mesh>();
    readonly List<GameObject> staticRoots=new List<GameObject>();
    readonly List<Renderer> cityRenderers=new List<Renderer>();
    readonly List<CityDistrictRoot> districtRoots=new List<CityDistrictRoot>();
    readonly Dictionary<(int,int,int),Transform> chunks=new Dictionary<(int,int,int),Transform>();
    readonly Dictionary<(Transform,CityColor,int),Batch> batches=new Dictionary<(Transform,CityColor,int),Batch>();
    readonly Dictionary<string,SharedProp> sharedProps=new Dictionary<string,SharedProp>();
    readonly System.Diagnostics.Stopwatch buildWatch=new System.Diagnostics.Stopwatch();
    static readonly Dictionary<PrimitiveType,Mesh> primitives=new Dictionary<PrimitiveType,Mesh>();
    GameTuning tuning;
    struct PropPiece { public string Name; public PrimitiveType Type; public Vector3 Position, Size; public Quaternion Rotation; public CityColor Color; }
    sealed class SharedProp { public Mesh Mesh; public Mesh[] Parts; public Material[] Materials; }
    sealed class Batch { public Transform Owner; public CityColor Color; public int Layer; public readonly List<CombineInstance> Parts=new List<CombineInstance>(); public long Vertices; }
    bool Meshes=>StaticMode==StaticGeometryMode.BuildingMeshes;
    bool facadeDetail;float facadeMinHeight;
    public void Initialize(GameTuning config)
    {
        buildWatch.Restart();
        tuning=config;Settings=Resources.Load<CityArtSettings>("CityArtSettings");
        if(Settings==null||Settings.Palette==null)throw new System.InvalidOperationException("Create city art assets via Overpowered > Create missing city art assets.");
        PropMode=Settings.PropMeshes;StaticMode=Settings.StaticGeometry;
        var preset=VisualPreset.Current;facadeDetail=VisualPreset.Active(preset)&&preset.FacadeDetail;facadeMinHeight=preset!=null?preset.FacadeMinHeight:0f;
        gameObject.AddComponent<CityMaterials>().Initialize(Settings.Palette);
    }
    public static GameObject Piece(Transform parent,string name,Vector3 position,Vector3 size,CityColor color,bool solid=false,PrimitiveType type=PrimitiveType.Cube)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;
        go.transform.localScale=PieceScale(size,type);
        go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(color);
        if(!solid){go.GetComponent<Collider>().enabled=false;Object.Destroy(go.GetComponent<Collider>());}
        return go;
    }
    static Vector3 PieceScale(Vector3 size,PrimitiveType type)=>new Vector3(size.x,size.y*(type==PrimitiveType.Cylinder?.5f:1),size.z);

    // ------------------------------------------------------------------ roots, chunks and the static piece sink
    public CityDistrictRoot District(int index,CityPlan plan=null)
    {
        while(districtRoots.Count<=index)districtRoots.Add(null);
        if(districtRoots[index]!=null)return districtRoots[index];
        var def=plan!=null&&index<plan.Districts.Count?plan.Districts[index]:null;
        var go=new GameObject("District "+index+(def!=null?" — "+def.Name:""));go.transform.SetParent(transform,false);
        var root=go.AddComponent<CityDistrictRoot>();root.Index=index;root.DistrictName=def?.Name;root.Region=def!=null?def.Region:default;
        root.Static=new GameObject("Static").transform;root.Static.SetParent(go.transform,false);
        return districtRoots[index]=root;
    }
    /// Ground, streets and structures are grouped per district and per ChunkSize cell, so culling stays local.
    Transform Chunk(int district,Vector3 world)
    {
        float size=Mathf.Max(8,Rendering.ChunkSize);var key=(district,Mathf.FloorToInt(world.x/size),Mathf.FloorToInt(world.z/size));
        if(chunks.TryGetValue(key,out var t)&&t!=null)return t;
        var go=new GameObject($"Chunk {key.Item2},{key.Item3}");go.transform.SetParent(District(district).Static,false);
        staticRoots.Add(go);return chunks[key]=go.transform;
    }
    /// One primitive piece of never-moving geometry. Legacy modes create a GameObject per piece; BuildingMeshes appends it to
    /// the owner's mesh for that colour and layer and adds a BoxCollider on the owner when solid. Owners are never rotated/scaled.
    GameObject Part(Transform owner,string name,Vector3 local,Vector3 size,CityColor color,bool solid,bool detail,PrimitiveType type=PrimitiveType.Cube,Quaternion? rotated=null)
    {
        PieceCount++;
        var rotation=rotated??Quaternion.identity;
        int layer=detail?Rendering.DetailLayer:0;
        if(!Meshes)
        {
            var go=Piece(owner,name,local,size,color,solid,type);go.layer=layer;
            if(detail)go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            if(rotation!=Quaternion.identity)go.transform.localRotation=rotation;
            return go;
        }
        var key=(owner,color,layer);
        if(!batches.TryGetValue(key,out var batch))batches[key]=batch=new Batch{Owner=owner,Color=color,Layer=layer};
        var mesh=Primitive(type);
        batch.Parts.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(local,rotation,PieceScale(size,type))});batch.Vertices+=mesh.vertexCount;
        if(solid)
        {
            // Cylinders are symmetric about their axis, so a yaw-only rotation needs no rotated collider.
            bool upright=rotation==Quaternion.identity||(type==PrimitiveType.Cylinder&&Mathf.Abs(Quaternion.Angle(rotation,Quaternion.Euler(0,rotation.eulerAngles.y,0)))<.01f);
            bool quarter=Mathf.Abs(Quaternion.Angle(rotation,Quaternion.Euler(0,Mathf.Round(rotation.eulerAngles.y/90)*90,0)))<.01f;
            if(upright||quarter)
            {
                var s=upright?size:Quaternion.Euler(0,Mathf.Round(rotation.eulerAngles.y/90)*90,0)*size;
                var box=owner.gameObject.AddComponent<BoxCollider>();box.center=local;box.size=new Vector3(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));
            }
            else
            {
                var holder=new GameObject(name+" collider");holder.transform.SetParent(owner,false);holder.transform.localPosition=local;holder.transform.localRotation=rotation;
                holder.AddComponent<BoxCollider>().size=size;
            }
        }
        return null;
    }
    void FlushBatches()
    {
        foreach(var batch in batches.Values)
        {
            if(batch.Owner==null||batch.Parts.Count==0)continue;
            var mesh=new Mesh{name=batch.Owner.name+" / "+batch.Color+(batch.Layer!=0?" (detail)":""),indexFormat=batch.Vertices>65000?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.CombineMeshes(batch.Parts.ToArray(),true,true);
            var go=new GameObject(batch.Color+(batch.Layer!=0?" detail":"")){layer=batch.Layer};go.transform.SetParent(batch.Owner,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=CityMaterials.Get(batch.Color);
            // Facade/street detail (windows, bands, signs, kerbs, lane marks) receives shadows but never casts: it is flush
            // with the surfaces behind it, and casting it multiplied shadow-pass triangles for no visible change.
            if(batch.Layer!=0)renderer.shadowCastingMode=ShadowCastingMode.Off;
            ownedMeshes.Add(mesh);
        }
        batches.Clear();
    }

    // ------------------------------------------------------------------ buildings
    public void Building(BuildingPlacement b,int index)
    {
        int styleIndex=Settings.StyleOf(tuning.City,b,index);var style=Settings.Styles[styleIndex];
        var root=new GameObject("Building "+index+" — "+style.Name);root.transform.SetParent(District(Mathf.Max(0,b.District)).Static,false);
        root.transform.position=b.Position-Vector3.up*b.Size.y*.5f;
        var info=root.AddComponent<ArtBuilding>();info.Style=styleIndex;info.Archetype=style.Name;info.District=b.District;
        var t=root.transform;
        float w=b.Size.x*style.Footprint.x,d=b.Size.z*style.Footprint.y,h=b.Size.y;
        float entry=Mathf.Min(Settings.FloorHeight,h*.4f),door=Settings.EntranceWidth,recess=Settings.EntranceDepth;
        // Solid ground-floor wings and recessed back wall make an actual entrance pocket.
        Part(t,"Ground floor",new Vector3(0,entry*.5f,recess*.5f),new Vector3(w,entry,d-recess),style.Wall,true,false);
        foreach(float side in new[]{-1f,1f})Part(t,"Entrance jamb",new Vector3(side*(w+door)*.25f,entry*.5f,-d*.5f+recess*.5f),new Vector3((w-door)*.5f,entry,recess),style.Wall,true,false);
        Part(t,"Recessed doorway",new Vector3(0,entry*.4f,-d*.5f+recess-.015f),new Vector3(door,entry*.8f,.035f),CityColor.Glass,false,true);
        float split=Mathf.Max(entry,h*style.SetbackAt);
        if(split-entry>.01f)Part(t,"Lower volume",new Vector3(0,(entry+split)*.5f,0),new Vector3(w,split-entry,d),style.Wall,true,false);
        float uw=w*style.UpperWidth,ud=d*style.UpperWidth;
        if(h-split>.01f)Part(t,"Upper volume",new Vector3(0,(split+h)*.5f,0),new Vector3(uw,h-split,ud),style.Wall,true,false);
        Facade(t,w,d,entry,split,index,style);Facade(t,uw,ud,split,h,index,style);
        if(style.UpperWidth<1)Part(t,"Setback terrace",new Vector3(0,split+.04f,0),new Vector3(w+.12f,.08f,d+.12f),CityColor.Roof,true,false);
        Part(t,"Roof deck",new Vector3(0,h+.04f,0),new Vector3(uw,.08f,ud),CityColor.Roof,true,false);
        float wall=Settings.ParapetHeight,thick=Settings.ParapetThickness;
        foreach(float side in new[]{-1f,1f})
        {
            Part(t,"Parapet",new Vector3(side*(uw-thick)*.5f,h+wall*.5f,0),new Vector3(thick,wall,ud),CityColor.Cream,true,false);
            Part(t,"Parapet",new Vector3(0,h+wall*.5f,side*(ud-thick)*.5f),new Vector3(uw,wall,thick),CityColor.Cream,true,false);
        }
        if(facadeDetail&&h>=facadeMinHeight)StreetLevel(t,w,d,uw,ud,h,entry,split,door);
        Part(t,"Shop canopy",new Vector3(0,entry,-d*.5f-.3f),new Vector3(w*.6f,.25f,.7f),CityColor.Teal,false,true);
        Part(t,"Shop sign",new Vector3(0,entry-.5f,-d*.5f-.36f),new Vector3(w*.5f,.65f,.12f),CityColor.Brick,false,true);
        for(int i=0;i<3;i++)Part(t,"Abstract shop glyph",new Vector3((i-1)*w*.13f,entry-.5f,-d*.5f-.44f),new Vector3(w*.09f,.16f,.035f),CityColor.Cream,false,true);
        if(Meshes)FlushOwner(t);
        StaticRoot(root);
    }
    /// VisualPreset.FacadeDetail (urban-height buildings only): the ground floor meets the street with shop glazing on every
    /// face (either side of the entrance on the front), a dark base plinth, corner pilasters up the lower volume and a
    /// projecting cornice under the parapet. Existing palette colours and batches only: no new meshes, materials or draws.
    void StreetLevel(Transform t,float w,float d,float uw,float ud,float h,float entry,float split,float door)
    {
        float gy=entry*.44f,gh=entry*.56f,out_=.03f;
        foreach(float side in new[]{-1f,1f})
        {
            Part(t,"Shop glazing",new Vector3(side*(w*.5f+out_),gy,0),new Vector3(.04f,gh,d*.78f),CityColor.Glass,false,true);
            Part(t,"Shop glazing",new Vector3(side*(w+door)*.25f,gy,-d*.5f-out_),new Vector3((w-door)*.5f*.72f,gh,.04f),CityColor.Glass,false,true);
            Part(t,"Base plinth",new Vector3(side*(w*.5f+.04f),.22f,0),new Vector3(.08f,.44f,d+.08f),CityColor.Slate,false,true);
            Part(t,"Base plinth",new Vector3(0,.22f,side*(d*.5f+.04f)),new Vector3(w+.08f,.44f,.08f),CityColor.Slate,false,true);
            foreach(float other in new[]{-1f,1f})
                Part(t,"Corner pilaster",new Vector3(side*(w*.5f+.06f),(entry+split)*.5f,other*(d*.5f+.06f)),new Vector3(.36f,split-entry+.2f,.36f),CityColor.Cream,false,true);
        }
        Part(t,"Shop glazing",new Vector3(0,gy,d*.5f+out_),new Vector3(w*.78f,gh,.04f),CityColor.Glass,false,true);
        Part(t,"Cornice",new Vector3(0,h-.12f,0),new Vector3(uw+.5f,.24f,ud+.5f),CityColor.Cream,false,false);
    }
    void Facade(Transform root,float w,float d,float bottom,float top,int seed,BuildingStyle style)
    {
        if(top-bottom<.5f)return;
        float spacing=Settings.WindowSpacing*Mathf.Max(.25f,style.WindowSpacingScale);
        int floors=Mathf.Max(1,Mathf.FloorToInt((top-bottom)/Settings.FloorHeight));float storey=(top-bottom)/floors;
        for(int floor=0;floor<floors;floor++)
        {
            float y=bottom+floor*storey;
            Part(root,"Floor band",new Vector3(0,y,0),new Vector3(w+.12f,Settings.BandHeight,d+.12f),CityColor.Cream,false,true);
            float wy=y+storey*.5f;float wh=Mathf.Min(Settings.WindowHeight,storey*.65f);if(wh<.3f)continue;
            for(int face=0;face<4;face++)
            {
                bool acrossX=face<2;float span=acrossX?w:d;int count=Mathf.Max(1,Mathf.FloorToInt(span/spacing));
                for(int i=0;i<count;i++)
                {
                    float along=(i-(count-1)*.5f)*spacing;float side=face%2==0?-1:1;
                    var pos=acrossX?new Vector3(along,wy,side*(d*.5f+.015f)):new Vector3(side*(w*.5f+.015f),wy,along);
                    var size=acrossX?new Vector3(Settings.WindowWidth,wh,.035f):new Vector3(.035f,wh,Settings.WindowWidth);
                    Part(root,"Window",pos,size,(seed+i+face+(int)y)%5==0?CityColor.Amber:CityColor.Glass,false,true);
                }
            }
        }
    }
    /// Flushes only this owner's batches (a building is complete once Building() returns).
    void FlushOwner(Transform owner)
    {
        var mine=batches.Where(p=>p.Key.Item1==owner).ToList();
        foreach(var p in mine)batches.Remove(p.Key);
        var keep=new Dictionary<(Transform,CityColor,int),Batch>(batches);batches.Clear();
        foreach(var p in mine)batches[p.Key]=p.Value;
        FlushBatches();
        foreach(var p in keep)batches[p.Key]=p.Value;
    }

    // ------------------------------------------------------------------ ground, sea, decks, streets, structures, backdrop
    public void Ground(CityPlan plan)
    {
        for(int i=0;i<plan.Districts.Count;i++)District(i,plan);
        foreach(var s in plan.Slabs)
        {
            var c=new Vector3(s.Area.center.x,(s.Top+s.Bottom)*.5f,s.Area.center.y);
            Part(Chunk(s.District,c),"Ground",c,new Vector3(s.Area.width,Mathf.Max(.02f,s.Top-s.Bottom),s.Area.height),s.Color,true,s.Detail);
        }
        foreach(var k in plan.Decks)
        {
            var a=k.Area;float top=k.Top+.04f;bool alongX=a.width>=a.height;var owner=Chunk(k.District,new Vector3(a.center.x,0,a.center.y));
            Part(owner,"Deck",new Vector3(a.center.x,top-.4f,a.center.y),new Vector3(a.width,.8f,a.height),k.Color,true,false);
            foreach(float side in new[]{-1f,1f})
            {
                var rail=alongX?new Vector3(a.center.x,top+k.Railing*.5f,side<0?a.yMin+.15f:a.yMax-.15f):new Vector3(side<0?a.xMin+.15f:a.xMax-.15f,top+k.Railing*.5f,a.center.y);
                var size=alongX?new Vector3(a.width,k.Railing,.3f):new Vector3(.3f,k.Railing,a.height);
                Part(owner,"Railing",rail,size,CityColor.Cream,true,false);
                var girder=alongX?new Vector3(a.center.x,top-1.2f,side<0?a.yMin+.3f:a.yMax-.3f):new Vector3(side<0?a.xMin+.3f:a.xMax-.3f,top-1.2f,a.center.y);
                Part(owner,"Girder",girder,alongX?new Vector3(a.width,.9f,.5f):new Vector3(.5f,.9f,a.height),CityColor.Slate,false,true);
            }
            float span=alongX?a.width:a.height;
            for(int n=0;n<k.Arches;n++)
            {
                float f=(n+1f)/(k.Arches+1f);float low=plan.Seabed;
                var at=alongX?new Vector3(a.xMin+span*f,(low+top-.8f)*.5f,a.center.y):new Vector3(a.center.x,(low+top-.8f)*.5f,a.yMin+span*f);
                Part(owner,"Bridge pier",at,alongX?new Vector3(2,top-.8f-low,a.height*.8f):new Vector3(a.width*.8f,top-.8f-low,2),CityColor.Slate,false,true);
            }
        }
    }
    /// Sea surface, seabed and island boundary. Not part of any district root, so never in the city NavMesh.
    public void Sea(CityPlan plan,CityLayout layout)
    {
        var root=new GameObject("Sea and bounds").transform;root.SetParent(transform,false);
        var isl=plan.Island;float m=layout.Backdrop.SeaMargin;
        var sea=Piece(root,"Sea surface",new Vector3(isl.center.x,layout.WaterLevel-.05f,isl.center.y),new Vector3(isl.width+2*m,.1f,isl.height+2*m),CityColor.Water);
        sea.layer=Rendering.BackdropLayer;var seaRenderer=sea.GetComponent<Renderer>();seaRenderer.shadowCastingMode=ShadowCastingMode.Off;
        var outer=CityLayout.Expand(isl,layout.BoundaryMargin);
        var bed=root.gameObject.AddComponent<BoxCollider>();bed.center=new Vector3(outer.center.x,layout.SeabedLevel-1,outer.center.y);bed.size=new Vector3(outer.width,2,outer.height);
        float wallH=layout.BoundaryHeight;
        foreach(var (c,s) in new[]{(new Vector3(outer.xMin-1,wallH*.5f,outer.center.y),new Vector3(2,wallH,outer.height+4)),(new Vector3(outer.xMax+1,wallH*.5f,outer.center.y),new Vector3(2,wallH,outer.height+4)),
            (new Vector3(outer.center.x,wallH*.5f,outer.yMin-1),new Vector3(outer.width+4,wallH,2)),(new Vector3(outer.center.x,wallH*.5f,outer.yMax+1),new Vector3(outer.width+4,wallH,2))})
        {var wall=root.gameObject.AddComponent<BoxCollider>();wall.center=c;wall.size=s;}
    }
    public void Streets(CityPlan plan)
    {
        foreach(var block in plan.Blocks)
        {
            var r=block.Area;var center=new Vector3(r.center.x,0,r.center.y);var owner=Chunk(block.District,center);
            foreach(float side in new[]{-1f,1f})
            {
                Part(owner,"Curb",center+new Vector3(side*r.width*.5f,Settings.CurbHeight*.5f,0),new Vector3(Settings.CurbWidth,Settings.CurbHeight,r.height),CityColor.Cream,false,true);
                Part(owner,"Curb",center+new Vector3(0,Settings.CurbHeight*.5f,side*r.height*.5f),new Vector3(r.width,Settings.CurbHeight,Settings.CurbWidth),CityColor.Cream,false,true);
            }
        }
        foreach(var line in plan.Dashes)
        {
            var dir=line.To-line.From;float len=dir.magnitude;if(len<1)continue;dir/=len;bool alongX=Mathf.Abs(dir.x)>Mathf.Abs(dir.z);
            for(float t=Settings.RoadDashLength*.5f;t<len;t+=Settings.RoadDashSpacing)
            {
                var at=line.From+dir*t;
                Part(Chunk(line.District,at),"Road dash",at,alongX?new Vector3(Settings.RoadDashLength,.02f,.12f):new Vector3(.12f,.02f,Settings.RoadDashLength),CityColor.Amber,false,true);
            }
        }
        foreach(var cross in plan.Crossings)
        {
            var owner=Chunk(cross.District,cross.Center);
            for(int i=0;i<Settings.CrosswalkStripes;i++)
            {
                float offset=(i-(Settings.CrosswalkStripes-1)*.5f)*cross.Street/Settings.CrosswalkStripes;
                foreach(float side in new[]{-1f,1f})
                {
                    Part(owner,"Crosswalk",cross.Center+new Vector3(offset,0,side*cross.Street*.6f),new Vector3(Settings.CrosswalkStripe,.025f,1.8f),CityColor.Cream,false,true);
                    Part(owner,"Crosswalk",cross.Center+new Vector3(side*cross.Street*.6f,0,offset),new Vector3(1.8f,.025f,Settings.CrosswalkStripe),CityColor.Cream,false,true);
                }
            }
        }
    }
    public void Structures(CityPlan plan)
    {
        foreach(var s in plan.Structures)
        {
            var recipe=Settings.Recipe(s.Recipe);
            if(recipe==null)throw new System.InvalidOperationException("CityArtSettings has no structure recipe '"+s.Recipe+"'.");
            var rot=Quaternion.Euler(0,s.Yaw,0);var owner=Chunk(s.District,s.Position);
            foreach(var p in recipe.Pieces)
            {
                var color=p.Variant&&recipe.Variants.Length>0?recipe.Variants[s.Variant%recipe.Variants.Length]:p.Color;
                Part(owner,recipe.Name,s.Position+rot*p.Position,p.Size,color,p.Solid,p.Detail,p.Type,rot*Quaternion.Euler(p.Euler));
            }
        }
    }
    /// Distant "mainland" silhouettes across the sea: no colliders, backdrop layer, fogged by distance.
    public void Backdrop(CityPlan plan,BackdropSettings backdrop)
    {
        BackdropRoot=new GameObject("Backdrop skyline").transform;BackdropRoot.SetParent(transform,false);
        Skirt(plan,backdrop);
        var rng=new System.Random(unchecked(tuning.City.Seed*13+5));var centre=new Vector3(plan.Island.center.x,plan.WaterLevel,plan.Island.center.y);
        for(int i=0;i<backdrop.Count;i++)
        {
            float angle=(i+(float)rng.NextDouble()*.8f)/backdrop.Count*Mathf.PI*2,radius=Mathf.Lerp(backdrop.Radius.x,backdrop.Radius.y,(float)rng.NextDouble());
            float h=Mathf.Lerp(backdrop.Height.x,backdrop.Height.y,(float)Mathf.Pow((float)rng.NextDouble(),2)),w=Mathf.Lerp(backdrop.Width.x,backdrop.Width.y,(float)rng.NextDouble());
            var rotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,0);var outward=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            // A low shore under each cluster so the silhouettes stand on land instead of floating on the horizon.
            Silhouette(centre+outward*(radius+20)+Vector3.up*3,new Vector3(w*2.6f,6,70),rotation,backdrop.ShoreColor);
            Silhouette(centre+outward*radius+Vector3.up*h*.5f,new Vector3(w,h,w*.7f),rotation,backdrop.Color);
            if(rng.NextDouble()<.6){float h2=h*Mathf.Lerp(.35f,.8f,(float)rng.NextDouble());Silhouette(centre+outward*(radius+12)+rotation*Vector3.right*(w*.8f)+Vector3.up*h2*.5f,new Vector3(w*.7f,h2,w*.6f),rotation,backdrop.Color);}
        }
    }
    void Skirt(CityPlan plan,BackdropSettings backdrop)
    {
        var c=new Vector3(plan.Island.center.x,plan.WaterLevel-20+backdrop.SkirtHeight*.5f,plan.Island.center.y);float r=backdrop.SkirtRadius,h=backdrop.SkirtHeight+20;
        Silhouette(c+Vector3.forward*r,new Vector3(2*r+20,h,10),Quaternion.identity,backdrop.SkirtColor).name=HazeSkirtName;Silhouette(c+Vector3.back*r,new Vector3(2*r+20,h,10),Quaternion.identity,backdrop.SkirtColor).name=HazeSkirtName;
        Silhouette(c+Vector3.right*r,new Vector3(10,h,2*r+20),Quaternion.identity,backdrop.SkirtColor).name=HazeSkirtName;Silhouette(c+Vector3.left*r,new Vector3(10,h,2*r+20),Quaternion.identity,backdrop.SkirtColor).name=HazeSkirtName;
    }
    /// Name of the four haze-skirt walls (VisualPreset may hide them when its sky paints the lower half in the fog colour).
    public const string HazeSkirtName="Backdrop haze skirt";
    GameObject Silhouette(Vector3 at,Vector3 size,Quaternion rotation,CityColor color)
    {
        var go=Piece(BackdropRoot,"Backdrop silhouette",at,size,color);go.layer=Rendering.BackdropLayer;go.transform.rotation=rotation;
        var r=go.GetComponent<Renderer>();r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
        return go;
    }
    void StaticRoot(GameObject root){staticRoots.Add(root);}
    /// Call once all static geometry exists (and before any capture render). Legacy modes combine as before; StaticBatching and
    /// BuildingMeshes run StaticBatchingUtility once PER DISTRICT ROOT (plus the backdrop), never over props or actors.
    public void FinishStaticGeometry()
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        if(Meshes)FlushBatches();
        var roots=districtRoots.Where(r=>r!=null).Select(r=>r.Static.gameObject).ToList();
        if(BackdropRoot!=null)roots.Add(BackdropRoot.gameObject);
        if(StaticMode==StaticGeometryMode.PerRootCombine)foreach(var root in staticRoots.Where(r=>r!=null))Combine(root);
        else if(StaticMode==StaticGeometryMode.StaticBatching||Meshes)
            foreach(var root in roots)
            {
                var filters=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh!=null&&f.GetComponent<MeshRenderer>()!=null).ToArray();
                if(filters.Length==0)continue;
                var source=new HashSet<Mesh>(filters.Select(f=>f.sharedMesh));
                StaticBatchingUtility.Combine(filters.Select(f=>f.gameObject).ToArray(),root);
                // The batch meshes Unity creates here belong to this city; release them with it.
                foreach(var mesh in filters.Select(f=>f.sharedMesh).Distinct())if(mesh!=null&&!source.Contains(mesh))ownedMeshes.Add(mesh);
            }
        else if(StaticMode==StaticGeometryMode.CityCombine)
        {
            var filters=roots.SelectMany(r=>r.GetComponentsInChildren<MeshFilter>()).Where(f=>f.sharedMesh!=null&&f.GetComponent<MeshRenderer>()!=null).ToArray();
            foreach(var group in filters.GroupBy(f=>(f.GetComponent<Renderer>().sharedMaterial,f.gameObject.layer)))
            {
                var parts=group.ToArray();var mesh=new Mesh{name="City static / "+group.Key.Item1.name,indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(parts.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
                var combined=new GameObject("City static geometry / "+group.Key.Item1.name){layer=group.Key.Item2};combined.transform.SetParent(transform,false);
                combined.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=combined.AddComponent<MeshRenderer>();renderer.sharedMaterial=group.Key.Item1;
                ownedMeshes.Add(mesh);cityRenderers.Add(renderer);
                foreach(var part in parts){var old=part.GetComponent<Renderer>();old.enabled=false;Destroy(old);Destroy(part);}
            }
        }
        staticRoots.Clear();
        StaticFinalizeMilliseconds+=watch.Elapsed.TotalMilliseconds;
    }
    /// Camera far clip, per-layer cull distances and fog for the big world (values in CityArtSettings.Rendering).
    public void ApplyRendering(Camera camera)
    {
        var r=Rendering;camera.farClipPlane=r.FarClip;
        var distances=new float[32];distances[r.DetailLayer]=r.DetailCull;distances[r.PropLayer]=r.PropCull;distances[r.ActorLayer]=r.ActorCull;distances[r.BackdropLayer]=r.BackdropCull;
        camera.layerCullDistances=distances;camera.layerCullSpherical=r.SphericalCull;
        RenderSettings.fog=r.Fog;RenderSettings.fogMode=r.FogMode;RenderSettings.fogDensity=r.FogDensity;RenderSettings.fogColor=Settings.Palette.Colors[(int)r.FogColor];
    }
    public void Populate(CityPlan plan)
    {
        Placements.AddRange(Settings.Generate(tuning.City,plan));foreach(var placement in Placements)CreateProp(placement);
        BuildMilliseconds=buildWatch.Elapsed.TotalMilliseconds;
    }
    public GameObject CreateProp(ArtPlacement placement)
    {
        var shape=System.Array.Find(Settings.Shapes,s=>s.Kind==placement.Kind);
        var root=new GameObject("City "+placement.Kind);root.transform.SetParent(transform,false);root.transform.position=placement.Position+Vector3.up*(placement.Rooftop?.1f:0);
        root.transform.rotation=Quaternion.Euler(0,placement.Yaw,0);
        var tag=root.AddComponent<CityArtProp>();tag.Kind=placement.Kind;tag.Rooftop=placement.Rooftop;
        Vector3 s=shape.Size;var pieces=PropRecipe(placement,s);
        bool shared=PropMode==PropMeshMode.SharedPerKind||PropMode==PropMeshMode.SharedPerKindPerMaterial;
        if(!shared)foreach(var p in pieces){var go=Piece(root.transform,p.Name,p.Position,p.Size,p.Color,false,p.Type);if(p.Rotation!=Quaternion.identity)go.transform.localRotation=p.Rotation;}
        var collider=root.AddComponent<BoxCollider>();collider.center=Vector3.up*s.y*.5f;collider.size=s;
        if(shape.Destructible)
        {
            var body=root.AddComponent<Rigidbody>();body.mass=shape.Mass;body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<BreakableProp>().Configure(tuning.Props);
        }
        if(shared)
        {
            var mesh=SharedPropMesh(placement.Kind,s,pieces);
            if(PropMode==PropMeshMode.SharedPerKind)
            {root.AddComponent<MeshFilter>().sharedMesh=mesh.Mesh;root.AddComponent<MeshRenderer>().sharedMaterials=mesh.Materials;}
            else for(int i=0;i<mesh.Parts.Length;i++)
            {
                var child=new GameObject(mesh.Materials[i].name);child.transform.SetParent(root.transform,false);
                child.AddComponent<MeshFilter>().sharedMesh=mesh.Parts[i];child.AddComponent<MeshRenderer>().sharedMaterial=mesh.Materials[i];
            }
        }
        else if(PropMode==PropMeshMode.PerPropCombine)Combine(root);
        foreach(var node in root.GetComponentsInChildren<Transform>(true))node.gameObject.layer=Rendering.PropLayer;
        return root;
    }
    // Piece list for one prop in root-local space. Pure data: identical for every mode.
    static List<PropPiece> PropRecipe(ArtPlacement placement,Vector3 s)
    {
        var pieces=new List<PropPiece>();
        void Add(string name,PrimitiveType type,Vector3 position,Vector3 size,CityColor color,Quaternion rotation)=>pieces.Add(new PropPiece{Name=name,Type=type,Position=position,Size=size,Color=color,Rotation=rotation});
        void box(string name,Vector3 p,Vector3 size,CityColor color)=>Add(name,PrimitiveType.Cube,Vector3.Scale(p,s),Vector3.Scale(size,s),color,Quaternion.identity);
        void cylinder(string name,Vector3 p,Vector3 size,CityColor color)=>Add(name,PrimitiveType.Cylinder,Vector3.Scale(p,s),Vector3.Scale(size,s),color,Quaternion.identity);
        switch(placement.Kind)
        {
            case CityPropKind.Lamp:
            case CityPropKind.Antenna:
                box("Mast",new Vector3(0,.5f,0),new Vector3(.13f,1,.13f),CityColor.Metal);
                box("Base",new Vector3(0,.07f,0),new Vector3(.6f,.14f,.6f),CityColor.Metal);
                box("Crossbar",new Vector3(0,.86f,0),new Vector3(1,.07f,.18f),CityColor.Cream);
                if(placement.Kind==CityPropKind.Lamp)
                {
                    box("Light housing",new Vector3(0,.96f,0),new Vector3(.85f,.08f,.85f),CityColor.Amber);
                    box("Street sign",new Vector3(.3f,.65f,0),new Vector3(.85f,.1f,.1f),CityColor.Teal);
                    box("Street sign glyph",new Vector3(.3f,.65f,-.07f),new Vector3(.5f,.025f,.04f),CityColor.Cream);
                }
                break;
            case CityPropKind.Bench:
                box("Seat",new Vector3(0,.45f,0),new Vector3(1,.16f,1),CityColor.Wood);
                box("Back",new Vector3(0,.8f,.38f),new Vector3(1,.4f,.18f),CityColor.Wood);
                foreach(float side in new[]{-1f,1f})box("Leg",new Vector3(side*.35f,.2f,0),new Vector3(.08f,.4f,.8f),CityColor.Metal);break;
            case CityPropKind.Car:
                box("Chassis",new Vector3(0,.35f,0),new Vector3(.95f,.4f,.96f),placement.Position.x<0?CityColor.Teal:CityColor.Red);
                box("Cabin",new Vector3(0,.7f,-.03f),new Vector3(.8f,.35f,.5f),CityColor.Glass);
                box("Roof",new Vector3(0,.89f,-.03f),new Vector3(.8f,.08f,.5f),CityColor.Cream);
                foreach(float side in new[]{-1f,1f})
                {
                    box("Bumper",new Vector3(0,.23f,side*.49f),new Vector3(.95f,.12f,.02f),CityColor.Metal);
                    box("Lights",new Vector3(0,.42f,side*.49f),new Vector3(.65f,.08f,.03f),CityColor.Amber);
                    foreach(float end in new[]{-1f,1f})
                        Add("Wheel",PrimitiveType.Cylinder,new Vector3(side*s.x*.44f,s.y*.22f,end*s.z*.3f),new Vector3(s.y*.4f,s.x*.16f,s.y*.4f),CityColor.Metal,Quaternion.Euler(0,0,90));
                }break;
            case CityPropKind.BusStop:
                box("Shelter roof",new Vector3(0,.96f,0),new Vector3(1,.08f,1),CityColor.Teal);
                box("Shelter back",new Vector3(-.45f,.5f,0),new Vector3(.08f,.9f,1),CityColor.Glass);
                foreach(float side in new[]{-1f,1f})box("Shelter post",new Vector3(.35f,.48f,side*.44f),new Vector3(.08f,.96f,.04f),CityColor.Cream);
                box("Waiting bench",new Vector3(-.15f,.18f,0),new Vector3(.5f,.08f,.75f),CityColor.Wood);break;
            case CityPropKind.WaterTower:
                foreach(float x in new[]{-.32f,.32f})foreach(float z in new[]{-.32f,.32f})box("Tower leg",new Vector3(x,.2f,z),new Vector3(.08f,.4f,.08f),CityColor.Metal);
                cylinder("Tank",new Vector3(0,.62f,0),new Vector3(.9f,.56f,.9f),CityColor.Wood);
                cylinder("Tank cap",new Vector3(0,.93f,0),new Vector3(1,.12f,1),CityColor.Metal);
                foreach(float y in new[]{.4f,.8f})cylinder("Tank band",new Vector3(0,y,0),new Vector3(.94f,.03f,.94f),CityColor.Cream);break;
            case CityPropKind.Vent:
            case CityPropKind.Hydrant:
                cylinder("Stem",new Vector3(0,.45f,0),new Vector3(.55f,.9f,.55f),placement.Kind==CityPropKind.Hydrant?CityColor.Red:CityColor.Metal);
                cylinder("Cap",new Vector3(0,.92f,0),new Vector3(.9f,.16f,.9f),CityColor.Cream);
                box("Cross pipe",new Vector3(0,.6f,0),new Vector3(1,.22f,.25f),CityColor.Metal);break;
            case CityPropKind.Planter:
                box("Planter",new Vector3(0,.25f,0),new Vector3(.85f,.5f,.85f),CityColor.Sand);
                Add("Low poly shrub",PrimitiveType.Cube,new Vector3(0,s.y*.65f,0),s*.65f,CityColor.Leaf,Quaternion.Euler(0,25,0));break;
            case CityPropKind.Billboard:
                foreach(float side in new[]{-1f,1f})box("Sign support",new Vector3(side*.35f,.35f,0),new Vector3(.06f,.7f,.3f),CityColor.Metal);
                box("Billboard",new Vector3(0,.72f,0),new Vector3(1,.56f,.3f),CityColor.Teal);
                for(int i=0;i<3;i++)box("Abstract sign glyph",new Vector3((i-1)*.25f,.72f,-.2f),new Vector3(.12f,.3f,.05f),i==1?CityColor.Amber:CityColor.Cream);break;
            default:
                box("Cabinet",new Vector3(0,.48f,0),new Vector3(.92f,.96f,.92f),placement.Kind==CityPropKind.Newspaper?CityColor.Red:CityColor.Slate);
                box("Lid",new Vector3(0,.97f,0),new Vector3(1,.06f,1),CityColor.Cream);
                if(placement.Kind==CityPropKind.HVAC)
                {cylinder("Fan",new Vector3(0,1.02f,0),new Vector3(.6f,.04f,.65f),CityColor.Metal);for(int i=0;i<4;i++)box("Vent grille",new Vector3(0,.2f+i*.16f,-.47f),new Vector3(.65f,.05f,.025f),CityColor.Glass);}
                else box(placement.Kind==CityPropKind.RoofAccess?"Access door":"Inset panel",new Vector3(0,.5f,-.47f),new Vector3(.6f,.7f,.03f),CityColor.Glass);
                break;
        }
        return pieces;
    }
    // One mesh per recipe signature (kind + the material of every piece), built once and shared by every instance,
    // so identical props present the same Mesh and GPU instancing can draw them together.
    SharedProp SharedPropMesh(CityPropKind kind,Vector3 size,List<PropPiece> pieces)
    {
        string key=kind+" "+size.ToString("R")+":"+string.Join(",",pieces.Select(p=>p.Color));
        if(sharedProps.TryGetValue(key,out var shared))return shared;
        var colors=pieces.Select(p=>p.Color).Distinct().ToArray(); // first-appearance order = legacy child order
        shared=new SharedProp{Parts=new Mesh[colors.Length],Materials=colors.Select(CityMaterials.Get).ToArray()};
        for(int i=0;i<colors.Length;i++)
        {
            var part=new Mesh{name="Prop "+key+" / "+colors[i]};
            part.CombineMeshes(pieces.Where(p=>p.Color==colors[i]).Select(p=>new CombineInstance{mesh=Primitive(p.Type),transform=Matrix4x4.TRS(p.Position,p.Rotation,PieceScale(p.Size,p.Type))}).ToArray(),true,true);
            shared.Parts[i]=part;ownedMeshes.Add(part);
        }
        if(PropMode==PropMeshMode.SharedPerKind)
        {
            shared.Mesh=new Mesh{name="Prop "+key};
            shared.Mesh.CombineMeshes(shared.Parts.Select(p=>new CombineInstance{mesh=p,transform=Matrix4x4.identity}).ToArray(),false,false);
            ownedMeshes.Add(shared.Mesh);
        }
        sharedProps.Add(key,shared);return shared;
    }
    static Mesh Primitive(PrimitiveType type)
    {
        if(primitives.TryGetValue(type,out var mesh)&&mesh!=null)return mesh;
        var probe=GameObject.CreatePrimitive(type);probe.SetActive(false);mesh=probe.GetComponent<MeshFilter>().sharedMesh;Destroy(probe);
        primitives[type]=mesh;return mesh;
    }
    void Combine(GameObject root)
    {
        var filters=root.GetComponentsInChildren<MeshFilter>();
        foreach(var group in filters.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial))
        {
            var parts=group.ToArray();var mesh=new Mesh{name=root.name+" / "+group.Key.name,indexFormat=IndexFormat.UInt32};
            mesh.CombineMeshes(parts.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=root.transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
            var combined=new GameObject(group.Key.name);combined.transform.SetParent(root.transform,false);combined.AddComponent<MeshFilter>().sharedMesh=mesh;combined.AddComponent<MeshRenderer>().sharedMaterial=group.Key;ownedMeshes.Add(mesh);
            foreach(var part in parts){var renderer=part.GetComponent<Renderer>();renderer.enabled=false;Destroy(renderer);Destroy(part);}
        }
    }
    void OnDestroy(){foreach(var mesh in ownedMeshes)if(mesh!=null)Destroy(mesh);}
}
