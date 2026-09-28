#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// See PowerStatsVerification. Per-power stats are instrumentation only: every value is checked against the hit that caused
/// it (health actually removed, so overkill is not counted), uncredited damage is a control, deferred hits (punch windup,
/// poison ticks, dash steps) are credited to their power, and a SEPARATE Unity process (Reload) reads the same numbers
/// back from the sandbox save.
public sealed class PowerStatsVerificationRunner : SessionVerificationRunner
{
    public bool Reload;
    protected override string Folder => "Verification/PowerStats/";
    protected override string ResultFile => Reload ? "reload.txt" : "results.txt";
    string Expected => Folder + "expected.txt";
    static string Line(PowerUsage u) => $"{u.Id} uses={u.Uses} hits={u.Hits} kills={u.Kills} sessions={u.Sessions} damage={u.Damage:F2}";
    PowerUsage U(string id) => W.Progression.Usage(id) ?? new PowerUsage { Id = id };
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        if (Reload) { ReadBack(); yield break; }
        yield return IceAndStrength();
        yield return PoisonAndSpeed();
    }
    IEnumerator IceAndStrength()
    {
        Log("---- ICE + STRENGTH");
        yield return Enter(F.Heroes[0], "ice", "strength", "hero");
        Isolate();
        Check(U("ice").Sessions == 1 && U("strength").Sessions == 1 && U("fire").Sessions == 0 && W.Progression.Data.PowerStats.All(u => u.Uses == 0 && u.Hits == 0),
            "Fresh save: one equipped session each for ice and strength, nothing for unequipped fire, no uses or hits yet.");
        var ice = Runtime("ice"); var stats = W.Powers.Stats(ice);
        var target = Actor(new Vector3(0, 150, 8), NpcRole.Criminal, 100f);
        yield return new WaitForSeconds(.2f);
        Aim(Chest(target)); Check(W.Powers.Use(ice), "Ice cast at a 100 HP criminal.");
        float iceDealt = 100f - target.Health;
        Check(U("ice").Uses == 1 && U("ice").Hits == 1 && Mathf.Abs(U("ice").Damage - iceDealt) < .01f && iceDealt > 0f && U("ice").Kills == 0, $"Ice stats: {Line(U("ice"))} (dealt {iceDealt:F2}, data damage {stats.Damage:F2}).");
        string before = string.Join(" | ", W.Progression.Data.PowerStats.Select(Line));
        target.Damage(10f, W.Powers); target.Damage(10f, null);
        Check(string.Join(" | ", W.Progression.Data.PowerStats.Select(Line)) == before, "CONTROL: damage outside any power (no credit) or with no source changes no stats.");
        var weak = Actor(W.Hero.transform.position + W.Hero.transform.forward * 1.1f, NpcRole.Criminal, 5f);
        yield return new WaitForSeconds(.2f);
        int frame = W.Hero.LastImpactFrame;
        Check(W.Hero.TryPunch(), "Super Strength punch at a 5 HP criminal.");
        float until = Time.time + 2f; while (W.Hero.LastImpactFrame == frame && Time.time < until) yield return null;
        Check(weak.Dead, $"The punch lands after its windup and kills ({W.Hero.LastPunchResult}).");
        Check(U("strength").Uses == 1 && U("strength").Kills == 1 && Mathf.Abs(U("strength").Damage - 5f) < .01f && U("ice").Kills == 0,
            $"Deferred punch credited to strength with only the 5 HP actually removed (no overkill): {Line(U("strength"))}.");
        W.Progression.Save();
        Check(W.Progression.LastError == null, "Saved.");
    }
    IEnumerator PoisonAndSpeed()
    {
        Log("---- POISON + SPEED");
        yield return Enter(F.Heroes[0], "poison", "speed", "hero");
        Isolate();
        Check(U("ice").Sessions == 1 && U("ice").Uses == 1 && U("strength").Kills == 1 && U("poison").Sessions == 1 && U("speed").Sessions == 1, "Stats from the first session carried over through the save; the new loadout adds its own sessions.");
        var poison = Runtime("poison"); var ps = W.Powers.Stats(poison);
        var sick = Actor(new Vector3(-6, 150, 10), NpcRole.Criminal, 1000f);
        yield return new WaitForSeconds(.2f);
        Aim(Chest(sick)); Check(W.Powers.Use(poison), "Poison a 1000 HP criminal.");
        yield return new WaitForSeconds(ps.Duration + .6f);
        var dot = sick.GetComponent<Poisoned>();
        Check(U("poison").Uses == 1 && U("poison").Hits == dot.Ticks && Mathf.Abs(U("poison").Damage - dot.TotalDamage) < .05f && Mathf.Abs(U("poison").Damage - (1000f - sick.Health)) < .05f,
            $"Every poison tick (after the cast returned) is credited to poison: {Line(U("poison"))}, {dot.Ticks} ticks, {dot.TotalDamage:F2} total.");
        var speed = Runtime("speed"); var ss = W.Powers.Stats(speed);
        var inPath = Actor(new Vector3(0, 150, 4.5f), NpcRole.Criminal, 1000f);
        yield return new WaitForSeconds(.2f);
        Aim(W.Hero.transform.position + new Vector3(0, 1.5f, 20));
        Check(W.Powers.Use(speed), "Dash through a criminal.");
        yield return new WaitForSeconds(ss.Duration + .3f);
        Check(U("speed").Uses == 1 && U("speed").Hits == 1 && Mathf.Abs(U("speed").Damage - (1000f - inPath.Health)) < .01f && U("speed").Damage > 0f, $"The dash step's hit is credited to speed: {Line(U("speed"))}.");
        W.Progression.Save();
        Check(W.Progression.LastError == null, "Saved.");
        File.WriteAllLines(Expected, W.Progression.Data.PowerStats.OrderBy(u => u.Id).Select(Line));
        Log("Expected values for the separate-process reload written to " + Expected + ":");
        foreach (var u in W.Progression.Data.PowerStats.OrderBy(u => u.Id)) Log("  " + Line(u));
    }
    void ReadBack()
    {
        Log("---- SEPARATE-PROCESS RELOAD");
        var probe = new GameObject("Stats probe").AddComponent<PlayerProgression>();
        probe.Initialize(Resources.Load<GameTuning>("GameTuning").Progression, Resources.LoadAll<PowerDefinition>("Powers"), WorldSession.VerificationSavePath);
        var expected = File.ReadAllLines(Expected); var actual = probe.Data.PowerStats.OrderBy(u => u.Id).Select(Line).ToArray();
        Check(probe.LastError == null && probe.Repairs.Count == 0, "The saved stats load without error or repair.");
        Check(expected.SequenceEqual(actual), $"Reloaded stats equal the saved ones exactly ({actual.Length} entries):\n  " + string.Join("\n  ", actual));
        Destroy(probe.gameObject);
    }
}
#endif
