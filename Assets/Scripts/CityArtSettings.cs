using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum CityPropKind { Lamp, Bench, Trash, Hydrant, BusStop, Newspaper, Planter, Car, HVAC, Vent, WaterTower, Antenna, RoofAccess, Billboard }
// How prop geometry becomes renderers. Props are Rigidbodies, so they can be instanced but never static-batched.
public enum PropMeshMode
{
    PerPropCombine,           // legacy (old CombineMeshes=true): a NEW mesh per prop per material, so nothing can batch
    SharedPerKind,            // one mesh per recipe shared by every instance; one renderer per prop, one submesh per material
    SharedPerKindPerMaterial, // shared per recipe and material; one child renderer per material (legacy hierarchy)
    Uncombined                // legacy (old CombineMeshes=false): one primitive renderer per piece
}
// How buildings and streets (never moving, unique per seed) are drawn; applied by CityArt.FinishStaticGeometry.
public enum StaticGeometryMode
{
    PerRootCombine, // legacy (old CombineMeshes=true): per building/street root, per material
    StaticBatching, // primitive pieces kept, StaticBatchingUtility over the whole static set
    CityCombine,    // one mesh per material across the whole static set
    Uncombined,     // legacy (old CombineMeshes=false): one primitive renderer per piece
    BuildingMeshes  // world pass: pieces written straight into one mesh per building (and per 64 m chunk for ground/streets/
                    // structures) per material and layer, no per-piece GameObjects; then StaticBatchingUtility per district root
}
/// A static structure built from primitive pieces (trees, containers, cranes, landmarks). Pure data: CityArt places any recipe.
[Serializable] public sealed class RecipePiece
{
    public PrimitiveType Type=PrimitiveType.Cube;
    [Tooltip("Piece centre relative to the recipe origin (ground level).")] public Vector3 Position;
    public Vector3 Size=Vector3.one, Euler;
    public CityColor Color;
    [Tooltip("Colour comes from the recipe's Variants (seeded per placement) instead of Color.")] public bool Variant;
    public bool Solid, Detail;
}
[Serializable] public sealed class StructureRecipe
{
    public string Name;
    public CityColor[] Variants=new CityColor[0];
    public RecipePiece[] Pieces=new RecipePiece[0];
}
/// Camera/fog/cull tuning for the large world. Layers: 0 buildings/ground/landmarks (far clip), Detail (windows, bands,
/// signs, kerbs, lane marks), Props (street props), Actors (NPC bodies), Backdrop (distant skyline).
[Serializable] public sealed class WorldRendering
{
    public float FarClip=2300;
    public bool Fog=true;
    public FogMode FogMode=FogMode.Exponential;
    public float FogDensity=.0016f;
    public CityColor FogColor=CityColor.Haze;
    public int DetailLayer=8, PropLayer=9, ActorLayer=10, BackdropLayer=11;
    [Tooltip("Camera.layerCullDistances for the layers above; 0 = far clip.")] public float DetailCull=220, PropCull=140, ActorCull=170, BackdropCull=0;
    public bool SphericalCull=true;
    [Tooltip("Ground, streets and structures are merged per chunk of this size (BuildingMeshes mode).")] public float ChunkSize=64;
}
[Serializable] public sealed class BuildingStyle
{
    public string Name;
    public CityColor Wall;
    public Vector2 Footprint=Vector2.one;
    public float SetbackAt=.65f, UpperWidth=1f;
    [Tooltip("Window spacing multiplier (sheds: few windows).")] public float WindowSpacingScale=1f;
}
[Serializable] public sealed class ArtPlacementRule
{
    public CityPropKind Kind;
    public bool Rooftop;
    [Range(0,1)] public float Chance=1;
    public Vector2[] Anchors;
    public float Yaw, Jitter=.25f;
}
[Serializable] public sealed class ArtPlacement
{
    public CityPropKind Kind;
    public Vector3 Position;
    public float Yaw;
    public bool Rooftop;
}
[Serializable] public sealed class PropShape
{
    public CityPropKind Kind;public Vector3 Size;public bool Destructible=true;public float Mass=80;
}
[CreateAssetMenu(menuName="Overpowered/City Art Settings")]
public sealed class CityArtSettings : ScriptableObject
{
    public CityPalette Palette;
    public PropMeshMode PropMeshes=PropMeshMode.SharedPerKind;
    public StaticGeometryMode StaticGeometry=StaticGeometryMode.BuildingMeshes;
    public WorldRendering Rendering=new WorldRendering();
    public float FloorHeight=3.2f, WindowSpacing=2.4f, WindowWidth=1.1f, WindowHeight=1.45f, BandHeight=.12f;
    public float EntranceWidth=2f, EntranceDepth=.65f, ParapetHeight=.6f, ParapetThickness=.18f;
    public float CurbHeight=.18f, CurbWidth=.22f, RoadDashLength=2.5f, RoadDashSpacing=5f, CrosswalkStripe=.45f;
    public int CrosswalkStripes=7;
    public BuildingStyle[] Styles={
        new BuildingStyle{Name="Brick warehouse",Wall=CityColor.Brick,Footprint=new Vector2(1,.94f)},
        new BuildingStyle{Name="Sandstone terraces",Wall=CityColor.Sand,Footprint=new Vector2(.92f,1),UpperWidth=.72f},
        new BuildingStyle{Name="Teal offices",Wall=CityColor.Teal,Footprint=new Vector2(.88f,.9f),UpperWidth=.84f,SetbackAt=.48f},
        new BuildingStyle{Name="Slate tower",Wall=CityColor.Slate,Footprint=new Vector2(.9f,.82f),UpperWidth=.76f,SetbackAt=.78f},
        new BuildingStyle{Name="Glass high-rise",Wall=CityColor.Glass,Footprint=new Vector2(.92f,.92f),UpperWidth=.8f,SetbackAt=.72f},
        new BuildingStyle{Name="Harbour shed",Wall=CityColor.Metal,Footprint=Vector2.one,UpperWidth=1,SetbackAt=1,WindowSpacingScale=3},
        new BuildingStyle{Name="Townhouse",Wall=CityColor.Cream,Footprint=new Vector2(.94f,.9f),UpperWidth=.9f,SetbackAt=.62f}};
    public PropShape[] Shapes={
        new PropShape{Kind=CityPropKind.Lamp,Size=new Vector3(.7f,4.2f,.7f)},new PropShape{Kind=CityPropKind.Bench,Size=new Vector3(2.2f,.95f,.8f)},
        new PropShape{Kind=CityPropKind.Trash,Size=new Vector3(.65f,.95f,.65f)},new PropShape{Kind=CityPropKind.Hydrant,Size=new Vector3(.7f,.85f,.5f),Mass=65},
        new PropShape{Kind=CityPropKind.BusStop,Size=new Vector3(1.2f,2.8f,3.8f),Mass=250},new PropShape{Kind=CityPropKind.Newspaper,Size=new Vector3(.65f,1.1f,.6f),Mass=45},
        new PropShape{Kind=CityPropKind.Planter,Size=new Vector3(1.4f,1.4f,1.4f),Mass=100},new PropShape{Kind=CityPropKind.Car,Size=new Vector3(1.8f,1.5f,4),Mass=400},
        new PropShape{Kind=CityPropKind.HVAC,Size=new Vector3(1.5f,1,1.2f),Mass=80},new PropShape{Kind=CityPropKind.Vent,Size=new Vector3(.6f,1.2f,.6f),Mass=45},
        new PropShape{Kind=CityPropKind.WaterTower,Size=new Vector3(2,3.8f,2),Mass=300},new PropShape{Kind=CityPropKind.Antenna,Size=new Vector3(1,3,1),Mass=45},
        new PropShape{Kind=CityPropKind.RoofAccess,Size=new Vector3(1.8f,2.2f,1.8f),Destructible=false},new PropShape{Kind=CityPropKind.Billboard,Size=new Vector3(2.8f,2.8f,.5f),Mass=100}};
    public ArtPlacementRule[] Rules={
        new ArtPlacementRule{Kind=CityPropKind.Lamp,Anchors=new[]{new Vector2(-.44f,-.44f),new Vector2(.44f,.44f)}},
        new ArtPlacementRule{Kind=CityPropKind.Bench,Anchors=new[]{new Vector2(0,-.45f)}},new ArtPlacementRule{Kind=CityPropKind.Trash,Anchors=new[]{new Vector2(.17f,-.45f)}},
        new ArtPlacementRule{Kind=CityPropKind.Hydrant,Anchors=new[]{new Vector2(.44f,-.2f)}},new ArtPlacementRule{Kind=CityPropKind.BusStop,Anchors=new[]{new Vector2(-.45f,-.25f)},Chance=.85f},
        new ArtPlacementRule{Kind=CityPropKind.Newspaper,Anchors=new[]{new Vector2(-.45f,.2f)}},new ArtPlacementRule{Kind=CityPropKind.Planter,Anchors=new[]{new Vector2(.44f,.2f)}},
        new ArtPlacementRule{Kind=CityPropKind.Car,Anchors=new[]{new Vector2(.56f,-.16f),new Vector2(-.56f,.16f)},Jitter=.2f},
        new ArtPlacementRule{Kind=CityPropKind.HVAC,Rooftop=true,Anchors=new[]{new Vector2(-.32f,-.32f)},Jitter=0},
        new ArtPlacementRule{Kind=CityPropKind.Vent,Rooftop=true,Anchors=new[]{new Vector2(-.32f,.12f)},Jitter=0},
        new ArtPlacementRule{Kind=CityPropKind.WaterTower,Rooftop=true,Anchors=new[]{new Vector2(.32f,.32f)},Chance=.45f,Jitter=0},
        new ArtPlacementRule{Kind=CityPropKind.Antenna,Rooftop=true,Anchors=new[]{new Vector2(.32f,-.32f)},Chance=.7f,Jitter=0},
        new ArtPlacementRule{Kind=CityPropKind.RoofAccess,Rooftop=true,Anchors=new[]{new Vector2(-.32f,.32f)},Jitter=0},
        new ArtPlacementRule{Kind=CityPropKind.Billboard,Rooftop=true,Anchors=new[]{new Vector2(.015f,-.36f)},Chance=.28f,Jitter=0}};
    public List<StructureRecipe> Structures=DefaultStructures();
    public bool UseAuthoredPlacements;
    public List<ArtPlacement> AuthoredPlacements=new List<ArtPlacement>();
    public int StyleIndex(int seed,int building)=>new System.Random(unchecked(seed*397+building*7919)).Next(Styles.Length);
    public int StyleOf(CitySettings city,BuildingPlacement b,int index)=>b.Style>=0&&b.Style<Styles.Length?b.Style:StyleIndex(city.Seed,index);
    public StructureRecipe Recipe(string name)=>Structures.Find(r=>r.Name==name);
    /// Street furniture per block (districts with StreetProps), rooftop equipment per building (districts with RooftopProps),
    /// then the plan's feature props (park paths, dock yard). Deterministic for a seed.
    public List<ArtPlacement> Generate(CitySettings city,CityPlan plan)
    {
        if(UseAuthoredPlacements)return new List<ArtPlacement>(AuthoredPlacements);
        var rng=new System.Random(unchecked(city.Seed^0x13572468));var result=new List<ArtPlacement>();
        var buildings=plan.Buildings;
        foreach(var rule in Rules)
        {
            int count=rule.Rooftop?buildings.Count:plan.Blocks.Count;
            for(int i=0;i<count;i++)
            {
                Vector3 center;Vector2 span;
                if(rule.Rooftop)
                {
                    var b=buildings[i];if(b.District>=0&&!plan.Districts[b.District].RooftopProps)continue;
                    var s=Styles[StyleOf(city,b,i)];center=b.Position+Vector3.up*b.Size.y*.5f;span=Vector2.Scale(new Vector2(b.Size.x,b.Size.z),s.Footprint)*s.UpperWidth;
                }
                else
                {
                    var block=plan.Blocks[i];if(!plan.Districts[block.District].StreetProps)continue;
                    center=new Vector3(block.Area.center.x,city.SidewalkHeight,block.Area.center.y);span=block.Area.size;
                }
                foreach(var anchor in rule.Anchors)if(rng.NextDouble()<=rule.Chance)
                {
                    var offset=Vector2.Scale(anchor,span)+new Vector2((float)rng.NextDouble()-.5f,(float)rng.NextDouble()-.5f)*rule.Jitter;
                    var position=center+new Vector3(offset.x,0,offset.y);if(rule.Kind==CityPropKind.Car)position.y=0;
                    result.Add(new ArtPlacement{Kind=rule.Kind,Position=position,Yaw=rule.Yaw,Rooftop=rule.Rooftop});
                }
            }
        }
        result.AddRange(plan.FeatureProps.Select(p=>new ArtPlacement{Kind=p.Kind,Position=p.Position,Yaw=p.Yaw,Rooftop=p.Rooftop}));
        return result;
    }
    static RecipePiece P(PrimitiveType t,float x,float y,float z,float sx,float sy,float sz,CityColor c,bool solid=false,bool detail=false,float yaw=0,bool variant=false)
        =>new RecipePiece{Type=t,Position=new Vector3(x,y,z),Size=new Vector3(sx,sy,sz),Euler=new Vector3(0,yaw,0),Color=c,Solid=solid,Detail=detail,Variant=variant};
    const PrimitiveType Box=PrimitiveType.Cube, Cyl=PrimitiveType.Cylinder;
    static List<StructureRecipe> DefaultStructures()=>new List<StructureRecipe>
    {
        new StructureRecipe{Name="Street tree",Pieces=new[]{P(Cyl,0,1.2f,0,.32f,2.4f,.32f,CityColor.Wood,true),P(Box,0,3.3f,0,2.4f,2f,2.4f,CityColor.Leaf,yaw:45),P(Box,0,4.3f,0,1.6f,1.3f,1.6f,CityColor.Leaf,yaw:20)}},
        new StructureRecipe{Name="Park tree",Pieces=new[]{P(Cyl,0,1.6f,0,.5f,3.2f,.5f,CityColor.Wood,true),P(Box,0,4.4f,0,4.2f,3.2f,4.2f,CityColor.Leaf,yaw:45),P(Box,0,6.2f,0,3f,2.2f,3f,CityColor.Lawn,yaw:15)}},
        new StructureRecipe{Name="Pine tree",Pieces=new[]{P(Cyl,0,1.1f,0,.42f,2.2f,.42f,CityColor.Wood,true),P(Box,0,2.8f,0,3.4f,1.7f,3.4f,CityColor.Leaf,yaw:45),P(Box,0,4.2f,0,2.5f,1.5f,2.5f,CityColor.Leaf,yaw:20),P(Box,0,5.4f,0,1.5f,1.4f,1.5f,CityColor.Leaf,yaw:45)}},
        new StructureRecipe{Name="Shipping container",Variants=new[]{CityColor.Red,CityColor.Blue,CityColor.Teal,CityColor.Amber,CityColor.Brick,CityColor.Cream},
            Pieces=new[]{P(Box,0,1.3f,0,2.44f,2.59f,6.06f,CityColor.Red,true,variant:true),P(Box,0,1.3f,-3.04f,2.2f,2.3f,.04f,CityColor.Metal,detail:true),P(Box,0,2.62f,0,2.2f,.04f,5.6f,CityColor.Metal,detail:true)}},
        new StructureRecipe{Name="Gantry crane",Pieces=new[]{
            P(Box,-6,13,-4.5f,1,26,1,CityColor.Amber,true),P(Box,6,13,-4.5f,1,26,1,CityColor.Amber,true),P(Box,-6,13,4.5f,1,26,1,CityColor.Amber,true),P(Box,6,13,4.5f,1,26,1,CityColor.Amber,true),
            P(Box,-6,25.5f,0,1.2f,1.4f,10.4f,CityColor.Amber,true),P(Box,6,25.5f,0,1.2f,1.4f,10.4f,CityColor.Amber,true),P(Box,-6,8,0,.6f,.6f,9,CityColor.Amber,detail:true),P(Box,6,8,0,.6f,.6f,9,CityColor.Amber,detail:true),
            P(Box,0,27,0,13.2f,1.6f,3,CityColor.Amber,true),P(Box,0,27,-16,2.2f,1.3f,36,CityColor.Amber,true),P(Box,0,27,9,2.2f,1.3f,8,CityColor.Amber,true),
            P(Box,0,30,4,5,4,6,CityColor.Cream,true),P(Box,0,30.2f,1,5.1f,1,.2f,CityColor.Glass,detail:true),P(Box,0,29.5f,11,3,4,3,CityColor.Slate,true),
            P(Box,0,20,-26,.15f,13,.15f,CityColor.Metal,detail:true),P(Box,0,13,-26,1.4f,1,1.4f,CityColor.Red,detail:true)}},
        new StructureRecipe{Name="Meridian Spire",Pieces=new[]{
            P(Box,0,3,0,26,6,26,CityColor.Slate,true),P(Box,0,6.2f,0,26.4f,.4f,26.4f,CityColor.Cream,detail:true),
            P(Box,0,36,0,19,60,19,CityColor.Teal,true),P(Box,0,21,0,19.4f,.6f,19.4f,CityColor.Amber,detail:true),P(Box,0,41,0,19.4f,.6f,19.4f,CityColor.Amber,detail:true),P(Box,0,61,0,19.4f,.6f,19.4f,CityColor.Amber,detail:true),
            P(Box,0,84,0,14,36,14,CityColor.Glass,true),P(Box,0,66.2f,0,19.6f,.4f,19.6f,CityColor.Cream,true),P(Box,0,93,0,14.4f,.6f,14.4f,CityColor.Amber,detail:true),
            P(Box,0,110,0,9,16,9,CityColor.Teal,true),P(Box,0,102.2f,0,14.6f,.4f,14.6f,CityColor.Cream,true),P(Box,0,119,0,11,2,11,CityColor.Amber,true),
            P(Cyl,0,136,0,1.3f,32,1.3f,CityColor.Metal,true),P(Box,0,153,0,2.2f,2.2f,2.2f,CityColor.Red)}},
        new StructureRecipe{Name="Harbour Light",Pieces=new[]{
            P(Cyl,0,2,0,12,4,12,CityColor.Slate,true),P(Cyl,0,9,0,7,10,7,CityColor.Cream,true),P(Cyl,0,19,0,6.6f,10,6.6f,CityColor.Red,true),P(Cyl,0,29,0,6.2f,10,6.2f,CityColor.Cream,true),
            P(Cyl,0,39,0,5.8f,10,5.8f,CityColor.Red,true),P(Cyl,0,44.5f,0,8.4f,1,8.4f,CityColor.Metal,true),P(Cyl,0,47,0,4.6f,4,4.6f,CityColor.Amber,true),P(Cyl,0,50,0,5.6f,2,5.6f,CityColor.Red,true),
            P(Box,0,52,0,.4f,2,.4f,CityColor.Metal)}},
        new StructureRecipe{Name="Lookout Tower",Pieces=new[]{
            P(Box,0,1.5f,0,14,3,14,CityColor.Slate,true),P(Cyl,0,37,0,6,68,6,CityColor.Cream,true),P(Cyl,0,69.5f,0,14,1,14,CityColor.Amber,true),
            P(Cyl,0,74,0,18,7,18,CityColor.Glass,true),P(Cyl,0,78,0,19,1.2f,19,CityColor.Cream,true),P(Cyl,0,90,0,1,24,1,CityColor.Metal),P(Box,0,102.8f,0,1.8f,1.8f,1.8f,CityColor.Red)}}
    };
}