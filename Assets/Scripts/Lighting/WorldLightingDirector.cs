using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// One scene-owned, data-driven day/dusk/night director. All point lights are preallocated; CityLayout/CityArt own placement.
public sealed class WorldLightingDirector : MonoBehaviour
{
    public static WorldLightingDirector Instance { get; private set; }
    WorldSession world; WorldLighting config; LightingCycle cycle; Light sun; Camera view;
    SphericalHarmonicsL2 sceneProbe; AmbientMode originalAmbientMode;Color originalAmbientLight,originalFogColor;
    Light originalSun; FogMode originalFogMode;bool originalFog;float originalReflection,baseFogDensity;
    Material originalSky,sessionSky;
    readonly List<CityArtProp> lamps=new List<CityArtProp>(); Light[] street,power; Transform[] follow;CityArtProp[] nearest;float[] nearestDistances;
    PowerLightSpec[] specs;float[] expires,heldUntil;int cursor;float streetNext,lastApplied=-10;
    public float TimeOfDay { get; private set; }
    public LightingState State { get; private set; }
    public int StreetActive { get {int count=0;if(street!=null)foreach(var l in street)if(l.enabled)count++;return count;} }
    public int PowerActive { get {int count=0;if(power!=null)foreach(var l in power)if(l.enabled)count++;return count;} }
    public int StreetCapacity => street?.Length??0;
    public int PowerCapacity => power?.Length??0;
    public LightingCycle Cycle=>cycle;
    static Light Point(string name,Transform parent,LightRenderMode renderMode)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);var light=go.AddComponent<Light>();
        light.type=LightType.Point;light.shadows=LightShadows.None;light.renderMode=renderMode;light.enabled=false;return light;
    }
    public void Initialize(WorldSession session,Light directional,Camera camera,WorldLighting data)
    {
        Instance=this;world=session;sun=directional;view=camera;config=data;
        cycle=session.Mode!=null&&session.Mode.Definition.Lighting!=null?session.Mode.Definition.Lighting:data.DefaultCycle;
        if(cycle==null||cycle.Keys==null||cycle.Keys.Length==0)throw new System.InvalidOperationException("Run LightingSetup.Configure to author a default lighting cycle");
        TimeOfDay=cycle.StartTime;sceneProbe=RenderSettings.ambientProbe;originalAmbientMode=RenderSettings.ambientMode;originalAmbientLight=RenderSettings.ambientLight;
        originalSun=RenderSettings.sun;originalFog=RenderSettings.fog;originalFogMode=RenderSettings.fogMode;originalFogColor=RenderSettings.fogColor;
        originalReflection=RenderSettings.reflectionIntensity;baseFogDensity=RenderSettings.fogDensity;
        originalSky=RenderSettings.skybox;
        if(data.DriveSky&&originalSky!=null){sessionSky=new Material(originalSky){name="Session procedural sky"};RenderSettings.skybox=sessionSky;}
        foreach(var art in FindObjectsByType<CityArtProp>(FindObjectsInactive.Exclude))if(art.Kind==CityPropKind.Lamp)lamps.Add(art);
        street=new Light[Mathf.Clamp(data.StreetLights.MaxLights,0,8)];nearest=new CityArtProp[street.Length];nearestDistances=new float[street.Length];for(int i=0;i<street.Length;i++)street[i]=Point("Pooled street light "+i,transform,data.StreetLights.RenderMode);
        power=new Light[Mathf.Clamp(data.PowerLights.PoolSize,0,8)];follow=new Transform[power.Length];specs=new PowerLightSpec[power.Length];expires=new float[power.Length];heldUntil=new float[power.Length];
        for(int i=0;i<power.Length;i++)power[i]=Point("Pooled power light "+i,transform,data.PowerLights.RenderMode);
        Apply(true);
    }
    void Update(){if(world==null)return;TimeOfDay=cycle.Advance(TimeOfDay,Time.deltaTime);Apply(false);UpdateStreet();UpdatePowers();}
    /// Verification and designer controls set time directly; the mode's cycle remains the authority afterward.
    public void SetTime(float time){TimeOfDay=cycle.Normalize(time);Apply(true);UpdateStreet(true);}
    void Apply(bool force)
    {
        cycle.Evaluate(TimeOfDay,out var from,out var to,out float weight);if(from==null)return;
        var palette=CityMaterials.Current.Palette;
        var state=LightingState.Lerp(LightingState.From(from,palette),LightingState.From(to,palette),weight);
        if(!force&&Mathf.Abs(state.Glow-lastApplied)<config.ApplyEpsilon)return;
        State=state;lastApplied=state.Glow;
        sun.transform.rotation=state.Rotation;sun.color=state.LightColor;sun.intensity=state.Intensity;
        RenderSettings.sun=sun;
        RenderSettings.ambientMode=AmbientMode.Custom;RenderSettings.ambientProbe=state.Ambient(sceneProbe);
        RenderSettings.ambientLight=new Color(.45f*state.AmbientTint.r+state.AmbientFloor.r,.5f*state.AmbientTint.g+state.AmbientFloor.g,.6f*state.AmbientTint.b+state.AmbientFloor.b,1);
        RenderSettings.reflectionIntensity=state.Reflections;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Exponential;RenderSettings.fogDensity=baseFogDensity*state.FogScale;RenderSettings.fogColor=state.Fog;
        if(sessionSky!=null)
        {
            if(sessionSky.HasProperty("_SkyTint"))sessionSky.SetColor("_SkyTint",state.SkyTint);
            if(sessionSky.HasProperty("_GroundColor"))sessionSky.SetColor("_GroundColor",state.SkyGround);
            if(sessionSky.HasProperty("_Exposure"))sessionSky.SetFloat("_Exposure",state.SkyExposure);
            if(sessionSky.HasProperty("_AtmosphereThickness"))sessionSky.SetFloat("_AtmosphereThickness",state.Atmosphere);
            if(sessionSky.HasProperty("_SunSize"))sessionSky.SetFloat("_SunSize",state.SunSize);
        }
        CityMaterials.Current.SetGlow(state.Glow);
    }
    void UpdateStreet(bool force=false)
    {
        if(street==null||street.Length==0)return;
        if(!force&&Time.time<streetNext)return;streetNext=Time.time+Mathf.Max(.05f,config.StreetLights.RefreshSeconds);
        var settings=config.StreetLights;float max=settings.SearchRadius*settings.SearchRadius;
        for(int i=0;i<nearest.Length;i++){nearest[i]=null;nearestDistances[i]=float.PositiveInfinity;}
        Vector3 eye=view!=null?view.transform.position:world.Hero.transform.position;
        foreach(var lamp in lamps)
        {
            if(lamp==null||!lamp.gameObject.activeInHierarchy||Vector3.Dot(lamp.transform.up,Vector3.up)<.55f)continue;
            float d=(lamp.transform.position-eye).sqrMagnitude;if(d>=max)continue;
            int where=-1;for(int i=0;i<nearest.Length;i++)if(d<nearestDistances[i]){where=i;break;}
            if(where<0)continue;for(int i=nearest.Length-1;i>where;i--){nearest[i]=nearest[i-1];nearestDistances[i]=nearestDistances[i-1];}nearest[where]=lamp;nearestDistances[where]=d;
        }
        for(int i=0;i<street.Length;i++)
        {
            var light=street[i];if(nearest[i]==null||State.StreetLights<.01f){light.enabled=false;continue;}
            float distance=Mathf.Sqrt(nearestDistances[i]);float fade=1-Mathf.InverseLerp(settings.FadeStart,settings.SearchRadius,distance);
            light.transform.position=nearest[i].transform.TransformPoint(Vector3.up*settings.Height);
            light.color=CityMaterials.Current.Palette.Colors[(int)settings.Color];light.range=settings.Range;
            light.intensity=settings.Intensity*State.StreetLights*fade;light.enabled=light.intensity>.01f;
        }
    }
    int Slot(PowerLightSpec spec,Vector3 point)
    {
        int reuse=-1;float distance=config.PowerLights.MergeDistance*config.PowerLights.MergeDistance;
        for(int i=0;i<power.Length;i++)if(power[i].enabled&&specs[i]==spec&&(power[i].transform.position-point).sqrMagnitude<distance){reuse=i;break;}
        if(reuse>=0)return reuse;
        for(int i=0;i<power.Length;i++)if(!power[i].enabled)return i;
        int next=cursor;cursor=(cursor+1)%power.Length;return next;
    }
    void LightPower(int i,PowerLightSpec spec,Vector3 point,float seconds,Transform target)
    {
        var light=power[i];specs[i]=spec;follow[i]=target;light.transform.position=point;light.color=CityMaterials.Current.Palette.Colors[(int)spec.Color];
        light.range=spec.Range;light.intensity=spec.Intensity*(target==null?1:spec.Travel);light.enabled=true;
        expires[i]=Time.time+seconds;heldUntil[i]=target==null?0:Time.time+config.PowerLights.MaxFollowSeconds;
    }
    public void FlashPower(PowerLightSpec spec,Vector3 point)
    {
        if(spec==null||!spec.Enabled||power==null||power.Length==0)return;
        int i=Slot(spec,point);if(follow[i]!=null)return;LightPower(i,spec,point,Mathf.Max(.01f,spec.Seconds),null);
    }
    public void FollowPower(PowerLightSpec spec,Transform source)
    {
        if(spec==null||!spec.Enabled||spec.Travel<=0||source==null||power==null||power.Length==0)return;
        int i=Slot(spec,source.position);LightPower(i,spec,source.position,config.PowerLights.MaxFollowSeconds,source);
    }
    public void HoldPower(PowerLightSpec spec,Vector3 point)
    {
        if(spec==null||!spec.Enabled||power==null||power.Length==0)return;
        int i=Slot(spec,point);LightPower(i,spec,point,config.PowerLights.HoldGraceSeconds+config.PowerLights.FadeOutSeconds,null);
    }
    void UpdatePowers()
    {
        if(power==null)return;
        for(int i=0;i<power.Length;i++)if(power[i].enabled)
        {
            if(follow[i]!=null)
            {
                if(Time.time<=heldUntil[i]){power[i].transform.position=follow[i].position;expires[i]=Time.time+config.PowerLights.FadeOutSeconds;}
                else follow[i]=null;
            }
            if(Time.time>=expires[i]){power[i].enabled=false;follow[i]=null;continue;}
            float fade=Mathf.Clamp01((expires[i]-Time.time)/Mathf.Max(.01f,config.PowerLights.FadeOutSeconds));
            power[i].intensity=specs[i].Intensity*(follow[i]!=null?specs[i].Travel:1)*fade;
        }
    }
    void OnDestroy()
    {
        if(Instance==this)Instance=null;
        RenderSettings.ambientMode=originalAmbientMode;RenderSettings.ambientProbe=sceneProbe;RenderSettings.ambientLight=originalAmbientLight;
        RenderSettings.reflectionIntensity=originalReflection;RenderSettings.sun=originalSun;
        RenderSettings.fog=originalFog;RenderSettings.fogMode=originalFogMode;RenderSettings.fogColor=originalFogColor;RenderSettings.fogDensity=baseFogDensity;
        if(sessionSky!=null){RenderSettings.skybox=originalSky;Destroy(sessionSky);}
    }
}
