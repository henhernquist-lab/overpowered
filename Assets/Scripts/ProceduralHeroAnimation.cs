using UnityEngine;

/// Writes only the visual child hierarchy. Never moves the controller or affects abilities.
public sealed class ProceduralHeroAnimation : MonoBehaviour
{
    [SerializeField] ProceduralAnimationTuning tuning;
    [SerializeField] Transform visualRoot;
    [SerializeField] Transform punchShoulder;
    SuperHeroController source;
    Vector3 restPosition, restScale, shoulderPosition;
    Quaternion restRotation, shoulderRotation;
    Vector3 lean;
    float bobPhase, bobWeight;
    float punchTime = float.PositiveInfinity, landTime = float.PositiveInfinity, landStrength;

    public void Initialize(SuperHeroController controller, Transform visuals, Transform shoulder,
        ProceduralAnimationTuning settings)
    {
        Unsubscribe();
        source = controller;
        visualRoot = visuals;
        punchShoulder = shoulder;
        tuning = settings;
        restPosition = visuals.localPosition;
        restRotation = visuals.localRotation;
        restScale = visuals.localScale;
        shoulderPosition = shoulder.localPosition;
        shoulderRotation = shoulder.localRotation;
        if (isActiveAndEnabled) Subscribe();
    }

    void OnEnable() { Subscribe(); }
    void OnDisable()
    {
        Unsubscribe();
        punchTime = landTime = float.PositiveInfinity;
        lean = Vector3.zero;
        bobPhase = bobWeight = landStrength = 0f;
        if (visualRoot != null)
        {
            visualRoot.SetLocalPositionAndRotation(restPosition, restRotation);
            visualRoot.localScale = restScale;
        }
        if (punchShoulder != null) punchShoulder.SetLocalPositionAndRotation(shoulderPosition, shoulderRotation);
    }
    void Subscribe()
    {
        if (source == null) return;
        // Idempotent for Initialize and component re-enabling.
        Unsubscribe();
        source.PunchStarted += PlayPunch;
        source.Landed += PlayLanding;
    }
    void Unsubscribe()
    {
        if (source == null) return;
        source.PunchStarted -= PlayPunch;
        source.Landed -= PlayLanding;
    }
    public void PlayPunch() { punchTime = 0f; }
    public void PlayLanding(float impactSpeed)
    {
        if (tuning == null || impactSpeed < tuning.LandMinimumImpactSpeed) return;
        landTime = 0f;
        landStrength = Mathf.Clamp01(impactSpeed / Mathf.Max(.001f, tuning.LandFullImpactSpeed));
    }
    void LateUpdate()
    {
        if (source != null) Advance(source.PresentationState, Time.deltaTime);
    }

    // Explicit state/time input permits repeatable pose tests without faking gameplay resources.
    public void Advance(HeroPresentationState state, float dt)
    {
        if (tuning == null || visualRoot == null || punchShoulder == null || dt <= 0f) return;
        punchTime += dt;
        landTime += dt;
        Vector3 horizontal = new Vector3(state.LocalVelocity.x, 0f, state.LocalVelocity.z);
        float speed = horizontal.magnitude;
        Vector3 direction = speed > 0f ? horizontal / speed : Vector3.zero;
        float groundWeight = state.Grounded ? Mathf.Clamp01(speed / Mathf.Max(.001f, tuning.RunFullLeanSpeed)) : 0f;
        float pitch = state.Flying
            ? tuning.FlightPitchDegrees * Mathf.Clamp01(speed / Mathf.Max(.001f, tuning.FlightFullLeanSpeed))
            : direction.z * tuning.RunLeanDegrees * groundWeight;
        float roll = state.Flying ? 0f : -direction.x * tuning.RunLeanDegrees * groundWeight;
        lean = Vector3.Lerp(lean, new Vector3(pitch, 0f, roll), 1f - Mathf.Exp(-tuning.PoseResponse * dt));
        bobWeight = Mathf.Lerp(bobWeight, groundWeight, 1f - Mathf.Exp(-tuning.BobResponse * dt));
        if (state.Grounded) bobPhase = Mathf.Repeat(bobPhase + speed * tuning.BobCyclesPerMetre * dt, 1f);
        // Positive-only bob keeps the feet above their resting plane.
        float bob = (1f - Mathf.Cos(bobPhase * Mathf.PI * 2f)) * .5f * tuning.BobHeight * bobWeight;
        float punch = Pulse(punchTime, tuning.PunchAttackSeconds, tuning.PunchRecoverSeconds);
        float landing = Pulse(landTime, tuning.LandCompressSeconds, tuning.LandRecoverSeconds) * landStrength;
        visualRoot.localPosition = restPosition + Vector3.up * bob;
        visualRoot.localRotation = restRotation * Quaternion.Euler(
            lean.x + punch * tuning.PunchBodyPitchDegrees - landing * tuning.LandBackTiltDegrees,
            punch * tuning.PunchBodyYawDegrees, lean.z);
        visualRoot.localScale = Vector3.Scale(restScale, new Vector3(
            1f + landing * tuning.LandWiden, 1f - landing * tuning.LandSquash, 1f + landing * tuning.LandWiden));
        punchShoulder.localRotation = shoulderRotation * Quaternion.Euler(punch * tuning.PunchArmDegrees, 0f, 0f);
        punchShoulder.localPosition = shoulderPosition + Vector3.forward * (punch * tuning.PunchReach);
    }
    static float Pulse(float time, float attack, float recover)
    {
        attack = Mathf.Max(.001f, attack);
        recover = Mathf.Max(.001f, recover);
        if (time < attack) return Mathf.Sin(Mathf.Clamp01(time / attack) * Mathf.PI * .5f);
        return 1f - Mathf.SmoothStep(0f, 1f, (time - attack) / recover);
    }
}
