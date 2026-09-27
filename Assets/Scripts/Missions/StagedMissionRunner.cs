#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

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
    /// Staged missions vary per spawn (StagedScenario "Variation"); suites fix the seed so a run is reproducible.
    protected int NextSeed = 1000;
    protected CrimeEncounter Spawn(EncounterDefinition def, int? seed = null)
    {
        int used = seed ?? NextSeed++; if (def.Scenario is StagedScenario) StagedState.SeedOverride = used;
        int district = -1;
        Check(W.City.PickEncounterSite(W.Hero.transform.position, s => W.Crimes.TrueForAll(c => c == null || c.Encounter == null || Vector3.Distance(c.Encounter.Site, s) > 45f), ref district, out var site),
            "Real encounter site found for " + def.DisplayName);
        var crime = W.SpawnEncounter(def, site);
        StagedState.SeedOverride = null;
        Check(crime != null && crime.Encounter != null && crime.Encounter.Scenario != null, $"{def.DisplayName} spawned at {site} with its {def.Scenario.GetType().Name}" +
            (crime.Encounter.Scenario is StagedState st ? $" (seed {st.Seed}, yaw {st.Yaw:F0}, mirrored {st.Mirrored}, band {st.Band}, +{st.ExtraActors} hostiles, timeouts x{st.TimeoutScale:F2})." : "."));
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
    /// Verification only: refill the player's health every frame so a long solver run is not ended by NPC fire (the player's
    /// own survival is not what these suites measure). Set through the private setter by reflection; never used in play.
    protected bool Immortal;
    static readonly System.Reflection.MethodInfo setHealth = typeof(WorldSession).GetProperty("Health").GetSetMethod(true);
    void LateUpdate() { if (Immortal && W != null && W.Hero != null) setHealth.Invoke(W, new object[] { W.MaxHealth }); }
    /// Nearest NavMesh floor point (ground height under a target / point).
    protected static Vector3 Floor(Vector3 near) => NavMesh.SamplePosition(near, out var hit, 4f, NavMesh.AllAreas) ? hit.position : new Vector3(near.x, 0f, near.z);
    protected void KillGroup(StagedState s, string group)
    {
        var g = s.Group(group); if (g == null) return;
        foreach (var a in g) if (a.Npc != null && !a.Npc.Dead && !a.Captured && !a.Escaped) a.Npc.Damage(99999f, W.Powers);
    }
    protected void KillOthers(StagedState s, string keep) { foreach (var id in s.Actors.Keys) if (id != keep) KillGroup(s, id); }
    /// Generic stage solver: does what a player would do for the active stage (by its kind) through the same entry points
    /// (NPC damage, CombatImpact.Blast, ScriptedHold R, walking onto pickups / points), then waits for the transition.
    protected IEnumerator SolveStage(CrimeEncounter e, StagedState s, StagedScenario d)
    {
        int index = s.StageIndex; var spec = s.Stage; if (spec == null) yield break;
        float limit = Time.time + 25f + spec.Seconds;
        bool Moved() => s.StageIndex != index || s.Terminal != MissionTerminal.Running || Ended(e);
        switch (spec.Kind)
        {
            case StageKind.DefeatTargets: case StageKind.ChaseExit:
                while (!Moved() && Time.time < limit) { KillGroup(s, spec.Group); yield return null; }
                break;
            case StageKind.ProtectActors: case StageKind.Survive:
                Ground(s.ReachPoint(spec) + Vector3.forward * 2f);
                while (!Moved() && Time.time < limit) { KillOthers(s, spec.Group); yield return null; }
                break;
            case StageKind.EscortActors:
            {
                var g = s.Group(spec.Group); var lead = g.Find(a => a.Npc != null && !a.Npc.Dead && !a.Captured);
                Ground(Floor(lead.Npc.transform.position + Vector3.forward * 1.5f));
                float until = Time.time + 3f;
                while (!g.TrueForAll(a => a.Npc == null || a.Npc.Dead || a.Following) && Time.time < until) { KillOthers(s, spec.Group); yield return null; }
                Check(g.Exists(a => a.Following), $"Escort: walking up to {spec.Group} makes them follow ({g.FindAll(a => a.Following).Count}/{g.Count}).");
                Vector3 at = s.Point(spec.Point); int i = 0;
                foreach (var a in g) if (a.Npc != null && !a.Npc.Dead) a.Npc.Agent.Warp(Floor(at + Quaternion.Euler(0, 120 * i++, 0) * Vector3.forward * 1.5f));
                Ground(Floor(at + Vector3.right * 2f));
                Log($"NOTE escort: followers warped the last {Vector3.Distance(lead.Npc.transform.position, at):F0} m (walking there is NavMesh time, not stage logic).");
                while (!Moved() && Time.time < limit) { KillOthers(s, spec.Group); yield return null; }
                break;
            }
            case StageKind.InteractTargets:
                foreach (var t in s.TargetGroup(spec.Group))
                {
                    if (t.Done || t.Go == null) continue;
                    float side = t.Vehicle != null ? 2.6f : 1.8f;
                    Ground(Floor(t.Go.transform.position + Vector3.right * side));
                    e.ScriptedHold = true; float until = Time.time + d.HoldSeconds + 3f;
                    while (!t.Done && !Moved() && Time.time < until) { KillOthers(s, null); yield return null; }
                    e.ScriptedHold = false;
                }
                break;
            case StageKind.DestroyTargets:
                Away(e);
                foreach (var t in s.TargetGroup(spec.Group))
                    if (t.Hardpoint != null && !t.Hardpoint.Destroyed) CombatImpact.Blast(W.Powers, t.Go.transform.position + Vector3.right * 1.2f, 2.5f, 300f, t.Hardpoint.MaxHealth + 50f, .1f);
                    else if (t.Hardpoint == null && t.Go != null) CombatImpact.Blast(W.Powers, t.Go.transform.position + Vector3.right * 1.2f, 2.5f, 300f, 99999f, .1f);
                break;
            case StageKind.CollectItems:
                foreach (var t in s.TargetGroup(spec.Group))
                    if (!t.Done && t.Pickup != null && t.Pickup.Visual != null) { Ground(Floor(t.Pickup.Visual.transform.position)); yield return null; yield return null; }
                break;
            case StageKind.StopVehicles:
            {
                Away(e); float until = Time.time + 2f;
                var list = s.TargetGroup(spec.Group);
                while (!list.TrueForAll(t => t.Vehicle == null || t.Vehicle.Driving || t.Vehicle.Stopped) && Time.time < until) yield return null;
                foreach (var t in list) if (t.Vehicle != null && t.Vehicle.Driving)
                    CombatImpact.Blast(W.Powers, t.Go.GetComponent<Rigidbody>().worldCenterOfMass - Vector3.up * .5f, 2.5f, d.VehicleStopImpulse * 1.3f, 0f, .2f);
                break;
            }
            case StageKind.ReachArea: Ground(Floor(s.ReachPoint(spec) + (string.IsNullOrEmpty(spec.Point) ? Vector3.right * Mathf.Min(spec.Radius * .5f, 6f) : Vector3.zero))); break;   // inside the radius, beside (not inside) the actor / vehicle
            case StageKind.EscapeRadius:
            {
                Vector3 from = s.ReachPoint(spec), away = W.Hero.transform.position - from; away.y = 0; if (away.sqrMagnitude < .01f) away = Vector3.right;
                PlaceHero(from + away.normalized * (spec.Radius + 10f) + Vector3.up * 2f); break;
            }
            case StageKind.RaiseHeat: if (W.Heat < spec.Count + .1f) W.AddHeat(spec.Count + .1f - W.Heat); break;
        }
        while (!Moved() && Time.time < limit) yield return null;
        Check(Moved(), $"Stage {index} \"{spec.Label}\" ({spec.Kind}) solved -> {(s.Terminal == MissionTerminal.Running ? "stage " + s.StageIndex : s.Terminal.ToString())}.");
    }
    protected IEnumerator SolveUntil(CrimeEncounter e, StagedState s, StagedScenario d, int stopBefore)
    {
        while (s.Terminal == MissionTerminal.Running && s.StageIndex < stopBefore && !Ended(e)) yield return SolveStage(e, s, d);
    }
}
#endif
