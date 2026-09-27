#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// See MissionVerification. Each mission is spawned with WorldSession.SpawnEncounter (the path GameModeSession.SpawnNext
/// uses) at a real encounter site picked by CityDistrict.PickEncounterSite, well away from the session's own encounter.
/// "Away" phases park the (disabled) hero 150 m above the site, beyond every NPC's detection range. Fail paths that
/// would take minutes use IN-MEMORY clones of the assets with one timing value shortened (never saved).
public sealed class MissionVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/Missions/";
    protected override string ResultFile => "results.txt";
    readonly List<EncounterOutcome> outcomes = new List<EncounterOutcome>();
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        foreach (var id in new[] { "mission-robbery", "mission-hostage", "mission-fire", "mission-heist" })
        {
            var d = Resources.Load<EncounterDefinition>("Encounters/" + id);
            Check(d != null && d.Scenario != null && d.Scenario.Tasks.Length > 0, $"{id}: scenario {d?.Scenario?.GetType().Name}, tasks: {string.Join(" / ", d?.Scenario?.Tasks.Select(t => t.Label) ?? new string[0])}.");
        }
        yield return Robbery();
        yield return Hostage();
        yield return Fire();
        yield return Heist();
        Log("NOTE: the shipping Hero/Villain modes still list the original encounters until MissionSetup.UseInModes is run (explicit content switch).");
        Log("LIMIT: movement/aim through teleport + the entry points input calls; NPC combat, cars and physics are live. No human playtest of readability, pacing or difficulty.");
    }
    // ---------------------------------------------------------------- helpers
    IEnumerator Session(string a, string b, string mode)
    {
        yield return Enter(F.Heroes[0], a, b, mode);
        outcomes.Clear(); W.Mode.EncounterResolved += o => outcomes.Add(o);
        Cam.GetComponent<ThirdPersonCamera>().enabled = false;
    }
    CrimeEncounter Spawn(string id, Action<EncounterDefinition> shorten = null)
    {
        var def = Resources.Load<EncounterDefinition>("Encounters/" + id);
        if (shorten != null) { def = Instantiate(def); def.Scenario = Instantiate(def.Scenario); shorten(def); }
        int district = -1;
        Check(W.City.PickEncounterSite(W.Hero.transform.position, s => W.Crimes.TrueForAll(c => c == null || c.Encounter == null || Vector3.Distance(c.Encounter.Site, s) > 45f), ref district, out var site),
            "Real encounter site found for " + id);
        var crime = W.SpawnEncounter(def, site);
        Check(crime != null && crime.Encounter != null && crime.Encounter.Scenario != null, $"{def.DisplayName} spawned at {site} with its scenario{(shorten != null ? " (in-memory clone, one timing shortened)" : "")}.");
        return crime.Encounter;
    }
    string Line(CrimeEncounter e) => W.Mode.Definition.Rules.Current(e).Text;
    void Away(CrimeEncounter e) => PlaceHero(e.Site + Vector3.up * 150f);
    void Ground(Vector3 at) => PlaceHero(at + Vector3.up * .05f);
    IEnumerator Outcome(CrimeEncounter e, float seconds)
    {
        float until = Time.time + seconds;
        while (!outcomes.Exists(o => ReferenceEquals(o.Encounter, e)) && Time.time < until) yield return null;
    }
    EncounterOutcome Result(CrimeEncounter e) => outcomes.FirstOrDefault(o => ReferenceEquals(o.Encounter, e));
    bool Ended(CrimeEncounter e) => outcomes.Exists(o => ReferenceEquals(o.Encounter, e));
    // ---------------------------------------------------------------- ROBBERY
    IEnumerator Robbery()
    {
        Log("---- ROBBERY GETAWAY (hero)");
        yield return Session("ice", "strength", "hero");
        var e = Spawn("mission-robbery"); var s = (RobberyState)e.Scenario; Away(e); var scenario = (RobberyScenario)e.Definition.Scenario;
        Check(e.Robbers.Count == 3 && s.Cars.Count == 2 && s.Cars.All(c => c.Body != null && !c.Body.isKinematic && !c.Driving), "3 robbers and 2 parked getaway cars (ordinary dynamic props, not driving).");
        Check(Line(e) == "STOP THE GETAWAY 0/3", $"Objective line: \"{Line(e)}\".");
        var parked = s.Cars.Select(c => c.Body.position).ToArray();
        yield return new WaitForSeconds(1f);
        Check(s.Cars.All(c => c.Driving || Vector3.Distance(c.Body.position, parked[s.Cars.IndexOf(c)]) < .3f), "CONTROL: a car does not move before the mission says depart.");
        // FAIL PATH: nobody stops them.
        float until = Time.time + 30; while (!s.Cars.Exists(c => c.Driving) && Time.time < until) yield return null;
        var driving = s.Cars.FirstOrDefault(c => c.Driving);
        Check(driving != null && driving.Body.isKinematic && driving.Route != null && driving.Route.Length >= 2, $"A car departs on a route: {s.LastEvent}.");
        Vector3 p0 = driving.Body.position; float driven0 = driving.Driven; yield return new WaitForSeconds(1.5f);
        float moved = driving.Body != null ? Vector3.Distance(p0, driving.Body.position) : 0;
        Log($"MEASURED getaway drive: {moved:F2} m straight-line, {driving.Driven - driven0:F2} m along the route in 1.5 s (speed {scenario.CarSpeed} m/s).");
        Check(moved > scenario.CarSpeed * 1.5f * .5f && driving.Driven - driven0 > scenario.CarSpeed * 1.5f * .9f, "The getaway car really leaves: meaningful displacement along its route (no curb can hold it).");
        yield return Outcome(e, 45);
        string reason = Ended(e) ? Result(e).Reason : "no outcome";
        Check(Ended(e) && !Result(e).Success && reason.Contains("getaway car got away"), $"Too slow: mission FAILED with the CAR reason \"{reason}\" ({s.LastEvent}).");
        Check(!reason.Contains("on foot"), "The car getaway reason is distinct from a robber escaping on foot.");
        // WIN PATH 1: take two down, freeze the car the third boards, cuff him when he bails.
        e = Spawn("mission-robbery"); s = (RobberyState)e.Scenario; Away(e);
        e.Robbers[0].Npc.Damage(9999, W.Powers); e.Robbers[1].Npc.Damage(9999, W.Powers);
        Check(Line(e) == "STOP THE GETAWAY 2/3" && !Ended(e), "Two robbers taken down: 2/3, mission still running.");
        var last = e.Robbers[2]; var car = s.Cars[0];
        until = Time.time + 25; while (!car.Driving && Time.time < until) yield return null;
        Check(car.Driving && car.Aboard.Contains(last) && !last.Npc.gameObject.activeSelf, "The last robber boarded and the car pulled away.");
        yield return new WaitForSeconds(.6f);
        Vector3 back = (car.Body.position - e.Site); back.y = 0; Vector3 behind = car.Body.position - back.normalized * 8f; behind.y = e.Site.y; Ground(behind);
        var ice = Runtime("ice"); W.Powers.Select(ice); ice.Charges = 2; ice.Cooldown = 0;
        Aim(car.Body.worldCenterOfMass); Check(W.Powers.Use(ice), "Ice cast at the moving getaway car.");
        yield return null; yield return null;
        Check(car.Stalled && car.StopReason == "frozen" && !car.Body.isKinematic && car.Body.GetComponent<FrozenBody>() != null && last.Npc.gameObject.activeSelf,
            $"Frozen car stops (dynamic again, frozen in place) and the robber bails out on foot ({s.LastEvent}).");
        Ground(last.Npc.transform.position + Vector3.forward * 1.5f);
        float standoff = Vector3.Distance(W.Hero.transform.position, last.Npc.transform.position);
        e.ScriptedHold = true; yield return new WaitForSeconds(1.4f); e.ScriptedHold = false;
        Check(!last.Captured && !Ended(e), $"CONTROL: holding R {standoff:F2} m from an un-subdued robber (frozen {last.Npc.Frozen}, rooted {last.Npc.Rooted}) does not cuff him.");
        ice.Cooldown = 0; Aim(Chest(last.Npc)); Check(W.Powers.Use(ice) && last.Npc.Frozen, "Freeze the bailed robber.");
        Ground(last.Npc.transform.position + Vector3.forward * 1.5f);
        standoff = Vector3.Distance(W.Hero.transform.position, last.Npc.transform.position);
        Check(standoff < scenario.CuffRadius && e.InteractableNear(W.Hero.transform.position), $"Standing {standoff:F2} m from the frozen robber: inside the {scenario.CuffRadius} m cuff radius, interactable.");
        // R is held through the encounter's own Update path (CrimeEncounter.ScriptedHold), exactly as the key would be.
        e.ScriptedHold = true; until = Time.time + 3f; while (!Ended(e) && Time.time < until) yield return null; e.ScriptedHold = false;
        Log($"CUFF state: Captured={last.Captured}, scenario Finished={e.Finished}, outcome recorded={Ended(e)}, Result={(Ended(e) ? (Result(e).Success ? "SUCCESS" : "FAIL") + " '" + Result(e).Reason + "'" : "none")}, distance {standoff:F2} m.");
        Check(last.Captured && Ended(e) && Result(e).Success, "Cuffing the frozen robber (held R) completes the mission: SUCCESS.");
        // WIN PATH 2: wreck the car with the robber inside, while it drives (below the stop impulse, so it keeps driving).
        e = Spawn("mission-robbery"); s = (RobberyState)e.Scenario; Away(e);
        e.Robbers[0].Npc.Damage(9999, W.Powers); e.Robbers[1].Npc.Damage(9999, W.Powers); car = s.Cars[0];
        until = Time.time + 25; while (!car.Driving && Time.time < until) yield return null;
        Check(car.Driving && car.Aboard.Count == 1, "Last robber is in the driving car.");
        CombatImpact.Blast(W.Powers, car.Body.worldCenterOfMass, 2f, scenario.StopImpulse * .4f, 1000f, .2f);
        yield return Outcome(e, 3);
        Check(car.Wrecked && Ended(e) && Result(e).Success, $"Wrecking the driving car (blast damage on the kinematic car) catches the robber inside: SUCCESS ({s.LastEvent}).");
        // HEAVY HIT: a punch-strength blast knocks a driving car out of its drive; the robber bails.
        e = Spawn("mission-robbery"); s = (RobberyState)e.Scenario; Away(e);
        e.Robbers[0].Npc.Damage(9999, W.Powers); e.Robbers[1].Npc.Damage(9999, W.Powers); car = s.Cars[0]; last = e.Robbers[2];
        until = Time.time + 25; while (!car.Driving && Time.time < until) yield return null;
        yield return new WaitForSeconds(.5f);
        CombatImpact.Blast(W.Powers, car.Body.worldCenterOfMass - Vector3.up * .5f, 2f, scenario.StopImpulse * .3f, 0f, .2f);
        Check(car.Driving, "CONTROL: a light hit (below StopImpulse) does not stop the car.");
        CombatImpact.Blast(W.Powers, car.Body.worldCenterOfMass - Vector3.up * .5f, 2f, 1350f, 0f, .2f);
        Check(car.Stalled && car.StopReason.StartsWith("rammed") && !car.Body.isKinematic, $"A 1350 N.s hit knocks the car out of its drive ({car.StopReason}).");
        yield return null;
        Check(last.Npc.gameObject.activeSelf && !Ended(e), "The robber inside bails out on foot; mission continues.");
    }
    // ---------------------------------------------------------------- HOSTAGE
    IEnumerator Hostage()
    {
        Log("---- HOSTAGE RESCUE (hero)");
        yield return Session("lightning", "strength", "hero");
        // FAIL PATH (clone: grace 1 s, lethal damage rate): get spotted and leave the gunmen up.
        var e = Spawn("mission-hostage", d => { var h = (HostageScenario)d.Scenario; h.AlertGraceSeconds = 1f; h.HostageDamagePerSecond = 30f; });
        Ground(e.Site + Vector3.back * 12f);
        yield return Outcome(e, 8);
        Check(Ended(e) && !Result(e).Success && Result(e).Reason.Contains("hostage"), $"Spotted with gunmen up: mission FAILED \"{(Ended(e) ? Result(e).Reason : "no outcome")}\".");
        // WIN PATH.
        e = Spawn("mission-hostage"); var s = (HostageState)e.Scenario; Away(e);
        Check(s.Gunmen.Count == 3 && e.Civilians.Count == 3 && e.Civilians.All(h => h.Blockade != null && !s.Freed(h)), "3 gunmen guarding 3 hostages, each pinned by debris.");
        Check(Line(e) == "TAKE DOWN THE GUNMEN 0/3", $"Objective line: \"{Line(e)}\".");
        Check(!s.Alerted, "CONTROL: with the player out of sight nobody is alerted.");
        foreach (var g in s.Gunmen) g.Damage(9999, W.Powers);
        yield return null;
        Check(s.GunmenLeft == 0 && Line(e) == "FREE THE HOSTAGES 0/3", $"Gunmen down -> \"{Line(e)}\".");
        var start = e.Civilians.Select(h => h.Npc.transform.position).ToArray();
        yield return new WaitForSeconds(1.5f);
        Check(e.Civilians.All(h => !s.Freed(h) && !h.Saved) && Enumerable.Range(0, 3).All(i => Vector3.Distance(start[i], e.Civilians[i].Npc.transform.position) < .3f),
            "CONTROL: still pinned (debris in place) -> hostages stay put.");
        foreach (var h in e.Civilians)
        {
            Vector3 push = h.Blockade.position - h.Npc.transform.position; push.y = 0;
            CombatImpact.Blast(W.Powers, h.Blockade.position - push.normalized * .9f, 1.2f, 6000f, 0f, .2f);
        }
        yield return new WaitForSeconds(1f);
        Check(e.Civilians.All(s.Freed), "Blasting the debris clear (0-damage shockwaves) frees every hostage.");
        yield return Outcome(e, 30);
        Check(Ended(e) && Result(e).Success && e.Civilians.All(h => h.Saved), "Freed hostages run to the safe point: SUCCESS.");
    }
    // ---------------------------------------------------------------- FIRE
    IEnumerator Fire()
    {
        Log("---- BUILDING FIRE (hero)");
        yield return Session("ice", "strength", "hero");
        var e = Spawn("mission-fire", d => { var f = (FireScenario)d.Scenario; f.CivilianBurnAfter = 0f; f.CivilianBurnPerSecond = 30f; });
        Away(e); yield return Outcome(e, 6);
        Check(Ended(e) && !Result(e).Success && Result(e).Reason.Contains("fire"), $"Left burning: mission FAILED \"{(Ended(e) ? Result(e).Reason : "no outcome")}\".");
        e = Spawn("mission-fire"); var s = (FireState)e.Scenario; Away(e);
        Check(s.Spots.Count == 4 && s.Spots.All(x => !x.Out) && e.Civilians.All(s.Trapped), "4 fire spots burning, 2 civilians trapped.");
        Check(Line(e) == "PUT OUT THE FIRE 0/4", $"Objective line: \"{Line(e)}\".");
        Ground(e.Civilians[0].Npc.transform.position + Vector3.forward * 1.2f); yield return new WaitForSeconds(.6f);
        Check(s.Following.Count == 0, "CONTROL: a trapped civilian will not follow while the fire blocks it.");
        Vector3 Out(FireSpot f) { Vector3 o = f.transform.position - e.Site; o.y = 0; return f.transform.position + o.normalized * 6f; }
        // Spot 0: Ice x2.
        var ice = Runtime("ice"); W.Powers.Select(ice); ice.Charges = 2; ice.Cooldown = 0;
        var f0 = s.Spots[0]; Ground(Out(f0)); Aim(f0.transform.position + Vector3.up);
        int charges = ice.Charges; Check(W.Powers.Use(ice) && ice.Charges == charges - 1 && Mathf.Abs(f0.Heat - .45f) < .02f, $"Ice cast douses a fire spot (paid): heat {f0.Heat:F2}.");
        ice.Cooldown = 0; Check(W.Powers.Use(ice) && f0.Out && f0.IceHits == 2, "Second Ice cast puts it out.");
        // Spot 1: heavy impacts; light CONTROL.
        var f1 = s.Spots[1];
        CombatImpact.Blast(W.Powers, f1.transform.position + Vector3.up, 1.2f, 450f, 0f, .2f);
        Check(f1.ImpactHits == 0 && f1.Heat > .99f, "CONTROL: a light 450 N.s hit (Fire Blast strength) does not douse.");
        for (int i = 0; i < 3; i++) CombatImpact.Blast(W.Powers, f1.transform.position + Vector3.up, 1.2f, 1350f, 0f, .2f);
        Check(f1.Out && f1.ImpactHits == 3, "Three punch-strength (1350 N.s) shockwaves snuff a spot.");
        // Spot 3: regrow CONTROL, then Ice.
        var f3 = s.Spots[3]; CombatImpact.Blast(W.Powers, f3.transform.position + Vector3.up, 1.2f, 1350f, 0f, .2f); float h0 = f3.Heat;
        yield return new WaitForSeconds(2f);
        Check(f3.Heat > h0 + .03f, $"A half-doused fire regrows: {h0:F2} -> {f3.Heat:F2} in 2 s.");
        ice.Charges = 2; ice.Cooldown = 0; Ground(Out(f3)); Aim(f3.transform.position + Vector3.up); W.Powers.Use(ice); ice.Cooldown = 0; W.Powers.Use(ice);
        Check(f3.Out, "Spot 3 out.");
        // Spot 2: the slow hold-R spray.
        var f2 = s.Spots[2]; Vector3 side = f2.transform.position - e.Site; side.y = 0; Ground(f2.transform.position + side.normalized * 1.5f);
        float held = 0; while (!f2.Out && held < 12f) { held += Time.deltaTime; e.Interact(Time.deltaTime); yield return null; }
        Check(f2.Out && held > 4f && held < 8f, $"Holding R sprays a spot out slowly: {held:F1} s (powers are near-instant).");
        Check(e.Civilians.All(c => !s.Trapped(c)) && Line(e) == "LEAD CIVILIANS OUT 0/2", $"Fire out -> civilians free; objective \"{Line(e)}\".");
        Ground(e.Site + Vector3.left * 1.5f); yield return new WaitForSeconds(.6f);
        Check(s.Following.Count == 2, "Walking up to the freed civilians makes them follow.");
        Ground(s.SafePoint);
        yield return Outcome(e, 30);
        Check(Ended(e) && Result(e).Success && e.Civilians.All(c => c.Saved), "Leading them to the safe point completes the mission: SUCCESS.");
    }
    // ---------------------------------------------------------------- HEIST
    IEnumerator Heist()
    {
        Log("---- VAULT HEIST (villain)");
        yield return Session("fire", "ice", "villain");
        var e = Spawn("mission-heist", d => d.Deadline = 3f); Away(e);
        yield return Outcome(e, 6);
        Check(Ended(e) && !Result(e).Success && Result(e).Reason.Contains("deadline"), $"Too slow: mission FAILED \"{(Ended(e) ? Result(e).Reason : "no outcome")}\".");
        e = Spawn("mission-heist"); var s = (HeistState)e.Scenario; Away(e);
        Check(!s.Vault.Cracked && e.Loot.Count == 0 && Line(e) == "CRACK THE VAULT 0/100", $"Vault intact, no loot yet; objective \"{Line(e)}\".");
        Ground(e.Site + Vector3.back * 9f);
        var ice = Runtime("ice"); W.Powers.Select(ice); ice.Cooldown = 0; int charges = ice.Charges; Aim(s.Vault.transform.position);
        Check(!W.Powers.Use(ice) && ice.Charges == charges && !s.Vault.Cracked && s.Vault.Hits == 0, "CONTROL: Ice does nothing to the vault (no charge spent).");
        var fire = Runtime("fire"); W.Powers.Select(fire); fire.Cooldown = 0; Aim(s.Vault.transform.position);
        float health = s.Vault.Health; Check(W.Powers.Use(fire), "Fire Blast at the vault.");
        yield return new WaitForSeconds(1f);
        Check(s.Vault.Hits >= 1 && s.Vault.Health < health, $"Projectile blast damages the vault: {health} -> {s.Vault.Health} ({Line(e)}).");
        yield return new WaitForSeconds(((HeistScenario)e.Definition.Scenario).ResponseDelay + .5f);
        Check(s.ResponseSent && s.ResponseSpawned == ((HeistScenario)e.Definition.Scenario).ResponseCops && e.Responders.All(c => c == null || c.Hostile), $"Alarm: {s.ResponseSpawned} more police respond, hostile to the villain ({e.Responders.Count} at the site).");
        foreach (var c in e.Responders) if (c != null) c.Damage(9999, W.Powers);
        float heat = W.Heat;
        CombatImpact.Blast(W.Powers, s.Vault.transform.position, 2f, 1350f, 400f, .2f); yield return null; yield return null;
        Check(s.Vault.Cracked && e.Loot.Count == 3 && W.Heat > heat && Line(e) == "GRAB THE LOOT 0/3", $"Vault cracked: 3 loot bags, Heat {heat:F2} -> {W.Heat:F2}; objective \"{Line(e)}\".");
        foreach (var bag in e.Loot) { Ground(bag.Visual.transform.position - Vector3.up * e.Definition.MarkerHeight); yield return null; yield return null; }
        Check(e.LootTaken == 3 && e.Loot.All(b => !b.Visual.activeSelf), "Running over the bags grabs all three.");
        yield return new WaitForSeconds(.5f);
        Check(!Ended(e) && Line(e).StartsWith("REACH THE GETAWAY VAN"), $"CONTROL: loot in hand but away from the van -> not complete (\"{Line(e)}\").");
        Ground(s.Van); yield return Outcome(e, 3);
        Check(Ended(e) && Result(e).Success, "Reaching the van with the loot completes the heist: SUCCESS.");
    }
}
#endif
