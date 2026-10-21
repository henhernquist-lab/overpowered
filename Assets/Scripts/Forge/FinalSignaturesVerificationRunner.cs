#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FinalSignaturesVerificationRunner : MonoBehaviour
{
    public bool Reload;
    public Action<int> Finished;
    readonly List<string> output = new List<string>();
    WorldSession W => WorldSession.Instance;
    SignatureCatalog S => Resources.Load<SignatureCatalog>("SignatureCatalog");
    ForgeCatalog F => Resources.Load<ForgeCatalog>("ForgeCatalog");
    PowerDefinition P(string id) => Resources.Load<PowerDefinition>("Powers/" + id);
    void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); output.Add("PASS " + msg); Debug.Log("PASS " + msg); }
    IEnumerator Start()
    {
        var stack = new Stack<IEnumerator>(); stack.Push(Reload ? ReloadChecks() : Checks());
        while (stack.Count > 0)
        {
            bool moved = false; object next = null;
            try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception e) { output.Add("FAIL " + e); Debug.LogError("FAIL " + e); Write(); Finished(1); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested); else yield return next;
        }
        Write(); Finished(0);
    }
    void Write() => File.WriteAllLines("Verification/FinalSignatures/" + (Reload ? "reload.txt" : "results.txt"), output);
    IEnumerator Scene(string name)
    {
        float until = Time.realtimeSinceStartup + 45;
        while (GameFlow.Instance == null || GameFlow.Instance.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        { if (Time.realtimeSinceStartup > until) throw new Exception("Scene timeout " + name); yield return null; }
        yield return new WaitForSecondsRealtime(.4f);
    }
    void Isolate()
    {
        foreach (var npc in W.Npcs.ToArray()) if (npc != null) Destroy(npc.gameObject);
        W.Npcs.Clear();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Final signature floor";
        floor.transform.position = new Vector3(0, 149.5f, 0); floor.transform.localScale = new Vector3(100, 1, 100);
        floor.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Road);
        var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false;
        W.Hero.transform.position = new Vector3(0, 150.05f, 0); cc.enabled = true; W.Hero.ResetMotion();
        var rig = Camera.main.GetComponent<ThirdPersonCamera>(); rig.enabled = false;
        Aim(new Vector3(0, 150, 10)); Physics.SyncTransforms();
    }
    void Aim(Vector3 point)
    {
        Camera.main.transform.position = W.Hero.transform.position + Vector3.up * 1.7f - Vector3.forward;
        Camera.main.transform.LookAt(point); W.Hero.transform.forward = Vector3.forward; Physics.SyncTransforms();
    }
    CityNpc Spawn(float x, float z, string kind = "Gunner", NpcRole role = NpcRole.Criminal, float hp = 1000)
    {
        var npc = CityNpc.Spawn(W, W.City.Sidewalks[0], role, Resources.Load<EnemyArchetype>("Enemies/" + kind), hp, 0);
        npc.enabled = false; npc.Agent.enabled = false; npc.transform.position = new Vector3(x, 150.05f, z); npc.SetCombatStats(hp, 0);
        Physics.SyncTransforms(); return npc;
    }
    IEnumerator Equip(string owner, string id, string mode = "hero")
    {
        var profile = FindAnyObjectByType<ModeScreens>().Profile;
        Check(profile.SetLoadout(F.Hero("vector"), P(owner), P(owner == "fire" ? "ice" : "fire"), P("speed"), CityColor.Blue, CityColor.Cyan), "Loadout " + owner);
        Check(profile.SetSignature(id), "Select " + id);
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/" + mode)), "Enter " + mode);
        yield return Scene(GameFlow.CityScene); Isolate(); yield return null;
        Check(W.Powers.Synergy.Id == id, "Session caches " + id);
    }
    IEnumerator Complete(SynergyRunner r)
    {
        float until = Time.time + 15;
        while (r.Busy && Time.time < until) yield return null;
        Check(!r.Busy, "Effect completes within bounded lifetime");
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene); SignatureCatalog.VerificationPreview = true;
        yield return Equip("telekinesis", "meteor-crush");
        var direct = Spawn(0, 10, "Brute"); var shock = Spawn(4, 10); var outside = Spawn(10, 10);
        var safe = Spawn(-2, 10, "Gunner", NpcRole.Civilian);
        Aim(new Vector3(0, 150, 10)); var meteor = W.Powers.SynergyRunner;
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube); roof.transform.position = new Vector3(0, 158, 10); roof.transform.localScale = new Vector3(8, .5f, 8); Physics.SyncTransforms();
        Check(!meteor.TryActivate() && meteor.Cooldown == 0, "Meteor refuses obstructed overhead space without cooldown");
        Destroy(roof); yield return null;
        Check(meteor.TryActivate(), "Meteor assembles without throwable props");
        Check(direct.Health == 1000 && SignatureVfx.Get().MeteorVisible, "Meteor anticipation precedes damage");
        yield return Complete(meteor);
        Check(Mathf.Abs(1000 - direct.Health - meteor.ActivationDamage) < .1f && Mathf.Abs(1000 - shock.Health - meteor.ActivationDamage * .28f) < .1f,
            "Meteor direct 210 / shock 58.8 authoritative damage, no double hit");
        Check(outside.Health == 1000 && safe.Health == 1000 && !SignatureVfx.Get().MeteorVisible, "Meteor range/friendly/cleanup controls");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        yield return Equip("darkness", "abyssal-grasp");
        var rusher = Spawn(-5, 10, "Rusher"); var brute = Spawn(5, 10, "Brute"); safe = Spawn(0, 12, "Gunner", NpcRole.Civilian);
        Aim(new Vector3(0, 150, 10)); var abyss = W.Powers.SynergyRunner;
        Vector3 rusherStart = rusher.transform.position, bruteStart = brute.transform.position;
        Check(abyss.TryActivate(), "Abyss activates on ground group");
        yield return new WaitForSeconds(.7f);
        Check(Vector3.Distance(rusherStart, rusher.transform.position) > Vector3.Distance(bruteStart, brute.transform.position) + .5f,
            "Abyss groups with Brute pull resistance");
        yield return Complete(abyss); yield return new WaitForSeconds(.25f);
        Check(Mathf.Abs(1000 - rusher.Health - abyss.ActivationDamage) < .1f && Mathf.Abs(1000 - brute.Health - abyss.ActivationDamage) < .1f,
            "Abyss collapse authoritative damage once per hostile");
        Check(!rusher.Frozen && !brute.Frozen && !rusher.Rooted && safe.Health == 1000 && !SignatureVfx.Get().AbyssVisible,
            "Abyss movement leases expire, no curse or friendly damage remains");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        yield return Equip("laser-eyes", "prism-barrage");
        var first = Spawn(0, 10); var branch = Spawn(3, 11); var duplicate = first.gameObject.AddComponent<SphereCollider>(); duplicate.radius = .2f;
        var civilian = Spawn(-2, 10, "Gunner", NpcRole.Civilian); var far = Spawn(20, 10);
        Aim(first.transform.position + Vector3.up);
        var r = W.Powers.SynergyRunner; float energy = W.Powers.Energy;
        Check(r.TryActivate(), "Prism activates on precise hostile target");
        Check(Mathf.Abs(energy - W.Powers.Energy - r.EnergyCost) < .1f && !r.TryActivate(), "Energy paid once; duplicate activation refused");
        yield return Complete(r);
        Check(Mathf.Abs(1000 - first.Health - r.ActivationDamage) < .05f, "Prism initial authoritative damage");
        Check(Mathf.Abs(1000 - branch.Health - r.ActivationDamage * .78f) < .05f, "Prism branch falloff authoritative damage");
        Check(PrismBarrageEffect.LastTargets.Count == 2 && PrismBarrageEffect.LastTargets.Distinct().Count() == 2, "Prism deduplicates colliders and bounds branch fan");
        Check(civilian.Health == 1000 && far.Health == 1000 && !SignatureVfx.Get().PrismVisible && !r.TryActivate(), "Prism friendly/range/cleanup/cooldown controls");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        yield return Equip("poison", "plague-bloom");
        var target = Spawn(0, 10); civilian = Spawn(2, 10, "Gunner", NpcRole.Civilian); far = Spawn(15, 10);
        Aim(new Vector3(0, 150, 10)); r = W.Powers.SynergyRunner;
        Check(r.TryActivate(), "Plague activates on ground without requiring physical props");
        yield return Complete(r); yield return new WaitForSeconds(1.1f);
        Check(1000 - target.Health > r.ActivationDamage * .9f && 1000 - target.Health <= r.ActivationDamage + .1f,
            "Plague sustained authoritative poison budget: " + (1000 - target.Health));
        Check(civilian.Health == 1000 && far.Health == 1000 && SignatureVfx.Get().ActiveBlooms == 0, "Plague faction/range/finite VFX controls");
        Check(target.GetComponents<Poisoned>().Length == 1 && !target.GetComponent<Poisoned>().Active, "One poison component, finite ticks");
        File.WriteAllText("Verification/FinalSignatures/saves/reload-path.txt", WorldSession.VerificationSavePath);
        W.Progression.Save(); Check(W.Progression.LastError == null, "Save equipped final signature");
    }
    IEnumerator ReloadChecks()
    {
        yield return Scene(GameFlow.HomeScene); SignatureCatalog.VerificationPreview = true;
        var profile = FindAnyObjectByType<ModeScreens>().Profile;
        Check(profile.EquippedSignature?.Id == "plague-bloom", "Separate process restores signature selection");
        Check(profile.EquippedA == P("poison") && profile.EquippedSecondary == P("speed"), "Separate process preserves primary and secondary");
    }
}
#endif
