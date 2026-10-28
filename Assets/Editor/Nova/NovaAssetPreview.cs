#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class NovaAssetPreview
{
    public static void Run()
    {
        int exit=0;IDisposable scope=null;
        try
        {
            NovaCosmeticIntegration.Validate();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);scope=NovaCosmeticContent.RegisterForVerification();
            var palette=Resources.Load<CityPalette>("CityPalette");var owner=new GameObject("NOVA asset preview palette").AddComponent<CityMaterials>();owner.Initialize(palette);
            RenderSettings.ambientLight=new Color(.6f,.65f,.72f);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            var sun=new GameObject("light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.transform.rotation=Quaternion.Euler(35,150,0);
            var hero=Resources.Load<ForgeCatalog>("ForgeCatalog").Hero("nova");var model=UnityEngine.Object.Instantiate(hero.CharacterPrefab);var animator=model.GetComponent<Animator>();animator.runtimeAnimatorController=Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning").Controller;animator.applyRootMotion=false;animator.Play("Locomotion");animator.Update(0);
            var body=model.GetComponentInChildren<SkinnedMeshRenderer>();hero.Suit.ApplyBody(body);var render=HeroAppearanceRenderer.Install(model,hero,owner,true);
            var selection=new HeroAppearanceSave{HeroId="nova",SuitId="nova_suit_energyweave",MaskId="nova_mask_unmasked",EmblemId="nova_emblem_star",Primary=CityColor.UiPurple,Secondary=CityColor.Metal,Accent=CityColor.Cyan};render.Apply(selection);animator.Update(0);
            var camera=new GameObject("NOVA asset camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=palette.Colors[(int)CityColor.UiPanel];camera.fieldOfView=28;camera.transform.position=new Vector3(.6f,1.15f,4.3f);camera.transform.LookAt(new Vector3(0,.95f,0));
            var rt=new RenderTexture(500,650,24);rt.Create();camera.targetTexture=rt;
            // Editor render skinning gets a fresh animation evaluation after mesh/bone reassignment.
            animator.Play("Locomotion");animator.Update(.1f);body.updateWhenOffscreen=true;
            Debug.Log("MEASURED resolved suit="+render.SuitId+"; catalog="+Resources.Load<CosmeticCatalog>("CosmeticCatalog").Items.Length+"; module="+Resources.Load<CosmeticCatalog>(NovaCosmeticContent.ResourcePath).Items.Length);
            if(render.SuitId!="nova_suit_energyweave"||body.sharedMesh.vertexCount>=hero.Suit.Body.vertexCount)throw new Exception("NOVA new suit was not actually assembled");
            camera.Render();camera.Render();RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=null;
            Directory.CreateDirectory("Verification/NovaCustomization");File.WriteAllBytes("Verification/NovaCustomization/native-asset-preview.png",image.EncodeToPNG());
            Debug.Log("PASS NOVA native mesh imports, ordered existing bone paths, valid weights and one Animator");
            foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())Debug.Log($"MEASURED {r.name}: {r.sharedMesh.vertexCount} vertices / {r.bones.Length} bone entries");
            camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(sun.gameObject);UnityEngine.Object.DestroyImmediate(owner.gameObject);
        }
        catch(Exception e){Debug.LogError("FAIL "+e);exit=1;}
        finally{scope?.Dispose();}
        EditorApplication.Exit(exit);
    }
}
#endif
