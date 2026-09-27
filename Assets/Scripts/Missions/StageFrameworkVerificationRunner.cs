#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// See StageFrameworkVerification. The stage layer itself, with IN-MEMORY StagedScenario definitions spawned through the real
/// encounter path in a real Hero session: positive completion, stage-rule failure, timeout, wrong-order controls (a hardpoint,
/// a pickup and an R-hold panel are inert until their own stage), every transition exactly once, no success after a failure
/// (including failure and success becoming true in the same tick) and a single reward per mission.
public sealed class StageFrameworkVerificationRunner : StagedMissionRunner
{
    protected override string Folder => "Verification/StageFramework/";
    protected override string ResultFile => "results.txt";
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Session("ice", "strength", "hero");
        yield return Positive();
        yield return Failure();
        yield return Timeout();
        yield return HoldGate();
        // A fresh session (the hero mode ends at 5 successes): bonus goals first, then variation with 2 successes banked.
        yield return Session("ice", "strength", "hero");
        yield return Bonuses();
        yield return Variation();
        // World-depth stage features (fresh sessions keep each under the mode's success / failure limits).
        yield return Session("ice", "strength", "hero");
        yield return MultiPointGroups(); yield return Search(); yield return Defend(); yield return CrossDistrict();
        yield return Session("fire", "ice", "villain");
        yield return LosePursuit(); yield return CriticalEscort();
        Log("LIMIT: hero moved by teleport; blasts through CombatImpact.Blast (the punch / Fire / synergy path) rather than input. NPCs, NavMesh and physics are live.");
    }
    // ---------------------------------------------------------------- positive path, wrong order, exactly once, one reward
    IEnumerator Positive()
    {
        Log("---- POSITIVE: survive -> reach -> collect -> destroy");
        var scenario = Staged(
            new MissionStageSpec { Kind = StageKind.Survive, Label = "HOLD ON", Seconds = .6f },
            StageOf(StageKind.ReachArea, "GET TO THE MARK", point: "mark"),
            StageOf(StageKind.CollectItems, "GRAB THE CACHE", "cache"),
            StageOf(StageKind.DestroyTargets, "BREAK THE DOOR", "door"));
        scenario.Stages[1].Radius = 3f;
        scenario.Points = new[] { new MissionPoint { Id = "mark", Angle = 90, Distance = 12 }, new MissionPoint { Id = "stash", Angle = 270, Distance = 12 } };
        scenario.Targets = new[] { new TargetGroupSpec { Id = "door", Kind = TargetKind.Hardpoint, Health = 60 }, new TargetGroupSpec { Id = "cache", Kind = TargetKind.Pickup, AtPoint = "stash" } };
        var def = Definition("Verification staged positive", scenario);
        int successes = W.Mode.Successes, xp = W.Mode.XpEarned;
        var e = Spawn(def); var s = (StagedState)e.Scenario; Away(e);
        int startedEvents = 0, completedEvents = 0; s.StageStarted += _ => startedEvents++; s.StageCompleted += _ => completedEvents++;
        var door = s.TargetGroup("door")[0]; var cache = s.TargetGroup("cache")[0];
        Check(s.StageIndex == 0 && s.Terminal == MissionTerminal.Running && Line(e) == "HOLD ON 0/1", $"Stage 0 active, objective line \"{Line(e)}\".");
        // WRONG ORDER 1: the door is only armed during its own stage.
        Vector3 doorAt = door.Go.transform.position;
        CombatImpact.Blast(W.Powers, doorAt + Vector3.right * 1.2f, 2.5f, 400f, 500f, .1f);
        Check(!door.Hardpoint.Armed && door.Hardpoint.IgnoredHits >= 1 && door.Hardpoint.Health == door.Hardpoint.MaxHealth && s.StageIndex == 0,
            $"CONTROL (wrong order): a 500-damage blast on the door during stage 0 is ignored (ignored hits {door.Hardpoint.IgnoredHits}, health {door.Hardpoint.Health}/{door.Hardpoint.MaxHealth}).");
        yield return Until(() => s.StageIndex >= 1, 3f);
        Check(s.StageIndex == 1 && s.Started[0] == 1 && s.Completed[0] == 1, $"Survive stage completes after {scenario.Stages[0].Seconds} s: stage 1 active ({Counts(s)}).");
        Check(Line(e).StartsWith("GET TO THE MARK ") && Line(e).EndsWith(" M"), $"Reach stage shows a distance line: \"{Line(e)}\".");
        yield return new WaitForSeconds(.5f);
        Check(s.StageIndex == 1, "CONTROL: 150 m above the site the reach stage does not complete.");
        // WRONG ORDER 2: standing on the cache before its collect stage does not pick it up.
        Ground(cache.Pickup.Visual.transform.position - Vector3.up * e.Definition.MarkerHeight);
        yield return new WaitForSeconds(.4f);
        Check(!cache.Done && cache.Pickup.Visual.activeSelf && s.StageIndex == 1, "CONTROL (wrong order): standing on the cache during the reach stage does not collect it.");
        Ground(s.Point("mark"));
        yield return Until(() => s.StageIndex >= 2, 2f);
        Check(s.StageIndex == 2 && s.Completed[1] == 1, $"Reaching the mark completes stage 1 ({Counts(s)}).");
        Ground(cache.Pickup.Visual.transform.position - Vector3.up * e.Definition.MarkerHeight);
        yield return Until(() => s.StageIndex >= 3, 2f);
        Check(s.StageIndex == 3 && cache.Done && !cache.Pickup.Visual.activeSelf && Line(e) == "BREAK THE DOOR 0/1", $"Collecting the cache completes stage 2; line \"{Line(e)}\".");
        Check(door.Hardpoint.Armed, "The door is armed now that its stage is active.");
        Away(e);
        CombatImpact.Blast(W.Powers, doorAt + Vector3.right * 1.2f, 2.5f, 400f, 30f, .1f);
        Check(door.Hardpoint.Hits == 1 && !door.Hardpoint.Destroyed && door.Hardpoint.Health <= 30.01f && !Ended(e), $"A 30-damage blast chips the armed door ({door.Hardpoint.Health}/{door.Hardpoint.MaxHealth}); mission still running.");
        CombatImpact.Blast(W.Powers, doorAt + Vector3.right * 1.2f, 2.5f, 400f, 100f, .1f);
        Check(door.Hardpoint.Destroyed, "A second blast destroys the door.");
        yield return Outcome(e, 2f);
        Check(Ended(e) && Result(e).Success && s.Terminal == MissionTerminal.Complete, $"Mission outcome: {Describe(e)}.");
        Check(ExactlyOnce(s.Started, 4) && ExactlyOnce(s.Completed, 4) && startedEvents == 3 && completedEvents == 4,
            $"Every stage started and completed exactly once ({Counts(s)}; events after subscribe: {startedEvents} started, {completedEvents} completed).");
        Check(!e.TryComplete(), "CONTROL: TryComplete after the end does nothing.");
        yield return new WaitForSeconds(1f);
        Check(OutcomeCount(e) == 1 && W.Mode.Successes - successes == 1, $"Exactly one outcome and one success recorded (outcomes {OutcomeCount(e)}, successes +{W.Mode.Successes - successes}).");
        Log($"MEASURED reward: outcome XP {Result(e).Xp}, session XpEarned +{W.Mode.XpEarned - xp} (mode SuccessXp {W.Mode.Definition.SuccessXp}; kills elsewhere would add to the session figure).");
        Check(Result(e).Xp == Mathf.Max(0, W.Mode.Definition.SuccessXp) && W.Mode.XpEarned - xp >= Result(e).Xp && W.Mode.XpEarned - xp < 2 * Mathf.Max(1, Result(e).Xp),
            "The mission reward was paid once (not twice).");
    }
    // ---------------------------------------------------------------- failure latch
    IEnumerator Failure()
    {
        Log("---- FAILURE: protect stage, and failure beating a simultaneous success");
        MissionStageSpec[] Stages() => new[]
        {
            new MissionStageSpec { Kind = StageKind.ProtectActors, Label = "KEEP THEM ALIVE", Group = "vip", Seconds = 0f, AllowedLosses = 0 },
            new MissionStageSpec { Kind = StageKind.Survive, Label = "WAIT", Seconds = .3f },
        };
        ActorGroupSpec[] Vips() => new[] { new ActorGroupSpec { Id = "vip", Role = NpcRole.Civilian, Count = 2, Behavior = ActorBehavior.Idle } };
        // CONTROL: nobody lost -> the zero-second protect stage passes and the mission succeeds.
        var control = Staged(Stages()); control.Actors = Vips();
        var e = Spawn(Definition("Verification staged protect control", control)); var s = (StagedState)e.Scenario; Away(e);
        yield return Outcome(e, 3f);
        Check(Ended(e) && Result(e).Success && ExactlyOnce(s.Completed, 2), $"CONTROL: nobody lost -> {Describe(e)} ({Counts(s)}).");
        // Lose one before the first tick: on that tick the success condition (0 s elapsed >= 0 s) AND the loss rule are both true.
        int successes = W.Mode.Successes, failures = W.Mode.Failures;
        var failing = Staged(Stages()); failing.Actors = Vips();
        e = Spawn(Definition("Verification staged protect", failing)); s = (StagedState)e.Scenario; Away(e);
        var vip = s.Group("vip")[0].Npc; vip.Damage(99999f, null);
        Check(vip.Dead && s.StageIndex == 0 && s.Terminal == MissionTerminal.Running, "One VIP killed before the stage's first tick.");
        yield return Outcome(e, 3f);
        Check(Ended(e) && !Result(e).Success && Result(e).Reason.Contains("keep them alive") && Result(e).Reason.Contains("lost"), $"Loss beats the simultaneous success: {Describe(e)}.");
        Check(s.Terminal == MissionTerminal.Failed && !s.Complete() && s.Completed[0] == 0 && s.Started[1] == 0, $"Latched FAILED: no stage completed, the next stage never started ({Counts(s)}).");
        s.Tick(5f);   // a stray tick after the latch (the state is being destroyed with the encounter)
        Check(s.Terminal == MissionTerminal.Failed && !s.Complete() && s.Completed[0] == 0, "CONTROL: a tick after the failure changes nothing (no success after failure).");
        yield return new WaitForSeconds(1f);
        Check(OutcomeCount(e) == 1 && W.Mode.Failures - failures == 1 && W.Mode.Successes == successes, $"One FAILED outcome, no success recorded (failures +{W.Mode.Failures - failures}, successes +{W.Mode.Successes - successes}).");
    }
    // ---------------------------------------------------------------- timeout
    IEnumerator Timeout()
    {
        Log("---- TIMEOUT");
        var scenario = Staged(new MissionStageSpec { Kind = StageKind.ReachArea, Label = "GET THERE", Point = "far", Radius = 3f, Timeout = 1f });
        scenario.Points = new[] { new MissionPoint { Id = "far", Angle = 0, Distance = 30 } };
        var e = Spawn(Definition("Verification staged timeout", scenario)); var s = (StagedState)e.Scenario; Away(e);
        yield return new WaitForSeconds(.5f);
        Check(!Ended(e) && s.StageElapsed < 1f, $"CONTROL: at {s.StageElapsed:F2} s (timeout 1 s) the mission is still running.");
        yield return Outcome(e, 3f);
        Check(Ended(e) && !Result(e).Success && Result(e).Reason == "Timed out: get there" && s.Completed[0] == 0, $"Stage timeout fails the mission: {Describe(e)}.");
        Check(e.Definition.Deadline > 100f, $"The failure came from the stage timeout, not the encounter deadline ({e.Definition.Deadline} s).");
    }
    // ---------------------------------------------------------------- bonus goals
    IEnumerator Bonuses()
    {
        Log("---- BONUS GOALS (paid once, on success only)");
        StagedScenario Mission()
        {
            var sc = Staged(new MissionStageSpec { Kind = StageKind.Survive, Label = "WAIT", Seconds = .6f });
            sc.Actors = new[] { new ActorGroupSpec { Id = "vips", Role = NpcRole.Civilian, Count = 2, Behavior = ActorBehavior.Idle } };
            sc.Bonuses = new[]
            {
                new BonusObjective { Label = "FAST", Kind = BonusKind.UnderSeconds, Value = 5f, RewardXp = 20 },
                new BonusObjective { Label = "UNTOUCHED", Kind = BonusKind.NoDamageTaken, RewardXp = 30 },
                new BonusObjective { Label = "QUIET", Kind = BonusKind.MaxHeatStars, Value = 5f, RewardXp = 10 },
                new BonusObjective { Label = "NOBODY LOST", Kind = BonusKind.NoLosses, Group = "vips", RewardXp = 15 },
            };
            return sc;
        }
        var grants = new List<XpGrant>(); W.Progression.XpGranted += grants.Add;
        var clean = Spawn(Definition("Verification staged bonus clean", Mission())); Away(clean);
        yield return Outcome(clean, 3f);
        var cs = (StagedState)clean.Scenario; int cleanBonus = grants.Where(g => g.Reason == "bonus").Sum(g => g.Amount);
        Check(Ended(clean) && Result(clean).Success && cleanBonus == 75 && cs.BonusXp == 75 && cs.BonusesEarned.Count == 4 && grants.Count(g => g.Reason == "bonus") == 1,
            $"Clean run: all four goals met ({string.Join(", ", cs.BonusesEarned)}), one bonus grant of {cleanBonus} XP.");
        grants.Clear();
        var messy = Spawn(Definition("Verification staged bonus messy", Mission())); Away(messy); var ms = (StagedState)messy.Scenario;
        ms.Group("vips")[0].Npc.Damage(99999f, null); yield return null;
        W.DamagePlayer(1f, true);
        yield return Outcome(messy, 3f);
        int messyBonus = grants.Where(g => g.Reason == "bonus").Sum(g => g.Amount);
        Check(Ended(messy) && Result(messy).Success && messyBonus == 30 && ms.DamageTaken >= 1f && ms.BonusesEarned.SequenceEqual(new[] { "FAST", "QUIET" }),
            $"CONTROL: a lost VIP and 1 damage taken forfeit those two goals; FAST + QUIET still pay {messyBonus} XP.");
        grants.Clear();
        var failing = Staged(new MissionStageSpec { Kind = StageKind.ReachArea, Label = "NEVER", Point = "far", Radius = 1f, Timeout = .5f });
        failing.Points = new[] { new MissionPoint { Id = "far", Distance = 40 } }; failing.Bonuses = Mission().Bonuses;
        var lost = Spawn(Definition("Verification staged bonus failed", failing)); Away(lost);
        yield return Outcome(lost, 3f);
        Check(Ended(lost) && !Result(lost).Success && !grants.Any(g => g.Reason == "bonus") && ((StagedState)lost.Scenario).BonusesEarned.Count == 0, "CONTROL: a FAILED mission pays no bonus even though FAST / UNTOUCHED / QUIET would hold.");
        W.Progression.XpGranted -= grants.Add;
    }
    // ---------------------------------------------------------------- per-spawn variation
    IEnumerator Variation()
    {
        Log("---- VARIATION (seeded layout, difficulty band)");
        StagedScenario Layout(bool vary)
        {
            var sc = Staged(new MissionStageSpec { Kind = StageKind.Survive, Label = "WAIT", Seconds = .4f, Timeout = 30f });
            sc.Points = new[] { new MissionPoint { Id = "p", Angle = 30, Distance = 12, Sidewalk = false }, new MissionPoint { Id = "q", Angle = 120, Distance = 8, Sidewalk = false } };
            sc.Actors = new[] { new ActorGroupSpec { Id = "thugs", Count = 2, Behavior = ActorBehavior.Idle }, new ActorGroupSpec { Id = "vips", Role = NpcRole.Civilian, Count = 2, Behavior = ActorBehavior.Idle, ScaleWithDifficulty = false } };
            sc.RandomYaw = sc.RandomMirror = vary; sc.DifficultyStep = 1; sc.ExtraActorsPerBand = vary ? 1f : 0f; sc.MaxExtraActors = 2; sc.TimeoutScalePerBand = vary ? .9f : 1f; sc.MinTimeoutScale = .75f;
            return sc;
        }
        Vector3 Expected(CrimeEncounter e, float angle, float distance, float yaw, bool mirror) => e.Site + Quaternion.Euler(0, (mirror ? -angle : angle) + yaw, 0) * Vector3.forward * distance;
        // CONTROL: variation off -> the authored layout and counts.
        var plain = Spawn(Definition("Verification staged plain layout", Layout(false)), 7); var ps = (StagedState)plain.Scenario;
        Check(ps.Yaw == 0f && !ps.Mirrored && ps.ExtraActors == 0 && ps.TimeoutScale == 1f && Vector3.Distance(ps.Point("p"), Expected(plain, 30, 12, 0, false)) < .01f && ps.Group("thugs").Count == 2,
            "CONTROL: variation off keeps the authored points, counts and timers.");
        // Seeded variation: the same seed gives the same yaw / mirror (System.Random order: yaw, then mirror).
        var r = new System.Random(7); float yaw = (float)(r.NextDouble() * 360d); bool mirror = r.Next(2) == 1;
        int successes = W.Mode.Successes; int band = successes; int extra = Mathf.Clamp(band, 0, 2); float scale = Mathf.Clamp(Mathf.Pow(.9f, band), .75f, 1f);
        var varied = Spawn(Definition("Verification staged varied layout", Layout(true)), 7); var vs = (StagedState)varied.Scenario;
        Check(Mathf.Abs(vs.Yaw - yaw) < .01f && vs.Mirrored == mirror && Vector3.Distance(vs.Point("p"), Expected(varied, 30, 12, yaw, mirror)) < .01f && Vector3.Distance(vs.Point("q"), Expected(varied, 120, 8, yaw, mirror)) < .01f,
            $"Seed 7: yaw {vs.Yaw:F1}, mirrored {vs.Mirrored}; both points rotated / mirrored about the site with distances kept.");
        Check(vs.Band == band && vs.ExtraActors == extra && vs.Group("thugs").Count == 2 + extra && vs.Group("vips").Count == 2 && Mathf.Abs(vs.TimeoutScale - scale) < 1e-4f && Mathf.Abs(vs.TimeoutOf(vs.Stage) - 30f * scale) < 1e-3f,
            $"Band {band} ({successes} successes / step 1): +{extra} thugs (cap 2), protected civilians unscaled, stage timeout {vs.TimeoutOf(vs.Stage):F1} s (x{scale:F2}).");
        yield return Outcome(plain, 3f); yield return Outcome(varied, 3f);
        Check(Ended(plain) && Ended(varied) && Result(plain).Success && Result(varied).Success, "Both variation probes complete normally.");
    }
    // ---------------------------------------------------------------- world-depth stage features
    static MissionPoint Pt(string id, float angle, float distance) => new MissionPoint { Id = id, Angle = angle, Distance = distance, Sidewalk = false };
    IEnumerator MultiPointGroups()
    {
        Log("---- MULTI-POINT GROUPS AND MULTI-GROUP STAGES");
        var sc = Staged(new MissionStageSpec { Kind = StageKind.ProtectActors, Label = "GUARD BOTH", Group = "left,right", Seconds = 30f, AllowedLosses = 1 });
        sc.Points = new[] { Pt("a", 90, 10), Pt("b", 270, 10) };
        sc.Actors = new[] { new ActorGroupSpec { Id = "left", Role = NpcRole.Civilian, Count = 2, AtPoint = "a", Ring = 1f, Behavior = ActorBehavior.Idle }, new ActorGroupSpec { Id = "right", Role = NpcRole.Civilian, Count = 2, AtPoint = "b", Ring = 1f, Behavior = ActorBehavior.Idle } };
        sc.Targets = new[] { new TargetGroupSpec { Id = "posts", Kind = TargetKind.Hardpoint, Count = 2, AtPoint = "a,b" } };
        var e = Spawn(Definition("Verification staged multi-group", sc)); var st = (StagedState)e.Scenario; Away(e);
        var posts = st.TargetGroup("posts");
        Check(Vector3.Distance(posts[0].Go.transform.position, st.Point("a")) < 1.5f && Vector3.Distance(posts[1].Go.transform.position, st.Point("b")) < 1.5f, "A target group with AtPoint \"a,b\" puts one member at each point.");
        Check(st.Group("left").All(x => Vector3.Distance(x.Npc.transform.position, st.Point("a")) < 3f) && st.Group("left,right").Count == 4, "Actor groups spawn at their own points; the comma key \"left,right\" reads all four members.");
        st.Group("left")[0].Npc.Damage(99999f, null); yield return new WaitForSeconds(.3f);
        Check(!Ended(e), "CONTROL: one loss across the two groups (allowed 1) keeps the stage running.");
        st.Group("right")[0].Npc.Damage(99999f, null);
        yield return Outcome(e, 2f);
        Check(Ended(e) && !Result(e).Success && Result(e).Reason.Contains("2 lost"), $"A second loss in the OTHER group fails the combined stage: {Describe(e)}.");
    }
    IEnumerator Search()
    {
        Log("---- SEARCH AMONG DECOYS");
        StagedScenario Mission()
        {
            var sc = Staged(new MissionStageSpec { Kind = StageKind.SearchTargets, Label = "FIND THE STASH", Group = "houses", OnDecoy = new[] { new StageAction { Kind = StageActionKind.SpawnActors, Group = "defenders", Amount = 1, Text = "$here" } } });
            sc.Points = new[] { Pt("a", 0, 12), Pt("b", 90, 12), Pt("c", 180, 12), Pt("d", 270, 12) };
            sc.Targets = new[] { new TargetGroupSpec { Id = "houses", Kind = TargetKind.Hardpoint, Count = 4, AtPoint = "a,b,c,d", Health = 999 } };
            sc.Actors = new[] { new ActorGroupSpec { Id = "defenders", Count = 1, Ring = 2f, Behavior = ActorBehavior.Idle, SpawnAtStart = false } };
            return sc;
        }
        var e = Spawn(Definition("Verification staged search", Mission()), 77); var st = (StagedState)e.Scenario;
        var houses = st.TargetGroup("houses"); int real = houses.FindIndex(h => h.Real);
        var probe = Spawn(Definition("Verification staged search seed probe", Mission()), 77); int probeReal = ((StagedState)probe.Scenario).TargetGroup("houses").FindIndex(h => h.Real);
        Check(houses.Count(h => h.Real) == 1 && real == probeReal, $"Exactly one real target, the same one for the same seed (index {real}).");
        var decoy = houses.First(h => !h.Real);
        Ground(Floor(decoy.Go.transform.position + Vector3.right * 1.8f)); e.ScriptedHold = true;
        yield return Until(() => decoy.Done, 4f); e.ScriptedHold = false; yield return null;
        Check(decoy.Done && st.DecoysRevealed == 1 && st.StageIndex == 0 && st.Group("defenders").Count == 1 && Vector3.Distance(st.Group("defenders")[0].Npc.transform.position, decoy.Go.transform.position) < 5f,
            "A decoy reveals itself (stage continues) and OnDecoy spawns a defender AT that decoy ($here).");
        var target = houses[real];
        Ground(Floor(target.Go.transform.position + Vector3.right * 1.8f)); e.ScriptedHold = true;
        yield return Outcome(e, 5f); e.ScriptedHold = false;
        Check(Ended(e) && Result(e).Success && target.Done, $"Holding R at the real target completes the search: {Describe(e)}.");
        Away(probe); probe.ScriptedHold = false;
        yield return new WaitForSeconds(.1f);
    }
    IEnumerator Defend()
    {
        Log("---- DEFEND BESIEGED TARGETS");
        StagedScenario Mission(float seconds)
        {
            var sc = Staged(new MissionStageSpec { Kind = StageKind.DefendTargets, Label = "SAVE THE HOUSES", Group = "houses", Seconds = seconds, AllowedLosses = 0 });
            sc.Points = new[] { Pt("h", 0, 8) };
            sc.Targets = new[] { new TargetGroupSpec { Id = "houses", Kind = TargetKind.Hardpoint, Count = 1, AtPoint = "h", Health = 20f, Besiegeable = true } };
            sc.Actors = new[] { new ActorGroupSpec { Id = "raiders", Count = 1, AtPoint = "h", Ring = 1.5f, Behavior = ActorBehavior.Harass, BehaviorArgument = "houses" } };
            return sc;
        }
        var e = Spawn(Definition("Verification staged defend", Mission(60f))); var st = (StagedState)e.Scenario; Away(e);
        var house = st.TargetGroup("houses")[0];
        yield return Outcome(e, 15f);
        Check(Ended(e) && !Result(e).Success && house.Hardpoint.Destroyed && house.Hardpoint.SiegeDamage > 0f && house.Hardpoint.Hits == 0, $"An unattended raider besieges the house to destruction (siege damage {house.Hardpoint.SiegeDamage:F1}, player hits 0): {Describe(e)}.");
        var c = Spawn(Definition("Verification staged defend control", Mission(2f))); var cs = (StagedState)c.Scenario; Away(c);
        cs.Group("raiders")[0].Npc.Damage(99999f, W.Powers);
        yield return Outcome(c, 5f);
        Check(Ended(c) && Result(c).Success && !cs.TargetGroup("houses")[0].Hardpoint.Destroyed, "CONTROL: raider down -> the house survives the 2 s and the stage completes.");
    }
    IEnumerator CrossDistrict()
    {
        Log("---- CROSS-DISTRICT DESTINATIONS");
        StagedScenario Mission()
        {
            var sc = Staged(StageOf(StageKind.ReachArea, "GO TO B", point: "b"), StageOf(StageKind.ReachArea, "GO TO C", point: "c"));
            sc.Stages[0].Radius = sc.Stages[1].Radius = 4f; sc.Stages[0].Timeout = sc.Stages[1].Timeout = 120f;
            sc.Points = new[]
            {
                new MissionPoint { Id = "b", Placement = PointPlacement.OtherDistrict },
                new MissionPoint { Id = "c", Placement = PointPlacement.OtherDistrict, NotInDistrictsOf = "b", DistrictTag = "no-such-tag" },
            };
            return sc;
        }
        var e = Spawn(Definition("Verification staged cross-district", Mission()), 4242); var st = (StagedState)e.Scenario;
        int home = W.City.DistrictAt(e.Site), db = st.DistrictOfPoint("b"), dc = st.DistrictOfPoint("c");
        var probe = Spawn(Definition("Verification staged cross-district probe", Mission()), 4242); var ps = (StagedState)probe.Scenario;
        Check(home != db && home != dc && db != dc && db >= 0 && dc >= 0, $"Site district {home} ({W.Districts.NameOf(home)}), B in {db} ({W.Districts.NameOf(db)}), C in {dc} ({W.Districts.NameOf(dc)}): three distinct districts.");
        Check(ps.Point("b") == st.Point("b") && ps.Point("c") == st.Point("c"), "The same seed picks the same destinations.");
        Check(st.PointFallbacks.Any(f => f.StartsWith("c:")), $"An unknown DistrictTag is relaxed and recorded ({string.Join("; ", st.PointFallbacks)}), not a failure.");
        var targets = new List<Vector3>(); var step = W.Mode.Definition.Rules.Current(e);
        ModeRules.Targets(e, step.Task, targets);
        Check(targets.Count == 1 && targets[0] == st.Point("b"), $"The HUD objective API points at B ({step.Text}).");
        st.Relocate("c", st.Point("c") + Vector3.down * 500f);
        Ground(Floor(st.Point("b")));
        yield return Until(() => st.StageIndex >= 1, 3f);
        ModeRules.Targets(e, W.Mode.Definition.Rules.Current(e).Task, targets);
        Check(st.StageIndex == 1 && st.DestinationRepairs == 1 && targets.Count == 1 && targets[0] == st.Point("c") && UnityEngine.AI.NavMesh.SamplePosition(st.Point("c"), out _, 6f, UnityEngine.AI.NavMesh.AllAreas),
            $"Reaching B moves the objective to C; C had been made unreachable and was repaired at stage start (repairs {st.DestinationRepairs}), so the HUD points at a reachable C.");
        Ground(Floor(st.Point("c")));
        yield return Outcome(e, 3f);
        Check(Ended(e) && Result(e).Success, $"Reaching the repaired C completes the mission: {Describe(e)}.");
        Away(probe);
    }
    IEnumerator LosePursuit()
    {
        Log("---- LOSE PURSUIT vs ESCAPE RADIUS (villain)");
        Isolate(); W.AddHeat(1f);
        var lose = Staged(new MissionStageSpec { Kind = StageKind.LosePursuit, Label = "LOSE THEM", Seconds = .5f, Timeout = 120f });
        var escape = Staged(new MissionStageSpec { Kind = StageKind.EscapeRadius, Label = "GET 25 M AWAY", Radius = 25f, Timeout = 120f });
        var e = Spawn(Definition("Verification staged lose pursuit", lose)); var st = (StagedState)e.Scenario;
        var x = Spawn(Definition("Verification staged escape radius", escape)); var xs = (StagedState)x.Scenario;
        Vector3 floor = new Vector3(0, 150.05f, 0); PlaceHero(floor);
        var cop = Actor(floor + new Vector3(0, 0, 8), NpcRole.Cop, 1000f); yield return null; W.Pursuit.Sample();
        Check(W.Pursuit.State == PursuitState.Pursued, "A hostile cop in view: Pursued.");
        PlaceHero(floor + new Vector3(0, 0, -22)); cop.transform.position = floor + new Vector3(0, 0, 8); Physics.SyncTransforms();
        float until = Time.time + 4f; while (Time.time < until) { W.Pursuit.Sample(); yield return null; }
        Check(!Ended(e) && W.Pursuit.State == PursuitState.Pursued, "CONTROL: 30 m from the cop while it still engages -> the LOSE PURSUIT stage does NOT complete.");
        Log($"Escape-radius stage meanwhile: hero {Vector3.Distance(W.Hero.transform.position, x.Site):F0} m from its site (a literal distance rule, kept for missions that want it).");
        cop.gameObject.SetActive(false); Away(e);
        yield return Outcome(e, W.Pursuit.Settings.LoseContactSeconds + W.Pursuit.Settings.SearchSeconds + 4f);
        Check(Ended(e) && Result(e).Success && st.Terminal == MissionTerminal.Complete, $"Out of sight and far away, the pursuit clears ({W.Pursuit.State}) and the stage completes: {Describe(e)}.");
        yield return Outcome(x, 3f);
        Check(Ended(x) && Result(x).Success, "The EscapeRadius stage completes on distance alone (it stays available).");
    }
    IEnumerator CriticalEscort()
    {
        Log("---- CRITICAL ESCORT");
        var sc = Staged(new MissionStageSpec { Kind = StageKind.EscortActors, Label = "GET THE WITNESS OUT", Group = "witness", Point = "safe", Radius = 4f, AllowedLosses = 0, Timeout = 120f });
        sc.Points = new[] { new MissionPoint { Id = "safe", Placement = PointPlacement.OtherDistrict } };
        sc.Actors = new[] { new ActorGroupSpec { Id = "witness", Role = NpcRole.Civilian, Count = 1, Behavior = ActorBehavior.Follow, ScaleWithDifficulty = false } };
        var e = Spawn(Definition("Verification staged critical escort", sc)); var st = (StagedState)e.Scenario; Away(e);
        Check(st.DistrictOfPoint("safe") != W.City.DistrictAt(e.Site), "The escort destination is in another district.");
        st.Group("witness")[0].Npc.Damage(99999f, null);
        yield return Outcome(e, 2f);
        Check(Ended(e) && !Result(e).Success && Result(e).Reason.Contains("1 lost"), $"Losing the critical escorted actor fails the mission at once: {Describe(e)}.");
    }
    // ---------------------------------------------------------------- R-hold gating
    IEnumerator HoldGate()
    {
        Log("---- HOLD GATE: an interact target is inert until its stage");
        var scenario = Staged(new MissionStageSpec { Kind = StageKind.Survive, Label = "WAIT FOR THE SIGNAL", Seconds = 1.2f }, StageOf(StageKind.InteractTargets, "HACK THE PANEL", "panel"));
        scenario.Targets = new[] { new TargetGroupSpec { Id = "panel", Kind = TargetKind.Hardpoint } };
        var e = Spawn(Definition("Verification staged hold gate", scenario)); var s = (StagedState)e.Scenario;
        var panel = s.TargetGroup("panel")[0]; Ground(panel.Go.transform.position + Vector3.forward * 1.6f - Vector3.up * .8f);
        e.ScriptedHold = true;
        yield return new WaitForSeconds(.9f);
        Check(s.StageIndex == 0 && !panel.Done && panel.Hold == 0f && !e.InteractableNear(W.Hero.transform.position), "CONTROL (wrong order): holding R at the panel during stage 0 does nothing.");
        yield return Until(() => s.StageIndex >= 1, 2f);
        Check(s.StageIndex == 1 && e.InteractableNear(W.Hero.transform.position), "Stage 1 active: the panel is now interactable.");
        yield return Outcome(e, scenario.HoldSeconds + 2f);
        e.ScriptedHold = false;
        Check(Ended(e) && Result(e).Success && panel.Done && ExactlyOnce(s.Completed, 2), $"Holding R through the hack stage completes it: {Describe(e)} ({Counts(s)}).");
    }
}
#endif
