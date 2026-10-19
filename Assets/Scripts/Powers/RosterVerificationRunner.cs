#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Profiling;

/// See RosterVerification. Every power is equipped through PlayerProgression.SetLoadout (the Hero Forge save path), entered in
/// a real Hero session, and fired through PowerUser.Use (LMB) / PowerUser.Channel (held LMB) against real CityNpc actors.
/// Fixtures that need no navigation stand on an isolated floor 150 m above the city (the Forge suite's pattern, AI off);
/// the Darkness root is measured on LIVE, NavMesh-driven criminals in the city.
public sealed class RosterVerificationRunner : SessionVerificationRunner
{
    public bool Reload;
    protected override string Folder => "Verification/Roster/";
    protected override string ResultFile => Reload ? "reload.txt" : "results.txt";
    static readonly string[] NewPowers = { "darkness", "laser-eyes", "lightning", "force-field", "speed", "poison" };
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var profile = FindAnyObjectByType<ModeScreens>().Profile;
        if (Reload)
        {
            Check(profile.Data.Loadout.PowerA == "laser-eyes" && profile.Data.Loadout.PowerB == "poison" && profile.Data.Loadout.HeroId == "nova",
                $"SECOND PROCESS restores the new-power loadout: {profile.Data.Loadout.HeroId} {profile.Data.Loadout.PowerA} + {profile.Data.Loadout.PowerB}.");
            Check(NewPowers.All(id => Power(id) != null && profile.Owns(Power(id))), "SECOND PROCESS: all six new powers owned (InitiallyUnlocked data).");
            GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")); yield return Scene(GameFlow.CityScene);
            Check(W.Powers.EquippedA == Power("laser-eyes") && W.Powers.EquippedB == Power("poison") && W.Powers.Synergy == null,
                "SECOND PROCESS session caches the reloaded pair; Laser Eyes + Poison has no synergy (cap CONTROL).");
            yield break;
        }
        // ---------------------------------------------------------------- data
        Check(NewPowers.All(id => Power(id) != null), "Six new PowerDefinition assets exist: " + string.Join(", ", NewPowers));
        var shipping=Resources.LoadAll<PowerDefinition>("Powers").Where(p=>!p.Id.StartsWith("verification-")).Select(p=>p.Id).ToArray();
        var expected=new[]{"darkness","fire","flight","force-field","ice","laser-eyes","lightning","poison","speed","strength","telekinesis","teleport","phase-shift"};
        Check(shipping.Length==expected.Length&&expected.All(shipping.Contains),"Exact 13 retained definitions: 12 selectable powers plus legacy Flight.");
        foreach (var hero in F.Heroes) Check(NewPowers.All(id => Array.IndexOf(hero.AvailablePowers, Power(id)) >= 0), hero.DisplayName + " can equip all six new powers (AvailablePowers).");
        Check(NewPowers.All(id => profile.Owns(Power(id)) && profile.Tier(Power(id)) == 0) && profile.Data.Points == 0, "Fresh profile owns the six new powers at tier 0 with 0 points spent.");
        var laser = Power("laser-eyes");
        Check(laser.Activation == PowerActivation.Channeled && laser.DrainPerSecond > 0 && laser.Effect is ChanneledEffect, $"Laser Eyes is channeled: drain {laser.DrainPerSecond}/s, start cost {laser.ResourceCost}.");
        Check(NewPowers.Where(id => id != "laser-eyes").All(id => Power(id).Activation == PowerActivation.Instant), "The other five new powers keep the instant charge pattern.");
        Check(Power("force-field").Damage == 0 && Power("force-field").Effect is ForceFieldEffect, "Force Field deals no damage (defensive).");
        foreach (var id in NewPowers) { var d = Power(id); Log($"DATA {id}: damage={d.Damage} force={d.Force} range={d.Range} duration={d.Duration} charges={d.Charges} recharge={d.ChargeRecharge}s cooldown={d.Cooldown}s energy={d.ResourceCost} drain={d.DrainPerSecond}/s colour={d.PaletteColor}"); }

        yield return Darkness();
        yield return Laser();
        yield return Lightning();
        yield return ForceField();
        yield return Speed();
        yield return Poison();
        yield return RosterSynergies();

        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        profile = FindAnyObjectByType<ModeScreens>().Profile;
        Check(profile.SetLoadout(F.Hero("nova"), Power("laser-eyes"), Power("poison"), CityColor.UiPurple, CityColor.Red), "Save Laser Eyes + Poison for the separate-process Reload.");
        Log("LIMIT: entry points are the ones the input handlers call (PowerUser.Use / Channel / SynergyRunner.TryActivate); no hardware input, no human feel test.");
        Log("LIMIT: most fixtures stand AI-off on an isolated floor (as in HeroForgeVerification); only the Darkness root is measured on live navigation.");
    }
    // ---------------------------------------------------------------- DARKNESS: live navigation
    IEnumerable<Vector3> NavPoints(float min, float max)
    {
        Vector3 hero = W.Hero.transform.position, head = hero + Vector3.up * 1.7f;
        for (float d = (min + max) * .5f; d <= max; d += .5f)
            for (int a = 0; a < 360; a += 15)
            {
                if (!NavMesh.SamplePosition(hero + Quaternion.Euler(0, a, 0) * Vector3.forward * d, out var hit, .75f, NavMesh.AllAreas)) continue;
                float real = Vector3.Distance(hit.position, hero); if (real < min || real > max) continue;
                Vector3 chest = hit.position + Vector3.up * 1.2f; bool clear = true;
                foreach (var h in Physics.RaycastAll(head, (chest - head).normalized, Vector3.Distance(head, chest), ~0, QueryTriggerInteraction.Ignore))
                    if (h.transform.root != W.Hero.transform && h.collider.GetComponentInParent<CityNpc>() == null) { clear = false; break; }
                if (clear) yield return hit.position;
            }
    }
    /// Mean horizontal speed of each NPC over `seconds` (sampled every frame).
    IEnumerator Speeds(CityNpc[] npcs, float seconds, float[] result)
    {
        var last = npcs.Select(n => n.transform.position).ToArray(); var distance = new float[npcs.Length]; float t = 0;
        while (t < seconds)
        {
            yield return null; t += Time.deltaTime;
            for (int i = 0; i < npcs.Length; i++) { Vector3 d = npcs[i].transform.position - last[i]; d.y = 0; distance[i] += d.magnitude; last[i] = npcs[i].transform.position; }
        }
        for (int i = 0; i < npcs.Length; i++) result[i] = distance[i] / Mathf.Max(.001f, t);
    }
    IEnumerator Darkness()
    {
        Log("---- DARKNESS (two-target curse, root/slow and aura)");
        yield return Enter("darkness", "strength");
        var rt=Runtime("darkness");var stats=W.Powers.Stats(rt);var effect=(DarknessEffect)rt.Definition.Effect;
        Check(stats.Charges==1&&rt.Definition.ChargeRecharge==9f&&effect.InitialRootSeconds>1f&&effect.SlowSpeedFactor==.4f,
            "Darkness has one charge, 9 s base recharge, brief root and 60% movement slow.");
        W.enabled=false;
        foreach(var existing in W.Npcs.ToArray())if(existing!=null)Destroy(existing.gameObject);
        yield return null;
        PlaceHero(W.City.Spawn+Vector3.up*.05f);Cam.GetComponent<ThirdPersonCamera>().enabled=false;
        var points=NavPoints(12,17).ToList();var controlPoints=NavPoints(25,30).ToList();
        Check(points.Count>=1&&controlPoints.Count>=1,"Clear live NavMesh points for target and distant movement control.");
        Vector3 pa=points[0],pb=controlPoints.FirstOrDefault(p=>Vector3.Distance(p,pa)>effect.SecondaryRadius+18f&&
            Vector3.Angle(p-W.Hero.transform.position,pa-W.Hero.transform.position)>60);
        Check(pb!=Vector3.zero,"Uncursed control is outside the secondary-search radius and aim.");
        var target=CityNpc.Spawn(W,pa,NpcRole.Criminal);var control=CityNpc.Spawn(W,pb,NpcRole.Criminal);
        control.AlwaysAggro=true;
        target.SetCombatStats(1000,target.ContactDamage);control.SetCombatStats(1000,control.ContactDamage);
        yield return new WaitForSeconds(1f);
        var speed=new float[2];yield return Speeds(new[]{target,control},.6f,speed);
        float ordinary=speed[0];Log($"MEASURED Darkness before: target {ordinary:F3} m/s, control {speed[1]:F3} m/s.");
        Check(ordinary>.3f&&speed[1]>.3f,"Both real agents move before the curse.");
        Aim(Chest(target));float health=target.Health;
        bool aimed=W.Powers.FindTarget(stats.Range,out var darkHit);
        bool cast=W.Powers.Use(rt);
        Check(cast&&DarknessEffect.LastPrimary==target&&DarknessEffect.LastSecondary==null,
            $"Crosshair target alone receives the curse: aimed={aimed} hit={darkHit.collider?.name} target={target.name} primary={DarknessEffect.LastPrimary?.name} secondary={DarknessEffect.LastSecondary?.name} secondaryPosition={DarknessEffect.LastSecondary?.transform.position} targetPosition={target.transform.position} controlPosition={control.transform.position} activeNpcs={W.Npcs.Count(n=>n!=null&&!n.Dead)} message={W.Powers.Message}.");
        Check(target.Rooted&&target.Slowed&&!target.Frozen&&rt.Charges==0,
            "Initial root and slow coexist without a freeze; one charge spent.");
        float direct=health-target.Health;
        Check(direct>=stats.Damage*.9f-.001f&&direct<=stats.Damage*1.1f+.001f,
            $"Direct damage {direct:F3} is the low rolled Darkness hit around {stats.Damage:F2}.");
        Check(PowerVfx.Get().ActiveLines>0,"Aimed activation snap uses the pooled line effect.");
        yield return null;
        var curse=target.GetComponent<ShadowCursed>();var look=target.GetComponent<RootedLook>();
        Check(curse!=null&&curse.Active&&look!=null&&look.Showing&&StatusPowerVfx.Get().ActiveRoots==1,
            "The live curse holds one pooled visible restraint.");
        Capture("darkness-rooted.png");
        yield return Speeds(new[]{target,control},.75f,speed);
        Check(speed[0]<.08f&&speed[1]>.3f,"Rooted target has near-zero real displacement; uncursed control still moves.");
        yield return new WaitForSeconds(.5f);
        Check(!target.Rooted&&target.Slowed&&curse.Active&&look.Showing,
            "Hard root expires while heavy slow and visible curse continue.");
        yield return Speeds(new[]{target,control},.8f,speed);
        Log($"MEASURED Darkness slowed: target {speed[0]:F3} m/s, control {speed[1]:F3} m/s; agent speed {target.Agent.speed:F3}.");
        Check(speed[0]>.05f&&target.Agent.speed<=target.Archetype.MoveSpeed*.41f,
            "Real movement resumes at no more than 40% configured enemy speed.");
        Check(speed[1]>.3f&&!control.Slowed,"Uncursed movement control remains ordinary.");
        yield return new WaitForSeconds(stats.Duration);
        Check(!curse.Active&&!target.Slowed&&StatusPowerVfx.Get().ActiveRoots==0,
            "Curse, slow and pooled tendrils all end.");
        yield return Speeds(new[]{target},.6f,speed);
        Check(speed[0]>.3f,"Real movement returns after the curse.");
        // A root restricts movement but never silences the existing attack cycle.
        int windups=target.Windups;target.Root(4f);PlaceHero(target.transform.position+target.transform.forward*1.5f);
        float until=Time.time+3.5f;while(target.Windups==windups&&Time.time<until)yield return null;
        Check(target.Windups>windups,"A rooted enemy in reach can still start an attack.");
        control.Freeze(3f);windups=control.Windups;PlaceHero(control.transform.position+control.transform.forward*1.5f);
        yield return new WaitForSeconds(1.5f);
        Check(control.Windups==windups,"Frozen control cannot attack in reach.");
        Check(rt.Charges==1&&rt.Charges<=stats.Charges,"Real charge clock restored exactly one charge after 9 s.");
        target.Agent.enabled=false;control.Agent.enabled=false;
        target.transform.position=new Vector3(5,150.05f,6);control.transform.position=new Vector3(0,150.05f,6);
        PlaceHero(new Vector3(0,150.05f,0));Physics.SyncTransforms();
        Aim(Chest(control));Check(W.Powers.Use(rt)&&rt.Charges==0,"Recharged Darkness casts once more.");
        Aim(Chest(target));health=target.Health;
        Check(!W.Powers.Use(rt)&&W.Powers.Message=="Blocked: cooldown"&&target.Health==health,
            "Immediate second cast is refused by its independent activation cooldown.");
        yield return new WaitForSeconds(stats.Cooldown+.05f);health=target.Health;
        Check(!W.Powers.Use(rt)&&W.Powers.Message=="Blocked: 0 charges"&&target.Health==health,
            "After activation cooldown, empty single-charge gate still refuses without damage.");
        rt.Charges=1;rt.Cooldown=0;Aim(W.Hero.transform.position+Vector3.up*60+Vector3.forward*5);
        Check(!W.Powers.Use(rt)&&rt.Charges==1,"Empty aim refuses and preserves the sole charge.");
    }
    // ---------------------------------------------------------------- LASER EYES: channeled
    IEnumerator Hold(float seconds)
    {
        float t = 0; while (t < seconds && W.Powers.Channeling != null) { yield return null; t += Time.deltaTime; W.Powers.Channel(true, Time.deltaTime); }
    }
    IEnumerator Laser()
    {
        Log("---- LASER EYES (channeled)");
        yield return Enter("laser-eyes", "strength");
        Isolate(); var rt = Runtime("laser-eyes"); var d = rt.Definition; var stats = W.Powers.Stats(rt);
        var cop = Actor(new Vector3(0, 150, 10), NpcRole.Cop, 1000); var control = Actor(new Vector3(4, 150, 10), NpcRole.Cop, 1000);
        yield return new WaitForSeconds(.3f);
        Aim(Chest(cop)); float energy = W.Powers.Energy, heat = W.Heat; int charges = rt.Charges;
        Check(W.Powers.Use(rt) && W.Powers.Channeling == rt, "Laser Eyes starts a channel: " + W.Powers.Message);
        Check(rt.Charges == charges && Mathf.Abs(W.Powers.Energy - energy) < .001f, "Starting the channel spends no charge and no up-front energy.");
        float hp = cop.Health, e0 = W.Powers.Energy, t0 = Time.time;
        yield return Hold(1f);
        float held = Time.time - t0, dealt = hp - cop.Health, drained = e0 - W.Powers.Energy;
        Log($"MEASURED 1 s hold: {held:F3} s held, damage {dealt:F2} (expected ~{stats.Damage * held:F1} at {stats.Damage}/s), energy drained {drained:F2} (expected ~{(d.DrainPerSecond - W.Powers.EnergyRegen) * held:F1} = drain {d.DrainPerSecond}/s - regen {W.Powers.EnergyRegen}/s), Heat +{W.Heat - heat:F3}.");
        Check(dealt > stats.Damage * (held - .15f) && dealt <= stats.Damage * (held + .02f), "Beam damage per second matches data (0.1 s tick quantisation).");
        // The harness power clock keeps regen running (as in play), so the NET loss is drain - regen per second.
        Check(Mathf.Abs(drained - (d.DrainPerSecond - W.Powers.EnergyRegen) * held) < d.DrainPerSecond * .05f + .5f, "Energy drains at DrainPerSecond (net of regen) while held.");
        Check(Mathf.Abs((W.Heat - heat) - W.Tuning.Heat.AssaultHeat) < .02f, $"Heat rose by ONE assault ({W.Tuning.Heat.AssaultHeat}) across ~10 ticks, not one per tick.");
        Check(control.Health == 1000, "CONTROL: the cop beside the beam is untouched.");
        Check(PowerVfx.Get().BeamVisible, "Beam visible while held."); Capture("laser-beam.png");
        var oldPosition=Cam.transform.position;var oldRotation=Cam.transform.rotation;
        Cam.transform.position=W.Hero.transform.position+new Vector3(2.5f,2f,-3f);
        Cam.transform.LookAt(Chest(cop));Capture("laser-beam-side.png");
        Cam.transform.SetPositionAndRotation(oldPosition,oldRotation);
        // The measured drain assertion above is complete. Start the separate throughput fixture full,
        // because doubled drain no longer permits both windows on one partial energy bar.
        W.Powers.Tick(100,true);
        Check(Mathf.Approximately(W.Powers.Energy,W.Powers.MaxEnergy),"Throughput fixture starts at full energy through normal regeneration.");
        yield return LaserThroughput();
        W.Powers.Channel(false, Time.deltaTime);
        Check(W.Powers.Channeling == null && Mathf.Approximately(rt.Cooldown, stats.Cooldown) && !PowerVfx.Get().BeamVisible, $"Releasing ends the channel, starts the {stats.Cooldown} s cooldown, hides the beam.");
        hp = cop.Health; yield return new WaitForSeconds(.4f);
        Check(cop.Health == hp, "CONTROL: no damage after release.");
        Check(!W.Powers.Use(rt) && W.Powers.Message == "Blocked: cooldown", "CONTROL: restart during cooldown refused.");
        yield return new WaitForSeconds(stats.Cooldown);
        Check(W.Powers.Use(rt), "Restart after cooldown."); yield return Hold(.2f);
        Check(W.Powers.Select(Runtime("strength")) && W.Powers.Channeling == null && !PowerVfx.Get().BeamVisible, "Selecting another power ends the channel.");
        W.Powers.Select(rt); yield return new WaitForSeconds(stats.Cooldown + .05f);
        Check(W.Powers.Use(rt), "Channel again to exhaust energy.");
        float budget = 10f; while (W.Powers.Channeling != null && budget > 0) { yield return null; budget -= Time.deltaTime; W.Powers.Channel(true, Time.deltaTime); }
        Check(W.Powers.Channeling == null && W.Powers.Message == "Blocked: energy" && Mathf.Approximately(rt.Cooldown, stats.Cooldown), $"Channel ends by itself when energy runs out (energy {W.Powers.Energy:F2}) and its cooldown starts.");
        // Regen would refill the start cost during the 0.8 s cooldown, so the cooldown is cleared directly (as DebugSetResources
        // does for Strength) to test the energy gate on its own, in the same frame the channel ran dry.
        float dry = W.Powers.Energy; rt.Cooldown = 0f;
        Check(dry < d.ResourceCost && !W.Powers.Use(rt) && W.Powers.Message == "Blocked: energy" && W.Powers.Channeling == null, $"CONTROL: cannot start below the {d.ResourceCost} start cost (energy {dry:F2}).");
    }
    IEnumerator LaserThroughput()
    {
        var target=new RenderTexture(1280,720,24);target.Create();
        var camera=Cam;var previous=camera.targetTexture;
        camera.targetTexture=target;
        for(int i=0;i<20;i++){yield return null;W.Powers.Channel(true,Time.deltaTime);camera.Render();}
        int materialsBefore=Resources.FindObjectsOfTypeAll<Material>().Length;
        int meshesBefore=Resources.FindObjectsOfTypeAll<Mesh>().Length;
        int linesBefore=FindObjectsByType<LineRenderer>().Length;
        int particlesBefore=FindObjectsByType<ParticleSystem>().Length;
        int poolBefore=PowerVfx.Get().PoolCount;
        using(var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1))
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();int frames=0;long bytes=0;
            while(watch.Elapsed.TotalSeconds<1.2&&W.Powers.Channeling!=null)
            {yield return null;W.Powers.Channel(true,Time.deltaTime);camera.Render();bytes+=gc.LastValue;frames++;}
            watch.Stop();
            Log($"MEASURED laser 1280x720 active channel: FPS={frames/watch.Elapsed.TotalSeconds:F2} mean={watch.Elapsed.TotalMilliseconds/frames:F3}ms whole-frame GC={bytes/frames}B/frame frames={frames} lineRenderers={linesBefore}->{FindObjectsByType<LineRenderer>().Length} particleSystems={particlesBefore}->{FindObjectsByType<ParticleSystem>().Length} materials={materialsBefore}->{Resources.FindObjectsOfTypeAll<Material>().Length} meshes={meshesBefore}->{Resources.FindObjectsOfTypeAll<Mesh>().Length} beamPool={poolBefore}->{PowerVfx.Get().PoolCount}.");
            Check(frames>20&&W.Powers.Channeling!=null,"Sustained channel performance sample stayed active.");
        }
        camera.targetTexture=previous;target.Release();Destroy(target);
    }
    IEnumerator EnterSecondary(string id)
    {
        yield return Home();
        var hero=F.Heroes[0];
        Check(Profile.SetLoadout(hero,Power("strength"),Power("fire"),Power(id),hero.Primary,hero.Secondary),
            "Save "+hero.DisplayName+" with Strength + Fire attacks and "+id+" secondary through the loadout API.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        Check(W.Powers.EquippedA==Power("strength")&&W.Powers.EquippedB==Power("fire")&&W.Powers.EquippedSecondary==Power(id),
            "Session caches the valid secondary "+id+" in its F slot.");
    }
    // ---------------------------------------------------------------- LIGHTNING: chain
    IEnumerator Lightning()
    {
        Log("---- LIGHTNING (chain)");
        yield return Enter("lightning", "strength");
        Isolate(); var rt = Runtime("lightning"); var stats = W.Powers.Stats(rt); var effect = (LightningEffect)rt.Definition.Effect;
        var chain = new[] { new Vector3(0, 150, 10), new Vector3(4, 150, 12), new Vector3(8, 150, 14), new Vector3(12, 150, 12), new Vector3(16, 150, 10), new Vector3(20, 150, 12) }
            .Select(p => Actor(p, NpcRole.Criminal, 1000)).ToArray();
        var beyondCap = Actor(new Vector3(24, 150, 14), NpcRole.Criminal, 1000);
        var civilian = Actor(new Vector3(2, 150, 11), NpcRole.Civilian, 1000);
        var walled = Actor(new Vector3(-3.5f, 150, 10), NpcRole.Criminal, 1000); Wall(new Vector3(-1.75f, 151.5f, 10), new Vector3(.3f, 4, 3));
        var far = Actor(new Vector3(40, 150, 10), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        Check(chain.All(n => n.Hostile) && !civilian.Hostile, "Criminals hostile, civilian not (hero side).");
        Aim(new Vector3(-20, 150, 30)); int charges = rt.Charges;
        Check(!W.Powers.Use(rt) && rt.Charges == charges, "CONTROL: bolt at empty floor with no enemy within reach is refused, no charge spent: " + W.Powers.Message);
        Aim(Chest(chain[0])); int draws = PowerVfx.Get().Draws;
        Check(W.Powers.Use(rt), "Lightning cast at the first criminal: " + W.Powers.Message);
        Check(LightningEffect.LastChain.Count == effect.MaxArcs + 1 && LightningEffect.LastChain.SequenceEqual(chain), $"Chain order = the six planted criminals ({LightningEffect.LastChain.Count} struck).");
        for (int i = 0; i < chain.Length; i++)
        {
            float expected = stats.Damage * Mathf.Pow(effect.Falloff, i), lost = 1000 - chain[i].Health;
            Check(Mathf.Abs(lost - expected) < .01f && Mathf.Abs(LightningEffect.LastDamages[i] - expected) < .01f, $"Jump {i}: {lost:F2} damage (expected {expected:F2}).");
        }
        Check(beyondCap.Health == 1000, "CONTROL: a 7th criminal in reach is untouched (MaxArcs cap).");
        Check(civilian.Health == 1000, "CONTROL: the civilian nearest the first target is never arced to.");
        Check(walled.Health == 1000, "CONTROL: the criminal behind a wall (nearer than jump 2) is skipped.");
        Check(far.Health == 1000, "CONTROL: the criminal 20 m from the chain is untouched.");
        Check(LightningEffect.LastVisualStrikes.Count==chain.Length&&
            LightningEffect.LastVisualStrikes.Select(x=>x.Target).SequenceEqual(chain)&&
            LightningEffect.LastVisualStrikes[0].Width>LightningEffect.LastVisualStrikes[1].Width*2&&
            LightningEffect.LastVisualStrikes.Skip(2).All(x=>x.Width<LightningEffect.LastVisualStrikes[1].Width)&&
            !LightningEffect.LastVisualStrikes.Any(x=>x.Target==beyondCap||x.Target==civilian||x.Target==walled),
            "Visual arcs follow actual ordered hit targets; primary is dominant, skipped controls have no fake arc.");
        Check(PowerVfx.Get().Draws - draws == chain.Length*2, "One core and one cached afterglow per real strike."); Capture("lightning-chain.png");
        yield return new WaitForSeconds(stats.Cooldown + .05f);
        float first = chain[0].Health; Aim(new Vector3(.5f, 150, 8.5f));
        Check(W.Powers.Use(rt) && LightningEffect.LastChain.Count > 0 && LightningEffect.LastChain[0] == chain[0] && chain[0].Health < first, "Bolt at the floor beside an enemy jumps to the nearest enemy.");
    }
    // ---------------------------------------------------------------- FORCE FIELD: defensive
    IEnumerator ForceField()
    {
        Log("---- FORCE FIELD (defensive)");
        yield return EnterSecondary("force-field");
        Isolate(); var rt = Runtime("force-field"); var stats = W.Powers.Stats(rt); var effect = (ForceFieldEffect)rt.Definition.Effect;
        int damagedEvents = 0; Action<bool> count = died => damagedEvents++; W.PlayerDamaged += count;
        float health = W.Health;
        Check(W.Powers.Use(rt) && W.Powers.Shield != null && W.Powers.Shield.Up && W.Powers.Shield.RingsVisible, $"Force Field raised: {effect.Capacity} absorb for {stats.Duration} s.");
        Capture("force-field.png");
        int ripples=W.Powers.Shield.HitRipples;
        W.DamagePlayer(25);
        Check(W.Powers.Shield.HitRipples==ripples+1&&W.Powers.Shield.HitRippleVisible,
            "Real absorbed amount triggers one local ripple on the boundary.");
        Check(W.Health == health && Mathf.Abs(W.Powers.Shield.Remaining - (effect.Capacity - 25)) < .01f && damagedEvents == 0, $"25 damage fully absorbed: health {W.Health}, field {W.Powers.Shield.Remaining}, 0 damage events.");
        W.DamagePlayer(50);
        float through = 50 - (effect.Capacity - 25);
        Check(Mathf.Abs(W.Health - (health - through)) < .01f && !W.Powers.Shield.Up && W.Powers.Shield.LastEnd == "broken" && !W.Powers.Shield.RingsVisible && damagedEvents == 1,
            $"50 damage: field absorbs its last {effect.Capacity - 25}, {through} reaches health ({W.Health}); field broken, rings hidden.");
        ripples=W.Powers.Shield.HitRipples;
        health = W.Health; W.DamagePlayer(10);
        Check(W.Powers.Shield.HitRipples==ripples,"CONTROL: damage with no raised shield draws no new ripple.");
        Check(Mathf.Abs(W.Health - (health - 10)) < .01f, "CONTROL: with the field down, damage reaches health in full.");
        rt.Charges = 1; rt.Cooldown = 0; Check(W.Powers.Use(rt) && W.Powers.Shield.Up, "Raise again.");
        rt.Charges = 1; rt.Cooldown = 0; Check(!W.Powers.Use(rt) && rt.Charges == 1 && W.Powers.Message == "Force field already up", "CONTROL: re-cast while up is refused and spends no charge.");
        health = W.Health; W.DamagePlayer(5, true);
        Check(Mathf.Abs(W.Health - (health - 5)) < .01f && Mathf.Abs(W.Powers.Shield.Remaining - effect.Capacity) < .01f, "CONTROL: unblockable damage (kill plane path) bypasses the field.");
        yield return new WaitForSeconds(stats.Duration + .1f);
        Check(!W.Powers.Shield.Up && W.Powers.Shield.LastEnd == "expired", "Field expires after its Duration.");
        health = W.Health; W.DamagePlayer(5); Check(Mathf.Abs(W.Health - (health - 5)) < .01f, "CONTROL: expired field absorbs nothing.");
        W.PlayerDamaged -= count;
    }
    // ---------------------------------------------------------------- SPEED: burst dash
    IEnumerator Speed()
    {
        Log("---- SPEED (burst dash)");
        yield return EnterSecondary("speed");
        Isolate(); var rt = Runtime("speed"); var stats = W.Powers.Stats(rt);
        var inPath = Actor(new Vector3(0, 150, 4.5f), NpcRole.Criminal, 1000);
        var civilian = Actor(new Vector3(.4f, 150, 6.5f), NpcRole.Civilian, 1000);
        var aside = Actor(new Vector3(3, 150, 4.5f), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        Aim(W.Hero.transform.position + new Vector3(0, 1.5f, 20)); Vector3 start = W.Hero.transform.position;
        Check(W.Powers.Use(rt) && W.Hero.Dashing, "Dash starts: " + W.Powers.Message);
        var speedVisual=W.Hero.GetComponentInChildren<DashTrail>();int priorStreaks=speedVisual!=null?speedVisual.StreakCount:0;
        yield return new WaitForSeconds(.07f);
        Check(speedVisual!=null&&speedVisual.Emitting&&speedVisual.StreakCount>priorStreaks,
            "Speed has visible bounded displacement streaks during its real dash, before the end snap.");
        Capture("speed-dash.png");
        yield return new WaitForSeconds(stats.Duration + .3f);
        Vector3 moved = W.Hero.transform.position - start; float flat = new Vector2(moved.x, moved.z).magnitude;
        Log($"MEASURED dash: {flat:F2} m horizontal (data {stats.Range} m in {stats.Duration} s), {moved.y:F2} m vertical, direction z {moved.z:F2}.");
        Check(!W.Hero.Dashing && Mathf.Abs(flat - stats.Range) < .6f && moved.z > stats.Range - .6f, "Dash covers its Range along the aim, through the enemy in its path.");
        Check(speedVisual!=null&&speedVisual.RendererCount==12&&speedVisual.StreakCount>priorStreaks&&
            Vector3.Dot(speedVisual.LastVisualDirection,moved.normalized)>.9f&&!speedVisual.Emitting,
            "Speed uses bounded side streaks aligned with real dash displacement and stops emitting at the end.");
        Check(Mathf.Abs(1000 - inPath.Health - stats.Damage) < .01f, $"Criminal in the path clipped once: {1000 - inPath.Health} damage.");
        Check(civilian.Health == 1000, "CONTROL: civilian in the path untouched (hostile only).");
        Check(aside.Health == 1000, "CONTROL: criminal 3 m to the side untouched.");
        var cc = W.Hero.GetComponent<CharacterController>();
        Check(!Physics.GetIgnoreCollision(cc, inPath.GetComponent<Collider>()), "NPC collision restored after the dash.");
        PlaceHero(new Vector3(20, 150.05f, 0)); Wall(new Vector3(20, 151.5f, 4), new Vector3(4, 3, 1));
        yield return new WaitForSeconds(stats.Cooldown + .05f);
        Aim(W.Hero.transform.position + new Vector3(0, 1.5f, 20)); start = W.Hero.transform.position;
        Check(W.Powers.Use(rt), "Dash toward a wall."); yield return new WaitForSeconds(stats.Duration + .3f);
        float blocked = W.Hero.transform.position.z - start.z;
        Check(blocked < 3.6f && blocked > 1.5f, $"CONTROL: the wall stops the dash at {blocked:F2} m (no tunnelling).");
        rt.Charges = 0; rt.Cooldown = 0;
        Check(!W.Powers.Use(rt) && !W.Hero.Dashing && W.Powers.Message == "Blocked: 0 charges", "CONTROL: zero charges refused.");
    }
    // ---------------------------------------------------------------- POISON: DoT + spread
    IEnumerator Poison()
    {
        Log("---- POISON (damage over time, spreads on death)");
        yield return Enter("poison", "strength");
        Isolate(); var rt = Runtime("poison"); var stats = W.Powers.Stats(rt); var effect = (PoisonEffect)rt.Definition.Effect;
        var p = Actor(new Vector3(-4, 150, 7), NpcRole.Criminal, 1000); var cop = Actor(new Vector3(4, 150, 8), NpcRole.Cop, 1000);
        yield return new WaitForSeconds(.3f);
        int pulsesBefore=StatusPowerVfx.Get().PoisonPulses;
        Aim(Chest(p)); Check(W.Powers.Use(rt), "Launch Poison at the criminal.");
        float arrival=Time.time+2f;while(p.GetComponent<Poisoned>()==null&&Time.time<arrival)yield return null;
        Check(p.GetComponent<Poisoned>()?.Active==true,"Physical Poison projectile actually hit the criminal before checking ticks.");
        rt.Cooldown = 0; float heat = W.Heat; Aim(Chest(cop)); Check(W.Powers.Use(rt), "Launch Poison at the cop.");
        arrival=Time.time+2f;while(cop.GetComponent<Poisoned>()==null&&Time.time<arrival)yield return null;
        Check(cop.GetComponent<Poisoned>()?.Active==true,"Physical Poison projectile actually hit the cop before checking ticks.");
        var statusPool=StatusPowerVfx.Get();
        yield return new WaitForSeconds(stats.Duration + .6f);
        int actualTicks=p.GetComponent<Poisoned>().Ticks+cop.GetComponent<Poisoned>().Ticks;
        Check(statusPool.PoisonPulses-pulsesBefore==actualTicks&&statusPool.ActivePoisons==0,
            "Each real DoT tick gives one pooled toxic pulse and expiry clears both status visuals.");
        float expected = stats.Damage * stats.Duration;
        Check(Mathf.Abs(1000 - p.Health - expected) < .01f && !p.GetComponent<Poisoned>().Active, $"Total poison damage == {stats.Damage}/s x {stats.Duration} s = {expected} ({1000 - p.Health:F2}); then it stops.");
        Check(Mathf.Abs((W.Heat - heat) - W.Tuning.Heat.AssaultHeat) < .02f, $"Poisoning a cop adds ONE assault of Heat ({W.Heat - heat:F3}) over {cop.GetComponent<Poisoned>().Ticks} ticks.");
        // Spread on death.
        var s = Actor(new Vector3(0, 150, 10), NpcRole.Criminal, 10);
        var n1 = Actor(new Vector3(2, 150, 10), NpcRole.Criminal, 1000); var n2 = Actor(new Vector3(0, 150, 12.5f), NpcRole.Criminal, 1000);
        var n3 = Actor(new Vector3(-3, 150, 10.5f), NpcRole.Criminal, 1000); var fourth = Actor(new Vector3(4, 150, 13), NpcRole.Criminal, 1000);
        var civ = Actor(new Vector3(1, 150, 8), NpcRole.Civilian, 1000); var outside = Actor(new Vector3(0, 150, 17.5f), NpcRole.Criminal, 1000);
        int spreads=0,visualSpreads=Poisoned.VisualSpreads;
        Action<CityNpc, CityNpc> onSpread=(a,b)=>spreads++;Poisoned.Spreading+=onSpread;
        rt.Charges = Mathf.Max(rt.Charges, 1); rt.Cooldown = 0; Aim(Chest(s)); Check(W.Powers.Use(rt), "Poison a 10 HP criminal with neighbours.");
        float until = Time.time + 3; while (!s.Dead && Time.time < until) yield return null;
        Check(s.Dead, "The poisoned criminal dies from the poison ticks.");
        bool On(CityNpc n) { var q = n.GetComponent<Poisoned>(); return q != null && q.Active; }
        Check(On(n1) && On(n2) && On(n3) && spreads == 3, $"Poison jumped to the three nearest enemies (events {spreads}).");
        Check(Poisoned.VisualSpreads-visualSpreads==spreads&&
            new[]{n1,n2,n3}.All(n=>n.GetComponent<Poisoned>().VisualActive),
            "Spread visuals match only the three actual poisoned recipients, all with active pooled status cues.");
        Check(new[] { n1, n2, n3 }.All(n => n.GetComponent<Poisoned>().Generation == 1), "Spread targets are generation 1.");
        Check(!On(fourth), "CONTROL: the 4th enemy in range is skipped (MaxSpreadTargets).");
        Check(!On(civ), "CONTROL: the civilian beside it is not poisoned (enemies only).");
        Check(!On(outside), $"CONTROL: the enemy {Vector3.Distance(outside.transform.position, s.transform.position):F1} m away (radius {effect.SpreadRadius}) is not poisoned.");
        Capture("poison-spread.png");
        // Any killing blow spreads; an expired poison does not; the generation cap holds.
        var k = Actor(new Vector3(30, 150, 10), NpcRole.Criminal, 1000); var k2 = Actor(new Vector3(32, 150, 10), NpcRole.Criminal, 1000);
        Poisoned.Apply(k, W.Powers, effect, stats.Damage, stats.Duration, 0, rt.Definition.PaletteColor); k.Damage(5000, W.Powers);
        Check(k.Dead && On(k2), "Killed by a non-poison hit while poisoned: the poison still spreads.");
        var e = Actor(new Vector3(-30, 150, 10), NpcRole.Criminal, 1000); var e2 = Actor(new Vector3(-28, 150, 10), NpcRole.Criminal, 1000);
        Poisoned.Apply(e, W.Powers, effect, stats.Damage, effect.TickSeconds * 2, 0, rt.Definition.PaletteColor);
        yield return new WaitForSeconds(effect.TickSeconds * 2 + .3f);
        Check(!On(e), "Short poison has run out."); e.Damage(5000, W.Powers);
        Check(e.Dead && !On(e2), "CONTROL: dying after the poison ran out spreads nothing.");
        var g = Actor(new Vector3(-30, 150, 30), NpcRole.Criminal, 1000); var g2 = Actor(new Vector3(-28, 150, 30), NpcRole.Criminal, 1000);
        Poisoned.Apply(g, W.Powers, effect, stats.Damage, stats.Duration, effect.MaxGenerations, rt.Definition.PaletteColor); g.Damage(5000, W.Powers);
        Check(g.Dead && !On(g2), $"CONTROL: generation {effect.MaxGenerations} (the cap) does not spread further.");
        Poisoned.Spreading -= onSpread;
    }
    // ---------------------------------------------------------------- the capped synergies
    static readonly string[] Capped = { "sonic-slam", "thermal-shock", "solar-flare", "void-grasp", "eclipse-beam" };
    IEnumerator RosterSynergies()
    {
        Log("---- SYNERGIES (capped at five)");
        Check(Capped.All(id => F.Synergies.Count(x => x != null && x.Id == id) == 1), "The five in-scope synergies exist exactly once: " + string.Join(", ", Capped));
        var all = Resources.LoadAll<PowerDefinition>("Powers").Where(p => !p.Id.StartsWith("verification-")).ToArray();
        int newPairs = 0;
        for (int i = 0; i < all.Length; i++) for (int j = i + 1; j < all.Length; j++)
            {
                if (!NewPowers.Contains(all[i].Id) && !NewPowers.Contains(all[j].Id)) continue;
                var s = F.Resolve(all[i], all[j]);
                Check(s == null || Capped.Contains(s.Id), $"Pair {all[i].Id} + {all[j].Id} -> {(s != null ? s.Id : "none")} (only capped synergies allowed).");
                if (s != null) newPairs++;
            }
        Check(newPairs == 3, "Exactly three pairs involving a new power have a synergy (Solar Flare, Void Grasp, Eclipse Beam).");
        Log("REPORT legacy synergies outside the cap, left in place (not deleted): " + string.Join(", ", F.Synergies.Where(x => x != null && !Capped.Contains(x.Id)).Select(x => x.Id)));
        yield return SolarFlare();
        yield return VoidGrasp();
        yield return EclipseBeam();
    }
    IEnumerator Cooldown(SynergyRunner r, PowerSynergyDefinition d)
    {
        Check(!r.TryActivate() && r.Feedback == "Synergy cooling down", $"{d.DisplayName}: mid-cooldown use refused ({r.Cooldown:F1} s left).");
        Check(r.Cooldown > 30 && d.Cooldown >= 40, $"{d.DisplayName}: long cooldown {d.Cooldown} s (normal powers <= 1 s).");
        yield break;
    }
    IEnumerator SolarFlare()
    {
        yield return Enter("fire", "laser-eyes"); Isolate();
        var r = W.Powers.SynergyRunner; var d = r.Definition;
        Check(d != null && d.Id == "solar-flare", "Fire + Laser Eyes resolves to Solar Flare (no unlock step).");
        var v = Actor(new Vector3(0, 150, 12), NpcRole.Criminal, 1000); var v2 = Actor(new Vector3(3, 150, 12), NpcRole.Criminal, 1000);
        var outside = Actor(new Vector3(12, 150, 12), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        Aim(W.Hero.transform.position + new Vector3(0, 60, 10));
        Check(!r.TryActivate() && r.Cooldown == 0, "CONTROL: no target under the crosshair refuses with no cooldown.");
        Aim(Chest(v)); Check(r.TryActivate(), "Solar Flare fires immediately when equipped.");
        yield return null; Check(PowerVfx.Get().BeamVisible, "Focusing beam drawn."); Capture("solar-flare-beam.png");
        float until = Time.time + 4; while (r.Busy && Time.time < until) yield return null;
        Check(r.Impacts == 1 && Mathf.Abs(1000 - v.Health - d.Damage) < .01f && Mathf.Abs(1000 - v2.Health - d.Damage) < .01f,
            $"Eruption hits both enemies in {d.Radius} m for {d.Damage}: {1000 - v.Health:F1} / {1000 - v2.Health:F1}.");
        Check(v.Burning && v2.Burning, "Targets left burning (feeds Thermal Shock's bonus rule).");
        Check(outside.Health == 1000, "CONTROL: the enemy 12 m away is untouched.");
        Check(!PowerVfx.Get().BeamVisible, "Beam hidden after the eruption.");
        yield return Cooldown(r, d);
    }
    IEnumerator VoidGrasp()
    {
        yield return Enter("darkness", "telekinesis"); Isolate();
        var r = W.Powers.SynergyRunner; var d = r.Definition;
        Check(d != null && d.Id == "void-grasp", "Darkness + Telekinesis resolves to Void Grasp.");
        var a = Actor(new Vector3(4, 150, 10), NpcRole.Criminal, 1000); var b = Actor(new Vector3(-5, 150, 12), NpcRole.Criminal, 1000);
        var c = Actor(new Vector3(0, 150, 16), NpcRole.Criminal, 1000); var civ = Actor(new Vector3(2, 150, 7), NpcRole.Civilian, 1000);
        var outside = Actor(new Vector3(0, 150, 22), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        Aim(new Vector3(-15, 150, 18));
        Check(!r.TryActivate() && r.Cooldown == 0, "CONTROL: a singularity with no enemy in reach is refused with no cooldown.");
        Vector3 core = new Vector3(0, 150, 10); Aim(core); core += Vector3.up * ((VoidGraspEffect)d.Effect).CoreHeight;
        var pulled = new[] { a, b, c }; var start = pulled.Select(n => Vector3.Distance(n.transform.position, core)).ToArray();
        Vector3 civStart = civ.transform.position, outsideStart = outside.transform.position;
        Check(r.TryActivate(), "Void Grasp opens immediately when equipped.");
        yield return new WaitForSeconds(d.Duration * .8f);
        var mid = pulled.Select(n => Vector3.Distance(n.transform.position, core)).ToArray();
        Log($"MEASURED pull: distance to core {string.Join(", ", start.Select(x => x.ToString("F2")))} -> {string.Join(", ", mid.Select(x => x.ToString("F2")))} m.");
        Check(VoidGraspEffect.LastPulled.Count == 3 && VoidGraspEffect.LastPulled.All(pulled.Contains), "Exactly the three enemies in reach are grasped.");
        Check(Enumerable.Range(0, 3).All(i => mid[i] < start[i] - 1.5f), "Each grasped enemy is physically pulled >1.5 m toward the core.");
        Check(Vector3.Distance(civStart, civ.transform.position) < .05f, "CONTROL: the civilian inside the radius is not pulled.");
        Check(Vector3.Distance(outsideStart, outside.transform.position) < .05f && outside.Health == 1000, "CONTROL: the enemy outside the radius is untouched.");
        Capture("void-grasp.png");
        float until = Time.time + 4; while (r.Busy && Time.time < until) yield return null;
        Check(r.Impacts == 1 && pulled.All(n => 1000 - n.Health >= d.Damage - .01f), $"Collapse blast damages every grasped enemy ({d.Damage}).");
        Check(pulled.All(n => n.Rooted), $"Survivors left rooted for {d.FreezeSeconds} s.");
        yield return Cooldown(r, d);
    }
    IEnumerator EclipseBeam()
    {
        yield return Enter("darkness", "laser-eyes"); Isolate();
        var r = W.Powers.SynergyRunner; var d = r.Definition;
        Check(d != null && d.Id == "eclipse-beam", "Darkness + Laser Eyes resolves to Eclipse Beam.");
        var t = Actor(new Vector3(0, 150, 12), NpcRole.Criminal, 1000); var bystander = Actor(new Vector3(1.2f, 150, 12.5f), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        Aim(new Vector3(-6, 150, 8));
        Check(!r.TryActivate() && r.Cooldown == 0, "CONTROL: aiming at the floor (no enemy) refuses with no cooldown.");
        Aim(Chest(t)); Check(r.TryActivate(), "Eclipse Beam starts on the aimed enemy.");
        yield return new WaitForSeconds(d.LiftSeconds * .5f);
        Check(t.Rooted && t.Health == 1000, "Darkness field roots the target before any damage.");
        yield return new WaitForSeconds(d.LiftSeconds * .5f + .1f);
        Check(PowerVfx.Get().BeamVisible, "Beam fires through the field."); Capture("eclipse-beam.png");
        float until = Time.time + 4; while (r.Busy && Time.time < until) yield return null;
        Check(Mathf.Abs(1000 - t.Health - d.Damage) < .01f && Mathf.Abs(EclipseBeamEffect.LastDamage - d.Damage) < .01f, $"One single-target hit of {d.Damage} ({1000 - t.Health:F1}).");
        Check(bystander.Health == 1000, "CONTROL: the enemy 1.3 m beside the target takes nothing (no area blast).");
        yield return Cooldown(r, d);
    }
}
#endif
