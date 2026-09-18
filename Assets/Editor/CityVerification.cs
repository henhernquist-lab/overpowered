using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CityVerification
{
    const string Key="Overpowered.CityVerification";
    public static void Run()
    {
        ProjectDataSetup.Create();
        // Two temporary DATA assets reuse the shipping projectile effect, giving a literal seventh entry.
        var original=AssetDatabase.LoadAssetAtPath<PowerDefinition>("Assets/Resources/Powers/fire.asset");
        for(int i=6;i<=7;i++)
        {
            string path=$"Assets/Resources/Powers/verification-{i}.asset";
            if(AssetDatabase.LoadAssetAtPath<PowerDefinition>(path)!=null) continue;
            var definition=Object.Instantiate(original); definition.Id="verification-"+i; definition.DisplayName="Verification power "+i;
            definition.InitiallyUnlocked=true; definition.Force=200f; definition.ResourceCost=0; definition.Damage=12f; definition.Color=Color.cyan;
            AssetDatabase.CreateAsset(definition,path);
            SessionState.SetBool(Key+"Created"+i,true);
        }
        AssetDatabase.SaveAssets();
        SessionState.SetString(Key,"first");
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity"); EditorApplication.isPlaying=true;
    }
    public static void Reload()
    {
        SessionState.SetString(Key,"reload"); EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity"); EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        string mode=SessionState.GetString(Key,""); if(string.IsNullOrEmpty(mode)) return;
        GameFlow.VerificationSandbox=true;
        string output=Path.GetFullPath("Verification/City"); Directory.CreateDirectory(output);
        WorldSession.VerificationSavePath=Path.Combine(output,"test-save.json");
        if(mode=="first" && File.Exists(WorldSession.VerificationSavePath))
            WorldSession.VerificationSavePath=Path.Combine(output,"test-save-"+System.Guid.NewGuid().ToString("N")+".json");
        if(mode=="first") File.WriteAllText(Path.Combine(output,"save-path.txt"),WorldSession.VerificationSavePath);
        else WorldSession.VerificationSavePath=File.ReadAllText(Path.Combine(output,"save-path.txt"));
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string mode=SessionState.GetString(Key,""); if(string.IsNullOrEmpty(mode)) return;
        var go=new GameObject("City verification runner"); var runner=go.AddComponent<CityVerificationRunner>(); runner.Mode=mode; runner.Finished=Finish;
    }
    public static void Finish(int code)
    {
        for(int i=6;i<=7;i++)
        {
            string path=$"Assets/Resources/Powers/verification-{i}.asset";
            if(!SessionState.GetBool(Key+"Created"+i,false)) continue;
            if(i==7) File.Copy(path,Path.GetFullPath("Verification/City/seventh-power.asset"),true);
            AssetDatabase.DeleteAsset(path); SessionState.EraseBool(Key+"Created"+i);
        }
        SessionState.EraseString(Key); EditorApplication.Exit(code);
    }
}
