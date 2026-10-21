#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

public sealed partial class CombatLabVerificationRunner
{
    IEnumerator VerifyBulwark()
    {
        PlaceHero(Vector3.zero);var npc=At("Bulwark",new Vector3(0,0,10));
        Check(npc!=null&&npc.HasGuard,"Bulwark registered with directional defense");if(npc==null)yield break;
        npc.transform.rotation=Quaternion.LookRotation(Vector3.back);
        Vector3 centre=npc.GetComponent<Collider>().bounds.center;
        Vector3 front=centre+npc.transform.forward*5, rear=centre-npc.transform.forward*5,side=centre+npc.transform.right*5;
        float hp=npc.Health;int count=0;NpcDamageResult result=default;
        Action<NpcDamageResult> handler=r=>{if(r.Target==npc){result=r;count++;}};CityNpc.DamageApplied+=handler;
        npc.Damage(10,W.Powers,true,0,front);
        Check(Mathf.Abs(hp-npc.Health-3.5f*W.Difficulty.PlayerDamage)<.01f,"Frontal guard reduces damage without immunity");
        Check(count==1&&Mathf.Abs(result.ResolvedDamage-3.5f*W.Difficulty.PlayerDamage)<.01f&&result.GuardAbsorbed>0,"Single authoritative result reports reduced damage");
        hp=npc.Health;npc.Damage(10,W.Powers,false,0,side);Check(Mathf.Abs(hp-npc.Health-10*W.Difficulty.PlayerDamage)<.01f,"Flank bypasses guard");
        hp=npc.Health;npc.Damage(10,W.Powers,false,0,rear);Check(Mathf.Abs(hp-npc.Health-10*W.Difficulty.PlayerDamage)<.01f,"Rear bypasses guard");
        hp=npc.Health;npc.Damage(10,W.Powers,true,npc.Archetype.Guard.BreakImpulse,front);
        Check(npc.GuardBroken&&Mathf.Abs(hp-npc.Health-10*W.Difficulty.PlayerDamage)<.01f,"Strong generic impact disrupts guard before damage");
        npc.Damage(5,W.Powers,false,0,front);Check(result.GuardAbsorbed==0,"Broken guard remains fully vulnerable");
        yield return new WaitForSeconds(npc.Archetype.Guard.BreakSeconds+.1f);
        Check(!npc.GuardBroken&&npc.GuardIntegrity>0,"Guard break expires and integrity recharges");
        npc.Root(.35f);Check(!npc.GuardRaised&&npc.GuardBroken,"Generic Darkness-style root disrupts guard");
        npc.Freeze(.35f);Check(!npc.GuardRaised,"Generic Ice freeze lowers shield");
        yield return new WaitForSeconds(npc.Archetype.Guard.BreakSeconds+.15f);
        Check(!npc.GuardBroken&&!npc.Frozen&&!npc.Rooted,"Control and guard disruption are finite");
        CityNpc.DamageApplied-=handler;Lab.Clear();yield return null;
        npc=At("Bulwark",new Vector3(0,0,1.8f),1000,5);float health=W.Health;float end=Time.time+7;
        bool recovery=false,windup=false;
        while(Time.time<end&&npc.Releases<1)
        {
            if(npc.Phase==AttackPhase.Windup){windup=true;Check(!npc.GuardRaised,"Shield drops during committed bash windup");break;}
            yield return null;
        }
        while(Time.time<end&&npc.Releases<1)yield return null;
        recovery=npc.Phase==AttackPhase.Recover&&!npc.GuardRaised;
        Check(windup&&npc.Releases>0&&npc.LastMeasuredWindup>=npc.Archetype.WindupSeconds-.04f,"Heavy bash waits for the configured readable windup");
        Check(recovery,"Bash exposes an unguarded recovery window");Check(W.Health<health,"Bulwark bash damages through the existing player-health path");
        npc.Damage(100000,W.Powers);Check(npc.Dead&&!npc.GuardRaised,"Bulwark can die and never guards while dead");
    }
}
#endif
