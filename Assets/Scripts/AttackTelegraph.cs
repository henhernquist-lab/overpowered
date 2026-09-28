using UnityEngine;
using UnityEngine.Rendering;

/// One pooled telegraph visual per NPC, created on its first windup and re-used for every later attack
/// (no per-attack Instantiate/Destroy). Shared palette material only (CityColor.Fire); no collider, no shadows.
/// Melee/Slam: flat disc on the ground at the committed area, growing to the exact hit radius at release. With an active
/// VisualPreset whose RingTelegraphs is on, the solid disc becomes two thin rings instead (same palette material): a fixed
/// boundary ring at the exact hit radius from windup start, and a ring growing from the start fraction to meet it at
/// release, so the danger area and the timing read without covering the ground. `Renderer` is the growing ring.
/// Ranged: thin line along the locked aim direction.
public sealed class AttackTelegraph
{
    public MeshRenderer Renderer { get; }
    public bool Visible => transform.gameObject.activeSelf;
    readonly Transform transform, boundary;
    readonly bool line, rings;
    Vector3 origin, direction; float size, width, startFraction;
    static Mesh disc, bar, ring;
    const float DiscThickness=.1f, DiscTop=.13f;

    public AttackTelegraph(Transform owner, bool isLine)
    {
        line=isLine;
        if(disc==null)disc=Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        if(bar==null)bar=Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var preset=VisualPreset.Current;rings=!line&&VisualPreset.Active(preset)&&preset.RingTelegraphs;
        var go=new GameObject(line?"Attack telegraph (aim line)":rings?"Attack telegraph (rings)":"Attack telegraph (disc)");
        transform=go.transform;transform.SetParent(owner,false);
        if(!rings){go.AddComponent<MeshFilter>().sharedMesh=line?bar:disc;Renderer=Setup(go.AddComponent<MeshRenderer>());}
        else
        {
            if(ring==null)ring=Ring(.9f,48);
            Renderer=Part(transform,"Telegraph timing ring");boundary=Part(transform,"Telegraph boundary ring").transform;
        }
        go.SetActive(false);
    }
    static MeshRenderer Setup(MeshRenderer r)
    {
        r.sharedMaterial=CityMaterials.Get(CityColor.Fire);
        r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
        r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
        return r;
    }
    static MeshRenderer Part(Transform parent,string name)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=ring;return Setup(go.AddComponent<MeshRenderer>());
    }
    /// Shared flat annulus (outer radius 1, inner `inner`), facing up. Built once for every telegraph.
    static Mesh Ring(float inner,int segments)
    {
        var v=new Vector3[segments*2];var n=new Vector3[segments*2];var t=new int[segments*6];
        for(int i=0;i<segments;i++)
        {
            float a=i*Mathf.PI*2f/segments;var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
            v[i*2]=d;v[i*2+1]=d*inner;n[i*2]=n[i*2+1]=Vector3.up;
            int j=(i+1)%segments,k=i*6;
            t[k]=i*2;t[k+1]=i*2+1;t[k+2]=j*2;t[k+3]=j*2;t[k+4]=i*2+1;t[k+5]=j*2+1;
        }
        var m=new Mesh{name="Telegraph ring",vertices=v,normals=n,triangles=t};m.RecalculateBounds();return m;
    }
    /// Disc centred on the committed ground point with the committed radius.
    public void ShowDisc(Vector3 centre, float radius, float fromFraction)
    {origin=centre;size=radius;startFraction=fromFraction;transform.gameObject.SetActive(true);Tick(0);}
    /// Line from the muzzle along the locked (normalised) aim direction.
    public void ShowLine(Vector3 muzzle, Vector3 aim, float length, float thickness)
    {origin=muzzle;direction=aim;size=length;width=thickness;transform.gameObject.SetActive(true);Tick(0);}
    /// progress 0..1 over the windup. World-space placement: the physics root is never scaled.
    public void Tick(float progress)
    {
        progress=Mathf.Clamp01(progress);
        if(line)
        {
            float w=width*Mathf.Lerp(.4f,1f,progress);
            transform.SetPositionAndRotation(origin+direction*(size*.5f),Quaternion.LookRotation(direction));
            transform.localScale=new Vector3(w,w,size);
        }
        else if(rings)
        {
            // Unscaled root on the ground point; boundary at the exact hit radius, timing ring closing in on it.
            transform.SetPositionAndRotation(origin+Vector3.up*DiscTop,Quaternion.identity);transform.localScale=Vector3.one;
            boundary.localScale=new Vector3(size,1f,size);
            float r=size*Mathf.Lerp(startFraction,1f,progress);Renderer.transform.localScale=new Vector3(r,1f,r);
            Renderer.transform.localPosition=Vector3.up*.01f;
        }
        else
        {
            float d=2f*size*Mathf.Lerp(startFraction,1f,progress);
            transform.SetPositionAndRotation(origin+Vector3.up*(DiscTop-DiscThickness*.5f),Quaternion.identity);
            transform.localScale=new Vector3(d,DiscThickness*.5f,d); // built-in cylinder is 2 units tall, 1 unit wide
        }
    }
    public void Hide(){if(transform!=null&&transform.gameObject.activeSelf)transform.gameObject.SetActive(false);}
}
