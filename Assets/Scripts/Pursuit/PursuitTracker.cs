using System;
using UnityEngine;

public enum PursuitState { Clear, Alerted, Pursued, Searching, Escaped }
/// Villain / open-world pursuit, derived only from existing facts (Heat, hostile police NPCs near the player, a clear line
/// between them, time and distance since the last contact). No stealth, no vision cones, no gameplay effect of its own:
/// missions (StageKind.LosePursuit) and a future HUD read it. A side the police can never turn on (Hero by default:
/// HostileFromStars above MaximumStars and RespondersHostile off) stays Clear.
///   Clear -> Alerted (Heat > 0) -> Pursued (contact) -> Searching (no contact for LoseContactSeconds)
///   Searching -> Pursued (reacquired) | Escaped (SearchSeconds without contact and EscapeDistance from the lost contact)
///   Escaped -> Clear / Alerted after EscapedHoldSeconds; session end or respawn -> Clear.
public sealed class PursuitTracker : MonoBehaviour
{
    public PursuitSettings Settings { get; private set; }
    public PursuitState State { get; private set; }
    public float SinceContact => lastContact < 0f ? float.PositiveInfinity : Time.time - lastContact;
    public Vector3 LastContactPosition { get; private set; }
    public int Contacts { get; private set; }
    public int Reacquired { get; private set; }
    public int Escapes { get; private set; }
    /// Police NPCs in contact at the last sample.
    public int ContactCount { get; private set; }
    public event Action<PursuitState, PursuitState> StateChanged;
    WorldSession world; float nextSample, lastContact = -1f, escapedAt;
    static readonly RaycastHit[] hits = new RaycastHit[16];
    public void Initialize(WorldSession owner, PursuitSettings settings = null) { world = owner; Settings = settings != null ? settings : PursuitSettings.Current; }
    /// Can the police ever pursue the current side? (data: GameTuning.Heat per-side police settings)
    public bool SideCanBePursued
    {
        get { var p = world.Tuning.Heat.Police(world.Progression.Data.Side); return p.RespondersHostile || p.HostileFromStars <= world.Tuning.Heat.MaximumStars; }
    }
    public void ResetState() { lastContact = -1f; ContactCount = 0; Set(PursuitState.Clear); }
    void Set(PursuitState next) { if (next == State) return; var from = State; State = next; if (next == PursuitState.Escaped) { Escapes++; escapedAt = Time.time; } StateChanged?.Invoke(from, next); }
    void Update()
    {
        if (world == null || world.Hero == null || Time.time < nextSample) return;
        nextSample = Time.time + Settings.SampleSeconds; Sample();
    }
    /// One evaluation (verification may call it directly instead of waiting for the next sample).
    public void Sample()
    {
        if ((world.Mode != null && world.Mode.Ended) || world.PlayerDead || !SideCanBePursued) { if (State != PursuitState.Clear) ResetState(); return; }
        Vector3 player = world.Hero.transform.position;
        ContactCount = 0;
        foreach (var npc in world.Npcs)
        {
            if (npc == null || npc.Dead || !npc.gameObject.activeInHierarchy || (npc.Role != NpcRole.Cop && npc.Role != NpcRole.PursuingHero) || !npc.Hostile) continue;
            if ((npc.transform.position - player).sqrMagnitude > Settings.ContactRange * Settings.ContactRange) continue;
            if (Clear(npc.transform.position + Vector3.up * 1.5f, player + Vector3.up * 1.2f)) ContactCount++;
        }
        bool contact = ContactCount > 0;
        if (contact) { if (State != PursuitState.Pursued) Contacts++; if (State == PursuitState.Searching || State == PursuitState.Escaped) Reacquired++; lastContact = Time.time; LastContactPosition = player; Set(PursuitState.Pursued); return; }
        switch (State)
        {
            case PursuitState.Clear: if (world.Heat > 0f) Set(PursuitState.Alerted); break;
            case PursuitState.Alerted: if (world.Heat <= 0f) Set(PursuitState.Clear); break;
            case PursuitState.Pursued: if (SinceContact >= Settings.LoseContactSeconds) Set(PursuitState.Searching); break;
            case PursuitState.Searching:
                if (SinceContact >= Settings.SearchSeconds && Vector3.Distance(player, LastContactPosition) >= Settings.EscapeDistance) Set(PursuitState.Escaped);
                break;
            case PursuitState.Escaped: if (Time.time - escapedAt >= Settings.EscapedHoldSeconds) Set(world.Heat > 0f ? PursuitState.Alerted : PursuitState.Clear); break;
        }
    }
    /// Solid static geometry blocks the line; NPCs, props and the player do not (same rule as the chain-lightning arc).
    bool Clear(Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a; float length = delta.magnitude; if (length < .01f) return true;
        int count = Physics.RaycastNonAlloc(a, delta / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = hits[i].collider;
            if (c.attachedRigidbody != null || c.GetComponentInParent<CityNpc>() != null || c.transform.root == world.Hero.transform) continue;
            return false;
        }
        return true;
    }
}
