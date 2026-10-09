#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.IO;
using UnityEngine;

public sealed class PhaseShiftVerificationRunner : SessionVerificationRunner
{
    public string Stage;
    protected override string Folder=>"Verification/PhaseShift/";
    protected override string ResultFile=>Stage+".txt";
    CharacterController CC=>W.Hero.GetComponent<CharacterController>();
    IEnumerator EnterPhase(string a="fire",string b="strength",string secondary="phase-shift")
    {
        yield return Home();var h=F.Heroes[0];
        Check(Profile.SetLoadout(h,Power(a),Power(b),Power(secondary),h.Primary,h.Secondary),"Save legal loadout with "+secondary);
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));yield return Scene(GameFlow.CityScene);
    }
    void Refill(){var p=W.Powers.SecondaryRuntime;p.Charges=2;p.Cooldown=0;W.Powers.Tick(100,true);}
    IEnumerator WaitUntil(Func<bool> predicate,float seconds,string label)
    {
        float end=Time.time+seconds;while(!predicate()&&Time.time<end)yield return null;
        Check(predicate(),label);
    }
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        if(Stage=="baseline"){yield return Enter("fire","strength");yield return DenseCombat();yield break;}
        if(Stage=="reload")
        {
            Check(Profile.EquippedSecondary==Power("phase-shift"),"Separate process reload keeps repaired Phase Shift secondary.");
            Check(Profile.Data.Level==7&&Profile.Data.Xp==23&&Profile.Data.Points==11&&Profile.Data.Loadout.PowerA=="fire"&&Profile.Data.Loadout.PowerB=="ice"&&Profile.Data.Loadout.HeroId==F.Heroes[0].Id,
                "Separate process preserves migrated hero, primary powers, level, XP and points.");
            Check(Profile.Data.Loadout.Primary==CityColor.Red&&Profile.Data.Loadout.Secondary==CityColor.Cyan,"Separate process preserves suit colours.");yield break;
        }
        Check(Profile.EquippedSecondary==Power("phase-shift"),"Fresh save selects Phase Shift, never Flight.");
        var expected=new[]{"phase-shift","speed","teleport","force-field"};
        Check(F.Heroes.All(h=>h.AvailablePowers.Where(p=>p.Role==PowerRole.Secondary).Select(p=>p.Id).OrderBy(x=>x).SequenceEqual(expected.OrderBy(x=>x))),"Every hero exposes exactly the four intended secondaries.");
        Check(!Profile.SetLoadout(F.Heroes[0],Power("fire"),Power("ice"),Power("flight"),CityColor.Red,CityColor.Cyan),"Legacy Flight selection is refused through normal save gate.");
        yield return EnterPhase();Isolate();
        var user=W.Powers;var phase=user.Phase;var rt=user.SecondaryRuntime;var fire=Runtime("fire");
        Check(rt.Definition.Duration==.75f&&rt.Definition.Charges==2&&rt.Definition.ChargeRecharge==5&&rt.Definition.ResourceCost==0,"Phase data: .75 seconds, two charges, 5 seconds recharge, zero energy.");
        var npc=Actor(new Vector3(0,150,1),NpcRole.Criminal,1000);var collider=npc.GetComponent<Collider>();
        float energy=user.Energy,started=Time.time;int charges=rt.Charges;
        Check(user.UseSecondary()&&phase.Active&&rt.Charges==charges-1&&user.Energy==energy,"Activation spends exactly one charge and zero energy.");
        Check(CC.enabled&&Physics.GetIgnoreCollision(CC,collider),"Only NPC pair ignored; CharacterController remains enabled.");
        var spawned=Actor(new Vector3(5,150,1),NpcRole.Criminal,1000);
        Check(Physics.GetIgnoreCollision(CC,spawned.GetComponent<Collider>()),"NPC spawned during phase joins the temporary ignore set.");
        float health=W.Health;W.DamagePlayer(30);Check(W.Health==health,"Ordinary incoming damage refused.");
        W.DamagePlayer(3,true);Check(W.Health==health-3,"Explicit unblockable damage still applies.");
        int attackCharges=fire.Charges;float cooldown=fire.Cooldown;float enemyHealth=npc.Health;
        Check(!user.Use(fire)&&!W.Hero.TryPunch()&&!W.Hero.TryHurricaneKick()&&!user.SynergyRunner.TryActivate(),"Primary, melee, Hurricane Kick and synergy all refuse during phase.");
        Check(fire.Charges==attackCharges&&fire.Cooldown==cooldown&&user.Energy==energy&&npc.Health==enemyHealth&&FireProjectilePool.Instance.ActiveCount==0,"Refused offense spends no resources, starts no cooldown, spawns no projectile and deals no damage.");
        CC.Move(Vector3.forward*2.5f);Physics.SyncTransforms();
        Check(W.Hero.transform.position.z>2,"Enabled controller passes through NPC body.");
        CC.Move(Vector3.back*1.5f);Physics.SyncTransforms();
        Check(Vector3.Distance(W.Hero.transform.position,npc.transform.position)<.6f,"End-overlap fixture actually overlaps NPC.");
        yield return WaitUntil(()=>!phase.Active,1,"Phase expires.");
        Check(Time.time-started>=.75f&&Time.time-started<.85f,"Measured phase duration matches data within one frame margin.");
        Check(!Physics.GetIgnoreCollision(CC,collider)&&phase.IgnoredCount==0&&phase.Recoveries>0&&phase.UnresolvedRecoveries==0,"NPC overlap resolves and owned ignores restore at end.");
        W.DamagePlayer(4);Check(W.Health==health-7,"Ordinary incoming damage works immediately after phase.");
        Check(user.UseSecondary(),"Second charge works.");phase.End();
        yield return new WaitForSeconds(.3f);
        Check(rt.Charges==0&&rt.Cooldown==0&&!user.UseSecondary(),"Zero charges alone refuses after cooldown has elapsed.");
        yield return new WaitForSeconds(5.1f);Check(rt.Charges>=1,"Charge replenishes after configured real elapsed recharge.");
        PlaceHero(new Vector3(0,150.05f,0));Refill();
        var wall=Wall(new Vector3(0,152,1.5f),new Vector3(8,4,.3f));
        var ceiling=Wall(new Vector3(0,153,0),new Vector3(8,.2f,8));
        Check(user.UseSecondary(),"World collision control phase activates.");
        CC.Move(Vector3.forward*3);Check(W.Hero.transform.position.z<1.1f,"Building wall remains solid during phase.");
        CC.Move(Vector3.down*3);Check(W.Hero.transform.position.y>=149.98f,"Floor remains solid during phase.");
        CC.Move(Vector3.up*5);Check(W.Hero.transform.position.y<152,"Ceiling remains solid during phase.");phase.End();Destroy(wall);Destroy(ceiling);yield return null;
        PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.15f);Refill();
        Check(W.Hero.TryJump(),"Grounded jump before phase.");yield return new WaitForSeconds(.12f);
        Check(user.UseSecondary()&&W.Hero.TryJump(),"Double jump remains available during phase.");
        Check(!W.Hero.AirJumpAvailable&&!W.Hero.TryJump(),"Phase does not refill double jump.");
        phase.End();PlaceHero(new Vector3(0,150.05f,0));Refill();Check(user.UseSecondary(),"Pause control activates.");
        float remaining=phase.Remaining;TimeArbiter.SetMenuPaused(true);yield return new WaitForSecondsRealtime(.2f);
        Check(phase.Active&&Mathf.Abs(phase.Remaining-remaining)<.001f,"Pause freezes evasion duration.");TimeArbiter.SetMenuPaused(false);phase.End();
        Refill();TimeArbiter.SetMenuPaused(true);Check(!user.UseSecondary(),"Paused activation refuses without payment.");TimeArbiter.SetMenuPaused(false);
        Refill();user.ShieldComponent().Raise(60,8,1,1,CityColor.Cyan);
        Check(user.UseSecondary()&&!user.Shield.Up,"Phase ends an existing shield rather than stacking perfect defense.");phase.End();
        Refill();Check(W.Hero.Dash(Vector3.forward,2,.2f,null),"Speed motion fixture starts before phase.");
        int unchanged=rt.Charges;Check(!user.UseSecondary()&&rt.Charges==unchanged,"Phase cannot overlap an existing Speed dash or spend a refused charge.");
        yield return new WaitForSeconds(.3f);PlaceHero(new Vector3(0,150.05f,0));Refill();
        Check(user.UseSecondary()&&!W.Hero.TryTeleport(8)&&!W.Hero.Dash(Vector3.forward,2,.2f,null),"Teleport and Speed motion cannot start during phase.");phase.End();
        // Each cached visual is reused over repeated activations, no runtime material or mesh creation.
        Refill();Check(user.UseSecondary(),"Visual warmup activates.");yield return null;phase.End();yield return new WaitForSeconds(.3f);
        int materials=Resources.FindObjectsOfTypeAll<Material>().Length,meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
        int renderers=FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length,objects=FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;
        for(int i=0;i<20;i++){Refill();Check(user.UseSecondary(),"Stress activation "+i);yield return null;phase.End();}
        yield return new WaitForSeconds(.3f);
        Check(materials==Resources.FindObjectsOfTypeAll<Material>().Length&&meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&renderers==FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length&&objects==FindObjectsByType<Transform>(FindObjectsInactive.Include).Length,"Repeated phase has no material, mesh, renderer or object growth.");
        Log($"MEASURED Phase pool: outlines=1 echoes={PlayerPhaseShift.EchoCount} peripheral=2, materials={materials}, meshes={meshes}, renderers={renderers}, objects={objects}.");
        Aim(W.Hero.transform.position+Vector3.forward*4+Vector3.up);Refill();Check(user.UseSecondary(),"Third-person capture activates.");yield return null;Capture("phase-third-person.png");phase.End();
        var rig=Cam.GetComponent<ThirdPersonCamera>();rig.enabled=true;if(!rig.FirstPerson)rig.ToggleView();rig.SetLook(15,-5);yield return null;yield return null;
        var aim=user.AimDirection;float fov=Cam.fieldOfView;Refill();Check(user.UseSecondary(),"First-person phase activates.");yield return null;Capture("phase-first-person.png");
        Check(Vector3.Angle(aim,user.AimDirection)<.01f&&Mathf.Abs(Cam.fieldOfView-fov)<.01f,"First-person phase preserves aim and FOV.");phase.End();yield return new WaitForSeconds(.3f);
        Check(Vector3.Angle(aim,user.AimDirection)<.01f,"Aim unchanged after phase ends.");
        // Death is immediate; ignores must restore before any respawn or next session.
        Refill();Check(user.UseSecondary(),"Death control active.");W.DamagePlayer(W.Health,true);
        Check(W.PlayerDead&&!phase.Active&&phase.IgnoredCount==0&&!Physics.GetIgnoreCollision(CC,collider),"Unblockable death immediately clears evasion and collisions.");
        yield return EnterPhase("laser-eyes","strength");Isolate();
        var laser=Runtime("laser-eyes");Check(W.Powers.Select(laser)&&W.Powers.Use(laser)&&W.Powers.Channeling!=null,"Real Laser Eyes channel starts.");
        Check(W.Powers.UseSecondary()&&W.Powers.Channeling==null,"Phase cleanly stops existing Laser channel.");W.Powers.Phase.End();
        yield return AttackComparison("Rusher");yield return AttackComparison("Gunner");
        yield return MovementComparisons();
        yield return EnterPhase();yield return DenseCombat();
        var oldPhase=W.Powers.Phase;oldPhase.End();Refill();Check(W.Powers.UseSecondary(),"Session teardown control active.");yield return Home();
        Check(oldPhase==null||!oldPhase.Active&&oldPhase.IgnoredCount==0,"Session teardown clears Phase state and ignores.");
        // Real old Flight save fixture, written to the sandbox shared with a separate process.
        var old=JsonUtility.FromJson<ProgressSave>(JsonUtility.ToJson(Profile.Data));old.Level=7;old.Xp=23;old.Points=11;
        old.Loadout=new HeroLoadout{HeroId=F.Heroes[0].Id,PowerA="fire",PowerB="ice",SecondaryPower="flight",Primary=CityColor.Red,Secondary=CityColor.Cyan};
        File.WriteAllText(Profile.SavePath,JsonUtility.ToJson(old));Profile.Load();
        Check(Profile.EquippedSecondary==Power("phase-shift")&&Profile.Data.Level==7&&Profile.Data.Xp==23&&Profile.Data.Points==11,"Old Flight fixture migrates without changing progression.");
        old.Loadout.SecondaryPower="phase-shift";
        Check(JsonUtility.ToJson(old)==JsonUtility.ToJson(Profile.Data),"Migration preserves every other serialized profile field.");
        Check(JsonUtility.FromJson<ProgressSave>(File.ReadAllText(Profile.SavePath)).Loadout.SecondaryPower=="phase-shift","Repair written immediately through normal save path.");
    }
    IEnumerator AttackComparison(string kind)
    {
        foreach(string secondary in new[]{"phase-shift","force-field"})
        {
            yield return EnterPhase(secondary:secondary);
            foreach(var n in W.Npcs.ToArray())if(n!=null)Destroy(n.gameObject);yield return null;
            PlaceHero(W.City.Spawn+Vector3.up*.05f);
            var archetype=Resources.Load<EnemyArchetype>("Enemies/"+kind);
            var npc=CityNpc.Spawn(W,W.City.Spawn+Vector3.forward*(kind=="Gunner"?6:1.6f),NpcRole.Criminal,archetype);
            Check(npc!=null,"Real "+kind+" spawned for "+secondary);npc.AlwaysAggro=true;
            yield return WaitUntil(()=>npc.Phase==AttackPhase.Windup,8,"Real "+kind+" commits attack.");
            float until=npc.WindupStartTime+archetype.WindupSeconds-.15f;while(Time.time<until)yield return null;
            Check(W.Powers.UseSecondary(),secondary+" activates before committed "+kind+" release.");
            float health=W.Health;int releases=npc.Releases;float capacity=W.Powers.Shield!=null?W.Powers.Shield.Remaining:0;
            yield return WaitUntil(()=>npc.Releases>releases,2,"Real "+kind+" attack releases.");
            Check(npc.LastReleaseHit&&W.Health==health,secondary+" avoids health loss on an actual intersecting "+kind+" hit.");
            if(secondary=="force-field")
            {
                Check(W.Powers.Shield.Remaining<capacity,"Shield spends finite capacity on same attack.");
                Check(W.Hero.TryPunch(),"Shield permits paid melee while raised.");
            }
            else Check(!W.Hero.TryPunch()&&W.Powers.Shield==null,"Phase refuses melee and uses no shield capacity.");
        }
    }
    IEnumerator MovementComparisons()
    {
        float phaseDistance=0,normalDistance=0;
        foreach(string secondary in new[]{"phase-shift","speed","teleport"})
        {
            yield return EnterPhase(secondary:secondary);Isolate();
            PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.2f);
            Cam.transform.position=new Vector3(0,152,-2);Cam.transform.rotation=Quaternion.identity;
            Vector3 start=W.Hero.transform.position;
            Check(W.Powers.UseSecondary(),secondary+" comparison activates.");
            if(secondary=="teleport")
            {
                float distance=Vector3.Distance(start,W.Hero.transform.position);
                Check(distance>7.5f,"Teleport immediately relocates by its configured range.");
                Log($"MEASURED Teleport instant distance={distance:F3}m; Phase instant distance=0m.");
            }
            else
            {
                if(secondary=="phase-shift")Check(Vector3.Distance(start,W.Hero.transform.position)<.001f,"Phase activation does not relocate.");
                W.Hero.ScriptedMove=Vector3.forward;float begin=Time.time;
                yield return new WaitForSeconds(.7f);W.Hero.ScriptedMove=Vector3.zero;
                float distance=Vector3.ProjectOnPlane(W.Hero.transform.position-start,Vector3.up).magnitude;
                if(secondary=="phase-shift")
                {
                    phaseDistance=distance;normalDistance=W.Tuning.Movement.WalkSpeed*W.Powers.HeroStats.MoveSpeed*(Time.time-begin);
                    Check(Mathf.Abs(distance-normalDistance)<.2f,"Phase movement matches normal walk speed, without a speed multiplier.");
                }
                else Check(distance>phaseDistance*1.5f,"Speed covers substantially more distance than Phase over the same interval.");
                Log($"MEASURED {secondary} .7s traversal: distance={distance:F3}m; Phase={phaseDistance:F3}m, ordinary expected={normalDistance:F3}m.");
            }
        }
        yield return EnterPhase();Isolate();PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.15f);
        var user=W.Powers;var phase=user.Phase;
        // Pending paid windups may not land during phase or resume later after phase ends.
        W.Hero.PunchWindupSeconds=.3f;float impact=W.Hero.LastImpactTime;
        Check(W.Hero.TryPunch()&&user.UseSecondary(),"Phase starts after paid melee windup.");
        yield return new WaitForSeconds(.4f);Check(W.Hero.LastImpactTime==impact,"Previously queued melee windup cannot impact during phase.");phase.End();
        yield return new WaitForSeconds(.4f);Check(W.Hero.LastImpactTime==impact,"Cancelled melee does not resume after phase ends.");
        user.Tick(100,true);Check(W.Hero.TryPunch(),"Normal attacking returns immediately after phase.");
        yield return new WaitForSeconds(.4f);Check(W.Hero.LastImpactTime>impact,"Fresh post-phase attack really impacts.");
        // Kill plane is still the World's explicit unblockable damage path.
        Refill();Check(user.UseSecondary(),"Kill-plane control starts phased.");
        var cc=CC;cc.enabled=false;W.Hero.transform.position=new Vector3(0,W.Tuning.Movement.KillPlane-1,0);cc.enabled=true;
        yield return null;yield return null;
        Check(W.PlayerDead&&!phase.Active&&phase.IgnoredCount==0,"Actual WorldSession kill plane defeats and clears phase.");
    }
    IEnumerator DenseCombat()
    {
        // Same seeded city, camera and 18 real attacking actors before and after. Restore health only in this harness.
        foreach(var n in W.Npcs.ToArray())if(n!=null)Destroy(n.gameObject);
        yield return null;
        PlaceHero(W.City.Spawn+Vector3.up*.1f,true);
        var crowd=new CityNpc[18];
        for(int i=0;i<crowd.Length;i++)
        {
            var a=Resources.Load<EnemyArchetype>("Enemies/"+(i%3==0?"Gunner":i%3==1?"Brute":"Rusher"));
            crowd[i]=CityNpc.Spawn(W,W.City.Spawn+Quaternion.Euler(0,i*20,0)*Vector3.forward*6,NpcRole.Criminal,a);
            Check(crowd[i]!=null,"Dense actor spawned "+i);crowd[i].AlwaysAggro=true;
        }
        var health=typeof(WorldSession).GetProperty("Health");
        float until=Time.time+4;while(Time.time<until){health.SetValue(W,W.MaxHealth);yield return null;}
        var target=new RenderTexture(1280,720,24);target.Create();var previous=Cam.targetTexture;Cam.targetTexture=target;
        int frames=0;double start=Time.realtimeSinceStartupAsDouble;long alloc=GC.GetAllocatedBytesForCurrentThread();
        int releases=crowd.Sum(n=>n.Releases);int casts=W.Powers.Phase!=null?W.Powers.Phase.Activations:0;
        while(Time.realtimeSinceStartupAsDouble-start<8)
        {
            health.SetValue(W,W.MaxHealth);
            if(Stage!="baseline"&&!W.Powers.Phase.Active){Refill();W.Powers.UseSecondary();}
            Cam.Render();frames++;yield return null;
        }
        double seconds=Time.realtimeSinceStartupAsDouble-start;long bytes=GC.GetAllocatedBytesForCurrentThread()-alloc;
        Cam.targetTexture=previous;target.Release();Destroy(target);
        Check(crowd.Sum(n=>n.Releases)>releases,"Dense real enemy attacks occurred during measurement.");
        if(Stage!="baseline")Check(W.Powers.Phase.Activations-casts>=5,"Dense stress actually activates Phase repeatedly while enemies attack (harness refills charges).");
        Log($"MEASURED {Stage} dense combat 18 enemies 1280x720: FPS={frames/seconds:F2}, mean={seconds*1000/frames:F3}ms, whole-frame GC={bytes/(double)frames:F0} B/frame, frames={frames}, releases={crowd.Sum(n=>n.Releases)-releases}, materials={Resources.FindObjectsOfTypeAll<Material>().Length}, meshes={Resources.FindObjectsOfTypeAll<Mesh>().Length}, renderers={FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length}, objects={FindObjectsByType<Transform>(FindObjectsInactive.Include).Length}, particles={FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length}.");
        Capture(Stage+"-dense.png");
    }
}
#endif
