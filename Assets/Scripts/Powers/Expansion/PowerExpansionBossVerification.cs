#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
public sealed partial class PowerExpansionVerificationRunner
{
    IEnumerator WaitBossWindup(PulsebreakerBoss boss)
    {
        for(float end=Time.time+8;boss.State!=PulsebreakerState.Windup&&Time.time<end;)yield return null;
        Check(boss.State==PulsebreakerState.Windup&&boss.TelegraphVisible&&boss.Windups>0,"Actual live Pulsebreaker committed a visible cannon windup");
    }
    CityNpc LiveBoss(float distance)
    {
        var npc=Npc("Pulsebreaker",new Vector3(0,.1f,distance));npc.SetCombatStats(10000,0);npc.enabled=true;
        var boss=npc.GetComponent<PulsebreakerBoss>();boss.ForceNextAttack(PulsebreakerAttack.Cannon);return npc;
    }
    IEnumerator BossCombatChecks()
    {
        foreach(string id in new[]{"aerokinesis","sonic-manipulation"})
        {
            yield return EnterExpansion(id);yield return ClearActors();var npc=LiveBoss(6);var boss=npc.GetComponent<PulsebreakerBoss>();yield return WaitBossWindup(boss);Ready();int cancels=boss.Cancels;
            float hp=npc.CurrentHealth;Check(W.Powers.Use(W.Powers.Selected),id+": activate during actual boss windup");
            Check(boss.Cancels==cancels+1&&!boss.TelegraphVisible&&npc.CurrentHealth<hp,id+": real boss attack cancels with authoritative damage");
            yield return new WaitForSeconds(.25f);Check(!npc.Rooted,"Boss control expires after its resisted duration");yield return WaitBossWindup(boss);
            cancels=boss.Cancels;Ready();W.Powers.Use(W.Powers.Selected);Check(boss.Cancels==cancels&&!npc.Rooted,"CONTROL repeated boss control inside six-second refractory cannot cancel another windup");
            yield return new WaitForSeconds(1.5f);Check(boss.Releases>0&&npc.Agent.enabled&&npc.Agent.isOnNavMesh,"Boss resumes a real attack and keeps valid navigation after resisted control");
        }
        string[] owners={"aerokinesis","sonic-manipulation","combat-morphing","plasma-blades"};string[] ids={"tempest-funnel","resonance-cascade","apex-mutation","blade-cyclone"};
        for(int i=0;i<4;i++)
        {
            yield return EnterSignature(owners[i],ids[i]);var npc=LiveBoss(i>=2?2.3f:5);var boss=npc.GetComponent<PulsebreakerBoss>();yield return WaitBossWindup(boss);Ready();float hp=npc.CurrentHealth;
            var runner=W.Powers.SynergyRunner;Check(runner.TryActivate(),ids[i]+": actual shipping signature during live boss encounter");
            if(i==2)Check(W.Powers.Use(W.Powers.Selected),"Apex permits a real fitted morph attack against the live boss");
            yield return new WaitForSeconds(i==2?7.3f:3.3f);
            Check(!runner.Busy&&!runner.DrivesMotion&&npc.CurrentHealth<hp&&npc.Agent.enabled&&npc.Agent.isOnNavMesh,ids[i]+": finite encounter damage without disabling boss navigation");
            string source=i==2?"combat-morphing":"synergy:"+ids[i];var hud=FindAnyObjectByType<GameHud>();yield return null;yield return null;
            Check(hud.EnemyDamageHistory.Any(x=>x.Target==npc&&x.Attacker==W.Powers&&x.SourceId==source),ids[i]+": existing resolved-damage number path receives correct attacker/source");
            Check(hud.BossBarVisible&&Mathf.Abs(hud.BossFraction-npc.HealthFraction)<.001f,ids[i]+": actual boss bar reads authoritative remaining HP");
            Check(!npc.Rooted&&boss.Windups>0&&boss.Releases>0,ids[i]+": boss continues real telegraphed combat after signature");
            yield return PictureHud(ids[i]+"-live-boss");
        }
        yield return EnterSignature("aerokinesis","tempest-funnel");Ready();
        for(int i=0;i<10;i++)Npc("Rusher",new Vector3((i%2==0?-.6f:.6f),.1f,2+i*.8f));
        var affected=new System.Collections.Generic.HashSet<CityNpc>();Action<NpcDamageResult> count=r=>{if(r.SourceId=="synergy:tempest-funnel")affected.Add(r.Target);};CityNpc.DamageApplied+=count;
        Check(W.Powers.SynergyRunner.TryActivate(),"Crowded Tempest scenario uses real shipping signature");yield return new WaitForSeconds(3.3f);CityNpc.DamageApplied-=count;
        Check(affected.Count>0&&affected.Count<=W.Powers.Synergy.MaxTargets,"CONTROL moving Tempest affects at most six distinct enemies over its entire lifetime, not six new targets each frame");
    }
    IEnumerator PictureHud(string name)
    {
        string directory="Verification/PowerExpansion2/images/";Directory.CreateDirectory(directory);
        var camera=Camera.main;var hud=FindAnyObjectByType<GameHud>();var texture=new RenderTexture(1280,720,24);texture.Create();camera.targetTexture=texture;hud.Panel.targetTexture=texture;hud.OverlayPanel.targetTexture=texture;
        yield return null;yield return null;yield return null;
        var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(directory+name+".png",pixels.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;hud.Panel.targetTexture=null;hud.OverlayPanel.targetTexture=null;texture.Release();Destroy(texture);Destroy(pixels);
    }
}
#endif
