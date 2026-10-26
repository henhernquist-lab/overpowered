#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
public sealed partial class PowerExpansionVerificationRunner
{
    IEnumerator GameplayChecks()
    {
        string[] ids={"aerokinesis","sonic-manipulation","combat-morphing","plasma-blades"};
        foreach(string difficulty in new[]{"normal","hard","extreme"})foreach(string id in ids)
        {
            yield return Home();Check(Profile.SetDifficulty(difficulty),"Use actual difficulty selection: "+difficulty);yield return EnterExpansion(id);
            foreach(string kind in new[]{"Rusher","Gunner","Brute","Skirmisher","Bulwark","Pulsebreaker"})
            {
                yield return ClearActors();Ready();var npc=Npc(kind,new Vector3(0,.1f,2.2f));yield return null;
                float before=npc.CurrentHealth,energy=W.Powers.Energy;int charges=W.Powers.Selected.Charges;int events=0;NpcDamageResult result=default;
                Action<NpcDamageResult> received=r=>{if(r.Target==npc){events++;result=r;}};CityNpc.DamageApplied+=received;
                Check(W.Difficulty.Id==difficulty&&W.Powers.Use(W.Powers.Selected),id+" activates vs "+kind+" / "+difficulty);
                Check(W.Powers.Selected.Charges==charges-1&&Mathf.Abs(W.Powers.Energy-(energy-W.Powers.EnergyCost(W.Powers.Selected)))<.02f,"One authoritative charge and difficulty-scaled energy payment");
                float mean=W.Powers.Stats(W.Powers.Selected).Damage;
                if(id=="plasma-blades")mean*=Expansion.PlasmaStage==3?1.5f:Expansion.PlasmaStage==2? .9f:1;
                yield return new WaitForSeconds(.52f);
                Check(events==1&&result.Attacker==W.Powers&&result.SourceId==id&&Mathf.Abs(result.ResolvedDamage-mean*W.Difficulty.PlayerDamage)<.02f&&Mathf.Abs((before-npc.CurrentHealth)-result.HealthLost)<.02f,id+" / "+kind+" / "+difficulty+": exact resolved damage, one centralized multiplier and deferred ownership");
                CityNpc.DamageApplied-=received;
            }
            yield return ClearActors();Ready();var friend=Npc("Rusher",new Vector3(0,.1f,2.2f),NpcRole.Cop);float friendlyHealth=friend.CurrentHealth;W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.5f);
            Check(friend.CurrentHealth==friendlyHealth,"CONTROL "+id+" excludes unprovoked friendly patrol");
            yield return ClearActors();Ready();var victim=Npc("Rusher",new Vector3(0,.1f,2.2f),NpcRole.Criminal,1);bool kill=false;Action<PowerHit> killed=h=>{if(h.Npc==victim&&h.Killed&&h.Credit==id)kill=true;};W.Powers.Hit+=killed;
            W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.5f);Check(kill&&victim.Dead,id+" / "+difficulty+": actual lethal hit credits player and primary ID");W.Powers.Hit-=killed;
        }
        yield return Home();Profile.SetDifficulty("normal");yield return EnterExpansion("plasma-blades");yield return ClearActors();
        var guard=Npc("Bulwark",new Vector3(0,.1f,2.45f));guard.enabled=true;guard.Agent.updatePosition=false;guard.transform.forward=Vector3.back;yield return null;
        Ready();float hp=guard.CurrentHealth;Check(guard.GuardRaised,"Directional shield CONTROL actually raised before plasma strike");W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.2f);float frontal=hp-guard.CurrentHealth;
        Check(guard.GuardHits>0&&frontal>0&&frontal<W.Powers.Stats(W.Powers.Selected).Damage,"Plasma uses real directional shield mitigation, no invulnerability bypass");
        guard.transform.forward=Vector3.forward;Ready();hp=guard.CurrentHealth;W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.2f);Check(hp-guard.CurrentHealth>frontal,"Rear plasma strike remains stronger than guarded frontal strike");
        yield return EnterExpansion("combat-morphing");yield return ClearActors();var brute=Npc("Brute",new Vector3(0,.1f,2.1f));Expansion.CycleForm();Ready();W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.5f);Check(brute.PowerInterrupts==1&&brute.GetComponent<NpcPowerDisplacement>().Travelled>0,"Hammer Fists stagger and move actual resistant Brute");
        yield return UniversalControls();yield return WeaponHeroes();yield return GrappleCombat();yield return NaturalApex();yield return VisualsAndBudget();
        Log("LIMIT: native functional/render/object-budget evidence, not standalone GPU/CPU profiling or human balance/feel/art approval.");
    }
    IEnumerator UniversalControls()
    {
        yield return EnterExpansion("combat-morphing");yield return ClearActors();int strength=W.Powers.Strength.Charges;
        Check(!W.Powers.IsEquipped(W.Powers.Strength.Definition)&&W.Hero.TryPunch(),"Universal E punch works with Strength retired and unequipped");yield return new WaitForSeconds(.8f);
        Check(W.Hero.TryComboAttack(),"Universal E combo first tap accepted");yield return new WaitForSeconds(.75f);
        Check(W.Hero.TryComboAttack()&&W.Hero.LastComboStage==2,"Universal E second tap dispatches punch-to-kick finisher");yield return new WaitForSeconds(.85f);
        Check(W.Hero.TryHurricaneKick(),"Universal RMB Hurricane Kick remains available");yield return new WaitForSeconds(.9f);
        Check(W.Powers.Strength.Charges==strength,"Universal melee never consumes legacy Strength charges");
        Check(W.Hero.TryJump(),"Universal ground jump");yield return new WaitForSeconds(.15f);Check(W.Hero.TryJump()&&W.Hero.LastJumpWasAir,"Universal double jump");
        Check(!W.Hero.TryJump(),"CONTROL third jump refused");yield return new WaitForSeconds(2);
        Check(W.Hero.TryBackflip(),"Universal backflip remains available");yield return new WaitForSeconds(1.1f);
        var wall=Wall(W.Hero.transform.position+Vector3.forward*.9f+Vector3.up*3,new Vector3(5,6,.2f));yield return null;
        Check(W.Hero.TryClimb(Vector3.forward),"Universal wall climb latches after new melee use");W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=Vector2.up;float y=W.Hero.transform.position.y;yield return new WaitForSeconds(.25f);
        Check(W.Hero.Climbing&&W.Hero.transform.position.y>y,"Shared climb movement and presentation continue");W.Hero.ScriptedClimbCancel=true;yield return null;Destroy(wall);
    }
    IEnumerator WeaponHeroes()
    {
        foreach(var hero in F.Heroes)
        {
            yield return Home();Check(Profile.SetLoadout(hero,Power("combat-morphing"),Power("plasma-blades"),Power("phase-shift"),hero.Primary,hero.Secondary),hero.Id+": actual new melee pair selected");
            GameFlow.Instance.OpenCombatLab();yield return Scene(CombatLab.SceneName);yield return null;yield return null;
            var body=W.Hero.GetComponent<CharacterController>();float h=body.height,r=body.radius;
            for(int form=0;form<3;form++){yield return null;var renderers=W.Hero.GetComponentsInChildren<MeshRenderer>().Where(x=>x.name.Contains("power weapon")).ToArray();Check(renderers.Length==2&&renderers.All(x=>x.enabled&&x.bounds.size.magnitude>.15f&&x.bounds.size.magnitude<3),hero.Id+": fitted pair of world-scale "+Expansion.Form+" meshes");Expansion.CycleForm();}
            W.Powers.Select(W.Powers.PowerForSlot(2));yield return null;Check(W.Hero.GetComponentsInChildren<Animator>().Length==1&&body.height==h&&body.radius==r,hero.Id+": plasma swap keeps existing Avatar/Animator/capsule");
            var view=Camera.main.GetComponent<ThirdPersonCamera>();if(!view.FirstPerson)view.ToggleView();yield return null;
            Check(W.Hero.GetComponentsInChildren<MeshRenderer>().Where(x=>x.name.Contains("power weapon")).All(x=>x.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),hero.Id+": first-person weapon hiding follows renderer-only contract");
            ReadySecondary();Check(W.Powers.UseSecondary(),hero.Id+": existing Phase Shift activates with new melee pair");Ready();Check(!W.Powers.Use(W.Powers.Selected),"CONTROL phase blocks new melee");W.Powers.Phase.End();
        }
    }
    IEnumerator GrappleCombat()
    {
        yield return EnterSecondary("grapple-line");ReadySecondary();var wall=Wall(new Vector3(0,3,14),new Vector3(8,8,.5f));var enemy=Npc("Gunner",new Vector3(4,.1f,5));enemy.SetCombatStats(10000,2);enemy.enabled=true;yield return null;
        Check(W.Powers.UseSecondary(),"Grapple starts during live hostile combat");yield return new WaitForSeconds(1);
        Check(enemy.Windups>0&&enemy.Releases>0,"Actual Gunner commits and releases while hero traverses");Expansion.ReleaseGrapple();Check(!W.Hero.AbilityDriving,"Enemy combat does not strand grapple motion");Destroy(wall);
        // Consume the air jump, then pass motion ownership to the grapple and back: no air-jump reset.
        PlaceHero(new Vector3(0,4,0),true);yield return null;Check(W.Hero.TryJump(),"Air jump consumed before grapple control");
        wall=Wall(new Vector3(0,6,14),new Vector3(8,8,.5f));ReadySecondary();yield return null;Check(W.Powers.UseSecondary(),"Airborne grapple acquires anchor");yield return new WaitForSeconds(.1f);Expansion.ReleaseGrapple();
        Check(!W.Hero.TryJump(),"CONTROL grapple release does not grant an extra air jump");Destroy(wall);
    }
    IEnumerator NaturalApex()
    {
        yield return EnterSignature("combat-morphing","apex-mutation");Ready();Check(W.Powers.SynergyRunner.TryActivate(),"Shipping Apex starts through complete normal catalog");
        yield return new WaitForSeconds(7.2f);Check(!Expansion.ApexActive&&!W.Powers.SynergyRunner.Busy,"Apex naturally expires at finite authored duration");
    }
    void Picture(string name)
    {
        string dir="Verification/PowerExpansion2/images/";Directory.CreateDirectory(dir);var camera=Camera.main;var target=new RenderTexture(1280,720,24);target.Create();var old=camera.targetTexture;var active=RenderTexture.active;
        camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(dir+name+".png",image.EncodeToPNG());camera.targetTexture=old;RenderTexture.active=active;target.Release();Destroy(target);Destroy(image);
    }
    IEnumerator VisualsAndBudget()
    {
        foreach(string id in new[]{"aerokinesis","sonic-manipulation","combat-morphing","plasma-blades"})
        {
            yield return EnterExpansion(id);yield return ClearActors();Npc("Rusher",new Vector3(0,.1f,3));var view=Camera.main.GetComponent<ThirdPersonCamera>();
            foreach(bool first in new[]{true,false}){if(view.FirstPerson!=first)view.ToggleView();yield return null;Ready();W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.06f);Picture(id+(first?"-first":"-third"));yield return new WaitForSeconds(.6f);}
        }
        yield return EnterSecondary("mirage-decoy");ReadySecondary();W.Powers.UseSecondary();yield return null;Picture("mirage-first");Camera.main.GetComponent<ThirdPersonCamera>().ToggleView();yield return null;Picture("mirage-third");Expansion.Decoy.Dissolve();yield return null;
        yield return EnterSecondary("grapple-line");var wall=Wall(new Vector3(0,3,14),new Vector3(8,8,.5f));ReadySecondary();W.Powers.UseSecondary();yield return new WaitForSeconds(.1f);Picture("grapple-first");Expansion.ReleaseGrapple();Destroy(wall);
        yield return EnterExpansion("aerokinesis");yield return ClearActors();Ready();W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.6f);
        int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,animators=W.Hero.GetComponentsInChildren<Animator>().Length;
        for(int i=0;i<100;i++){Ready();W.Powers.Use(W.Powers.Selected);}
        Check(Expansion.Vfx.LiveCount<=32&&meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&materials==Resources.FindObjectsOfTypeAll<Material>().Length&&W.Hero.GetComponentsInChildren<Animator>().Length==animators,"One hundred wind casts retain fixed 32 visual slots and shared native mesh/material/Animator counts");
        yield return new WaitForSeconds(.8f);Check(Expansion.Vfx.LiveCount==0,"Stress visuals fully expire");
        var idle=new List<float>();var activeFrames=new List<float>();
        for(int i=0;i<30;i++)yield return null;
        for(int i=0;i<120;i++){idle.Add(Time.unscaledDeltaTime*1000);yield return null;}
        for(int i=0;i<120;i++){if(i%6==0){Ready();W.Powers.Use(W.Powers.Selected);}activeFrames.Add(Time.unscaledDeltaTime*1000);yield return null;}
        idle.Sort();activeFrames.Sort();Log($"MEASURED same empty native Combat Lab, 30 warm +120 idle +120 active Editor frames: median/p95 {idle[60]:F2}/{idle[114]:F2}ms idle, {activeFrames[60]:F2}/{activeFrames[114]:F2}ms repeated wind. Frame timing includes Editor and batch environment; no standalone/GPU/causal skinning claim.");
        yield return new WaitForSeconds(.8f);Check(Expansion.Vfx.LiveCount==0,"Measured phase leaves no live expansion VFX");
    }
}
#endif
