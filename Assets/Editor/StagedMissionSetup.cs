using System.IO;
using UnityEditor;
using UnityEngine;

/// Staged mission data through the editor (no hand-written .asset files). Recipes: StagedMissionLibrary.
/// 1) "Create missing staged missions" (safe, idempotent, never overwrites): Resources/Missions/staged-<id>.asset
///    (StagedScenario) + Resources/Encounters/staged-<id>.asset (EncounterDefinition using it) for every library entry.
/// 2) "Rebuild staged missions from code" (explicit): overwrites those assets' data with the current recipes (discards any
///    inspector tuning of them).
/// 3) "Create missing mission rotations": Resources/Rotations/{hero,villain}-rotation.asset, EncounterSelection assets that mix
///    the mode's current Encounters (weight 1), the Missions/ scenario encounters that exist (weight 1) and this side's staged
///    missions (library weight, first difficulty band, district filter), anti-repeat 2, difficulty = session successes.
/// 4) "Use mission rotations in Hero and Villain modes" (explicit content switch): sets the modes' Selection.
/// Steps 1-3 never change the shipping Hero / Villain modes; LOCAL decides step 4 after its sweep.
/// Batch: -executeMethod StagedMissionSetup.Batch (steps 1 and 3)
public static class StagedMissionSetup
{
    const string Scenarios = "Assets/Resources/Missions/", Encounters = "Assets/Resources/Encounters/";
    [MenuItem("Overpowered/Missions/Create missing staged missions")]
    public static void Create() { Build(false); }
    [MenuItem("Overpowered/Missions/Rebuild staged missions from code (overwrites their tuning)")]
    public static void Rebuild() { Build(true); }
    public static void Batch() { Create(); CreateRotations(); EditorApplication.Exit(0); }
    const string Rotations = "Assets/Resources/Rotations/";
    [MenuItem("Overpowered/Missions/Create missing mission rotations")]
    public static void CreateRotations()
    {
        Create(); Directory.CreateDirectory(Rotations); AssetDatabase.Refresh();
        Rotation("hero-rotation", PlayerSide.Hero, "Assets/Resources/Modes/hero.asset", "mission-robbery", "mission-hostage", "mission-fire");
        Rotation("villain-rotation", PlayerSide.Villain, "Assets/Resources/Modes/villain.asset", "mission-heist");
        AssetDatabase.SaveAssets();
    }
    [MenuItem("Overpowered/Missions/Use mission rotations in Hero and Villain modes (content switch)")]
    public static void UseRotationsInModes()
    {
        CreateRotations();
        foreach (var (mode, rotation) in new[] { ("hero", "hero-rotation"), ("villain", "villain-rotation") })
        {
            var m = AssetDatabase.LoadAssetAtPath<GameModeDefinition>("Assets/Resources/Modes/" + mode + ".asset");
            m.Selection = AssetDatabase.LoadAssetAtPath<EncounterSelection>(Rotations + rotation + ".asset"); EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
    }
    static void Rotation(string id, PlayerSide side, string modePath, params string[] scenarioMissions)
    {
        string path = Rotations + id + ".asset";
        if (AssetDatabase.LoadAssetAtPath<EncounterSelection>(path) != null) return;
        var options = new System.Collections.Generic.List<EncounterSelection.Option>();
        var mode = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(modePath);
        if (mode != null && mode.Encounters != null) foreach (var e in mode.Encounters) if (e != null) options.Add(new EncounterSelection.Option { Encounter = e, Weight = 1f });
        foreach (var name in scenarioMissions)
        { var e = AssetDatabase.LoadAssetAtPath<EncounterDefinition>(Encounters + name + ".asset"); if (e != null) options.Add(new EncounterSelection.Option { Encounter = e, Weight = 1f }); }
        foreach (var entry in StagedMissionLibrary.All)
        {
            if (entry.Side != side) continue;
            var e = AssetDatabase.LoadAssetAtPath<EncounterDefinition>(Encounters + entry.Asset + ".asset");
            if (e != null) options.Add(new EncounterSelection.Option { Encounter = e, Weight = entry.Weight, MinDifficulty = entry.MinBand, MaxDifficulty = 999, Districts = entry.Districts });
        }
        var selection = ScriptableObject.CreateInstance<EncounterSelection>();
        selection.Options = options.ToArray(); selection.AntiRepeatWindow = 2; selection.Difficulty = EncounterSelection.DifficultySource.Successes;
        selection.DifficultyStep = 1; selection.DistrictFallback = true; selection.Seeded = false;
        AssetDatabase.CreateAsset(selection, path);
    }
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
