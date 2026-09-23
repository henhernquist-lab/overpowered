using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class CityArtProp : MonoBehaviour { public CityPropKind Kind; public bool Rooftop; }
public sealed class ArtBuilding : MonoBehaviour { public int Style; public string Archetype; }
public sealed class CityArt : MonoBehaviour
{
    public CityArtSettings Settings {get;private set;}
    public readonly List<ArtPlacement> Placements=new List<ArtPlacement>();
    // Modes are read once so every prop (including runtime CreateProp calls) matches the rest of this city.
    public PropMeshMode PropMode {get;private set;}
    public StaticGeometryMode StaticMode {get;private set;}
    public double BuildMilliseconds {get;private set;}
    public double StaticFinalizeMilliseconds {get;private set;}
    public int SharedPropMeshes=>sharedProps.Count;
    /// Renderers that draw buildings and streets in the current mode (combined children, pieces or city meshes).
    public IEnumerable<Renderer> StaticGeometryRenderers=>staticRoots.Where(r=>r!=null).SelectMany(r=>r.GetComponentsInChildren<Renderer>()).Concat(cityRenderers.Where(r=>r!=null));
    readonly List<Mesh> ownedMeshes=new List<Mesh>();
    readonly List<GameObject> staticRoots=new List<GameObject>(), pendingStatic=new List<GameObject>();
    readonly List<Renderer> cityRenderers=new List<Renderer>();
    readonly Dictionary<string,SharedProp> sharedProps=new Dictionary<string,SharedProp>();
    readonly System.Diagnostics.Stopwatch buildWatch=new System.Diagnostics.Stopwatch();
    static readonly Dictionary<PrimitiveType,Mesh> primitives=new Dictionary<PrimitiveType,Mesh>();
    GameTuning tuning;
    struct PropPiece { public string Name; public PrimitiveType Type; public Vector3 Position, Size; public Quaternion Rotation; public CityColor Color; }
    sealed class SharedProp { public Mesh Mesh; public Mesh[] Parts; public Material[] Materials; }
    public void Initialize(GameTuning config)
    {
        buildWatch.Restart();
        tuning=config;Settings=Resources.Load<CityArtSettings>("CityArtSettings");
        if(Settings==null||Settings.Palette==null)throw new System.InvalidOperationException("Create city art assets via Overpowered > Create missing city art assets.");
        PropMode=Settings.PropMeshes;StaticMode=Settings.StaticGeometry;
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
    public void Building(BuildingPlacement b,int index)
    {
        int styleIndex=Settings.StyleIndex(tuning.City.Seed,index);var style=Settings.Styles[styleIndex];
        var root=new GameObject("Building "+index+" — "+style.Name);root.transform.SetParent(transform,false);
        root.transform.position=b.Position-Vector3.up*b.Size.y*.5f;
        var info=root.AddComponent<ArtBuilding>();info.Style=styleIndex;info.Archetype=style.Name;
        float w=b.Size.x*style.Footprint.x,d=b.Size.z*style.Footprint.y,h=b.Size.y;
        float entry=Mathf.Min(Settings.FloorHeight,h*.4f),door=Settings.EntranceWidth,recess=Settings.EntranceDepth;
        // Solid ground-floor wings and recessed back wall make an actual entrance pocket.
        Piece(root.transform,"Ground floor",new Vector3(0,entry*.5f,recess*.5f),new Vector3(w,entry,d-recess),style.Wall,true);
        foreach(float side in new[]{-1f,1f})Piece(root.transform,"Entrance jamb",new Vector3(side*(w+door)*.25f,entry*.5f,-d*.5f+recess*.5f),new Vector3((w-door)*.5f,entry,recess),style.Wall,true);
        Piece(root.transform,"Recessed doorway",new Vector3(0,entry*.4f,-d*.5f+recess-.015f),new Vector3(door,entry*.8f,.035f),CityColor.Glass);
        float split=Mathf.Max(entry,h*style.SetbackAt);
        Piece(root.transform,"Lower volume",new Vector3(0,(entry+split)*.5f,0),new Vector3(w,split-entry,d),style.Wall,true);
        float uw=w*style.UpperWidth,ud=d*style.UpperWidth;
        Piece(root.transform,"Upper volume",new Vector3(0,(split+h)*.5f,0),new Vector3(uw,h-split,ud),style.Wall,true);
        Facade(root.transform,w,d,entry,split,index);Facade(root.transform,uw,ud,split,h,index);
        if(style.UpperWidth<1)Piece(root.transform,"Setback terrace",new Vector3(0,split+.04f,0),new Vector3(w+.12f,.08f,d+.12f),CityColor.Roof,true);
        Piece(root.transform,"Roof deck",new Vector3(0,h+.04f,0),new Vector3(uw,.08f,ud),CityColor.Roof,true);
        float wall=Settings.ParapetHeight,thick=Settings.ParapetThickness;
        foreach(float side in new[]{-1f,1f})
        {
            Piece(root.transform,"Parapet",new Vector3(side*(uw-thick)*.5f,h+wall*.5f,0),new Vector3(thick,wall,ud),CityColor.Cream,true);
            Piece(root.transform,"Parapet",new Vector3(0,h+wall*.5f,side*(ud-thick)*.5f),new Vector3(uw,wall,thick),CityColor.Cream,true);
        }
        Piece(root.transform,"Shop canopy",new Vector3(0,entry,-d*.5f-.3f),new Vector3(w*.6f,.25f,.7f),CityColor.Teal);
        Piece(root.transform,"Shop sign",new Vector3(0,entry-.5f,-d*.5f-.36f),new Vector3(w*.5f,.65f,.12f),CityColor.Brick);
        for(int i=0;i<3;i++)Piece(root.transform,"Abstract shop glyph",new Vector3((i-1)*w*.13f,entry-.5f,-d*.5f-.44f),new Vector3(w*.09f,.16f,.035f),CityColor.Cream);
        StaticRoot(root);
    }
    void Facade(Transform root,float w,float d,float bottom,float top,int seed)
    {
        int floors=Mathf.Max(1,Mathf.FloorToInt((top-bottom)/Settings.FloorHeight));float storey=(top-bottom)/floors;
        for(int floor=0;floor<floors;floor++)
        {
            float y=bottom+floor*storey;
            Piece(root,"Floor band",new Vector3(0,y,0),new Vector3(w+.12f,Settings.BandHeight,d+.12f),CityColor.Cream);
            float wy=y+storey*.5f;float wh=Mathf.Min(Settings.WindowHeight,storey*.65f);if(wh<.3f)continue;
            for(int face=0;face<4;face++)
            {
                bool acrossX=face<2;float span=acrossX?w:d;int count=Mathf.Max(1,Mathf.FloorToInt(span/Settings.WindowSpacing));
                for(int i=0;i<count;i++)
                {
                    float along=(i-(count-1)*.5f)*Settings.WindowSpacing;float side=face%2==0?-1:1;
                    var pos=acrossX?new Vector3(along,wy,side*(d*.5f+.015f)):new Vector3(side*(w*.5f+.015f),wy,along);
                    var size=acrossX?new Vector3(Settings.WindowWidth,wh,.035f):new Vector3(.035f,wh,Settings.WindowWidth);
                    Piece(root,"Window",pos,size,(seed+i+face+(int)y)%5==0?CityColor.Amber:CityColor.Glass);
                }
            }
        }
    }
    public void Streets()
    {
        var c=tuning.City;float pitch=c.BlockSize+c.StreetWidth;
        var root=new GameObject("Curbs crossings and lane markings");root.transform.SetParent(transform,false);
        for(int x=0;x<c.Blocks;x++)for(int z=0;z<c.Blocks;z++)
        {
            var center=new Vector3((x-(c.Blocks-1)*.5f)*pitch,0,(z-(c.Blocks-1)*.5f)*pitch);
            foreach(float side in new[]{-1f,1f})
            {
                Piece(root.transform,"Curb",center+new Vector3(side*c.BlockSize*.5f,Settings.CurbHeight*.5f,0),new Vector3(Settings.CurbWidth,Settings.CurbHeight,c.BlockSize),CityColor.Cream);
                Piece(root.transform,"Curb",center+new Vector3(0,Settings.CurbHeight*.5f,side*c.BlockSize*.5f),new Vector3(c.BlockSize,Settings.CurbHeight,Settings.CurbWidth),CityColor.Cream);
            }
            for(float along=-c.BlockSize*.4f;along<c.BlockSize*.4f;along+=Settings.RoadDashSpacing)
            {
                Piece(root.transform,"Road dash",center+new Vector3(pitch*.5f,.012f,along),new Vector3(.12f,.02f,Settings.RoadDashLength),CityColor.Amber);
                Piece(root.transform,"Road dash",center+new Vector3(along,.012f,pitch*.5f),new Vector3(Settings.RoadDashLength,.02f,.12f),CityColor.Amber);
            }
        }
        for(int x=0;x<c.Blocks-1;x++)for(int z=0;z<c.Blocks-1;z++)
        {
            var center=new Vector3((x-(c.Blocks-2)*.5f)*pitch,.025f,(z-(c.Blocks-2)*.5f)*pitch);
            for(int i=0;i<Settings.CrosswalkStripes;i++)
            {
                float offset=(i-(Settings.CrosswalkStripes-1)*.5f)*c.StreetWidth/Settings.CrosswalkStripes;
                foreach(float side in new[]{-1f,1f})
                {
                    Piece(root.transform,"Crosswalk",center+new Vector3(offset,0,side*c.StreetWidth*.6f),new Vector3(Settings.CrosswalkStripe,.025f,1.8f),CityColor.Cream);
                    Piece(root.transform,"Crosswalk",center+new Vector3(side*c.StreetWidth*.6f,0,offset),new Vector3(1.8f,.025f,Settings.CrosswalkStripe),CityColor.Cream);
                }
            }
        }
        StaticRoot(root);
    }
    void StaticRoot(GameObject root)
    {
        staticRoots.Add(root);
        if(StaticMode==StaticGeometryMode.PerRootCombine)Combine(root);else pendingStatic.Add(root);
    }
    /// Call once all buildings and streets exist (and before any capture render). Batches or merges the
    /// never-moving geometry for StaticBatching/CityCombine; a no-op for the per-root and uncombined modes.
    public void FinishStaticGeometry()
    {
        if(pendingStatic.Count==0)return;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var filters=pendingStatic.Where(r=>r!=null).SelectMany(r=>r.GetComponentsInChildren<MeshFilter>()).Where(f=>f.sharedMesh!=null&&f.GetComponent<MeshRenderer>()!=null).ToArray();
        pendingStatic.Clear();
        if(StaticMode==StaticGeometryMode.StaticBatching&&filters.Length>0)
        {
            var source=new HashSet<Mesh>(filters.Select(f=>f.sharedMesh));
            StaticBatchingUtility.Combine(filters.Select(f=>f.gameObject).ToArray(),gameObject);
            // The batch meshes Unity creates here belong to this city; release them with it.
            foreach(var mesh in filters.Select(f=>f.sharedMesh).Distinct())if(mesh!=null&&!source.Contains(mesh))ownedMeshes.Add(mesh);
        }
        else if(StaticMode==StaticGeometryMode.CityCombine)
        {
            foreach(var group in filters.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial))
            {
                var parts=group.ToArray();var mesh=new Mesh{name="City static / "+group.Key.name,indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(parts.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
                var combined=new GameObject("City static geometry / "+group.Key.name){layer=parts[0].gameObject.layer};combined.transform.SetParent(transform,false);
                combined.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=combined.AddComponent<MeshRenderer>();renderer.sharedMaterial=group.Key;
                ownedMeshes.Add(mesh);cityRenderers.Add(renderer);
                // Piece GameObjects stay (the solid ones keep their colliders); only their drawing moves.
                foreach(var part in parts){var old=part.GetComponent<Renderer>();old.enabled=false;Destroy(old);Destroy(part);}
            }
        }
        StaticFinalizeMilliseconds+=watch.Elapsed.TotalMilliseconds;
    }
    public void Populate(List<BuildingPlacement> buildings)
    {
        Placements.AddRange(Settings.Generate(tuning.City,buildings));foreach(var placement in Placements)CreateProp(placement);
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
