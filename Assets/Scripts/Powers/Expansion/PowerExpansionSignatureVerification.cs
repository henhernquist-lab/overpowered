#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;
public sealed partial class PowerExpansionVerificationRunner
{
    IEnumerator EnterSignature(string primary,string signature)
    {
        yield return Home();var p=Power(primary);p.Selectable=true;var registry=Resources.Load<SignatureCatalog>("SignatureCatalog");
        var ability=Resources.Load<PowerSynergyDefinition>("PowerExpansion/"+signature);
        if(!registry.Abilities.Contains(ability))registry.Abilities=registry.Abilities.Concat(new[]{ability}).ToArray();SignatureCatalog.VerificationPreview=Phase=="e";
        foreach(var h in F.Heroes)if(!h.AvailablePowers.Contains(p))h.AvailablePowers=h.AvailablePowers.Concat(new[]{p}).ToArray();
        Check(Profile.SetLoadout(F.Heroes[0],p,Power("ice"),Profile.EquippedSecondary,F.Heroes[0].Primary,F.Heroes[0].Secondary),"Staged signature owner passes actual equip gates: "+primary);
        Check(Profile.SetSignature(signature),"Actual saved signature choice: "+signature);
        Check(GameFlow.Instance.OpenCombatLab(),"Enter real lab with "+signature);yield return Scene(CombatLab.SceneName);W.Rolls.FixedUnit=.5f;
        var view=Camera.main.GetComponent<ThirdPersonCamera>();if(!view.FirstPerson)view.ToggleView();view.SetLook(0,0);yield return null;yield return ClearActors();
        registry=Resources.Load<SignatureCatalog>("SignatureCatalog");if(!registry.Abilities.Contains(W.Powers.Synergy))registry.Abilities=registry.Abilities.Concat(new[]{W.Powers.Synergy}).ToArray();
    }
    IEnumerator SignatureChecks()
    {
        string[] primaries={"aerokinesis","sonic-manipulation","combat-morphing","plasma-blades"};
        string[] names={"tempest-funnel","resonance-cascade","apex-mutation","blade-cyclone"};
        for(int index=0;index<4;index++)
        {
            yield return EnterSignature(primaries[index],names[index]);Ready();var runner=W.Powers.SynergyRunner;
            var target=Npc("Rusher",new Vector3(0,.1f,index==3?2.5f:index==2?2.2f:4));var boss=Npc("Pulsebreaker",new Vector3(1,.1f,index==3?3:5));yield return null;
            float hp=target.CurrentHealth,bossHp=boss.CurrentHealth;Vector3 capsule=W.Hero.GetComponent<CharacterController>().bounds.size;
            int hits=0;bool credit=true;System.Action<NpcDamageResult> onHit=r=>{if(r.Target==target||r.Target==boss){hits++;credit&=r.Attacker==W.Powers&&r.SourceId=="synergy:"+names[index];}};CityNpc.DamageApplied+=onHit;
            float energy=W.Powers.Energy;
            var registry=Resources.Load<SignatureCatalog>("SignatureCatalog");
            // The Home Forge tears down and reloads its resource catalog across scenes; stage only in memory again here.
            if(!registry.Abilities.Contains(runner.Definition))registry.Abilities=registry.Abilities.Concat(new[]{runner.Definition}).ToArray();
            bool activated=runner.TryActivate();Check(activated,names[index]+": actual signature gate activates / "+runner.StatusText+" / equipped="+runner.Definition?.Id+" / energy="+W.Powers.Energy);
            Check(!runner.TryActivate(),"CONTROL signature repeat refused while busy/cooling down");
            Check(Mathf.Abs(W.Powers.Energy-(energy-runner.EnergyCost))<.01f,"Signature energy paid once through existing runner");
            if(index==2)
            {
                Check(Expansion.ApexActive&&W.Powers.Selected.Definition.Id==primaries[index],"Apex explicitly manifests morph weapons");
                CityNpc.DamageApplied-=onHit;int impacts=Expansion.MeleeImpacts;Ready();Check(W.Powers.Use(W.Powers.Selected),"Apex permits actual morph primaries while its timer runs");yield return new WaitForSeconds(.3f);
                Check(Expansion.MeleeImpacts==impacts+1&&Mathf.Abs((hp-target.CurrentHealth)-W.Powers.Stats(W.Powers.Selected).Damage*1.45f*W.Difficulty.PlayerDamage)<.02f,"Apex buffs only morph strike damage through authoritative pipeline");
                Check(W.Hero.GetComponent<CharacterController>().bounds.size==capsule,"Apex changes no physics capsule");
                runner.Cancel();Check(!Expansion.ApexActive&&!runner.Busy,"Cancellation restores normal morph immediately");
                Ready();float before=target.CurrentHealth;W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.3f);Check(Mathf.Abs((before-target.CurrentHealth)-W.Powers.Stats(W.Powers.Selected).Damage*W.Difficulty.PlayerDamage)<.02f,"Normal morph damage restored after Apex ends");
            }
            else
            {
                Check(!W.Powers.Use(W.Powers.Selected),"CONTROL other signatures retain existing primary lock");
                yield return new WaitForSeconds(index==0?3.3f:index==1?1.2f:3);
                Check(!runner.Busy&&!runner.DrivesMotion,names[index]+": finite signature and movement end");
                Check(hits>0&&credit&&target.CurrentHealth<hp,names[index]+": real damage with deferred signature ownership");
                Check(boss.CurrentHealth<bossHp,"Pulsebreaker remains a finite valid signature target");
                if(index==1)Check(target.PowerInterrupts<=1&&boss.PowerInterrupts<=1,"CONTROL linked sonic pulses respect interrupt refractory");
                CityNpc.DamageApplied-=onHit;
            }
            yield return new WaitForSeconds(.8f);Check(Expansion.Vfx.LiveCount==0,"Signature expansion effects fully expire");
            Check(W.Hero.GetComponentsInChildren<Animator>().Length==1,"Signatures retain one existing humanoid Animator");
        }
        yield return EnterSignature("plasma-blades","blade-cyclone");var shielded=Npc("Rusher",new Vector3(0,.1f,4));var wall=Wall(new Vector3(0,1,2),new Vector3(5,4,.4f));yield return null;
        var finalRegistry=Resources.Load<SignatureCatalog>("SignatureCatalog");Check(finalRegistry.Eligible(W.Powers.Synergy,W.Powers.EquippedA,W.Powers.EquippedB),"Wall CONTROL has a valid implemented equipped signature");
        Check(!W.Powers.SynergyRunner.TryActivate()&&W.Powers.SynergyRunner.Feedback=="No valid target","CONTROL Blade Cyclone refuses target behind a solid building for visibility, not a configuration failure");Destroy(wall);
        Log("LIMIT: bounded native signature mechanics verified; human visual/balance approval and standalone performance remain unverified.");
    }
}
#endif
