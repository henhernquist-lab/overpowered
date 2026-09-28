#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// See CivilianLedgerVerification. Session civilian outcomes through the real paths: player / hostile / environment harm
/// attributed honestly and counted once per civilian, a mission death counted as lost while a despawn and a mission's own
/// cleanup are not, a legacy encounter rescue counted once, a staged escort counted per survivor, the summary copied to the
/// session result, and nothing of it in the save.
public sealed class CivilianLedgerVerificationRunner : StagedMissionRunner
{
    protected override string Folder => "Verification/CivilianLedger/";
    protected override string ResultFile => "results.txt";
    CivilianLedger L => W.Civilians;
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Session("ice", "strength", "hero");
        Isolate(); Immortal = true;
        Attribution();
        yield return MissionLossAndDespawn();
        yield return HostileHarassPath();
        yield return LegacyRescue();
        yield return Escort();
        yield return SummaryAndSave();
    }
    void Attribution()
    {
        Log("---- ATTRIBUTION, COUNTED ONCE");
        var a = Actor(new Vector3(-4, 150, 8), NpcRole.Civilian, 1000f); var b = Actor(new Vector3(0, 150, 8), NpcRole.Civilian, 1000f); var c = Actor(new Vector3(4, 150, 8), NpcRole.Civilian, 1000f);
        var criminal = Actor(new Vector3(8, 150, 8), NpcRole.Criminal, 1000f);
        a.Damage(5f, W.Powers); a.Damage(5f, W.Powers);
        using (HarmContext.Hostile()) { b.Damage(5f, null); b.Damage(5f, null); }
        c.Damage(5f, null);
        criminal.Damage(5f, W.Powers);
        Check(L.Count(CivilianOutcome.HarmedByPlayer) == 1 && L.Has(a, CivilianOutcome.HarmedByPlayer), "Two player hits on one civilian: harmed-by-player counts 1.");
        Check(L.Count(CivilianOutcome.HarmedByHostile) == 1 && L.Has(b, CivilianOutcome.HarmedByHostile) && !L.Has(b, CivilianOutcome.HarmedByPlayer), "Hostile-scoped harm: harmed-by-hostile 1, never the player.");
        Check(L.Count(CivilianOutcome.HarmedByEnvironment) == 1 && L.Has(c, CivilianOutcome.HarmedByEnvironment), "Unattributed damage (no source, no scope) is ENVIRONMENT - not guessed as hostile or player.");
        Check(!L.Has(criminal, CivilianOutcome.HarmedByPlayer) && L.Summary.Killed == 0, "CONTROL: a criminal hit is not a civilian outcome; nobody died.");
        Check(HarmContext.Current == HarmCause.Environment, "The hostile scope is closed again (default Environment).");
    }
    IEnumerator MissionLossAndDespawn()
    {
        Log("---- MISSION LOSS vs DESPAWN vs CLEANUP");
        var sc = Staged(new MissionStageSpec { Kind = StageKind.Survive, Label = "WAIT", Seconds = 1.5f });
        sc.Actors = new[] { new ActorGroupSpec { Id = "people", Role = NpcRole.Civilian, Count = 3, Behavior = ActorBehavior.Idle, ScaleWithDifficulty = false } };
        var e = Spawn(Definition("Verification ledger mission", sc)); var s = (StagedState)e.Scenario; Away(e);
        var people = s.Group("people"); int killed = L.Summary.Killed, lost = L.Summary.LostInMission;
        people[0].Npc.Damage(99999f, null);
        Destroy(people[1].Npc.gameObject); yield return null;
        Check(L.Summary.LostInMission == lost + 1 && L.Summary.Killed == killed + 1, "A civilian killed during a mission: lost-in-mission +1 and killed +1 (environment-attributed, no source).");
        yield return Outcome(e, 4f); yield return null; yield return null;
        Check(Ended(e) && L.Summary.LostInMission == lost + 1 && L.Summary.Killed == killed + 1, "CONTROL: the despawned civilian and the one removed by the mission's cleanup are NOT counted as deaths.");
    }
    IEnumerator HostileHarassPath()
    {
        Log("---- REAL HOSTILE PATH (staged harass)");
        var sc = Staged(new MissionStageSpec { Kind = StageKind.Survive, Label = "WAIT", Seconds = 3f });
        sc.Actors = new[]
        {
            new ActorGroupSpec { Id = "victims", Role = NpcRole.Civilian, Count = 1, Ring = 0f, Behavior = ActorBehavior.Idle, HealthMultiplier = 10f, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "raider", Count = 1, Ring = 1f, Behavior = ActorBehavior.Harass, BehaviorArgument = "victims" },
        };
        var e = Spawn(Definition("Verification ledger harass", sc)); var s = (StagedState)e.Scenario; Away(e);
        var victim = s.Group("victims")[0].Npc;
        yield return Outcome(e, 5f);
        Check(victim.Health < victim.MaxHealth && L.Has(victim, CivilianOutcome.HarmedByHostile) && !L.Has(victim, CivilianOutcome.HarmedByEnvironment), $"A raider harassing a civilian (real Harass path) is attributed to hostiles ({victim.MaxHealth - victim.Health:F1} damage).");
    }
    IEnumerator LegacyRescue()
    {
        Log("---- LEGACY ENCOUNTER RESCUE");
        var def = Instantiate(Resources.Load<EncounterDefinition>("Encounters/bank")); def.Robbers = 0; def.RespondingCops = 0; def.Deadline = 600f;
        var e = Spawn(def); var civilian = e.Civilians[0]; int rescued = L.Summary.Rescued;
        civilian.Blockade.position = civilian.BlockadeStart + Vector3.right * (def.PropMoveDistance + 2f); civilian.Blockade.isKinematic = true; Physics.SyncTransforms();
        Ground(Floor(civilian.Npc.transform.position + Vector3.forward * 1.2f));
        e.ScriptedHold = true; yield return Until(() => civilian.Saved, def.HoldSeconds + 2f);
        yield return new WaitForSeconds(def.HoldSeconds + .5f); e.ScriptedHold = false;
        Check(civilian.Saved && L.Summary.Rescued == rescued + 1 && L.Has(civilian.Npc, CivilianOutcome.Rescued), "Holding R at a freed civilian of an original encounter: rescued +1, and holding on does not count them again.");
        Away(e);
    }
    IEnumerator Escort()
    {
        Log("---- STAGED ESCORT");
        var sc = Staged(new MissionStageSpec { Kind = StageKind.EscortActors, Label = "ESCORT", Group = "walkers", Point = "safe", Radius = 5f, AllowedLosses = 1 });
        sc.Points = new[] { new MissionPoint { Id = "safe", Angle = 90, Distance = 20 } };
        sc.Actors = new[] { new ActorGroupSpec { Id = "walkers", Role = NpcRole.Civilian, Count = 3, Ring = 1.5f, Behavior = ActorBehavior.Follow, ScaleWithDifficulty = false } };
        var e = Spawn(Definition("Verification ledger escort", sc)); var s = (StagedState)e.Scenario; int escorted = L.Summary.SafelyEscorted;
        s.Group("walkers")[2].Npc.Damage(99999f, null);
        yield return SolveStage(e, s, sc);
        yield return Outcome(e, 3f);
        Check(Ended(e) && Result(e).Success && L.Summary.SafelyEscorted == escorted + 2, $"Escort completed with 2 of 3 alive: safely-escorted +2 (the dead one is not).");
    }
    IEnumerator SummaryAndSave()
    {
        Log("---- SUMMARY AND SAVE");
        var summary = L.Summary; Log("Session summary: " + summary);
        string path = W.Progression.SavePath;
        W.Mode.ReturnHome(); yield return Scene(GameFlow.HomeScene);
        var result = GameFlow.Instance.Result;
        Check(result == null || result.Civilians.Rescued == summary.Rescued, "The session result carries the civilian summary (when a result is produced).");
        string json = File.Exists(path) ? File.ReadAllText(path) : "";
        Check(json.Length > 0 && !json.Contains("Harmed") && !json.Contains("Civilian") && !json.Contains("Escorted"), "The progression save contains no civilian ledger data or per-session actor ids.");
    }
}
#endif
