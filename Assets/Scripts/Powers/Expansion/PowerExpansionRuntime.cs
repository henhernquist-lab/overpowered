using System.Collections.Generic;
using UnityEngine;
public sealed partial class PowerExpansionRuntime : MonoBehaviour
{
    public PowerUser User{get;private set;}
    public PowerExpansionArt Art{get;private set;}
    public PowerExpansionTuning Tuning{get;private set;}
    public ExpansionVfx Vfx{get;private set;}
    readonly Collider[] hits=new Collider[96];readonly RaycastHit[] rays=new RaycastHit[48];
    readonly HashSet<CityNpc> unique=new HashSet<CityNpc>();
    public readonly List<CityNpc> Targets=new List<CityNpc>(16);
    public int RangedHits{get;private set;}
    public void Initialize(PowerUser user)
    {
        weaponBlock=new MaterialPropertyBlock();User=user;Art=Resources.Load<PowerExpansionArt>("PowerExpansionArt");Tuning=Resources.Load<PowerExpansionTuning>("PowerExpansionTuning");
        Vfx=gameObject.AddComponent<ExpansionVfx>();Vfx.Initialize();
    }
    public bool Visible(Vector3 from,CityNpc npc)
    {
        Vector3 delta=npc.transform.position+Vector3.up-from;float distance=delta.magnitude;
        int count=Physics.RaycastNonAlloc(from,delta.normalized,rays,distance,~0,QueryTriggerInteraction.Ignore);if(count>=rays.Length)return false;
        for(int i=0;i<count;i++)if(rays[i].transform.root!=transform&&rays[i].collider.GetComponentInParent<CityNpc>()==null&&rays[i].distance<distance-.05f)return false;
        return true;
    }
    public List<CityNpc> Scan(Vector3 origin,Vector3 forward,float range,float halfAngle,int maximum)
    {
        Targets.Clear();unique.Clear();int count=Physics.OverlapSphereNonAlloc(origin,range,hits,~0,QueryTriggerInteraction.Ignore);
        float cosine=Mathf.Cos(halfAngle*Mathf.Deg2Rad);
        for(int i=0;i<count&&Targets.Count<Mathf.Min(16,maximum);i++)
        {
            var n=hits[i].GetComponentInParent<CityNpc>();if(n==null||n.Dead||!n.Hostile||!unique.Add(n))continue;
            var direction=n.transform.position+Vector3.up-origin;
            if(direction.sqrMagnitude>range*range||Vector3.Dot(direction.normalized,forward)<cosine||!Visible(origin,n))continue;
            Targets.Add(n);
        }
        for(int i=1;i<Targets.Count;i++)
        {
            var item=Targets[i];float distance=(item.transform.position-origin).sqrMagnitude;int j=i-1;
            while(j>=0&&(Targets[j].transform.position-origin).sqrMagnitude>distance){Targets[j+1]=Targets[j];j--;}Targets[j+1]=item;
        }
        return Targets;
    }
    public bool Directional(PowerRuntime power,bool sonic)
    {
        if(Art==null||Tuning==null)return false;
        var stats=User.Stats(power);Vector3 origin=User.AimOrigin,forward=(User.AimPoint(stats.Range)-origin).normalized;
        if(forward.sqrMagnitude<.001f)forward=transform.forward;
        float damage=User.RollDamage(stats.Damage,stats.DamageVariance);
        var targets=Scan(origin,forward,stats.Range,sonic?Tuning.SonicHalfAngle:Tuning.WindHalfAngle,Tuning.MaxTargets);
        foreach(var npc in targets)
        {
            npc.Damage(damage,User,true,stats.Force,origin);RangedHits++;
            if(!npc.Dead)
            {
                npc.TryPowerInterrupt(sonic?Tuning.SonicStagger:Tuning.WindInterrupt,npc.IsBoss?Tuning.BossInterruptRefractory:Tuning.InterruptRefractory);
                if(!sonic){var push=npc.GetComponent<NpcPowerDisplacement>()??npc.gameObject.AddComponent<NpcPowerDisplacement>();push.Begin(npc,forward,Tuning.WindDistance,Tuning.WindSeconds);}
            }
            Vfx.Emit(Art.Slash,npc.transform.position+Vector3.up,forward,Vector3.one*.5f,Vector3.zero,.2f,sonic?CityColor.UiPurple:CityColor.Cream,2,90);
        }
        for(int i=0;i<3;i++)Vfx.Emit(sonic?Art.SoundRing:Art.WindArc,origin+forward*(.7f+i*.4f),forward,Vector3.one*(sonic? .5f+i*.15f:.9f),forward*(sonic?14:22),sonic? .5f:.4f,sonic?CityColor.UiPurple:CityColor.Cyan,sonic?4:1,sonic?0:120);
        if(!sonic)
        {
            Vector3 sample=transform.position+forward*2+Vector3.up;
            if(Physics.Raycast(sample,Vector3.down,out var ground,2,~0,QueryTriggerInteraction.Ignore))
                for(int i=0;i<3;i++)Vfx.Emit(Art.Slash,ground.point+Vector3.up*.1f+transform.right*(i-1)*.5f,Vector3.up,Vector3.one*.25f,forward*5+Vector3.up*.35f,.35f,CityColor.Sand,2,80);
        }
        return true;
    }
}
