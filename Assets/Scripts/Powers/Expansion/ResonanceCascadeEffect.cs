using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/ResonanceCascade")]
public sealed class ResonanceCascadeEffect : SynergyEffect
{
    public override bool PreservesTraversalState=>true;
    static bool ExpansionSignatureLive(SynergyRunner r)=>WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&!WorldSession.Instance.MenuOpen&&!r.User.OffensiveActionsBlocked&&(WorldSession.Instance.Mode==null||(!WorldSession.Instance.Mode.Ended&&!WorldSession.Instance.Mode.Paused));
    public override bool CanBegin(SynergyRunner r)=>r.User.Expansion.Scan(r.User.AimOrigin,r.User.AimDirection,r.Definition.Range,65,r.Definition.MaxTargets).Count>0;
    public override IEnumerator Execute(SynergyRunner r)
    {
        var x=r.User.Expansion;var marked=new List<CityNpc>(x.Scan(r.User.AimOrigin,r.User.AimDirection,r.Definition.Range,65,r.Definition.MaxTargets));
        foreach(var n in marked)x.Vfx.Emit(x.Art.SoundRing,n.transform.position+Vector3.up,Vector3.up,Vector3.one*.55f,Vector3.zero,.4f,CityColor.UiPurple,2,90);
        for(int pulse=0;pulse<3;pulse++)
        {
            yield return new WaitForSeconds(.3f);if(!ExpansionSignatureLive(r))yield break;
            int index=0;
            foreach(var n in marked)
            {
                if(n==null||n.Dead||!n.Hostile||!x.Visible(r.User.AimOrigin,n)||(n.transform.position-r.transform.position).sqrMagnitude>r.Definition.Range*r.Definition.Range)continue;
                float falloff=Mathf.Max(.55f,1-index++*.08f)*(1-pulse*.15f);
                n.Damage(r.ActivationDamage/3*falloff,r.User,true,r.Definition.Force/3,r.User.AimOrigin);n.TryPowerInterrupt(.25f,n.IsBoss?6:1.2f);
                x.Vfx.Emit(x.Art.SoundRing,n.transform.position+Vector3.up,-r.User.AimDirection,Vector3.one*.6f,Vector3.zero,.4f,CityColor.UiPurple,5,0);
            }
        }
    }
}
