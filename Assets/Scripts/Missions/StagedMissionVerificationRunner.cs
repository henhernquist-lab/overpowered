#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// See StagedMissionVerification. For every staged mission asset (StagedMissionLibrary via StagedMissionSetup): data rules
/// (references resolve, >= 2 stage kinds, not an R-hold loop, no two missions with the same stage sequence), then in a fresh
/// real session of its side: one FAILURE path that exercises the mission's own mechanic with controls, and one full SOLVE
/// through the generic stage solver with every stage started / completed exactly once and exactly one outcome.
/// Fail paths that would take minutes use IN-MEMORY clones with one timer changed (logged, never saved).
public sealed class StagedMissionVerificationRunner : StagedMissionRunner
{
    protected override string Folder => "Verification/StagedMissions/";
    protected override string ResultFile => "results.txt";
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        Data();
        foreach (var entry in StagedMissionLibrary.All)
        {
            Log($"---- {entry.Title.ToUpperInvariant()} ({entry.Side}, {entry.Asset})");
            bool hero = entry.Side == PlayerSide.Hero;
            yield return Session(hero ? "ice" : "fire", hero ? "strength" : "ice", hero ? "hero" : "villain");
            Immortal = true;
            yield return FailPath(entry);
            yield return Solve(entry);
            Immortal = false;
        }
        Log("NOTE Rooftop Rescue is not built: NPCs exist only on the ground NavMesh (no rooftop NavMesh), see STATUS.");
        Log("LIMIT: the solver teleports the hero and acts through NPC damage / CombatImpact.Blast / ScriptedHold R; escort followers are warped the last stretch; player health is refilled by the harness. No human playtest of difficulty, pacing or readability.");
    }
    // ---------------------------------------------------------------- data rules
    static readonly StageKind[] PowerKinds = { StageKind.DefeatTargets, StageKind.ChaseExit, StageKind.DestroyTargets, StageKind.StopVehicles, StageKind.ProtectActors, StageKind.EscortActors, StageKind.RaiseHeat };
    void Data()
    {
        Check(StagedMissionLibrary.All.Count(x => x.Side == PlayerSide.Hero) == 4 && StagedMissionLibrary.All.Count(x => x.Side == PlayerSide.Villain) == 5, "Library: 4 hero missions (Rooftop Rescue omitted, see STATUS) and 5 villain missions.");
        var sequences = new Dictionary<string, string>();
        foreach (var entry in StagedMissionLibrary.All)
        {
            var def = Resources.Load<EncounterDefinition>("Encounters/" + entry.Asset); var d = def != null ? def.Scenario as StagedScenario : null;
            Check(d != null && def.Robbers + def.Civilians + def.RespondingCops + def.Cars + def.LooseProps + def.Loot + def.Hazards == 0, $"{entry.Asset}: encounter asset with a StagedScenario and no legacy cast.");
            Check(References(d, out string problem), $"{entry.Asset}: every group / point / anchor reference resolves{(problem == null ? "" : " - " + problem)}.");
            var kinds = d.Stages.Select(x => x.Kind).ToArray();
            int holds = kinds.Count(k => k == StageKind.InteractTargets);
            Check(kinds.Distinct().Count() >= 2 && holds <= 1 && holds * 2 < kinds.Length && kinds.Any(k => PowerKinds.Contains(k)),
                $"{entry.Asset}: {kinds.Length} stages [{string.Join(" > ", d.Stages.Select(x => x.Label))}] - {kinds.Distinct().Count()} kinds, {holds} hold-R stage(s), needs powers/combat.");
            string key = string.Join(",", kinds);
            Check(!sequences.ContainsKey(key), $"{entry.Asset}: stage sequence differs from every other mission{(sequences.TryGetValue(key, out var other) ? " (same as " + other + ")" : "")}.");
            sequences[key] = entry.Asset;
        }
    }
    static bool References(StagedScenario d, out string problem)
    {
        problem = null;
        var actors = new HashSet<string>(d.Actors.Select(a => a.Id)); var targets = new HashSet<string>(d.Targets.Select(t => t.Id)); var points = new HashSet<string>(d.Points.Select(p => p.Id));
        bool Any(string id) => actors.Contains(id) || targets.Contains(id) || points.Contains(id);
        foreach (var a in d.Actors)
        {
            if (!string.IsNullOrEmpty(a.AtPoint) && !points.Contains(a.AtPoint)) { problem = $"actor {a.Id} AtPoint {a.AtPoint}"; return false; }
            if (a.Behavior == ActorBehavior.Flee && (string.IsNullOrEmpty(a.BehaviorArgument) || a.BehaviorArgument.Split(',').Any(x => !points.Contains(x.Trim())))) { problem = $"actor {a.Id} exits"; return false; }
            if (!string.IsNullOrEmpty(a.BehaviorArgument) && a.Behavior != ActorBehavior.Flee && !actors.Contains(a.BehaviorArgument) && !targets.Contains(a.BehaviorArgument) && !a.BehaviorArgument.Contains(",")) { problem = $"actor {a.Id} argument {a.BehaviorArgument}"; return false; }
        }
        foreach (var t in d.Targets) if (!string.IsNullOrEmpty(t.AtPoint) && !points.Contains(t.AtPoint)) { problem = $"target {t.Id} AtPoint {t.AtPoint}"; return false; }
        foreach (var s in d.Stages)
        {
            if (!string.IsNullOrEmpty(s.Group) && !actors.Contains(s.Group) && !targets.Contains(s.Group)) { problem = $"stage {s.Label} group {s.Group}"; return false; }
            if (!string.IsNullOrEmpty(s.Point) && !points.Contains(s.Point)) { problem = $"stage {s.Label} point {s.Point}"; return false; }
            if (!string.IsNullOrEmpty(s.RepeatGroup) && !actors.Contains(s.RepeatGroup)) { problem = $"stage {s.Label} repeat {s.RepeatGroup}"; return false; }
            bool needsGroup = s.Kind != StageKind.Survive && s.Kind != StageKind.RaiseHeat && s.Kind != StageKind.EscapeRadius && !(s.Kind == StageKind.ReachArea && !string.IsNullOrEmpty(s.Point));
            if (needsGroup && string.IsNullOrEmpty(s.Group)) { problem = $"stage {s.Label} has no group"; return false; }
            foreach (var a in (s.OnStart ?? new StageAction[0]).Concat(s.OnComplete ?? new StageAction[0]))
            {
                if (a.Kind == StageActionKind.SpawnActors && !actors.Contains(a.Group)) { problem = $"action spawn actors {a.Group}"; return false; }
                if (a.Kind == StageActionKind.SpawnTargets && !targets.Contains(a.Group)) { problem = $"action spawn targets {a.Group}"; return false; }
                if (a.Kind == StageActionKind.SetBehavior && !actors.Contains(a.Group)) { problem = $"action behavior {a.Group}"; return false; }
                if ((a.Kind == StageActionKind.SpawnActors || a.Kind == StageActionKind.SpawnTargets) && !string.IsNullOrEmpty(a.Text) && !Any(a.Text)) { problem = $"anchor {a.Text}"; return false; }
            }
        }
        return true;
    }
    // ---------------------------------------------------------------- shared
    EncounterDefinition Def(StagedMissionLibrary.Entry entry, Action<StagedScenario> tweak = null)
    {
        var def = Resources.Load<EncounterDefinition>("Encounters/" + entry.Asset);
        if (tweak != null) { def = Instantiate(def); def.Scenario = Instantiate(def.Scenario); tweak((StagedScenario)def.Scenario); }
        return def;
    }
    IEnumerator Solve(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry)); var s = (StagedState)e.Scenario; var d = (StagedScenario)e.Definition.Scenario; Away(e);
        int n = d.Stages.Length, guard = 0;
        while (s.Terminal == MissionTerminal.Running && !Ended(e) && guard++ < n + 2)
        {
            Check(Line(e).StartsWith(s.Stage.Label), $"HUD objective line for stage {s.StageIndex}: \"{Line(e)}\".");
            yield return SolveStage(e, s, d);
        }
        yield return Outcome(e, 3f);
        Check(Ended(e) && Result(e).Success, $"SOLVE: {Describe(e)}.");
        Check(ExactlyOnce(s.Started, n) && ExactlyOnce(s.Completed, n), $"Every stage started and completed exactly once ({Counts(s)}).");
        yield return new WaitForSeconds(.5f);
        Check(OutcomeCount(e) == 1, "Exactly one outcome for the mission.");
    }
    IEnumerator FailPath(StagedMissionLibrary.Entry entry)
    {
        switch (entry.Id)
        {
            case "pursuit": yield return Pursuit(entry); break;
            case "convoy-intercept": yield return ConvoyIntercept(entry); break;
            case "blackout": yield return Blackout(entry); break;
            case "hold-the-block": yield return HoldTheBlock(entry); break;
            case "armored-heist": yield return ArmoredHeist(entry); break;
            case "sabotage-run": yield return SabotageRun(entry); break;
            case "convoy-robbery": yield return ConvoyRobbery(entry); break;
            case "distraction": yield return Distraction(entry); break;
            case "getaway": yield return Getaway(entry); break;
            default: throw new Exception("No failure path written for " + entry.Id);
        }
    }
    IEnumerator Failed(CrimeEncounter e, StagedState s, float seconds, string contains, int stage)
    {
        yield return Outcome(e, seconds);
        Check(Ended(e) && !Result(e).Success && Result(e).Reason.Contains(contains), $"FAIL PATH: {Describe(e)} (expected \"{contains}\").");
        Check(s.Completed[stage] == 0 && (stage + 1 >= s.Started.Length || s.Started[stage + 1] == 0) && !s.Complete(), $"Latched at stage {stage}: {Counts(s)}.");
    }
    // ---------------------------------------------------------------- HERO
    IEnumerator Pursuit(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry)); var s = (StagedState)e.Scenario; var d = (StagedScenario)e.Definition.Scenario; Away(e);
        var runners = s.Group("runners"); var posts = runners.Select(r => r.Npc.transform.position).ToArray();
        yield return new WaitForSeconds(1.5f);
        Check(s.StageIndex == 0 && runners.Select((r, i) => Vector3.Distance(r.Npc.transform.position, posts[i])).Max() < 3f, "CONTROL: before the hero arrives the crew holds position (no chase starts off-screen).");
        yield return SolveStage(e, s, d);
        var exits = s.ExitPoints(s.ActorSpec("runners"));
        Check(s.StageIndex == 1 && runners.Select(r => r.ExitIndex % exits.Length).Distinct().Count() == runners.Count, $"Arrival starts the chase; runners scatter to {runners.Count} different exits.");
        Away(e); yield return new WaitForSeconds(1f);
        float[] before = runners.Select(r => Vector3.Distance(r.Npc.transform.position, exits[r.ExitIndex % exits.Length])).ToArray();
        yield return new WaitForSeconds(1.5f);
        float[] after = runners.Select(r => Vector3.Distance(r.Npc.transform.position, exits[r.ExitIndex % exits.Length])).ToArray();
        Log($"MEASURED runner distance to exit over 1.5 s: {string.Join(", ", before.Select((b, i) => $"{b:F1}->{after[i]:F1} m"))}.");
        Check(after.Where((a, i) => a < before[i] - 1f).Count() >= 2, "Runners really head for their exits.");
        var held = runners[1]; int route = held.ExitIndex; held.Npc.Root(d.StuckSeconds * 2f + 1f);
        yield return Until(() => held.ExitIndex != route, d.StuckSeconds * 2f + 1.5f);
        Check(held.ExitIndex != route, $"A runner held in place (rooted) re-routes to its next exit after {d.StuckSeconds} s (exit {route % exits.Length} -> {held.ExitIndex % exits.Length}).");
        yield return Failed(e, s, 95f, "got away", 1);
    }
    IEnumerator ConvoyIntercept(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry)); var s = (StagedState)e.Scenario; var d = (StagedScenario)e.Definition.Scenario; Away(e);
        var trucks = s.TargetGroup("trucks"); var parked = trucks.Select(t => t.Go.transform.position).ToArray();
        yield return new WaitForSeconds(1.5f);
        Check(trucks.All(t => !t.Vehicle.Driving) && trucks.Select((t, i) => Vector3.Distance(t.Go.transform.position, parked[i])).Max() < .5f, "CONTROL: the trucks wait until the hero closes in.");
        Check(s.TargetGroup("crates").Count == 0 && s.Group("gunmen").Count == 0, "Gunmen and crates do not exist yet (they come out of the stopped trucks).");
        yield return SolveStage(e, s, d);
        yield return Until(() => trucks.All(t => t.Vehicle.Driving), 2f);
        Check(s.StageIndex == 1 && trucks.All(t => t.Vehicle.Driving), "Closing in starts the convoy: both trucks drive.");
        var body = trucks[0].Go.GetComponent<Rigidbody>();
        CombatImpact.Blast(W.Powers, body.worldCenterOfMass - Vector3.up * .5f, 2.5f, d.VehicleStopImpulse * .4f, 0f, .2f);
        Check(trucks[0].Vehicle.Driving, "CONTROL: a light hit (40% of the stop impulse) does not stop a truck.");
        Away(e);
        yield return Failed(e, s, 30f, "a vehicle got away", 1);
    }
    IEnumerator Blackout(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry, d => d.Stages[0].Timeout = 16f)); var s = (StagedState)e.Scenario; var d0 = (StagedScenario)e.Definition.Scenario;
        Log("In-memory clone: relay stage timeout 120 s -> 16 s.");
        var relays = s.TargetGroup("relays"); var relay = relays[0];
        Ground(Floor(relay.Go.transform.position + Vector3.right * 1.8f)); e.ScriptedHold = true;
        yield return Until(() => relay.Done, d0.HoldSeconds + 2f); e.ScriptedHold = false;
        Check(relay.Done && s.StageIndex == 0 && Line(e) == "RESTORE THE RELAYS 1/3", $"Holding R restores one relay: \"{Line(e)}\".");
        Away(e);
        var saboteur = s.Group("saboteurs").First(a => a.Npc != null && !a.Npc.Dead);
        saboteur.Npc.Agent.Warp(Floor(relay.Go.transform.position + Vector3.forward * 1.2f));
        yield return Until(() => !relay.Done, d0.HarassSeconds + 3f);
        Check(!relay.Done && Line(e) == "RESTORE THE RELAYS 0/3", $"A saboteur left beside a restored relay knocks it out again after {d0.HarassSeconds} s: \"{Line(e)}\".");
        yield return Failed(e, s, 20f, "Timed out: restore the relays", 0);
    }
    IEnumerator HoldTheBlock(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry, d => d.Stages[1].Seconds = 120f)); var s = (StagedState)e.Scenario; var d0 = (StagedScenario)e.Definition.Scenario;
        Log("In-memory clone: hold stage 40 s -> 120 s so an unattended block is certain to be lost first.");
        Away(e); var residents = s.Group("residents"); var raiders = s.Group("raiders");
        yield return new WaitForSeconds(1.5f);
        Check(s.StageIndex == 0 && residents.All(r => r.Npc.Health == r.Npc.MaxHealth), "CONTROL: nobody is hurt before the hero arrives.");
        yield return SolveStage(e, s, d0);
        Check(s.StageIndex == 1, "Arrival starts the defence.");
        Away(e); int maxAlive = 0; float until = Time.time + 90f; bool hurt = false;
        while (!Ended(e) && Time.time < until)
        {
            maxAlive = Mathf.Max(maxAlive, raiders.Count(r => r.Npc != null && !r.Npc.Dead));
            hurt |= residents.Any(r => r.Npc != null && (r.Npc.Dead || r.Npc.Health < r.Npc.MaxHealth));
            yield return null;
        }
        Log($"MEASURED unattended defence: {raiders.Count} raiders spawned in total, at most {maxAlive} alive at once (cap {d0.Stages[1].RepeatMaxAlive}).");
        Check(hurt && raiders.Count > 2 && maxAlive <= d0.Stages[1].RepeatMaxAlive, "Raiders hurt the residents, reinforcements keep arriving, never more than the cap alive.");
        yield return Failed(e, s, 1f, "hold the block: 2 lost", 1);
    }
    // ---------------------------------------------------------------- VILLAIN
    IEnumerator ArmoredHeist(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry, d => d.Stages[2].Timeout = 6f)); var s = (StagedState)e.Scenario; var d0 = (StagedScenario)e.Definition.Scenario;
        Log("In-memory clone: cash stage timeout 35 s -> 6 s.");
        Away(e); var door = s.TargetGroup("door")[0];
        CombatImpact.Blast(W.Powers, door.Go.transform.position + Vector3.right * 1.2f, 2.5f, 300f, 9999f, .1f);
        Check(!door.Hardpoint.Destroyed && door.Hardpoint.IgnoredHits >= 1, "CONTROL (wrong order): the strongroom door ignores a 9999-damage blast while its guards stand.");
        yield return SolveStage(e, s, d0);
        Check(s.StageIndex == 1 && door.Hardpoint.Armed, "Guards down: the door is exposed.");
        float heat = W.Heat;
        yield return SolveStage(e, s, d0);
        Check(s.StageIndex == 2 && (W.Heat >= heat + .9f || W.Stars >= W.Tuning.Heat.MaximumStars) && s.Group("response").Count >= 2 && s.TargetGroup("cash").Count == 3,
            $"Breach: Heat {heat:F2} -> {W.Heat:F2}, {s.Group("response").Count} response officers, {s.TargetGroup("cash").Count} cash bags dropped at the door.");
        Away(e);
        yield return Failed(e, s, 12f, "Timed out: grab the cash", 2);
    }
    IEnumerator SabotageRun(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry, d => d.Stages[1].Timeout = 5f)); var s = (StagedState)e.Scenario; var d0 = (StagedScenario)e.Definition.Scenario;
        Log("In-memory clone: node B timeout 45 s -> 5 s.");
        Away(e); var b = s.TargetGroup("node-b")[0];
        CombatImpact.Blast(W.Powers, b.Go.transform.position + Vector3.right * 1.2f, 2.5f, 300f, 9999f, .1f);
        Check(!b.Hardpoint.Destroyed && b.Hardpoint.IgnoredHits >= 1 && s.TargetGroup("node-a")[0].Hardpoint.Armed, "CONTROL (wrong order): node B ignores hits while node A is the target.");
        yield return SolveStage(e, s, d0);
        Check(s.StageIndex == 1 && b.Hardpoint.Armed && !s.TargetGroup("node-c")[0].Hardpoint.Armed, "Node A down: only node B is exposed.");
        Away(e);
        yield return Failed(e, s, 10f, "Timed out: hit node b", 1);
    }
    IEnumerator ConvoyRobbery(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry)); var s = (StagedState)e.Scenario; var d0 = (StagedScenario)e.Definition.Scenario;
        yield return SolveStage(e, s, d0);
        var car = s.TargetGroup("car")[0];
        yield return Until(() => car.Vehicle != null && car.Vehicle.Driving, 2f);
        Check(s.StageIndex == 1 && car.Vehicle.Driving, "The armoured car drives off when the villain closes in.");
        Away(e);
        CombatImpact.Blast(W.Powers, car.Go.GetComponent<Rigidbody>().worldCenterOfMass, 2.5f, 0f, 99999f, .1f);
        yield return Until(() => s.StageIndex >= 2 || Ended(e), 2f);
        Check(car.Go == null && s.StageIndex == 2, "Wrecking the car stops it (the stop stage completes)...");
        yield return SolveStage(e, s, d0);
        yield return Failed(e, s, 3f, "crack the cargo door: objective lost", 3);
    }
    IEnumerator Distraction(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry, d => d.Stages[1].Timeout = 5f)); var s = (StagedState)e.Scenario;
        Log("In-memory clone: cordon timeout 40 s -> 5 s.");
        Ground(Floor(e.Site + Vector3.right * 2f));
        W.AddHeat(-W.Heat); W.AddHeat(.9f);
        yield return new WaitForSeconds(.5f);
        Check(s.StageIndex == 0 && W.Stars == 1, "CONTROL: 1 Heat star is not a scene yet.");
        W.AddHeat(1.9f - W.Heat);
        yield return Until(() => s.StageIndex >= 1, 1f);
        Check(s.StageIndex == 1 && W.Stars >= 2 && s.Group("cordon").Count == 3, $"2 stars: the police take the bait ({s.Group("cordon").Count} cordon officers).");
        yield return Failed(e, s, 10f, "Timed out: slip the cordon", 1);
    }
    IEnumerator Getaway(StagedMissionLibrary.Entry entry)
    {
        var e = Spawn(Def(entry)); var s = (StagedState)e.Scenario; var d0 = (StagedScenario)e.Definition.Scenario; Away(e);
        var crew = s.Group("crew"); var hunters = s.Group("hunters");
        yield return new WaitForSeconds(1.5f);
        Check(crew.All(c => !c.Following && c.Npc.Health == c.Npc.MaxHealth), "CONTROL: the crew waits (not following, unhurt) until the villain reaches them.");
        yield return SolveStage(e, s, d0);
        yield return null;
        Check(s.StageIndex == 1 && crew.Any(c => c.Following), "Reaching the crew: they follow.");
        Away(e); int maxAlive = 0; float until = Time.time + 80f;
        while (!Ended(e) && Time.time < until) { maxAlive = Mathf.Max(maxAlive, hunters.Count(h => h.Npc != null && !h.Npc.Dead)); yield return null; }
        Log($"MEASURED unattended escort: {hunters.Count} hunters spawned, at most {maxAlive} alive (cap {d0.Stages[1].RepeatMaxAlive}).");
        Check(maxAlive <= d0.Stages[1].RepeatMaxAlive, "Hunter reinforcements respect the cap.");
        yield return Failed(e, s, 1f, "get the crew to the van: 2 lost", 1);
    }
}
#endif
