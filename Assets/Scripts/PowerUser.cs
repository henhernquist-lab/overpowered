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
        get
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null) return transform.forward;
            // Aim from the shoulder toward the camera crosshair, excluding the player.
            Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            float distance = Selected == null ? Strength.Definition.Range : Stats(Selected).Range;
            Vector3 target = ray.GetPoint(distance);
            foreach (var hit in Physics.RaycastAll(ray, distance))
                if (!hit.collider.isTrigger && hit.collider.transform.root != transform && hit.distance < distance) { distance = hit.distance; target = hit.point; }
            return (target - AimOrigin).normalized;
        }
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
        if(SynergyRunner!=null&&SynergyRunner.Busy){Message="Synergy in progress";return false;}
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
            HeldBody.AddForce(AimDirection * Stats(heldPower).Force, ForceMode.Impulse);
            var impact = HeldBody.GetComponent<ThrownProp>();
            if (impact == null) impact = HeldBody.gameObject.AddComponent<ThrownProp>();
            impact.Initialize(this, Stats(heldPower).Damage, Stats(heldPower).Duration);
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
    public void Initialize(PowerUser user, float value, float duration) { owner = user; damage = value; expiry = Time.time + duration; spent = false; }
    void OnCollisionEnter(Collision other)
    {
        if (spent || Time.time > expiry || other.transform.root == owner.transform) return;
        var npc = other.collider.GetComponentInParent<CityNpc>(); if (npc == null) return;
        npc.Damage(damage, owner); spent = true;
    }
}
