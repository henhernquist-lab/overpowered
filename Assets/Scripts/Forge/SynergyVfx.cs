using System.Collections.Generic;
using UnityEngine;

// Fixed scene-lifetime pool. Built-in materials are shared by palette role (particles: lit swatch; ring: CityMaterials' unlit
// swatch, so the thin ribbon keeps its palette colour); no per-cast meshes/materials.
public sealed class SynergyVfx : MonoBehaviour
{
    sealed class Slot {public GameObject Root;public ParticleSystem Particles;public LineRenderer Ring;public float Until,Start,Radius;}
    Slot[] slots;int cursor;ForgeCatalog tuning;readonly Dictionary<CityColor,Material> ringMaterials=new Dictionary<CityColor,Material>();
    public int Emissions {get;private set;}
    public int PoolCount=>slots?.Length??0;
    public ParticleSystem LastParticles=>slots==null||cursor==0?null:slots[(cursor-1)%slots.Length].Particles;
    public void Initialize(ForgeCatalog catalog)
    {
        tuning=catalog;slots=new Slot[Mathf.Max(1,catalog.EffectPoolSize)];
        foreach(var effect in catalog.Synergies)if(effect!=null&&!ringMaterials.ContainsKey(effect.Secondary))
            ringMaterials.Add(effect.Secondary,CityMaterials.GetVfx(effect.Secondary));
        var template=GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh particleMesh=template.GetComponent<MeshFilter>().sharedMesh;
        template.SetActive(false);Destroy(template);
        for(int i=0;i<slots.Length;i++)
        {
            var root=new GameObject("Pooled synergy "+i);root.transform.SetParent(WorldSession.Instance.transform,false);
            var ps=root.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=false;main.maxParticles=catalog.ParticlesPerBurst;
            main.startLifetime=catalog.ParticleLifetime;main.startSize=catalog.ParticleSize;main.startSpeed=5;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.scalingMode=ParticleSystemScalingMode.Shape;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=particleMesh;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            var emission=ps.emission;emission.enabled=false;
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.3f;
            var ring=root.AddComponent<LineRenderer>();ring.useWorldSpace=false;ring.loop=true;ring.positionCount=40;ring.widthMultiplier=.12f;
            ring.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            for(int n=0;n<40;n++){float angle=n*Mathf.PI*2/40;ring.SetPosition(n,new Vector3(Mathf.Cos(angle),.08f,Mathf.Sin(angle)));}
            slots[i]=new Slot{Root=root,Particles=ps,Ring=ring};root.SetActive(false);
        }
    }
    public void Burst(Vector3 position,PowerSynergyDefinition d)
    {
        var s=slots[cursor++%slots.Length];s.Root.SetActive(true);s.Root.transform.position=position;s.Root.transform.rotation=Quaternion.identity;s.Root.transform.localScale=Vector3.one;
        s.Particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        s.Particles.GetComponent<ParticleSystemRenderer>().sharedMaterial=CityMaterials.Get(d.Primary);
        // Catalog swatches are resolved at pool start; any other swatch is resolved once through the same bounded palette cache.
        if(!ringMaterials.TryGetValue(d.Secondary,out var ring))ringMaterials.Add(d.Secondary,ring=CityMaterials.GetVfx(d.Secondary));
        s.Ring.sharedMaterial=ring;
        s.Radius=d.Radius;s.Start=Time.time;s.Until=Time.time+tuning.ShockwaveSeconds;
        s.Particles.Play();s.Particles.Emit(tuning.ParticlesPerBurst);Emissions++;
    }
    void LateUpdate()
    {
        if(slots==null)return;
        foreach(var s in slots)
        {
            if(!s.Root.activeSelf)continue;
            if(Time.time>=s.Until){s.Root.SetActive(false);continue;}
            float t=(Time.time-s.Start)/Mathf.Max(.01f,tuning.ShockwaveSeconds);
            // Particle simulation is world-space, so expanding the ring does not expand the particle cloud.
            s.Root.transform.localScale=Vector3.one*Mathf.Lerp(.2f,s.Radius,t);
        }
    }
    void OnDestroy(){if(slots!=null)foreach(var s in slots)if(s.Root!=null)Destroy(s.Root);}
}
