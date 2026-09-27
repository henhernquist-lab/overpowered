using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// Authors data assets once, then leaves designer-tuned values alone. Repeated Configure calls are byte-identical.
public static class LightingSetup
{
    const string Dir="Assets/Resources/Lighting/";
    static T ExistingOrCreate<T>(string path,Action<T> seed) where T:ScriptableObject
    {
        var asset=AssetDatabase.LoadAssetAtPath<T>(path);
        if(asset!=null)return asset;
        asset=ScriptableObject.CreateInstance<T>();seed(asset);AssetDatabase.CreateAsset(asset,path);return asset;
    }
    static void Key(LightingCycle cycle,LightingPreset preset,float time)
    {cycle.Keys=new[]{new LightingKey{Time=time,Preset=preset}};cycle.StartTime=time;cycle.DurationSeconds=0;cycle.Loop=false;}
    public static void Configure()
    {
        Directory.CreateDirectory(Dir);AssetDatabase.Refresh();
        var palette=AssetDatabase.LoadAssetAtPath<CityPalette>("Assets/Resources/CityPalette.asset");
        if(palette==null)throw new InvalidOperationException("CityPalette.asset is required");
        var defaults=ScriptableObject.CreateInstance<CityPalette>();
        if(palette.Colors.Length<defaults.Colors.Length){Array.Resize(ref palette.Colors,defaults.Colors.Length);for(int i=27;i<defaults.Colors.Length;i++)palette.Colors[i]=defaults.Colors[i];EditorUtility.SetDirty(palette);}
        if(palette.Emissive==null||palette.Emissive.Length==0){palette.Emissive=defaults.Emissive;EditorUtility.SetDirty(palette);}
        UnityEngine.Object.DestroyImmediate(defaults);
        var art=AssetDatabase.LoadAssetAtPath<CityArtSettings>("Assets/Resources/CityArtSettings.asset");
        if(art==null)throw new InvalidOperationException("CityArtSettings.asset is required");
        bool artChanged=false;
        foreach(var recipe in art.Structures)
        {
            if(recipe==null||recipe.Pieces==null)continue;
            bool landmark=recipe.Name=="Meridian Spire"||recipe.Name=="Harbour Light"||recipe.Name=="Lookout Tower";
            if(!landmark)continue;
            foreach(var piece in recipe.Pieces)
            {
                if(piece.Color==CityColor.Amber){piece.Color=recipe.Name=="Harbour Light"?CityColor.LampGlow:CityColor.LitWindow;artChanged=true;}
                if(piece.Color==CityColor.Red&&piece.Position.y>100){piece.Color=CityColor.Beacon;artChanged=true;}
            }
        }
        if(artChanged)EditorUtility.SetDirty(art);
        var day=ExistingOrCreate<LightingPreset>(Dir+"Day.asset",p=>{});
        var dusk=ExistingOrCreate<LightingPreset>(Dir+"Dusk.asset",p=>{p.Elevation=12;p.Azimuth=-65;p.LightColor=new Color(1f,.65f,.45f);p.Intensity=.95f;p.AmbientTint=new Color(.82f,.7f,.64f);p.AmbientFloor=new Color(.04f,.035f,.04f);p.Reflections=.65f;p.FogColor=CityColor.DuskHaze;p.SkyTint=new Color(.9f,.55f,.43f);p.SkyGround=new Color(.4f,.26f,.29f);p.SkyExposure=1.1f;p.Atmosphere=1.2f;p.SunSize=.06f;p.Glow=.55f;p.StreetLights=.6f;});
        var night=ExistingOrCreate<LightingPreset>(Dir+"Night.asset",p=>{p.Elevation=38;p.Azimuth=140;p.LightColor=new Color(.55f,.7f,1f);p.Intensity=.32f;p.AmbientTint=new Color(.34f,.42f,.56f);p.AmbientFloor=new Color(.035f,.045f,.065f);p.Reflections=.45f;p.FogColor=CityColor.NightHaze;p.FogDensityScale=.8f;p.SkyTint=new Color(.1f,.16f,.3f);p.SkyGround=new Color(.06f,.08f,.14f);p.SkyExposure=.6f;p.Atmosphere=.75f;p.SunSize=.01f;p.Glow=1;p.StreetLights=1;});
        // Static means the clock does not advance, while all three authored keys remain available to designers and visual tests.
        var dayCycle=ExistingOrCreate<LightingCycle>(Dir+"StaticDay.asset",c=>{c.Keys=new[]{new LightingKey{Time=0,Preset=day},new LightingKey{Time=.5f,Preset=dusk},new LightingKey{Time=.75f,Preset=night}};c.StartTime=0;c.DurationSeconds=0;c.Loop=false;});
        if(dayCycle.Keys.Length==1){dayCycle.Keys=new[]{new LightingKey{Time=0,Preset=day},new LightingKey{Time=.5f,Preset=dusk},new LightingKey{Time=.75f,Preset=night}};EditorUtility.SetDirty(dayCycle);}
        var duskCycle=ExistingOrCreate<LightingCycle>(Dir+"StaticDusk.asset",c=>Key(c,dusk,.5f));
        var nightCycle=ExistingOrCreate<LightingCycle>(Dir+"StaticNight.asset",c=>Key(c,night,.75f));
        var free=ExistingOrCreate<LightingCycle>(Dir+"FreePlayCycle.asset",c=>{c.Keys=new[]{new LightingKey{Time=0,Preset=day},new LightingKey{Time=.4f,Preset=dusk},new LightingKey{Time=.68f,Preset=night},new LightingKey{Time=.94f,Preset=day}};c.DurationSeconds=300;c.StartTime=0;c.Loop=true;});
        var config=ExistingOrCreate<WorldLighting>("Assets/Resources/WorldLighting.asset",w=>{w.DefaultCycle=dayCycle;});
        if(config.DefaultCycle==null){config.DefaultCycle=dayCycle;EditorUtility.SetDirty(config);}
        Link("hero",dayCycle);Link("villain",duskCycle);Link("free-play",free);Link("endless-fight",dayCycle);Link("endless-fight-villain",nightCycle);
        var fire=AssetDatabase.LoadAssetAtPath<PowerDefinition>("Assets/Resources/Powers/fire.asset");if(fire!=null&&fire.Light!=null&&!fire.Light.Enabled){fire.Light.Enabled=true;fire.Light.Color=CityColor.Fire;fire.Light.Intensity=3;fire.Light.Range=8;fire.Light.Seconds=.32f;fire.Light.Travel=.6f;EditorUtility.SetDirty(fire);}
        var lightning=AssetDatabase.LoadAssetAtPath<PowerDefinition>("Assets/Resources/Powers/lightning.asset");if(lightning!=null&&lightning.Light!=null&&!lightning.Light.Enabled){lightning.Light.Enabled=true;lightning.Light.Color=CityColor.Cyan;lightning.Light.Intensity=3.2f;lightning.Light.Range=9;lightning.Light.Seconds=.25f;lightning.Light.Travel=0;EditorUtility.SetDirty(lightning);}
        var laser=AssetDatabase.LoadAssetAtPath<PowerDefinition>("Assets/Resources/Powers/laser-eyes.asset");if(laser!=null&&laser.Light!=null&&!laser.Light.Enabled){laser.Light.Enabled=true;laser.Light.Color=CityColor.Red;laser.Light.Intensity=2.5f;laser.Light.Range=7;laser.Light.Seconds=.12f;laser.Light.Travel=0;EditorUtility.SetDirty(laser);}
        AssetDatabase.SaveAssets();Debug.Log("Lighting setup: data cycles and emissive palette roles configured; shipping modes remain data-driven");EditorApplication.Exit(0);
    }
    static void Link(string id,LightingCycle cycle)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameModeDefinition>("Assets/Resources/Modes/"+id+".asset");
        if(asset==null)throw new InvalidOperationException("Shipping mode not found: "+id);
        if(asset.Lighting==cycle)return;asset.Lighting=cycle;EditorUtility.SetDirty(asset);
    }
}
