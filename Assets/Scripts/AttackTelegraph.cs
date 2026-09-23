using UnityEngine;
using UnityEngine.Rendering;

/// One pooled telegraph visual per NPC, created on its first windup and re-used for every later attack
/// (no per-attack Instantiate/Destroy). Shared palette material only (CityColor.Fire); no collider, no shadows.
/// Melee/Slam: flat disc on the ground at the committed area, growing to the exact hit radius at release.
/// Ranged: thin line along the locked aim direction.
public sealed class AttackTelegraph
{
    public MeshRenderer Renderer { get; }
    public bool Visible => transform.gameObject.activeSelf;
    readonly Transform transform;
    readonly bool line;
    Vector3 origin, direction; float size, width, startFraction;
    static Mesh disc, bar;
    const float DiscThickness=.1f, DiscTop=.13f;

    public AttackTelegraph(Transform owner, bool isLine)
    {
        line=isLine;
        if(disc==null)disc=Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        if(bar==null)bar=Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var go=new GameObject(line?"Attack telegraph (aim line)":"Attack telegraph (disc)");
        transform=go.transform;transform.SetParent(owner,false);
        go.AddComponent<MeshFilter>().sharedMesh=line?bar:disc;
        Renderer=go.AddComponent<MeshRenderer>();
        Renderer.sharedMaterial=CityMaterials.Get(CityColor.Fire);
        Renderer.shadowCastingMode=ShadowCastingMode.Off;Renderer.receiveShadows=false;
        Renderer.lightProbeUsage=LightProbeUsage.Off;Renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
        go.SetActive(false);
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
        else
        {
            float d=2f*size*Mathf.Lerp(startFraction,1f,progress);
            transform.SetPositionAndRotation(origin+Vector3.up*(DiscTop-DiscThickness*.5f),Quaternion.identity);
            transform.localScale=new Vector3(d,DiscThickness*.5f,d); // built-in cylinder is 2 units tall, 1 unit wide
        }
    }
    public void Hide(){if(transform!=null&&transform.gameObject.activeSelf)transform.gameObject.SetActive(false);}
}
