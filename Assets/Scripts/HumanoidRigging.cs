using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(-10)]
public sealed class HumanoidRigging : MonoBehaviour
{
    [field:SerializeField] public RigBuilder Builder { get; private set; }
    [field:SerializeField] public MultiAimConstraint Look { get; private set; }
    [field:SerializeField] public TwoBoneIKConstraint LeftHand { get; private set; }
    [field:SerializeField] public TwoBoneIKConstraint RightHand { get; private set; }
    public float LookWeight => Look != null ? Look.weight : 0;
    public float HoldWeight => LeftHand != null ? LeftHand.weight : 0;
    HumanoidPresentation presentation;
    SuperHeroController hero;
    CityNpc npc;
    PowerUser powers;
    [SerializeField] Transform gaze, leftTarget, rightTarget, leftHint, rightHint;
    bool initialized;
    Transform Child(string name, Transform parent)
    {
        var child = new GameObject(name).transform; child.SetParent(parent, false); return child;
    }
    public void Initialize(HumanoidPresentation owner, SuperHeroController player, CityNpc actor)
    {
        presentation=owner;hero=player;npc=actor;powers=owner.GetComponent<PowerUser>();
        if(Builder==null||Look==null)throw new System.InvalidOperationException("Run PresentationSetup.Configure to author the humanoid rig prefab");
        var lookData=Look.data;lookData.limits=new Vector2(-owner.Tuning.LookLimit,owner.Tuning.LookLimit);Look.data=lookData;
        Look.weight=0;if(LeftHand!=null)LeftHand.weight=RightHand.weight=0;
        owner.Animator.Rebind();owner.Animator.Update(0);
#if UNITY_EDITOR
        if(Application.isBatchMode&&player!=null)Debug.Log($"RIG BIND root={owner.Animator.name} builderAnimator={Builder.GetComponent<Animator>().GetEntityId()} expected={owner.Animator.GetEntityId()} lookChild={Look.transform.IsChildOf(owner.Animator.transform)} sourceChild={gaze.IsChildOf(owner.Animator.transform)} avatarRoot={owner.Animator.avatarRoot.name}, lookInsideAvatar={Look.transform.IsChildOf(owner.Animator.avatarRoot)}, hierarchy={owner.Animator.hasTransformHierarchy}");
#endif
        Builder.enabled=true;
        if(!Builder.graph.IsValid()&&!Builder.Build())throw new System.InvalidOperationException("Authored humanoid rig did not build");
        initialized=true;
    }
#if UNITY_EDITOR
    public void Author(Animator animator, HumanoidAnimationTuning tuning, bool arms)
    {
        var rigRoot = Child("Awareness and telekinesis rig", animator.avatarRoot);
        var rig = rigRoot.gameObject.AddComponent<Rig>();
        gaze = Child("Gaze target", animator.transform);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        gaze.position = head.position + animator.transform.forward * 8;
        Look = Child("Restrained head gaze", rigRoot).gameObject.AddComponent<MultiAimConstraint>();
        var look = Look.data;
        look.constrainedObject = head;
        var sources = new WeightedTransformArray(); sources.Add(new WeightedTransform(gaze, 1)); look.sourceObjects = sources;
        look.aimAxis = ClosestAxis(head.InverseTransformDirection(animator.transform.forward));
        look.upAxis = ClosestAxis(head.InverseTransformDirection(Vector3.up));
        look.worldUpType = MultiAimConstraintData.WorldUpType.SceneUp;
        look.worldUpAxis = MultiAimConstraintData.Axis.Y;
        look.limits = new Vector2(-tuning.LookLimit, tuning.LookLimit);
        look.constrainedXAxis = look.constrainedYAxis = look.constrainedZAxis = true;
        look.maintainOffset = false; Look.data = look; Look.weight = 0;
        if (arms)
        {
            LeftHand = Arm(animator, rigRoot, true, out leftTarget, out leftHint);
            RightHand = Arm(animator, rigRoot, false, out rightTarget, out rightHint);
        }
        Builder=animator.gameObject.AddComponent<RigBuilder>();Builder.enabled=false;
        Builder.layers=new List<RigLayer>{new RigLayer(rig)};
    }
#endif
    static MultiAimConstraintData.Axis ClosestAxis(Vector3 direction)
    {
        var axes = new[] {Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back};
        int best = 0; for (int i = 1; i < axes.Length; i++) if (Vector3.Dot(direction, axes[i]) > Vector3.Dot(direction, axes[best])) best = i;
        return (MultiAimConstraintData.Axis)best;
    }
    TwoBoneIKConstraint Arm(Animator animator, Transform parent, bool left, out Transform target, out Transform hint)
    {
        var constraint = Child(left ? "Left hold arm" : "Right hold arm", parent).gameObject.AddComponent<TwoBoneIKConstraint>();
        target = Child(left ? "Left hand target" : "Right hand target", animator.transform); hint = Child(left ? "Left elbow hint" : "Right elbow hint", animator.transform);
        var data = constraint.data;
        data.root = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        data.mid = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        data.tip = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        target.position = data.tip.position; hint.position = data.mid.position;
        data.target = target; data.hint = hint; data.targetPositionWeight = 1; data.targetRotationWeight = 0; data.hintWeight = .5f;
        constraint.data = data; constraint.weight = 0; return constraint;
    }
    public void SetNear(bool near)
    {
        if (!initialized) return;
        if (!near) { Look.weight = 0; if (LeftHand != null) LeftHand.weight = RightHand.weight = 0; }
        Builder.enabled = near;
    }
    void OnDisable() { SetNear(false); }
    void OnEnable() { SetNear(true); }
    void Update()
    {
        if (!initialized || !presentation.enabled) return;
        var world = WorldSession.Instance; var t = presentation.Tuning;
        bool safe = !presentation.ActionActive && !presentation.IsDead && (npc == null || !npc.Frozen && !npc.Fleeing);
        Vector3 at = gaze.position; bool aware = false;
        if (safe && world != null)
        {
            if (hero != null && powers != null && presentation.FlightWeight<.01f && !hero.PresentationState.Flying && !(ThirdPersonCamera.Active != null && ThirdPersonCamera.Active.FirstPerson))
            {
                if(powers.HeldBody!=null)at=powers.HeldBody.worldCenterOfMass;
                else
                {
                    var camera=Camera.main;
                    if(camera==null)at=powers.AimOrigin+presentation.transform.forward*t.LookDistance;
                    else{var ray=camera.ViewportPointToRay(new Vector3(.5f,.5f,0));at=ray.GetPoint(Mathf.Max(0,Vector3.Dot(powers.AimOrigin-ray.origin,ray.direction))+t.LookDistance);}
                }
                aware = true;
            }
            else if (npc != null && world.Hero != null && (world.Hero.transform.position - presentation.transform.position).sqrMagnitude < t.NpcLookDistance * t.NpcLookDistance)
            { at = world.Hero.transform.position + Vector3.up * 1.5f; aware = true; }
        }
        var head = presentation.Animator.GetBoneTransform(HumanBodyBones.Head);
        aware &= Vector3.Angle(presentation.transform.forward, at - head.position) <= t.LookLimit;
        if (aware) gaze.position = at;
        float response = 1 - Mathf.Exp(-t.RigResponse * Time.deltaTime);
        Look.weight = safe ? Mathf.Lerp(Look.weight, aware ? t.LookWeight : 0, response) : 0;
        if (LeftHand == null) return;
        bool hold = safe && powers != null && powers.HeldBody != null;
        float weight = hold ? t.HoldWeight : 0;
        LeftHand.weight = RightHand.weight = safe ? Mathf.Lerp(LeftHand.weight, weight, response) : 0;
        if (!hold) return;
        var chest = presentation.Animator.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = presentation.Animator.GetBoneTransform(HumanBodyBones.Spine);
        Vector3 direction = (powers.HeldBody.worldCenterOfMass - chest.position).normalized;
        Vector3 target = chest.position + direction * t.HoldReach;
        leftTarget.position = target - presentation.transform.right * t.HoldHandSpread;
        rightTarget.position = target + presentation.transform.right * t.HoldHandSpread;
        leftHint.position = chest.position - presentation.transform.right * .5f - presentation.transform.up * .3f;
        rightHint.position = chest.position + presentation.transform.right * .5f - presentation.transform.up * .3f;
    }
}
