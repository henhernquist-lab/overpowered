using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/ApexMutation")]
public sealed class ApexMutationEffect : SynergyEffect
{
    public override bool PreservesTraversalState=>true;
    static bool ExpansionSignatureLive(SynergyRunner r)=>WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&!WorldSession.Instance.MenuOpen&&!r.User.OffensiveActionsBlocked&&(WorldSession.Instance.Mode==null||(!WorldSession.Instance.Mode.Ended&&!WorldSession.Instance.Mode.Paused));
    public override bool AllowsPrimaryActions=>true;
    public override bool CanBegin(SynergyRunner r)=>r.User.IsEquipped(r.Definition.OwnerPrimary);
    public override IEnumerator Execute(SynergyRunner r)
    {
        var x=r.User.Expansion;var power=r.User.Powers.Find(p=>p.Definition==r.Definition.OwnerPrimary);r.User.Select(power);
        x.Apex(r.Definition.Duration);r.User.Hero.PresentPunch();
        x.Vfx.Emit(x.Art.Slash,r.transform.position+Vector3.up,Vector3.up,Vector3.one*2,Vector3.up,.5f,CityColor.Cream,2,180);
        try{float end=Time.time+Mathf.Min(10,r.Definition.Duration);while(Time.time<end&&ExpansionSignatureLive(r))yield return null;}
        finally{x.Apex(0);}
    }
}
