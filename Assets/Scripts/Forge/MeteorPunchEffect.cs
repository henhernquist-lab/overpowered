using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Effects/Charged melee")]
public sealed class MeteorPunchEffect : SynergyEffect
{
    public override bool CanBegin(SynergyRunner r)=>r.User.Strength.Charges>0&&r.User.Strength.Cooldown<=0;
    public override IEnumerator Execute(SynergyRunner r)
    {
        var pose=r.User.GetComponent<HumanoidPresentation>();pose.Cast();
        float elapsed=0;
        while(elapsed<r.Definition.LiftSeconds){r.Vfx.Burst(r.transform.position+Vector3.up,r.Definition);elapsed+=.15f;yield return new WaitForSeconds(.15f);}
        // Existing punch event/clip and configured windup, with independent synergy cooldown.
        r.User.Hero.PresentPunch();
        yield return new WaitForSeconds(r.User.Hero.PunchWindupSeconds);
        r.Impact(r.transform.position+Vector3.up+r.transform.forward*1.7f);
    }
}
