#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
public sealed partial class HeroForgeArtVerificationRunner
{
    IEnumerator Poses(HeroForgeScreen screen,HeroDefinition hero,string mask)
    {
        var animator=screen.PreviewModel.GetComponent<Animator>();var camera=screen.PreviewModel.transform.parent.GetComponentInChildren<Camera>();var bake=new Mesh();var originalPosition=camera.transform.position;var originalRotation=camera.transform.rotation;float originalFov=camera.fieldOfView;float originalAspect=camera.aspect;
        foreach(string state in new[]{"Locomotion","Jump","Double Jump","Backflip","Climb","Fall","Land","Punch","Martelo Kick","Hurricane Kick","Cast"})
        {
            Check(animator.HasState(0,Animator.StringToHash(state)),hero.Id+"/"+mask+": unchanged "+state+" state exists");animator.Play(state,0,.35f);animator.Update(0);yield return null;yield return null;
            var world=new Bounds();bool first=true;bool finite=true;
            foreach(var skin in screen.PreviewModel.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.BakeMesh(bake);finite&=bake.vertexCount==skin.sharedMesh.vertexCount;
                foreach(var p in bake.vertices){finite&=float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);var q=skin.transform.TransformPoint(p);if(first){world=new Bounds(q,Vector3.zero);first=false;}else world.Encapsulate(q);}
            }
            Check(finite,hero.Id+"/"+mask+": finite posed body, helmet and emblem for "+state);
            camera.fieldOfView=32;camera.aspect=.8f;float distance=Mathf.Max(world.size.y,world.size.x/.8f)*.5f/Mathf.Tan(16*Mathf.Deg2Rad)*1.65f;
            camera.transform.position=world.center+new Vector3(.16f,.04f,1).normalized*distance;camera.transform.LookAt(world.center);camera.targetTexture=screen.Preview;camera.Render();camera.targetTexture=null;
            var image=Capture(screen.Preview,"poses/"+mask+"/"+state.Replace(" ","-")+".png");Destroy(image);
        }
        animator.Play("Locomotion");animator.Update(0);camera.transform.SetPositionAndRotation(originalPosition,originalRotation);camera.fieldOfView=originalFov;camera.aspect=originalAspect;Destroy(bake);
    }
    IEnumerator PreviewChecks()
    {
        var menu=FindAnyObjectByType<ModeScreens>();var profile=menu.Profile;
        foreach(var hero in Forge.Heroes)
        {
            var screen=OpenForge();Check(screen.SelectHero(hero),hero.Id+": actual Hero Forge archetype selection");var masks=Masks(hero);var emblems=Emblems(hero);
            Check(masks.Length==2&&emblems.Length==2&&masks[0].Mesh!=masks[1].Mesh&&emblems[0].Mesh!=emblems[1].Mesh,hero.Id+": two distinct authored mask assets and two distinct emblem assets");
            string protectedBefore=Protected(profile.Data);
            Check(screen.SelectCosmetic(CosmeticSlot.Suit,Suit(hero.Id))&&screen.SelectCosmetic(CosmeticSlot.Mask,masks[0].Id)&&screen.SelectCosmetic(CosmeticSlot.Emblem,emblems[0].Id),hero.Id+": actual compatible costume, mask and emblem selectors");
            Check(screen.SetColors(CityColor.Red,CityColor.Cyan,CityColor.Amber),hero.Id+": select all three palette channels");yield return null;yield return null;
            var model=screen.PreviewModel;var assembler=model.GetComponent<HeroAppearanceRenderer>();var animator=model.GetComponent<Animator>();var camera=model.transform.parent.GetComponentInChildren<Camera>();var rt=screen.Preview;var material=assembler.ColorMaterial;
            var first=Capture(rt,"preview/"+masks[0].Id+".png");Check(screen.SelectCosmetic(CosmeticSlot.Mask,masks[1].Id),hero.Id+": second mask replaces first");yield return null;yield return null;var second=Capture(rt,"preview/"+masks[1].Id+".png");
            Check(Difference(first,second)>50,hero.Id+": mask selection visibly changes actual preview pixels");Destroy(first);Destroy(second);
            first=Capture(rt,"preview/"+emblems[0].Id+".png");Check(screen.SelectCosmetic(CosmeticSlot.Emblem,emblems[1].Id),hero.Id+": second emblem replaces first");yield return null;yield return null;second=Capture(rt,"preview/"+emblems[1].Id+".png");
            Check(Difference(first,second)>5,hero.Id+": emblem selection visibly changes actual preview pixels");Destroy(first);Destroy(second);
            Colors(assembler,hero,profile.Appearance(hero));Check(Protected(profile.Data)==protectedBefore,hero.Id+": costume changes preserve every noncosmetic profile field, including purchased tiers and records");
            Check(!screen.SelectCosmetic(CosmeticSlot.Mask,"does-not-exist")&&!screen.SelectCosmetic(CosmeticSlot.Mask,Masks(Forge.Heroes.First(h=>h!=hero))[0].Id),hero.Id+": invalid and wrong-archetype masks refused");
            Check(screen.Root.Query<Button>().ToList().All(b=>!(b.name??"").StartsWith("cosmetic-unavailable-")),hero.Id+": Forge displays no missing-art option buttons");
            yield return null;int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,textures=Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            for(int i=0;i<32;i++){screen.SetColors(Forge.SuitColors[i%Forge.SuitColors.Length],CityColor.Cyan,CityColor.Amber);screen.SelectCosmetic(CosmeticSlot.Mask,masks[i%2].Id);screen.SelectCosmetic(CosmeticSlot.Emblem,emblems[i%2].Id);}yield return null;yield return null;
            Check(screen.PreviewModel==model&&screen.Preview==rt&&model.GetComponent<Animator>()==animator&&model.transform.parent.GetComponentInChildren<Camera>()==camera&&assembler.ColorMaterial==material,hero.Id+": 96 edits reuse model, Animator, camera, render texture and material");
            Check(Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes&&Resources.FindObjectsOfTypeAll<Material>().Length==materials&&Resources.FindObjectsOfTypeAll<Texture2D>().Length==textures,hero.Id+": 96 edits retain zero extra live mesh/material/texture resources");
            Check(model.GetComponentsInChildren<SkinnedMeshRenderer>().Length==3,hero.Id+": masks replace one bounded renderer; no stacked helmets");
            var unknown=profile.Appearance(hero);unknown.SuitId="future-suit";unknown.MaskId="future-mask";unknown.EmblemId="future-emblem";Check(profile.SetAppearance(unknown),hero.Id+": future IDs can be stored without destructive repair");
            screen.Open();Check(assembler.SuitId==hero.Id+"-legacy"&&profile.Appearance(hero).MaskId=="future-mask",hero.Id+": unknown IDs render legacy safely and remain in save");
            var other=new GameObject("Isolated unknown-ID reload").AddComponent<PlayerProgression>();other.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),profile.SavePath);
            Check(other.Appearance(hero).SuitId=="future-suit"&&other.Appearance(hero).EmblemId=="future-emblem",hero.Id+": unknown IDs survive actual disk reload");Destroy(other.gameObject);
            screen.SelectCosmetic(CosmeticSlot.Suit,Suit(hero.Id));screen.SelectCosmetic(CosmeticSlot.Emblem,emblems[1].Id);
            screen.SetColors(hero.Id=="nova"?CityColor.UiPurple:hero.Id=="titan"?CityColor.Red:CityColor.Blue,CityColor.Metal,hero.Id=="titan"?CityColor.Amber:CityColor.Cyan);
            foreach(var mask in masks){screen.SelectCosmetic(CosmeticSlot.Mask,mask.Id);yield return Poses(screen,hero,mask.Id);}
            screen.SelectCosmetic(CosmeticSlot.Mask,masks[1].Id);yield return null;yield return null;appearances[hero.Id]=profile.Appearance(hero);
            var menuRT=new RenderTexture(1440,900,24);menuRT.Create();menu.Panel.targetTexture=menuRT;yield return null;yield return null;
            var menuImage=Capture(menuRT,"preview/"+hero.Id+"-forge-menu.png");Destroy(menuImage);menu.Panel.targetTexture=null;menuRT.Release();Destroy(menuRT);screen.Close();
        }
    }
}
#endif
