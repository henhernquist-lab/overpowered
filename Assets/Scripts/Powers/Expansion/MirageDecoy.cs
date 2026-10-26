using System.Collections.Generic;
using UnityEngine;
// A presentation-only finite target, deliberately no CityNpc, Animator, PowerUser or progression component.
public sealed class MirageDecoy : MonoBehaviour
{
    public static MirageDecoy Current{get;private set;}
    public bool Alive=>health>0&&Time.time<until;
    public int ReceivedHits{get;private set;}
    float health=50,until;PowerExpansionRuntime owner;
    public static MirageDecoy Create(PowerExpansionRuntime source,Vector3 at,float duration)
    {
        var presentation=source.GetComponent<HumanoidPresentation>();if(presentation?.VisualRoot==null)return null;
        if(Current!=null)Current.Dissolve();
        var root=new GameObject("Mirage hologram");root.transform.SetPositionAndRotation(at,source.transform.rotation);
        var decoy=root.AddComponent<MirageDecoy>();decoy.owner=source;decoy.until=Time.time+Mathf.Clamp(duration,1,8);Current=decoy;
        var map=new Dictionary<Transform,Transform>();
        // Copy transforms only: never instantiate the player or its appearance scripts/material ownership.
        void Copy(Transform original,Transform parent)
        {
            var go=new GameObject(original.name);go.transform.SetParent(parent,false);go.transform.localPosition=original.localPosition;go.transform.localRotation=original.localRotation;go.transform.localScale=original.localScale;map[original]=go.transform;
            foreach(Transform child in original)Copy(child,go.transform);
        }
        Copy(presentation.VisualRoot,root.transform);
        foreach(var original in presentation.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if(!original.enabled||original.sharedMesh==null||!map.TryGetValue(original.transform,out var target))continue;
            var renderer=target.gameObject.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=original.sharedMesh;
            var bones=original.bones;for(int i=0;i<bones.Length;i++)bones[i]=bones[i]!=null&&map.TryGetValue(bones[i],out var b)?b:null;
            renderer.bones=bones;renderer.rootBone=original.rootBone!=null&&map.TryGetValue(original.rootBone,out var rb)?rb:target;
            renderer.localBounds=original.localBounds;renderer.updateWhenOffscreen=true;
            var materials=new Material[original.sharedMaterials.Length];for(int i=0;i<materials.Length;i++)materials[i]=source.Art.Hologram;renderer.sharedMaterials=materials;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        foreach(var original in presentation.VisualRoot.GetComponentsInChildren<MeshRenderer>())
        {
            var mesh=original.GetComponent<MeshFilter>();if(!original.enabled||mesh?.sharedMesh==null||!map.TryGetValue(original.transform,out var target))continue;
            target.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;var renderer=target.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=source.Art.Hologram;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        source.Vfx.Emit(source.Art.SoundRing,at+Vector3.up,Vector3.up,Vector3.one*.6f,Vector3.zero,.35f,CityColor.UiPurple,4,45);return decoy;
    }
    public void Hit(float damage){if(!Alive)return;ReceivedHits++;health-=Mathf.Max(0,damage);if(health<=0)Dissolve();}
    void Update(){if(!Alive||owner==null)Dissolve();}
    public void Dissolve()
    {
        if(Current==this)Current=null;health=0;
        if(owner!=null)owner.Vfx.Emit(owner.Art.SoundRing,transform.position+Vector3.up,Vector3.up,Vector3.one*.8f,Vector3.up,.3f,CityColor.UiPurple,3,80);
        Destroy(gameObject);
    }
    void OnDestroy(){if(Current==this)Current=null;}
}
