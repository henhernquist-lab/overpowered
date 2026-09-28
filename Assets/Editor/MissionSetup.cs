using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// Mission data through the editor (no hand-written .asset files).
/// 1) "Create missing mission assets" (safe, idempotent, never overwrites): the four scenario assets under Resources/Missions
///    (tuning + objective labels + hint) and four EncounterDefinitions under Resources/Encounters/mission-*.asset that use them.
///    The shipping modes are NOT changed by this step.
/// 2) "Use missions in Hero and Villain modes" (explicit content switch, run after MissionVerification passes): Hero's
///    encounter list becomes robbery -> hostage -> fire, Villain's becomes the vault heist. Suites written against the original
///    mixed encounters read the mode list, so re-run them after switching (see STATUS).
/// Batch: -executeMethod MissionSetup.Batch (step 1) / MissionSetup.BatchUseInModes (1 + 2).
public static class MissionSetup
{
    const string Scenarios = "Assets/Resources/Missions/", Encounters = "Assets/Resources/Encounters/";
    [MenuItem("Overpowered/Missions/Create missing mission assets")]
    public static void Create()
    {
        Directory.CreateDirectory(Scenarios); AssetDatabase.Refresh();
        var robbery = Scenario<RobberyScenario>("robbery-getaway", s =>
        {
            s.Tasks = new[] { Label(ObjectiveTask.Robbers, "STOP THE GETAWAY") };
            s.Hint = "Robbers are running for their getaway cars. Take them down, or wreck, freeze or flip a car before it drives off. Hold R to cuff a frozen or rooted robber.";
        });
        var hostage = Scenario<HostageScenario>("hostage-rescue", s =>
        {
            s.Tasks = new[] { Label(ObjectiveTask.Threats, "TAKE DOWN THE GUNMEN"), Label(ObjectiveTask.Hostages, "FREE THE HOSTAGES") };
            s.Hint = "Gunmen guard pinned hostages. Once they spot you, the clock runs. Take them down, then shift each hostage's debris with your powers. Area attacks hurt hostages too.";
        });
        var fire = Scenario<FireScenario>("building-fire", s =>
        {
            s.Tasks = new[] { Label(ObjectiveTask.Fires, "PUT OUT THE FIRE"), Label(ObjectiveTask.Carry, "LEAD CIVILIANS OUT") };
            s.Hint = "Douse the flames: Ice, heavy punches and ground pounds snuff them fast; holding R sprays slowly. Then walk up to a freed civilian and lead them to the green safe point.";
        });
        var heist = Scenario<HeistScenario>("vault-heist", s =>
        {
            s.Tasks = new[] { Label(ObjectiveTask.Vault, "CRACK THE VAULT"), Label(ObjectiveTask.Loot, "GRAB THE LOOT"), Label(ObjectiveTask.Extract, "REACH THE GETAWAY VAN") };
            s.Hint = "Smash or burn the vault open (the alarm brings police), run over the loot bags, then get to the blue van.";
        });
        Encounter("mission-robbery", "Bank robbery getaway", CrimeKind.Robbery, robbery, e => { e.LooseProps = 3; e.Deadline = 150f; });
        Encounter("mission-hostage", "Hostage standoff", CrimeKind.Mugging, hostage, e => { e.LooseProps = 2; e.Deadline = 180f; });
        Encounter("mission-fire", "Apartment fire", CrimeKind.Fire, fire, e => { e.Deadline = 180f; });
        Encounter("mission-heist", "Vault heist", CrimeKind.Robbery, heist, e => { e.RespondingCops = 2; e.LooseProps = 4; e.Deadline = 240f; e.DestructionGoal = 0; });
        AssetDatabase.SaveAssets();
    }
    [MenuItem("Overpowered/Missions/Use missions in Hero and Villain modes")]
    public static void UseInModes()
    {
        Create();
        var hero = AssetDatabase.LoadAssetAtPath<GameModeDefinition>("Assets/Resources/Modes/hero.asset");
        var villain = AssetDatabase.LoadAssetAtPath<GameModeDefinition>("Assets/Resources/Modes/villain.asset");
        EncounterDefinition Load(string id) => AssetDatabase.LoadAssetAtPath<EncounterDefinition>(Encounters + id + ".asset");
        hero.Encounters = new[] { Load("mission-robbery"), Load("mission-hostage"), Load("mission-fire") };
        villain.Encounters = new[] { Load("mission-heist") };
        EditorUtility.SetDirty(hero); EditorUtility.SetDirty(villain); AssetDatabase.SaveAssets();
    }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
    public static void BatchUseInModes() { UseInModes(); EditorApplication.Exit(0); }
    static ObjectiveTaskLabel Label(ObjectiveTask task, string label) => new ObjectiveTaskLabel { Task = task, Label = label };
    static T Scenario<T>(string id, Action<T> configure) where T : EncounterScenario
    {
        string path = Scenarios + id + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path); if (existing != null) return existing;
        var asset = ScriptableObject.CreateInstance<T>(); configure(asset); AssetDatabase.CreateAsset(asset, path); return asset;
    }
    /// Scenario encounters spawn their own cast: the original mixed-encounter counts start at 0 (then configure).
    static void Encounter(string id, string title, CrimeKind kind, EncounterScenario scenario, Action<EncounterDefinition> configure)
    {
        string path = Encounters + id + ".asset";
        if (AssetDatabase.LoadAssetAtPath<EncounterDefinition>(path) != null) return;
        var e = ScriptableObject.CreateInstance<EncounterDefinition>();
        e.DisplayName = title; e.Kind = kind; e.Scenario = scenario;
        e.Robbers = e.Civilians = e.RespondingCops = e.Cars = e.LooseProps = e.Loot = e.Hazards = 0; e.DestructionGoal = 0;
        configure(e); AssetDatabase.CreateAsset(e, path);
    }
}
