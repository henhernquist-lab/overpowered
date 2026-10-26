using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/TempestFunnel")]
public sealed class TempestFunnelEffect : SynergyEffect
{
    public override bool PreservesTraversalState=>true;
    static bool ExpansionSignatureLive(SynergyRunner r)=>WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&!WorldSession.Instance.MenuOpen&&!r.User.OffensiveActionsBlocked&&(WorldSession.Instance.Mode==null||(!WorldSession.Instance.Mode.Ended&&!WorldSession.Instance.Mode.Paused));
    static bool Valid(CityNpc n,Vector3 center,SynergyRunner r,PowerExpansionRuntime x)=>n!=null&&!n.Dead&&n.Hostile&&(n.transform.position+Vector3.up-center).sqrMagnitude<=r.Definition.Radius*r.Definition.Radius&&x.Visible(center,n);
    public override bool CanBegin(SynergyRunner r)=>true;
    public override IEnumerator Execute(SynergyRunner r)
    {
        var x=r.User.Expansion;Vector3 forward=r.User.AimDirection;forward.y=0;forward.Normalize();Vector3 center=r.transform.position+Vector3.up+forward;
        float end=Time.time+Mathf.Min(4,r.Definition.Duration),next=0;int ticks=0;
        var carried=new List<CityNpc>(8);int maximum=Mathf.Clamp(r.Definition.MaxTargets,1,8);
        while(Time.time<end)
        {
            if(!ExpansionSignatureLive(r))yield break;
            center=x.AdvanceEffect(center,forward*3*Time.deltaTime,.65f);
            foreach(var candidate in x.Scan(center,forward,r.Definition.Radius,180,maximum))
                if(carried.Count<maximum&&!carried.Contains(candidate))carried.Add(candidate);
            foreach(var n in carried)
            {
                if(!Valid(n,center,r,x))continue;
                Vector3 delta=center-(n.transform.position+Vector3.up);delta.y=0;
                NpcPowerDisplacement.MoveSafe(n,Vector3.ClampMagnitude(delta,1)*4*n.PowerDisplacementFactor*Time.deltaTime);
            }
            if(Time.time>=next&&ticks<6)
            {
                ticks++;next=Time.time+.5f;
                foreach(var n in carried)if(Valid(n,center,r,x))n.Damage(r.ActivationDamage/6,r.User,true,r.Definition.Force/6,center);
                for(int i=0;i<7;i++)x.Vfx.Emit(x.Art.WindArc,center+Vector3.up*(i*.35f-.7f),Vector3.up,Vector3.one*(.8f+i*.12f),forward*2,.6f,CityColor.Cyan,0,270+i*20);
            }
            yield return null;
        }
    }
}
