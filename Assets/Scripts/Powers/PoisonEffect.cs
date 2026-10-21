using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// POISON — damage over time. The aimed NPC takes the power's Damage PER SECOND for its Duration, in TickSeconds steps (the
/// first tick counts as the assault for Heat; later ticks do not). If a poisoned NPC dies WHILE still poisoned — from the
/// poison or from anything else — the poison jumps to up to MaxSpreadTargets nearby enemies within SpreadRadius, for a fresh
/// Duration x SpreadDurationScale. Each jump is one generation deeper; MaxGenerations bounds the chain.
[CreateAssetMenu(menuName = "Overpowered/Effects/Poison")]
public sealed class PoisonEffect : PowerEffect
{
    [Header("Damage over time")]
    public float TickSeconds = .5f;
    [Header("Spread on death")]
    public float SpreadRadius = 6f, SpreadDurationScale = 1f;
    public int MaxSpreadTargets = 3, MaxGenerations = 3;
    [Tooltip("Spread only to NPCs hostile to the player.")] public bool EnemiesOnly = true;
    [Header("Presentation")]
    public int ApplyParticles = 10, TickParticles = 3;
    public float SpreadArcSeconds = .35f, SpreadArcWidth = .06f;
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var d = power.Definition;
        Vector3 direction = user.AimDirection, origin = user.AimOrigin;
        float radius = d.ProjectileSize * .5f;
        foreach (var hit in Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Ignore))
            if (hit.transform.root != user.transform) return false;
        float offset = d.OriginOffset;
        foreach (var hit in Physics.SphereCastAll(origin, radius, direction, offset, ~0, QueryTriggerInteraction.Ignore))
            if (hit.transform.root != user.transform) offset = Mathf.Min(offset, Mathf.Max(0, hit.distance - .01f));
        var pool = PoisonProjectilePool.Instance ?? PoisonProjectilePool.Create(WorldSession.Instance);
        return pool.Launch(user, power, this, origin + direction * offset, direction);
    }
}
/// Live poison on one NPC. Added once per NPC and reused; listens to CityNpc.Damaged so any killing blow spreads it.
public sealed class Poisoned : MonoBehaviour
{
    CityNpc npc; PowerUser source; PoisonEffect settings; CityColor color; string credit;
    float dps, intendedDps, tickDamage, nextTick, baseSeconds; int ticksLeft; bool assaulted, spread;
    public float DamagePerSecond => dps;
    public float DamagePerTick => tickDamage;
    public int Generation { get; private set; }
    public float TotalDamage { get; private set; }
    public int Ticks { get; private set; }
    /// Poisoned = ticks still owed. Duration is converted to a whole tick count, so the total is exactly dps x duration.
    public bool Active => npc != null && !npc.Dead && ticksLeft > 0;
    public bool Spread => spread;
    bool lethalTick; int visualSlot=-1;
    public bool VisualActive => visualSlot>=0&&Active;
    public static int VisualSpreads { get; private set; }
    /// (from, to) each time poison jumps from a dying NPC to a new one.
    public static event System.Action<CityNpc, CityNpc> Spreading;
    static readonly Collider[] nearby = new Collider[64];
    public static Poisoned Apply(CityNpc target, PowerUser user, PoisonEffect settings, float dps, float seconds, int generation, CityColor color, float? rolledDamagePerSecond = null)
    {
        var p = target.GetComponent<Poisoned>();
        if (p == null) { p = target.gameObject.AddComponent<Poisoned>(); p.npc = target; target.Damaged += p.OnDamaged; }
        bool wasActive = p.Active;
        p.source = user; p.settings = settings; p.color = color; p.baseSeconds = seconds; p.credit = user != null ? user.Crediting : null;
        float rolled = rolledDamagePerSecond ?? (user != null ? user.RollDamage(dps) : dps);
        if (!wasActive || rolled >= p.dps) { p.dps = rolled; p.intendedDps = dps; }
        int ticks = Mathf.Max(1, Mathf.RoundToInt(seconds / Mathf.Max(.01f, settings.TickSeconds)));
        p.tickDamage = p.dps * seconds / ticks; // exact full-duration damage, including non-integral tick durations
        p.ticksLeft = wasActive ? Mathf.Max(p.ticksLeft, ticks) : ticks;
        p.Generation = wasActive ? Mathf.Min(p.Generation, generation) : generation;
        if (!wasActive) { p.nextTick = Time.time + settings.TickSeconds; p.assaulted = false; p.spread = false; }
        p.enabled = true;
        if(p.visualSlot<0)p.visualSlot=StatusPowerVfx.Get().AcquirePoison(p);
        return p;
    }
    void Update()
    {
        if (!Active) { enabled = false; return; }
        if(visualSlot<0)visualSlot=StatusPowerVfx.Get().AcquirePoison(this);
        // An ended session (results pending) must not keep killing NPCs and paying XP; pause already stops Time.time.
        var world = WorldSession.Instance; if (world != null && world.Mode != null && world.Mode.Ended) { enabled = false; return; }
        if (Time.time < nextTick) return;
        nextTick += settings.TickSeconds; ticksLeft--;
        float damage = tickDamage;
        TotalDamage += damage; Ticks++;
        bool first = !assaulted; assaulted = true;
        if(visualSlot>=0&&StatusPowerVfx.Existing!=null)StatusPowerVfx.Existing.PulsePoison(this,visualSlot);
        FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * 1.2f, color, settings.TickParticles);
        lethalTick = true;
        using (source != null ? source.Credit(credit) : default) npc.Damage(damage, source, first);   // a lethal tick raises Damaged(true) -> OnDamaged spreads
        lethalTick = false;
    }
    void OnDamaged(bool died)
    {
        // Still poisoned at the moment of death: spread once. The lethal poison tick itself counts (it was owed while alive).
        if (!died || spread || settings == null || (ticksLeft <= 0 && !lethalTick)) return;
        spread = true; enabled = false;
        if (Generation >= settings.MaxGenerations) return;
        Vector3 origin = transform.position;
        int count = Physics.OverlapSphereNonAlloc(origin, settings.SpreadRadius, nearby, ~0, QueryTriggerInteraction.Ignore);
        // Pooled per spread (nested-safe); the nearest MaxSpreadTargets are kept by insertion, no sort delegate per spread.
        var chosen = ListPool<CityNpc>.Get();
        try
        {
            int keep = Mathf.Max(0, settings.MaxSpreadTargets);
            for (int i = 0; i < count && keep > 0; i++)
            {
                var other = nearby[i].GetComponentInParent<CityNpc>();
                if (other == null || other == npc || other.Dead || chosen.Contains(other)) continue;
                if (settings.EnemiesOnly && !other.Hostile) continue;
                var existing = other.GetComponent<Poisoned>(); if (existing != null && existing.Active) continue;
                float d = (other.transform.position - origin).sqrMagnitude; int at = chosen.Count;
                while (at > 0 && (chosen[at - 1].transform.position - origin).sqrMagnitude > d) at--;
                if (at >= keep) continue;
                chosen.Insert(at, other); if (chosen.Count > keep) chosen.RemoveAt(chosen.Count - 1);
            }
            var vfx = PowerVfx.Get();
            foreach (var target in chosen)
            {
                Apply(target, source, settings, intendedDps, baseSeconds * settings.SpreadDurationScale, Generation + 1, color);
                var from=origin+Vector3.up;var to=target.transform.position+Vector3.up;
                var arc=vfx.Arc(from,to,5,.16f,color,settings.SpreadArcWidth*1.5f,settings.SpreadArcSeconds);
                if(arc!=null)arc.sharedMaterial=CityMaterials.GetVfx(CityColor.Leaf);
                VisualSpreads++;
                Spreading?.Invoke(npc, target);
            }
        }
        finally { ListPool<CityNpc>.Release(chosen); }
    }
    void OnDisable()
    {
        if(visualSlot>=0&&StatusPowerVfx.Existing!=null)StatusPowerVfx.Existing.ReleasePoison(this,visualSlot);
        visualSlot=-1;
    }
    void OnDestroy() { if (npc != null) npc.Damaged -= OnDamaged; }
}
