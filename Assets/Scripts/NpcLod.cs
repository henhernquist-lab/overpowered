using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Distance LOD for city NPCs (tuning: CityLayout.NpcLod).
/// NEAR (within NearRadius of the hero, with hysteresis): unchanged full behaviour - CityNpc AI every frame, Animator and
/// HumanoidPresentation enabled (clips, weighted panic bones, lean, attack markers), obstacle avoidance on.
/// FAR: CityNpc AI ticks every FarThinkInterval (paths, encounter driving and fleeing still run), the Animator is disabled
/// and advanced manually every FarAnimatorInterval, the presentation overlay is off, skinning runs only while visible,
/// agents skip obstacle avoidance. Promotion restores every saved setting.
/// Civilians that are far, unseen and not part of an encounter are moved to an unseen sidewalk around the hero, so the
/// large world keeps a local crowd instead of spreading 24 people over half a square kilometre.
[DefaultExecutionOrder(-50)]
public sealed class NpcLod : MonoBehaviour
{
    public static NpcLod Current { get; private set; }
    /// Counts for the current frame (updated before any CityNpc.Update).
    public int NearCount { get; private set; }
    public int FarCount { get; private set; }
    public int Promotions { get; private set; }
    public int Demotions { get; private set; }
    public int Recycled { get; private set; }
    public NpcLodSettings Settings { get; private set; }
    WorldSession world; int actorLayer; float recycleBudget;
    sealed class State
    {
        public bool Far; public float NextThink, AnimatorTime; public int Thinks, AnimatorSteps;
        public Animator Animator; public HumanoidPresentation Presentation; public SkinnedMeshRenderer[] Skins; public Renderer[] Renderers;
        public ObstacleAvoidanceType Avoidance;
    }
    readonly Dictionary<CityNpc, State> states = new Dictionary<CityNpc, State>();
    readonly List<CityNpc> gone = new List<CityNpc>();

    public void Initialize(WorldSession session, NpcLodSettings settings, int layer)
    {
        world = session; Settings = settings; actorLayer = layer; Current = this;
    }
    void OnDestroy() { if (Current == this) Current = null; }

    public bool IsFar(CityNpc npc) => npc != null && states.TryGetValue(npc, out var s) && s.Far;
    /// Asked by CityNpc.Update: false means "skip this frame" (a far NPC between its AI ticks).
    public bool ShouldThink(CityNpc npc)
    {
        if (!states.TryGetValue(npc, out var s)) return true;
        if (s.Far) { if (Time.time < s.NextThink) return false; s.NextThink = Time.time + Settings.FarThinkInterval; }
        s.Thinks++; return true;
    }
    /// Diagnostics: AI ticks granted and manual animator steps taken for this NPC so far.
    public int Thinks(CityNpc npc) => npc != null && states.TryGetValue(npc, out var s) ? s.Thinks : 0;
    public int AnimatorSteps(CityNpc npc) => npc != null && states.TryGetValue(npc, out var s) ? s.AnimatorSteps : 0;

    void Update()
    {
        if (world == null || world.Hero == null || Settings == null) return;
        Vector3 hero = world.Hero.transform.position;
        float near = Settings.NearRadius - Settings.Hysteresis, far = Settings.NearRadius + Settings.Hysteresis;
        int nearCount = 0, farCount = 0;
        foreach (var npc in world.Npcs)
        {
            if (npc == null) continue;
            if (!states.TryGetValue(npc, out var s)) s = Register(npc);
            float d = Vector3.Distance(npc.transform.position, hero);
            bool wantFar = Settings.Enabled && !npc.Dead && (s.Far ? d > near : d > far);
            if (wantFar != s.Far) { if (wantFar) Demote(npc, s); else Promote(npc, s); }
            if (s.Far)
            {
                farCount++;
                s.AnimatorTime += Time.deltaTime;
                if (s.AnimatorTime >= Settings.FarAnimatorInterval && s.Animator != null && npc.gameObject.activeInHierarchy) { s.Animator.Update(s.AnimatorTime); s.AnimatorTime = 0; s.AnimatorSteps++; }
            }
            else nearCount++;
        }
        NearCount = nearCount; FarCount = farCount;
        gone.Clear(); foreach (var key in states.Keys) if (key == null) gone.Add(key);
        foreach (var key in gone) states.Remove(key);
        if (Settings.Enabled) Recycle(hero);
    }

    State Register(CityNpc npc)
    {
        var p = npc.GetComponent<HumanoidPresentation>();
        var s = new State { Presentation = p, Animator = p != null ? p.Animator : null, Skins = npc.GetComponentsInChildren<SkinnedMeshRenderer>(true), Renderers = npc.GetComponentsInChildren<Renderer>(true),
            Avoidance = npc.Agent != null ? npc.Agent.obstacleAvoidanceType : ObstacleAvoidanceType.HighQualityObstacleAvoidance,
            NextThink = Time.time + Random.value * Settings.FarThinkInterval };
        // Bodies on the Actor layer get their own camera cull distance (CityArtSettings.Rendering.ActorCull).
        if (p != null && p.VisualRoot != null) foreach (var t in p.VisualRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = actorLayer;
        states[npc] = s; return s;
    }
    void Demote(CityNpc npc, State s)
    {
        s.Far = true; Demotions++; s.AnimatorTime = 0;
        if (s.Animator != null) s.Animator.enabled = false;
        if (s.Presentation != null) s.Presentation.enabled = false;
        foreach (var skin in s.Skins) if (skin != null) skin.updateWhenOffscreen = false;
        if (npc.Agent != null) { s.Avoidance = npc.Agent.obstacleAvoidanceType; npc.Agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance; }
        s.NextThink = Time.time + Random.value * Settings.FarThinkInterval;
    }
    void Promote(CityNpc npc, State s)
    {
        s.Far = false; Promotions++;
        if (s.Animator != null) s.Animator.enabled = true;
        if (s.Presentation != null) s.Presentation.enabled = true;
        foreach (var skin in s.Skins) if (skin != null) skin.updateWhenOffscreen = true;
        if (npc.Agent != null) npc.Agent.obstacleAvoidanceType = s.Avoidance;
    }
    /// Test/diagnostic access: the saved state is authoritative, so a forced tier goes through the same code path.
    public void Force(CityNpc npc, bool far)
    {
        if (!states.TryGetValue(npc, out var s)) s = Register(npc);
        if (far != s.Far) { if (far) Demote(npc, s); else Promote(npc, s); }
    }

    void Recycle(Vector3 hero)
    {
        recycleBudget = Mathf.Min(recycleBudget + Settings.RecyclePerSecond * Time.deltaTime, Settings.RecyclePerSecond);
        if (recycleBudget < 1f) return;
        var camera = Camera.main; var sidewalks = world.City.Sidewalks;
        foreach (var npc in world.Npcs)
        {
            if (recycleBudget < 1f) return;
            if (npc == null || npc.Dead || npc.Role != NpcRole.Civilian || npc.Encounter != null || npc.Fleeing || npc.Agent == null || !npc.Agent.isOnNavMesh) continue;
            if (Vector3.Distance(npc.transform.position, hero) < Settings.RecycleRadius) continue;
            if (!states.TryGetValue(npc, out var s) || Seen(s)) continue;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var point = sidewalks[Random.Range(0, sidewalks.Count)];
                float d = Vector3.Distance(point, hero);
                if (d < Settings.RecycleDistance.x || d > Settings.RecycleDistance.y || Visible(camera, point)) continue;
                if (!NavMesh.SamplePosition(point, out var hit, 2f, NavMesh.AllAreas)) continue;
                npc.Agent.Warp(hit.position); npc.Agent.ResetPath(); Recycled++; recycleBudget -= 1f; break;
            }
        }
    }
    static bool Seen(State s) { foreach (var r in s.Renderers) if (r != null && r.isVisible) return true; return false; }
    static bool Visible(Camera camera, Vector3 point)
    {
        if (camera == null) return false;
        var v = camera.WorldToViewportPoint(point + Vector3.up);
        return v.z > 0 && v.x > -.1f && v.x < 1.1f && v.y > -.1f && v.y < 1.1f;
    }
}
