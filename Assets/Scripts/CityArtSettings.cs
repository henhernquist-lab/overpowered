using System;
using System.Collections.Generic;
using UnityEngine;

public enum CityPropKind { Lamp, Bench, Trash, Hydrant, BusStop, Newspaper, Planter, Car, HVAC, Vent, WaterTower, Antenna, RoofAccess, Billboard }
[Serializable] public sealed class BuildingStyle
{
    public string Name;
    public CityColor Wall;
    public Vector2 Footprint=Vector2.one;
    public float SetbackAt=.65f, UpperWidth=1f;
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
    public bool CombineMeshes=true;
    public float FloorHeight=3.2f, WindowSpacing=2.4f, WindowWidth=1.1f, WindowHeight=1.45f, BandHeight=.12f;
    public float EntranceWidth=2f, EntranceDepth=.65f, ParapetHeight=.6f, ParapetThickness=.18f;
    public float CurbHeight=.18f, CurbWidth=.22f, RoadDashLength=2.5f, RoadDashSpacing=5f, CrosswalkStripe=.45f;
    public int CrosswalkStripes=7;
    public BuildingStyle[] Styles={
        new BuildingStyle{Name="Brick warehouse",Wall=CityColor.Brick,Footprint=new Vector2(1,.94f)},
        new BuildingStyle{Name="Sandstone terraces",Wall=CityColor.Sand,Footprint=new Vector2(.92f,1),UpperWidth=.72f},
        new BuildingStyle{Name="Teal offices",Wall=CityColor.Teal,Footprint=new Vector2(.88f,.9f),UpperWidth=.84f,SetbackAt=.48f},
        new BuildingStyle{Name="Slate tower",Wall=CityColor.Slate,Footprint=new Vector2(.9f,.82f),UpperWidth=.76f,SetbackAt=.78f}};
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
    public bool UseAuthoredPlacements;
    public List<ArtPlacement> AuthoredPlacements=new List<ArtPlacement>();
    public int StyleIndex(int seed,int building)=>new System.Random(unchecked(seed*397+building*7919)).Next(Styles.Length);
    public List<ArtPlacement> Generate(CitySettings city,List<BuildingPlacement> buildings)
    {
        if(UseAuthoredPlacements)return new List<ArtPlacement>(AuthoredPlacements);
        var rng=new System.Random(unchecked(city.Seed^0x13572468));var result=new List<ArtPlacement>();
        foreach(var rule in Rules)
        {
            int count=rule.Rooftop?buildings.Count:city.Blocks*city.Blocks;
            for(int i=0;i<count;i++)
            {
                Vector3 center;Vector2 span;
                if(rule.Rooftop)
                {var b=buildings[i];var s=Styles[StyleIndex(city.Seed,i)];center=b.Position+Vector3.up*b.Size.y*.5f;span=Vector2.Scale(new Vector2(b.Size.x,b.Size.z),s.Footprint)*s.UpperWidth;}
                else {float pitch=city.BlockSize+city.StreetWidth;center=new Vector3((i/city.Blocks-(city.Blocks-1)*.5f)*pitch,city.SidewalkHeight,(i%city.Blocks-(city.Blocks-1)*.5f)*pitch);span=Vector2.one*city.BlockSize;}
                foreach(var anchor in rule.Anchors)if(rng.NextDouble()<=rule.Chance)
                {
                    var offset=Vector2.Scale(anchor,span)+new Vector2((float)rng.NextDouble()-.5f,(float)rng.NextDouble()-.5f)*rule.Jitter;
                    var position=center+new Vector3(offset.x,0,offset.y);if(rule.Kind==CityPropKind.Car)position.y=0;
                    result.Add(new ArtPlacement{Kind=rule.Kind,Position=position,Yaw=rule.Yaw,Rooftop=rule.Rooftop});
                }
            }
        }
        return result;
    }
}
