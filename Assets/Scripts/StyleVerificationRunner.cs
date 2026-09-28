#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// See StyleVerification. StyleScoreTracker arithmetic and every anti-exploit rule with a farming attempt and a control, on
/// real NPCs through the real damage path (CityNpc.Damage -> PowerUser.Hit) inside credit scopes, then the session tracker
/// on a real Ice kill and BestStyle saved per mode.
public sealed class StyleVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/Style/";
    protected override string ResultFile => "results.txt";
    StyleSettings settings;
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Enter(F.Heroes[0], "ice", "strength", "hero");
        Isolate();
        settings = ScriptableObject.CreateInstance<StyleSettings>(); settings.AirborneKillBonus = 0;   // exact arithmetic regardless of the (disabled) hero's grounded flag
        Log($"Settings: hit {settings.HitPoints}, kill {settings.KillPoints}, multi-kill +{settings.MultiKillBonus}/extra within {settings.MultiKillWindow}s, variety +{settings.VarietyStep}/power (max x{settings.MaxMultiplier}), repeat x{settings.RepeatDecay}, per-target cap {settings.PerTargetHitCap}, idle {settings.IdleSeconds}s, {settings.MaxPointsPerSecond}/s.");
        yield return Variety();
        TargetCap(); DotTicks();
        yield return Bystanders();
        yield return Idle();
        yield return RateLimit();
        MultiKillAndSynergy();
        yield return Session();
    }
    StyleScoreTracker Fresh() { var t = new GameObject("Style probe").AddComponent<StyleScoreTracker>(); t.Begin(W.Powers, settings); return t; }
    void Done(StyleScoreTracker t) { t.End(); Destroy(t.gameObject); }
    int next;
    CityNpc Enemy(float health = 1f, NpcRole role = NpcRole.Criminal) { next++; return Actor(new Vector3((next % 20) * 2f - 20f, 150f, 12f + (next / 20) * 2f), role, health); }
    void KillWith(string credit, CityNpc npc) { using (W.Powers.Credit(credit)) npc.Damage(99999f, W.Powers); }
    IEnumerator Variety()
    {
        Log("---- VARIETY vs REPEAT");
        var varied = Fresh(); string[] mixed = { "ice", "fire", "strength" };
        for (int i = 0; i < 3; i++) { KillWith(mixed[i], Enemy()); if (i < 2) yield return new WaitForSeconds(2f); }
        int a = varied.Total; float peak = varied.PeakMultiplier; Done(varied);
        yield return new WaitForSeconds(settings.IdleSeconds + .2f);
        var same = Fresh();
        for (int i = 0; i < 3; i++) { KillWith("ice", Enemy()); if (i < 2) yield return new WaitForSeconds(2f); }
        int b = same.Total; Done(same);
        Log($"MEASURED three kills 2 s apart: varied (ice, fire, strength) {a} (peak x{peak}); same power (ice x3) {b}.");
        Check(a == 45 + 56 + 68 && peak == 1.5f, "Varied kills: 45 + 45x1.25 + 45x1.5 = 169 (hit 5 + kill 40 each).");
        Check(b == 45 + 32 + 22, "CONTROL: the same power three times decays: 45 + 45x0.7 + 45x0.49 = 99.");
    }
    void TargetCap()
    {
        Log("---- PER-TARGET HIT CAP (dummy farming)");
        var t = Fresh(); var dummy = Enemy(100000f);
        for (int i = 0; i < 10; i++) using (W.Powers.Credit("i" + (i % 5))) dummy.Damage(1f, W.Powers);
        int farmed = t.Total, capped = t.CappedHits; Done(t);
        var c = Fresh();
        for (int i = 0; i < 10; i++) using (W.Powers.Credit("i" + (i % 5))) Enemy(100000f).Damage(1f, W.Powers);
        Check(capped == 10 - settings.PerTargetHitCap && farmed < c.Total && c.CappedHits == 0, $"Ten hits on ONE dummy: {settings.PerTargetHitCap} pay, {capped} capped ({farmed} points); CONTROL ten different enemies: {c.Total} points, 0 capped.");
        Done(c);
    }
    void DotTicks()
    {
        Log("---- DAMAGE-OVER-TIME TICKS");
        var t = Fresh(); var npc = Enemy(100000f);
        for (int i = 0; i < 20; i++) using (W.Powers.Credit("poison")) npc.Damage(1f, W.Powers, false);
        Check(t.Total == 0 && t.Events == 0, "Twenty poison / beam ticks (assault=false) give no style.");
        using (W.Powers.Credit("poison")) npc.Damage(1f, W.Powers, true);
        Check(t.Total == settings.HitPoints, $"CONTROL: the first (assault) hit of the same poison pays {settings.HitPoints}.");
        Done(t);
    }
    IEnumerator Bystanders()
    {
        Log("---- BYSTANDERS");
        var t = Fresh();
        using (W.Powers.Credit("ice")) Enemy(100000f).Damage(1f, W.Powers);
        using (W.Powers.Credit("fire")) Enemy(100000f).Damage(1f, W.Powers);
        float built = t.Multiplier; int before = t.Total;
        var civilian = Enemy(1f, NpcRole.Civilian); yield return null;
        Check(!civilian.Hostile, "The civilian is not hostile to a hero.");
        KillWith("strength", civilian);
        Check(built > 1f && t.Total == before && t.Multiplier == 1f, $"Killing a civilian pays nothing and resets the multiplier (x{built} -> x{t.Multiplier}).");
        Done(t);
    }
    IEnumerator Idle()
    {
        Log("---- IDLE RESET");
        var t = Fresh();
        using (W.Powers.Credit("ice")) Enemy(100000f).Damage(1f, W.Powers);
        using (W.Powers.Credit("fire")) Enemy(100000f).Damage(1f, W.Powers);
        float built = t.Multiplier;
        yield return new WaitForSeconds(settings.IdleSeconds * .5f);
        Check(t.Multiplier == built, $"CONTROL: x{built} holds at {settings.IdleSeconds * .5f:F1} s.");
        yield return new WaitForSeconds(settings.IdleSeconds * .5f + .3f);
        Check(built == 1.25f && t.Multiplier == 1f, $"After {settings.IdleSeconds} s idle the multiplier resets (x{built} -> x{t.Multiplier}).");
        Done(t);
    }
    IEnumerator RateLimit()
    {
        Log("---- RATE LIMIT (mass-kill farming)");
        var victims = new List<CityNpc>(); for (int i = 0; i < 20; i++) victims.Add(Enemy());
        yield return null;
        var t = Fresh();
        for (int i = 0; i < victims.Count; i++) KillWith("k" + (i % 6), victims[i]);
        int burst = t.Total, limited = t.Limited;
        Log($"MEASURED 20 kills in one frame: {burst} granted, {limited} refused by the limiter.");
        Check(burst <= settings.MaxPointsPerSecond && limited > 0, $"One frame can never grant more than {settings.MaxPointsPerSecond} points.");
        yield return new WaitForSeconds(settings.MultiKillWindow + .2f);   // past the multi-kill window, inside the idle window
        int before = t.Total; KillWith("late", Enemy());
        Check(t.Total > before && t.Limited == limited, $"CONTROL: {settings.MultiKillWindow + .2f:F1} s later the bucket has refilled and the next kill pays in full (+{t.Total - before}).");
        Done(t);
    }
    void MultiKillAndSynergy()
    {
        Log("---- MULTI-KILL + SYNERGY");
        var t = Fresh();
        KillWith("ice", Enemy()); KillWith("fire", Enemy());
        Check(t.Total == 45 + 94, $"Two kills inside {settings.MultiKillWindow} s: 45 + (45 + 30) x1.25 = 139 ({t.Total}).");
        int before = t.Total; W.Powers.RecordUse("synergy:thermal-shock");
        Check(t.Total - before == Mathf.RoundToInt(settings.SynergyPoints * 1.5f), $"A synergy activation pays {settings.SynergyPoints} x the variety multiplier (x1.5): +{t.Total - before}.");
        int plain = t.Total; W.Powers.RecordUse("ice");
        Check(t.Total == plain, "CONTROL: an ordinary power activation alone pays nothing (only hits and kills do).");
        Done(t);
    }
    IEnumerator Session()
    {
        Log("---- SESSION TRACKER + BEST STYLE");
        var style = W.Mode.Style;
        Check(style != null && style.Settings != null, "The session owns a StyleScoreTracker.");
        int before = style.Total;
        var ice = Runtime("ice"); ice.Cooldown = 0f; ice.Charges = Mathf.Max(1, ice.Charges);
        var target = Actor(new Vector3(0, 150, 8), NpcRole.Criminal, 1f);
        yield return new WaitForSeconds(.2f);
        Aim(Chest(target)); Check(W.Powers.Use(ice) && target.Dead, "A real Ice cast kills a 1 HP criminal.");
        Check(style.Total > before, $"The session style rose by {style.Total - before} for the Ice kill (rank {style.Rank}, total {style.Total}).");
        int total = style.Total; string path = W.Progression.SavePath;
        W.Mode.ReturnHome(); yield return Scene(GameFlow.HomeScene);
        var probe = new GameObject("Style probe").AddComponent<PlayerProgression>();
        probe.Initialize(Resources.Load<GameTuning>("GameTuning").Progression, Resources.LoadAll<PowerDefinition>("Powers"), path);
        Check(probe.Record("hero") != null && probe.Record("hero").BestStyle == total, $"BestStyle {total} saved for the hero mode at session end ({probe.Record("hero")?.BestStyle}).");
        Destroy(probe.gameObject);
    }
}
#endif
