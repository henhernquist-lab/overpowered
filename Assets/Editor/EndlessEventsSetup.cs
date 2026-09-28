using UnityEditor;
using UnityEngine;

/// Endless milestone data through the editor (no hand-written .asset files).
/// 1) "Create missing Endless wave events" (safe, idempotent, never overwrites): Resources/ModeDirectors/EndlessWaveEvents.asset
///    with five modifiers (Armoured, Frenzied, Swarm, Veterans, Glass cannons), elites from wave 3 and a Brute miniboss every
///    5th wave. It is NOT assigned to any director by this step: the shipping Endless waves stay exactly as they are.
/// 2) "Use wave events in Endless" (explicit content switch, after EndlessEventsVerification / EndlessSimulation pass):
///    assigns it to Resources/ModeDirectors/EndlessWaves.asset (shared by Hero and Villain Endless Fight).
/// Batch: -executeMethod EndlessEventsSetup.Batch (step 1 only).
public static class EndlessEventsSetup
{
    public const string Path = "Assets/Resources/ModeDirectors/EndlessWaveEvents.asset";
    [MenuItem("Overpowered/Endless/Create missing Endless wave events")]
    public static EndlessWaveEvents Create()
    {
        var existing = AssetDatabase.LoadAssetAtPath<EndlessWaveEvents>(Path); if (existing != null) return existing;
        var e = Build(); AssetDatabase.CreateAsset(e, Path); AssetDatabase.SaveAssets(); return e;
    }
    [MenuItem("Overpowered/Endless/Use wave events in Endless (content switch)")]
    public static void UseInEndless()
    {
        var director = AssetDatabase.LoadAssetAtPath<EndlessWaveDirector>("Assets/Resources/ModeDirectors/EndlessWaves.asset");
        director.Events = Create(); EditorUtility.SetDirty(director); AssetDatabase.SaveAssets();
    }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
    /// The recipe (also used in memory by the verification suites, so they test exactly what Create writes).
    public static EndlessWaveEvents Build()
    {
        var e = ScriptableObject.CreateInstance<EndlessWaveEvents>();
        e.MinibossArchetype = AssetDatabase.LoadAssetAtPath<EnemyArchetype>("Assets/Resources/Enemies/Brute.asset");
        e.Modifiers = new[]
        {
            new WaveModifier { Id = "armoured", DisplayName = "ARMOURED", Weight = 3, FromWave = 4, Health = 1.5f, Score = 1.3f },
            new WaveModifier { Id = "frenzied", DisplayName = "FRENZIED", Weight = 3, FromWave = 4, Damage = 1.35f, Score = 1.3f },
            new WaveModifier { Id = "swarm", DisplayName = "SWARM", Weight = 2, FromWave = 4, Count = 1.5f, Health = .75f, Score = 1.2f },
            new WaveModifier { Id = "glass", DisplayName = "GLASS CANNONS", Weight = 1, FromWave = 6, Health = .6f, Damage = 1.6f, Score = 1.2f },
            new WaveModifier { Id = "veterans", DisplayName = "VETERANS", Weight = 2, FromWave = 8, EliteShareBonus = .2f, Score = 1.4f },
        };
        return e;
    }
}
