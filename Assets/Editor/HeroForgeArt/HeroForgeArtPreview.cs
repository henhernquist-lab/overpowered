#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class HeroForgeArtPreview
{
    public static void Run()
    {
        int code=0;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);Directory.CreateDirectory("Verification/HeroForgeArt/art");
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.59f,.67f);
            var light=new GameObject("Art inspection light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.transform.rotation=Quaternion.Euler(30,150,0);
            var palette=new GameObject("Palette").AddComponent<CityMaterials>();palette.Initialize(Resources.Load<CityPalette>("CityPalette"));
            var camera=new GameObject("Art inspection camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=palette.Palette.Colors[(int)CityColor.UiPanel];camera.fieldOfView=28;
            var rt=new RenderTexture(500,650,24);rt.Create();camera.targetTexture=rt;
            foreach(string id in new[]{"vector","titan","nova"})
            {
                var hero=Resources.Load<ForgeCatalog>("ForgeCatalog").Hero(id);var catalog=Resources.Load<CosmeticCatalog>("CosmeticCatalog");string suit=id=="vector"?"vector_suit_tactical":id=="titan"?"titan_suit_guardian":"nova_suit_energyweave";
                var masks=catalog.Items.Where(d=>d.HeroId==id&&d.Slot==CosmeticSlot.Mask&&d.Mesh!=null&&d.Available&&d.Compatible(hero,suit)&&d.Id.StartsWith(id+"_")).ToArray();
                foreach(var mask in masks)
                {
                    var model=UnityEngine.Object.Instantiate(hero.CharacterPrefab);var animator=model.GetComponent<Animator>();animator.runtimeAnimatorController=Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning").Controller;animator.applyRootMotion=false;animator.Play("Locomotion");animator.Update(.3f);
                    hero.Suit.ApplyBody(model.GetComponentInChildren<SkinnedMeshRenderer>());var appearance=HeroAppearanceRenderer.Install(model,hero,palette,true);
                    var emblem=catalog.Items.First(d=>d.HeroId==id&&d.Slot==CosmeticSlot.Emblem&&d.Mesh!=null&&d.Compatible(hero,suit));
                    appearance.Apply(new HeroAppearanceSave{HeroId=id,SuitId=suit,MaskId=mask.Id,EmblemId=emblem.Id,Primary=id=="nova"?CityColor.UiPurple:id=="titan"?CityColor.Red:CityColor.Blue,Secondary=CityColor.Metal,Accent=id=="titan"?CityColor.Amber:CityColor.Cyan});animator.Update(0);
                    // CPU-baked inspection proxies avoid stale Editor skinning buffers between manual renders.
                    var bounds=new Bounds();bool first=true;
                    foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var mesh=new Mesh();skin.BakeMesh(mesh);var go=new GameObject("Inspection proxy");go.transform.SetParent(skin.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=skin.sharedMaterial;skin.enabled=false;
                        if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);
                    }
                    camera.transform.position=bounds.center+new Vector3(.65f,.08f,4.1f)*bounds.size.y/1.75f;camera.transform.LookAt(bounds.center);camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes("Verification/HeroForgeArt/art/"+mask.Id+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                    foreach(var proxy in model.GetComponentsInChildren<MeshFilter>())UnityEngine.Object.DestroyImmediate(proxy.sharedMesh);UnityEngine.Object.DestroyImmediate(model);
                    Debug.Log("PASS native rendered "+mask.Id+" with actual registered suit/emblem");
                }
                if(masks.Length!=2)throw new Exception(id+" must have two authored headgear choices");
            }
            camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
        }
        catch(Exception e){Debug.LogError("FAIL "+e);code=1;}
        EditorApplication.Exit(code);
    }
}
#endif
