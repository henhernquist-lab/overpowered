using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/BladeCyclone")]
public sealed class BladeCycloneEffect : SynergyEffect
{
    public override bool PreservesTraversalState=>true;
    static bool ExpansionSignatureLive(SynergyRunner r)=>WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&!WorldSession.Instance.MenuOpen&&!r.User.OffensiveActionsBlocked&&(WorldSession.Instance.Mode==null||(!WorldSession.Instance.Mode.Ended&&!WorldSession.Instance.Mode.Paused));
    public override bool CanBegin(SynergyRunner r)=>r.User.Expansion.Scan(r.User.AimOrigin,r.User.AimDirection,r.Definition.Range,160,r.Definition.MaxTargets).Count>0;
    public override IEnumerator Execute(SynergyRunner r)
    {
        var x=r.User.Expansion;var targets=new List<CityNpc>(x.Scan(r.User.AimOrigin,r.User.AimDirection,r.Definition.Range,160,r.Definition.MaxTargets));
        r.User.Select(r.User.Powers.Find(p=>p.Definition==r.Definition.OwnerPrimary));r.DrivesMotion=true;
        try{
            foreach(var n in targets)
            {
                if(!ExpansionSignatureLive(r))yield break;if(n==null||n.Dead||!n.Hostile)continue;
                float until=Time.time+.25f;
                while(n!=null&&!n.Dead&&Time.time<until&&Vector3.Distance(n.transform.position,r.transform.position)>2)
                {
                    if(!ExpansionSignatureLive(r))yield break;Vector3 direction=n.transform.position-r.transform.position;direction.y=0;
                    r.Move(direction.normalized*12+Vector3.down*2,false);yield return null;
                }
                for(int strike=0;strike<3;strike++)
                {
                    if(!ExpansionSignatureLive(r))yield break;
                    if(n==null||n.Dead||Vector3.Distance(n.transform.position,r.transform.position)>3.2f||!x.Visible(r.transform.position+Vector3.up,n))break;
                    r.User.Hero.PresentPunch();n.Damage(r.ActivationDamage*(strike==2? .6f:.2f),r.User,true,r.Definition.Force/3,r.transform.position+Vector3.up);
                    x.Vfx.Emit(x.Art.Slash,n.transform.position+Vector3.up,r.User.AimDirection,Vector3.one*(strike==2?1.8f:1),Vector3.zero,.25f,CityColor.Cyan,1,450);yield return new WaitForSeconds(.13f);
                }
            }
        }finally{r.DrivesMotion=false;}
    }
}
