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
    IEnumerator EnterBuild(HeroDefinition hero,string attackA,string attackB,string secondary)
    {
        yield return Home();
        Check(Profile.SetLoadout(hero,Power(attackA),Power(attackB),Power(secondary),hero.Primary,hero.Secondary),
            "Saved two attack powers and a separate secondary through progression.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));
        yield return Scene(GameFlow.CityScene);
        Check(W.Powers.EquippedA==Power(attackA)&&W.Powers.EquippedB==Power(attackB)&&W.Powers.EquippedSecondary==Power(secondary),
            "Session received the exact three-slot build.");
    }
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        using var legacyFlight=new LegacyFlightVerificationScope();
        if(Stage=="route"){Time.captureDeltaTime=1f/120;yield return CityRoute(false);yield return CityRoute(true);Time.captureDeltaTime=0;yield break;}
        if(Stage=="mantle"){Time.captureDeltaTime=1f/120;yield return MantleControls();yield return CityRoute(false);Time.captureDeltaTime=0;yield break;}
        if(Stage=="jump"){Time.captureDeltaTime=1f/120;yield return JumpControls();Time.captureDeltaTime=0;yield break;}
        yield return EnterBuild(F.Heroes[0],"strength","fire","flight");
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
        float expectedRise=W.Tuning.Movement.JumpSpeed*W.Tuning.Movement.JumpSpeed/(2*W.Tuning.Movement.Gravity);
        Check(peak-start>expectedRise*.92f&&peak-start<expectedRise*1.02f,"Ground jump matches configured ballistic rise.");
        PlaceHero(new Vector3(0,160,0),true);W.Hero.ScriptedFlight=true;W.Hero.ScriptedAscend=true;
        W.Hero.ScriptedRun=true;W.Hero.ScriptedMove=Vector3.forward;
        W.Powers.Flight.Fuel=W.Powers.Stats(W.Powers.Flight).Duration;
        Vector3 before=W.Hero.transform.position;float fuel=W.Hero.FlightFuel;
        for(int i=0;i<240;i++)yield return null;
        Vector3 delta=W.Hero.transform.position-before;
        Log($"MEASURED flight 2s: ascent={delta.y:F4}m, horizontal={new Vector2(delta.x,delta.z).magnitude:F4}m, fuel={fuel-W.Hero.FlightFuel:F4}, final velocity={CC.velocity}, lift={W.Tuning.Movement.FlightLift}, response={W.Tuning.Movement.FlightResponse}.");
        Check(delta.y>0&&delta.z>80,"Flight activates via real controller and resource gates.");
        if(Stage=="flight")
        {
            Check(delta.y>29.1983f*.8f&&delta.y<29.1983f*.9f,"Ascent measured with baseline harness is reduced10..20%.");
            Check(Mathf.Abs(CC.velocity.z-72)<.1f,"Full horizontal Flight speed remains72m/s.");
            Check(delta.z<143.475f&&delta.z>110,"Activation ramp lowers initial travel modestly while preserving strong horizontal distance.");
            // Switching to another motion owner must not preserve a stale full-speed flight ramp.
            W.Hero.MoveAbility(Vector3.zero,false);yield return null;yield return null;
            Check(W.Hero.PresentationState.Flying&&CC.velocity.z>0&&CC.velocity.z<30,"Ability motion handoff restarts the Flight acceleration ramp.");
            yield return new WaitForSeconds(.6f);
            Vector3 at=W.Hero.transform.position;
            var ledge=Wall(at+new Vector3(0,.1f,1.5f),new Vector3(4,.2f,2));
            var lip=Wall(at+new Vector3(0,.45f,.59f),new Vector3(4,.5f,.18f));
            W.Hero.ScriptedFlight=false;W.Hero.ScriptedMove=Vector3.zero;
            int priorMantles=W.Hero.MantleCount;
            Check(W.Hero.TryMantle(Vector3.forward),"Flight can hand movement to a valid nearby ledge.");
            float deadline=Time.time+2;while(W.Hero.Mantling&&Time.time<deadline)yield return null;
            Check(W.Hero.MantleCount==priorMantles+1,"Flight-to-mantle handoff completes through collision movement.");
            W.Hero.ScriptedFlight=true;W.Hero.ScriptedMove=Vector3.forward;
            yield return null;yield return null;
            Check(W.Hero.PresentationState.Flying&&CC.velocity.z>0&&CC.velocity.z<30,"Mantle handoff cannot resume stale instant full-speed Flight.");
            Destroy(ledge);Destroy(lip);
            W.Powers.Flight.Fuel=0;Check(!W.Powers.ConsumeFlight(.01f)&&!W.Powers.ConsumeFlight(0),"Empty/zero-dt flight controls refuse.");
            W.Powers.Tick(1,false);Check(W.Powers.Flight.Fuel==0,"Airborne recharge CONTROL stays empty.");
            W.Powers.Tick(1,true);Check(Mathf.Abs(W.Powers.Flight.Fuel-2.5f)<.001f,"Ground recharge remains2.5 units/s.");
        }
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
        yield return EnterBuild(F.Heroes[0],"fire","strength","flight");
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
    IEnumerator JumpControls()
    {
        // A non-Flight loadout proves traversal is universal rather than an ownership side effect.
        yield return Home();Profile.Data.Points=20;
        if(!Profile.Owns(Power("fire")))Check(Profile.Buy(Power("fire")),"Unlock non-Flight test loadout through progression.");
        yield return EnterBuild(F.Heroes[0],"strength","fire","speed");
        Isolate();PlaceHero(new Vector3(0,150.05f,0),true);
        yield return new WaitForSeconds(.2f);
        float floor=W.Hero.transform.position.y;
        Check(W.Hero.TryJump(),"Universal grounded jump without Flight.");
        Check(!W.Hero.TryJump(),"CONTROL repeated call before leaving ground cannot relaunch.");
        float first=floor;
        for(int i=0;i<43;i++){yield return null;first=Mathf.Max(first,W.Hero.transform.position.y);}
        Check(!CC.isGrounded&&W.Hero.TryJump(),"Exactly one air jump without Flight.");
        Check(!W.Hero.TryJump(),"CONTROL third jump refused immediately.");
        float second=W.Hero.transform.position.y,peak=second;int refused=0;
        for(int i=0;i<100;i++){yield return null;peak=Mathf.Max(peak,W.Hero.transform.position.y);if(!CC.isGrounded){Check(!W.Hero.TryJump(),"CONTROL airborne spam refused.");refused++;}}
        Log($"MEASURED double jump: first apex={first-floor:F4}m, air-jump rise={peak-second:F4}m, combined peak={peak-floor:F4}m.");
        float firstExpected=W.Tuning.Movement.JumpSpeed*W.Tuning.Movement.JumpSpeed/(2*W.Tuning.Movement.Gravity);
        float airExpected=W.Tuning.Movement.AirJumpSpeed*W.Tuning.Movement.AirJumpSpeed/(2*W.Tuning.Movement.Gravity);
        Check(first-floor>firstExpected*.92f&&first-floor<firstExpected*1.02f&&peak-second>airExpected*.9f&&peak-second<airExpected*1.02f,
            "Both jump rises match their configured ballistic speeds; no third jump.");
        yield return new WaitForSeconds(.5f);
        Check(CC.isGrounded&&W.Hero.AirJumpAvailable&&W.Hero.TryJump(),"Real landing restores one jump.");
        W.Mode.SetPaused(true);var held=W.Hero.transform.position;
        Check(!W.Hero.TryJump(),"CONTROL paused air jump refused.");
        for(int i=0;i<4;i++)yield return null;
        Check(Vector3.Distance(held,W.Hero.transform.position)<.001f,"Pause halts movement.");W.Mode.SetPaused(false);
        PlaceHero(new Vector3(59,150.05f,0),true);yield return new WaitForSeconds(.2f);
        W.Hero.ScriptedMove=Vector3.right;
        float deadline=Time.time+2;
        while(CC.isGrounded&&Time.time<deadline)yield return null;
        W.Hero.ScriptedMove=Vector3.zero;
        Check(!CC.isGrounded&&W.Hero.TryJump()&&!W.Hero.TryJump(),"Walk off floor edge: one air jump, no second air jump.");
        Check(Mathf.Abs(W.Hero.transform.up.y-1)<.0001f,"Jump never pitches or rolls physics root.");
        W.DamagePlayer(W.Health,true);Check(W.PlayerDead&&!W.Hero.TryJump(),"CONTROL dead hero cannot jump; no invulnerability granted.");
        yield return new WaitForSeconds(W.Tuning.Movement.RespawnDelay+.2f);
        Check(!W.PlayerDead&&W.Hero.AirJumpAvailable,"Respawn clears stale air-jump state through ResetMotion.");
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.3f);
        Check(W.Hero.TryBackflip(),"Existing grounded Backflip remains available.");
        Check(!W.Hero.TryJump(),"CONTROL jump cannot steal Backflip motion while active.");
        yield return new WaitForSeconds(1.2f);
        Check(CC.isGrounded&&W.Hero.TryJump(),"Normal jump restored after Backflip landing.");
        W.Mode.EndToResults();Check(!W.Hero.TryJump(),"CONTROL session-ended jump refused before scene transition.");
        yield return Scene(GameFlow.HomeScene);
        foreach(var hero in F.Heroes.Skip(1))
        {
            yield return Home();Profile.Data.Points=100;
            var pair=hero.AvailablePowers.Where(p=>!p.Effect.IsFlight).Take(2).ToArray();
            foreach(var power in pair)if(!Profile.Owns(power))Check(Profile.Buy(power),"Unlock non-Flight control loadout for "+hero.DisplayName);
            yield return EnterBuild(hero,pair[0].Id,pair[1].Id,"speed");
            Isolate();PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.2f);
            Check(!W.Powers.IsEquipped(Power("flight"))&&!W.Powers.ConsumeFlight(.1f)&&W.Hero.TryJump(),hero.DisplayName+" grounded jump without Flight; unequipped Flight refuses consumption.");
            yield return new WaitForSeconds(.36f);
            Check(W.Hero.TryJump()&&!W.Hero.TryJump(),hero.DisplayName+" exactly one air jump without Flight.");
        }

    }
    IEnumerator MantleControls()
    {
        yield return EnterBuild(F.Heroes[0],"strength","fire","flight");
        Isolate();
        foreach(float height in new[]{.7f,1.6f})
        {
            var ledge=Wall(new Vector3(0,150+height*.5f,1.5f),new Vector3(4,height,2));
            PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.2f);
            int count=W.Hero.MantleCount;
            Check(W.Hero.TryMantle(Vector3.forward),"Valid ledge begins mantle height="+height);
            Check(!W.Hero.TryMantle(Vector3.forward),"CONTROL cannot restart an active mantle.");
            float end=Time.time+2;while(W.Hero.Mantling&&Time.time<end)yield return null;
            yield return new WaitForSeconds(.3f);
            Check(W.Hero.MantleCount==count+1&&CC.isGrounded&&W.Hero.transform.position.y>150+height-.1f,"Mantle reaches supported top through CharacterController height="+height);
            Check(Mathf.Abs(W.Hero.transform.up.y-1)<.0001f,"Mantle keeps physics root upright.");
            Destroy(ledge);yield return null;
        }
        var wall=Wall(new Vector3(0,151.2f,1.5f),new Vector3(4,2.4f,2));
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.2f);
        Check(!W.Hero.TryMantle(Vector3.forward),"CONTROL ledge above maximum height refused.");
        wall.transform.localScale=new Vector3(4,20,.1f);wall.transform.position=new Vector3(0,160,1);
        Physics.SyncTransforms();Check(!W.Hero.TryMantle(Vector3.forward),"CONTROL sheer face with no reachable top refused.");
        wall.transform.localScale=new Vector3(4,1,2);wall.transform.position=new Vector3(0,150.5f,1.5f);Physics.SyncTransforms();
        Check(!W.Hero.TryMantle(Vector3.back),"CONTROL backward motion cannot mantle forward obstacle.");
        var ceiling=Wall(new Vector3(0,152.5f,1.5f),new Vector3(4,.2f,2));
        Check(!W.Hero.TryMantle(Vector3.forward),"CONTROL blocked standing headroom refused.");
        // Same real geometric probe repeated; allocation measured outside logging/formatting.
        W.Hero.TryMantle(Vector3.forward);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)W.Hero.TryMantle(Vector3.forward);
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Log($"ALLOC mantle blocked-headroom probe: {bytes} managed bytes /1000 calls.");
        Check(bytes==0,"Geometric traversal probe has no per-call managed allocations.");
        ceiling.transform.position=new Vector3(0,152.1f,0);ceiling.transform.localScale=new Vector3(4,.2f,1);Physics.SyncTransforms();
        Check(!W.Hero.TryMantle(Vector3.forward),"CONTROL low ceiling along lift path refused.");
        Destroy(wall);Destroy(ceiling);yield return null;
        var contact=W.Hero.gameObject.AddComponent<MovementMantleContactProbe>();
        var building=W.City.GetComponentsInChildren<ArtBuilding>().OrderBy(b=>b.transform.position.y).First();
        var roof=building.GetComponents<BoxCollider>().Where(c=>Mathf.Abs(c.size.y-.08f)<.01f&&c.size.x>3&&c.size.z>3).OrderByDescending(c=>c.bounds.max.y).First();
        var b=roof.bounds;
        PlaceHero(new Vector3(b.center.x,b.max.y+.08f,b.min.z-.55f),true);
        bool starts=W.Hero.TryMantle(Vector3.forward);
        if(!starts)
        {
            Vector3 feet=new Vector3(b.center.x,b.max.y+CC.skinWidth+.02f,b.min.z+CC.radius*2+CC.skinWidth);
            Vector3 center=feet+CC.center;float half=CC.height*.5f-CC.radius;
            foreach(var obstacle in Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,CC.radius,~0,QueryTriggerInteraction.Ignore))
                if(obstacle.transform.root!=W.Hero.transform)Log($"BLOCKED ROOF standing at={feet} collider={obstacle.name} bounds={obstacle.bounds}");
        }
        Check(!starts,"CONTROL actual rooftop billboard blocks the standing capsule.");
        PlaceHero(new Vector3(b.center.x+b.extents.x*.5f,b.max.y+.08f,b.min.z-.55f),true);
        Check(!W.Hero.TryMantle(Vector3.forward),"CONTROL antenna corner inside controller contact margin refused before movement.");
        PlaceHero(new Vector3(b.center.x+b.extents.x*.4f,b.max.y+.08f,b.min.z-.55f),true);
        Check(W.Hero.TryMantle(Vector3.forward),"Clear approach on same generated roof parapet begins mantle.");
        int prior=W.Hero.MantleCount;float deadline=Time.time+2;
        while(W.Hero.Mantling&&Time.time<deadline)yield return null;
        yield return new WaitForSeconds(.5f);
        Log($"MANTLE generated lip result={W.Hero.LastMantleResult} contact={contact.LastCollider?.name} bounds={contact.LastCollider?.bounds} normal={contact.Normal} stepOffset={CC.stepOffset} count={W.Hero.MantleCount-prior} grounded={CC.isGrounded} pos={W.Hero.transform.position} roof={b}");
        Check(W.Hero.MantleCount==prior+1&&CC.isGrounded&&W.Hero.transform.position.z>b.min.z,"Generated roof lip reached and grounded; no authored traversal components.");
        Log($"GEOMETRY roof-lip fixture building={building.name} roof={b} arrival={W.Hero.transform.position}. Initial placement is a fixture, not the street-to-roof route proof.");
    }
    IEnumerator CityRoute(bool flight)
    {
        yield return Home();Profile.Data.Points=100;
        if(!Profile.Owns(Power("fire")))Check(Profile.Buy(Power("fire")),"Unlock non-Flight route pair.");
        yield return EnterBuild(F.Heroes[0],"fire","strength",flight?"flight":"speed");
        // Seed2409: actual quay -> lighthouse pier west railing -> Harbour Light podium roof.
        // No fixture geometry or collider modifications; only the initial street placement is scripted.
        Check(W.Tuning.City.Seed==2409,"Reproducible route uses city seed2409.");
        var landmark=W.City.Plan.Structures.First(s=>s.Recipe=="Harbour Light");
        Vector3 start=new Vector3(-22,.13f,-207);
        PlaceHero(start,true);yield return new WaitForSeconds(.3f);
        Check(CC.isGrounded,"Route starts on real quay street support.");
        start=W.Hero.transform.position;
        int jumps=0;Action jumped=()=>jumps++;W.Hero.Jumped+=jumped;
        int mantles=W.Hero.MantleCount;float fuel=W.Hero.FlightFuel,time=Time.time;
        W.Hero.ScriptedRun=true;
        routeRecording=true;routeFuel=0;StartCoroutine(RecordRouteFuel());
        Check(W.Hero.TryJump(),"Route ground jump accepted.");
        if(flight)
        {
            yield return null;W.Hero.ScriptedFlight=W.Hero.ScriptedAscend=true;
            float end=Time.time+2;
            while(W.Hero.transform.position.y<landmark.Position.y+4.3f&&Time.time<end)yield return null;
            W.Hero.ScriptedAscend=false;
            Check(W.Hero.transform.position.y>landmark.Position.y+4,"Flight reaches podium clearance.");
        }
        yield return Travel(new Vector3(-18.8f,0,-207),flight?1.5f:3);
        if(!flight){W.Hero.ScriptedMove=Vector3.zero;yield return new WaitForSeconds(.8f);Check(CC.isGrounded,"Low pier railing cleared onto real support.");}
        yield return Travel(new Vector3(-14f,0,-246.8f),flight?2:7);
        if(!flight)Check(W.Hero.TryJump(),"Ground jump clears actual lighthouse-platform railing.");
        yield return Travel(new Vector3(-14f,0,-250.9f),flight?1:2);
        if(!flight)yield return new WaitForSeconds(.6f);
        W.Hero.ScriptedMove=Vector3.zero;
        if(!flight)
        {
            Check(CC.isGrounded&&W.Hero.TryJump(),"Roof approach starts a grounded jump.");
            yield return new WaitForSeconds(.35f);
            Check(W.Hero.TryJump()&&!W.Hero.TryJump(),"Roof approach uses one air jump; third refused.");
        }
        W.Hero.ScriptedMove=Vector3.back;
        float deadline=Time.time+3;
        while(W.Hero.transform.position.z>-252.7f&&Time.time<deadline)yield return null;
        W.Hero.ScriptedMove=Vector3.zero;W.Hero.ScriptedFlight=W.Hero.ScriptedAscend=false;W.Hero.ScriptedRun=false;
        yield return new WaitForSeconds(1);
        routeRecording=false;
        float seconds=Time.time-time,used=routeFuel;
        W.Hero.Jumped-=jumped;
        Check(CC.isGrounded&&W.Hero.transform.position.y>landmark.Position.y+3.9f&&W.Hero.transform.position.z<-252.3f,
            "Actual Harbour Light podium rooftop arrival, Flight="+flight+" at "+W.Hero.transform.position);
        var podium=Resources.Load<CityArtSettings>("CityArtSettings").Recipe(landmark.Recipe).Pieces[0];
        float radial=Vector3.ProjectOnPlane(W.Hero.transform.position-(landmark.Position+podium.Position),Vector3.up).magnitude;
        float radius=Mathf.Min(podium.Size.x,podium.Size.z)*.5f;
        Check(radial+CC.radius<radius,$"Arrival capsule is inside visible cylindrical podium footprint, radial={radial:F3}, radius={radius:F3}; not merely its box-collider corner.");
        Check(flight?used>0:!W.Powers.IsEquipped(Power("flight"))&&Mathf.Abs(used)<.001f&&W.Hero.MantleCount>mantles,
            "Route uses intended traversal resources, Flight="+flight);
        Rig.enabled=true;Rig.SetLook(155,25);yield return null;Capture(flight?"route-flight.png":"route-no-flight.png");
        Log($"ROUTE seed2409 flight={flight} start={start} end={W.Hero.transform.position} seconds={seconds:F4} vertical={W.Hero.transform.position.y-start.y:F4} jumps={jumps} mantles={W.Hero.MantleCount-mantles} fuelUsed={used:F4}. Podium roof only; no claim of access to the sheer upper lighthouse tower.");
    }
    IEnumerator Travel(Vector3 target,float timeout)
    {
        float end=Time.time+timeout;bool running=W.Hero.ScriptedRun;
        while(Time.time<end)
        {
            Vector3 delta=Vector3.ProjectOnPlane(target-W.Hero.transform.position,Vector3.up);
            if(delta.magnitude<.25f)break;
            if(delta.magnitude<.8f)W.Hero.ScriptedRun=false;
            W.Hero.ScriptedMove=delta.normalized;yield return null;
        }
        W.Hero.ScriptedMove=Vector3.zero;W.Hero.ScriptedRun=running;
        Check(Vector3.ProjectOnPlane(target-W.Hero.transform.position,Vector3.up).magnitude<.5f,"Route waypoint reached "+target+" actual="+W.Hero.transform.position);
    }
    bool routeRecording;float routeFuel;
    IEnumerator RecordRouteFuel()
    {
        float prior=W.Hero.FlightFuel;
        while(routeRecording){yield return null;float now=W.Hero.FlightFuel;routeFuel+=Mathf.Max(0,prior-now);prior=now;}
    }
}
public sealed class MovementMantleContactProbe : MonoBehaviour
{
    public Collider LastCollider;public Vector3 Normal;
    void OnControllerColliderHit(ControllerColliderHit hit)
    {if(GetComponent<SuperHeroController>().Mantling){LastCollider=hit.collider;Normal=hit.normal;}}
}
#endif
