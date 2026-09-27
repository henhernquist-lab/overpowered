using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(110)]
public sealed class FreezeIndicatorPool : MonoBehaviour
{
    public static FreezeIndicatorPool Current { get; private set; }
    public sealed class Marker { public FreezeIndicatorPool Owner; public Transform Root, Target; public Collider Bounds; public Renderer Render; public bool Used; }
    readonly List<Marker> slots=new List<Marker>();
    Mesh mesh;
    public int ActiveCount { get; private set; }
    public int PoolCount => slots.Count;
    public int Growths { get; private set; }
    public void Initialize()
    {
        Current=this;mesh=Snowflake();
        foreach(CityColor color in System.Enum.GetValues(typeof(CityColor)))CityMaterials.Get(color);
        for(int i=0;i<64;i++)Create();
    }
    Marker Create()
    {
        var root=new GameObject("Pooled frost silhouette "+slots.Count);root.transform.SetParent(transform,false);
        root.AddComponent<MeshFilter>().sharedMesh=mesh;
        var render=root.AddComponent<MeshRenderer>();render.sharedMaterials=new[]{CityMaterials.Get(CityColor.Cream),CityMaterials.Get(CityColor.UiNavy)};
        render.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;render.receiveShadows=false;
        root.SetActive(false);var marker=new Marker{Owner=this,Root=root.transform,Render=render};slots.Add(marker);return marker;
    }
    public Marker Rent(Transform target)
    {
        Marker marker=null;foreach(var slot in slots)if(!slot.Used){marker=slot;break;}
        // Grow only on a previously unseen peak, preserving a glyph on EVERY frozen target. All glyphs share the
        // prebuilt geometry and palette materials; there is never a material allocation during a freeze.
        if(marker==null){marker=Create();Growths++;}
        marker.Target=target;marker.Bounds=target.GetComponent<Collider>();marker.Used=true;
        marker.Root.gameObject.SetActive(true);ActiveCount++;Position(marker,Camera.main);return marker;
    }
    public void Release(Marker marker)
    {
        if(marker==null||!marker.Used)return;marker.Used=false;marker.Target=null;marker.Bounds=null;
        if(marker.Root!=null)marker.Root.gameObject.SetActive(false);ActiveCount--;
    }
    void Position(Marker marker,Camera camera)
    {
        Vector3 at=marker.Target.position;
        at.y=marker.Bounds!=null&&marker.Bounds.enabled?marker.Bounds.bounds.max.y:at.y+1.8f;
        marker.Root.position=at+Vector3.up*.6f;
        if(camera!=null)marker.Root.rotation=camera.transform.rotation;
    }
    void LateUpdate()
    {
        var camera=Camera.main;
        foreach(var marker in slots)if(marker.Used)
        {
            if(marker.Target==null||!marker.Target.gameObject.activeInHierarchy){Release(marker);continue;}
            Position(marker,camera);
        }
    }
    void OnDestroy(){if(Current==this)Current=null;if(mesh!=null)Destroy(mesh);}
    static Mesh Snowflake()
    {
        var vertices=new List<Vector3>();var front=new List<int>();var border=new List<int>();
        void Bar(Vector2 a,Vector2 b,float width,float z,List<int> triangles)
        {
            Vector2 side=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;
            int n=vertices.Count;foreach(var p in new[]{a-side,a+side,b+side,b-side})vertices.Add(new Vector3(p.x,p.y,z));
            triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);triangles.Add(n);triangles.Add(n+2);triangles.Add(n+3);
        }
        for(int layer=0;layer<2;layer++)for(int arm=0;arm<6;arm++)
        {
            float angle=arm*Mathf.PI/3;Vector2 ray=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle));
            var triangles=layer==0?front:border;float width=layer==0?.045f:.085f,z=layer==0?-.008f:0;
            Bar(Vector2.zero,ray*.37f,width,z,triangles);
            Vector2 tangent=new Vector2(-ray.y,ray.x);
            for(int branch=-1;branch<=1;branch+=2)Bar(ray*.2f,ray*.3f+tangent*.105f*branch,width,z,triangles);
        }
        var result=new Mesh{name="Shared frost silhouette"};result.SetVertices(vertices);result.subMeshCount=2;
        result.SetTriangles(front,0);result.SetTriangles(border,1);result.RecalculateNormals();result.RecalculateBounds();return result;
    }
}
