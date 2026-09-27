#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// See RosterVerification. Every power is equipped through PlayerProgression.SetLoadout (the Hero Forge save path), entered in
/// a real Hero session, and fired through PowerUser.Use (LMB) / PowerUser.Channel (held LMB) against real CityNpc actors.
/// Fixtures that need no navigation stand on an isolated floor 150 m above the city (the Forge suite's pattern, AI off);
/// the Darkness root is measured on LIVE, NavMesh-driven criminals in the city.
public sealed class RosterVerificationRunner : MonoBehaviour
{
    public Action<int> Finished; public bool Reload;
    const string Folder = "Verification/Roster/";
    readonly List<string> output = new List<string>(); string runtimeFailure;
    WorldSession W => WorldSession.Instance;
    ForgeCatalog F => Resources.Load<ForgeCatalog>("ForgeCatalog");
    Camera Cam => Camera.main;
    PowerDefinition Power(string id) => Resources.Load<PowerDefinition>("Powers/" + id);
    PowerRuntime Runtime(string id) => W.Powers.Powers.Find(p => p.Definition.Id == id);
    void Awake() { Application.logMessageReceived += ObserveLog; }
    void OnDestroy() { Application.logMessageReceived -= ObserveLog; }
    void ObserveLog(string message, string trace, LogType type)
    { if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && trace.Contains("Assets/Scripts/")) runtimeFailure = message; }
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder); var stack = new Stack<IEnumerator>(); stack.Push(Checks());
        while (stack.Count > 0)
        {
            object next = null; bool moved = false;
            try { if (runtimeFailure != null) throw new Exception("Gameplay Console error: " + runtimeFailure); moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception e) { Log("FAIL " + e); Write(); Finished(1); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested); else yield return next;
        }
        Write(); Finished(0);
    }
    void Log(string line) { output.Add(line); Debug.Log(line); }
    void Check(bool ok, string line) { if (!ok) throw new Exception(line); Log("PASS " + line); }
    void Write() { File.WriteAllLines(Folder + (Reload ? "reload.txt" : "results.txt"), output); }
    IEnumerator Scene(string name)
    {
        float until = Time.realtimeSinceStartup + 60;
        while (GameFlow.Instance == null || GameFlow.Instance.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        { if (Time.realtimeSinceStartup > until) throw new Exception("Scene timeout: " + name); yield return null; }
        yield return new WaitForSecondsRealtime(.5f);
    }
    static readonly string[] NewPowers = { "darkness", "laser-eyes", "lightning", "force-field", "speed", "poison" };
    IEnumerator Checks()
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
        Check(Resources.LoadAll<PowerDefinition>("Powers").Count(p => !p.Id.StartsWith("verification-")) == 11, "Shipping roster is 11 powers.");
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
    // ---------------------------------------------------------------- session + fixtures
    IEnumerator Enter(string a, string b)
    {
        if (SceneManager.GetActiveScene().name != GameFlow.HomeScene) { GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene); }
        var profile = FindAnyObjectByType<ModeScreens>().Profile;
        Check(profile.SetLoadout(F.Heroes[0], Power(a), Power(b), CityColor.Blue, CityColor.Cyan), $"Equip {a} + {b} through the saved loadout.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")); yield return Scene(GameFlow.CityScene);
        Check(W.Powers.IsEquipped(Power(a)) && W.Powers.IsEquipped(Power(b)), $"Session equips {a} + {b}.");
        var rt = Runtime(a); Check(W.Powers.Select(rt) && W.Powers.Selected == rt, $"{a} selected for LMB (key {W.Powers.SlotNumber(rt)}).");
    }
    void Isolate()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Roster isolated floor";
        floor.transform.position = new Vector3(0, 149.5f, 0); floor.transform.localScale = new Vector3(120, 1, 120);
        floor.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Road);
        PlaceHero(new Vector3(0, 150.05f, 0));
        Cam.GetComponent<ThirdPersonCamera>().enabled = false;
    }
    void PlaceHero(Vector3 at)
    {
        W.Hero.enabled = false; var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false;
        W.Hero.transform.position = at; W.Hero.transform.forward = Vector3.forward; cc.enabled = true; W.Hero.ResetMotion(); Physics.SyncTransforms();
    }
    /// Shoulder camera 1 m behind the hero's head, looking at the point: the viewport-centre ray (the crosshair) passes through it.
    void Aim(Vector3 point)
    {
        Vector3 head = W.Hero.transform.position + Vector3.up * 1.7f, dir = (point - head).normalized;
        Cam.transform.position = head - dir * 1f + Vector3.up * .2f; Cam.transform.LookAt(point); Physics.SyncTransforms();
    }
    CityNpc Actor(Vector3 feet, NpcRole role, float health)
    {
        var npc = CityNpc.Spawn(W, W.City.Sidewalks[0], role);
        if (npc == null) throw new Exception("Actor spawn failed");
        npc.enabled = false; npc.Agent.enabled = false; npc.transform.position = feet; npc.SetCombatStats(health, 0); Physics.SyncTransforms();
        return npc;
    }
    GameObject Wall(Vector3 centre, Vector3 size)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Roster wall CONTROL"; wall.transform.position = centre; wall.transform.localScale = size;
        wall.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Brick); Physics.SyncTransforms(); return wall;
    }
    static Vector3 Chest(CityNpc npc) => npc.transform.position + Vector3.up * 1.2f;
    void Capture(string file)
    {
        var target = new RenderTexture(1280, 720, 24); var previous = Cam.targetTexture; Cam.targetTexture = target; Cam.Render(); Cam.targetTexture = previous;
        var active = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Folder + file, image.EncodeToPNG()); RenderTexture.active = active; Destroy(image); target.Release(); Destroy(target);
        Log("IMAGE " + Folder + file);
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
        Log("---- DARKNESS (Shadow Tendrils)");
        yield return Enter("darkness", "strength");
        var rt = Runtime("darkness"); var stats = W.Powers.Stats(rt);
        PlaceHero(W.City.Spawn + Vector3.up * .05f); Cam.GetComponent<ThirdPersonCamera>().enabled = false;
        var points = NavPoints(12, 17).ToList();
        Check(points.Count >= 2, "Two clear NavMesh points 12-17 m from the hero for live criminals.");
        Vector3 pa = points[0]; Vector3 pb = points.FirstOrDefault(p => Vector3.Angle(p - W.Hero.transform.position, pa - W.Hero.transform.position) > 60);
        Check(pb != Vector3.zero, "CONTROL criminal placed >60 deg from the target's bearing.");
        var target = CityNpc.Spawn(W, pa, NpcRole.Criminal); var control = CityNpc.Spawn(W, pb, NpcRole.Criminal);
        target.SetCombatStats(1000, target.ContactDamage); control.SetCombatStats(1000, control.ContactDamage);
        yield return new WaitForSeconds(1f);
        var v = new float[2]; yield return Speeds(new[] { target, control }, .6f, v);
        Log($"MEASURED before root: target {v[0]:F2} m/s, control {v[1]:F2} m/s (live AI approaching a hero-side player).");
        Check(v[0] > .3f, "Target criminal is really moving before the root.");
        Aim(Chest(target)); int charges = rt.Charges; float hp = target.Health;
        Check(W.Powers.Use(rt), "Shadow Tendrils cast on the aimed live criminal: " + W.Powers.Message);
        Check(target.Rooted && !target.Frozen && rt.Charges == charges - 1, $"Target rooted (not frozen); charge spent {charges}->{rt.Charges}.");
        Check(Mathf.Abs(hp - target.Health - stats.Damage) < .01f, $"Root damage == data: {hp - target.Health:F2} vs {stats.Damage}.");
        yield return null;
        Check(target.GetComponent<RootedLook>() != null && target.GetComponent<RootedLook>().Showing && PowerVfx.Get().ActiveLines > 0, "Tendrils visible from the pooled line set while rooted.");
        Capture("darkness-rooted.png");
        yield return Speeds(new[] { target, control }, stats.Duration - .8f, v);
        Log($"MEASURED during root ({stats.Duration - .8f:F1} s): target {v[0]:F3} m/s, control {v[1]:F2} m/s.");
        Check(v[0] < .05f, "Rooted criminal does not move.");
        Check(v[1] > .3f && !control.Rooted, "CONTROL: the un-rooted criminal keeps moving.");
        yield return new WaitForSeconds(1.2f);
        Check(!target.Rooted, "Root expires after its Duration.");
        yield return Speeds(new[] { target }, 1f, v);
        Log($"MEASURED after root: target {v[0]:F2} m/s.");
        Check(v[0] > .3f, "Target moves again after the root ends.");
        // Root is crowd control, not a stun: a rooted enemy in reach still attacks; a frozen one does not.
        var rooted = target; var frozen = control; rooted.Root(4f);
        int rootedWindups = rooted.Windups;
        PlaceHero(rooted.transform.position + rooted.transform.forward * 1.5f);
        float until = Time.time + 3.5f; while (rooted.Windups == rootedWindups && Time.time < until) yield return null;
        Check(rooted.Windups > rootedWindups, "A rooted enemy with the player in reach still starts its attack (root is not a stun).");
        frozen.Freeze(3f); int frozenWindups = frozen.Windups;
        PlaceHero(frozen.transform.position + frozen.transform.forward * 1.5f);
        yield return new WaitForSeconds(1.5f);
        Check(frozen.Windups == frozenWindups, "CONTROL: a frozen enemy in reach starts no attack.");
        // Charges CONTROL: 2 charges, 5 s recharge; the second cast is allowed, the third refused.
        rt.Cooldown = 0; rt.Charges = 1; Aim(Chest(control)); Check(W.Powers.Use(rt), "Last charge casts.");
        rt.Cooldown = 0; hp = target.Health; Aim(Chest(target)); bool third = W.Powers.Use(rt);
        Check(!third && W.Powers.Message == "Blocked: 0 charges" && target.Health == hp, "CONTROL: zero charges refused, target untouched.");
        rt.Charges = 1; rt.Cooldown = 0; Aim(W.Hero.transform.position + Vector3.up * 60 + Vector3.forward * 5); int before = rt.Charges;
        Check(!W.Powers.Use(rt) && rt.Charges == before, "CONTROL: aiming at empty sky refuses and spends nothing.");
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
        Log($"MEASURED 1 s hold: {held:F3} s held, damage {dealt:F2} (expected ~{stats.Damage * held:F1} at {stats.Damage}/s), energy drained {drained:F2} (expected ~{d.DrainPerSecond * held:F1}), Heat +{W.Heat - heat:F3}.");
        Check(dealt > stats.Damage * (held - .15f) && dealt <= stats.Damage * (held + .02f), "Beam damage per second matches data (0.1 s tick quantisation).");
        Check(Mathf.Abs(drained - d.DrainPerSecond * held) < d.DrainPerSecond * .05f + .5f, "Energy drains at DrainPerSecond while held.");
        Check(Mathf.Abs((W.Heat - heat) - W.Tuning.Heat.AssaultHeat) < .02f, $"Heat rose by ONE assault ({W.Tuning.Heat.AssaultHeat}) across ~10 ticks, not one per tick.");
        Check(control.Health == 1000, "CONTROL: the cop beside the beam is untouched.");
        Check(PowerVfx.Get().BeamVisible, "Beam visible while held."); Capture("laser-beam.png");
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
        Check(W.Powers.Channeling == null && W.Powers.Message == "Blocked: energy", $"Channel ends by itself when energy runs out (energy {W.Powers.Energy:F2}).");
        yield return new WaitForSeconds(stats.Cooldown + .05f);
        Check(!W.Powers.Use(rt) && W.Powers.Message == "Blocked: energy", $"CONTROL: cannot start below the {d.ResourceCost} start cost.");
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
        Check(PowerVfx.Get().Draws - draws == chain.Length, "One pooled arc line per strike."); Capture("lightning-chain.png");
        yield return new WaitForSeconds(stats.Cooldown + .05f);
        float first = chain[0].Health; Aim(new Vector3(.5f, 150, 8.5f));
        Check(W.Powers.Use(rt) && LightningEffect.LastChain.Count > 0 && LightningEffect.LastChain[0] == chain[0] && chain[0].Health < first, "Bolt at the floor beside an enemy jumps to the nearest enemy.");
    }
    // ---------------------------------------------------------------- FORCE FIELD: defensive
    IEnumerator ForceField()
    {
        Log("---- FORCE FIELD (defensive)");
        yield return Enter("force-field", "strength");
        Isolate(); var rt = Runtime("force-field"); var stats = W.Powers.Stats(rt); var effect = (ForceFieldEffect)rt.Definition.Effect;
        int damagedEvents = 0; Action<bool> count = died => damagedEvents++; W.PlayerDamaged += count;
        float health = W.Health;
        Check(W.Powers.Use(rt) && W.Powers.Shield != null && W.Powers.Shield.Up && W.Powers.Shield.RingsVisible, $"Force Field raised: {effect.Capacity} absorb for {stats.Duration} s.");
        Capture("force-field.png");
        W.DamagePlayer(25);
        Check(W.Health == health && Mathf.Abs(W.Powers.Shield.Remaining - (effect.Capacity - 25)) < .01f && damagedEvents == 0, $"25 damage fully absorbed: health {W.Health}, field {W.Powers.Shield.Remaining}, 0 damage events.");
        W.DamagePlayer(50);
        float through = 50 - (effect.Capacity - 25);
        Check(Mathf.Abs(W.Health - (health - through)) < .01f && !W.Powers.Shield.Up && W.Powers.Shield.LastEnd == "broken" && !W.Powers.Shield.RingsVisible && damagedEvents == 1,
            $"50 damage: field absorbs its last {effect.Capacity - 25}, {through} reaches health ({W.Health}); field broken, rings hidden.");
        health = W.Health; W.DamagePlayer(10);
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
        yield return Enter("speed", "strength");
        Isolate(); var rt = Runtime("speed"); var stats = W.Powers.Stats(rt);
        var inPath = Actor(new Vector3(0, 150, 4.5f), NpcRole.Criminal, 1000);
        var civilian = Actor(new Vector3(.4f, 150, 6.5f), NpcRole.Civilian, 1000);
        var aside = Actor(new Vector3(3, 150, 4.5f), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        Aim(W.Hero.transform.position + new Vector3(0, 1.5f, 20)); Vector3 start = W.Hero.transform.position;
        Check(W.Powers.Use(rt) && W.Hero.Dashing, "Dash starts: " + W.Powers.Message);
        yield return new WaitForSeconds(stats.Duration + .3f);
        Vector3 moved = W.Hero.transform.position - start; float flat = new Vector2(moved.x, moved.z).magnitude;
        Log($"MEASURED dash: {flat:F2} m horizontal (data {stats.Range} m in {stats.Duration} s), {moved.y:F2} m vertical, direction z {moved.z:F2}.");
        Check(!W.Hero.Dashing && Mathf.Abs(flat - stats.Range) < .6f && moved.z > stats.Range - .6f, "Dash covers its Range along the aim, through the enemy in its path.");
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
        var p = Actor(new Vector3(-6, 150, 10), NpcRole.Criminal, 1000); var cop = Actor(new Vector3(-10, 150, 10), NpcRole.Cop, 1000);
        yield return new WaitForSeconds(.3f);
        Aim(Chest(p)); Check(W.Powers.Use(rt), "Poison the criminal.");
        rt.Cooldown = 0; float heat = W.Heat; Aim(Chest(cop)); Check(W.Powers.Use(rt), "Poison the cop.");
        yield return new WaitForSeconds(stats.Duration + .6f);
        float expected = stats.Damage * stats.Duration;
        Check(Mathf.Abs(1000 - p.Health - expected) < .01f && !p.GetComponent<Poisoned>().Active, $"Total poison damage == {stats.Damage}/s x {stats.Duration} s = {expected} ({1000 - p.Health:F2}); then it stops.");
        Check(Mathf.Abs((W.Heat - heat) - W.Tuning.Heat.AssaultHeat) < .02f, $"Poisoning a cop adds ONE assault of Heat ({W.Heat - heat:F3}) over {cop.GetComponent<Poisoned>().Ticks} ticks.");
        // Spread on death.
        var s = Actor(new Vector3(0, 150, 10), NpcRole.Criminal, 10);
        var n1 = Actor(new Vector3(2, 150, 10), NpcRole.Criminal, 1000); var n2 = Actor(new Vector3(0, 150, 12.5f), NpcRole.Criminal, 1000);
        var n3 = Actor(new Vector3(-3, 150, 10.5f), NpcRole.Criminal, 1000); var fourth = Actor(new Vector3(4, 150, 13), NpcRole.Criminal, 1000);
        var civ = Actor(new Vector3(1, 150, 8), NpcRole.Civilian, 1000); var outside = Actor(new Vector3(0, 150, 17.5f), NpcRole.Criminal, 1000);
        int spreads = 0; Action<CityNpc, CityNpc> onSpread = (a, b) => spreads++; Poisoned.Spreading += onSpread;
        rt.Charges = Mathf.Max(rt.Charges, 1); rt.Cooldown = 0; Aim(Chest(s)); Check(W.Powers.Use(rt), "Poison a 10 HP criminal with neighbours.");
        float until = Time.time + 3; while (!s.Dead && Time.time < until) yield return null;
        Check(s.Dead, "The poisoned criminal dies from the poison ticks.");
        bool On(CityNpc n) { var q = n.GetComponent<Poisoned>(); return q != null && q.Active; }
        Check(On(n1) && On(n2) && On(n3) && spreads == 3, $"Poison jumped to the three nearest enemies (events {spreads}).");
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
