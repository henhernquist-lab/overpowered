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
        if (Input.GetMouseButtonDown(0)) powers.Use(powers.Selected);
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
    public bool TryJump()
    {
        if(!controller.isGrounded)return false;
        verticalVelocity=movement.JumpSpeed;Jumped?.Invoke();return true;
    }
    // Used by deterministic test and the in-game verification harness.
    public void DebugSetResources(float fuel, int newCharges, float newCooldown = 0f)
    { powers.Flight.Fuel = fuel; powers.Strength.Charges = newCharges; powers.Strength.Cooldown = newCooldown; }
}
