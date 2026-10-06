#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed class ClimbingVerificationRunner : SessionVerificationRunner
{
    public string Stage="fixtures";
    protected override string Folder=>"Verification/Climbing/";
    protected override string ResultFile=>Stage+".txt";
    CharacterController CC=>W.Hero.GetComponent<CharacterController>();
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var hero=F.Heroes[0];
        Check(Profile.SetLoadout(hero,Power("fire"),Power("strength"),Power("force-field"),hero.Primary,hero.Secondary),
            "Climbing build uses two attacks and a non-Flight secondary.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));
        yield return Scene(GameFlow.CityScene);
        Time.captureDeltaTime=1f/120;
        if(Stage=="city")yield return CityControls();
        else {yield return FixtureControls();yield return FlightHandoff();}
        Time.captureDeltaTime=0;
    }
    IEnumerator FixtureControls()
    {
        Isolate();
        var wall=Wall(new Vector3(0,160,1),new Vector3(12,20,.4f));
        // A parapet lip stands above the walkable deck, as it does on generated city roofs.
        var roof=Wall(new Vector3(0,169.7f,2.2f),new Vector3(12,.1f,2));
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.2f);
        Check(CC.isGrounded,"Street-height fixture starts on real support.");
        Check(!W.Hero.TryClimb(Vector3.back),"CONTROL moving away cannot latch.");
        Check(W.Hero.TryClimb(Vector3.forward),"Tall static facade latches without a tag or power.");
        W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=new Vector2(0,1);
        Vector3 begin=W.Hero.transform.position;float start=Time.time;
        yield return new WaitForSeconds(.65f);
        Vector3 raised=W.Hero.transform.position;
        Log($"MEASURED climb vertical={(raised.y-begin.y)/(Time.time-start):F3}m/s rise={raised.y-begin.y:F3}m wallGap={.8f-raised.z-CC.radius:F3}m.");
        Check(W.Hero.Climbing&&raised.y-begin.y>2.6f&&raised.y-begin.y<3.7f,"Attached climb moves at configured vertical speed.");
        Check(Mathf.Abs(.8f-raised.z-CC.radius)<.3f,"Wall distance remains bounded while attached.");
        W.Hero.ScriptedClimbInput=new Vector2(1,0);start=Time.time;begin=W.Hero.transform.position;
        yield return new WaitForSeconds(.55f);
        Log($"MEASURED climb sideways={(W.Hero.transform.position.x-begin.x)/(Time.time-start):F3}m/s move={W.Hero.transform.position.x-begin.x:F3}m.");
        Check(W.Hero.Climbing&&W.Hero.transform.position.x-begin.x>1.7f,"Sideways traversal follows the wall.");
        var rig=Cam.GetComponent<ThirdPersonCamera>();rig.enabled=true;
        foreach(bool first in new[]{false,true})
        {
            if(rig.FirstPerson!=first)Check(rig.ToggleView(),"Switch climbing camera view.");
            rig.SetLook(0,first?-75f:-65f);yield return null;yield return null;
            float radius=first?.14f:.19f;
            bool embedded=false;
            foreach(var hit in Physics.OverlapSphere(Cam.transform.position,radius,~0,QueryTriggerInteraction.Ignore))
                if(hit.transform.root!=W.Hero.transform)embedded=true;
            Check(!embedded&&Mathf.Abs(W.Hero.transform.up.y-1)<.0001f,
                "Climbing camera stays outside geometry and root upright, first-person="+first);
            Check(Vector3.Angle(Cam.ViewportPointToRay(new Vector3(.5f,.5f)).direction,Cam.transform.forward)<.01f,
                "Climbing preserves center-screen aiming, first-person="+first);
        }
        Check(W.Powers.UseSecondary()&&W.Hero.Climbing,"Force Field can activate without detaching from a wall.");
        Check(W.Hero.AirJumpAvailable,"Wall latch did not spend or refill a fresh air jump.");
        W.Hero.ScriptedClimbCancel=true;yield return null;
        Check(!W.Hero.Climbing&&W.Hero.LastClimbResult=="Released","Climb-cancel releases the wall without a jump.");
        float releaseHeight=W.Hero.transform.position.y;yield return new WaitForSeconds(.15f);
        Check(W.Hero.transform.position.y<releaseHeight-.05f,"Released hero returns to actual falling motion.");
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.1f);
        Check(W.Hero.TryClimb(Vector3.forward),"Wall jump fixture re-latches after a real reset.");
        begin=W.Hero.transform.position;
        Check(W.Hero.TryWallJump()&&!W.Hero.Climbing,"Wall jump detaches immediately.");
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL same-wall re-latch is refused during wall-jump cooldown.");
        yield return new WaitForSeconds(.25f);
        Check(W.Hero.transform.position.z<begin.z-.4f&&W.Hero.transform.position.y>begin.y+.7f,"Wall jump pushes out and up in real motion.");
        Check(W.Hero.AirJumpAvailable&&W.Hero.TryJump()&&!W.Hero.TryJump(),"Wall jump preserves exactly one unused air jump.");
        W.Hero.ScriptedClimbOverride=false;
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.1f);
        Check(W.Hero.TryJump(),"Spent-air-jump control launches from ground.");
        yield return new WaitForSeconds(.12f);
        Check(W.Hero.TryJump()&&!W.Hero.AirJumpAvailable,"Air jump can be spent before reaching a wall.");
        Check(W.Hero.TryClimb(Vector3.forward)&&!W.Hero.AirJumpAvailable,
            "Latching while airborne does not refill a spent air jump.");
        Check(W.Hero.TryWallJump()&&!W.Hero.TryJump(),
            "Wall jump after a spent air jump cannot create another air jump.");
        float groundDeadline=Time.time+5;
        while(!CC.isGrounded&&Time.time<groundDeadline)yield return null;
        Check(CC.isGrounded&&W.Hero.AirJumpAvailable,"Actual landing restores the spent air jump.");
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.2f);
        Check(W.Hero.TryClimb(Vector3.forward),"Full-storey top-out route re-latches after actual reset.");
        W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=new Vector2(0,1);
        float deadline=Time.time+8;
        while((W.Hero.Climbing||W.Hero.Mantling)&&Time.time<deadline)yield return null;
        Log($"MEASURED top-out state-end pos={W.Hero.transform.position} result={W.Hero.LastClimbResult}/{W.Hero.LastMantleResult}.");
        yield return new WaitForSeconds(.35f);
        Log($"MEASURED top-out pos={W.Hero.transform.position} grounded={CC.isGrounded} result={W.Hero.LastClimbResult}/{W.Hero.LastMantleResult}.");
        Check(!W.Hero.Climbing&&!W.Hero.Mantling&&CC.isGrounded&&W.Hero.transform.position.y>169.8f&&W.Hero.transform.position.z>1.2f,
            "Full-height wall reaches supported roof through collision-checked top-out.");
        Check(Mathf.Abs(W.Hero.transform.up.y-1)<.0001f,"Physics root stays upright on the roof.");
        Destroy(wall);Destroy(roof);yield return null;
        W.Hero.ScriptedClimbInput=Vector2.zero;
        yield return NegativeControls();
    }
    IEnumerator NegativeControls()
    {
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.1f);
        var tiny=Wall(new Vector3(0,150.6f,1),new Vector3(.8f,1.2f,.4f));
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL small clutter cannot latch.");tiny.SetActive(false);Destroy(tiny);
        var car=Wall(new Vector3(0,152,1),new Vector3(4,4,.4f));car.AddComponent<Rigidbody>().isKinematic=true;Physics.SyncTransforms();
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL moving or rigidbody car cannot latch.");car.SetActive(false);Destroy(car);
        var floor=Wall(new Vector3(0,150.1f,1),new Vector3(5,.2f,3));
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL floor cannot latch.");floor.SetActive(false);Destroy(floor);
        var ceiling=Wall(new Vector3(0,152.5f,1),new Vector3(5,.2f,3));
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL ceiling cannot latch.");ceiling.SetActive(false);Destroy(ceiling);
        var angled=Wall(new Vector3(0,154,1),new Vector3(5,8,.4f));angled.transform.rotation=Quaternion.Euler(22,0,0);Physics.SyncTransforms();
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL wall outside normal tolerance cannot latch.");angled.SetActive(false);Destroy(angled);
        var npc=Actor(new Vector3(0,150.05f,1),NpcRole.Civilian,20);
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL NPC capsule cannot latch.");npc.gameObject.SetActive(false);Destroy(npc.gameObject);
        yield return null;
        // Hot-path control away from a wall, without logging or scene queries inside the sample.
        W.Hero.TryClimb(Vector3.forward);
        long beforeAlloc=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)W.Hero.TryClimb(Vector3.forward);
        long probeBytes=GC.GetAllocatedBytesForCurrentThread()-beforeAlloc;
        Log($"ALLOC no-wall climbing probe: {probeBytes} managed bytes /1000 calls.");
        Check(probeBytes==0,"No-wall latch probe makes no per-call managed allocation.");
        var probeWatch=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<10000;i++)W.Hero.TryClimb(Vector3.forward);
        probeWatch.Stop();
        double probeMilliseconds=probeWatch.Elapsed.TotalMilliseconds/10000.0;
        Log($"MEASURED no-wall climbing probe: {probeMilliseconds:F6} ms/call over 10000 calls.");
        Check(probeMilliseconds<.05,"No-wall latch probe stays below 0.05 ms per call in this Editor run.");
        var unsupportedWall=Wall(new Vector3(0,156,1),new Vector3(6,12,.4f));
        var unsupportedLip=Wall(new Vector3(0,162.2f,1.2f),new Vector3(6,.1f,.6f));
        PlaceHero(new Vector3(0,160.2f,0),true);yield return null;
        Check(W.Hero.TryClimb(Vector3.forward),"Unsupported overhang fixture latches below the lip.");
        W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=new Vector2(0,1);
        int unsupportedMantles=W.Hero.MantleCount;yield return new WaitForSeconds(.45f);
        Log($"MEASURED unsupported overhang pos={W.Hero.transform.position} refusal={W.Hero.LastTopOutFailure}.");
        Check(W.Hero.MantleCount==unsupportedMantles&&W.Hero.transform.position.y<162.5f,
            "CONTROL overhang without a standing point cannot top out or cross the solid lip.");
        Destroy(unsupportedWall);Destroy(unsupportedLip);
        W.Hero.ScriptedClimbInput=Vector2.zero;
        var blockedWall=Wall(new Vector3(0,156,1),new Vector3(6,12,.4f));
        var blockedRoof=Wall(new Vector3(0,161.7f,2.2f),new Vector3(6,.1f,2));
        var blockedCeiling=Wall(new Vector3(0,163,1.4f),new Vector3(6,.3f,3));
        int priorMantles=W.Hero.MantleCount;
        PlaceHero(new Vector3(0,159.8f,0),true);yield return null;
        Check(W.Hero.TryClimb(Vector3.forward),"Blocked top-out fixture latches below its lip.");
        W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=new Vector2(0,1);
        yield return new WaitForSeconds(.5f);
        Log($"MEASURED blocked top-out pos={W.Hero.transform.position} refusal={W.Hero.LastTopOutFailure} mantle={W.Hero.LastMantleResult}.");
        Check(W.Hero.transform.position.y<162f&&W.Hero.MantleCount==priorMantles,
            "CONTROL overhead obstruction prevents top-out without crossing solid geometry.");
        Destroy(blockedWall);Destroy(blockedRoof);Destroy(blockedCeiling);
        W.Hero.ScriptedClimbInput=Vector2.zero;
        var wall=Wall(new Vector3(0,156,1),new Vector3(6,12,.4f));
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.1f);
        Check(W.Hero.TryClimb(Vector3.forward),"State cleanup fixture latches.");
        W.Mode.SetPaused(true);Vector3 paused=W.Hero.transform.position;
        for(int i=0;i<4;i++)yield return null;
        Check(!W.Hero.Climbing&&Vector3.Distance(paused,W.Hero.transform.position)<.001f,"Pause clears climb state without moving the hero.");
        W.Mode.SetPaused(false);Check(W.Hero.TryClimb(Vector3.forward),"Unpaused hero can relatch legitimately.");
        W.DamagePlayer(W.Health,true);yield return null;
        Check(W.PlayerDead&&!W.Hero.Climbing,"Death clears climbing state.");
        Destroy(wall);
    }
    IEnumerator FlightHandoff()
    {
        yield return new WaitForSeconds(W.Tuning.Movement.RespawnDelay+.2f);
        yield return Home();
        var hero=F.Heroes[0];
        Check(Profile.SetLoadout(hero,Power("fire"),Power("strength"),Power("flight"),hero.Primary,hero.Secondary),
            "Flight handoff fixture equips Flight through progression.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));
        yield return Scene(GameFlow.CityScene);
        Isolate();Wall(new Vector3(0,156,1),new Vector3(6,12,.4f));
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.15f);
        Check(W.Hero.TryClimb(Vector3.forward),"Flight handoff starts on a real attached wall.");
        W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=new Vector2(0,1);
        yield return new WaitForSeconds(.15f);
        float fuel=W.Hero.FlightFuel;W.Hero.ScriptedFlight=true;yield return null;
        Check(!W.Hero.Climbing&&W.Hero.PresentationState.Flying&&W.Hero.FlightFuel<fuel,
            "Equipped Flight spends fuel once and cleanly takes movement from climbing.");
        W.Hero.ScriptedFlight=false;W.Hero.ScriptedClimbOverride=false;W.Hero.ScriptedMove=Vector3.zero;
        yield return null;yield return null;
        Check(!W.Hero.Climbing,"Ending Flight against a wall does not latch without movement input.");
        PlaceHero(new Vector3(0,150.05f,0),true);yield return null;
        Check(W.Hero.TryClimb(Vector3.forward),"Session-end control starts attached to a wall.");
        var attached=W.Hero;W.Mode.ReturnHome();yield return Scene(GameFlow.HomeScene);
        Check(attached==null||!attached.Climbing,"Session end destroys or clears the old attached controller.");
    }
    IEnumerator CityControls()
    {
        // Actual seeded facades; no authored traversal component or fixture collider is added.
        Check(W.Tuning.City.Seed==2409,"City route uses reproducible generated seed 2409.");
        var buildings=W.City.GetComponentsInChildren<ArtBuilding>();
        foreach(var name in new[]{"Downtown","Residential","Docks"})
        {
            int district=W.City.Plan.Districts.FindIndex(d=>d.Name==name);
            Check(district>=0,"Generated district exists: "+name);
            var candidates=buildings.Where(b=>b.District==district)
                .OrderByDescending(b=>name=="Downtown"?b.GetComponents<BoxCollider>().Max(c=>c.bounds.max.y):0)
                .ToArray();
            ArtBuilding chosen=null;BoxCollider face=null;Vector3 start=Vector3.zero;
            foreach(var candidate in candidates)
            {
                var ground=candidate.GetComponents<BoxCollider>()
                    .Where(c=>c.bounds.size.y>=W.Tuning.Movement.ClimbMinimumWallHeight&&
                        c.bounds.size.x>3&&c.bounds.size.z>3&&
                        Mathf.Abs(c.bounds.min.y-candidate.transform.position.y)<.3f)
                    .OrderByDescending(c=>c.bounds.size.x*c.bounds.size.z).FirstOrDefault();
                if(ground==null)continue;
                var bounds=ground.bounds;
                var at=new Vector3(bounds.min.x-CC.radius-.12f,bounds.min.y+.05f,bounds.center.z);
                PlaceHero(at,true);
                if(!W.Hero.TryClimb(Vector3.right))continue;
                chosen=candidate;face=ground;start=W.Hero.transform.position;break;
            }
            Check(chosen!=null,$"Street approach latches onto real {name} structural wall.");
            if(name=="Docks")
            {
                // Start outside latch range on the generated street, then let normal Update movement acquire the wall.
                var approach=start-Vector3.right*1.4f;
                PlaceHero(approach,true);yield return new WaitForSeconds(.15f);
                Check(CC.isGrounded,"Docks route begins on genuine street support outside latch range.");
                W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=new Vector2(0,1);
                W.Hero.ScriptedMove=Vector3.right;
                float latchDeadline=Time.time+2;
                while(!W.Hero.Climbing&&Time.time<latchDeadline)yield return null;
                W.Hero.ScriptedMove=Vector3.zero;
                Check(W.Hero.Climbing,"Real street movement automatically latches to the generated warehouse wall.");
                start=W.Hero.transform.position;
            }
            float roofY=chosen.GetComponents<BoxCollider>()
                .Where(c=>c.bounds.size.y<1f&&c.bounds.size.x>3&&c.bounds.size.z>3)
                .Max(c=>c.bounds.max.y);
            W.Hero.ScriptedClimbOverride=true;W.Hero.ScriptedClimbInput=new Vector2(0,1);
            float started=Time.time;yield return new WaitForSeconds(.75f);
            Vector3 raised=W.Hero.transform.position;
            Log($"MEASURED generated {name} building={chosen.name} start={start} height={roofY-start.y:F2}m rise={raised.y-start.y:F3}m speed={(raised.y-start.y)/(Time.time-started):F3}m/s.");
            Check(W.Hero.Climbing&&raised.y-start.y>3f,$"Generated {name} facade climbs a full storey.");
            W.Hero.ScriptedClimbInput=new Vector2(1,0);started=Time.time;
            yield return new WaitForSeconds(.4f);
            Log($"MEASURED generated {name} sideways={Vector3.Distance(new Vector3(raised.x,0,raised.z),new Vector3(W.Hero.transform.position.x,0,W.Hero.transform.position.z)):F3}m.");
            Check(W.Hero.Climbing&&Vector3.Distance(new Vector3(raised.x,0,raised.z),new Vector3(W.Hero.transform.position.x,0,W.Hero.transform.position.z))>1f,
                $"Generated {name} facade supports sideways traversal.");
            if(name=="Docks")
            {
                W.Hero.ScriptedClimbInput=new Vector2(0,1);
                float deadline=Time.time+10;
                while((W.Hero.Climbing||W.Hero.Mantling)&&Time.time<deadline)yield return null;
                yield return new WaitForSeconds(.4f);
                Log($"MEASURED generated Docks roof result={W.Hero.LastClimbResult}/{W.Hero.LastMantleResult} refusal={W.Hero.LastTopOutFailure} top={W.Hero.LastTopOutTop?.name}@{W.Hero.LastTopOutHeight:F3} blocker={W.Hero.LastTopOutBlocker?.name}/{W.Hero.LastTopOutBlocker?.bounds} pos={W.Hero.transform.position} roof={roofY:F3} bounds={face.bounds}.");
                Check(CC.isGrounded&&W.Hero.transform.position.y>roofY-.9f&&W.Hero.transform.position.x>face.bounds.min.x,
                    "Generated Docks building route tops out on its real supported roof.");
                W.Hero.ScriptedClimbOverride=false;W.Hero.ScriptedClimbInput=Vector2.zero;
                Check(W.Hero.TryJump(),"Generated roof launches a normal grounded jump for the return route.");
                W.Hero.ScriptedMove=Vector3.left;
                float leaveDeadline=Time.time+3;
                while(W.Hero.transform.position.x>face.bounds.min.x-2f&&Time.time<leaveDeadline)yield return null;
                W.Hero.ScriptedMove=Vector3.zero;
                Check(!CC.isGrounded&&W.Hero.TryJump()&&!W.Hero.TryJump(),
                    "After leaving the real roof, exactly one air jump remains available.");
                float landDeadline=Time.time+5;
                while(!CC.isGrounded&&Time.time<landDeadline)yield return null;
                Log($"MEASURED generated Docks return pos={W.Hero.transform.position} grounded={CC.isGrounded}.");
                Check(CC.isGrounded&&W.Hero.transform.position.y<2f,"Generated street-to-roof route returns to ground.");
                ArtBuilding second=null;BoxCollider secondFace=null;
                foreach(var candidate in candidates.Where(b=>b!=chosen))
                {
                    var structural=candidate.GetComponents<BoxCollider>()
                        .Where(c=>c.bounds.size.y>=W.Tuning.Movement.ClimbMinimumWallHeight&&
                            c.bounds.size.x>3&&c.bounds.size.z>3&&
                            Mathf.Abs(c.bounds.min.y-candidate.transform.position.y)<.3f)
                        .OrderByDescending(c=>c.bounds.size.x*c.bounds.size.z).FirstOrDefault();
                    if(structural==null)continue;
                    var bounds=structural.bounds;
                    PlaceHero(new Vector3(bounds.min.x-CC.radius-.12f,bounds.min.y+.05f,bounds.center.z),true);
                    if(!W.Hero.TryClimb(Vector3.right))continue;
                    second=candidate;secondFace=structural;break;
                }
                Check(second!=null,"A different generated Docks wall supports the return wall jump.");
                Vector3 secondStart=W.Hero.transform.position;
                Check(W.Hero.TryWallJump()&&!W.Hero.Climbing,"Real second-building wall jump detaches.");
                yield return new WaitForSeconds(.22f);
                Log($"MEASURED generated Docks second-wall jump building={second.name} from={secondStart} to={W.Hero.transform.position} face={secondFace.bounds}.");
                Check(W.Hero.transform.position.x<secondStart.x-.35f&&W.Hero.transform.position.y>secondStart.y+.65f,
                    "Second generated wall jump produces real away/up displacement.");
                Check(W.Hero.TryJump()&&!W.Hero.TryJump(),"Second generated wall jump retains exactly one air jump.");
                landDeadline=Time.time+5;
                while(!CC.isGrounded&&Time.time<landDeadline)yield return null;
                Check(CC.isGrounded&&W.Hero.transform.position.y<2f,"Second wall-jump route returns to street support.");
            }
            W.Hero.ScriptedClimbOverride=false;W.Hero.ScriptedClimbInput=Vector2.zero;
            if(name=="Downtown")Check(roofY-start.y>45f,"Tall Downtown tower is included in generated-wall testing.");
        }
        yield return null;
    }
}
#endif
