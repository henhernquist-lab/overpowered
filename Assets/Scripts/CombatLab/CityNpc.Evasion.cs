using UnityEngine;
using UnityEngine.AI;

public enum EvasionPhase { None, Telegraph, Travel, Recover }

public sealed partial class CityNpc
{
    public EvasionPhase EvasionState {get;private set;}
    public int Dodges {get;private set;}
    public int Dashes {get;private set;}
    public int Retreats {get;private set;}
    public float NextEvasionAt=>nextEvasion;
    public Vector3 EvasionDestination {get;private set;}
    public bool Evasive=>Archetype!=null&&Archetype.Evasion!=null&&Archetype.Evasion.Enabled;
    float nextEvasion, evasionUntil;int burstIndex, evasionSerial;bool evasionRotated;bool savedRotation;
    NavMeshPath evasionPath;
    static readonly RaycastHit[] evasionHits=new RaycastHit[24];
    void InitializeTactics()
    {
        InitializeDefense();
        if(!Evasive)return;
        evasionPath=new NavMeshPath();
        nextEvasion=Time.time+1.5f+(Mathf.Abs(GetEntityId().GetHashCode()%100)*.012f);
        EnemyEquipment.Attach(this);
    }
    bool TickEvasion(float distance)
    {
        if(!Evasive)return false;
        if(Frozen||Rooted||!Agent.enabled||!Agent.isOnNavMesh){CancelEvasion();return false;}
        var e=Archetype.Evasion;
        if(EvasionState==EvasionPhase.Telegraph)
        {
            Agent.isStopped=true;
            if(Time.time<evasionUntil)return true;
            // Revalidate after the readable tell; newly moved props/actors can close the lane.
            if(!EvasionLane(EvasionDestination,out var end)){CancelEvasion();nextAttack=Mathf.Max(nextAttack,Time.time+e.Recovery);return false;}
            EvasionDestination=end;EvasionState=EvasionPhase.Travel;evasionUntil=Time.time+Mathf.Max(.1f,e.TravelSeconds);
            Agent.isStopped=false;Agent.SetPath(evasionPath);MoveSpeed(Vector3.Distance(transform.position,end)/Mathf.Max(.1f,e.TravelSeconds));
            return true;
        }
        if(EvasionState==EvasionPhase.Travel)
        {
            if(Time.time<evasionUntil&&Vector3.Distance(transform.position,EvasionDestination)>.2f)
            {MoveSpeed(Mathf.Max(Archetype.MoveSpeed,Archetype.Evasion.DashDistance/Mathf.Max(.1f,e.TravelSeconds)));return true;}
            Agent.ResetPath();Agent.isStopped=true;
            EvasionState=EvasionPhase.Recover;evasionUntil=Time.time+Mathf.Max(.1f,e.Recovery);
            nextAttack=Mathf.Max(nextAttack,evasionUntil);return true;
        }
        if(EvasionState==EvasionPhase.Recover)
        {
            Agent.isStopped=true;
            if(Time.time<evasionUntil)return true;
            CancelEvasion();Agent.isStopped=false;nextPath=0;
            return false;
        }
        if(Time.time<nextEvasion||distance<e.MinimumDistance||distance>e.MaximumDistance||Phase==AttackPhase.Windup)return false;
        bool dash=Phase==AttackPhase.Engage&&distance>Archetype.TriggerDistance+1f&&(evasionSerial++%2)==0;
        Vector3 toward=CombatTargetPosition-transform.position;toward.y=0;
        if(toward.sqrMagnitude<.01f)return false;toward.Normalize();
        Vector3 direction=dash?toward:Vector3.Cross(Vector3.up,toward)*circleSign;
        float length=dash?Mathf.Min(e.DashDistance,Mathf.Max(0,distance-Archetype.TriggerDistance)):e.LateralDistance;
        nextEvasion=Time.time+Mathf.Max(1f,e.Cooldown);circleSign=-circleSign;
        if(!BeginEvasion(transform.position+direction*length))return false;
        if(dash)Dashes++;else Dodges++;
        return true;
    }
    bool BeginEvasion(Vector3 destination)
    {
        if(!EvasionLane(destination,out var end))return false;
        EvasionDestination=end;EvasionState=EvasionPhase.Telegraph;evasionUntil=Time.time+Mathf.Max(.1f,Archetype.Evasion.Telegraph);
        savedRotation=Agent.updateRotation;Agent.updateRotation=false;evasionRotated=true;
        Agent.isStopped=true;Agent.velocity=Vector3.zero;
        return true;
    }
    bool EvasionLane(Vector3 destination,out Vector3 end)
    {
        end=transform.position;
        if(!NavMesh.SamplePosition(destination,out var hit,.45f,Agent.areaMask)||Mathf.Abs(hit.position.y-transform.position.y)>.5f)return false;
        if(NavMesh.Raycast(transform.position,hit.position,out _,Agent.areaMask))return false;
        Vector3 delta=hit.position-transform.position;float distance=delta.magnitude;if(distance<.35f)return false;
        var c=GetComponent<CapsuleCollider>();float radius=c.radius*.95f;
        int n=Physics.CapsuleCastNonAlloc(transform.position+Vector3.up*(radius+.08f),transform.position+Vector3.up*(c.height-radius),radius,delta.normalized,evasionHits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(n==evasionHits.Length)return false;
        for(int i=0;i<n;i++)if(evasionHits[i].collider!=c)return false;
        if(!Agent.CalculatePath(hit.position,evasionPath)||evasionPath.status!=NavMeshPathStatus.PathComplete)return false;
        end=hit.position;return true;
    }
    void BeginRetreat()
    {
        if(!Evasive||Frozen||Rooted||Dead||world.PlayerDead||!Agent.enabled||!Agent.isOnNavMesh)return;
        Vector3 away=transform.position-CombatTargetPosition;away.y=0;
        if(away.sqrMagnitude<.01f)away=-transform.forward;
        if(BeginEvasion(transform.position+away.normalized*Archetype.Evasion.RetreatDistance))Retreats++;
        // Retreat has its own tell/travel/recovery. Do not restart the independent dodge cooldown on every burst:
        // a fast attack cycle would otherwise postpone dodging forever.
        var e=Archetype.Evasion;
        nextEvasion=Mathf.Max(nextEvasion,Time.time+Mathf.Max(.1f,e.Telegraph)+Mathf.Max(.1f,e.TravelSeconds)+Mathf.Max(.1f,e.Recovery));
    }
    void CancelEvasion()
    {
        if(EvasionState==EvasionPhase.None)return;
        EvasionState=EvasionPhase.None;
        if(Agent!=null&&Agent.enabled&&Agent.isOnNavMesh){Agent.ResetPath();Agent.isStopped=Frozen||Rooted;}
        if(Agent!=null&&evasionRotated)Agent.updateRotation=savedRotation;
        evasionRotated=false;nextPath=0;
    }
}
