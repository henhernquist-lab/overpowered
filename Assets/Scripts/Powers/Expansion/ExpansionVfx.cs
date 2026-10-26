using UnityEngine;
using UnityEngine.Rendering;
/// One finite mesh pool per player. All geometry and palette materials are shared, never rebuilt on a hit.
public sealed class ExpansionVfx : MonoBehaviour
{
    public const int Capacity=32;
    sealed class Slot {public GameObject Root;public MeshFilter Mesh;public MeshRenderer Render;public Vector3 Velocity,Scale;public float Start,Until,Spin,Growth;public MaterialPropertyBlock Properties=new MaterialPropertyBlock();}
    Slot[] slots;int cursor;GameObject worldRoot;PowerExpansionArt art;
    public int Emitted{get;private set;}
    public int LiveCount{get{int count=0;if(slots!=null)foreach(var s in slots)if(s.Root.activeSelf)count++;return count;}}
    public void Initialize()
    {
        if(slots!=null)return;slots=new Slot[Capacity];art=Resources.Load<PowerExpansionArt>("PowerExpansionArt");worldRoot=new GameObject("Expansion world effect pool");
        foreach(var color in new[]{CityColor.Cyan,CityColor.UiPurple,CityColor.Cream,CityColor.Teal,CityColor.Fire})CityMaterials.GetVfx(color);
        for(int i=0;i<Capacity;i++)
        {
            var go=new GameObject("Expansion effect "+i);go.transform.SetParent(worldRoot.transform,false);
            var render=go.AddComponent<MeshRenderer>();render.shadowCastingMode=ShadowCastingMode.Off;render.receiveShadows=false;
            slots[i]=new Slot{Root=go,Mesh=go.AddComponent<MeshFilter>(),Render=render};go.SetActive(false);
        }
    }
    public void Emit(Mesh mesh,Vector3 at,Vector3 direction,Vector3 scale,Vector3 velocity,float seconds,CityColor color,float growth=0,float spin=0)
    {
        if(mesh==null)return;Initialize();var s=slots[cursor++%Capacity];
        s.Mesh.sharedMesh=mesh;
        s.Render.sharedMaterial=art!=null&&art.EffectMaterial!=null?art.EffectMaterial:CityMaterials.GetVfx(color);
        var tint=CityMaterials.Current.Palette.Colors[(int)color];tint.a=.65f;s.Properties.SetColor("_Color",tint);s.Render.SetPropertyBlock(s.Properties);
        s.Root.transform.SetPositionAndRotation(at,Quaternion.LookRotation(direction.sqrMagnitude>.001f?direction:Vector3.forward));
        s.Root.transform.localScale=scale;s.Scale=scale;s.Velocity=velocity;s.Start=Time.time;s.Until=Time.time+Mathf.Clamp(seconds,.03f,8);s.Growth=growth;s.Spin=spin;s.Root.SetActive(true);Emitted++;
    }
    void LateUpdate()
    {
        if(slots==null)return;
        foreach(var s in slots)
        {
            if(!s.Root.activeSelf)continue;if(Time.time>=s.Until){s.Root.SetActive(false);continue;}
            s.Root.transform.position+=s.Velocity*Time.deltaTime;
            s.Root.transform.Rotate(Vector3.forward,s.Spin*Time.deltaTime,Space.Self);
            s.Root.transform.localScale=s.Scale*(1+s.Growth*(Time.time-s.Start));
        }
    }
    void OnDestroy(){if(worldRoot!=null)Destroy(worldRoot);}
    public void Clear(){if(slots!=null)foreach(var s in slots)s.Root.SetActive(false);}
}
