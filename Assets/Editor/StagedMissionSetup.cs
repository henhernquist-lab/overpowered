using System.IO;
using UnityEditor;
using UnityEngine;

/// Staged mission data through the editor (no hand-written .asset files). Recipes: StagedMissionLibrary.
/// 1) "Create missing staged missions" (safe, idempotent, never overwrites): Resources/Missions/staged-<id>.asset
///    (StagedScenario) + Resources/Encounters/staged-<id>.asset (EncounterDefinition using it) for every library entry.
/// 2) "Rebuild staged missions from code" (explicit): overwrites those assets' data with the current recipes (discards any
///    inspector tuning of them).
/// Neither step changes the shipping Hero / Villain mode encounter lists; LOCAL decides that after its sweep.
/// Batch: -executeMethod StagedMissionSetup.Batch
public static class StagedMissionSetup
{
    const string Scenarios = "Assets/Resources/Missions/", Encounters = "Assets/Resources/Encounters/";
    [MenuItem("Overpowered/Missions/Create missing staged missions")]
    public static void Create() { Build(false); }
    [MenuItem("Overpowered/Missions/Rebuild staged missions from code (overwrites their tuning)")]
    public static void Rebuild() { Build(true); }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
    static void Build(bool overwrite)
    {
        Directory.CreateDirectory(Scenarios); Directory.CreateDirectory(Encounters); AssetDatabase.Refresh();
        foreach (var entry in StagedMissionLibrary.All)
        {
            string scenarioPath = Scenarios + entry.Asset + ".asset", encounterPath = Encounters + entry.Asset + ".asset";
            var fresh = StagedMissionLibrary.Create(entry);
            var scenario = AssetDatabase.LoadAssetAtPath<StagedScenario>(scenarioPath);
            if (scenario == null) { AssetDatabase.CreateAsset(fresh, scenarioPath); scenario = fresh; }
            else if (overwrite) { EditorUtility.CopySerialized(fresh, scenario); scenario.name = entry.Asset; EditorUtility.SetDirty(scenario); Object.DestroyImmediate(fresh); }
            else Object.DestroyImmediate(fresh);
            var encounter = AssetDatabase.LoadAssetAtPath<EncounterDefinition>(encounterPath);
            bool made = encounter == null;
            if (!made && !overwrite) continue;
            if (made) encounter = ScriptableObject.CreateInstance<EncounterDefinition>();
            // A scenario encounter spawns its own cast: the original mixed-encounter counts are 0.
            encounter.DisplayName = entry.Title; encounter.Kind = entry.Kind; encounter.Scenario = scenario; encounter.Deadline = entry.Deadline;
            encounter.Robbers = encounter.Civilians = encounter.RespondingCops = encounter.Cars = encounter.LooseProps = encounter.Loot = encounter.Hazards = 0; encounter.DestructionGoal = 0;
            if (made) AssetDatabase.CreateAsset(encounter, encounterPath); else EditorUtility.SetDirty(encounter);
        }
        AssetDatabase.SaveAssets();
    }
}
