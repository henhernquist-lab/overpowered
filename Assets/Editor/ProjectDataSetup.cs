using System.IO;
using UnityEditor;
using UnityEngine;

public static class ProjectDataSetup
{
    [MenuItem("Overpowered/Create missing data assets")]
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources/Powers"); Directory.CreateDirectory("Assets/Resources/Effects");
        Asset<GameTuning>("Assets/Resources/GameTuning.asset");
        Asset<CityLayout>("Assets/Resources/CityLayout.asset");
        var flight=Asset<FlightEffect>("Assets/Resources/Effects/Flight.asset");
        var punch=Asset<PunchEffect>("Assets/Resources/Effects/Punch.asset");
        var grab=Asset<TelekinesisEffect>("Assets/Resources/Effects/Telekinesis.asset");
        var fire=Asset<FireBlastEffect>("Assets/Resources/Effects/FireBlast.asset");
        var ice=Asset<IceEffect>("Assets/Resources/Effects/Ice.asset");
        Define("flight","Flight",flight,true,d=> { d.Duration=PrototypeTuning.FlightDuration; d.ResourceCost=1; d.GroundRecharge=PrototypeTuning.FlightRechargePerSecond; d.Description="Hold F airborne. Space ascends; no directional input hovers. Land to recharge."; });
        Define("strength","Super Strength",punch,true,d=> { d.Charges=PrototypeTuning.PunchMaxCharges; d.Cooldown=PrototypeTuning.PunchCooldown; d.ChargeRecharge=PrototypeTuning.PunchChargeRecharge; d.Radius=PrototypeTuning.PunchRadius; d.Force=PrototypeTuning.PunchForce; d.UpwardForce=PrototypeTuning.PunchUpwardForce; d.ResourceCost=0; d.Damage=35; d.Description="Punch nearby physics objects and enemies. E always punches."; });
        Define("telekinesis","Telekinesis",grab,false,d=> { d.Charges=2; d.Force=1800; d.Duration=6; d.Description="Aim at a prop and click to grab. Click again to hurl; hold expires safely."; });
        Define("fire","Fire Blast",fire,false,d=> { d.Charges=3; d.Damage=45; d.Description="Launch an explosive physics projectile at the crosshair."; });
        Define("ice","Ice",ice,false,d=> { d.Charges=2; d.Damage=5; d.Color=Color.cyan; d.Description="Aim at an NPC or rigidbody to freeze it temporarily."; });
        EditorBuildSettings.scenes=new[] { new EditorBuildSettingsScene("Assets/Scenes/Prototype.unity",true) };
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }
    static T Asset<T>(string path) where T:ScriptableObject
    {
        var existing=AssetDatabase.LoadAssetAtPath<T>(path); if(existing!=null) return existing;
        var asset=ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset,path); return asset;
    }
    static void Define(string id,string name,PowerEffect effect,bool unlocked,System.Action<PowerDefinition> configure)
    {
        string path="Assets/Resources/Powers/"+id+".asset";
        if(AssetDatabase.LoadAssetAtPath<PowerDefinition>(path)!=null) return;
        var d=ScriptableObject.CreateInstance<PowerDefinition>(); d.Id=id; d.DisplayName=name; d.Effect=effect; d.InitiallyUnlocked=unlocked; configure(d);
        AssetDatabase.CreateAsset(d,path);
    }
    [MenuItem("Overpowered/Bake generated buildings into layout data")]
    public static void BakeLayout()
    {
        var tuning=Resources.Load<GameTuning>("GameTuning"); var layout=Resources.Load<CityLayout>("CityLayout");
        Undo.RecordObject(layout,"Bake building layout"); layout.Buildings=layout.Generate(tuning.City); layout.UseAuthoredBuildings=true;
        EditorUtility.SetDirty(layout); AssetDatabase.SaveAssets();
    }
}
