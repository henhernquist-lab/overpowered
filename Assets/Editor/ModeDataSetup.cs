using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ModeDataSetup
{
    [MenuItem("Overpowered/Create missing mode assets and scenes")]
    public static void Create()
    {
        if(string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity");
        }
        Directory.CreateDirectory("Assets/Resources/Modes"); Directory.CreateDirectory("Assets/Resources/ModeRules"); Directory.CreateDirectory("Assets/Resources/Encounters");
        var heroRules=Asset<HeroModeRules>("Assets/Resources/ModeRules/Hero.asset");
        var villainRules=Asset<VillainModeRules>("Assets/Resources/ModeRules/Villain.asset");
        var bank=Encounter("bank","Bank break-out",CrimeKind.Robbery,0);
        var convoy=Encounter("convoy","Street ambush",CrimeKind.Mugging,0);
        var fire=Encounter("arson","Arson attack",CrimeKind.Fire,2);
        var encounters=new[]{bank,convoy,fire};
        Mode("hero","Hero Mode",0,true,PlayerSide.Hero,heroRules,encounters,-1f);
        Mode("villain","Villain Mode",1,true,PlayerSide.Villain,villainRules,encounters,1f);
        Mode("free-play","Free Play",2,false,PlayerSide.Hero,null,encounters,0);
        Mode("endless-fight","Endless Fight",3,false,PlayerSide.Villain,null,encounters,0);
        Scene("Assets/Scenes/Home.unity"); Scene("Assets/Scenes/Results.unity");
        var scenes=new List<EditorBuildSettingsScene>{new EditorBuildSettingsScene("Assets/Scenes/Home.unity",true),new EditorBuildSettingsScene("Assets/Scenes/Prototype.unity",true),new EditorBuildSettingsScene("Assets/Scenes/Results.unity",true)};
        foreach(var existing in EditorBuildSettings.scenes) if(!scenes.Exists(s=>s.path==existing.path)) scenes.Add(existing);
        EditorBuildSettings.scenes=scenes.ToArray(); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }
    static T Asset<T>(string path) where T:ScriptableObject
    {var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset!=null)return asset;asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);return asset;}
    static EncounterDefinition Encounter(string id,string name,CrimeKind kind,int hazards)
    {
        string path="Assets/Resources/Encounters/"+id+".asset";
        var asset=AssetDatabase.LoadAssetAtPath<EncounterDefinition>(path);if(asset!=null)return asset;
        asset=ScriptableObject.CreateInstance<EncounterDefinition>();asset.DisplayName=name;asset.Kind=kind;asset.Hazards=hazards;
        AssetDatabase.CreateAsset(asset,path);return asset;
    }
    static void Mode(string id,string name,int order,bool playable,PlayerSide side,ModeRules rules,EncounterDefinition[] encounters,float successHeat)
    {
        string path="Assets/Resources/Modes/"+id+".asset";if(AssetDatabase.LoadAssetAtPath<GameModeDefinition>(path)!=null)return;
        var asset=ScriptableObject.CreateInstance<GameModeDefinition>();asset.Id=id;asset.DisplayName=name;asset.MenuOrder=order;asset.Playable=playable;asset.Side=side;asset.Rules=rules;asset.Encounters=encounters;asset.SuccessHeat=successHeat;
        asset.Description=!playable?"More ways to play are on the way.":side==PlayerSide.Hero?"Complete 5 rescues. Stop fleeing robbers, free trapped civilians and extinguish arson. 3 failed events or defeats loses; 15-minute cap.":"Complete 5 heists. Steal loot, wreck props, sabotage arson sites, then escape. 3 failed events or defeats loses; 15-minute cap.";
        AssetDatabase.CreateAsset(asset,path);
    }
    static void Scene(string path)
    {
        if(File.Exists(path))return;
        var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        GameCamera.Ensure(scene,false);
        EditorSceneManager.SaveScene(scene,path);EditorSceneManager.CloseScene(scene,true);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
    }
}
