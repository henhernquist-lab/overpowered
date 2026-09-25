using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Effects/Slam")]
public sealed class SonicSlamEffect : SynergyEffect
{
    public bool Targeted;
    public override bool CanBegin(SynergyRunner r)=>r.GroundTarget(Targeted,out r.Target);
    public override IEnumerator Execute(SynergyRunner r)
    {
        var d=r.Definition;
        r.DrivesMotion=true;
        float elapsed=0;
        while(elapsed<d.LiftSeconds)
        {
            r.Move(Vector3.up*d.LiftSpeed,true);
            elapsed+=Time.deltaTime; yield return null;
        }
        elapsed=0;
        while(elapsed<d.Duration)
        {
            Vector3 direction=Targeted?(r.Target-r.transform.position).normalized:Vector3.down;
            if(direction.y>-.15f)direction=(direction+Vector3.down).normalized;
            var flags=r.Move(direction*d.DiveSpeed,true);
            if((flags&CollisionFlags.Below)!=0)
            {
                r.Impact(r.transform.position+Vector3.up*.2f);
                break;
            }
            elapsed+=Time.deltaTime;yield return null;
        }
    }
}
