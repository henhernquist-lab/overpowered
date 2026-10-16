using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class SuperHeroController : MonoBehaviour
{
    CharacterController controller;
    Camera view;
    float verticalVelocity;
    float flightBoost = 1f;
    Vector3 wallJumpVelocity;
    float airbornePeakHeight;
    float airborneStartTime;
    public float LastLandingDrop { get; private set; }
    public float LastLandingAirTime { get; private set; }
    public float LastLandingImpactSpeed { get; private set; }
    bool airJumpUsed, jumpLaunched;
    public bool AirJumpAvailable => !airJumpUsed;
    public bool LastJumpWasAir { get; private set; }
    bool TraversalInputAllowed => powers != null && Time.timeScale > 0f && WorldSession.Instance != null &&
        !WorldSession.Instance.MenuOpen && !WorldSession.Instance.PlayerDead &&
        (WorldSession.Instance.Mode == null || !WorldSession.Instance.Mode.Ended);
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
    public event System.Action ClimbReleased;
    public event System.Action PunchImpacted;
    public float PunchWindupSeconds { get; set; }
    public float LastImpactTime { get; private set; }=-1;
    public int LastImpactFrame { get; private set; }=-1;
    public event System.Action<float> Landed;
    public event System.Action BackflipStarted;
    public event System.Action HurricaneKickStarted;
    public event System.Action HurricaneKickImpacted;
    public event System.Action MarteloKickStarted;
    public event System.Action MarteloKickImpacted;
    public float KickWindupSeconds { get; set; }
    public float ComboKickWindupSeconds { get; set; }
    public bool BackflipActive => Time.time < backflipUntil;
    public float BackflipCooldown => Mathf.Max(0f, backflipReadyAt - Time.time);
    public int BackflipCount { get; private set; }
    public string LastBackflipResult { get; private set; } = "Ready";
    public string LastKickResult { get; private set; } = "Ready";
    public float LastKickForce { get; private set; }
    public int LastKickAffectedBodies { get; private set; }
    public float LastKickImpactTime { get; private set; }=-1;
    public int LastKickImpactFrame { get; private set; }=-1;
    public float LastComboKickForce { get; private set; }
    public int LastComboKickAffectedBodies { get; private set; }
    public float LastComboKickImpactTime { get; private set; }=-1;
    public int LastComboKickImpactFrame { get; private set; }=-1;
    public string LastComboKickResult { get; private set; }="Ready";
    float backflipUntil=-1f, backflipReadyAt=-1f;
    bool kickPending, comboKickPending;
    float basicReadyAt;
    public bool AbilityDriving=>powers?.SynergyRunner!=null&&powers.SynergyRunner.DrivesMotion;
    public CollisionFlags MoveAbility(Vector3 velocity,bool flying)
    {
        EndClimb();
        flightBoost=1f;
        verticalVelocity=0;
        bool grounded=controller.isGrounded;
        if(grounded){airbornePeakHeight=transform.position.y;airborneStartTime=Time.time;}
        else airbornePeakHeight=Mathf.Max(airbornePeakHeight,transform.position.y);
        var flags=controller.Move(velocity*Time.deltaTime);
        PresentationState=new HeroPresentationState(transform.InverseTransformDirection(controller.velocity),controller.isGrounded,flying&&!controller.isGrounded);
        if(!grounded&&(flags&CollisionFlags.Below)!=0&&velocity.y<0)ReportLanding(-velocity.y);
        return flags;
    }
    void ReportLanding(float impactSpeed)
    {
        LastLandingImpactSpeed=impactSpeed;
        LastLandingDrop=Mathf.Max(0,airbornePeakHeight-transform.position.y);
        LastLandingAirTime=Mathf.Max(0,Time.time-airborneStartTime);
        Landed?.Invoke(impactSpeed);
    }
    PowerStats BasicStats()=>new PowerStats{Damage=powers.Forge.BasicDamage*powers.HeroStats.MeleeDamage,Force=powers.Forge.BasicForce*powers.HeroStats.MeleeDamage,Radius=powers.Forge.BasicRadius};
    /// Verification only (batch mode has no keyboard): used as the camera-relative movement input when non-zero.
    public Vector3 ScriptedMove; public bool ScriptedRun;
    // Verification input follows the exact same flight resource and motion path as keyboard input.
    public bool ScriptedFlight, ScriptedAscend, ScriptedSecondaryPress;
    /// Verification input for the wall-local movement path; normal play uses WASD.
    public bool ScriptedClimbOverride, ScriptedClimbJump, ScriptedClimbCancel;
    public Vector2 ScriptedClimbInput;
    bool BasicMelee(bool kick,bool combo=false)
    {
        if(powers.OffensiveActionsBlocked||Time.time<basicReadyAt||powers.SynergyRunner.Busy)return false;
        basicReadyAt=Time.time+powers.Forge.BasicCooldown;
        if(kick)PerformKick(BasicStats(),combo);else PerformPunch(powers.Strength.Definition,BasicStats());
        return true;
    }

    void Awake() { controller = GetComponent<CharacterController>(); view = Camera.main; airbornePeakHeight = transform.position.y; airborneStartTime=Time.time; }
    public void Initialize(PowerUser user, MovementSettings settings) { powers = user; movement = settings; }
    public void ResetMotion() { flightBoost = 1f; verticalVelocity = 0f; wallJumpVelocity=Vector3.zero; airbornePeakHeight = transform.position.y; airborneStartTime=Time.time; airJumpUsed = jumpLaunched = false; LastJumpWasAir=false; Mantling = mantleUsed = false; EndClimb();climbRelatchAt=0; comboStage=0;comboExpires=-1f; }
    void Update()
    {
        if (powers == null) return;
        if (view == null) view = Camera.main;
        if (view == null) return;
        powers.Tick(Time.deltaTime, controller.isGrounded);
        if(powers.OffensiveActionsBlocked||WorldSession.Instance.PlayerDead||WorldSession.Instance.Mode?.Ended==true){comboStage=0;comboExpires=-1f;}
        // Channeled powers (Laser Eyes) run while the fire button stays held; anything that takes input away ends them.
        powers.Channel(!Climbing && !AbilityDriving && !WorldSession.Instance.MenuOpen && !WorldSession.Instance.PlayerDead &&
            WorldSession.Instance.Mode?.Paused!=true && WorldSession.Instance.Mode?.Ended!=true &&
            (Input.GetMouseButton(0) || powers.ScriptedHold), Time.deltaTime);
        // The backflip may be queued during a combat hit pause; its dash coroutine holds position until time resumes.
        if(TimeArbiter.HitPaused&&!TimeArbiter.MenuPaused&&Input.GetKeyDown(KeyCode.Q))TryBackflip();
        if(AbilityDriving){Mantling=false;EndClimb();return;}
        if(Mantling){MoveMantle();return;}
        bool climbFlightFrame=false;
        if(Climbing)
        {
            if(TraversalInputAllowed && !controller.isGrounded && (Input.GetKey(KeyCode.F)||ScriptedFlight) &&
                powers.EquippedSecondary!=null && powers.EquippedSecondary.Effect.IsFlight && powers.ConsumeFlight(Time.deltaTime))
            {EndClimb();climbFlightFrame=true;}
            else
            {
                if(TraversalInputAllowed && (Input.GetKeyDown(KeyCode.F)||ScriptedSecondaryPress))
                {powers.UseSecondary();ScriptedSecondaryPress=false;if(Dashing)EndClimb();}
                if(Climbing){MoveClimb();return;}
            }
        }
        bool acceptsInput = TraversalInputAllowed;
        Vector3 forward = Vector3.Scale(view.transform.forward, new Vector3(1, 0, 1)).normalized;
        Vector3 right = Vector3.Scale(view.transform.right, new Vector3(1,0,1)).normalized;
        Vector3 move = acceptsInput ? (forward * Input.GetAxisRaw("Vertical") + right * Input.GetAxisRaw("Horizontal")).normalized : Vector3.zero;
        if (acceptsInput && ScriptedMove.sqrMagnitude > 0f) move = Vector3.ProjectOnPlane(ScriptedMove, Vector3.up).normalized;
        MoveInput = move;
        bool flying = acceptsInput && !controller.isGrounded && (Input.GetKey(KeyCode.F) || ScriptedFlight) &&
            (climbFlightFrame || powers.ConsumeFlight(Time.deltaTime));
        bool secondaryPressed=Input.GetKeyDown(KeyCode.F)||ScriptedSecondaryPress;
        ScriptedSecondaryPress=false;
        if (acceptsInput && secondaryPressed && powers.EquippedSecondary != null && !powers.EquippedSecondary.Effect.IsFlight)
            powers.UseSecondary();
        if (move.sqrMagnitude > 0f) transform.forward = Vector3.Slerp(transform.forward, move, Time.deltaTime * movement.TurnResponse);
        float speed = (Input.GetKey(KeyCode.LeftShift) || ScriptedRun ? movement.RunSpeed : movement.WalkSpeed) * powers.HeroStats.MoveSpeed;
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -movement.GroundStickSpeed;
        // While Flight is active, Space retains its ascend role rather than spending the universal air jump.
        if (acceptsInput && !flying && Input.GetButtonDown("Jump")) TryJump();
        if (!flying && move.sqrMagnitude > .1f && TryMantle(move)) { MoveMantle(); return; }
        if (!flying && move.sqrMagnitude > .1f && TryClimb(move)) { MoveClimb(); return; }
        if (flying)
        {
            verticalVelocity = Mathf.MoveTowards(verticalVelocity, (Input.GetKey(KeyCode.Space) || ScriptedAscend) ? movement.FlightLift : 0f, movement.FlightLift * movement.FlightResponse * Time.deltaTime);
            flightBoost = Mathf.MoveTowards(flightBoost, movement.FlightForwardBoost, movement.FlightBoostAcceleration * Time.deltaTime);
            move *= flightBoost;
        }
        else { flightBoost = 1f; verticalVelocity -= movement.Gravity * Time.deltaTime; }
        bool wasGrounded = controller.isGrounded;
        if(wasGrounded){airbornePeakHeight=transform.position.y;airborneStartTime=Time.time;}
        else airbornePeakHeight=Mathf.Max(airbornePeakHeight,transform.position.y);
        float impactSpeed = Mathf.Max(0f, -verticalVelocity);
        // A frozen frame (hit pause / timeScale 0) moves nothing; skipping the zero Move keeps the CharacterController's
        // ground contact, so jump/backflip input stays valid through a hit pause.
        if (Time.deltaTime > 0f)
        {
            var flags = controller.Move((move * speed + wallJumpVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
            wallJumpVelocity=Vector3.MoveTowards(wallJumpVelocity,Vector3.zero,movement.ClimbDetachPushSpeed /
                Mathf.Max(.01f,movement.ClimbRelatchDelay)*Time.deltaTime);
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            // Reset only on a downward move that actually contacts support, never a stale isGrounded
            // immediately after launch, a pause, or an upward contact with a ceiling.
            if ((flags & CollisionFlags.Below) != 0 && verticalVelocity <= 0f)
                airJumpUsed = jumpLaunched = mantleUsed = false;
        }
        PresentationState = new HeroPresentationState(transform.InverseTransformDirection(controller.velocity),
            controller.isGrounded, flying && !controller.isGrounded);
        // CharacterController can alternate ground contact for sub-skin-width moves.
        // Report actual falls, not those resting contact transitions; this does not alter motion.
        if (!wasGrounded && controller.isGrounded && airbornePeakHeight - transform.position.y > controller.skinWidth)
            ReportLanding(impactSpeed);
        if (!acceptsInput) { chargeStart = -1f; return; }
        // E: airborne (high enough) = ground pound; otherwise press starts a charge, release decides tap (combo) or heavy.
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (!controller.isGrounded && HeightAboveGround() >= Melee.PoundMinHeight) TryGroundPound();
            else chargeStart = Time.time;
        }
        if (chargeStart >= 0f && !Input.GetKey(KeyCode.E))
        {
            float held = Time.time - chargeStart; chargeStart = -1f;
            if (held < Melee.HeavyTapThreshold) TryComboAttack(); else TryHeavyAttack(held);
        }
        if (Input.GetKeyDown(KeyCode.Q)) TryBackflip();
        if (Input.GetMouseButtonDown(0)) powers.Use(powers.Selected);
        if (Input.GetMouseButtonDown(1)) TryHurricaneKick();
        for (int i = 1; i <= 9; i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + i))) { var slot = powers.PowerForSlot(i); if (slot != null) powers.Select(slot); }
    }
    public void DebugSimulateFlight(float seconds) { powers.ConsumeFlight(seconds); }
    public void DebugSimulateGround(float seconds) { powers.Tick(seconds, true); }
    /// Per-power stats id for melee hits: Super Strength when it drives the melee, "melee" for basic melee without it.
    string MeleeCredit => powers.Strength != null && (powers.Forge == null || powers.IsEquipped(powers.Strength.Definition)) ? powers.Strength.Definition.Id : "melee";
    public bool TryPunch()
    {
        if(powers.OffensiveActionsBlocked)return false;
        if(powers.Forge!=null&&!powers.IsEquipped(powers.Strength.Definition))return BasicMelee(false);
        bool fired = powers.Use(powers.Strength);
        if (!fired) LastPunchResult = powers.Message;
        return fired;
    }
    public void PerformPunch(PowerDefinition definition, PowerStats stats)
    {
        // A pending ground pound / hurricane kick swaps only the executed gesture; payment, charges and cooldown
        // still come from the Super Strength runtime (or basic melee cooldown) that already accepted the activation.
        if (poundPending) { poundPending=false; stats=TakeModifiers(stats,out _); StartCoroutine(GroundPound(stats)); return; }
        if (kickPending) { kickPending=false; PerformKick(stats,comboKickPending); return; }
        stats=TakeModifiers(stats,out bool pause);
        PunchStarted?.Invoke();
        if(PunchWindupSeconds>0)StartCoroutine(PunchAfterWindup(definition,stats,PunchWindupSeconds,pause));
        else ApplyPunch(definition,stats,pause);
    }
    public void PresentPunch(){PunchStarted?.Invoke();}
    System.Collections.IEnumerator PunchAfterWindup(PowerDefinition definition,PowerStats stats,float delay,bool pause)
    {
        int epoch=offenseEpoch;
        yield return new WaitForSeconds(delay);
        if(epoch==offenseEpoch&&!powers.OffensiveActionsBlocked&&WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&(WorldSession.Instance.Mode==null||!WorldSession.Instance.Mode.Ended))ApplyPunch(definition,stats,pause);
    }
    void ApplyPunch(PowerDefinition definition,PowerStats stats,bool pause=false)
    {
        LastForce = stats.Force;
        using (powers.Credit(MeleeCredit)) LastAffectedBodies = CombatImpact.Blast(powers, transform.position + Vector3.up * definition.OriginHeight + transform.forward * definition.OriginOffset,
            stats.Radius, stats.Force, stats.Damage, definition.UpwardForce, melee:true);
        LastPunchResult = $"PUNCH: {LastAffectedBodies} bodies hit @ {LastForce:0} N·s";
        LastImpactTime=Time.time;LastImpactFrame=Time.frameCount;PunchImpacted?.Invoke();
        if(pause)FinisherPause();
    }
    // Backflip: a short backward dash plus a small hop, using the existing CharacterController and
    // gravity arc. Repositioning only: no invincibility window, i-frames or dodge state is added.
    public bool TryBackflip()
    {
        bool hitPauseInput=TimeArbiter.HitPaused&&!TimeArbiter.MenuPaused&&powers!=null&&WorldSession.Instance!=null&&
            !WorldSession.Instance.MenuOpen&&!WorldSession.Instance.PlayerDead&&WorldSession.Instance.Mode?.Paused!=true&&
            WorldSession.Instance.Mode?.Ended!=true;
        if(!(TraversalInputAllowed||hitPauseInput)||Mantling||Climbing){LastBackflipResult="Blocked: traversal input";return false;}
        if(powers?.SynergyRunner!=null&&powers.SynergyRunner.Busy){LastBackflipResult="Blocked: synergy";return false;}
        if (powers == null || controller == null) { LastBackflipResult = "Blocked: no hero"; return false; }
        if (Time.time < backflipUntil) { LastBackflipResult = "Blocked: already flipping"; return false; }
        if (!controller.isGrounded) { LastBackflipResult = "Blocked: airborne"; return false; }
        if (Time.time < backflipReadyAt) { LastBackflipResult = "Blocked: cooldown"; return false; }
        backflipUntil = Time.time + HeroAbilityTuning.BackflipSeconds;
        backflipReadyAt = Time.time + HeroAbilityTuning.BackflipCooldown;
        BackflipCount++;
        verticalVelocity = HeroAbilityTuning.BackflipHopSpeed;
        jumpLaunched = true;
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
            if (Time.deltaTime > 0f) controller.Move(-transform.forward * (speed * Time.deltaTime));   // frozen frame: keep ground contact
            yield return null;
        }
    }
    // Hurricane Kick: the secondary melee option next to the punch. It is gated by the existing
    // Super Strength runtime (same charge pool, cooldown and energy cost) and differs only in the
    // gesture, force, damage and radius it applies.
    public bool TryHurricaneKick()
    {
        return TryKick(false);
    }
    // Both kicks use the existing paid Strength/basic-melee path and hit geometry. Only their
    // animation events, windup fitting, and impact records differ.
    bool TryKick(bool combo)
    {
        // RMB is its own action even when a basic E combo is waiting for a follow-up.
        if(!combo){comboStage=0;comboExpires=-1f;}
        if(powers!=null&&powers.OffensiveActionsBlocked)return false;
        if (powers == null || powers.Strength == null)
        {if(combo)LastComboKickResult="Power locked";else LastKickResult="Power locked";return false;}
        if(powers.Forge!=null&&!powers.IsEquipped(powers.Strength.Definition))return BasicMelee(true,combo);
        kickPending = true;comboKickPending=combo;
        bool accepted = powers.Use(powers.Strength);
        // PerformPunch consumes the flag synchronously when the gesture is actually dispatched.
        bool gestured = accepted && !kickPending;
        kickPending = comboKickPending = false;
        if (!gestured)
        {if(combo)LastComboKickResult=accepted?"Blocked: no gesture":powers.Message;
         else LastKickResult=accepted?"Blocked: no gesture":powers.Message;}
        return gestured;
    }
    void PerformKick(PowerStats stats,bool combo)
    {
        stats = TakeModifiers(stats, out bool pause);
        if(combo)MarteloKickStarted?.Invoke();else HurricaneKickStarted?.Invoke();
        float windup=combo?ComboKickWindupSeconds:KickWindupSeconds;
        if (windup > 0) StartCoroutine(KickAfterWindup(stats, windup, pause,combo));
        else ApplyKick(stats,pause,combo);
    }
    System.Collections.IEnumerator KickAfterWindup(PowerStats stats, float delay, bool pause,bool combo)
    {
        int epoch=offenseEpoch;
        yield return new WaitForSeconds(delay);
        if(epoch==offenseEpoch&&!powers.OffensiveActionsBlocked&&WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&(WorldSession.Instance.Mode==null||!WorldSession.Instance.Mode.Ended))ApplyKick(stats,pause,combo);
    }
    void ApplyKick(PowerStats stats,bool pause,bool combo)
    {
        stats.Radius=HeroAbilityTuning.KickRadius;
        float force=stats.Force*HeroAbilityTuning.KickForceMultiplier;
        int bodies;
        using (powers.Credit(MeleeCredit)) bodies=CombatImpact.Blast(powers, transform.position + Vector3.up * HeroAbilityTuning.KickOriginHeight + transform.forward * HeroAbilityTuning.KickOriginOffset,
            HeroAbilityTuning.KickRadius, force, stats.Damage * HeroAbilityTuning.KickDamageMultiplier, HeroAbilityTuning.KickUpwardForce, melee:true);
        if(combo)
        {
            LastComboKickForce=force;LastComboKickAffectedBodies=bodies;
            LastComboKickResult=$"MARTELO KICK: {bodies} bodies hit @ {force:0} N\u00b7s";
            LastComboKickImpactTime=Time.time;LastComboKickImpactFrame=Time.frameCount;MarteloKickImpacted?.Invoke();
        }
        else
        {
            LastKickForce=force;LastKickAffectedBodies=bodies;
            LastKickResult=$"HURRICANE KICK: {bodies} bodies hit @ {force:0} N\u00b7s";
            LastKickImpactTime=Time.time;LastKickImpactFrame=Time.frameCount;HurricaneKickImpacted?.Invoke();
        }
        if (pause) FinisherPause();
    }
    // ------------------------------------------------------------------ melee depth (GameTuning.Melee)
    // Combo / heavy / ground pound are NEW entry points over the existing paid gestures: TryPunch / TryHurricaneKick keep
    // their exact behaviour (other suites call them directly); only the E key is routed through these.
    MeleeSettings Melee => WorldSession.Instance != null && WorldSession.Instance.Tuning != null ? WorldSession.Instance.Tuning.Melee : fallbackMelee;
    static readonly MeleeSettings fallbackMelee = new MeleeSettings();
    float pendingForce = 1f, pendingDamage = 1f, chargeStart = -1f, comboExpires = -1f; bool pendingPause, poundPending; int comboStage;
    public int LastComboStage { get; private set; }
    public float LastHeavyCharge { get; private set; }
    public bool Charging => chargeStart >= 0f;
    /// 0..1 progress of the held E charge toward its cap (presentation / HUD may read it).
    public float Charge01 => Charging ? Mathf.Clamp01((Time.time - chargeStart - Melee.HeavyTapThreshold) / Mathf.Max(.01f, Melee.HeavyMaxChargeSeconds - Melee.HeavyTapThreshold)) : 0f;
    public bool GroundPounding { get; private set; }
    public int GroundPounds { get; private set; }
    public int FinisherPauses { get; private set; }
    public float LastPoundForce { get; private set; }
    public float LastPoundDamage { get; private set; }
    public int LastPoundBodies { get; private set; }
    public float LastPoundImpactTime { get; private set; } = -1f;
    public string LastMeleeResult { get; private set; } = "Ready";
    public event System.Action<int> ComboHit;
    public event System.Action<float> HeavyReleased;
    public event System.Action GroundPoundStarted;
    public event System.Action GroundPoundImpacted;
    PowerStats TakeModifiers(PowerStats stats, out bool pause)
    {
        stats.Force *= pendingForce; stats.Damage *= pendingDamage; pause = pendingPause;
        pendingForce = pendingDamage = 1f; pendingPause = false; return stats;
    }
    void ClearModifiers() { pendingForce = pendingDamage = 1f; pendingPause = false; poundPending = false; }
    void FinisherPause()
    {
        var feel = WorldSession.Instance != null ? WorldSession.Instance.Tuning.Feel : null; if (feel == null) return;
        if (TimeArbiter.RequestHitPause(Melee.FinisherHitPauseSeconds, feel.HitPauseMinInterval, feel.HitPauseTimeScale)) FinisherPauses++;
    }
    /// One tap: punch, then the kick finisher (bigger force + damage + short hit pause). A tap after ComboWindow
    /// restarts the string; a refused tap (cooldown / no charges) does not advance it.
    public bool TryComboAttack()
    {
        var m = Melee; if (Time.time > comboExpires) comboStage = 0;
        bool finisher = comboStage >= Mathf.Max(1, m.ComboLength) - 1;
        if (finisher) { pendingForce = m.FinisherForceMultiplier; pendingDamage = m.FinisherDamageMultiplier; pendingPause = true; }
        bool ok = finisher ? TryKick(true) : TryPunch();
        ClearModifiers();
        if (!ok) { LastMeleeResult = finisher ? LastComboKickResult : LastPunchResult; return false; }
        LastComboStage = finisher ? Mathf.Max(1, m.ComboLength) : comboStage + 1;
        comboStage = finisher ? 0 : comboStage + 1; comboExpires = Time.time + m.ComboWindow;
        LastMeleeResult = finisher ? "COMBO FINISHER" : "COMBO " + LastComboStage;
        ComboHit?.Invoke(LastComboStage); return true;
    }
    /// Held E released after `heldSeconds`: one paid punch scaled linearly from x1 at the tap threshold to the caps at
    /// HeavyMaxChargeSeconds (longer holds are capped). Resets the combo string.
    public bool TryHeavyAttack(float heldSeconds)
    {
        var m = Melee;
        float t = Mathf.Clamp01((heldSeconds - m.HeavyTapThreshold) / Mathf.Max(.01f, m.HeavyMaxChargeSeconds - m.HeavyTapThreshold));
        pendingForce = Mathf.Lerp(1f, m.HeavyMaxForceMultiplier, t); pendingDamage = Mathf.Lerp(1f, m.HeavyMaxDamageMultiplier, t); pendingPause = false;
        bool ok = TryPunch(); ClearModifiers();
        if (!ok) { LastMeleeResult = LastPunchResult; return false; }
        LastHeavyCharge = t; comboStage = 0; LastMeleeResult = $"HEAVY x{Mathf.Lerp(1f, m.HeavyMaxForceMultiplier, t):0.00}";
        HeavyReleased?.Invoke(t); return true;
    }
    /// Airborne E: dive and slam with a radial knockback (PoundRadius, punch force/damage x the pound multipliers), paid
    /// like a punch (one Strength charge, or the basic-melee cooldown). The dive runs through the normal Update movement,
    /// so the controller must be enabled.
    public bool TryGroundPound()
    {
        if (GroundPounding) { LastMeleeResult = "Blocked: already pounding"; return false; }
        if (controller.isGrounded) { LastMeleeResult = "Blocked: grounded"; return false; }
        poundPending = true; bool ok = TryPunch(); bool started = ok && GroundPounding; ClearModifiers();
        LastMeleeResult = started ? "GROUND POUND" : LastPunchResult; return started;
    }
    System.Collections.IEnumerator GroundPound(PowerStats stats)
    {
        var m = Melee; GroundPounding = true; comboStage = 0; GroundPoundStarted?.Invoke();
        float elapsed = 0f; bool landed = false;
        while (elapsed < m.PoundMaxSeconds)
        {
            verticalVelocity = -m.PoundDiveSpeed;   // Update's own Move carries the dive; its Landed event reports the real impact speed
            yield return null; elapsed += Time.deltaTime;
            if (controller.isGrounded) { landed = true; break; }
        }
        GroundPounding = false;
        var world = WorldSession.Instance;
        if (!landed || world == null || world.PlayerDead || (world.Mode != null && world.Mode.Ended)) { LastMeleeResult = landed ? "Pound cancelled" : "Pound found no ground"; yield break; }
        LastPoundForce = stats.Force * m.PoundForceMultiplier; LastPoundDamage = stats.Damage * m.PoundDamageMultiplier;
        using (powers.Credit(MeleeCredit)) LastPoundBodies = CombatImpact.Blast(powers, transform.position + Vector3.up * .3f, m.PoundRadius, LastPoundForce, LastPoundDamage, m.PoundLift, melee: true);
        LastPoundImpactTime = Time.time; GroundPounds++; GroundPoundImpacted?.Invoke();
    }
    /// Distance from the feet to the ground below (infinity with nothing below within 200 m).
    public float HeightAboveGround()
    {
        float nearest = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(transform.position + Vector3.up * .1f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.transform.root != transform && hit.distance < nearest) nearest = hit.distance;
        return float.IsPositiveInfinity(nearest) ? nearest : Mathf.Max(0f, nearest - .1f);
    }
    /// Camera-relative movement input of the last frame (zero with no input / in batch mode).
    public Vector3 MoveInput { get; private set; }
    public bool Dashing { get; private set; }
    public int DashCount { get; private set; }
    public event System.Action DashStarted;
    /// Speed power: a short horizontal burst through the CharacterController (never the physics root's rigidbody, never root
    /// motion). NPC capsules are ignored for the dash so it can cut through a crowd; stops early at a wall.
    /// onStep runs once per moved frame (the effect uses it for its pass-through hit).
    public bool Dash(Vector3 direction, float distance, float seconds, System.Action onStep)
    {
        direction.y = 0f;
        if (powers.OffensiveActionsBlocked || Dashing || direction.sqrMagnitude < .0001f || distance <= 0f) return false;
        EndClimb();
        StartCoroutine(DashRoutine(direction.normalized, distance, Mathf.Max(.02f, seconds), onStep));
        return true;
    }
    System.Collections.IEnumerator DashRoutine(Vector3 direction, float distance, float seconds, System.Action onStep)
    {
        Dashing = true; DashCount++; DashStarted?.Invoke();
        var ignored = new System.Collections.Generic.List<Collider>();
        if (WorldSession.Instance != null)
            foreach (var npc in WorldSession.Instance.Npcs)
            {
                var c = npc != null ? npc.GetComponent<Collider>() : null;
                if (c != null && c.enabled) { Physics.IgnoreCollision(controller, c, true); ignored.Add(c); }
            }
        transform.forward = direction;
        float speed = distance / seconds, moved = 0f;
        while (moved < distance)
        {
            if (Time.deltaTime > 0f)   // frozen frame (hit pause): no zero Move, ground contact kept
            {
                float step = Mathf.Min(distance - moved, speed * Time.deltaTime);
                var flags = controller.Move(direction * step);
                moved += step; onStep?.Invoke();
                if ((flags & CollisionFlags.Sides) != 0) break;
            }
            yield return null;
        }
        foreach (var c in ignored) if (c != null && controller != null) Physics.IgnoreCollision(controller, c, false);
        Dashing = false;
    }
    void OnDisable() { Dashing = false; Mantling = false; EndClimb(); }
    public bool Climbing { get; private set; }
    public Vector3 ClimbNormal { get; private set; }
    public string LastClimbResult { get; private set; }="Ready";
    public string LastTopOutFailure { get; private set; }="None";
    public Collider LastTopOutBlocker { get; private set; }
    public Collider LastTopOutTop { get; private set; }
    public float LastTopOutHeight { get; private set; }
    float climbRelatchAt;
    int offenseEpoch;
    public void BeginEvasion()
    {
        EndClimb();offenseEpoch++;chargeStart=-1;ClearModifiers();
        // Preserve vertical velocity and spent air-jump state.
    }
    void EndClimb()
    {
        Climbing=false;ClimbNormal=Vector3.zero;
        // Exits can return before the ordinary movement branch writes a new frame.
        // Clear the presentation flag now so a release/wall jump cannot re-enter Climb.
        if(controller!=null)PresentationState=new HeroPresentationState(
            transform.InverseTransformDirection(controller.velocity),controller.isGrounded,false);
    }
    bool Climbable(RaycastHit hit)
    {
        var c=hit.collider;
        if(!TraversalObstacle(c)||c.attachedRigidbody!=null||
            Mathf.Abs(hit.normal.y)>movement.ClimbNormalYLimit)return false;
        if(c.GetComponentInParent<CityNpc>()!=null||c.GetComponentInParent<BreakableProp>()!=null||
            c.GetComponentInParent<CityArtProp>()!=null)return false;
        var size=c.bounds.size;
        return size.y>=movement.ClimbMinimumWallHeight &&
            Mathf.Max(size.x,size.z)>=movement.ClimbMinimumWallWidth;
    }
    /// Geometry-only latch: generated structural colliders need no marker or extra component.
    public bool TryClimb(Vector3 towardWall)
    {
        if(powers.OffensiveActionsBlocked||!TraversalInputAllowed||Climbing||Mantling||AbilityDriving||Dashing||BackflipActive||GroundPounding||
            Time.time<climbRelatchAt)return false;
        towardWall=Vector3.ProjectOnPlane(towardWall,Vector3.up).normalized;
        if(towardWall.sqrMagnitude<.5f)return false;
        Vector3 chest=transform.position+Vector3.up*(controller.center.y+controller.height*.2f);
        if(!Physics.Raycast(chest,towardWall,out var hit,controller.radius+movement.ClimbLatchDistance,~0,QueryTriggerInteraction.Ignore)||
            !Climbable(hit)||Vector3.Dot(towardWall,-hit.normal)<.5f)return false;
        Climbing=true;ClimbNormal=hit.normal;
        wallJumpVelocity=Vector3.zero;verticalVelocity=0f;flightBoost=1f;
        LastClimbResult="Latched";return true;
    }
    /// The wall never refills the air jump. Only a real downward contact with ground does.
    public bool TryWallJump()
    {
        if(!Climbing||!TraversalInputAllowed)return false;
        Vector3 normal=ClimbNormal;EndClimb();
        climbRelatchAt=Time.time+movement.ClimbRelatchDelay;
        wallJumpVelocity=normal*movement.ClimbDetachPushSpeed;
        verticalVelocity=movement.ClimbWallJumpSpeed;
        jumpLaunched=true;LastJumpWasAir=false;LastClimbResult="Wall jump";Jumped?.Invoke();
        return true;
    }
    void MoveClimb()
    {
        if(!TraversalInputAllowed)
        {
            if(WorldSession.Instance.MenuOpen||WorldSession.Instance.PlayerDead||
                WorldSession.Instance.Mode?.Paused==true||WorldSession.Instance.Mode?.Ended==true)EndClimb();
            return;
        }
        bool jump=ScriptedClimbJump||Input.GetButtonDown("Jump");ScriptedClimbJump=false;
        if(jump){TryWallJump();return;}
        bool cancel=ScriptedClimbCancel||Input.GetKeyDown(KeyCode.LeftControl);ScriptedClimbCancel=false;
        if(cancel){EndClimb();climbRelatchAt=Time.time+movement.ClimbRelatchDelay;LastClimbResult="Released";ClimbReleased?.Invoke();return;}
        Vector2 axis=ScriptedClimbOverride?ScriptedClimbInput:
            new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
        if(axis.y>0f&&TryMantle(-ClimbNormal,true))
        {EndClimb();LastClimbResult="Topping out";MoveMantle();return;}
        Vector3 tangent=Vector3.Cross(ClimbNormal,Vector3.up).normalized;
        Vector3 chest=transform.position+Vector3.up*(controller.center.y+controller.height*.2f);
        float reach=controller.radius+movement.ClimbLatchDistance*2f;
        if(!Physics.Raycast(chest,-ClimbNormal,out var face,reach,~0,QueryTriggerInteraction.Ignore)||!Climbable(face))
        {EndClimb();LastClimbResult="Wall ended";return;}
        Vector3 correction=-face.normal*Mathf.Clamp(face.distance-controller.radius-controller.skinWidth-.03f,
            -movement.ClimbLatchDistance,movement.ClimbLatchDistance);
        Vector3 velocity=Vector3.up*(axis.y*movement.ClimbSpeed)+tangent*(axis.x*movement.ClimbSideSpeed);
        Vector3 step=velocity*Time.deltaTime+correction;
        var flags=controller.Move(step);
        PresentationState=new HeroPresentationState(transform.InverseTransformDirection(controller.velocity),false,false,true);
        if((flags&CollisionFlags.Above)!=0&&axis.y>0f&&TryMantle(-ClimbNormal,true))
        {EndClimb();LastClimbResult="Topping out";return;}
        if((flags&CollisionFlags.Below)!=0&&axis.y<0f)
        {EndClimb();airJumpUsed=jumpLaunched=mantleUsed=false;LastClimbResult="Landed";return;}
        chest=transform.position+Vector3.up*(controller.center.y+controller.height*.2f);
        if(!Physics.Raycast(chest,-face.normal,out var next,reach,~0,QueryTriggerInteraction.Ignore)||!Climbable(next))
        {EndClimb();LastClimbResult="Wall ended";return;}
        ClimbNormal=next.normal;
    }
    public bool Mantling { get; private set; }
    public int MantleCount { get; private set; }
    public string LastMantleResult { get; private set; }="Ready";
    Vector3 mantleLift, mantleEnd;
    float activeMantleSpeed;
    bool mantleAcross, mantleUsed;
    float mantleDeadline;
    readonly RaycastHit[] traversalHits = new RaycastHit[16];
    readonly Collider[] traversalOverlaps = new Collider[16];

    bool TraversalObstacle(Collider c) => c != null && c.transform.root != transform && !c.isTrigger;
    // Include the controller skin/contact margin: a mathematically clear capsule can still
    // be stopped by CharacterController.Move near a collider corner without this margin.
    // Keep capsule height unchanged so expanding sideways does not begin a lift inside its floor.
    bool CapsuleClear(Vector3 feet)
    {
        Vector3 center=feet+controller.center;
        float half=Mathf.Max(0,controller.height*.5f-controller.radius-controller.skinWidth);
        int count=Physics.OverlapCapsuleNonAlloc(center+Vector3.up*half,center-Vector3.up*half,
            controller.radius+controller.skinWidth,traversalOverlaps,~0,QueryTriggerInteraction.Ignore);
        if(count==traversalOverlaps.Length)return false; // crowded query fails closed
        for(int i=0;i<count;i++)if(TraversalObstacle(traversalOverlaps[i]))return false;
        return true;
    }
    bool CapsulePathClear(Vector3 from,Vector3 to)
    {
        Vector3 delta=to-from;if(delta.sqrMagnitude<.000001f)return true;
        Vector3 center=from+controller.center;
        float half=Mathf.Max(0,controller.height*.5f-controller.radius-controller.skinWidth);
        int count=Physics.CapsuleCastNonAlloc(center+Vector3.up*half,center-Vector3.up*half,
            controller.radius+controller.skinWidth,delta.normalized,traversalHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        lastPathHitCount=count;
        if(count==traversalHits.Length)return false;
        for(int i=0;i<count;i++)if(TraversalObstacle(traversalHits[i].collider))return false;
        return true;
    }
    int lastPathHitCount;
    /// A grounded short blink. The entire standing capsule and travel corridor must be clear,
    /// and the destination must have a walkable support surface.
    public bool TryTeleport(float distance)
    {
        if(powers.OffensiveActionsBlocked||!TraversalInputAllowed||!controller.isGrounded||Mantling||AbilityDriving||Dashing||BackflipActive||GroundPounding)return false;
        Vector3 direction=Vector3.ProjectOnPlane(view!=null?view.transform.forward:transform.forward,Vector3.up).normalized;
        if(direction.sqrMagnitude<.5f||distance<=0f)return false;
        Vector3 from=transform.position;
        Vector3 target=from+direction*distance;
        if(!CapsulePathClear(from+Vector3.up*.1f,target+Vector3.up*.1f))return false;
        if(!Physics.Raycast(target+Vector3.up*.5f,Vector3.down,out var support,1f,~0,QueryTriggerInteraction.Ignore)||
            support.collider.transform.root==transform||support.normal.y<Mathf.Cos(controller.slopeLimit*Mathf.Deg2Rad))return false;
        target.y=support.point.y+controller.skinWidth+.02f;
        if(Mathf.Abs(target.y-from.y)>.5f||!CapsuleClear(target))return false;
        controller.enabled=false;transform.position=target;controller.enabled=true;ResetMotion();Physics.SyncTransforms();
        return true;
    }
    /// Geometric ledge probe, shared by player input and verification. No building tags/components required.
    public bool TryMantle(Vector3 direction)=>TryMantle(direction,false);
    bool MantleRefused(string reason,bool fromClimb)
    {if(fromClimb)LastTopOutFailure=reason;return false;}
    bool TryMantle(Vector3 direction,bool fromClimb)
    {
        if(powers.OffensiveActionsBlocked||!TraversalInputAllowed||Mantling||(mantleUsed&&!fromClimb)||AbilityDriving||Dashing||BackflipActive||GroundPounding)return MantleRefused("movement state",fromClimb);
        direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
        if(direction.sqrMagnitude<.5f||!fromClimb&&Vector3.Dot(direction,transform.forward)<.5f)return MantleRefused("direction",fromClimb);
        float maximum=fromClimb?movement.ClimbTopOutDistance:movement.MantleMaxHeight;
        Vector3 feet=transform.position;
        // A front face is required; reaching into empty air must never create an upward step.
        if(!Physics.Raycast(feet+Vector3.up*movement.MantleMinHeight,direction,out var face,
            controller.radius+movement.MantleReach,~0,QueryTriggerInteraction.Ignore)||
            !TraversalObstacle(face.collider)||face.rigidbody!=null||Vector3.Dot(face.normal,direction)>-.4f)return MantleRefused("front face",fromClimb);
        Vector3 probe=face.point+direction*.03f;
        probe.y=feet.y+maximum+.02f;
        if(!Physics.Raycast(probe,Vector3.down,out var top,maximum-movement.MantleMinHeight+.02f,
            ~0,QueryTriggerInteraction.Ignore)||top.rigidbody!=null||top.normal.y<Mathf.Cos(controller.slopeLimit*Mathf.Deg2Rad))return MantleRefused("top surface",fromClimb);
        if(fromClimb){LastTopOutTop=top.collider;LastTopOutHeight=top.point.y;}
        float height=top.point.y-feet.y;
        if(height<movement.MantleMinHeight||height>maximum)return MantleRefused("top height",fromClimb);
        // Sample support beyond the lip separately: a parapet may be higher than its roof.
        Vector3 inside=face.point+direction*(controller.radius*2+controller.skinWidth);
        inside.y=top.point.y+.02f;
        if(!Physics.Raycast(inside,Vector3.down,out var support,movement.MantleMaxDrop+.02f,~0,QueryTriggerInteraction.Ignore)||
            support.rigidbody!=null||support.normal.y<Mathf.Cos(controller.slopeLimit*Mathf.Deg2Rad))return MantleRefused("inside support",fromClimb);
        // Clear the whole capsule on the lower standing surface, not just while above the lip.
        // Otherwise the descent catches a parapet side even though the elevated sweep is clear.
        if(!CapsuleClear(support.point+Vector3.up*(controller.skinWidth+.02f)))return MantleRefused("standing capsule",fromClimb);
        Vector3 end=new Vector3(inside.x,top.point.y+controller.skinWidth+.02f,inside.z);
        Vector3 lift=new Vector3(feet.x,end.y,feet.z);
        if(!CapsuleClear(lift))return MantleRefused("lift capsule",fromClimb);
        if(!CapsuleClear(end))return MantleRefused("end capsule",fromClimb);
        if(!CapsulePathClear(feet,lift))return MantleRefused("vertical path",fromClimb);
        if(!CapsulePathClear(lift,end))
        {
            if(fromClimb)for(int i=0;i<lastPathHitCount&&i<traversalHits.Length;i++)
                if(TraversalObstacle(traversalHits[i].collider)){LastTopOutBlocker=traversalHits[i].collider;break;}
            return MantleRefused("across path",fromClimb);
        }
        if(fromClimb){LastTopOutFailure="None";LastTopOutBlocker=null;}
        flightBoost=1f;LastMantleResult="Climbing";Mantling=true;mantleUsed=true;mantleAcross=false;mantleLift=lift;mantleEnd=end;
        activeMantleSpeed=fromClimb?movement.ClimbTopOutSpeed:movement.MantleSpeed;
        mantleDeadline=Time.time+(Vector3.Distance(feet,lift)+Vector3.Distance(lift,end))/Mathf.Max(.1f,activeMantleSpeed)+.5f;
        verticalVelocity=0;return true;
    }
    void MoveMantle()
    {
        if(!TraversalInputAllowed){if(WorldSession.Instance.PlayerDead||WorldSession.Instance.Mode?.Ended==true)Mantling=false;return;}
        Vector3 target=mantleAcross?mantleEnd:mantleLift;
        Vector3 before=transform.position;
        Vector3 step=Vector3.MoveTowards(before,target,activeMantleSpeed*Time.deltaTime)-before;
        var flags=controller.Move(step);
        PresentationState=new HeroPresentationState(transform.InverseTransformDirection(controller.velocity),controller.isGrounded,false);
        if((flags&(CollisionFlags.Above|CollisionFlags.Sides))!=0||Time.time>mantleDeadline)
        {LastMantleResult=Time.time>mantleDeadline?"Timed out":"Blocked by collision";Mantling=false;return;}
        if(Vector3.Distance(transform.position,target)<.025f)
        {
            if(!mantleAcross)mantleAcross=true;
            else {LastMantleResult="Complete";Mantling=false;MantleCount++;verticalVelocity=-movement.GroundStickSpeed;}
        }
    }

    public bool TryJump()
    {
        if(Climbing)return TryWallJump();
        if (!TraversalInputAllowed || Mantling || AbilityDriving || Dashing || BackflipActive || GroundPounding) return false;
        if (controller.isGrounded)
        {
            if (jumpLaunched) return false;
            LastJumpWasAir=false;
            verticalVelocity = movement.JumpSpeed;
        }
        else
        {
            if (airJumpUsed) return false;
            airJumpUsed = true;
            LastJumpWasAir=true;
            verticalVelocity = movement.AirJumpSpeed;
        }
        jumpLaunched = true;
        Jumped?.Invoke();
        return true;
    }
    // Used by deterministic test and the in-game verification harness.
    public void DebugSetResources(float fuel, int newCharges, float newCooldown = 0f)
    { powers.Flight.Fuel = fuel; powers.Strength.Charges = newCharges; powers.Strength.Cooldown = newCooldown; }
}
