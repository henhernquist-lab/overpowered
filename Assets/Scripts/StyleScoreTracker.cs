using System.Collections.Generic;
using UnityEngine;

public readonly struct StyleAward
{
    public readonly int Points; public readonly string Reason; public readonly float Multiplier;
    public StyleAward(int points, string reason, float multiplier) { Points = points; Reason = reason; Multiplier = multiplier; }
}
/// Session style score: rewards VARIED, aggressive play against hostile enemies. It never changes the mode score or rewards;
/// it is a separate number (best per mode is saved) that challenges and the HUD may read.
/// Anti-exploit rules: non-hostile targets give nothing (killing one resets the streak); damage-over-time ticks give no hit
/// points; repeating one power decays its value; hit points per NPC are capped; idling resets the multiplier; a token
/// bucket caps points per second, so no loop (beam on one dummy, poison farm, spawn camping) scales without limit.
public sealed class StyleScoreTracker : MonoBehaviour
{
    public StyleSettings Settings { get; private set; }
    public int Total { get; private set; }
    public float Multiplier { get; private set; } = 1f;
    public float PeakMultiplier { get; private set; } = 1f;
    public int Events { get; private set; }
    /// Points refused by the rate limiter / zeroed by caps (verification: farming controls).
    public int Limited { get; private set; }
    public int CappedHits { get; private set; }
    public string Rank { get { string r = Settings.Ranks.Length > 0 ? Settings.Ranks[0] : ""; for (int i = 0; i < Settings.RankThresholds.Length && i < Settings.Ranks.Length; i++) if (Total >= Settings.RankThresholds[i]) r = Settings.Ranks[i]; return r; } }
    public event System.Action<StyleAward> Awarded;
    PowerUser user; float lastEvent = -999f, tokens, lastRefill; string streakId; int streak;
    readonly List<(float time, string id)> recent = new List<(float, string)>();
    readonly Dictionary<CityNpc, (int hits, float since)> targets = new Dictionary<CityNpc, (int, float)>();
    readonly List<float> kills = new List<float>();
    readonly List<CityNpc> stale = new List<CityNpc>();
    public void Begin(PowerUser powers, StyleSettings settings = null)
    {
        End(); user = powers; Settings = settings != null ? settings : StyleSettings.Current;
        tokens = Settings.MaxPointsPerSecond; lastRefill = Time.time;
        if (user != null) { user.Hit += OnHit; user.Used += OnUsed; }
    }
    public void End() { if (user != null) { user.Hit -= OnHit; user.Used -= OnUsed; } user = null; }
    void OnDestroy() { End(); }
    void Update()
    {
        if (Settings == null) return;
        if (Time.time - lastEvent > Settings.IdleSeconds && (recent.Count > 0 || streak > 0)) { recent.Clear(); streak = 0; streakId = null; Multiplier = 1f; }
        if (Time.frameCount % 60 == 0)
        {
            stale.Clear(); foreach (var pair in targets) if (pair.Key == null || Time.time - pair.Value.since > Settings.TargetWindow) stale.Add(pair.Key);
            foreach (var npc in stale) targets.Remove(npc);
        }
    }
    void OnUsed(string id)
    {
        if (id == null || !id.StartsWith("synergy:")) return;
        Award(Settings.SynergyPoints, id, "synergy");
    }
    void OnHit(PowerHit hit)
    {
        if (hit.Npc == null) return;
        if (!hit.Npc.Hostile)
        {
            if (hit.Killed) { recent.Clear(); streak = 0; streakId = null; Multiplier = 1f; }   // hurting bystanders breaks the streak
            return;
        }
        if (!hit.Assault && !hit.Killed) return;   // poison / beam ticks: no hit style
        string id = hit.Credit ?? "other"; int points = 0; string reason = "hit";
        if (hit.Assault)
        {
            targets.TryGetValue(hit.Npc, out var t);
            if (t.hits == 0 || Time.time - t.since > Settings.TargetWindow) t = (0, Time.time);
            if (t.hits < Settings.PerTargetHitCap) points += Settings.HitPoints; else CappedHits++;
            targets[hit.Npc] = (t.hits + 1, t.since);
        }
        if (hit.Killed)
        {
            reason = "kill"; points += Settings.KillPoints;
            var hero = WorldSession.Instance != null ? WorldSession.Instance.Hero : null;
            if (hero != null && !hero.PresentationState.Grounded) { points += Settings.AirborneKillBonus; reason = "airborne kill"; }
            for (int k = kills.Count - 1; k >= 0; k--) if (Time.time - kills[k] > Settings.MultiKillWindow) kills.RemoveAt(k);
            kills.Add(Time.time);
            if (kills.Count > 1) { points += Settings.MultiKillBonus * (kills.Count - 1); reason = kills.Count + "x multi-kill"; }
            targets.Remove(hit.Npc);
        }
        Award(points, id, reason);
    }
    void Award(int basePoints, string id, string reason)
    {
        float now = Time.time; lastEvent = now; Events++;
        for (int r = recent.Count - 1; r >= 0; r--) if (now - recent[r].time > Settings.VarietyWindow) recent.RemoveAt(r);   // per event: no closure
        recent.Add((now, id));
        int distinct = 0; for (int i = 0; i < recent.Count; i++) { bool seen = false; for (int j = 0; j < i; j++) if (recent[j].id == recent[i].id) { seen = true; break; } if (!seen) distinct++; }
        Multiplier = Mathf.Min(Settings.MaxMultiplier, 1f + Settings.VarietyStep * (distinct - 1));
        PeakMultiplier = Mathf.Max(PeakMultiplier, Multiplier);
        if (id == streakId) streak++; else { streakId = id; streak = 1; }
        float repeat = Mathf.Max(Settings.MinRepeatFactor, Mathf.Pow(Settings.RepeatDecay, streak - 1));
        int wanted = Mathf.RoundToInt(basePoints * Multiplier * repeat);
        tokens = Mathf.Min(Settings.MaxPointsPerSecond, tokens + (now - lastRefill) * Settings.MaxPointsPerSecond); lastRefill = now;
        int granted = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(wanted, tokens)), 0, wanted);
        tokens -= granted; Limited += wanted - granted;
        if (granted <= 0) return;
        Total += granted; Awarded?.Invoke(new StyleAward(granted, reason + " (" + id + ")", Multiplier));
    }
}
