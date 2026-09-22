using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class SuperHeroController : MonoBehaviour
{
    CharacterController controller;
    Camera view;
    float verticalVelocity;
    float airbornePeakHeight;
    PowerUser powers;
    MovementSettings movement;
    public float FlightFuel => powers?.Flight?.Fuel ?? 0f;
    public int Charges => powers?.Strength?.Charges ?? 0;
    public float Cooldown => powers?.Strength?.Cooldown ?? 0f;
    public string LastPunchResult { get; private set; } = "Ready";
    public int LastAffectedBodies { get; private set; }
    public float LastForce { get; private set; }
    public HeroPresentationState PresentationState { get; private set; }
    public event System.Action PunchStarted;
    public event System.Action Jumped;
    public event System.Action PunchImpacted;
    public float PunchWindupSeconds { get; set; }
    public float LastImpactTime { get; private set; }=-1;
    public int LastImpactFrame { get; private set; }=-1;
    public event System.Action<float> Landed;
    public event System.Action BackflipStarted;
    public event System.Action HurricaneKickStarted;
    public event System.Action HurricaneKickImpacted;
    public float KickWindupSeconds { get; set; }
    public bool BackflipActive => Time.time < backflipUntil;
    public float BackflipCooldown => Mathf.Max(0f, backflipReadyAt - Time.time);
    public int BackflipCount { get; private set; }
    public string LastBackflipResult { get; private set; } = "Ready";
    public string LastKickResult { get; private set; } = "Ready";
    public float LastKickForce { get; private set; }
    public int LastKickAffectedBodies { get; private set; }
    public float LastKickImpactTime { get; private set; }=-1;
    public int LastKickImpactFrame { get; private set; }=-1;
    float backflipUntil=-1f, backflipReadyAt=-1f;
    bool hurricaneKickPending;

    void Awake() { controller = GetComponent<CharacterController>(); view = Camera.main; airbornePeakHeight = transform.position.y; }
    public void Initialize(PowerUser user, MovementSettings settings) { powers = user; movement = settings; }
    public void ResetMotion() { verticalVelocity = 0f; airbornePeakHeight = transform.position.y; }
    void Update()
    {
        if (powers == null) return;
        if (view == null) view = Camera.main;
        if (view == null) return;
        powers.Tick(Time.deltaTime, controller.isGrounded);
        bool acceptsInput = !WorldSession.Instance.MenuOpen && !WorldSession.Instance.PlayerDead;
        Vector3 forward = Vector3.Scale(view.transform.forward, new Vector3(1, 0, 1)).normalized;
        Vector3 right = Vector3.Scale(view.transform.right, new Vector3(1,0,1)).normalized;
        Vector3 move = acceptsInput ? (forward * Input.GetAxisRaw("Vertical") + right * Input.GetAxisRaw("Horizontal")).normalized : Vector3.zero;
        bool flying = acceptsInput && !controller.isGrounded && Input.GetKey(KeyCode.F) && powers.ConsumeFlight(Time.deltaTime);
        if (move.sqrMagnitude > 0f) transform.forward = Vector3.Slerp(transform.forward, move, Time.deltaTime * movement.TurnResponse);
        float speed = Input.GetKey(KeyCode.LeftShift) ? movement.RunSpeed : movement.WalkSpeed;
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -movement.GroundStickSpeed;
        if (acceptsInput && controller.isGrounded && Input.GetButtonDown("Jump")) TryJump();
        if (flying)
        {
            verticalVelocity = Mathf.MoveTowards(verticalVelocity, Input.GetKey(KeyCode.Space) ? movement.FlightLift : 0f, movement.FlightLift * movement.FlightResponse * Time.deltaTime);
            move *= movement.FlightForwardBoost;
        }
        else verticalVelocity -= movement.Gravity * Time.deltaTime;
        bool wasGrounded = controller.isGrounded;
        airbornePeakHeight = wasGrounded ? transform.position.y : Mathf.Max(airbornePeakHeight, transform.position.y);
        float impactSpeed = Mathf.Max(0f, -verticalVelocity);
        controller.Move((move * speed + Vector3.up * verticalVelocity) * Time.deltaTime);
        PresentationState = new HeroPresentationState(transform.InverseTransformDirection(controller.velocity),
            controller.isGrounded, flying && !controller.isGrounded);
        // CharacterController can alternate ground contact for sub-skin-width moves.
        // Report actual falls, not those resting contact transitions; this does not alter motion.
        if (!wasGrounded && controller.isGrounded && airbornePeakHeight - transform.position.y > controller.skinWidth)
            Landed?.Invoke(impactSpeed);
        if (!acceptsInput) return;
        if (Input.GetKeyDown(KeyCode.E)) TryPunch();
        if (Input.GetKeyDown(KeyCode.Q)) TryBackflip();
        if (Input.GetMouseButtonDown(0)) powers.Use(powers.Selected);
        if (Input.GetMouseButtonDown(1)) TryHurricaneKick();
        for (int i = 0; i < Mathf.Min(9, powers.Powers.Count); i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) powers.Select(powers.Powers[i]);
    }
    public void DebugSimulateFlight(float seconds) { powers.ConsumeFlight(seconds); }
    public void DebugSimulateGround(float seconds) { powers.Tick(seconds, true); }
    public bool TryPunch()
    {
        bool fired = powers.Use(powers.Strength);
        if (!fired) LastPunchResult = powers.Message;
        return fired;
    }
    public void PerformPunch(PowerDefinition definition, PowerStats stats)
    {
        // A pending hurricane kick swaps only the executed gesture; payment, charges and cooldown
        // still come from the Super Strength runtime that already accepted the activation.
        if (hurricaneKickPending) { hurricaneKickPending=false; PerformHurricaneKick(stats); return; }
        PunchStarted?.Invoke();
        if(PunchWindupSeconds>0)StartCoroutine(PunchAfterWindup(definition,stats,PunchWindupSeconds));
        else ApplyPunch(definition,stats);
    }
    System.Collections.IEnumerator PunchAfterWindup(PowerDefinition definition,PowerStats stats,float delay)
    {
        yield return new WaitForSeconds(delay);
        if(WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&(WorldSession.Instance.Mode==null||!WorldSession.Instance.Mode.Ended))ApplyPunch(definition,stats);
    }
    void ApplyPunch(PowerDefinition definition,PowerStats stats)
    {
        LastForce = stats.Force;
        LastAffectedBodies = CombatImpact.Blast(powers, transform.position + Vector3.up * definition.OriginHeight + transform.forward * definition.OriginOffset,
            stats.Radius, stats.Force, stats.Damage, definition.UpwardForce);
        LastPunchResult = $"PUNCH: {LastAffectedBodies} bodies hit @ {LastForce:0} N·s";
        LastImpactTime=Time.time;LastImpactFrame=Time.frameCount;PunchImpacted?.Invoke();
    }
    // Backflip: a short backward dash plus a small hop, using the existing CharacterController and
    // gravity arc. Repositioning only: no invincibility window, i-frames or dodge state is added.
    public bool TryBackflip()
    {
        if (powers == null || controller == null) { LastBackflipResult = "Blocked: no hero"; return false; }
        if (Time.time < backflipUntil) { LastBackflipResult = "Blocked: already flipping"; return false; }
        if (!controller.isGrounded) { LastBackflipResult = "Blocked: airborne"; return false; }
        if (Time.time < backflipReadyAt) { LastBackflipResult = "Blocked: cooldown"; return false; }
        backflipUntil = Time.time + HeroAbilityTuning.BackflipSeconds;
        backflipReadyAt = Time.time + HeroAbilityTuning.BackflipCooldown;
        BackflipCount++;
        verticalVelocity = HeroAbilityTuning.BackflipHopSpeed;
        LastBackflipResult = $"BACKFLIP: {HeroAbilityTuning.BackflipDistance:0.0} m backward in {HeroAbilityTuning.BackflipSeconds:0.00}s";
        BackflipStarted?.Invoke();
        StartCoroutine(BackflipDash());
        return true;
    }
    System.Collections.IEnumerator BackflipDash()
    {
        float speed = HeroAbilityTuning.BackflipDistance / Mathf.Max(.01f, HeroAbilityTuning.BackflipSeconds);
        while (Time.time < backflipUntil)
        {
            controller.Move(-transform.forward * (speed * Time.deltaTime));
            yield return null;
        }
    }
    // Hurricane Kick: the secondary melee option next to the punch. It is gated by the existing
    // Super Strength runtime (same charge pool, cooldown and energy cost) and differs only in the
    // gesture, force, damage and radius it applies.
    public bool TryHurricaneKick()
    {
        if (powers == null || powers.Strength == null) { LastKickResult = "Power locked"; return false; }
        hurricaneKickPending = true;
        bool accepted = powers.Use(powers.Strength);
        // PerformPunch consumes the flag synchronously when the gesture is actually dispatched.
        bool gestured = accepted && !hurricaneKickPending;
        hurricaneKickPending = false;
        if (!gestured) LastKickResult = accepted ? "Blocked: no gesture" : powers.Message;
        return gestured;
    }
    void PerformHurricaneKick(PowerStats stats)
    {
        HurricaneKickStarted?.Invoke();
        if (KickWindupSeconds > 0) StartCoroutine(HurricaneKickAfterWindup(stats, KickWindupSeconds));
        else ApplyHurricaneKick(stats);
    }
    System.Collections.IEnumerator HurricaneKickAfterWindup(PowerStats stats, float delay)
    {
        yield return new WaitForSeconds(delay);
        if(WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&(WorldSession.Instance.Mode==null||!WorldSession.Instance.Mode.Ended))ApplyHurricaneKick(stats);
    }
    void ApplyHurricaneKick(PowerStats stats)
    {
        LastKickForce = stats.Force * HeroAbilityTuning.KickForceMultiplier;
        LastKickAffectedBodies = CombatImpact.Blast(powers, transform.position + Vector3.up * HeroAbilityTuning.KickOriginHeight + transform.forward * HeroAbilityTuning.KickOriginOffset,
            HeroAbilityTuning.KickRadius, LastKickForce, stats.Damage * HeroAbilityTuning.KickDamageMultiplier, HeroAbilityTuning.KickUpwardForce);
        LastKickResult = $"HURRICANE KICK: {LastKickAffectedBodies} bodies hit @ {LastKickForce:0} N\u00b7s";
        LastKickImpactTime=Time.time;LastKickImpactFrame=Time.frameCount;HurricaneKickImpacted?.Invoke();
    }
    public bool TryJump()
    {
        if(!controller.isGrounded)return false;
        verticalVelocity=movement.JumpSpeed;Jumped?.Invoke();return true;
    }
    // Used by deterministic test and the in-game verification harness.
    public void DebugSetResources(float fuel, int newCharges, float newCooldown = 0f)
    { powers.Flight.Fuel = fuel; powers.Strength.Charges = newCharges; powers.Strength.Cooldown = newCooldown; }
}
