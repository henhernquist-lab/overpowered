using UnityEditor;
using UnityEngine;

public static class PresentationSetup
{
    public static void Configure()
    {
        var tuning = AssetDatabase.LoadAssetAtPath<GameTuning>("Assets/Resources/GameTuning.asset");
        tuning.Feel.CartoonImpact = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Impacts/CFXR2 Ground Hit.prefab");
        if (tuning.Feel.CartoonImpact == null) throw new System.InvalidOperationException("Cartoon FX ground hit prefab missing");
        tuning.Feel.CartoonImpacts = true;
        var animation=AssetDatabase.LoadAssetAtPath<HumanoidAnimationTuning>("Assets/Resources/HumanoidAnimationTuning.asset");
        System.IO.Directory.CreateDirectory("Assets/Resources/Presentation");AssetDatabase.Refresh();
        animation.Model=Author(animation.Model,animation,false,"mannequin");EditorUtility.SetDirty(animation);
        foreach(var look in animation.NpcLooks)if(look!=null&&look.Prefab!=null){look.Prefab=Author(look.Prefab,animation,false,look.name);EditorUtility.SetDirty(look);}
        var catalog=AssetDatabase.LoadAssetAtPath<ForgeCatalog>("Assets/Resources/ForgeCatalog.asset");
        foreach(var hero in catalog.Heroes){hero.CharacterPrefab=Author(hero.CharacterPrefab,animation,true,hero.Id);EditorUtility.SetDirty(hero);}
        EditorUtility.SetDirty(tuning); AssetDatabase.SaveAssets();
        Debug.Log("PRESENTATION setup: pooled Cartoon FX ground hit on heavy impacts; existing Feel debris preserved");
        EditorApplication.Exit(0);
    }
    static GameObject Author(GameObject source,HumanoidAnimationTuning tuning,bool arms,string id)
    {

        var clone=Object.Instantiate(source);clone.name=source.name;
        var animator=clone.GetComponent<Animator>();animator.enabled=false;animator.runtimeAnimatorController=tuning.Controller;
        var rigged=clone.GetComponent<HumanoidRigging>();
        if(rigged==null){rigged=clone.AddComponent<HumanoidRigging>();rigged.Author(animator,tuning,arms);}
        else
        {
            rigged.Builder.enabled=false;
            rigged.Builder.layers[0].rig.transform.SetParent(animator.avatarRoot,false);
            var data=rigged.Look.data;data.limits=new Vector2(-tuning.LookLimit,tuning.LookLimit);rigged.Look.data=data;
            rigged.Look.data.sourceObjects[0].transform.SetParent(animator.transform,true);
            foreach(var hand in new[]{rigged.LeftHand,rigged.RightHand})if(hand!=null){bool left=hand==rigged.LeftHand;hand.data.target.name=left?"Left hand target":"Right hand target";hand.data.hint.name=left?"Left elbow hint":"Right elbow hint";hand.data.target.SetParent(animator.transform,true);hand.data.hint.SetParent(animator.transform,true);}
        }
        var prefab=PrefabUtility.SaveAsPrefabAsset(clone,"Assets/Resources/Presentation/"+id+"-rigged.prefab");
        Object.DestroyImmediate(clone);return prefab;
    }
}
