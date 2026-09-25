using UnityEngine;

// Added at most once per prop/actor, reused on later throws.
public sealed class SynergyPayload : MonoBehaviour
{
    SynergyRunner runner;bool fire,blast;float expires;
    public void Arm(SynergyRunner owner,bool burning,bool explosive){runner=owner;fire=burning;blast=explosive;expires=Time.time+owner.Definition.Duration+3;enabled=true;}
    void OnCollisionEnter(Collision collision)
    {
        if(!enabled||runner==null||collision.transform.root==runner.transform)return;
        var npc=collision.collider.GetComponentInParent<CityNpc>();
        if(fire&&npc!=null)npc.MarkBurn(runner.Definition.FreezeSeconds);
        if(blast){runner.Impact(transform.position);enabled=false;}
    }
    void Update(){if(Time.time>expires||runner==null)enabled=false;}
}
