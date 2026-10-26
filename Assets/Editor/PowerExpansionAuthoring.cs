#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class PowerExpansionAuthoring
{
    const string Dir="Assets/Resources/PowerExpansion";
    static T Asset<T>(string path,Action<T> configure) where T:ScriptableObject
    {
        var a=AssetDatabase.LoadAssetAtPath<T>(path);if(a==null){a=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(a,path);}configure(a);EditorUtility.SetDirty(a);return a;
    }
    public static void PhaseB()
    {
        if(!AssetDatabase.IsValidFolder(Dir))AssetDatabase.CreateFolder("Assets/Resources","PowerExpansion");
        var art=Asset<PowerExpansionArt>("Assets/Resources/PowerExpansionArt.asset",a=>{});
        art.WindArc=Arc("wind-arc",140,24,.78f);art.SoundRing=Arc("sound-ring",360,48,.91f);art.Slash=Arc("impact-slash",80,8,.45f);
        var shader=Shader.Find("Overpowered/Expansion Unlit");if(shader==null)throw new Exception("Expansion shader not imported");
        art.EffectMaterial=Material("effect-shared",shader,false);art.Hologram=Material("hologram-shared",shader,true);EditorUtility.SetDirty(art);
        Asset<PowerExpansionTuning>("Assets/Resources/PowerExpansionTuning.asset",a=>{});
        var wind=Asset<AerokinesisEffect>(Dir+"/AerokinesisEffect.asset",a=>{});
        var sonic=Asset<SonicManipulationEffect>(Dir+"/SonicManipulationEffect.asset",a=>{});
        Power("aerokinesis","Aerokinesis","WIND BLAST / directional gust, finite displacement and windup interruption. Heavy enemies resist the shove.",wind,20,14,.9f,12,450,CityColor.Cyan);
        Power("sonic-manipulation","Sonic Manipulation","SONIC BURST / directional concussion and brief stagger. Interruption has a refractory window, especially on bosses.",sonic,18,11,1.2f,14,180,CityColor.UiPurple);
        AssetDatabase.SaveAssets();Debug.Log("PASS Phase B creates two real hidden primaries, native shared VFX meshes/materials and data; existing selectable eight remain.");EditorApplication.Exit(0);
    }
    public static PowerDefinition Power(string id,string name,string description,PowerEffect effect,float damage,float range,float cooldown,float energy,float force,CityColor color)
        =>Asset<PowerDefinition>("Assets/Resources/Powers/"+id+".asset",a=>{a.Id=id;a.DisplayName=name;a.Description=description;a.Effect=effect;a.Role=PowerRole.Combat;a.Selectable=false;a.InitiallyUnlocked=true;a.Damage=damage;a.Range=range;a.Radius=2;a.Cooldown=cooldown;a.ResourceCost=energy;a.Force=force;a.Charges=3;a.ChargeRecharge=1.5f;a.PaletteColor=color;a.CastingPresentation=true;a.MenuIcon=MenuGlyph.Star;});
    static Material Material(string name,Shader shader,bool holo)
    {
        string path=Dir+"/"+name+".mat";var a=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(a==null){a=new Material(shader);AssetDatabase.CreateAsset(a,path);}a.shader=shader;
        var color=Resources.Load<CityPalette>("CityPalette").Colors[(int)CityColor.Cyan];color.a=holo? .38f:.65f;a.SetColor("_Color",color);a.SetFloat("_Hologram",holo?1:0);EditorUtility.SetDirty(a);return a;
    }
    public static Mesh Arc(string name,float degrees,int segments,float inner)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<=segments;i++)
        {
            float angle=(-degrees*.5f+degrees*i/segments)*Mathf.Deg2Rad;var direction=new Vector3(Mathf.Sin(angle),Mathf.Cos(angle),0);
            vertices.Add(direction*inner);vertices.Add(direction);
            if(i==segments)continue;int n=i*2;triangles.AddRange(new[]{n,n+1,n+2,n+1,n+3,n+2});
        }
        var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path=Dir+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;
    }
}
#endif
