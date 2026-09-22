using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Humanoid Animation Tuning")]
public sealed class HumanoidAnimationTuning : ScriptableObject
{
    public GameObject Model;
    public RuntimeAnimatorController Controller;
    [Header("Clips (Backflip and Hurricane Kick are dispatched by those two gestures)")]
    public AnimationClip Idle, Walk, Jog, Run, Back, Jump, Land, Punch, Hit, Death, Cast, ArmedRun, Shoot, Backflip, HurricaneKick;
    [Header("Continuous locomotion: metres/second")]
    public float WalkThreshold=1.8f, JogThreshold=5f, RunThreshold=9f;
    public float SpeedDamping=.10f, TransitionSeconds=.12f, BackThreshold=-.5f;
    public float JumpPlayback=1f, LandPlayback=1.2f, ActionPlayback=1f;
    public float HitPlayback=3f, CastPlayback=3f, ShootPlayback=1.5f;
    [Header("Supplied Shooting Gun uses a left-handed side draw")]
    public float ShootStartSeconds=1.25f, ShootEndSeconds=2.5f, ShootVisualYaw=90f;
    [Header("Punch timing: source clip start / impact, seconds")]
    public float PunchStartSeconds=11f/30f, PunchImpactSeconds=17f/30f, PunchPlayback=1.6f;
    // Sampled from the supplied clips (Verification/Abilities/clip-sample.txt): the backflip launches
    // near frame 18 of 65 and lands near frame 40; the hurricane kick's striking (left) leg starts
    // lifting near frame 10 and reaches peak forward extension at frame 28 of 55.
    [Header("Backflip: source clip start, seconds (playback fits the dash)")]
    public float BackflipStartSeconds=.6f;
    [Header("Hurricane Kick timing: source clip start / impact, seconds")]
    public float KickStartSeconds=.333f, KickImpactSeconds=.933f, KickPlayback=2f;
    [Header("Flight: model-space pose, applied after Animator")]
    public float FlightPitch=78f, FlightFullSpeed=10f, FlightBlendResponse=6f, PoseResponse=10f;
    public float HoverBobAmplitude=.065f, HoverBobFrequency=.7f;
    public float HoverArmOut=.4f, HoverArmForward=.12f, HoverElbowForward=.5f;
    public float FlightArmSpread=.12f, FlightHeadLift=65f, HoverKneeBend=12f;
    [Header("Civilian panic overlay")]
    public float PanicPlayback=1.45f, PanicLean=24f, PanicResponse=8f;
    public float PanicArmRaise=.8f, PanicArmOut=.7f, PanicFlailDegrees=28f, PanicFlailFrequency=2.7f;
    public float PanicForearmOut=.2f, PanicForearmForward=.5f, PanicSidePhase=.25f;
    public float PanicLookBackDegrees=72f, PanicLookBackFrequency=.65f;
    public float PanicPathDeviation=1.5f, PanicPathFrequency=.75f;
    [Header("Ground presentation (clip owns vertical gait; no second bob)")]
    public float GroundLean=6f, GroundLeanFullSpeed=9f;
    public float PunchWindup=>Mathf.Max(0,(PunchImpactSeconds-PunchStartSeconds)/Mathf.Max(.01f,PunchPlayback));
    public float KickWindup=>Mathf.Max(0,(KickImpactSeconds-KickStartSeconds)/Mathf.Max(.01f,KickPlayback));
    void OnValidate()
    {
        WalkThreshold=Mathf.Max(.01f,WalkThreshold);JogThreshold=Mathf.Max(WalkThreshold+.01f,JogThreshold);RunThreshold=Mathf.Max(JogThreshold+.01f,RunThreshold);
        FlightFullSpeed=Mathf.Max(.01f,FlightFullSpeed);GroundLeanFullSpeed=Mathf.Max(.01f,GroundLeanFullSpeed);
        PunchPlayback=Mathf.Max(.01f,PunchPlayback);ActionPlayback=Mathf.Max(.01f,ActionPlayback);PanicPlayback=Mathf.Max(.01f,PanicPlayback);
        JumpPlayback=Mathf.Max(.01f,JumpPlayback);LandPlayback=Mathf.Max(.01f,LandPlayback);HitPlayback=Mathf.Max(.01f,HitPlayback);CastPlayback=Mathf.Max(.01f,CastPlayback);ShootPlayback=Mathf.Max(.01f,ShootPlayback);
        PunchStartSeconds=Mathf.Max(0,PunchStartSeconds);PunchImpactSeconds=Mathf.Max(PunchStartSeconds,PunchImpactSeconds);
        KickPlayback=Mathf.Max(.01f,KickPlayback);
        BackflipStartSeconds=Mathf.Max(0,BackflipStartSeconds);
        KickStartSeconds=Mathf.Max(0,KickStartSeconds);KickImpactSeconds=Mathf.Max(KickStartSeconds,KickImpactSeconds);
    }
}
