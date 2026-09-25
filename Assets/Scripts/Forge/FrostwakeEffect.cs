using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Effects/Flight burst")]
public sealed class FrostwakeEffect : SynergyEffect
{
    public override bool CanBegin(SynergyRunner r)=>r.User.Flight!=null&&r.User.Flight.Fuel>0;
    public override IEnumerator Execute(SynergyRunner r)
    {
        var d=r.Definition;Vector3 direction=r.User.AimDirection;
        direction.y=Mathf.Clamp(direction.y,-.2f,.35f);direction.Normalize();
        r.PassActors();r.DrivesMotion=true;float elapsed=0,nextTrail=0;
        while(elapsed<d.Duration&&r.User.ConsumeFlight(Time.deltaTime))
        {
            var flags=r.Move(direction*d.DiveSpeed+Vector3.up*2,true);
            if((flags&CollisionFlags.Sides)!=0)break;
            r.AffectNearby(r.transform.position,d.Radius,d.Damage,d.FreezeSeconds,false);
            if(elapsed>=nextTrail){r.Vfx.Burst(r.transform.position,d);nextTrail=elapsed+.15f;}
            elapsed+=Time.deltaTime;yield return null;
        }
    }
}
