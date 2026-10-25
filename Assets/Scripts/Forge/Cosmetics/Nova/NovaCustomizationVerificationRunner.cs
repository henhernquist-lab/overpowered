#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class NovaCustomizationVerificationRunner : MonoBehaviour
{
    public bool Reload;public Action<int> Finished;
    const string Dir="Verification/NovaCustomization";
    readonly List<string> lines=new List<string>();string failure;
    ForgeCatalog Forge=>Resources.Load<ForgeCatalog>("ForgeCatalog");
    WorldSession W=>WorldSession.Instance;
    void Awake(){Application.logMessageReceived+=Observe;}
    void OnDestroy(){Application.logMessageReceived-=Observe;}
    void Observe(string text,string trace,LogType type){if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&trace.Contains("Assets/Scripts/"))failure=text;}
    void Log(string text){lines.Add(text);Debug.Log(text);File.WriteAllLines(Dir+(Reload?"/reload.txt":"/results.txt"),lines);}
    void Check(bool value,string text){if(!value)throw new Exception(text);Log("PASS "+text);}
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{if(failure!=null)throw new Exception(failure);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Finished(0);
    }
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+90;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name!="Home"&&W==null))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout "+name);yield return null;}
        yield return new WaitForSecondsRealtime(.5f);
    }
    void Submit(Button button){using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    Texture2D Capture(RenderTexture rt,string file)
    {
        var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();RenderTexture.active=old;File.WriteAllBytes(Dir+"/"+file,tex.EncodeToPNG());return tex;
    }
    void VisibleBody(RenderTexture rt,string file)
    {
        var tex=Capture(rt,file);var pixels=tex.GetPixels32();var background=pixels[0];int foreground=0;
        for(int y=30;y<230;y++)for(int x=70;x<330;x++)if(!pixels[y*tex.width+x].Equals(background))foreground++;
        Destroy(tex);Check(foreground>3000,"Completed preview contains the rendered lower body ("+foreground+" foreground pixels), not only headgear");
    }
    string Protected(ProgressSave data)
    {
        var copy=JsonUtility.FromJson<ProgressSave>(JsonUtility.ToJson(data));copy.HeroAppearances=null;copy.Loadout.Primary=copy.Loadout.Secondary=default;return JsonUtility.ToJson(copy);
    }
    void Colors(HeroAppearanceRenderer renderer,HeroDefinition hero,HeroAppearanceSave appearance)
    {
        var map=(Texture2D)SidekickSuit.MapOf(renderer.ColorMaterial);var palette=Resources.Load<CityPalette>("CityPalette");
        foreach(var cell in hero.Suit.Swatches)
        {
            Color32 expected=cell.Role==SuitRole.Keep?SidekickSuit.Pixel(hero.Suit.BaseColorMap,cell.Cell):palette.Colors[(int)(cell.Role==SuitRole.Primary?appearance.Primary:cell.Role==SuitRole.Secondary?appearance.Secondary:appearance.Accent)];
            if(!SidekickSuit.Pixel(map,cell.Cell).Equals(expected))throw new Exception("Incorrect/protected color "+cell.Name);
        }
        Check(true,"All three costume swatches and protected face/eye swatches match the palette and authored map");
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);var registry=Resources.Load<CosmeticCatalog>("CosmeticCatalog");
        Check(NovaCosmeticContent.Combined(registry).Length==registry.Items.Length,"Character registration is idempotent and retains other heroes");
        Check(Resources.Load<CosmeticCatalog>(NovaCosmeticContent.ResourcePath).Items.All(x=>x.HeroId=="nova"),"Local module contains only the exclusive character");var menu=FindAnyObjectByType<ModeScreens>();var nova=Forge.Hero("nova");var profile=menu.Profile;
        if(Reload)
        {
            Check(JsonUtility.ToJson(profile.Data)==File.ReadAllText(Dir+"/saves/expected.json"),"SECOND PROCESS exact progress, appearances, powers, signature, difficulty and parkour data survive");
            Check(profile.Appearance(nova).EmblemId=="nova_emblem_core"&&profile.Appearance(nova).Accent==CityColor.Amber,"SECOND PROCESS exact NOVA emblem and accent restored");
            Check(GameFlow.Instance.OpenCombatLab(),"SECOND PROCESS saved appearance enters actual Battle Lab");yield return Scene("CombatLab");
            var actual=W.Hero.GetComponentInChildren<HeroAppearanceRenderer>();
            Check(actual.SuitId=="nova_suit_energyweave"&&actual.EmblemId=="nova_emblem_core","SECOND PROCESS gameplay reconstructs the saved modular outfit");yield break;
        }
        Check(profile.Data.HeroAppearances==null||profile.Data.HeroAppearances.Count==0,"Fresh/legacy Version 1 save has no forced appearance migration");
        var legacy=profile.Appearance(nova);Check(legacy.SuitId=="nova-legacy"&&legacy.Primary==(profile.Data.Loadout.HeroId==nova.Id?profile.Data.Loadout.Primary:nova.Primary)&&legacy.Accent==nova.Suit.Trim,"Missing appearance resolves original suit and exact legacy colors");
        profile.AddXp(120);profile.Data.ParkourCourses.Add(new ParkourCourseRecord{Id="future-course",BestSeconds=38.5f,Completions=2,RewardClaimed=true,Completed=true});
        Submit(menu.ForgeButton);yield return null;var screen=menu.ForgeScreen;
        Check(screen.SelectHero(nova),"Existing Forge selects NOVA");string protectedBefore=Protected(profile.Data);
        Check(screen.SelectCosmetic(CosmeticSlot.Suit,"nova_suit_energyweave")&&screen.SelectCosmetic(CosmeticSlot.Mask,"nova_mask_unmasked"),"Fitted body and safe unmasked option selectable");
        Check(screen.SetColors(CityColor.Red,CityColor.Cyan,CityColor.Amber),"Three palette channels selectable and persisted");yield return null;yield return null;VisibleBody(screen.Preview,"nova-immediate-body.png");
        Check(Protected(profile.Data)==protectedBefore,"Cosmetic changes preserve XP/level/powers/signature/difficulty/session and parkour records exactly");
        Check(!screen.SelectCosmetic(CosmeticSlot.Mask,"nova_mask_spectrum")&&!screen.SelectCosmetic(CosmeticSlot.Mask,"nova_mask_phantom"),"Both unavailable target masks are refused without placeholder headgear");
        Check(screen.Root.Q<Button>("cosmetic-unavailable-nova_mask_spectrum")?.enabledSelf==false&&screen.Root.Q<Button>("cosmetic-unavailable-nova_mask_phantom")?.enabledSelf==false,"Missing target mask artwork is visibly unavailable");
        Check(screen.SelectCosmetic(CosmeticSlot.Emblem,"nova_emblem_star"),"First authored emblem selectable");
        screen.Close();screen.Open(); // Stop the reveal so captures compare the same camera pose.
        var model=screen.PreviewModel;var assembler=model.GetComponent<HeroAppearanceRenderer>();var material=assembler.ColorMaterial;
        var animator=model.GetComponent<Animator>();var camera=model.transform.parent.GetComponentInChildren<Camera>();var rt=screen.Preview;var scale=model.transform.localScale;
        Colors(assembler,nova,profile.Appearance(nova));yield return null;yield return null;
        foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()){var diagnostic=new Mesh();skin.BakeMesh(diagnostic);Log($"MEASURED preview {skin.name}: bounds={diagnostic.bounds} local={skin.transform.localPosition} scale={skin.transform.lossyScale} enabled={skin.enabled} bones={skin.bones.Length} layer={skin.gameObject.layer}");Destroy(diagnostic);}
        var menuTarget=new RenderTexture(1440,900,24);menuTarget.Create();menu.Panel.targetTexture=menuTarget;yield return null;yield return null;
        var rows=screen.Root.Q<VisualElement>("forge-form").Children().ToArray();
        Check(rows.Zip(rows.Skip(1),(x,y)=>x.worldBound.yMax<=y.worldBound.yMin+1).All(x=>x),"Appearance and power rows retain their height without overlapping text in the scrolling Forge form");
        Check(screen.Root.Q<ScrollView>("forge-form-scroll").contentContainer.worldBound.height>screen.Root.Q<ScrollView>("forge-form-scroll").contentViewport.worldBound.height,"Expanded Forge form scrolls instead of compressing controls");
        var scroll=screen.Root.Q<ScrollView>("forge-form-scroll");
        Check(scroll.contentViewport.worldBound.yMax<=screen.Root.Q<Button>("forge-done").worldBound.yMin&&scroll.style.overflow.value==Overflow.Hidden,"Scroll viewport clips controls above the footer and inside the Forge panel");
        scroll.scrollOffset=new Vector2(0,10000);yield return null;yield return null;
        Check(scroll.scrollOffset.y>100&&rows.Last().worldBound.yMax<=scroll.contentViewport.worldBound.yMax+1,"Signature controls remain reachable by scrolling to the end of the form");
        scroll.scrollOffset=Vector2.zero;yield return null;yield return null;
        var menuImage=Capture(menuTarget,"nova-forge-menu.png");Destroy(menuImage);menu.Panel.targetTexture=null;menuTarget.Release();Destroy(menuTarget);
        var hood=Capture(rt,"nova_emblem_star.png");
        Check(screen.SelectCosmetic(CosmeticSlot.Emblem,"nova_emblem_core"),"Second authored chest emblem selectable");yield return null;yield return null;VisibleBody(rt,"nova-immediate-emblem-swap.png");var covered=Capture(rt,"nova_emblem_core.png");
        Check(hood.GetPixels32().Zip(covered.GetPixels32(),(a,b)=>!a.Equals(b)).Count(x=>x)>5,"Emblem swapping changes actual rendered preview pixels");Destroy(hood);Destroy(covered);
        yield return null;int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,textures=Resources.FindObjectsOfTypeAll<Texture2D>().Length;
        for(int i=0;i<48;i++)
        {
            Check(screen.SetColors(Forge.SuitColors[i%Forge.SuitColors.Length],CityColor.Cyan,CityColor.Amber)&&screen.SelectCosmetic(CosmeticSlot.Emblem,i%2==0?"nova_emblem_star":"nova_emblem_core"),"Explicit preview update "+i);
        }
        yield return null;yield return null;
        Check(screen.PreviewModel==model&&screen.Preview==rt&&model.GetComponent<Animator>()==animator&&model.transform.parent.GetComponentInChildren<Camera>()==camera&&assembler.ColorMaterial==material,"96 appearance edits reuse the exact model, Animator, camera, render texture and material");
        Check(Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes&&Resources.FindObjectsOfTypeAll<Material>().Length==materials&&Resources.FindObjectsOfTypeAll<Texture2D>().Length==textures,"Repeated preview edits create zero persistent meshes/materials/textures");
        Check(model.transform.localScale==scale&&model.GetComponentsInChildren<Animator>().Length==1&&model.GetComponentsInChildren<SkinnedMeshRenderer>().Length==2,"Emblem swaps preserve canonical scale, one Animator and two bounded renderers");
        Log($"MEASURED preview resources after 96 edits: Mesh={meshes}, Material={materials}, Texture2D={textures}; RT={rt.width}x{rt.height}");
        var unknown=profile.Appearance(nova);unknown.SuitId="future-suit";unknown.MaskId="future-mask";unknown.EmblemId="future-emblem";
        Check(profile.SetAppearance(unknown),"Unknown cosmetic IDs can be preserved without destructive repair");screen.Open();
        Check(model.GetComponent<HeroAppearanceRenderer>().SuitId=="nova-legacy"&&profile.Appearance(nova).SuitId=="future-suit"&&profile.Appearance(nova).MaskId=="future-mask"&&profile.Appearance(nova).EmblemId=="future-emblem","Unknown outfit renders safe legacy fallback while retaining all saved IDs");
        var sandbox=new GameObject("Unknown save reload").AddComponent<PlayerProgression>();sandbox.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),profile.SavePath);
        Check(sandbox.Appearance(nova).SuitId=="future-suit"&&sandbox.Appearance(nova).MaskId=="future-mask","Disk reload retains unknown IDs");Destroy(sandbox.gameObject);
        screen.SelectCosmetic(CosmeticSlot.Suit,"nova_suit_energyweave");screen.SelectCosmetic(CosmeticSlot.Emblem,"nova_emblem_core");screen.SetColors(CityColor.Red,CityColor.Cyan,CityColor.Amber);
        screen.SelectCosmetic(CosmeticSlot.Mask,"nova_mask_unmasked");
        string appearanceBefore=JsonUtility.ToJson(profile.Appearance(nova));
        Check(screen.SelectPower(0,Resources.Load<PowerDefinition>("Powers/fire"))&&screen.SelectPower(1,Resources.Load<PowerDefinition>("Powers/strength"))&&appearanceBefore==JsonUtility.ToJson(profile.Appearance(nova)),"Changing combat powers preserves every appearance field");
        foreach(var other in Forge.Heroes.Where(h=>h!=nova))
        {
            Check(screen.SelectHero(other),"Select unchanged "+other.DisplayName);
            var skin=screen.PreviewModel.GetComponentInChildren<SkinnedMeshRenderer>();
            Check(skin.sharedMesh==other.Suit.Body&&screen.PreviewModel.GetComponentsInChildren<SkinnedMeshRenderer>().Length==1&&screen.PreviewModel.GetComponent<Animator>().avatar==other.CharacterPrefab.GetComponent<Animator>().avatar,other.DisplayName+" retains its optimized body, original Avatar and one renderer");
        }
        Check(screen.SelectHero(nova)&&JsonUtility.ToJson(profile.Appearance(nova))==appearanceBefore,"Returning to NOVA restores per-hero outfit and all three colors");
        Check(screen.SelectPower(0,Resources.Load<PowerDefinition>("Powers/fire"))&&screen.SelectPower(1,Resources.Load<PowerDefinition>("Powers/strength")),"After hero cycling, explicitly restore the paid Fire/Strength verification loadout through Forge");
        // Capture the actual shared-controller poses, in the real Forge preview, for visual review.
        Directory.CreateDirectory(Dir+"/poses");var poseAnimator=screen.PreviewModel.GetComponent<Animator>();var poseCamera=screen.PreviewModel.transform.parent.GetComponentInChildren<Camera>();float idleFov=poseCamera.fieldOfView;poseCamera.fieldOfView=44;
        foreach(string pose in new[]{"Locomotion","Jump","Double Jump","Backflip","Climb","Fall","Land","Punch","Martelo Kick","Hurricane Kick","Cast"})
        {
            poseAnimator.Play(pose,0,.35f);poseAnimator.Update(0);yield return null;yield return null;
            // Cinemachine evaluates the preview lens during the yielded frames; set the diagnostic lens after that.
            poseCamera.fieldOfView=44;poseCamera.targetTexture=screen.Preview;poseCamera.Render();poseCamera.targetTexture=null;
            var frame=Capture(screen.Preview,"poses/"+pose.Replace(" ","-")+".png");Destroy(frame);
        }
        poseCamera.fieldOfView=idleFov;Check(true,"Captured eleven actual shared-controller preview poses for human deformation review");
        profile.Save();File.WriteAllText(Dir+"/saves/expected.json",JsonUtility.ToJson(profile.Data));screen.Close();
        Check(GameFlow.Instance.OpenCombatLab(),"Enter actual gameplay through Home Battle Lab");yield return Scene("CombatLab");
        var presentation=W.Hero.GetComponent<HumanoidPresentation>();var live=W.Hero.GetComponentInChildren<HeroAppearanceRenderer>();
        Check(live.SuitId=="nova_suit_energyweave"&&live.EmblemId=="nova_emblem_core","Actual playable NOVA uses exact saved cosmetics");Colors(live,nova,W.Progression.Appearance(nova));
        var runtimeSkins=live.GetComponentsInChildren<SkinnedMeshRenderer>();
        Check(runtimeSkins.Sum(r=>r.sharedMesh.vertexCount)<=nova.Suit.Body.vertexCount&&runtimeSkins.Sum(r=>r.bones.Length)<=nova.Suit.BodyBones.Length,"Runtime modular vertex and bone budgets stay below the optimized original body");
        Check(runtimeSkins.All(r=>r.sharedMaterial==live.ColorMaterial)&&presentation.VisualRoot.GetComponentsInChildren<Animator>().Length==1,"Runtime body and mask share one cached material and the existing single Animator");
        Check(!W.Progression.SetAppearance(W.Progression.Appearance(nova)),"Live session appearance mutation refused; startup camera renderer cache remains complete");
        Check(W.Powers.HeroStats==nova.Stats&&Mathf.Approximately(W.MaxHealth,W.Tuning.Movement.Health*nova.Stats.MaxHealth*W.Difficulty.PlayerHealth),"Actual gameplay retains the selected hero stat definition and unchanged health formula");
        var cc=W.Hero.GetComponent<CharacterController>();var capsule=(cc.height,cc.radius,cc.center,W.Hero.transform.localScale);
        var visual=presentation.VisualRoot.GetComponentsInChildren<Renderer>(true);var shadow=visual.Select(r=>r.shadowCastingMode).ToArray();var view=Camera.main.GetComponent<ThirdPersonCamera>();
        Check(visual.Length==2&&view.ToggleView()&&visual.All(r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),"First-person hides body AND modular chest emblem through the real startup renderer cache");
        Check(view.ToggleView()&&visual.Select((r,i)=>r.shadowCastingMode==shadow[i]).All(x=>x),"Third-person restores both original shadow modes");
        Check((cc.height,cc.radius,cc.center,W.Hero.transform.localScale)==capsule&&presentation.Animator.applyRootMotion==false,"View/cosmetics never change physics capsule, root scale or root motion");
        var bMesh=new Mesh();
        foreach(string state in new[]{"Locomotion","Jump","Double Jump","Backflip","Climb","Fall","Land","Punch","Martelo Kick","Hurricane Kick","Cast"})
        {
            Check(presentation.Animator.HasState(0,Animator.StringToHash(state)),state+": existing controller state present");
            presentation.Animator.Play(state,0,.35f);presentation.Animator.Update(0);
            foreach(var r in live.GetComponentsInChildren<SkinnedMeshRenderer>()){r.BakeMesh(bMesh);Check(bMesh.vertexCount==r.sharedMesh.vertexCount&&bMesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),state+": finite baked body/emblem under the unchanged shared controller");}
        }
        Destroy(bMesh);
        presentation.Animator.Play("Locomotion");W.Hero.ScriptedMove=Vector3.right;W.Hero.ScriptedRun=true;var position=W.Hero.transform.position;yield return new WaitForSeconds(.35f);W.Hero.ScriptedMove=Vector3.zero;
        Check(Vector3.Distance(position,W.Hero.transform.position)>.25f,"Real running moves the customized playable character");
        Check(W.Hero.TryJump(),"Real grounded jump accepts customized character");yield return new WaitForSeconds(.15f);
        Check(W.Hero.TryJump(),"Real double jump accepts customized character");yield return new WaitForSeconds(2f);
        if(W.Powers.EquippedSecondary.Effect.IsFlight)
        {
            Check(W.Hero.TryJump(),"Secondary-flight setup uses the real grounded jump");yield return new WaitForSeconds(.15f);
            float fuel=W.Hero.FlightFuel;W.Hero.ScriptedFlight=true;W.Hero.ScriptedAscend=true;yield return new WaitForSeconds(.25f);
            Check(W.Hero.FlightFuel<fuel&&presentation.FlightWeight>.1f,"Real equipped Flight spends fuel and drives the existing presentation with the modular suit");
            W.Hero.ScriptedFlight=W.Hero.ScriptedAscend=false;yield return new WaitForSeconds(2f);
        }
        else {Check(W.Powers.UseSecondary(),"Real equipped secondary activates with the modular suit");yield return new WaitForSeconds(1f);}
        W.Hero.DebugSetResources(2,4,0);Check(W.Hero.TryPunch(),"Real punch windup accepts customized character");yield return new WaitForSeconds(Mathf.Max(W.Hero.PunchWindupSeconds,W.Powers.Stats(W.Powers.Strength).Cooldown)+.1f);
        Check(W.Powers.IsEquipped(W.Powers.Strength.Definition),"Actual session has Strength equipped before its paid Hurricane control");
        bool kicked=W.Hero.TryHurricaneKick();Check(kicked,"Real Hurricane Kick accepts customized character: "+W.Hero.LastKickResult);yield return new WaitForSeconds(1.4f);
        Check(W.Hero.TryBackflip(),"Real backflip accepts customized character");yield return new WaitForSeconds(1f);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Cosmetic climb control";wall.transform.position=W.Hero.transform.position+Vector3.forward*.9f+Vector3.up*3;wall.transform.localScale=new Vector3(5,6,.2f);wall.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Metal);Physics.SyncTransforms();
        Check(W.Hero.TryClimb(Vector3.forward),"Real wall latch accepts the customized character");W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=Vector2.up;float climbY=W.Hero.transform.position.y;yield return new WaitForSeconds(.3f);
        Check(W.Hero.Climbing&&W.Hero.transform.position.y>climbY&&presentation.State=="Climb","Real climbing advances customized body and shared Climb presentation");W.Hero.ScriptedClimbCancel=true;yield return null;Destroy(wall);yield return new WaitForSeconds(1f);
        var aimCamera=Camera.main;aimCamera.transform.position=W.Hero.transform.position+Vector3.up*3;aimCamera.transform.rotation=Quaternion.LookRotation(Vector3.up,Vector3.forward);
        Check(W.Powers.Use(W.Powers.PowerForSlot(1)),"Real equipped primary Fire activates with the modular suit: "+W.Powers.Message);yield return new WaitForSeconds(.2f);
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")),"Customized NOVA enters the normal Hero city session");yield return Scene(GameFlow.CityScene);
        live=W.Hero.GetComponentInChildren<HeroAppearanceRenderer>();Check(!W.IsPractice&&live.SuitId=="nova_suit_energyweave"&&live.EmblemId=="nova_emblem_core","Normal city spawn reconstructs the same saved outfit outside practice");
        var gameplayCamera=Camera.main;var portrait=new RenderTexture(400,500,24);portrait.Create();gameplayCamera.targetTexture=portrait;
        gameplayCamera.transform.position=W.Hero.transform.position+W.Hero.transform.forward*3.6f+W.Hero.transform.right*.8f+Vector3.up*1.35f;gameplayCamera.transform.LookAt(W.Hero.transform.position+Vector3.up*.95f);gameplayCamera.Render();
        var gameplayImage=Capture(portrait,"nova-gameplay-city.png");Destroy(gameplayImage);gameplayCamera.targetTexture=null;portrait.Release();Destroy(portrait);
        var viewCamera=Camera.main;var follow=viewCamera.GetComponent<ThirdPersonCamera>();follow.enabled=false;viewCamera.transform.position=W.Hero.transform.position+Vector3.up*3;viewCamera.transform.LookAt(W.Hero.transform.position+Vector3.forward*5);
        Check(W.Powers.SynergyRunner.TryActivate(),"Existing signature can activate on the customized playable hero");yield return null;
        Check(W.Hero.GetComponent<HumanoidPresentation>().State=="Cast","Real signature dispatch preserves the shared Cast animation");W.Powers.SynergyRunner.Cancel();
        // City session instrumentation may legitimately add a power-session counter; compare the separate-process save against the real final state.
        W.Progression.Save();File.WriteAllText(Dir+"/saves/expected.json",JsonUtility.ToJson(W.Progression.Data));
        Log("MEASURED NOVA runtime: "+string.Join("; ",live.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r=>r.sharedMesh.vertexCount+" vertices / "+r.bones.Length+" bones"))+"; one Animator and shared costume material");
    }
}
#endif
