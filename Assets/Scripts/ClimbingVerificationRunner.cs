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
        if(Stage=="city")yield return CityControls();else yield return FixtureControls();
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
        Check(W.Hero.AirJumpAvailable,"Wall latch did not spend or refill a fresh air jump.");
        begin=W.Hero.transform.position;
        Check(W.Hero.TryWallJump()&&!W.Hero.Climbing,"Wall jump detaches immediately.");
        yield return new WaitForSeconds(.25f);
        Check(W.Hero.transform.position.z<begin.z-.4f&&W.Hero.transform.position.y>begin.y+.7f,"Wall jump pushes out and up in real motion.");
        Check(W.Hero.AirJumpAvailable&&W.Hero.TryJump()&&!W.Hero.TryJump(),"Wall jump preserves exactly one unused air jump.");
        W.Hero.ScriptedClimbOverride=false;
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
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL small clutter cannot latch.");Destroy(tiny);
        var car=Wall(new Vector3(0,152,1),new Vector3(4,4,.4f));car.AddComponent<Rigidbody>().isKinematic=true;Physics.SyncTransforms();
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL moving or rigidbody car cannot latch.");Destroy(car);
        var floor=Wall(new Vector3(0,150.1f,1),new Vector3(5,.2f,3));
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL floor cannot latch.");Destroy(floor);
        var ceiling=Wall(new Vector3(0,152.5f,1),new Vector3(5,.2f,3));
        Check(!W.Hero.TryClimb(Vector3.forward),"CONTROL ceiling cannot latch.");Destroy(ceiling);
        yield return null;
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
            }
            W.Hero.ScriptedClimbOverride=false;W.Hero.ScriptedClimbInput=Vector2.zero;
            if(name=="Downtown")Check(roofY-start.y>45f,"Tall Downtown tower is included in generated-wall testing.");
        }
        yield return null;
    }
}
#endif
