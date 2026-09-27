using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// SPEED — a short combat burst dash, distinct from Flight (sustained, fuel, vertical traversal). Range = dash distance,
/// Duration = dash time, both through SuperHeroController.Dash (CharacterController only). Direction: the movement input,
/// or the flattened aim when standing still. NPC capsules are passed through; each HOSTILE NPC the dash passes takes the
/// power's Damage once. Several charges with a short cooldown make it a tempo tool rather than a travel tool.
[CreateAssetMenu(menuName = "Overpowered/Effects/Burst dash")]
public sealed class SpeedDashEffect : PowerEffect
{
    [Header("Dash")]
    public float PassRadius = 1.1f;
    public int StepParticles = 2, PassParticles = 8;
    public float TrailSeconds = .25f, TrailWidth = .35f;
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var hero = user.Hero;
        if (hero == null || hero.Dashing) return false;
        var stats = user.Stats(power); var color = power.Definition.PaletteColor;
        Vector3 direction = hero.MoveInput.sqrMagnitude > .01f ? hero.MoveInput : Vector3.Scale(user.AimDirection, new Vector3(1, 0, 1));
        if (direction.sqrMagnitude < .0001f) direction = hero.transform.forward;
        var trail = DashTrail.For(hero, color, TrailSeconds, TrailWidth);
        // Per-hero reusable state and a cached step delegate: no collection or closure is allocated per dash (a dash cannot
        // start while one is running, so the set is never shared between two live dashes).
        trail.Begin(user, this, stats.Damage, color);
        trail.Emitting = true;
        bool started = hero.Dash(direction, stats.Range, stats.Duration, trail.Step);
        if (!started) { trail.Emitting = false; return false; }
        LastPassed = trail.Passed;
        return true;
    }
    /// Hostile NPCs hit by the most recent dash (filled while that dash runs).
    public static HashSet<CityNpc> LastPassed { get; private set; } = new HashSet<CityNpc>();
}
/// One TrailRenderer on the player ROOT (first person keeps it; not on the VisualRoot), created once and reused; it only
/// emits while SuperHeroController.Dashing. Shared palette material.
public sealed class DashTrail : MonoBehaviour
{
    TrailRenderer trail; SuperHeroController hero;
    PowerUser user; SpeedDashEffect settings; float damage; CityColor color;
    readonly Collider[] nearby = new Collider[32];
    /// Hostile NPCs clipped by the current (or last) dash.
    public readonly HashSet<CityNpc> Passed = new HashSet<CityNpc>();
    public System.Action Step { get; private set; }
    public void Begin(PowerUser owner, SpeedDashEffect effect, float hitDamage, CityColor tint)
    { user = owner; settings = effect; damage = hitDamage; color = tint; Passed.Clear(); if (Step == null) Step = OnStep; }
    void OnStep()
    {
        if (hero == null || settings == null) return;
        Vector3 centre = hero.transform.position + Vector3.up * .9f;
        FeelDirector.Instance?.Particles.Burst(hero.transform.position + Vector3.up * .3f, color, settings.StepParticles);
        int count = Physics.OverlapSphereNonAlloc(centre, settings.PassRadius, nearby, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var npc = nearby[i].GetComponentInParent<CityNpc>();
            if (npc == null || npc.Dead || !npc.Hostile || !Passed.Add(npc)) continue;
            npc.Damage(damage, user);
            FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up, color, settings.PassParticles);
        }
    }
    public bool Emitting { get => trail != null && trail.emitting; set { if (trail != null) trail.emitting = value; } }
    public static DashTrail For(SuperHeroController hero, CityColor color, float seconds, float width)
    {
        var t = hero.GetComponentInChildren<DashTrail>();
        if (t == null)
        {
            var go = new GameObject("Dash trail"); go.transform.SetParent(hero.transform, false); go.transform.localPosition = Vector3.up * .9f;
            t = go.AddComponent<DashTrail>(); t.hero = hero;
            t.trail = go.AddComponent<TrailRenderer>(); t.trail.emitting = false; t.trail.minVertexDistance = .2f;
            t.trail.shadowCastingMode = ShadowCastingMode.Off; t.trail.receiveShadows = false;
        }
        t.trail.sharedMaterial = CityMaterials.Get(color); t.trail.time = seconds; t.trail.widthMultiplier = width;
        return t;
    }
    void LateUpdate() { if (trail != null && trail.emitting && hero != null && !hero.Dashing) trail.emitting = false; }
}
