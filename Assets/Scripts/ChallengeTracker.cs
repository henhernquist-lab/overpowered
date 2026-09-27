using System;
using System.Collections.Generic;
using UnityEngine;

public enum ChallengeMetric
{
    /// Hostile NPCs killed (Parameter = the credited power id, "melee" or "synergy:<id>"; empty = any).
    Kills,
    /// Endless: elite / miniboss kills, flawless waves (counts) and the highest wave cleared (best value).
    EliteKills, MinibossKills, FlawlessWaves, EndlessWave,
    /// Encounters completed successfully (Parameter = the EncounterDefinition asset name; empty = any).
    MissionsCompleted,
    /// Synergy activations (Parameter = synergy id; empty = any).
    SynergyUses,
    /// Best values within one session: the style total, and how many different powers landed a hit on an enemy.
    SessionStyle, DistinctPowersInSession,
}
public enum ChallengeScope { Lifetime, SingleSession }
public enum ChallengeSide { Any, Hero, Villain }
/// Saved state of one challenge (ProgressSave.Challenges). Progress = lifetime total, or the best single-session value.
/// Paid is written in the same save as the reward, so a reward can never be paid twice (also across processes).
[Serializable] public sealed class ChallengeProgress { public string Id; public int Progress; public bool Completed, Paid; }

/// Runtime challenge tracking for one session. Definitions come from an ENABLED Resources/ChallengeCatalog (none otherwise) (on the GameModeSession object). Listens to the player's hits and power uses,
/// encounter outcomes, the Endless director's score events and the session style score; pays through
/// PlayerProgression.PayChallenge. Unknown ids in the save (removed challenges) are left untouched and never paid.
public sealed class ChallengeTracker : MonoBehaviour
{
    public GameModeSession Session { get; private set; }
    public readonly List<ChallengeDefinition> Definitions = new List<ChallengeDefinition>();
    /// Per-session counts (SingleSession challenges) and the distinct powers that hit an enemy this session.
    readonly Dictionary<ChallengeDefinition, int> session = new Dictionary<ChallengeDefinition, int>();
    public readonly HashSet<string> PowersThatHit = new HashSet<string>();
    public event Action<ChallengeDefinition> Completed;
    PowerUser powers; EndlessWaveState waves; int lastStyle = -1;
    PlayerProgression Progression => Session.World.Progression;
    public void Begin(GameModeSession owner)
    {
        Session = owner; powers = owner.World.Powers;
        var catalog = ChallengeCatalog.Current;
        UseDefinitions(catalog != null && catalog.Enabled ? catalog.Challenges : new ChallengeDefinition[0]);
        if (powers != null) { powers.Hit += OnHit; powers.Used += OnUsed; }
        owner.EncounterResolved += OnEncounter;
        waves = owner.Director as EndlessWaveState; if (waves != null) waves.Scored += OnScored;
    }
    /// Replaces the active definitions (verification uses in-memory ones). Duplicate ids: the first wins, the rest are ignored.
    public void UseDefinitions(IEnumerable<ChallengeDefinition> definitions)
    {
        Definitions.Clear(); session.Clear(); var ids = new HashSet<string>();
        foreach (var d in definitions)
        {
            if (d == null || string.IsNullOrEmpty(d.Id)) continue;
            if (!ids.Add(d.Id)) { Debug.LogWarning("Duplicate challenge id ignored: " + d.Id + " (" + d.name + ")"); continue; }
            Definitions.Add(d);
        }
    }
    void OnDestroy()
    {
        if (powers != null) { powers.Hit -= OnHit; powers.Used -= OnUsed; }
        if (Session != null) Session.EncounterResolved -= OnEncounter;
        if (waves != null) waves.Scored -= OnScored;
    }
    void Update()
    {
        if (Session == null || Session.Style == null || Session.Style.Total == lastStyle) return;
        lastStyle = Session.Style.Total; Report(ChallengeMetric.SessionStyle, null, lastStyle);
    }
    void OnHit(PowerHit hit)
    {
        if (hit.Npc == null || !hit.Npc.Hostile) return;
        if (hit.Credit != null && hit.Credit != "melee" && !hit.Credit.StartsWith("synergy:") && PowersThatHit.Add(hit.Credit))
            Report(ChallengeMetric.DistinctPowersInSession, null, PowersThatHit.Count);
        if (hit.Killed) Report(ChallengeMetric.Kills, hit.Credit, 1);
    }
    void OnUsed(string id) { if (id != null && id.StartsWith("synergy:")) Report(ChallengeMetric.SynergyUses, id.Substring(8), 1); }
    void OnEncounter(EncounterOutcome outcome) { if (outcome.Success) Report(ChallengeMetric.MissionsCompleted, outcome.Definition != null ? outcome.Definition.name : null, 1); }
    void OnScored(EndlessScoreEvent e)
    {
        switch (e.Kind)
        {
            case EndlessScoreKind.EliteKill: Report(ChallengeMetric.EliteKills, null, 1); break;
            case EndlessScoreKind.MinibossKill: Report(ChallengeMetric.MinibossKills, null, 1); break;
            case EndlessScoreKind.Flawless: Report(ChallengeMetric.FlawlessWaves, null, 1); break;
            case EndlessScoreKind.WaveClear: Report(ChallengeMetric.EndlessWave, null, e.Wave); break;
        }
    }
    /// A metric observation: `amount` is an increment for counting metrics and the current value for best-value metrics.
    public void Report(ChallengeMetric metric, string parameter, int amount)
    {
        if (Session == null || Session.Ended || amount <= 0) return;
        var side = Progression.Data.Side;
        foreach (var d in Definitions)
        {
            if (d.Metric != metric || (!string.IsNullOrEmpty(d.Parameter) && d.Parameter != parameter)) continue;
            if ((d.Side == ChallengeSide.Hero && side != PlayerSide.Hero) || (d.Side == ChallengeSide.Villain && side != PlayerSide.Villain)) continue;
            var entry = Progression.Challenge(d.Id, true);
            if (entry.Paid) continue;
            int value;
            if (d.Accumulates)
            {
                session.TryGetValue(d, out int s); s = (int)Math.Min((long)s + amount, int.MaxValue); session[d] = s;
                if (d.Scope == ChallengeScope.Lifetime) entry.Progress = (int)Math.Min((long)entry.Progress + amount, int.MaxValue);
                else entry.Progress = Mathf.Max(entry.Progress, s);
                value = d.Scope == ChallengeScope.Lifetime ? entry.Progress : s;
            }
            else { entry.Progress = Mathf.Max(entry.Progress, amount); value = d.Scope == ChallengeScope.Lifetime ? entry.Progress : amount; }
            if (value >= d.Target && Progression.PayChallenge(d)) Completed?.Invoke(d);
        }
    }
}
