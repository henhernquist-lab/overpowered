using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneCameraSetup
{
    [MenuItem("Overpowered/Repair scene cameras")]
    public static void Install()
    {
        if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        var previous=EditorSceneManager.GetSceneManagerSetup();
        foreach(string name in new[]{GameFlow.HomeScene,GameFlow.CityScene,GameFlow.ResultsScene})
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity");
            var camera=GameCamera.Ensure(scene,name==GameFlow.CityScene);
            camera.transform.position=new Vector3(0,12,-18);camera.transform.rotation=Quaternion.Euler(25,0,0);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        if(previous.Length>0&&!string.IsNullOrEmpty(previous[0].path))EditorSceneManager.RestoreSceneManagerSetup(previous);
    }
}
