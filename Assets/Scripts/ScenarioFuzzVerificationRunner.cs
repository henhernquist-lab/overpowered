#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// See ScenarioFuzzVerification. Seeded random play against the staged missions: each seed picks a side, a loadout and a
/// mission, then performs a fixed-length sequence of random actions (teleports, power uses, R holds, kills, blasts, waits,
/// pauses, Heat, freezes). The ACTION SEQUENCE is fully determined by the seed (System.Random); physics / NavMesh timing is
/// not, so a replay reproduces the same inputs, not necessarily the same frames. After every action the invariants hold:
/// no gameplay exception, stages start / complete at most once and in order, nothing transitions after the terminal latch,
/// at most one outcome, finite positions, non-negative score and health. Seeds and action logs: Verification/Fuzz/.
public sealed class ScenarioFuzzVerificationRunner : StagedMissionRunner
{
    /// Empty = the default seed list; otherwise only these seeds (replay a failure: ScenarioFuzzVerification.Replay).
    public int[] Seeds;
    public int Actions = 40;
    protected override string Folder => "Verification/Fuzz/";
    protected override string ResultFile => "results.txt";
    static readonly int[] DefaultSeeds = { 101, 202, 303, 404, 505, 606, 707, 808, 909, 1111, 1212, 1313 };
    static readonly string[] Loadouts = { "ice+strength", "fire+lightning", "poison+speed", "darkness+laser-eyes", "telekinesis+force-field", "flight+ice", "lightning+poison", "speed+darkness" };
    readonly List<string> actionLog = new List<string>();
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var seeds = Seeds != null && Seeds.Length > 0 ? Seeds : DefaultSeeds;
        Log($"Seeds: {string.Join(", ", seeds)}; {Actions} actions each.");
        foreach (int seed in seeds) yield return Fuzz(seed);
        File.WriteAllLines(Folder + "actions.txt", actionLog);
        Log("Replay one seed: Unity -batchmode -projectPath <copy> -executeMethod ScenarioFuzzVerification.Replay -fuzzSeed <seed>");
    }
    IEnumerator Fuzz(int seed)
    {
        var random = new System.Random(seed);
        var side = random.Next(2) == 0 ? PlayerSide.Hero : PlayerSide.Villain;
        var pool = StagedMissionLibrary.All.Where(e => e.Side == side).ToArray(); var entry = pool[random.Next(pool.Length)];
        var loadout = Loadouts[random.Next(Loadouts.Length)].Split('+');
        Log($"---- SEED {seed}: {side}, {entry.Asset}, {loadout[0]} + {loadout[1]}");
        actionLog.Add($"# seed {seed}: {side} {entry.Asset} {loadout[0]}+{loadout[1]}");
        yield return Session(loadout[0], loadout[1], side == PlayerSide.Hero ? "hero" : "villain");
        Immortal = random.Next(4) != 0;   // one seed in four lets the player die
        var def = Resources.Load<EncounterDefinition>("Encounters/" + entry.Asset);
        Check(def != null, "Mission asset " + entry.Asset);
        var e = Spawn(def); var s = (StagedState)e.Scenario;
        var order = new List<string>(); bool terminalSeen = false; string violation = null;
        s.StageStarted += i => { order.Add("S" + i); if (terminalSeen) violation = "stage started after the terminal latch"; };
        s.StageCompleted += i => { order.Add("C" + i); if (terminalSeen) violation = "stage completed after the terminal latch"; };
        order.Add("S0");   // stage 0 started inside Setup, before subscription
        MissionTerminal lastTerminal = MissionTerminal.Running; int stageHigh = 0;
        for (int step = 0; step < Actions && !Ended(e) && W.Mode != null && !W.Mode.Ended; step++)
        {
            string action = null;
            yield return Act(random, e, s, a => action = a);
            actionLog.Add($"{seed}:{step} {action}");
            if (s.Terminal != MissionTerminal.Running) terminalSeen = true;
            if (lastTerminal != MissionTerminal.Running && s.Terminal != lastTerminal) violation = $"terminal changed {lastTerminal} -> {s.Terminal}";
            lastTerminal = s.Terminal;
            if (s.StageIndex < stageHigh) violation = $"stage index went back {stageHigh} -> {s.StageIndex}";
            stageHigh = Mathf.Max(stageHigh, s.StageIndex);
            if (s.Started.Any(c => c > 1) || s.Completed.Any(c => c > 1)) violation = "a stage started or completed twice: " + Counts(s);
            if (OutcomeCount(e) > 1) violation = "more than one outcome";
            if (!Finite(W.Hero.transform.position) || W.Npcs.Any(n => n != null && !Finite(n.transform.position))) violation = "non-finite position";
            if (W.Mode.Score < 0 || W.Health < 0f) violation = "negative score or health";
            if (violation != null) break;
        }
        Check(violation == null, $"Seed {seed}: invariants held over {actionLog.Count(l => l.StartsWith(seed + ":"))} actions ({violation ?? "ok"}); stage events {string.Join(" ", order)}; {Counts(s)}; terminal {s.Terminal}; outcome {Describe(e)}.");
        Check(Ordered(order), $"Seed {seed}: stage events strictly ordered (each S(i) before C(i), C(i) before S(i+1)).");
        Immortal = false;
    }
    static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    static bool Ordered(List<string> order)
    {
        int expectStart = 0, expectComplete = 0;
        foreach (var o in order)
        {
            int i = int.Parse(o.Substring(1));
            if (o[0] == 'S') { if (i != expectStart || expectComplete != i) return false; expectStart++; }
            else { if (i != expectComplete || expectStart != i + 1) return false; expectComplete++; }
        }
        return true;
    }
    IEnumerator Act(System.Random random, CrimeEncounter e, StagedState s, Action<string> said)
    {
        var actors = s.Actors.Values.SelectMany(g => g).Where(a => a.Npc != null && !a.Npc.Dead && a.Npc.gameObject.activeInHierarchy).ToList();
        var targets = s.TargetsById.Values.SelectMany(g => g).Where(t => t.Go != null || (t.Pickup != null && t.Pickup.Visual != null)).ToList();
        int kind = random.Next(12);
        switch (kind)
        {
            case 0:
                if (actors.Count == 0) goto case 7;
                { var a = actors[random.Next(actors.Count)]; Ground(Floor(a.Npc.transform.position + Vector3.right * 2f)); said("go to actor " + a.Npc.name); }
                break;
            case 1:
                if (targets.Count == 0) goto case 7;
                { var t = targets[random.Next(targets.Count)]; var at = t.Go != null ? t.Go.transform.position : t.Pickup.Visual.transform.position; Ground(Floor(at + Vector3.right * 1.5f)); said("go to target"); }
                break;
            case 2: Away(e); said("away"); break;
            case 3:
            {
                var power = random.Next(2) == 0 ? W.Powers.EquippedA : W.Powers.EquippedB; var rt = Runtime(power.Id);
                if (!power.Effect.IsFlight) W.Powers.Select(rt);
                var npc = W.Npcs.Where(n => n != null && !n.Dead).OrderBy(n => Vector3.Distance(n.transform.position, W.Hero.transform.position)).FirstOrDefault();
                if (npc != null) Aim(Chest(npc));
                bool used = !power.Effect.IsFlight && W.Powers.Use(rt);
                if (W.Powers.Channeling != null) { for (int i = 0; i < 10; i++) { W.Powers.Channel(true, Time.deltaTime); yield return null; } W.Powers.Channel(false, Time.deltaTime); }
                if (W.Powers.HeldBody != null) W.Powers.Release(random.Next(2) == 0);
                said($"use {power.Id}: {used} ({W.Powers.Message})");
                break;
            }
            case 4:
            {
                float hold = .2f + (float)random.NextDouble() * 1.8f; e.ScriptedHold = true;
                float until = Time.time + hold; while (Time.time < until && !Ended(e)) yield return null;
                if (e != null) e.ScriptedHold = false; said($"hold R {hold:F2}s"); break;
            }
            case 5:
                if (actors.Count == 0) goto case 7;
                { var a = actors[random.Next(actors.Count)]; a.Npc.Damage(99999f, W.Powers); said("kill " + a.Npc.name); }
                break;
            case 6:
                if (targets.Count == 0) goto case 7;
                {
                    var t = targets[random.Next(targets.Count)]; if (t.Go == null) goto case 7;
                    float damage = (float)random.NextDouble() * 400f, impulse = (float)random.NextDouble() * 2500f;
                    CombatImpact.Blast(W.Powers, t.Go.transform.position + Vector3.right, 2.5f, impulse, damage, .2f); said($"blast target {damage:F0} dmg {impulse:F0} N.s");
                }
                break;
            case 7: { float wait = .1f + (float)random.NextDouble() * 1.9f; yield return new WaitForSeconds(wait); said($"wait {wait:F2}s"); break; }
            case 8: W.Mode.SetPaused(true); yield return new WaitForSecondsRealtime(.3f); W.Mode.SetPaused(false); said("pause 0.3s"); break;
            case 9: { bool punched = W.Hero.TryPunch(); said("punch " + punched); break; }
            case 10: { float heat = (float)random.NextDouble() * 2f; W.AddHeat(heat); said($"heat +{heat:F2}"); break; }
            case 11:
                if (actors.Count == 0) goto case 7;
                { var a = actors[random.Next(actors.Count)]; float t = (float)random.NextDouble() * 3f; if (random.Next(2) == 0) a.Npc.Freeze(t); else a.Npc.Root(t); said($"status {a.Npc.name} {t:F1}s"); }
                break;
        }
        yield return null;
    }
}
#endif
