using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Owns only temporary evasion, NPC collision pairs and pooled outline presentation.
/// CharacterController remains enabled; world collision and movement stay with the controller.
[DefaultExecutionOrder(-20)]
public sealed class PlayerPhaseShift : MonoBehaviour
{
    public bool Active { get; private set; }
    public float Remaining => Active ? Mathf.Max(0, until-Time.time) : 0;
    public int Activations { get; private set; }
    public int Recoveries { get; private set; }
    public int UnresolvedRecoveries { get; private set; }
    public int IgnoredCount => ignored.Count;
    public const int EchoCount=3;
    PowerUser user; CharacterController body; float until, pulseUntil, nextEcho;
    readonly List<Collider> ignored=new List<Collider>(256);
    readonly Collider[] overlaps=new Collider[128];
    readonly RaycastHit[] hits=new RaycastHit[128];
    readonly Vector3[] history=new Vector3[64]; int historyCount, historyNext;
    readonly LineRenderer[] echoes=new LineRenderer[EchoCount];
    readonly float[] echoUntil=new float[EchoCount];
    LineRenderer outline; readonly LineRenderer[] edges=new LineRenderer[2];
    int echoNext;
    readonly Vector3[] echoPositions=new Vector3[EchoCount];
    readonly Quaternion[] echoRotations=new Quaternion[EchoCount];
    public void Initialize(PowerUser owner,CityColor color)
    {
        user=owner;body=GetComponent<CharacterController>();
        outline=Line("Phase silhouette",15,color);
        // Stylized shoulder/head/body outline. No Sidekick renderer or vendor material is modified.
        Vector3[] shape={new Vector3(-.28f,.1f,0),new Vector3(-.36f,.75f,0),new Vector3(-.55f,1.35f,0),
            new Vector3(-.22f,1.5f,0),new Vector3(-.2f,1.82f,0),new Vector3(0,1.98f,0),
            new Vector3(.2f,1.82f,0),new Vector3(.22f,1.5f,0),new Vector3(.55f,1.35f,0),
            new Vector3(.36f,.75f,0),new Vector3(.28f,.1f,0),new Vector3(.1f,.1f,0),
            new Vector3(0,.75f,0),new Vector3(-.1f,.1f,0),new Vector3(-.28f,.1f,0)};
        outline.SetPositions(shape);
        for(int i=0;i<EchoCount;i++){echoRotations[i]=Quaternion.identity;echoes[i]=Line("Phase echo "+i,shape.Length,color);echoes[i].SetPositions(shape);}
        for(int i=0;i<2;i++)edges[i]=Line("Phase peripheral "+i,3,color);
    }
    LineRenderer Line(string label,int points,CityColor color)
    {
        var go=new GameObject(label);go.transform.SetParent(transform,false);
        var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.positionCount=points;
        line.widthMultiplier=.025f;line.sharedMaterial=CityMaterials.GetVfx(color);
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;return line;
    }
    bool Live=>WorldSession.Instance!=null&&!WorldSession.Instance.PlayerDead&&WorldSession.Instance.Mode?.Ended!=true;
    public bool Begin(float seconds)
    {
        if(Active||!Live||Time.timeScale<=0||WorldSession.Instance.MenuOpen||WorldSession.Instance.Mode?.Paused==true||
            user.Hero.Dashing||user.Hero.BackflipActive||user.Hero.GroundPounding||user.Hero.Mantling||
            user.SynergyRunner!=null&&user.SynergyRunner.Busy||!WorldClear(transform.position))return false;
        user.EndChannel();user.Release(false);user.Shield?.Drop(false);user.Hero.BeginEvasion();
        historyCount=historyNext=0;if(Clear(transform.position,true))Remember();Active=true;until=Time.time+Mathf.Max(.01f,seconds);
        Activations++;pulseUntil=Time.time+.12f;nextEcho=0;
        foreach(var npc in WorldSession.Instance.Npcs)Ignore(npc);
        CityNpc.Spawned+=Ignore;
        return true;
    }
    void Ignore(CityNpc npc)
    {
        if(!Active||npc==null)return;
        // CityNpc.Spawn creates exactly one root CapsuleCollider; keep pre-existing ignores owned by other systems.
        var c=npc.GetComponent<Collider>();
        if(c==null||Physics.GetIgnoreCollision(body,c))return;
        Physics.IgnoreCollision(body,c,true);ignored.Add(c);
    }
    void Update()
    {
        if(!Active)return;
        if(!Live){End();return;}
        if(Time.time>=until)End();
    }
    void LateUpdate()
    {
        if(Active&&Clear(transform.position,true))Remember();
        var camera=Camera.main;var rig=camera!=null?camera.GetComponent<ThirdPersonCamera>():null;
        bool first=rig!=null&&rig.isActiveAndEnabled&&rig.FirstPerson;
        bool visible=Active||Time.time<pulseUntil;
        if(outline==null)return;
        outline.enabled=visible&&!first;
        outline.widthMultiplier=Time.time<pulseUntil?.06f:.025f;
        if(Active&&!first&&Time.time>=nextEcho&&(nextEcho==0||
            (transform.position-echoPositions[(echoNext+EchoCount-1)%EchoCount]).sqrMagnitude>.0025f))
        {
            echoPositions[echoNext]=transform.position;echoRotations[echoNext]=transform.rotation;
            echoUntil[echoNext]=Time.time+.24f;echoNext=(echoNext+1)%EchoCount;nextEcho=Time.time+.09f;
        }
        for(int i=0;i<EchoCount;i++)
        {
            // World-space placement via cached pose; parent motion must not drag the old outline.
            echoes[i].transform.SetPositionAndRotation(echoPositions[i],echoRotations[i]);
            echoes[i].enabled=!first&&Time.time<echoUntil[i];
            echoes[i].widthMultiplier=.022f*Mathf.Clamp01((echoUntil[i]-Time.time)/.24f);
        }
        for(int i=0;i<2;i++)
        {
            var edge=edges[i];edge.enabled=visible&&first;if(!edge.enabled)continue;
            edge.useWorldSpace=true;edge.widthMultiplier=.0025f;
            float x=i==0?.09f:.91f;
            edge.SetPosition(0,camera.ViewportToWorldPoint(new Vector3(x,.35f,.3f)));
            edge.SetPosition(1,camera.ViewportToWorldPoint(new Vector3(x+(i==0?-.015f:.015f),.5f,.3f)));
            edge.SetPosition(2,camera.ViewportToWorldPoint(new Vector3(x,.65f,.3f)));
        }
    }
    // Record free positions along the actual walked path, not a point on the far side of a wall.
    void Remember(){history[historyNext]=transform.position;historyNext=(historyNext+1)%history.Length;historyCount=Mathf.Min(historyCount+1,history.Length);}
    void Capsule(Vector3 at,out Vector3 a,out Vector3 b,out float r)
    {
        r=Mathf.Max(.05f,body.radius-body.skinWidth);
        var center=at+body.center;float half=Mathf.Max(0,body.height*.5f-body.radius);
        a=center+Vector3.up*half;b=center-Vector3.up*half;
    }
    bool WorldClear(Vector3 at)=>Clear(at,false);
    bool Clear(Vector3 at,bool includeNpc)
    {
        Capsule(at,out var a,out var b,out float r);
        int count=Physics.OverlapCapsuleNonAlloc(a,b,r,overlaps,~0,QueryTriggerInteraction.Ignore);
        if(count==overlaps.Length)return false;
        for(int i=0;i<count;i++)
        {
            var c=overlaps[i];if(c.transform.root==transform)continue;
            if(c.GetComponentInParent<CityNpc>()!=null)continue;
            return false;
        }
        if(!includeNpc)return true;
        // World tolerates the controller's normal contact skin; NPC exit needs full-body clearance.
        count=Physics.OverlapCapsuleNonAlloc(a,b,body.radius+.02f,overlaps,~0,QueryTriggerInteraction.Ignore);
        if(count==overlaps.Length)return false;
        for(int i=0;i<count;i++)if(overlaps[i].GetComponentInParent<CityNpc>()!=null)return false;
        return true;
    }
    bool WorldPath(Vector3 target)
    {
        Vector3 delta=target-transform.position;if(delta.sqrMagnitude<.00001f)return true;
        Capsule(transform.position,out var a,out var b,out float r);
        int count=Physics.CapsuleCastNonAlloc(a,b,r,delta.normalized,hits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        if(count==hits.Length)return false;
        for(int i=0;i<count;i++)
            if(hits[i].collider.transform.root!=transform&&hits[i].collider.GetComponentInParent<CityNpc>()==null)return false;
        return true;
    }
    bool Recover(Vector3 target)
    {
        if(!Clear(target,true)||!WorldPath(target))return false;
        body.Move(target-transform.position);Physics.SyncTransforms();return Clear(transform.position,true);
    }
    bool ReversePathClear(Vector3 from,Vector3 to)
    {
        if(!Clear(from,true))return false;
        Vector3 delta=to-from;Capsule(from,out var a,out var b,out float r);
        float length=delta.magnitude;if(length<.001f)return true;
        int count=Physics.CapsuleCastNonAlloc(a,b,r,delta/length,hits,length,~0,QueryTriggerInteraction.Ignore);
        if(count==hits.Length)return false;
        for(int i=0;i<count;i++)
            if(hits[i].collider.transform.root!=transform&&hits[i].collider.GetComponentInParent<CityNpc>()==null&&
                hits[i].distance<length-body.skinWidth)return false;
        return true;
    }
    public void End()
    {
        if(!Active)return;
        // Resolve NPC overlaps while their pairs are still ignored, with world-swept controller motion only.
        if(body!=null&&body.enabled&&!Clear(transform.position,true))
        {
            bool recovered=false;
            Vector3 origin=transform.position;
            if(WorldClear(origin))
            {
                for(int radius=1;radius<=6&&!recovered;radius++)for(int i=0;i<16&&!recovered;i++)
                    recovered=Recover(origin+Quaternion.Euler(0,i*22.5f,0)*Vector3.forward*(radius*.3f));
            }
            // A world overlap can arise from an external moving collider. Retreat only toward a recorded
            // free pose on the same side: reverse sweep must reach within one controller skin of this pose.
            for(int i=0;i<historyCount&&!recovered;i++)
            {
                var target=history[(historyNext-1-i+history.Length)%history.Length];
                if(!WorldClear(origin)&&!ReversePathClear(target,origin))continue;
                recovered=Recover(target);
            }
            if(recovered)Recoveries++;
            else {UnresolvedRecoveries++;Debug.LogWarning("Phase exit found no world-safe free position; restoring NPC collisions without teleporting through geometry.");}
        }
        Active=false;CityNpc.Spawned-=Ignore;
        foreach(var c in ignored)if(c!=null&&body!=null)Physics.IgnoreCollision(body,c,false);
        ignored.Clear();pulseUntil=Time.time+.12f;
    }
    void OnDisable(){End();if(outline!=null)outline.enabled=false;foreach(var e in echoes)if(e!=null)e.enabled=false;foreach(var e in edges)if(e!=null)e.enabled=false;}
}
