using UnityEngine;
using UnityEngine.AI;
/// Ground-constrained finite displacement. Agent.Move plus both NavMesh and capsule sweeps prevents wall tunnelling.
public sealed class NpcPowerDisplacement : MonoBehaviour
{
    CityNpc npc;Vector3 direction;float left,speed,nextAllowed;
    static readonly RaycastHit[] hits=new RaycastHit[24];
    public float Travelled{get;private set;}
    public bool Begin(CityNpc target,Vector3 vector,float distance,float seconds)
    {
        if(target==null||target.Dead||Time.time<nextAllowed||!target.Agent.enabled||!target.Agent.isOnNavMesh)return false;
        npc=target;vector.y=0;if(vector.sqrMagnitude<.001f)return false;direction=vector.normalized;
        left=Mathf.Clamp(distance*target.PowerDisplacementFactor,0,4);speed=left/Mathf.Max(.1f,seconds);Travelled=0;
        nextAllowed=Time.time+(target.IsBoss?3f:1f);enabled=true;return true;
    }
    public static float MoveSafe(CityNpc target,Vector3 delta)
    {
        if(target==null||target.Dead||target.Agent==null||!target.Agent.enabled||!target.Agent.isOnNavMesh)return 0;
        delta.y=0;if(delta.sqrMagnitude<.000001f)return 0;
        Vector3 from=target.transform.position,to=from+delta;
        if(NavMesh.Raycast(from,to,out var nav,NavMesh.AllAreas))to=nav.position;
        delta=to-from;float distance=delta.magnitude;if(distance<.001f)return 0;
        var capsule=target.GetComponent<CapsuleCollider>();float radius=capsule.radius,height=capsule.height;
        Vector3 a=from+Vector3.up*(radius+.06f),b=from+Vector3.up*(height-radius+.06f);
        int count=Physics.CapsuleCastNonAlloc(a,b,radius,delta.normalized,hits,distance+.03f,~0,QueryTriggerInteraction.Ignore);
        if(count>=hits.Length)return 0;
        for(int i=0;i<count;i++)if(hits[i].transform.root!=target.transform)distance=Mathf.Min(distance,Mathf.Max(0,hits[i].distance-.04f));
        if(distance<=0)return 0;target.Agent.Move(delta.normalized*distance);return distance;
    }
    void LateUpdate()
    {
        if(npc==null||npc.Dead||left<=.001f){enabled=false;return;}
        float step=Mathf.Min(left,speed*Time.deltaTime);if(step<=0)return;
        float moved=MoveSafe(npc,direction*step);left-=step;Travelled+=moved;if(moved<step*.5f)left=0;
    }
}
