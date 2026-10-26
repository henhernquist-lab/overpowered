using UnityEngine;
using UnityEngine.AI;
public sealed partial class PowerExpansionRuntime
{
    Collider grappleAnchor;Vector3 localAnchor,grappleVelocity;float grappleUntil,grappleMaxRange;int grappleFrame;
    LineRenderer tether;
    public Vector3 GrappleVelocity=>grappleVelocity;
    public MirageDecoy Decoy{get;private set;}
    public bool BeginGrapple(PowerRuntime power)
    {
        if(!CanAct||Grappling||lunging||Art==null)return false;
        float range=Mathf.Clamp(User.Stats(power).Range,2,60);grappleMaxRange=range;Vector3 origin=User.AimOrigin,direction=(User.AimPoint(range)-origin).normalized;
        int count=Physics.RaycastNonAlloc(origin,direction,rays,range,~0,QueryTriggerInteraction.Ignore);if(count>=rays.Length)return false;
        RaycastHit closest=default;float distance=range+.01f;
        for(int i=0;i<count;i++)if(rays[i].transform.root!=transform&&rays[i].distance<distance){closest=rays[i];distance=closest.distance;}
        if(closest.collider==null||closest.rigidbody!=null||closest.collider.GetComponentInParent<CityNpc>()!=null||distance<2)return false;
        grappleAnchor=closest.collider;localAnchor=grappleAnchor.transform.InverseTransformPoint(closest.point);grappleUntil=Time.time+8;grappleFrame=Time.frameCount;
        grappleVelocity=Vector3.ClampMagnitude(User.Hero.GetComponent<CharacterController>().velocity,18);
        if(tether==null)
        {
            var go=new GameObject("Grapple tether");go.transform.SetParent(transform,false);tether=go.AddComponent<LineRenderer>();
            tether.sharedMaterial=CityMaterials.GetVfx(CityColor.Cyan);tether.positionCount=2;tether.useWorldSpace=true;tether.startWidth=.045f;tether.endWidth=.025f;tether.numCapVertices=2;tether.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        tether.enabled=true;Vfx.Emit(Art.SoundRing,closest.point,-direction,Vector3.one*.25f,Vector3.zero,.3f,CityColor.Cyan,3,0);return true;
    }
    void TickGrapple()
    {
        if(grappleAnchor==null){if(tether!=null&&tether.enabled)ReleaseGrapple(true);return;}
        if(!grappleAnchor.enabled||!grappleAnchor.gameObject.activeInHierarchy||Time.time>=grappleUntil){ReleaseGrapple(true);return;}
        if(Time.frameCount>grappleFrame+1&&(Input.GetKeyDown(KeyCode.F)||Input.GetKeyDown(KeyCode.Space)))
        {
            bool jump=Input.GetKeyDown(KeyCode.Space);ReleaseGrapple(true);if(jump)User.Hero.TryJump();return;
        }
        Vector3 anchor=grappleAnchor.transform.TransformPoint(localAnchor),origin=transform.position+Vector3.up;
        Vector3 delta=anchor-origin;float distance=delta.magnitude;
        if(distance<1.2f||distance>grappleMaxRange+2||!TetherClear(origin,anchor)){ReleaseGrapple(true);return;}
        var camera=Camera.main;Vector3 right=camera!=null?camera.transform.right:transform.right;
        Vector3 input=right*Input.GetAxisRaw("Horizontal")+Vector3.Cross(right,Vector3.up)*Input.GetAxisRaw("Vertical");
        grappleVelocity+=(delta.normalized*28+Vector3.down*12+Vector3.ProjectOnPlane(input,delta.normalized)*9)*Time.deltaTime;
        grappleVelocity=Vector3.ClampMagnitude(grappleVelocity,22);
        var flags=User.Hero.MoveAbility(grappleVelocity,false);
        if((flags&CollisionFlags.Above)!=0&&grappleVelocity.y>0)grappleVelocity.y=0;
        if((flags&CollisionFlags.Sides)!=0){grappleVelocity*=.5f;if(distance<2.5f)ReleaseGrapple(true);}
        tether.SetPosition(0,User.AimOrigin);tether.SetPosition(1,anchor);
    }
    bool TetherClear(Vector3 from,Vector3 anchor)
    {
        Vector3 delta=anchor-from;int count=Physics.RaycastNonAlloc(from,delta.normalized,rays,delta.magnitude-.08f,~0,QueryTriggerInteraction.Ignore);
        if(count>=rays.Length)return false;
        for(int i=0;i<count;i++)if(rays[i].transform.root!=transform&&rays[i].collider!=grappleAnchor)return false;return true;
    }
    public void ReleaseGrapple(bool momentum=true)
    {
        bool was=Grappling||tether!=null&&tether.enabled;
        if(was&&Art!=null&&Vfx!=null)Vfx.Emit(Art.SoundRing,transform.position+Vector3.up,Vector3.up,Vector3.one*.35f,Vector3.zero,.2f,CityColor.Cyan,3,80);
        grappleAnchor=null;if(tether!=null)tether.enabled=false;
        if(was&&momentum&&User!=null)User.Hero.ResumeTraversalMomentum(grappleVelocity);grappleVelocity=Vector3.zero;
    }
    public bool SummonDecoy(PowerRuntime power)
    {
        if(!CanAct||Art?.Hologram==null)return false;
        Vector3 desired=transform.position+transform.right*2+transform.forward;
        if(!NavMesh.SamplePosition(desired,out var nav,2,NavMesh.AllAreas))return false;
        Vector3 delta=nav.position-transform.position;
        int count=Physics.RaycastNonAlloc(transform.position+Vector3.up,delta.normalized,rays,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)if(rays[i].transform.root!=transform)return false;
        if(Decoy!=null)Decoy.Dissolve();
        Decoy=MirageDecoy.Create(this,nav.position,Mathf.Min(8,User.Stats(power).Duration));return Decoy!=null;
    }
}
