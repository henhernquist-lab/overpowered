#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// Shared harness for staged-mission suites (StageFrameworkVerification, StagedMissionVerification): a real session, missions
/// spawned through WorldSession.SpawnEncounter at real encounter sites, and every EncounterResolved outcome recorded so a suite
/// can prove "exactly one outcome per mission". In-memory definitions are never saved.
public abstract class StagedMissionRunner : SessionVerificationRunner
{
    protected readonly List<EncounterOutcome> Outcomes = new List<EncounterOutcome>();
    protected IEnumerator Session(string a, string b, string mode)
    {
        yield return Enter(F.Heroes[0], a, b, mode);
        Outcomes.Clear(); W.Mode.EncounterResolved += o => Outcomes.Add(o);
        Cam.GetComponent<ThirdPersonCamera>().enabled = false;
    }
    /// An in-memory encounter definition that only runs its scenario (no legacy robbers / civilians / loot / props).
    protected static EncounterDefinition Definition(string name, EncounterScenario scenario, float deadline = 600f)
    {
        var d = ScriptableObject.CreateInstance<EncounterDefinition>(); d.name = name; d.DisplayName = name;
        d.Robbers = d.Civilians = d.RespondingCops = d.Cars = d.LooseProps = d.Loot = d.Hazards = 0; d.Deadline = deadline; d.Scenario = scenario;
        return d;
    }
    protected static StagedScenario Staged(params MissionStageSpec[] stages)
    { var s = ScriptableObject.CreateInstance<StagedScenario>(); s.name = "Verification staged mission"; s.Hint = "verification"; s.Stages = stages; return s; }
    protected static MissionStageSpec StageOf(StageKind kind, string label, string group = null, string point = null) => new MissionStageSpec { Kind = kind, Label = label, Group = group, Point = point };
    protected CrimeEncounter Spawn(EncounterDefinition def)
    {
        int district = -1;
        Check(W.City.PickEncounterSite(W.Hero.transform.position, s => W.Crimes.TrueForAll(c => c == null || c.Encounter == null || Vector3.Distance(c.Encounter.Site, s) > 45f), ref district, out var site),
            "Real encounter site found for " + def.DisplayName);
        var crime = W.SpawnEncounter(def, site);
        Check(crime != null && crime.Encounter != null && crime.Encounter.Scenario != null, $"{def.DisplayName} spawned at {site} with its {def.Scenario.GetType().Name}.");
        return crime.Encounter;
    }
    protected string Line(CrimeEncounter e) => W.Mode.Definition.Rules.Current(e).Text;
    protected void Away(CrimeEncounter e) => PlaceHero(e.Site + Vector3.up * 150f);
    protected void Ground(Vector3 at) => PlaceHero(at + Vector3.up * .05f);
    protected IEnumerator Outcome(CrimeEncounter e, float seconds)
    {
        float until = Time.time + seconds;
        while (!Ended(e) && Time.time < until) yield return null;
    }
    protected IEnumerator Until(Func<bool> condition, float seconds)
    {
        float until = Time.time + seconds;
        while (!condition() && Time.time < until) yield return null;
    }
    protected EncounterOutcome Result(CrimeEncounter e) => Outcomes.FirstOrDefault(o => ReferenceEquals(o.Encounter, e));
    protected bool Ended(CrimeEncounter e) => Outcomes.Exists(o => ReferenceEquals(o.Encounter, e));
    protected int OutcomeCount(CrimeEncounter e) => Outcomes.Count(o => ReferenceEquals(o.Encounter, e));
    protected string Describe(CrimeEncounter e) => Ended(e) ? (Result(e).Success ? "SUCCESS" : "FAILED") + " \"" + Result(e).Reason + "\"" : "no outcome";
    protected static string Counts(StagedState s) => $"started [{string.Join(",", s.Started)}], completed [{string.Join(",", s.Completed)}]";
    protected static bool ExactlyOnce(int[] counts, int upTo) { for (int i = 0; i < counts.Length; i++) if (counts[i] != (i < upTo ? 1 : 0)) return false; return true; }
}
#endif
