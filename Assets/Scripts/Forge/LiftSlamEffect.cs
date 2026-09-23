using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Effects/Lift and slam")]
public sealed class LiftSlamEffect : SynergyEffect
{
    public bool EnemyOnly, Freeze;
    public override bool CanBegin(SynergyRunner r)
    {
        if(!r.User.FindTarget(r.Definition.Range,out var hit))return false;
        var npc=hit.collider.GetComponentInParent<CityNpc>();
        if(npc!=null&&!npc.Dead){r.Captive=npc;return r.GroundTarget(true,out r.Target);}
        if(EnemyOnly||hit.rigidbody==null||hit.rigidbody.isKinematic||hit.rigidbody.mass>r.Definition.MaxMass)return false;
        r.CaptiveBody=hit.rigidbody;return r.GroundTarget(true,out r.Target);
    }
    public override IEnumerator Execute(SynergyRunner r)
    {
        Rigidbody body=r.CaptiveBody;
        if(r.Captive!=null)
        {
            if(Freeze)r.Captive.Freeze(r.Definition.FreezeSeconds);
            var suspension=r.Captive.GetComponent<SynergySuspension>()??r.Captive.gameObject.AddComponent<SynergySuspension>();
            body=suspension.Begin(r.Captive);r.Suspension=suspension;
        }
        r.HoldOne(body);
        float elapsed=0,nextFx=0;
        while(elapsed<r.Definition.LiftSeconds&&body!=null)
        {
            Vector3 target=r.transform.position+Vector3.up*r.Definition.OrbitRadius;
            body.AddForce((target-body.position)*r.Definition.Spring-body.linearVelocity*r.Definition.Damping,ForceMode.Acceleration);
            if(elapsed>=nextFx){r.Vfx.Burst(body.position,r.Definition);nextFx=elapsed+.15f;}
            elapsed+=Time.fixedDeltaTime;yield return SynergyRunner.FixedStep;
        }
        if(body!=null)
        {
            r.ReleaseHeld();body.useGravity=true;
            body.AddForce((r.Target-body.position).normalized*r.Definition.Force,ForceMode.Impulse);
            var payload=body.GetComponent<SynergyPayload>()??body.gameObject.AddComponent<SynergyPayload>();
            payload.Arm(r,false,true);
            if(r.Suspension!=null)r.Suspension.Release();
            float until=Time.time+r.Definition.Duration;
            while(payload.enabled&&Time.time<until)yield return null;
        }
        r.Captive=null;r.CaptiveBody=null;r.Suspension=null;
    }
}
