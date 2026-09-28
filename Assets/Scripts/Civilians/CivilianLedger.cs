using System;
using System.Collections.Generic;
using UnityEngine;

public enum CivilianOutcome { Rescued, HarmedByHostile, HarmedByPlayer, HarmedByEnvironment, LostInMission, SafelyEscorted, Killed }
public enum HarmCause { Environment, Hostile }
/// Who is hurting a civilian when the damage has no PowerUser source. Hostile harm sites (a raider harassing, gunmen near
/// hostages, a robbery's threat) wrap their damage in HarmContext.Hostile(); fire and other world damage say Environment
/// (also the default for unattributed damage - never guessed as hostile or player).
public static class HarmContext
{
    public static HarmCause Current { get; private set; } = HarmCause.Environment;
    public readonly struct Scope : IDisposable
    {
        readonly HarmCause previous; readonly bool active;
        public Scope(HarmCause cause) { previous = Current; Current = cause; active = true; }
        public void Dispose() { if (active) Current = previous; }
    }
    public static Scope Hostile() => new Scope(HarmCause.Hostile);
    public static Scope Environment() => new Scope(HarmCause.Environment);
}
public struct CivilianSummary
{
    public int Rescued, HarmedByHostile, HarmedByPlayer, HarmedByEnvironment, LostInMission, SafelyEscorted, Killed;
    public override string ToString() => $"rescued {Rescued}, escorted {SafelyEscorted}, harmed by hostiles {HarmedByHostile} / player {HarmedByPlayer} / environment {HarmedByEnvironment}, killed {Killed} (in missions {LostInMission})";
}
/// Session-level civilian outcomes (WorldSession.Civilians). Each outcome counts a civilian at most once; a despawn or a
/// mission cleanup is not a death (only a lethal CityNpc.Damage is). Transient: nothing here is saved, and the per-actor
/// keys are Unity instance ids that live only as long as the session. No morality, reputation or score effect: scoring and
/// challenges may READ the summary later.
public sealed class CivilianLedger : MonoBehaviour
{
    readonly Dictionary<CivilianOutcome, HashSet<int>> seen = new Dictionary<CivilianOutcome, HashSet<int>>();
    public event Action<CivilianOutcome, CityNpc> Recorded;
    public int Count(CivilianOutcome outcome) => seen.TryGetValue(outcome, out var set) ? set.Count : 0;
    public bool Has(CityNpc npc, CivilianOutcome outcome) => npc != null && seen.TryGetValue(outcome, out var set) && set.Contains(npc.GetInstanceID());
    public CivilianSummary Summary => new CivilianSummary
    {
        Rescued = Count(CivilianOutcome.Rescued), HarmedByHostile = Count(CivilianOutcome.HarmedByHostile), HarmedByPlayer = Count(CivilianOutcome.HarmedByPlayer),
        HarmedByEnvironment = Count(CivilianOutcome.HarmedByEnvironment), LostInMission = Count(CivilianOutcome.LostInMission), SafelyEscorted = Count(CivilianOutcome.SafelyEscorted), Killed = Count(CivilianOutcome.Killed),
    };
    void Record(CivilianOutcome outcome, CityNpc npc)
    {
        if (npc == null || npc.Role != NpcRole.Civilian) return;
        if (!seen.TryGetValue(outcome, out var set)) seen[outcome] = set = new HashSet<int>();
        if (set.Add(npc.GetInstanceID())) Recorded?.Invoke(outcome, npc);
    }
    /// From CityNpc.Damage (health actually removed): player-sourced, else the current HarmContext.
    public void Harmed(CityNpc npc, float dealt, PowerUser source, bool died)
    {
        if (npc == null || npc.Role != NpcRole.Civilian || dealt <= 0f) return;
        Record(source != null ? CivilianOutcome.HarmedByPlayer : HarmContext.Current == HarmCause.Hostile ? CivilianOutcome.HarmedByHostile : CivilianOutcome.HarmedByEnvironment, npc);
        if (!died) return;
        Record(CivilianOutcome.Killed, npc);
        if (npc.Encounter != null) Record(CivilianOutcome.LostInMission, npc);
    }
    public void Rescued(CityNpc npc) => Record(CivilianOutcome.Rescued, npc);
    public void Escorted(CityNpc npc) => Record(CivilianOutcome.SafelyEscorted, npc);
}
