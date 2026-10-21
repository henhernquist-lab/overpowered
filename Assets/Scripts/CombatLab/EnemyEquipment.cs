using UnityEngine;

/// Shared primitive attachments on the existing rigged mannequin. No cloned meshes or per-hit materials.
public sealed class EnemyEquipment : MonoBehaviour
{
    CityNpc npc;HumanoidPresentation presentation;Transform visor, helmet, head, shield;Renderer visorRenderer, shieldFace;
    public static void Attach(CityNpc actor)
    {
        if(actor.GetComponent<EnemyEquipment>()!=null)return;
        var equipment=actor.gameObject.AddComponent<EnemyEquipment>();equipment.Build(actor);
    }
    void Build(CityNpc actor)
    {
        npc=actor;presentation=npc.GetComponent<HumanoidPresentation>();head=presentation.Animator.GetBoneTransform(HumanBodyBones.Head);
        if(actor.HasGuard)
        {
            shield=new GameObject("Directional shield (visual only)").transform;shield.SetParent(presentation.VisualRoot,false);shield.localPosition=new Vector3(0,1.05f,.58f);
            var plate=Part("Shield plate",Vector3.zero,new Vector3(1.12f,1.5f,.16f),CityColor.Metal);plate.transform.SetParent(shield,false);plate.transform.localPosition=Vector3.zero;shieldFace=plate.GetComponent<Renderer>();
            var stripe=Part("Shield guard stripe",Vector3.zero,new Vector3(.16f,1.25f,.18f),actor.Archetype.Accent);stripe.transform.SetParent(shield,false);stripe.transform.localPosition=Vector3.zero;
            Part("Heavy left pauldron",new Vector3(-.35f,1.44f,0),new Vector3(.32f,.3f,.4f),CityColor.Metal);
            Part("Heavy right pauldron",new Vector3(.35f,1.44f,0),new Vector3(.32f,.3f,.4f),CityColor.Metal);
            Part("Chest armor",new Vector3(0,1.18f,.13f),new Vector3(.55f,.47f,.18f),CityColor.Slate);
        }
        visor=Part("Agile visor",new Vector3(0,1.7f,.16f),new Vector3(.34f,.075f,.12f),actor.Archetype.Accent).transform;
        visorRenderer=visor.GetComponent<Renderer>();
        helmet=Part("Light helmet",new Vector3(0,1.76f,0),new Vector3(.4f,.2f,.3f),CityColor.Metal).transform;
        if(!npc.HasGuard)Part("Light left shoulder",new Vector3(-.27f,1.38f,0),new Vector3(.13f,.16f,.24f),CityColor.Metal);
        if(!npc.HasGuard)Part("Light right shoulder",new Vector3(.27f,1.38f,0),new Vector3(.13f,.16f,.24f),CityColor.Metal);
    }
    GameObject Part(string label,Vector3 position,Vector3 size,CityColor color)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=label;
        go.transform.SetParent(presentation.VisualRoot,false);go.transform.localPosition=position;go.transform.localScale=size;
        var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
        go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(color);return go;
    }
    void LateUpdate()
    {
        if(head!=null)
        {
            visor.position=head.position+npc.transform.forward*.15f;helmet.position=head.position+Vector3.up*.065f;
            visor.rotation=helmet.rotation=npc.transform.rotation;
        }
        if(shield!=null)
        {
            shield.localRotation=Quaternion.Euler(npc.GuardRaised?0:35,0,npc.GuardRaised?0:-18);
            shieldFace.sharedMaterial=CityMaterials.Get(npc.GuardBroken?CityColor.Red:Time.time-npc.GuardHitAt<.14f?CityColor.Cream:CityColor.Metal);
        }
        visorRenderer.sharedMaterial=CityMaterials.Get(npc.EvasionState==EvasionPhase.Telegraph?CityColor.Cream:npc.Archetype.Accent);
    }
}
