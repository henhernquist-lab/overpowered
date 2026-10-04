#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed class MovementVerificationRunner : SessionVerificationRunner
{
    public string Stage="baseline";
    protected override string Folder => "Verification/Movement/";
    protected override string ResultFile => Stage+".txt";
    CharacterController CC => W.Hero.GetComponent<CharacterController>();
    ThirdPersonCamera Rig => Cam.GetComponent<ThirdPersonCamera>();
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Enter(F.Heroes[0],"flight","strength","free-play");
        foreach(var group in W.City.Buildings.GroupBy(b=>b.District))
            Log($"GEOMETRY {W.City.Plan.Districts[group.Key].Name}: {group.Count()} buildings, height {group.Min(b=>b.Size.y):F3}..{group.Max(b=>b.Size.y):F3}m.");
        Time.captureDeltaTime=1f/120;
        Isolate(); PlaceHero(new Vector3(0,150.05f,0),true);
        yield return new WaitForSeconds(.3f);
        Check(CC.isGrounded,"Baseline fixture genuinely grounded.");
        float start=W.Hero.transform.position.y,peak=start;
        Check(W.Hero.TryJump(),"Grounded jump accepted.");
        for(int i=0;i<150;i++){yield return null;peak=Mathf.Max(peak,W.Hero.transform.position.y);}
        Log($"MEASURED first jump peak={peak-start:F4}m, launch={W.Tuning.Movement.JumpSpeed:F3}m/s, gravity={W.Tuning.Movement.Gravity:F3}m/s², dt={Time.deltaTime:F6}.");
        Check(peak-start>1.3f&&peak-start<1.6f,"Ground jump remains within original ballistic envelope.");
        PlaceHero(new Vector3(0,160,0),true);W.Hero.ScriptedFlight=true;W.Hero.ScriptedAscend=true;
        W.Hero.ScriptedRun=true;W.Hero.ScriptedMove=Vector3.forward;
        W.Powers.Flight.Fuel=W.Powers.Stats(W.Powers.Flight).Duration;
        Vector3 before=W.Hero.transform.position;float fuel=W.Hero.FlightFuel;
        for(int i=0;i<240;i++)yield return null;
        Vector3 delta=W.Hero.transform.position-before;
        Log($"MEASURED flight 2s: ascent={delta.y:F4}m, horizontal={new Vector2(delta.x,delta.z).magnitude:F4}m, fuel={fuel-W.Hero.FlightFuel:F4}, final velocity={CC.velocity}, lift={W.Tuning.Movement.FlightLift}, response={W.Tuning.Movement.FlightResponse}.");
        Check(delta.y>0&&delta.z>80,"Flight activates via real controller and resource gates.");
        W.Hero.ScriptedFlight=W.Hero.ScriptedAscend=W.Hero.ScriptedRun=false;W.Hero.ScriptedMove=Vector3.zero;
        W.Hero.enabled=false;
        PlaceHero(new Vector3(0,150.05f,0));Rig.enabled=true;
        foreach(bool first in new[]{false,true})
        {
            if(Rig.FirstPerson!=first)Check(Rig.ToggleView(),"Toggle view for pitch controls.");
            float min=first?W.Tuning.Camera.FirstPersonMinimumPitch:W.Tuning.Camera.MinimumPitch;
            float max=first?W.Tuning.Camera.FirstPersonMaximumPitch:W.Tuning.Camera.MaximumPitch;
            foreach(float pitch in new[]{min,max})
            {
                Rig.SetLook(0,pitch);yield return null;yield return null;
                Vector3 expected=Cam.transform.forward;Vector3 position=Cam.transform.position;
                var c=W.Tuning.Camera;
                Vector3 analytic=first?Quaternion.Euler(pitch,0,0)*Vector3.forward:(Vector3.up*c.LookHeight-Quaternion.Euler(pitch,0,0)*c.Offset).normalized;
                Check(Vector3.Angle(analytic,expected)<.03f,"View reaches configured limit through existing orbit/eye math.");
                Log($"MEASURED camera first={first} input={pitch:F2} renderedPitch={Mathf.Asin(-expected.y)*Mathf.Rad2Deg:F3} camera={position}.");
                Rig.SetLook(0,pitch==min?min-50:max+50);yield return null;yield return null;
                Check(Vector3.Angle(expected,Cam.transform.forward)<.01f,"CONTROL pitch overshoot clamps at "+pitch+" first="+first);
                Check(Vector3.Angle(Cam.ViewportPointToRay(new Vector3(.5f,.5f,0)).direction,Cam.transform.forward)<.01f,"Center ray remains aligned with crosshair.");
                Check(Mathf.Abs(Cam.transform.right.y)<.001f&&Quaternion.Angle(W.Hero.transform.rotation,Quaternion.identity)<.01f,"No camera roll or physics-root rotation.");
                Check(!Physics.OverlapSphere(Cam.transform.position,first?.14f:.19f,~0,QueryTriggerInteraction.Ignore).Any(c=>c.transform.root!=W.Hero.transform),"Camera collision volume clear at configured limit.");
            }
        }
        if(Stage=="camera")yield return UpwardAimControls();
        Time.captureDeltaTime=0;
    }
    IEnumerator UpwardAimControls()
    {
        yield return Home();Profile.Data.Points=20;
        if(!Profile.Owns(Power("fire")))Check(Profile.Buy(Power("fire")),"Unlock Fire through existing progression for camera verification.");
        yield return Enter(F.Heroes[0],"fire","strength","free-play");
        Isolate();Rig.enabled=true;
        foreach(bool first in new[]{false,true})
        {
            if(Rig.FirstPerson!=first)Check(Rig.ToggleView(),"Change aim verification view.");
            Rig.SetLook(0,first?W.Tuning.Camera.FirstPersonMinimumPitch:W.Tuning.Camera.MinimumPitch);
            yield return null;yield return null;
            var ray=Cam.ViewportPointToRay(new Vector3(.5f,.5f));
            float near=Mathf.Max(0,Vector3.Dot(W.Powers.AimOrigin-ray.origin,ray.direction));
            var target=Wall(ray.GetPoint(near+9),Vector3.one*.7f);
            var rb=target.AddComponent<Rigidbody>();rb.useGravity=false;rb.mass=45;
            var probe=target.AddComponent<FirstPersonImpactProbe>();
            var off=Wall(target.transform.position+Cam.transform.right*5,Vector3.one*.7f);
            off.AddComponent<Rigidbody>().useGravity=false;var miss=off.AddComponent<FirstPersonImpactProbe>();
            Physics.SyncTransforms();W.Powers.Tick(100,true);W.Powers.Select(Runtime("fire"));
            Check(W.Powers.FindTarget(20,out var hit)&&hit.rigidbody==rb,"Upward limit gameplay aim hits crosshair target first="+first);
            Check(W.Powers.Use(Runtime("fire")),"Upward-limit Fire cast accepted.");
            float until=Time.time+2;while(!probe.Hit&&Time.time<until)yield return null;
            Check(probe.Hit&&!miss.Hit,"Physical projectile hits upward target; off-axis CONTROL untouched first="+first);
            Destroy(target);Destroy(off);yield return null;
        }
    }
}
#endif
