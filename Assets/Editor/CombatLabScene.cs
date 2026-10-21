using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class CombatLabScene
{
    public const string Path="Assets/Scenes/CombatLab.unity";
    [MenuItem("Overpowered/Combat/Open Combat Lab")]
    public static void Open()
    {
        if(EditorApplication.isPlaying)return;
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        Ensure();EditorSceneManager.OpenScene(Path);
    }
    public static void Ensure()
    {
        if(File.Exists(Path))return;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene,Path);AssetDatabase.SaveAssets();
    }
}
