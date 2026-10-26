#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
public sealed partial class PowerExpansionVerificationRunner
{
    IEnumerator PresentationChecks()
    {
        foreach(string difficulty in new[]{"normal","hard","extreme"})
        {
            yield return Home();Profile.SetDifficulty(difficulty);yield return EnterExpansion("plasma-blades");yield return ClearActors();
            var npc=Npc("Rusher",new Vector3(0,.1f,2.2f));var presentation=W.Hero.GetComponent<HumanoidPresentation>();Ready();float hp=npc.CurrentHealth,energy=W.Powers.Energy;
            Check(!Power("plasma-blades").CastingPresentation&&Expansion.ChargedStrike(),difficulty+": charged primary avoids generic Cast override");
            float expectedEnergy=(Power("plasma-blades").ResourceCost+8)*W.Difficulty.EnergyUsage;
            Check(Mathf.Abs(W.Powers.Energy-(energy-expectedEnergy))<.02f,difficulty+": base plus extra charge energy scale centrally once");
            Check(presentation.State=="Punch"&&Mathf.Abs(presentation.Animator.GetFloat("ActionRate")-(presentation.Tuning.PunchImpactSeconds-presentation.Tuning.PunchStartSeconds)/.4f)<.002f,"Actual existing Punch clip fitted to committed charge windup");
            float start=Time.time,impact=-1;Action<NpcDamageResult> handler=r=>{if(r.Target==npc)impact=Time.time;};CityNpc.DamageApplied+=handler;
            yield return new WaitForSeconds(.2f);Check(npc.CurrentHealth==hp,"CONTROL charging windup has no early damage");yield return new WaitForSeconds(.3f);
            Check(impact>=start+.38f&&presentation.LastAnimationImpactTime>=start+.37f&&Mathf.Abs(presentation.LastAnimationImpactTime-impact)<.06f,"Fitted animation marker follows real deferred charge impact within native frame tolerance");CityNpc.DamageApplied-=handler;
            Picture("charged-plasma-"+difficulty);
        }
        yield return Home();Profile.SetDifficulty("normal");yield return EnterExpansion("combat-morphing");yield return ClearActors();
        var target=Npc("Brute",new Vector3(0,.1f,2.1f));var p=W.Hero.GetComponent<HumanoidPresentation>();
        for(int i=0;i<3;i++)
        {
            Ready();var form=Expansion.Form;Check(W.Powers.Use(W.Powers.Selected),"Fitted morph attack: "+form);
            float expected=form==CombatMorphForm.HammerFists? .42f:form==CombatMorphForm.ExtendedClaws? .2f:.14f;
            Check(p.State=="Punch"&&Mathf.Abs(p.Animator.GetFloat("ActionRate")-(p.Tuning.PunchImpactSeconds-p.Tuning.PunchStartSeconds)/expected)<.002f,form+": existing Animator owns marker-fitted weapon action");
            Expansion.CycleForm();yield return null;
            var mesh= form==CombatMorphForm.HammerFists?Expansion.Art.MorphHammer:form==CombatMorphForm.ExtendedClaws?Expansion.Art.MorphClaws:Expansion.Art.MorphBlade;
            Check(W.Hero.GetComponentsInChildren<MeshFilter>().Where(x=>x.name.Contains("power weapon")).All(x=>x.sharedMesh==mesh),"Form input queues selection without swapping weapon during a committed strike");
            yield return new WaitForSeconds(.5f);
        }
        yield return EnterSecondary("grapple-line");W.Progression.Data.Powers.Find(x=>x.Id=="grapple-line").Tier=2;PlaceHero(new Vector3(0,8,0),true);yield return null;
        var upgradedAnchor=Wall(new Vector3(0,9.5f,46),new Vector3(8,6,.5f));ReadySecondary();yield return null;
        Check(W.Powers.Stats(W.Powers.SecondaryRuntime).Range>46&&W.Powers.UseSecondary(),"Tier-two grapple honors inherited range upgrades beyond 45m");yield return new WaitForSeconds(.15f);Check(Expansion.Grappling,"Upgraded distant anchor remains inside its actual finite range contract");Expansion.ReleaseGrapple();Destroy(upgradedAnchor);
        foreach(var hero in F.Heroes)
        {
            yield return Home();Profile.SetLoadout(hero,Power("combat-morphing"),Power("plasma-blades"),Power("speed"),hero.Primary,hero.Secondary);GameFlow.Instance.OpenCombatLab();yield return Scene(CombatLab.SceneName);yield return null;
            var camera=Camera.main;var follow=camera.GetComponent<ThirdPersonCamera>();if(follow.FirstPerson)follow.ToggleView();follow.enabled=false;camera.transform.position=W.Hero.transform.position+Vector3.forward*3+Vector3.right*.8f+Vector3.up*1.1f;camera.transform.LookAt(W.Hero.transform.position+Vector3.up);
            for(int i=0;i<3;i++){yield return null;Picture(hero.Id+"-fitted-"+Expansion.Form);Expansion.CycleForm();}
            W.Powers.Select(W.Powers.PowerForSlot(2));yield return null;Picture(hero.Id+"-fitted-plasma");
            var pose=W.Hero.GetComponent<HumanoidPresentation>();pose.VerificationState=new HeroPresentationState(new Vector3(2,0,12),false,true);yield return new WaitForSeconds(.3f);
            foreach(bool left in new[]{true,false})
            {
                var forearm=pose.Animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);var hand=pose.Animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);var weapon=W.Hero.transform.Find(left?"Left power weapon":"Right power weapon");
                Check(Vector3.Distance(weapon.position,Vector3.Lerp(forearm.position,hand.position,.7f))<.01f,hero.Id+": weapon follows finalized weighted flight/lean bones, not the pre-presentation pose");
            }
            pose.VerificationState=null;
        }
        yield return EnterExpansion("combat-morphing");yield return ClearActors();var interrupted=Npc("Rusher",new Vector3(0,.1f,2.2f));Ready();float before=interrupted.CurrentHealth;W.Powers.Use(W.Powers.Selected);W.DamagePlayer(W.Health,true);yield return new WaitForSeconds(.5f);
        Check(interrupted.CurrentHealth==before&&!Expansion.MeleePending&&!Expansion.DrivesMotion,"CONTROL real player death cancels pending special melee without ghost damage or motion");
        yield return Home();Check(UnityEngine.Object.FindObjectsByType<PowerExpansionRuntime>(FindObjectsSortMode.None).Length==0&&!UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(x=>x.name=="Expansion world effect pool"),"Leaving gameplay disposes the independent world VFX pool and weapon runtime");
        yield return EnterSecondary("mirage-decoy");ReadySecondary();W.Powers.UseSecondary();yield return null;yield return Home();Check(MirageDecoy.Current==null&&UnityEngine.Object.FindObjectsByType<MirageDecoy>(FindObjectsSortMode.None).Length==0,"Scene exit with a live decoy removes its copy and target lease");
        yield return EnterSecondary("grapple-line");var exitAnchor=Wall(new Vector3(0,3,14),new Vector3(8,8,.5f));ReadySecondary();yield return null;W.Powers.UseSecondary();yield return new WaitForSeconds(.1f);yield return Home();
        Check(!UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None).Any(x=>x.name=="Grapple tether"),"Scene exit while grappling disposes the bounded tether");
    }
}
#endif
