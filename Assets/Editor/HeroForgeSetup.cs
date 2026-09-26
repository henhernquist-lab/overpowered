using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class HeroForgeSetup
{
    [MenuItem("Overpowered/Forge/Create missing assets")]
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources/Forge/Heroes");Directory.CreateDirectory("Assets/Resources/Forge/Synergies");Directory.CreateDirectory("Assets/Resources/Forge/Effects");AssetDatabase.Refresh();
        var powers=Resources.LoadAll<PowerDefinition>("Powers");PowerDefinition Power(string id)=>powers.Single(p=>p.Id==id);
        var catalog=AssetDatabase.LoadAssetAtPath<ForgeCatalog>("Assets/Resources/ForgeCatalog.asset");
        if(catalog!=null){Expand(catalog);return;}
        catalog=ScriptableObject.CreateInstance<ForgeCatalog>();
        var model=Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning").Model;
        HeroDefinition Hero(string id,string title,float width,CityColor primary,CityColor secondary,string a,string b)
        {
            var d=ScriptableObject.CreateInstance<HeroDefinition>();d.Id=id;d.DisplayName=title;d.CharacterPrefab=model;
            d.VisualScale=new Vector3(width,1,width);d.Primary=primary;d.Secondary=secondary;d.AvailablePowers=powers;
            d.DefaultA=Power(a);d.DefaultB=Power(b);AssetDatabase.CreateAsset(d,"Assets/Resources/Forge/Heroes/"+id+".asset");return d;
        }
        catalog.Heroes=new[]{Hero("vector","VECTOR",1,CityColor.Blue,CityColor.Cyan,"flight","strength"),
            Hero("titan","TITAN",1.2f,CityColor.Teal,CityColor.Amber,"strength","telekinesis"),
            Hero("nova","NOVA",.92f,CityColor.UiPurple,CityColor.Red,"fire","ice")};
        var slam=ScriptableObject.CreateInstance<SonicSlamEffect>();AssetDatabase.CreateAsset(slam,"Assets/Resources/Forge/Effects/sonic-slam.asset");
        var synergy=ScriptableObject.CreateInstance<PowerSynergyDefinition>();synergy.Id="sonic-slam";synergy.DisplayName="Sonic Slam";synergy.PowerA=Power("flight");synergy.PowerB=Power("strength");synergy.Effect=slam;
        synergy.Description="Rise. Drop. Send the street flying.";synergy.Cooldown=35;AssetDatabase.CreateAsset(synergy,"Assets/Resources/Forge/Synergies/sonic-slam.asset");
        catalog.Synergies=new[]{synergy};AssetDatabase.CreateAsset(catalog,"Assets/Resources/ForgeCatalog.asset");Expand(catalog);AssetDatabase.SaveAssets();
    }
    static void Expand(ForgeCatalog catalog)
    {
        var entries=catalog.Synergies.ToList();
        T Effect<T>(string id,Action<T> configure=null) where T:SynergyEffect
        {
            string path="Assets/Resources/Forge/Effects/"+id+".asset";
            var effect=AssetDatabase.LoadAssetAtPath<T>(path);
            if(effect!=null)return effect;
            effect=ScriptableObject.CreateInstance<T>();configure?.Invoke(effect);AssetDatabase.CreateAsset(effect,path);return effect;
        }
        void Add(string id,string title,string a,string b,SynergyEffect effect,string description,Action<PowerSynergyDefinition> configure)
        {
            if(entries.Any(d=>d.Id==id))return;
            var d=ScriptableObject.CreateInstance<PowerSynergyDefinition>();d.Id=id;d.DisplayName=title;d.Description=description;
            d.PowerA=Resources.Load<PowerDefinition>("Powers/"+a);d.PowerB=Resources.Load<PowerDefinition>("Powers/"+b);d.Effect=effect;configure(d);
            AssetDatabase.CreateAsset(d,"Assets/Resources/Forge/Synergies/"+id+".asset");entries.Add(d);
        }
        Add("phoenix-dive","Phoenix Dive","flight","fire",Effect<SonicSlamEffect>("phoenix-dive",e=>e.Targeted=true),"Launch, dive ahead, ignite the landing.",d=>{d.Cooldown=40;d.Primary=CityColor.Fire;d.Secondary=CityColor.Amber;d.BurnSeconds=4;d.Damage=55;d.Force=1800;});
        Add("frostwake","Frostwake","flight","ice",Effect<FrostwakeEffect>("frostwake"),"Burn flight fuel. Freeze enemies you pass.",d=>{d.Cooldown=25;d.Primary=CityColor.Cyan;d.Duration=1.1f;d.DiveSpeed=26;d.Radius=3;d.Damage=12;d.FreezeSeconds=2;});
        Add("orbit-throw","Orbit Throw","flight","telekinesis",Effect<OrbitThrowEffect>("orbit-throw"),"Gather loose props. Press C again to volley.",d=>{d.Cooldown=30;d.Primary=CityColor.Blue;d.Range=9;d.Duration=2;d.Force=1800;d.Damage=35;});
        Add("thermal-shock","Thermal Shock","fire","ice",Effect<ThermalShockEffect>("thermal-shock"),"Hot/cold blast. Bonus against burning or frozen targets.",d=>{d.Cooldown=30;d.Primary=CityColor.Fire;d.Secondary=CityColor.Cyan;d.Radius=4;d.Damage=25;d.Force=1500;d.FreezeSeconds=.6f;});
        Add("meteor-punch","Meteor Punch","fire","strength",Effect<MeteorPunchEffect>("meteor-punch"),"Charge a flaming fist. Deliver an explosive blow.",d=>{d.Cooldown=40;d.Primary=CityColor.Fire;d.Secondary=CityColor.Amber;d.LiftSeconds=.6f;d.Radius=4;d.Damage=75;d.Force=3400;d.BurnSeconds=4;});
        Add("inferno-orbit","Inferno Orbit","fire","telekinesis",Effect<OrbitThrowEffect>("inferno-orbit",e=>e.Ignite=true),"Ignite orbiting debris. Press C again to release.",d=>{d.Cooldown=45;d.Primary=CityColor.Fire;d.Secondary=CityColor.Red;d.Range=9;d.Duration=2;d.Force=1600;d.Damage=40;d.Radius=3;d.BurnSeconds=4;});
        Add("glacier-fist","Glacier Fist","ice","strength",Effect<GlacierFistEffect>("glacier-fist"),"Ice-coated fists. Heavy hits knock back and freeze.",d=>{d.Cooldown=30;d.Primary=CityColor.Cyan;d.Duration=6;d.Radius=.4f;d.FreezeSeconds=1.5f;d.MeleeMultiplier=1.65f;});
        Add("cryo-crush","Cryo Crush","ice","telekinesis",Effect<LiftSlamEffect>("cryo-crush",e=>{e.EnemyOnly=true;e.Freeze=true;}),"Suspend an enemy in ice. Slam them into the ground.",d=>{d.Cooldown=30;d.Primary=CityColor.Cyan;d.OrbitRadius=5;d.LiftSeconds=1.1f;d.Damage=35;d.Force=2000;d.Radius=4;});
        Add("meteor-slam","Meteor Slam","strength","telekinesis",Effect<LiftSlamEffect>("meteor-slam"),"Lift an enemy or prop. Slam it down with a shockwave.",d=>{d.Cooldown=35;d.Primary=CityColor.Amber;d.OrbitRadius=6;d.LiftSeconds=1.2f;d.Damage=50;d.Force=2800;d.Radius=7;});
        catalog.Synergies=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
    }
    public static void Batch(){Create();EditorApplication.Exit(0);}
}
