using UnityEngine;

/// Game-feel reactions for one city session: hit pause (through TimeArbiter), camera impulse + FOV kick (applied by
/// ThirdPersonCamera, render-only) and pooled impact debris. Every value comes from GameTuning.Feel.
/// Inputs are additive hooks: CombatImpact.Blast (outgoing hits, incl. synergy impacts), CityNpc's contact release
/// (damage landing on the player), SuperHeroController.Landed (hard landings) and BreakableProp.Destroyed (breaks).
/// Heavy/light is decided here from data (Feel.HeavyImpulse / Feel.HeavyIncomingDamage), never by caller identity.
[DefaultExecutionOrder(-1000)]
public sealed class FeelDirector : MonoBehaviour
{
    public static FeelDirector Instance { get; private set; }
    /// The live values. Defaults to WorldSession.Tuning.Feel; verification may point it at a CLONE (never the asset).
    public FeelSettings Settings { get; set; }
    public ImpactParticlePool Particles { get; private set; }
    public int HeavyImpacts { get; private set; }
    public int LightImpacts { get; private set; }
    public int HeavyIncoming { get; private set; }
    public int HardLandings { get; private set; }
    public int SoftLandings { get; private set; }
    public int CameraKicks { get; private set; }
    public string LastEvent { get; private set; } = "";
    public Vector3 LastImpactOrigin { get; private set; }
    public float LastImpactImpulse { get; private set; }
    public bool LastImpactHeavy { get; private set; }
    SuperHeroController hero;

    public void Initialize(WorldSession world)
    {
        Instance = this; Settings = world.Tuning.Feel;
        Particles = new GameObject("Impact debris pool").AddComponent<ImpactParticlePool>();
        Particles.transform.SetParent(transform, false); Particles.Initialize(Settings);
        hero = world.Hero; if (hero != null) hero.Landed += OnLanded;
        BreakableProp.Destroyed += OnPropDestroyed;
    }
    void Update() { TimeArbiter.Tick(); }
    void OnDestroy()
    {
        if (hero != null) hero.Landed -= OnLanded;
        BreakableProp.Destroyed -= OnPropDestroyed;
        TimeArbiter.Reset();
        if (Instance == this) Instance = null;
    }

    // ---------------------------------------------------------------- hooks (static, safe with no session)
    /// CombatImpact.Blast finished: `touched` = bodies + NPCs it reached.
    public static void Impact(Vector3 origin, float impulse, float damage, int touched)
    {
        var d = Instance; if (d == null || touched <= 0) return;
        var f = d.Settings; bool heavy = impulse >= f.HeavyImpulse;
        d.LastImpactOrigin = origin; d.LastImpactImpulse = impulse; d.LastImpactHeavy = heavy;
        d.Particles.Burst(origin, heavy ? f.HeavyDebris : f.HitDebris, heavy ? f.HeavyHitParticles : f.LightHitParticles);
        if (!heavy) { d.LightImpacts++; d.LastEvent = $"light impact {impulse:0} N·s"; return; }
        d.HeavyImpacts++; d.LastEvent = $"HEAVY impact {impulse:0} N·s";
        d.Heavy(origin, 1f);
    }
    /// Damage landed on the player from a hostile's committed attack.
    public static void PlayerHit(float damage, Vector3 from)
    {
        var d = Instance; if (d == null || damage < d.Settings.HeavyIncomingDamage) return;
        d.HeavyIncoming++; d.LastEvent = $"HEAVY incoming {damage:0.#}";
        d.Heavy(from, d.Settings.IncomingImpulseScale);
    }
    void OnLanded(float speed)
    {
        var f = Settings;
        if (speed < f.HardLandingSpeed) { SoftLandings++; return; }
        HardLandings++; LastEvent = $"hard landing {speed:0.0} m/s";
        if (hero != null) Particles.Burst(hero.transform.position + Vector3.up * .05f, f.LandingDebris, f.LandingParticles);
        Kick(hero != null ? hero.transform.position + Vector3.up * 3f : Vector3.zero, f.LandingScale, true);
    }
    void OnPropDestroyed(Vector3 position) { Particles.Burst(position, Settings.BreakDebris, Settings.BreakParticles); }

    void Heavy(Vector3 source, float scale)
    {
        var f = Settings;
        TimeArbiter.RequestHitPause(f.HitPauseSeconds, f.HitPauseMinInterval, f.HitPauseTimeScale);
        Kick(source, scale, false);
    }
    /// Camera impulse along the source -> camera direction (biased downward) plus an FOV kick, both scaled.
    void Kick(Vector3 source, float scale, bool vertical)
    {
        var cam = ThirdPersonCamera.Active; if (cam == null) return;
        var f = Settings;
        Vector3 away = cam.transform.position - source; away.y = 0f;
        Vector3 direction = vertical || away.sqrMagnitude < .01f ? Vector3.down : (away.normalized * .6f + Vector3.down * .8f).normalized;
        cam.Impulse(direction, f.ImpulseAmplitude * scale, f.ImpulseSeconds, f.ImpulseFrequency);
        cam.FovKick(f.FovKickDegrees * scale, f.FovKickSeconds);
        CameraKicks++;
    }
}
