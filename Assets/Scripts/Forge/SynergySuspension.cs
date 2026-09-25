using UnityEngine;
using UnityEngine.AI;

// NPCs normally use NavMesh locomotion, not rigidbodies. This temporary owner relinquishes both safely.
public sealed class SynergySuspension : MonoBehaviour
{
    CityNpc npc;Rigidbody body;bool wasEnabled,agentEnabled,released;Vector3 start;float deadline;
    public Rigidbody Begin(CityNpc target)
    {
        if(enabled&&npc==target&&body!=null)return body;
        npc=target;start=transform.position;wasEnabled=npc.enabled;agentEnabled=npc.Agent.enabled;
        npc.enabled=false;npc.Agent.enabled=false;
        body=GetComponent<Rigidbody>();if(body==null)body=gameObject.AddComponent<Rigidbody>();
        body.mass=80;body.isKinematic=false;body.useGravity=false;body.constraints=RigidbodyConstraints.FreezeRotation;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        released=false;deadline=Time.time+8;enabled=true;return body;
    }
    public void Release(){released=true;deadline=Time.time+4;}
    void OnCollisionEnter(Collision other){if(released&&other.collider.GetComponentInParent<SuperHeroController>()==null)Finish();}
    void Update(){if(npc==null||npc.Dead||Time.time>deadline)Finish();}
    public void Finish()
    {
        if(body!=null){body.isKinematic=true;body.useGravity=false;}
        if(npc!=null&&!npc.Dead)
        {
            Vector3 destination=NavMesh.SamplePosition(transform.position,out var hit,8,NavMesh.AllAreas)?hit.position:start;
            transform.position=destination;npc.Agent.enabled=agentEnabled;
            if(agentEnabled)npc.Agent.Warp(destination);npc.enabled=wasEnabled;
        }
        enabled=false;
    }
    void OnDisable(){if(npc!=null&&!npc.Dead&&!npc.Agent.enabled&&agentEnabled)Finish();}
}
