#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Profiling;
public sealed partial class HeroForgeArtVerificationRunner
{
    void Live(HeroDefinition hero,HeroAppearanceSave expected,string mode)
    {
        Check(Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t=>t.name=="Hero Forge preview")==0,hero.Id+"/"+mode+": leaving Home disposes the Forge preview RenderTexture");
        var presentation=W.Hero.GetComponent<HumanoidPresentation>();var visual=presentation.VisualRoot;var live=visual.GetComponentInChildren<HeroAppearanceRenderer>();var skins=live.GetComponentsInChildren<SkinnedMeshRenderer>();
        Check(live.SuitId==expected.SuitId&&live.MaskId==expected.MaskId&&live.EmblemId==expected.EmblemId,hero.Id+"/"+mode+": ordinary playable spawn matches selected suit, mask and emblem");Colors(live,hero,expected);
        Check(skins.Length==3&&skins.All(s=>s.sharedMesh!=null&&s.bones.Length==s.sharedMesh.bindposes.Length)&&skins.All(s=>s.sharedMaterial==live.ColorMaterial),hero.Id+"/"+mode+": exactly three bounded renderers share one cached material with valid bindings");
        Check(skins.Sum(s=>s.sharedMesh.vertexCount)<hero.Suit.Body.vertexCount&&skins.Sum(s=>s.bones.Length)<=hero.Suit.BodyBones.Length,hero.Id+"/"+mode+": modern geometry and renderer bone entries stay below legacy budgets");
        Check(visual.GetComponentsInChildren<Animator>().Length==1&&presentation.Animator.avatar==hero.CharacterPrefab.GetComponent<Animator>().avatar&&!presentation.Animator.applyRootMotion,hero.Id+"/"+mode+": existing Avatar, one Animator and no root motion");
        var cc=W.Hero.GetComponent<CharacterController>();var shape=(cc.height,cc.radius,cc.center,W.Hero.transform.localScale);
        if(!capsules.ContainsKey(hero.Id))capsules[hero.Id]=shape;Check(capsules[hero.Id]==shape,hero.Id+"/"+mode+": masks and modes preserve physics capsule and root scale");
        Check(W.Powers.HeroStats==hero.Stats&&Mathf.Approximately(W.MaxHealth,W.Tuning.Movement.Health*hero.Stats.MaxHealth*W.Difficulty.PlayerHealth),hero.Id+"/"+mode+": cosmetics retain hero stats and original health formula");
        var renderers=visual.GetComponentsInChildren<Renderer>(true);var old=renderers.Select(r=>r.shadowCastingMode).ToArray();var view=Camera.main.GetComponent<ThirdPersonCamera>();
        Check(view.ToggleView()&&renderers.All(r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),hero.Id+"/"+mode+": first-person hides body, helmet and emblem through the real startup cache");
        Check(view.ToggleView()&&renderers.Select((r,i)=>r.shadowCastingMode==old[i]).All(x=>x),hero.Id+"/"+mode+": third-person restores every original shadow mode");
        Check(!W.Progression.SetAppearance(expected),hero.Id+"/"+mode+": live appearance mutation gate remains intact");
    }
    IEnumerator Actions(HeroDefinition hero)
    {
        var presentation=W.Hero.GetComponent<HumanoidPresentation>();var start=W.Hero.transform.position;W.Hero.ScriptedMove=Vector3.right;W.Hero.ScriptedRun=true;yield return new WaitForSeconds(.35f);W.Hero.ScriptedMove=Vector3.zero;
        Check(Vector3.Distance(start,W.Hero.transform.position)>.25f,hero.Id+": actual running moves the equipped character");
        Check(W.Hero.TryJump(),hero.Id+": actual grounded jump");yield return new WaitForSeconds(.15f);Check(W.Hero.TryJump(),hero.Id+": actual double jump");yield return new WaitForSeconds(2f);
        W.Hero.DebugSetResources(2,3,0);Check(W.Hero.TryPunch(),hero.Id+": actual paid punch");int charges=W.Hero.Charges;
        Check(!W.Hero.TryHurricaneKick()&&W.Hero.Charges==charges,hero.Id+": immediate kick is refused by the existing shared cooldown without extra payment");yield return new WaitForSeconds(Mathf.Max(W.Hero.PunchWindupSeconds,W.Powers.Stats(W.Powers.Strength).Cooldown)+.1f);
        Check(W.Hero.TryComboAttack(),hero.Id+": actual first combo tap");yield return new WaitForSeconds(Mathf.Max(W.Hero.PunchWindupSeconds,W.Powers.Stats(W.Powers.Strength).Cooldown)+.06f);
        Check(W.Hero.TryComboAttack()&&W.Hero.LastComboStage==2,hero.Id+": second combo tap dispatches the existing Martelo finisher");yield return new WaitForSeconds(.8f);
        W.Hero.DebugSetResources(2,3,0);Check(W.Hero.TryHurricaneKick(),hero.Id+": actual Hurricane Kick");yield return new WaitForSeconds(1f);Check(W.Hero.TryBackflip(),hero.Id+": actual grounded backflip");yield return new WaitForSeconds(1f);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Appearance wall-climb control";wall.transform.position=W.Hero.transform.position+Vector3.forward*.9f+Vector3.up*3;wall.transform.localScale=new Vector3(5,6,.2f);wall.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Metal);Physics.SyncTransforms();
        Check(W.Hero.TryClimb(Vector3.forward),hero.Id+": actual wall latch");W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=Vector2.up;float y=W.Hero.transform.position.y;yield return new WaitForSeconds(.3f);
        Check(W.Hero.Climbing&&W.Hero.transform.position.y>y&&presentation.State=="Climb",hero.Id+": climb advances body, helmet and shared presentation");W.Hero.ScriptedClimbCancel=true;yield return null;Destroy(wall);yield return new WaitForSeconds(1f);
        Check(W.Powers.EquippedSecondary.Id=="phase-shift"&&W.Powers.UseSecondary(),hero.Id+": actual equipped Phase Shift secondary activates");Check(!W.Hero.TryPunch(),hero.Id+": phased offensive-action refusal remains intact");
        float end=Time.time+12;while(W.Powers.OffensiveActionsBlocked&&Time.time<end)yield return null;Check(!W.Powers.OffensiveActionsBlocked,hero.Id+": Phase Shift expires through its normal lifecycle");
        Camera.main.transform.position=W.Hero.transform.position+Vector3.up*3;Camera.main.transform.rotation=Quaternion.LookRotation(Vector3.up,Vector3.forward);
        Check(W.Powers.Use(W.Powers.PowerForSlot(1)),hero.Id+": actual primary Fire accepted");yield return new WaitForSeconds(.2f);
        Check(W.Powers.Synergy.Id=="solar-flare"&&W.Powers.SynergyRunner.TryActivate(),hero.Id+": actual equipped Solar Flare accepted");
        Check(presentation.State=="Cast",hero.Id+": signature immediately dispatches the established Cast event");W.Powers.SynergyRunner.Cancel();yield return new WaitForSeconds(2f);
    }
    IEnumerator Measure(HeroDefinition hero,HeroAppearanceSave modern)
    {
        var camera=Camera.main;var follow=camera.GetComponent<ThirdPersonCamera>();follow.enabled=false;var live=W.Hero.GetComponentInChildren<HeroAppearanceRenderer>();var rt=new RenderTexture(1280,720,24);rt.Create();camera.targetTexture=rt;bool enabled=camera.enabled;camera.enabled=false;
        camera.transform.position=W.Hero.transform.position+Vector3.forward*4+Vector3.up*1.3f;camera.transform.LookAt(W.Hero.transform.position+Vector3.up*.95f);
        foreach(bool legacy in new[]{true,false})
        {
            live.Apply(legacy?new HeroAppearanceSave{HeroId=hero.Id,SuitId=hero.Id+"-legacy",MaskId=hero.Id+"-integrated",EmblemId=hero.Id+"-no-emblem",Primary=modern.Primary,Secondary=modern.Secondary,Accent=modern.Accent}:modern);
            for(int i=0;i<60;i++){camera.Render();yield return null;}
            var cpu=new List<double>();var frames=new List<float>();var allocated=new List<long>();var watch=new System.Diagnostics.Stopwatch();
            using(var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1))
            for(int i=0;i<120;i++){watch.Restart();camera.Render();watch.Stop();cpu.Add(watch.Elapsed.TotalMilliseconds);frames.Add(Time.unscaledDeltaTime*1000);if(gc.Valid)allocated.Add(gc.LastValue);yield return null;}
            cpu.Sort();frames.Sort();allocated.Sort();
            Log($"MEASURED {hero.Id} {(legacy?"legacy":"modern")}: same idle Battle Lab/camera, 60 warm +120 measured frames, one explicit 1280x720 render/frame; render CPU median/p95 {cpu[60]:F3}/{cpu[114]:F3}ms, whole Editor frame {frames[60]:F3}/{frames[114]:F3}ms, whole-frame GC median {(allocated.Count>0?allocated[allocated.Count/2]:-1)}B. Not a standalone FPS, GPU or causal skinning-isolation claim.");
        }
        camera.targetTexture=null;camera.enabled=enabled;follow.enabled=true;rt.Release();Destroy(rt);
    }
    IEnumerator PlayChecks()
    {
        foreach(var hero in Forge.Heroes)
        {
            foreach(string mode in new[]{"hero","villain","free-play","endless-fight","endless-fight-villain","lab"})
            {
                var menu=FindAnyObjectByType<ModeScreens>();var profile=menu.Profile;var screen=OpenForge();screen.SelectHero(hero);var selected=appearances[hero.Id].Copy();
                selected.MaskId=Masks(hero)[mode=="hero"||mode=="free-play"||mode=="endless-fight-villain"?0:1].Id;screen.SelectCosmetic(CosmeticSlot.Mask,selected.MaskId);
                Check(screen.SelectPower(0,Resources.Load<PowerDefinition>("Powers/fire"))&&screen.SelectPower(1,Resources.Load<PowerDefinition>("Powers/strength"))&&screen.SelectPower(2,Resources.Load<PowerDefinition>("Powers/phase-shift")),hero.Id+"/"+mode+": equip combat controls through existing Forge gates");
                Check(profile.SetSignature(Resources.Load<SignatureCatalog>("SignatureCatalog").ForPrimary(Resources.Load<PowerDefinition>("Powers/fire")).Id),hero.Id+": equip actual Fire signature through progression");screen.Close();
                Check(mode=="lab"?GameFlow.Instance.OpenCombatLab():GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/"+mode)),hero.Id+"/"+mode+": enter ordinary existing game route");yield return Scene(mode=="lab"?"CombatLab":GameFlow.CityScene);Live(hero,selected,mode);
                if(mode=="hero")
                {
                    var view=Camera.main;var rt=new RenderTexture(400,500,24);rt.Create();view.targetTexture=rt;view.transform.position=W.Hero.transform.position+Vector3.forward*3.7f+Vector3.right*.6f+Vector3.up*1.3f;view.transform.LookAt(W.Hero.transform.position+Vector3.up*.95f);view.Render();
                    var image=Capture(rt,"gameplay/"+hero.Id+"-hero-city.png");Destroy(image);view.targetTexture=null;rt.Release();Destroy(rt);
                }
                if(mode=="lab"){yield return Actions(hero);yield return Measure(hero,selected);}
                GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
            }
        }
    }
}
#endif
