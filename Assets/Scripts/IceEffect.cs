using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Freeze")]
public sealed class IceEffect : PowerEffect
{
    [Header("Melee shatter (additive to the paid melee hit)")]
    public float ShatterDamage = 30f, ShatterImpulse = 1800f, ShatterLift = .65f;
    public int ShatterParticles = 28;
    public static float Shatter(PowerUser user, Component target, Vector3 origin, float radius)
    {
        var npc=target.GetComponent<CityNpc>();var frozen=target.GetComponent<FrozenBody>();
        // A freeze applied in this same frame (e.g. by an area freeze in the same call) is established, not consumed:
        // shatter is a follow-up against a freeze that existed before this frame.
        if(npc!=null ? npc.Dead||!npc.Frozen||npc.FreezeStartedFrame==Time.frameCount : frozen==null||!frozen.Frozen)return 0;
        var power=user.Powers.Find(p=>p.Definition.Effect is IceEffect);
        if(power==null)return 0;
        var settings=(IceEffect)power.Definition.Effect;
        Rigidbody body;
        if(npc!=null)
        {
            npc.Thaw();
            var suspension=npc.GetComponent<SynergySuspension>();if(suspension==null)suspension=npc.gameObject.AddComponent<SynergySuspension>();
            body=suspension.Begin(npc);body.useGravity=true;suspension.Release();
        }
        else { body=frozen.GetComponent<Rigidbody>();frozen.Thaw(); }
        target.GetComponent<FrozenLook>()?.Clear();
        body.AddExplosionForce(settings.ShatterImpulse,origin,radius,settings.ShatterLift,ForceMode.Impulse);
        FeelDirector.Instance?.Particles.Burst(target.transform.position+Vector3.up*.8f,power.Definition.PaletteColor,settings.ShatterParticles);
        return settings.ShatterDamage;
    }
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var stats = user.Stats(power);
        if (!user.FindTarget(stats.Range, out RaycastHit hit)) return false;
        var npc = hit.collider.GetComponentInParent<CityNpc>();
        if (npc != null)
        {
            npc.Freeze(stats.Duration); npc.Damage(stats.Damage, user);
            // Visible state for as long as the NPC's own Frozen flag holds (a later Ice/synergy freeze extends it too).
            if (!npc.Dead) FrozenLook.Show(npc.gameObject, power.Definition.PaletteColor, () => npc != null && !npc.Dead && npc.Frozen);
            Cast(user, power, hit.point); return true;
        }
        var mission = hit.collider.GetComponentInParent<MissionTarget>();   // e.g. a mission fire spot: Ice douses it
        if (mission != null && mission.Freeze(user, stats)) { Cast(user, power, hit.point); return true; }
        if (hit.rigidbody == null || hit.rigidbody.isKinematic) return false;
        var frozen = hit.rigidbody.GetComponent<FrozenBody>();
        if (frozen == null) frozen = hit.rigidbody.gameObject.AddComponent<FrozenBody>();
        frozen.Apply(stats.Duration, power.Definition.PaletteColor);
        Cast(user, power, hit.point);
        return true;
    }
    /// Visible cast: a small burst at the casting hand and a heavy burst where the freeze lands, from the fixed Feel pool in
    /// the power's palette colour (ice.asset PaletteColor). No new particle systems or materials.
    static void Cast(PowerUser user, PowerRuntime power, Vector3 point)
    {
        var feel = FeelDirector.Instance; if (feel == null) return;
        var color = power.Definition.PaletteColor;
        feel.Particles.Burst(user.AimOrigin + user.AimDirection * .6f, color, feel.Settings.LightHitParticles);
        feel.Particles.Burst(point, color, feel.Settings.HeavyHitParticles);
    }
}
public sealed class FrozenBody : MonoBehaviour
{
    Rigidbody body; RigidbodyConstraints prior; float until; bool applied;
    public bool Frozen => applied && enabled && Time.time < until;
    public void Thaw() { enabled=false; Destroy(this); }
    public void Apply(float seconds, CityColor color)
    {
        if (!applied) { body = GetComponent<Rigidbody>(); prior = body.constraints; applied = true; }
        body.constraints = RigidbodyConstraints.FreezeAll; until = Time.time + seconds;
        FrozenLook.Show(gameObject, color, () => this != null && enabled);
    }
    void Update() { if (Time.time >= until) Destroy(this); }
    void OnDisable() { if (body != null && applied) body.constraints = prior; }
}
/// Visible frozen state for an NPC or a physics prop: every body renderer shows ONE shared palette material
/// (CityMaterials.Get, never a per-instance material) and an NPC's Animator holds its pose. Everything is restored exactly
/// when the freeze condition ends (thaw, death, or the frozen component going away).
public sealed class FrozenLook : MonoBehaviour
{
    Renderer[] renderers; Material[][] original; Animator animator; float animatorSpeed; System.Func<bool> active; bool shown;
    public bool Shown => shown;
    public void Clear() { Restore(); }
    public static FrozenLook Show(GameObject target, CityColor color, System.Func<bool> stillFrozen)
    {
        var look = target.GetComponent<FrozenLook>(); if (look == null) look = target.AddComponent<FrozenLook>();
        look.Apply(color, stillFrozen); return look;
    }
    void Apply(CityColor color, System.Func<bool> stillFrozen)
    {
        active = stillFrozen;
        if (!shown)
        {
            var npc = GetComponent<CityNpc>();
            // NPC: only the humanoid body (not the attack telegraph); prop: every renderer under the rigidbody.
            renderers = npc != null ? GetComponentsInChildren<SkinnedMeshRenderer>(true) : GetComponentsInChildren<Renderer>(true);
            original = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++) original[i] = renderers[i].sharedMaterials;
            var presentation = npc != null ? GetComponent<HumanoidPresentation>() : null;
            animator = presentation != null ? presentation.Animator : null;
            if (animator != null) animatorSpeed = animator.speed;
            shown = true;
        }
        var ice = CityMaterials.Get(color);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials; for (int m = 0; m < mats.Length; m++) mats[m] = ice; r.sharedMaterials = mats;
        }
        if (animator != null) animator.speed = 0f;
        enabled = true;
    }
    void LateUpdate() { if (shown && (active == null || !active())) Restore(); }
    void Restore()
    {
        if (!shown) return; shown = false;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].sharedMaterials = original[i];
        if (animator != null) animator.speed = animatorSpeed;
        enabled = false;
    }
    void OnDestroy() { Restore(); }
}
