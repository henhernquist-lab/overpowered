#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.Profiling;

public sealed class FinalSignaturesVerificationRunner : MonoBehaviour
{
    public bool Reload, Matrix, Safety, PrismOnly;
    string runtimeError;
    void Awake() { Application.logMessageReceived += Observe; }
    void OnDestroy() { Application.logMessageReceived -= Observe; }
    void Observe(string message, string trace, LogType type) { if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && trace.Contains("Assets/Scripts/")) runtimeError = message; }
    public Action<int> Finished;
    readonly List<string> output = new List<string>();
    WorldSession W => WorldSession.Instance;
    SignatureCatalog S => Resources.Load<SignatureCatalog>("SignatureCatalog");
    ForgeCatalog F => Resources.Load<ForgeCatalog>("ForgeCatalog");
    PowerDefinition P(string id) => Resources.Load<PowerDefinition>("Powers/" + id);
    void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); output.Add("PASS " + msg); Debug.Log("PASS " + msg); }
    IEnumerator Start()
    {
        var stack = new Stack<IEnumerator>(); stack.Push(Safety ? SafetyChecks() : Matrix ? MatrixChecks() : Reload ? ReloadChecks() : Checks());
        while (stack.Count > 0)
        {
            bool moved = false; object next = null;
            try { if (runtimeError != null) throw new Exception("Gameplay error: " + runtimeError); moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception e) { output.Add("FAIL " + e); Debug.LogError("FAIL " + e); Write(); Finished(1); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested); else yield return next;
        }
        Write(); Finished(0);
    }
    void Write() => File.WriteAllLines("Verification/FinalSignatures/" + (Safety ? "safety.txt" : Matrix ? (PrismOnly ? "prism-matrix.txt" : "matrix.txt") : Reload ? "reload.txt" : "results.txt"), output);
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
        yield return Scene(GameFlow.HomeScene);
        Check(S.Enabled && S.EnableWhenComplete && !S.DeveloperPreview && !SignatureCatalog.VerificationPreview, "Authored full roster enabled without preview");
        Check(S.Complete && S.Abilities.Length == 8 && S.Abilities.All(a => S.Implemented(a) && !string.IsNullOrEmpty(a.Description)), "Complete roster has eight valid described mappings");
        var profile = FindAnyObjectByType<ModeScreens>().Profile;
        var powers = Resources.LoadAll<PowerDefinition>("Powers").Where(p => p.Role == PowerRole.Combat).OrderBy(p => p.Id).ToArray();
        int pairs = 0;
        for (int a = 0; a < powers.Length; a++) for (int b = a + 1; b < powers.Length; b++)
        {
            var choices = S.Choices(powers[a], powers[b]);
            Check(choices.Count == 2 && choices.All(d => S.Eligible(d, powers[a], powers[b])) && choices[0] != choices[1], "Pair choices " + powers[a].Id + "/" + powers[b].Id);
            Check(profile.SetLoadout(F.Hero("vector"), powers[a], powers[b], P("speed"), CityColor.Blue, CityColor.Cyan) &&
                profile.SetSignature(choices[0].Id) && profile.EquippedSignature == choices[0] && profile.SetSignature(choices[1].Id) && profile.EquippedSignature == choices[1], "One selectable signature for pair " + ++pairs);
        }
        Check(pairs == 28 && !profile.SetSignature("invalid-signature") && S.Choices(P("fire"), P("fire")).Count == 0, "28 unique pairs, invalid ID and duplicate-power controls");
        var menu = FindAnyObjectByType<ModeScreens>();
        using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = menu.ForgeButton; menu.ForgeButton.SendEvent(submit); }
        yield return null;
        var screen = menu.ForgeScreen;
        foreach (var pair in new[] { new[] { "telekinesis", "poison" }, new[] { "darkness", "laser-eyes" } })
        {
            Check(screen.SelectHero(F.Hero("vector")) && screen.SelectPower(0, P(pair[0])) && screen.SelectPower(1, P(pair[1])), "Forge new primary pair " + string.Join("/", pair));
            foreach (var option in S.Choices(P(pair[0]), P(pair[1])))
            {
                var button = screen.Root.Q<Button>("signature-" + option.Id);
                Check(button != null && button.text.Contains(option.Description) && button.text.Contains(option.EnergyCost.ToString("0") + " energy") && button.text.Contains(option.Cooldown.ToString("0") + "s cooldown"), "Forge description/cost/cooldown " + option.Id);
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
                Check(profile.EquippedSignature == option && screen.Root.Query<Button>().ToList().Count(b => b.name != null && b.name.StartsWith("signature-") && b.text.Contains("SELECTED")) == 1, "Forge equips exactly one " + option.Id);
            }
        }
        screen.Close();
        foreach (string stale in new[] { "bogus-saved-signature", "" })
        {
            string path = Path.GetFullPath("Verification/FinalSignatures/saves/compat-" + Guid.NewGuid().ToString("N") + ".json");
            string signature = stale.Length == 0 ? "" : ",\"SignatureId\":\"" + stale + "\"";
            File.WriteAllText(path, "{\"Version\":1,\"Level\":4,\"Xp\":25,\"Points\":3,\"FirstPerson\":true,\"Powers\":[{\"Id\":\"fire\",\"Tier\":1}],\"Loadout\":{\"HeroId\":\"vector\",\"PowerA\":\"telekinesis\",\"PowerB\":\"poison\",\"SecondaryPower\":\"speed\",\"Primary\":14,\"Secondary\":15" + signature + "}}");
            var probe = new GameObject("Saved signature compatibility control").AddComponent<PlayerProgression>();
            probe.Initialize(Resources.Load<GameTuning>("GameTuning").Progression, Resources.LoadAll<PowerDefinition>("Powers"), path);
            Check(probe.LastError == null && probe.EquippedSignature?.Id == "meteor-crush" && (stale.Length == 0 || probe.Data.Loadout.UnresolvedSignatureId == stale), "Invalid/missing saved signature repairs safely and retains stale ID: " + stale);
            Check(probe.Data.Level == 4 && probe.Data.Xp == 25 && probe.Data.Points == 3 && probe.Tier(P("fire")) == 1 && probe.SelectedHero.Id == "vector" && probe.Data.FirstPerson && probe.Data.Loadout.Primary == CityColor.Blue && probe.Data.Loadout.Secondary == CityColor.Cyan && probe.EquippedSecondary == P("speed"), "Signature repair preserves XP/level/points/upgrades/hero/suit/view/secondary");
            Destroy(probe.gameObject);
        }
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
        Spawn(-4, 10); Spawn(4, 13); Spawn(-5, 12); var capped = Spawn(6, 13);
        Aim(first.transform.position + Vector3.up);
        var r = W.Powers.SynergyRunner; float energy = W.Powers.Energy;
        Check(r.TryActivate(), "Prism activates on precise hostile target");
        Check(Mathf.Abs(energy - W.Powers.Energy - r.EnergyCost) < .1f && !r.TryActivate(), "Energy paid once; duplicate activation refused");
        yield return Complete(r);
        Check(Mathf.Abs(1000 - first.Health - r.ActivationDamage) < .05f, "Prism initial authoritative damage");
        Check(Mathf.Abs(1000 - branch.Health - r.ActivationDamage * .78f) < .05f, "Prism branch falloff authoritative damage");
        Check(PrismBarrageEffect.LastTargets.Count == 5 && PrismBarrageEffect.LastTargets.Distinct().Count() == 5 && capped.Health == 1000, "Prism deduplicates colliders and enforces five-target cap with an eligible sixth untouched");
        Check(civilian.Health == 1000 && far.Health == 1000 && !SignatureVfx.Get().PrismVisible && !r.TryActivate(), "Prism friendly/range/cleanup/cooldown controls");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        yield return Equip("poison", "plague-bloom");
        var target = Spawn(0, 10); civilian = Spawn(2, 10, "Gunner", NpcRole.Civilian); far = Spawn(15, 10);
        Aim(new Vector3(0, 150, 10)); r = W.Powers.SynergyRunner;
        int rollSamples = W.Rolls.DamageSamples;
        Check(r.TryActivate(), "Plague activates on ground without requiring physical props");
        yield return Complete(r); yield return new WaitForSeconds(1.1f);
        Check(1000 - target.Health > r.ActivationDamage * .9f && 1000 - target.Health <= r.ActivationDamage + .1f,
            "Plague sustained authoritative poison budget: " + (1000 - target.Health));
        Check(civilian.Health == 1000 && far.Health == 1000 && SignatureVfx.Get().ActiveBlooms == 0, "Plague faction/range/finite VFX controls");
        Check(W.Rolls.DamageSamples == rollSamples + 1, "Bloom rolls signature variance once, never per application or tick");
        Check(target.GetComponents<Poisoned>().Length == 1 && !target.GetComponent<Poisoned>().Active, "One poison component, finite ticks");
        File.WriteAllText("Verification/FinalSignatures/saves/reload-path.txt", WorldSession.VerificationSavePath);
        W.Progression.Save(); Check(W.Progression.LastError == null, "Save equipped final signature");
    }
    IEnumerator MatrixChecks()
    {
        yield return Scene(GameFlow.HomeScene);
        Check(S.Enabled && !SignatureCatalog.VerificationPreview, "Matrix uses default roster gate");
        string[] owners = {"telekinesis", "darkness", "laser-eyes", "poison"};
        string[] ids = {"meteor-crush", "abyssal-grasp", "prism-barrage", "plague-bloom"};
        string[] modes = {"hero", "villain", "free-play", "endless-fight"};
        foreach (string difficulty in new[] {"normal", "hard", "extreme"})
        for (int ability = PrismOnly ? 2 : 0; ability < (PrismOnly ? 3 : 4); ability++)
        for (int mode = 0; mode < modes.Length; mode++)
        {
            var profile = FindAnyObjectByType<ModeScreens>().Profile;
            Check(profile.SetDifficulty(difficulty), "Select difficulty " + difficulty);
            if (PrismOnly)
            {
                Check(profile.SetLoadout(F.Hero("vector"), P("laser-eyes"), P("ice"), P("speed"), CityColor.Blue, CityColor.Cyan) && profile.SetSignature("prism-barrage"), "Prepare Prism Forge difficulty display");
                var menu = FindAnyObjectByType<ModeScreens>();
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = menu.ForgeButton; menu.ForgeButton.SendEvent(submit); }
                yield return null;
                string expectedCooldown = difficulty == "hard" ? "26.4" : difficulty == "extreme" ? "27.6" : "24";
                string expectedEnergy = difficulty == "hard" ? "33" : difficulty == "extreme" ? "34.5" : "30";
                var button = menu.ForgeScreen.Root.Q<Button>("signature-prism-barrage");
                Check(button != null && button.text.Contains(expectedCooldown + "s cooldown") && button.text.Contains(expectedEnergy + " energy") && menu.ForgeScreen.SynergyStatus.text.Contains(expectedEnergy + " ENERGY"),
                    "Forge displays actual " + difficulty + " cooldown and energy requirements");
                string iceCooldown = difficulty == "hard" ? "27.5" : difficulty == "extreme" ? "28.75" : "25";
                var iceButton = menu.ForgeScreen.Root.Q<Button>("signature-glacial-prison");
                Check(iceButton != null && iceButton.text.Contains(iceCooldown + "s cooldown") && iceButton.text.Contains(expectedEnergy + " energy"), "Forge preserves fractional first-four cooldown requirement");
                menu.ForgeScreen.Close();
            }
            yield return Equip(owners[ability], ids[ability], modes[mode]);
            bool villain = W.Progression.Data.Side == PlayerSide.Villain;
            var enemy = Spawn(0, 10, "Brute", villain ? NpcRole.Cop : NpcRole.Criminal);
            enemy.AlwaysAggro = true; // Villain cop fixture represents an active responder, not a neutral zero-Heat patrol.
            CityNpc opticalVisible = null, opticalHidden = null; Rigidbody opticalCover = null;
            if (ability == 2)
            {
                opticalVisible = Spawn(-3, 11, "Gunner", villain ? NpcRole.Cop : NpcRole.Criminal); opticalVisible.AlwaysAggro = true;
                opticalHidden = Spawn(4, 10, "Gunner", villain ? NpcRole.Cop : NpcRole.Criminal); opticalHidden.AlwaysAggro = true;
                var cover = Wall(new Vector3(2, 151.5f, 10), new Vector3(.4f, 3, 5)); opticalCover = cover.AddComponent<Rigidbody>(); opticalCover.useGravity = false; opticalCover.mass = 45;
            }
            var friendly = Spawn(3, 10, "Gunner", villain ? NpcRole.Criminal : NpcRole.Cop);
            var civilian = Spawn(-3, 10, "Gunner", NpcRole.Civilian);
            bool firstPerson = mode % 2 == 1;
            Vector3 aim = new Vector3(0, ability == 2 ? 151.1f : 150f, 10);
            if (firstPerson)
            {
                var rig = Camera.main.GetComponent<ThirdPersonCamera>(); rig.enabled = true;
                if (!rig.FirstPerson) Check(rig.ToggleView(), "Enable first-person camera");
                Vector3 eye = W.Hero.transform.position + Vector3.up * W.Tuning.Camera.EyeHeight;
                rig.SetLook(0, Mathf.Atan2(eye.y - aim.y, aim.z - eye.z) * Mathf.Rad2Deg);
                yield return null; yield return null;
                Check(Vector3.Distance(W.Powers.AimOrigin, Camera.main.transform.position) < .01f, "True first-person aim origin");
            }
            else
            {
                var rig = Camera.main.GetComponent<ThirdPersonCamera>(); rig.enabled = true;
                if (rig.FirstPerson) Check(rig.ToggleView() && !rig.FirstPerson, "Restore actual third-person view");
                rig.enabled = false; Aim(aim);
            }
            var r = W.Powers.SynergyRunner;
            Check(Mathf.Abs(r.EnergyCost - r.Definition.EnergyCost * W.Powers.Difficulty.EnergyUsage) < .01f, "Difficulty energy once");
            Check(r.TryActivate(), ids[ability] + " activates " + difficulty + "/" + modes[mode] + "/" + (firstPerson ? "first" : "third"));
            Check(Mathf.Abs(r.Cooldown - r.Definition.Cooldown * W.Powers.Difficulty.Cooldown) < .1f, "Difficulty cooldown once");
            yield return Complete(r); yield return new WaitForSeconds(1.1f);
            float damage = 1000 - enemy.Health;
            float expected = r.ActivationDamage * W.Powers.Difficulty.PlayerDamage;
            Check(damage > expected * .89f && damage <= expected + .2f, "Authoritative difficulty damage " + damage + " expected " + expected);
            if (ability == 2) Check(opticalVisible.Health < 1000 && opticalHidden.Health == 1000 && !opticalCover.isKinematic && !opticalCover.gameObject.isStatic,
                "Optical branches stop at movable cover and preserve dynamic props");
            Check(friendly.Health == 1000 && civilian.Health == 1000, "Hero/Villain hostility protects friendly and civilian");
            Check(!enemy.Frozen && !enemy.Rooted && !SignatureVfx.Get().MeteorVisible && !SignatureVfx.Get().AbyssVisible && SignatureVfx.Get().ActiveBlooms == 0,
                "Finite effect and movement recovery in " + modes[mode]);
            Check(!r.TryActivate(), "Cooldown remains enforced after effect");
            GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        }
    }
    GameObject Wall(Vector3 center, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Signature obstruction control"; go.transform.position = center; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Brick); Physics.SyncTransforms(); return go;
    }
    void Capture(string name)
    {
        var camera = Camera.main; var old = camera.targetTexture; var active = RenderTexture.active;
        var rt = new RenderTexture(960, 540, 24); var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply();
        File.WriteAllBytes("Verification/FinalSignatures/" + name + ".png", texture.EncodeToPNG());
        camera.targetTexture = old; RenderTexture.active = active; rt.Release(); Destroy(rt); Destroy(texture);
    }
    IEnumerator SafetyChecks()
    {
        yield return Scene(GameFlow.HomeScene);
        Check(S.Enabled && S.EnableWhenComplete && !S.DeveloperPreview && !SignatureCatalog.VerificationPreview, "Authored full roster enabled without preview");
        yield return Equip("laser-eyes", "prism-barrage");
        var first = Spawn(0, 10); var blocked = Spawn(4, 10); var far = Spawn(20, 10); Spawn(-3, 11); Spawn(-4, 8);
        var wall = Wall(new Vector3(2, 151.5f, 10), new Vector3(.4f, 3, 5));
        Aim(first.transform.position + Vector3.up); var r = W.Powers.SynergyRunner;
        float beforeRefusal = W.Powers.Energy;
        var owned = W.Progression.Data.Powers.Find(p => p.Id == "laser-eyes"); W.Progression.Data.Powers.Remove(owned);
        Check(!r.TryActivate() && r.Cooldown == 0 && W.Powers.Energy == beforeRefusal, "Unowned signature refuses without resource cost");
        W.Progression.Data.Powers.Add(owned);
        W.Powers.TrySpendEnergy(W.Powers.Energy - 1);
        Check(!r.TryActivate() && r.ReadyState == "INSUFFICIENT ENERGY" && r.Cooldown == 0, "New signature insufficient-energy control");
        W.Powers.Tick(100f, true);
        Check(r.TryActivate(), "Prism wall control activates initial target");
        yield return new WaitForSeconds(.42f); Camera.main.transform.position = new Vector3(-7, 154, -3); Camera.main.transform.LookAt(new Vector3(0, 151, 8)); Capture("prism-barrage");
        yield return Complete(r);
        Check(first.Health < 1000 && blocked.Health == 1000 && far.Health == 1000, "Prism refraction blocked by solid wall");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        yield return Equip("laser-eyes", "prism-barrage");
        first = Spawn(0, 10); var next = Spawn(3, 10); Aim(first.transform.position + Vector3.up); r = W.Powers.SynergyRunner;
        Check(r.TryActivate(), "Prism startup-death control starts"); first.Damage(2000, null);
        yield return Complete(r);
        Check(next.Health == 1000 && !SignatureVfx.Get().PrismVisible, "Dead initial target cannot silently redirect Prism");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        yield return Equip("telekinesis", "meteor-crush");
        first = Spawn(0, 10); next = Spawn(1, 10); blocked = Spawn(4, 10);
        wall = Wall(new Vector3(2.5f, 151.5f, 10), new Vector3(.4f, 3, 5));
        Aim(new Vector3(0, 150, 10)); r = W.Powers.SynergyRunner;
        Check(r.TryActivate(), "Meteor startup-death and wall controls start"); first.Damage(2000, null);
        yield return new WaitForSeconds(.65f);
        Camera.main.transform.position = new Vector3(-9, 157, -4); Camera.main.transform.LookAt(new Vector3(0, 154, 10)); Capture("meteor-crush");
        yield return Complete(r);
        Check(next.Health < 1000 && blocked.Health == 1000 && !SignatureVfx.Get().MeteorVisible, "Meteor survives startup death and respects impact obstacle");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        yield return Equip("darkness", "abyssal-grasp");
        first = Spawn(-4, 10, "Rusher"); blocked = Spawn(4, 10); var doomed = Spawn(-2, 12); Spawn(-3, 14);
        wall = Wall(new Vector3(2, 151.5f, 10), new Vector3(.4f, 3, 7));
        Vector3 blockedStart = blocked.transform.position; Aim(new Vector3(0, 150, 10)); r = W.Powers.SynergyRunner;
        Check(r.TryActivate(), "Abyss wall/cancel control starts");
        yield return new WaitForSeconds(.6f); Capture("abyssal-grasp"); doomed.Damage(2000, null); r.Cancel();
        yield return new WaitForSeconds(.25f);
        Check(blocked.transform.position == blockedStart && blocked.Health == 1000 && !first.Frozen && !SignatureVfx.Get().AbyssVisible,
            "Abyss never pulls through wall and cancel expires restraint");
        // Move helper also refuses an actual capsule-width obstruction between valid ground points.
        Check(!SignatureTargeting.MoveOnGround(blocked, new Vector3(0, 150.05f, 10), W.Hero.transform), "Abyss displacement refuses solid crossing");
        GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene);
        yield return Equip("poison", "plague-bloom");
        var victims = new List<CityNpc>(); for (int i = 0; i < 6; i++) victims.Add(Spawn(i - 2, 10));
        var civilian = Spawn(0, 12, "Gunner", NpcRole.Civilian);
        Aim(new Vector3(0, 150, 10)); r = W.Powers.SynergyRunner;
        Check(r.TryActivate(), "Bloom propagation/cap control starts");
        yield return new WaitForSeconds(.15f); Capture("plague-bloom");
        foreach (var victim in victims) victim.Damage(2000, null);
        Check(PlagueBloomEffect.LastBloomCount == 4, "Main plus secondary blooms capped at four for six exposed deaths");
        yield return null; r.Cancel(); yield return new WaitForSeconds(2);
        Check(SignatureVfx.Get().ActiveBlooms == 0 && civilian.Health == 1000 && !r.Busy, "Bloom cancel cleans event-driven secondary effects and protects civilian");
        var navNpc = CityNpc.Spawn(W, W.City.Sidewalks[0], NpcRole.Criminal, Resources.Load<EnemyArchetype>("Enemies/Rusher"), 1000, 0);
        navNpc.enabled = false;
        Check(navNpc.Agent.enabled && navNpc.Agent.isOnNavMesh, "Real city actor starts on NavMesh");
        Vector3 navStart = navNpc.transform.position; bool movedOnNav = false;
        foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            if (SignatureTargeting.MoveOnGround(navNpc, navStart + direction * .25f, W.Hero.transform)) { movedOnNav = true; break; }
        Check(movedOnNav && navNpc.Agent.enabled && navNpc.Agent.isOnNavMesh, "Abyss real-agent movement preserves navigation ownership");
        Check(!SignatureTargeting.MoveOnGround(navNpc, navStart + Vector3.up * 20, W.Hero.transform), "Abyss refuses inaccessible navigation height");
        float? originalRoll = W.Rolls.FixedUnit; W.Rolls.FixedUnit = 1;
        int samples = W.Rolls.DamageSamples; float hpBeforePoison = navNpc.Health;
        var poison = Resources.Load<PoisonEffect>("Effects/Poison");
        var applied = Poisoned.Apply(navNpc, W.Powers, poison, 20, 1, poison.MaxGenerations, CityColor.Leaf);
        W.Rolls.FixedUnit = 0;
        yield return new WaitForSeconds(1.1f);
        Check(Mathf.Abs(hpBeforePoison - navNpc.Health - 22) < .05f && W.Rolls.DamageSamples == samples + 1 && applied.Ticks == 2,
            "Ordinary poison rolls once on application; changing RNG afterward never rerolls its ticks");
        W.Rolls.FixedUnit = originalRoll;
        // Pool stability: repeat presentations after warmup, independently of gameplay/verification screenshot allocations.
        var fx = SignatureVfx.Get(); fx.HideAll();
        int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length, materials = Resources.FindObjectsOfTypeAll<Material>().Length;
        int objects = fx.GetComponentsInChildren<Transform>(true).Length;
        for (int cast = 0; cast < 20; cast++)
        {
            fx.ShowMeteor(Vector3.zero, 8, 1, .5f, -1, 6); fx.ShowAbyss(Vector3.zero, 7, .5f); fx.ShowTendril(0, Vector3.zero, Vector3.forward * 5, 1);
            fx.ShowPrism(0, Vector3.up, Vector3.forward * 10, true, .3f);
            for (int b = 0; b < 4; b++) fx.ShowBloom(b, Vector3.zero, 6, .5f, .5f);
            fx.HideAll();
        }
        Check(meshes == Resources.FindObjectsOfTypeAll<Mesh>().Length && materials == Resources.FindObjectsOfTypeAll<Material>().Length && objects == fx.GetComponentsInChildren<Transform>(true).Length,
            "20 repeated presentations preserve mesh/material/VFX-object counts");
        Debug.Log("MEASURED fixed signature pool transforms=" + objects + " total meshes=" + meshes + " total materials=" + materials);
        // Matched idle/active fixed-pool sample, whole-frame Editor cost; no standalone claim.
        using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
        for (int phase = 0; phase < 2; phase++)
        {
            var timings = new List<float>(180); long bytes = 0;
            for (int frame = 0; frame < 180; frame++)
            {
                if (phase == 1) { fx.ShowAbyss(new Vector3(0, 150, 10), 7, frame / 180f); fx.ShowTendril(0, new Vector3(0, 150, 10), new Vector3(4, 150.6f, 10), frame / 60f); }
                yield return null; timings.Add(Time.unscaledDeltaTime); bytes += gc.LastValue;
            }
            timings.Sort(); Debug.Log("MEASURED " + (phase == 0 ? "idle" : "active") + " fixed-pool whole-frame median_ms=" + timings[timings.Count / 2] * 1000 + " p95_ms=" + timings[(int)(timings.Count * .95f)] * 1000 + " GC_B_per_frame=" + bytes / 180);
        }
        fx.HideAll();
    }

    IEnumerator ReloadChecks()
    {
        yield return Scene(GameFlow.HomeScene);
        Check(S.Enabled && S.EnableWhenComplete && !S.DeveloperPreview && !SignatureCatalog.VerificationPreview, "Authored full roster enabled without preview");
        var profile = FindAnyObjectByType<ModeScreens>().Profile;
        Check(profile.EquippedSignature?.Id == "plague-bloom", "Separate process restores signature selection");
        Check(profile.EquippedA == P("poison") && profile.EquippedSecondary == P("speed"), "Separate process preserves primary and secondary");
    }
}
#endif
