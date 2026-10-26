using UnityEngine;

public sealed partial class CityNpc
{
    public bool HasGuard=>Archetype!=null&&Archetype.Guard!=null&&Archetype.Guard.Enabled;
    public bool GuardBroken=>HasGuard&&Time.time<guardBrokenUntil;
    public bool GuardRaised=>HasGuard&&enabled&&!Dead&&!Frozen&&!Rooted&&!GuardBroken&&
        (Phase==AttackPhase.Approach||Phase==AttackPhase.Engage)&&Agent!=null&&Agent.enabled&&Agent.isOnNavMesh;
    public float GuardIntegrity=>HasGuard?(GuardBroken?0:Time.time>=guardBrokenUntil&&guardRefillPending?Archetype.Guard.Capacity:guardIntegrity):0;
    public int GuardHits {get;private set;}
    public int GuardBreaks {get;private set;}
    public float GuardHitAt {get;private set;}=-100;
    public float GuardBreakEndsAt=>guardBrokenUntil;
    float guardIntegrity,guardBrokenUntil;bool guardRefillPending;
    void InitializeDefense()
    {
        if(!HasGuard)return;guardIntegrity=Mathf.Max(1,Archetype.Guard.Capacity);EnemyEquipment.Attach(this);
    }
    /// A generic stagger lease, extended by strong impact/freeze/root. There is no power-ID dispatch.
    public void DisruptGuard(float seconds)
    {
        if(!HasGuard||Dead||seconds<=0)return;
        if(!GuardBroken)GuardBreaks++;
        guardBrokenUntil=Mathf.Max(guardBrokenUntil,Time.time+seconds);guardIntegrity=0;guardRefillPending=true;
    }
    void TurnGuard(bool aggro)
    {
        if(!HasGuard)return;
        if(guardRefillPending&&!GuardBroken){guardIntegrity=Mathf.Max(1,Archetype.Guard.Capacity);guardRefillPending=false;}
        Agent.updateRotation=!aggro;
        if(!aggro||Phase==AttackPhase.Windup||Frozen||Rooted)return;
        Vector3 toward=CombatTargetPosition-transform.position;toward.y=0;
        if(toward.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(toward),Mathf.Max(1,Archetype.Guard.TurnDegreesPerSecond)*Time.deltaTime);
    }
    float ResolveGuardDamage(float damage,Vector3? origin,float impulse)
    {
        if(!HasGuard)return damage;
        var g=Archetype.Guard;
        if(impulse>=Mathf.Max(1,g.BreakImpulse))DisruptGuard(Mathf.Max(.25f,g.BreakSeconds));
        if(!GuardRaised||!origin.HasValue)return damage;
        var c=GetComponent<CapsuleCollider>();Vector3 from=origin.Value-(transform.position+Vector3.up*c.height*.5f);
        // Include elevation: the shield is a forward surface, not an overhead dome.
        if(from.sqrMagnitude<.001f||!EnemyGuardMath.InArc(Vector3.Dot(transform.forward,from.normalized),g.ArcDegrees))return damage;
        if(guardRefillPending){guardIntegrity=Mathf.Max(1,g.Capacity);guardRefillPending=false;}
        float resolved=EnemyGuardMath.Block(damage,g.DamageReduction,ref guardIntegrity,out bool broken);
        GuardHits++;GuardHitAt=Time.time;
        if(broken)DisruptGuard(Mathf.Max(.25f,g.BreakSeconds));
        return resolved;
    }
}
