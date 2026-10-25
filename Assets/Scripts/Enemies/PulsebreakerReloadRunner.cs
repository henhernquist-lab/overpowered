#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

/// Separate-process Reload for the Pulsebreaker work: proves the live run's save still parses and keeps the saved loadout
/// and earned XP, and that the boss data a cold import produced is the same data the fight used. No boss exists in the hub.
public sealed class PulsebreakerReloadRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/Pulsebreaker/";
    protected override string ResultFile => "reload-results.txt";

    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var menu = FindAnyObjectByType<ModeScreens>();
        Check(menu != null && menu.Profile != null, "Home screen rebuilt its progression from the save file.");
        var profile = menu.Profile;
        Check(profile.LastError == null, "SECOND PROCESS: saved progression parsed without error.");
        Check(profile.EquippedA == Power("ice") && profile.EquippedB == Power("strength"),
            $"SECOND PROCESS: the saved loadout survived the reload ({profile.EquippedA?.Id} + {profile.EquippedB?.Id}).");
        Check(profile.Data.Level > 1 || profile.Data.Xp > 0,
            $"SECOND PROCESS: XP granted during the live run persisted (level {profile.Data.Level}, {profile.Data.Xp} XP).");

        var archetype = Resources.Load<EnemyArchetype>("Enemies/Pulsebreaker");
        var tuning = Resources.Load<PulsebreakerTuning>("Enemies/PulsebreakerTuning");
        Check(archetype != null && archetype.DisplayName == "Pulsebreaker" && archetype.VisualScale > 1f,
            "SECOND PROCESS: the imported Pulsebreaker archetype is intact.");
        Check(tuning != null && Mathf.Abs(tuning.SlamRadius - 8f) < .0001f && Mathf.Abs(tuning.PhaseHealthFraction - .4f) < .0001f,
            "SECOND PROCESS: the imported tuning still carries the verified slam radius and phase threshold.");
        var events = Resources.Load<EndlessWaveEvents>("ModeDirectors/EndlessWaveEvents");
        Check(events != null && events.MinibossArchetype == archetype && events.MinibossEvery == 5,
            "SECOND PROCESS: the Endless miniboss slot still points at Pulsebreaker, every 5 rounds.");
        Check(PulsebreakerBoss.Active == null, "SECOND PROCESS: no boss exists in the Home scene.");
        Log($"Save file: {Path.GetFileName(WorldSession.VerificationSavePath)}");
    }
}
#endif
