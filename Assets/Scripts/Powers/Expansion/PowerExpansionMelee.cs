using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
public enum CombatMorphForm{BladeArms,HammerFists,ExtendedClaws}
public sealed partial class PowerExpansionRuntime
{
    public CombatMorphForm Form{get;private set;}
    public int PlasmaStage{get;private set;}
    public int MeleeImpacts{get;private set;}
    public bool MeleePending{get;private set;}
    public bool DrivesMotion=>lunging||Grappling;
    // Traversal is implemented in the next isolated phase.
    public bool Grappling=>grappleAnchor!=null;
    bool lunging,chargedRequested;
    float comboUntil,chargeStarted=-1,apexUntil;
    readonly MeshRenderer[] weapons=new MeshRenderer[2];
    readonly MeshFilter[] weaponMeshes=new MeshFilter[2];
    MaterialPropertyBlock weaponBlock;
    public bool ApexActive=>Time.time<apexUntil;
    public void Apex(float duration){apexUntil=Time.time+Mathf.Clamp(duration,0,10);}
    public void CycleForm(){Form=(CombatMorphForm)(((int)Form+1)%3);User.Message="Morph: "+Form+" / "+Tuning.MorphFormKey+" cycles forms";}
    bool CanAct=>User!=null&&WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&!WorldSession.Instance.MenuOpen&&!User.OffensiveActionsBlocked;
    public string Status
    {
        get{
            if(Grappling)return "GRAPPLE / F release / Space jump release";
            if(Decoy!=null&&Decoy.Alive)return "MIRAGE / hologram active";
            if(User?.Selected?.Definition.Effect is CombatMorphingEffect)return "MORPH / "+Form+" / "+Tuning.MorphFormKey+" change form"+(ApexActive?" / APEX":"");
            if(User?.Selected?.Definition.Effect is PlasmaBladesEffect)return "PLASMA / strike "+(PlasmaStage%3+1)+" / hold "+Tuning.PlasmaChargeKey+" to charge";
            return "";
        }
    }
    void Update()
    {
        if(!CanAct){ReleaseGrapple(false);if(Decoy!=null)Decoy.Dissolve();chargeStarted=-1;return;}
        TickGrapple();
        var selected=User.Selected?.Definition.Effect;
        if(selected is CombatMorphingEffect&&Input.GetKeyDown(Tuning.MorphFormKey))CycleForm();
        if(selected is PlasmaBladesEffect)
        {
            if(Input.GetKeyDown(Tuning.PlasmaChargeKey))chargeStarted=Time.time;
            if(Input.GetKeyUp(Tuning.PlasmaChargeKey)&&chargeStarted>=0){if(Time.time-chargeStarted>=.6f)ChargedStrike();chargeStarted=-1;}
        }
        else chargeStarted=-1;
    }
    public bool ChargedStrike()
    {
        if(!(User.Selected?.Definition.Effect is PlasmaBladesEffect))return false;
        chargedRequested=true;bool result=User.Use(User.Selected);chargedRequested=false;return result;
    }
    public bool Melee(PowerRuntime power,bool plasma)
    {
        if(!CanAct||MeleePending||Art==null||Tuning==null)return false;
        bool charge=plasma&&chargedRequested;
        if(charge&&User.Energy<User.EnergyCost(power)+8)return false;
        var stats=User.Stats(power);float windup,multiplier,recovery,range=stats.Range,force=stats.Force;
        bool lunge=!plasma&&Form==CombatMorphForm.ExtendedClaws;
        bool hammer=!plasma&&Form==CombatMorphForm.HammerFists;
        if(plasma)
        {
            PlasmaStage=Time.time>comboUntil?1:PlasmaStage%3+1;comboUntil=Time.time+.95f;
            windup=charge? .4f:PlasmaStage==3? .25f:.1f;
            multiplier=charge?2.2f:PlasmaStage==3?1.5f:PlasmaStage==2? .9f:1;
            recovery=charge?4:PlasmaStage==3?2.2f:1;
            if(charge)User.TrySpendEnergy(8);
        }
        else
        {
            windup=hammer? .42f:lunge? .2f:.14f;multiplier=hammer?1.6f:lunge? .9f:1;
            recovery=hammer?1.9f:lunge?1.4f:.75f;range+=lunge?1.25f:0;force*=hammer?2:1;
            if(ApexActive){multiplier*=1.45f;range+=.6f;recovery*=.8f;}
        }
        power.ActivationCooldownMultiplier=recovery;
        Vector3 forward=User.AimDirection;forward.y=0;forward.Normalize();if(forward.sqrMagnitude<.1f)forward=transform.forward;
        MeleePending=true;User.Hero.PresentPunch();
        StartCoroutine(Strike(power,forward,User.RollDamage(stats.Damage*multiplier,stats.DamageVariance),range,force,windup,lunge,hammer,plasma,charge));return true;
    }
    IEnumerator Strike(PowerRuntime power,Vector3 forward,float damage,float range,float force,float windup,bool lunge,bool hammer,bool plasma,bool charge)
    {
        try{
            lunging=lunge;float end=Time.time+windup;
            while(Time.time<end)
            {
                if(!CanAct||User.Selected!=power)yield break;
                if(lunge)User.Hero.MoveAbility(forward*7+Vector3.down*2,false);
                yield return null;
            }
            if(!CanAct||User.Selected!=power)yield break;
            Vector3 origin=transform.position+Vector3.up;
            using(User.Credit(power.Definition.Id))foreach(var n in Scan(origin,forward,range,plasma?65:55,Tuning.MaxTargets))
            {
                n.Damage(damage,User,true,force,origin);MeleeImpacts++;
                if(hammer&&!n.Dead){n.TryPowerInterrupt(.3f,n.IsBoss?6:1.2f);(n.GetComponent<NpcPowerDisplacement>()??n.gameObject.AddComponent<NpcPowerDisplacement>()).Begin(n,forward,1.2f,.2f);}
                Vfx.Emit(Art.Slash,n.transform.position+Vector3.up,forward,Vector3.one*(charge?1.2f:.65f),Vector3.zero,.22f,plasma?CityColor.Cyan:CityColor.Cream,2,90);
            }
            Vfx.Emit(Art.Slash,origin+forward,forward,Vector3.one*(charge?1.8f:1),forward*3,.25f,plasma?CityColor.Cyan:CityColor.Cream,1,plasma?300:120);
        }finally{lunging=false;MeleePending=false;}
    }
    void LateUpdate(){UpdateWeapons();}
    void UpdateWeapons()
    {
        bool morph=User?.Selected?.Definition.Effect is CombatMorphingEffect,plasma=User?.Selected?.Definition.Effect is PlasmaBladesEffect;
        bool show=CanAct&&(morph||plasma);var presentation=GetComponent<HumanoidPresentation>();var animator=presentation?.Animator;
        if(animator==null||!animator.isHuman)show=false;
        for(int i=0;i<2;i++)
        {
            if(!show){if(weapons[i]!=null)weapons[i].enabled=false;continue;}
            if(weapons[i]==null)
            {
                var go=new GameObject(i==0?"Left power weapon":"Right power weapon");go.transform.SetParent(transform,false);
                weaponMeshes[i]=go.AddComponent<MeshFilter>();weapons[i]=go.AddComponent<MeshRenderer>();weapons[i].receiveShadows=false;
            }
            var forearm=animator.GetBoneTransform(i==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
            var hand=animator.GetBoneTransform(i==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            if(forearm==null||hand==null){weapons[i].enabled=false;continue;}
            Mesh mesh=plasma?Art.PlasmaBlade:Form==CombatMorphForm.HammerFists?Art.MorphHammer:Form==CombatMorphForm.ExtendedClaws?Art.MorphClaws:Art.MorphBlade;
            if(weaponMeshes[i].sharedMesh!=mesh)weaponMeshes[i].sharedMesh=mesh;
            var material=plasma?Art.EffectMaterial:CityMaterials.Get(CityColor.Metal);
            if(weapons[i].sharedMaterial!=material)weapons[i].sharedMaterial=material;
            if(plasma){weaponBlock.SetColor("_Color",CityMaterials.Current.Palette.Colors[(int)CityColor.Cyan]);weapons[i].SetPropertyBlock(weaponBlock);}else weapons[i].SetPropertyBlock(null);
            var t=weapons[i].transform;t.position=Vector3.Lerp(forearm.position,hand.position,.7f);
            t.rotation=Quaternion.LookRotation((hand.position-forearm.position).normalized,transform.up);
            float size=Mathf.Clamp(Vector3.Distance(forearm.position,hand.position),.18f,.65f)*(morph&&ApexActive?1.35f:1);
            t.localScale=Vector3.one*size;
            var rig=Camera.main!=null?Camera.main.GetComponent<ThirdPersonCamera>():null;
            weapons[i].shadowCastingMode=rig!=null&&rig.FirstPerson?ShadowCastingMode.ShadowsOnly:ShadowCastingMode.On;weapons[i].enabled=mesh!=null;
        }
    }
    void OnDisable(){StopAllCoroutines();ReleaseGrapple(false);if(Decoy!=null)Decoy.Dissolve();lunging=false;MeleePending=false;apexUntil=0;chargeStarted=-1;foreach(var w in weapons)if(w!=null)w.enabled=false;}
}
