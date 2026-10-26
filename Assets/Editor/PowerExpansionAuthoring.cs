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
    public static void PhaseC()
    {
        var art=Asset<PowerExpansionArt>("Assets/Resources/PowerExpansionArt.asset",a=>{});
        art.MorphBlade=Weapon("morph-blade",0);art.MorphHammer=Weapon("morph-hammer",1);art.MorphClaws=Weapon("morph-claws",2);art.PlasmaBlade=Weapon("plasma-blade",3);EditorUtility.SetDirty(art);
        var morph=Asset<CombatMorphingEffect>(Dir+"/CombatMorphingEffect.asset",a=>{});
        var plasma=Asset<PlasmaBladesEffect>(Dir+"/PlasmaBladesEffect.asset",a=>{});
        Power("combat-morphing","Combat Morphing","Adaptive arm weapons: V cycles Blade Arms, Hammer Fists, Extended Claws. LMB attacks; E/RMB remain universal melee.",morph,24,2.8f,.45f,10,500,CityColor.Metal);
        Power("plasma-blades","Plasma Blades","LMB: fast three-strike plasma combo. Hold G for 0.6s, release for a committed charged strike (+8 energy).",plasma,18,2.6f,.25f,10,260,CityColor.Cyan);
        AssetDatabase.SaveAssets();Debug.Log("PASS Phase C native fitted blade/hammer/claw/plasma meshes and hidden functional melee definitions authored.");EditorApplication.Exit(0);
    }
    static Mesh Weapon(string name,int kind)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        Action<Vector3,Vector3> box=(center,size)=>{
            int start=vertices.Count;
            for(int i=0;i<8;i++)vertices.Add(center+Vector3.Scale(new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1),size)*.5f);
            int[] faces={0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5};foreach(int index in faces)triangles.Add(start+index);
        };
        if(kind==1){box(new Vector3(0,0,.45f),new Vector3(1.3f,1.1f,1.5f));box(new Vector3(0,0,-.3f),new Vector3(.75f,.75f,.8f));}
        else
        {
            box(Vector3.zero,new Vector3(.65f,.5f,.9f));int blades=kind==2?3:1;
            for(int j=0;j<blades;j++)
            {
                float x=(j-(blades-1)*.5f)*.33f;int n=vertices.Count;
                vertices.AddRange(new[]{new Vector3(x-.16f,-.12f,.2f),new Vector3(x+.16f,-.12f,.2f),new Vector3(x+.12f,.12f,.2f),new Vector3(x-.12f,.12f,.2f),new Vector3(x,0,kind==3?2.7f:kind==2?2.3f:2)});
                foreach(int index in new[]{0,1,4,1,2,4,2,3,4,3,0,4,0,3,1,1,3,2})triangles.Add(n+index);
            }
        }
        var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path=Dir+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;
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
