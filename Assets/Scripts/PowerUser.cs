using System.Collections.Generic;
using UnityEngine;

public sealed class PowerUser : MonoBehaviour
{
    public readonly List<PowerRuntime> Powers = new List<PowerRuntime>();
    public SuperHeroController Hero { get; private set; }
    public PlayerProgression Progression { get; private set; }
    public PowerRuntime Flight { get; private set; }
    public PowerRuntime Strength { get; private set; }
    public PowerRuntime Selected { get; private set; }
    public float Energy { get; private set; }
    public string Message = "Ready";
    public event System.Action<PowerDefinition> Activated;
    public Rigidbody HeldBody { get; private set; }
    public ForgeCatalog Forge {get;private set;}
    public PowerDefinition EquippedA {get;private set;}
    public PowerDefinition EquippedB {get;private set;}
    public PowerSynergyDefinition Synergy {get;private set;}
    public HeroDefinition HeroDefinition {get;private set;}
    public SynergyRunner SynergyRunner {get;private set;}
    public bool IsEquipped(PowerDefinition definition)=>definition!=null&&(Forge==null||definition==EquippedA||definition==EquippedB);
    public Vector3 AimOrigin
    {
        get
        {
            var camera=Camera.main;var rig=camera!=null?camera.GetComponent<ThirdPersonCamera>():null;
            return rig!=null&&rig.isActiveAndEnabled&&rig.FirstPerson?camera.transform.position:
                transform.position+Vector3.up*(Selected?.Definition.OriginHeight??1f);
        }
    }
    public Vector3 AimDirection
    {
        get => (AimPoint(Selected == null ? Strength.Definition.Range : Stats(Selected).Range, HeldBody) - AimOrigin).normalized;
    }
    // Shared crosshair convergence for Fire and throws; a held prop must not become its own aim target.
    public Vector3 AimPoint(float range, Rigidbody ignore = null)
    {
        var camera = UnityEngine.Camera.main;
        if (camera == null) return AimOrigin + transform.forward * range;
        // Aim from the shoulder toward the camera crosshair, excluding the player.
        Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
        // The power's reach is measured from the hero, not from the camera behind it, and anything between the
        // camera and the hero (a lamppost behind you) is never the aim target.
        float near = Mathf.Max(0f, Vector3.Dot(AimOrigin - ray.origin, ray.direction));
        float distance = near + range;
        Vector3 target = ray.GetPoint(distance);
        foreach (var hit in Physics.RaycastAll(ray, distance))
            if (!hit.collider.isTrigger && hit.collider.transform.root != transform && (ignore == null || hit.rigidbody != ignore) && hit.distance > near && hit.distance < distance) { distance = hit.distance; target = hit.point; }
        return target;
    }
    PowerRuntime heldPower; bool heldGravity; float heldUntil;
    MovementSettings config;
    public void Initialize(SuperHeroController hero, PlayerProgression progression, PowerDefinition[] definitions, MovementSettings settings)
    {
        Hero = hero; Progression = progression; config = settings; Energy = config.Energy;
        foreach (var definition in definitions)
        {
            if (definition.Effect == null) continue;
            var runtime = new PowerRuntime(definition); Powers.Add(runtime);
            if (definition.Effect.IsFlight && Flight == null) Flight = runtime;
            if (definition.Id == "strength") Strength = runtime;
        }
        Forge=Resources.Load<ForgeCatalog>("ForgeCatalog");
        EquippedA=progression.EquippedA;EquippedB=progression.EquippedB;HeroDefinition=progression.SelectedHero;
        Synergy=Forge?.Resolve(EquippedA,EquippedB);
        Selected = Powers.Find(p=>IsEquipped(p.Definition)&&!p.Definition.Effect.IsFlight)??Strength; progression.Changed += Refresh;
        Refresh();
        if(Forge!=null){SynergyRunner=gameObject.AddComponent<SynergyRunner>();SynergyRunner.Initialize(this);}
    }
    public PowerStats Stats(PowerRuntime power) => power.Definition.GetStats(Mathf.Max(0, Progression.Tier(power.Definition)));
    void Refresh()
    {
        foreach (var p in Powers) { var stats = Stats(p); p.Charges = Mathf.Min(p.Charges, stats.Charges); p.Fuel = Mathf.Min(p.Fuel, stats.Duration); }
    }
    public bool Select(PowerRuntime power)
    {
        if (power==null||!Powers.Contains(power)||!IsEquipped(power.Definition)||!Progression.Owns(power.Definition)) return false;
        Release(false); Selected = power; Message = power.Definition.Description; return true;
    }
    public void Tick(float dt, bool grounded)
    {
        Energy = Mathf.Min(config.Energy, Energy + config.EnergyRecharge * dt);
        foreach (var power in Powers)
        {
            var s = Stats(power); power.Cooldown = Mathf.Max(0f, power.Cooldown - dt);
            if (grounded && power.Definition.Effect.IsFlight) power.Fuel = Mathf.Min(s.Duration, power.Fuel + power.Definition.GroundRecharge * dt);
            if (power.Charges >= s.Charges) { power.ChargeTimer = 0f; continue; }
            power.ChargeTimer += dt;
            while (power.ChargeTimer >= Mathf.Max(.001f, power.Definition.ChargeRecharge) && power.Charges < s.Charges)
            { power.Charges++; power.ChargeTimer -= Mathf.Max(.001f, power.Definition.ChargeRecharge); }
        }
    }
    public bool ConsumeFlight(float dt)
    {
        if (dt<=0||Flight == null || !IsEquipped(Flight.Definition)||!Progression.Owns(Flight.Definition) || Flight.Fuel <= 0f) return false;
        Flight.Fuel = Mathf.Max(0f, Flight.Fuel - dt * Flight.Definition.ResourceCost); return true;
    }
    public bool Use(PowerRuntime power)
    {
        if(power==null||!Powers.Contains(power)||!IsEquipped(power.Definition)){Message="Power not equipped";return false;}
        if(SynergyRunner!=null&&SynergyRunner.Busy)
        {
            if(power.Definition.Effect is TelekinesisEffect && SynergyRunner.Definition.Effect is OrbitThrowEffect && SynergyRunner.HeldCount>0)
            { SynergyRunner.RequestRelease(); Message="Throw orbit"; return true; }
            Message="Synergy in progress";return false;
        }
        if (power == null || !Progression.Owns(power.Definition)) { Message = "Power locked"; return false; }
        if (HeldBody != null && power == heldPower) { Release(true); return true; } // Hurl is the second half of the paid grab.
        if (power.Cooldown > 0f) { Message = "Blocked: cooldown"; return false; }
        if (power.Charges <= 0) { Message = "Blocked: 0 charges"; return false; }
        if (Energy < power.Definition.ResourceCost) { Message = "Blocked: energy"; return false; }
        Message = "No valid target";
        if (!power.Definition.Effect.Execute(this, power)) return false;
        power.Charges--; power.Cooldown = Stats(power).Cooldown; power.ChargeTimer = 0f;
        Energy -= power.Definition.ResourceCost; Message = power.Definition.DisplayName + " activated";
        Activated?.Invoke(power.Definition);
        WorldSession.Instance?.Alarm(transform.position);
        return true;
    }
    public bool FindTarget(float range, out RaycastHit result)
    {
        result = default; float closest = range;
        foreach (var hit in Physics.RaycastAll(AimOrigin, AimDirection, range))
            if (!hit.collider.isTrigger && hit.collider.transform.root != transform && hit.distance < closest) { result = hit; closest = hit.distance; }
        return result.collider != null;
    }
    public void Grab(Rigidbody body, PowerRuntime power)
    {
        Release(false); HeldBody = body; heldPower = power; heldGravity = body.useGravity; body.useGravity = false;
        heldUntil = Time.time + Stats(power).Duration;
    }
    public void Release(bool hurl)
    {
        if (HeldBody == null) return;
        HeldBody.useGravity = heldGravity;
        if (hurl)
        {
            ((TelekinesisEffect)heldPower.Definition.Effect).Throw(this, heldPower, HeldBody);
            Message = "Thrown";
        }
        HeldBody = null; heldPower = null;
    }
    void FixedUpdate()
    {
        if (HeldBody == null) return;
        if (Time.time > heldUntil) { Release(false); return; }
        var d = heldPower.Definition;
        Vector3 target = AimOrigin + AimDirection * d.HoldDistance;
        HeldBody.AddForce((target - HeldBody.position) * d.HoldSpring - HeldBody.linearVelocity * d.HoldDamping, ForceMode.Acceleration);
    }
    void OnDisable() { Release(false); }
    void OnDestroy() { if (Progression != null) Progression.Changed -= Refresh; }
}
public sealed class ThrownProp : MonoBehaviour
{
    PowerUser owner; float damage, expiry; bool spent;
    TelekinesisEffect throwSettings;
    public Vector3 LastContact { get; private set; }
    public float LastDamage { get; private set; }
    public float LastImpulse { get; private set; }
    public Vector3 LaunchImpulse { get; private set; }
    public bool Spent => spent;
    public void Initialize(PowerUser user, float value, float duration, TelekinesisEffect settings = null, Vector3 launchImpulse = default)
    { owner = user; damage = value; expiry = Time.time + duration; spent = false; throwSettings = settings; LastDamage=LastImpulse=0; LaunchImpulse=launchImpulse; }
    void OnCollisionEnter(Collision other)
    {
        if (spent || Time.time > expiry || owner == null || other.transform.root == owner.transform) return;
        var npc = other.collider.GetComponentInParent<CityNpc>(); if (npc == null) return;
        if(npc.Dead)return;
        LastContact=other.GetContact(0).point;
        if(throwSettings!=null)
        {
            LastImpulse=Mathf.Min(throwSettings.MaxImpactImpulse, GetComponent<Rigidbody>().mass * other.relativeVelocity.magnitude * throwSettings.MomentumTransfer);
            Vector3 direction=(npc.transform.position-transform.position).normalized;
            // Hosted on the struck actor: a breakable projectile may destroy itself in this very collision.
            npc.StartCoroutine(LaunchAfterContact(npc,(direction+Vector3.up*throwSettings.ImpactLift).normalized*LastImpulse,
                GetComponentsInChildren<Collider>(),throwSettings.ImpactSeparationSeconds));
            FeelDirector.Impact(LastContact,LastImpulse,damage,1);
        }
        LastDamage=damage;npc.Damage(damage, owner); spent = true;
    }
    static System.Collections.IEnumerator LaunchAfterContact(CityNpc npc,Vector3 impulse,Collider[] projectileColliders,float separation)
    {
        // Let this collision's callbacks finish before enabling suspension: otherwise its OnCollisionEnter treats
        // the initiating projectile as a landing and immediately cancels the launch.
        var targetCollider=npc.GetComponent<Collider>();
        var changed=new List<Collider>();
        foreach(var c in projectileColliders)
            if(c!=null&&!Physics.GetIgnoreCollision(c,targetCollider)){Physics.IgnoreCollision(c,targetCollider,true);changed.Add(c);}
        yield return new WaitForFixedUpdate();
        if(npc==null)yield break;
        var suspension=npc.GetComponent<SynergySuspension>();
        if(suspension==null)suspension=npc.gameObject.AddComponent<SynergySuspension>();
        var target=suspension.Begin(npc);target.useGravity=true;suspension.Release();
        target.AddForce(impulse,ForceMode.Impulse);
        CombatImpact.PreserveDeathLaunch(npc);
        yield return new WaitForSeconds(separation);
        foreach(var c in changed)if(c!=null&&targetCollider!=null)Physics.IgnoreCollision(c,targetCollider,false);
    }
}
