using UnityEngine;

/// Shared primitive attachments on the existing rigged mannequin. No cloned meshes or per-hit materials.
public sealed class EnemyEquipment : MonoBehaviour
{
    CityNpc npc;Transform visor, helmet;Renderer visorRenderer;
    public static void Attach(CityNpc actor)
    {
        if(actor.GetComponent<EnemyEquipment>()!=null)return;
        var equipment=actor.gameObject.AddComponent<EnemyEquipment>();equipment.Build(actor);
    }
    void Build(CityNpc actor)
    {
        npc=actor;
        visor=Part("Agile visor",new Vector3(0,1.7f,.16f),new Vector3(.34f,.075f,.12f),actor.Archetype.Accent).transform;
        visorRenderer=visor.GetComponent<Renderer>();
        helmet=Part("Light helmet",new Vector3(0,1.76f,0),new Vector3(.4f,.2f,.3f),CityColor.Metal).transform;
        Part("Light left shoulder",new Vector3(-.27f,1.38f,0),new Vector3(.13f,.16f,.24f),CityColor.Metal);
        Part("Light right shoulder",new Vector3(.27f,1.38f,0),new Vector3(.13f,.16f,.24f),CityColor.Metal);
    }
    GameObject Part(string label,Vector3 position,Vector3 size,CityColor color)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=label;
        go.transform.SetParent(npc.GetComponent<HumanoidPresentation>().VisualRoot,false);go.transform.localPosition=position;go.transform.localScale=size;
        var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
        go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(color);return go;
    }
    void LateUpdate()
    {
        var presentation=npc.GetComponent<HumanoidPresentation>();var head=presentation.Animator.GetBoneTransform(HumanBodyBones.Head);
        if(head!=null)
        {
            visor.position=head.position+npc.transform.forward*.15f;helmet.position=head.position+Vector3.up*.065f;
            visor.rotation=helmet.rotation=npc.transform.rotation;
        }
        visorRenderer.sharedMaterial=CityMaterials.Get(npc.EvasionState==EvasionPhase.Telegraph?CityColor.Cream:npc.Archetype.Accent);
    }
}
