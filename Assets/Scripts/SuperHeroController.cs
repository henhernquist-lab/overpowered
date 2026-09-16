using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class SuperHeroController : MonoBehaviour
{
    CharacterController controller;
    Camera view;
    float verticalVelocity;
    float flightFuel = PrototypeTuning.FlightDuration;
    float cooldown;
    float chargeTimer;
    int charges = PrototypeTuning.PunchMaxCharges;
    public float FlightFuel => flightFuel;
    public int Charges => charges;
    public float Cooldown => cooldown;
    public string LastPunchResult { get; private set; } = "Ready";
    public int LastAffectedBodies { get; private set; }
    public float LastForce { get; private set; }

    void Awake() { controller = GetComponent<CharacterController>(); view = Camera.main; }
    void Update()
    {
        if (view == null) view = Camera.main;
        TickResources(Time.deltaTime, controller.isGrounded);
        Vector3 forward = Vector3.Scale(view.transform.forward, new Vector3(1, 0, 1)).normalized;
        Vector3 right = view.transform.right;
        Vector3 move = (forward * Input.GetAxisRaw("Vertical") + right * Input.GetAxisRaw("Horizontal")).normalized;
        bool flying = !controller.isGrounded && Input.GetKey(KeyCode.F) && flightFuel > 0f;
        if (move.sqrMagnitude > .01f) transform.forward = Vector3.Slerp(transform.forward, move, Time.deltaTime * 14f);
        float speed = Input.GetKey(KeyCode.LeftShift) ? PrototypeTuning.RunSpeed : PrototypeTuning.WalkSpeed;
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        if (controller.isGrounded && Input.GetButtonDown("Jump")) verticalVelocity = PrototypeTuning.JumpSpeed;
        if (flying)
        {
            verticalVelocity = Mathf.MoveTowards(verticalVelocity, Input.GetKey(KeyCode.Space) ? PrototypeTuning.FlightLift : 0f, PrototypeTuning.FlightLift * 3f * Time.deltaTime);
            move *= PrototypeTuning.FlightForwardBoost;
            flightFuel = Mathf.Max(0f, flightFuel - Time.deltaTime);
        }
        else verticalVelocity -= PrototypeTuning.Gravity * Time.deltaTime;
        controller.Move((move * speed + Vector3.up * verticalVelocity) * Time.deltaTime);
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E)) TryPunch();
    }
    void TickResources(float dt, bool grounded)
    {
        cooldown = Mathf.Max(0f, cooldown - dt);
        if (grounded) flightFuel = Mathf.Min(PrototypeTuning.FlightDuration, flightFuel + PrototypeTuning.FlightRechargePerSecond * dt);
        if (charges < PrototypeTuning.PunchMaxCharges)
        {
            chargeTimer += dt;
            if (chargeTimer >= PrototypeTuning.PunchChargeRecharge) { charges++; chargeTimer = 0f; }
        }
    }
    public void DebugSimulateFlight(float seconds) { flightFuel = Mathf.Max(0f, flightFuel - seconds); }
    public void DebugSimulateGround(float seconds) { TickResources(seconds, true); }
    public bool TryPunch()
    {
        if (cooldown > 0f) { LastPunchResult = "Punch blocked: cooldown"; return false; }
        if (charges <= 0) { LastPunchResult = "Punch blocked: 0 charges"; return false; }
        charges--; cooldown = PrototypeTuning.PunchCooldown; chargeTimer = 0f; LastForce = PrototypeTuning.PunchForce;
        LastAffectedBodies = 0;
        foreach (Collider hit in Physics.OverlapSphere(transform.position + transform.forward * 1.7f, PrototypeTuning.PunchRadius))
        {
            Rigidbody body = hit.attachedRigidbody;
            if (body == null || body.isKinematic) continue;
            body.AddExplosionForce(PrototypeTuning.PunchForce, transform.position + transform.forward * 1.7f, PrototypeTuning.PunchRadius, PrototypeTuning.PunchUpwardForce, ForceMode.Impulse);
            var breakable = body.GetComponent<BreakableProp>(); if (breakable != null) breakable.HitByPunch();
            LastAffectedBodies++;
        }
        LastPunchResult = $"PUNCH: {LastAffectedBodies} bodies hit @ {LastForce:0} N";
        return true;
    }
    // Used by deterministic test and the in-game verification harness.
    public void DebugSetResources(float fuel, int newCharges, float newCooldown = 0f) { flightFuel = fuel; charges = newCharges; cooldown = newCooldown; }
}
