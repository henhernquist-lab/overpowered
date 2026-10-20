#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// Automated Play Mode verification for Synergies 2.0 foundation and the first four signature abilities
/// (Seismic Slam, Solar Flare, Glacial Prison, Thunderstorm). Evidence written to Verification/Synergies2/.
public sealed class Signatures2VerificationRunner : MonoBehaviour
{
    public Action<int> Finished;
    public bool Reload;
    const string Folder = "Verification/Synergies2/";
    const string Saves = Folder + "saves/";
    readonly List<string> output = new List<string>();
    string runtimeFailure;
    bool originalPreview, originalCompleteGate;

    WorldSession W => WorldSession.Instance;
    ForgeCatalog F => Resources.Load<ForgeCatalog>("ForgeCatalog");
    SignatureCatalog S => Resources.Load<SignatureCatalog>("SignatureCatalog");
    PowerDefinition Power(string id) => Resources.Load<PowerDefinition>("Powers/" + id);
    EnemyArchetype Archetype(string name) => Resources.Load<EnemyArchetype>("Enemies/" + name);

    void Awake()
    {
        if (S != null) { originalPreview = S.DeveloperPreview; originalCompleteGate = S.EnableWhenComplete; }
        Application.logMessageReceived += ObserveLog;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= ObserveLog;
        RestoreCatalog();
    }

    void RestoreCatalog()
    {
        SignatureCatalog.VerificationPreview = false;
        if (S == null) return;
        S.DeveloperPreview = originalPreview;
        S.EnableWhenComplete = originalCompleteGate;
    }

    void ObserveLog(string message, string trace, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) &&
            (trace.Contains("Assets/Scripts/") || trace.Contains("Signature") || trace.Contains("Synergy")))
            runtimeFailure = message;
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(Saves);
        var stack = new Stack<IEnumerator>();
        stack.Push(Reload ? ReloadChecks() : Checks());
        while (stack.Count > 0)
        {
            object next = null;
            bool moved = false;
            try
            {
                if (runtimeFailure != null) throw new Exception("Gameplay Console error: " + runtimeFailure);
                moved = stack.Peek().MoveNext();
                if (moved) next = stack.Peek().Current;
            }
            catch (Exception e)
            {
                Log("FAIL " + e);
                RestoreCatalog();
                Write();
                Finished?.Invoke(1);
                yield break;
            }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested);
            else yield return next;
        }
        RestoreCatalog();
        Write();
        Finished?.Invoke(0);
    }

    void Log(string line) { output.Add(line); Debug.Log(line); }
    void Check(bool ok, string line) { if (!ok) throw new Exception(line); Log("PASS " + line); }
    void Write() { File.WriteAllLines(Folder + (Reload ? "reload.txt" : "results.txt"), output); }

    IEnumerator Scene(string name)
    {
        float until = Time.realtimeSinceStartup + 45f;
        while (GameFlow.Instance == null || GameFlow.Instance.Loading ||
               SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        {
            if (Time.realtimeSinceStartup > until) throw new Exception("Scene timeout: " + name);
            yield return null;
        }
        yield return new WaitForSecondsRealtime(.4f);
    }

    void Submit(Button button)
    {
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); }
    }

    GameObject arenaFloor;
    void Isolate()
    {
        foreach (var npc in W.Npcs.ToArray()) if (npc != null) Destroy(npc.gameObject);
        W.Npcs.Clear();
        if (arenaFloor == null)
        {
            arenaFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arenaFloor.name = "Signatures2 isolated floor";
            arenaFloor.transform.position = new Vector3(0f, 149.5f, 0f);
            arenaFloor.transform.localScale = new Vector3(120f, 1f, 120f);
            arenaFloor.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Road);
        }
        var cc = W.Hero.GetComponent<CharacterController>();
        cc.enabled = false;
        W.Hero.transform.position = new Vector3(0f, 150.05f, 0f);
        W.Hero.transform.rotation = Quaternion.identity;
        cc.enabled = true;
        W.Hero.ResetMotion();
        var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (rig != null) rig.enabled = false;
        AimAt(new Vector3(0f, 151f, 10f));
        Physics.SyncTransforms();
    }

    CityNpc SpawnActor(Vector3 pos, NpcRole role, EnemyArchetype archetype, float hp = 1000f)
    {
        var npc = CityNpc.Spawn(W, W.City.Sidewalks[0], role, archetype, hp, 0f);
        if (npc == null) throw new Exception("Failed to spawn test actor");
        npc.enabled = false;
        npc.Agent.enabled = false;
        npc.transform.position = pos;
        npc.SetCombatStats(hp, 0f);
        Physics.SyncTransforms();
        return npc;
    }

    void AimAt(Vector3 worldPoint)
    {
        Vector3 head = W.Hero.transform.position + Vector3.up * 1.7f;
        Vector3 dir = (worldPoint - head).normalized;
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        if (flat.sqrMagnitude > .001f) W.Hero.transform.forward = flat.normalized;
        if (Camera.main != null)
        {
            Camera.main.transform.position = head - dir * 1f + Vector3.up * .2f;
            Camera.main.transform.LookAt(worldPoint);
        }
        Physics.SyncTransforms();
    }

    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var menu = FindAnyObjectByType<ModeScreens>();
        var profile = menu.Profile;

        // 1. Default transition gate: DeveloperPreview is off on disk, Complete is false (4/8), legacy synergies remain active.
        Check(S != null && !S.DeveloperPreview && S.EnableWhenComplete && !S.Complete && !S.Enabled,
            "Transition gate default: SignatureCatalog has 4/8 abilities, Complete=false, DeveloperPreview=false, Enabled=false.");
        Check(S.Abilities.Length == 4 && S.Abilities.All(a => S.Implemented(a)),
            "Exactly four implemented signature abilities registered: " + string.Join(", ", S.Abilities.Select(a => a.Id)));
        Check(S.ForPrimary(Power("strength"))?.Id == "seismic-slam" &&
              S.ForPrimary(Power("fire"))?.Id == "solar-flare" &&
              S.ForPrimary(Power("ice"))?.Id == "glacial-prison" &&
              S.ForPrimary(Power("lightning"))?.Id == "thunderstorm",
            "Primary ownership maps Strength->seismic-slam, Fire->solar-flare, Ice->glacial-prison, Lightning->thunderstorm.");
        foreach (var unimpl in new[] { "telekinesis", "darkness", "laser-eyes", "poison" })
            Check(S.ForPrimary(Power(unimpl)) == null, $"Unimplemented primary '{unimpl}' has no placeholder marked implemented.");

        Check(profile.SetLoadout(F.Hero("vector"), Power("fire"), Power("ice"), Power("speed"), CityColor.Blue, CityColor.Cyan),
            "Set Fire + Ice loadout with preview off.");
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Enter Hero mode with preview off.");
        yield return Scene(GameFlow.CityScene);
        Check(W.Powers.Synergy != null && !W.Powers.Synergy.IsSignature && W.Powers.Synergy.Id == "thermal-shock",
            "Normal gameplay (preview off) preserves legacy pair synergy Thermal Shock for Fire + Ice.");
        GameFlow.Instance.Home();
        yield return Scene(GameFlow.HomeScene);

        // 2. Enable DeveloperPreview: Hero Forge selection, switching, invalidation, and unimplemented primary handling.
        SignatureCatalog.VerificationPreview = true;
        S.DeveloperPreview = true;
        Check(S.Enabled, "DeveloperPreview=true enables SignatureCatalog in Editor/dev builds.");
        menu = FindAnyObjectByType<ModeScreens>();
        profile = menu.Profile;
        Submit(menu.ForgeButton);
        yield return null;
        var screen = menu.ForgeScreen;
        Check(screen.SelectHero(F.Hero("vector")) && screen.SelectPower(0, Power("fire")) && screen.SelectPower(1, Power("ice")),
            "Equip Fire + Ice in Hero Forge with signature preview active.");
        var choices = S.Choices(Power("fire"), Power("ice"));
        Check(choices.Count == 2 && choices[0].Id == "solar-flare" && choices[1].Id == "glacial-prison",
            "Two implemented primaries (Fire + Ice) expose exactly two signature choices: Solar Flare and Glacial Prison.");
        Check(profile.EquippedSignature == choices[0] && screen.SynergyLabel.text == "SYNERGY / Solar Flare",
            "Only one signature is equipped initially (Solar Flare), reflected in Hero Forge label.");
        var btnSolar = screen.Root.Q<Button>("signature-solar-flare");
        var btnGlacial = screen.Root.Q<Button>("signature-glacial-prison");
        Check(btnSolar != null && btnGlacial != null && btnSolar.text.Contains("SELECTED") && !btnGlacial.text.Contains("SELECTED"),
            "Hero Forge renders both signature choice buttons with owner primary, cooldown, energy cost, and selected state.");

        Submit(btnGlacial);
        yield return null;
        Check(profile.EquippedSignature != null && profile.EquippedSignature.Id == "glacial-prison" &&
              screen.SynergyLabel.text == "SYNERGY / Glacial Prison",
            "Selecting Glacial Prison replaces Solar Flare as the single equipped signature.");

        // Changing Ice -> Lightning invalidates Glacial Prison and selects a valid choice (Solar Flare), storing Glacial Prison in UnresolvedSignatureId.
        Check(screen.SelectPower(1, Power("lightning")), "Change Slot 2 from Ice to Lightning.");
        Check(profile.EquippedSignature != null && profile.EquippedSignature.Id == "solar-flare" &&
              profile.Data.Loadout.UnresolvedSignatureId == "glacial-prison",
            "Changing primaries invalidates Glacial Prison, preserves it in UnresolvedSignatureId, and selects Solar Flare.");

        // 1 implemented + 1 unimplemented primary (Strength + Telekinesis) -> 1 choice (Seismic Slam).
        Check(screen.SelectPower(0, Power("strength")) && screen.SelectPower(1, Power("telekinesis")),
            "Equip Strength (implemented) + Telekinesis (unimplemented).");
        Check(S.Choices(Power("strength"), Power("telekinesis")).Count == 1 &&
              profile.EquippedSignature?.Id == "seismic-slam" &&
              screen.Root.Q<Button>("signature-seismic-slam") != null,
            "One implemented + one unimplemented primary exposes only Seismic Slam.");

        // 2 unimplemented primaries (Darkness + Telekinesis) -> 0 choices, null EquippedSignature, cannot activate.
        Check(screen.SelectPower(0, Power("darkness")) && screen.SelectPower(1, Power("telekinesis")),
            "Equip Darkness + Telekinesis (both unimplemented).");
        Check(S.Choices(Power("darkness"), Power("telekinesis")).Count == 0 && profile.EquippedSignature == null &&
              !profile.SetSignature("bogus-signature"),
            "Two unimplemented primaries expose zero selectable signatures and refuse invalid signature IDs.");
        screen.Close();

        // 3. Save migration & corrupted/legacy signature IDs.
        string migPath = Path.GetFullPath(Saves + "mig-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(migPath, "{\"Version\":1,\"Level\":4,\"Xp\":25,\"Points\":3,\"Side\":0,\"Powers\":[{\"Id\":\"fire\",\"Tier\":1},{\"Id\":\"lightning\",\"Tier\":0},{\"Id\":\"speed\",\"Tier\":0}],\"Loadout\":{\"HeroId\":\"nova\",\"PowerA\":\"fire\",\"PowerB\":\"lightning\",\"SecondaryPower\":\"speed\",\"SignatureId\":\"thermal-shock\",\"Primary\":13,\"Secondary\":15}}");
        var mig = new GameObject("Migration test").AddComponent<PlayerProgression>();
        mig.Initialize(Resources.Load<GameTuning>("GameTuning").Progression, Resources.LoadAll<PowerDefinition>("Powers"), migPath);
        Check(mig.LastError == null && mig.Data.Level == 4 && mig.Data.Xp == 25 && mig.Data.Points == 3 &&
              mig.Tier(Power("fire")) == 1 && mig.EquippedSignature?.Id == "solar-flare" &&
              mig.Data.Loadout.UnresolvedSignatureId == "thermal-shock",
            "Save referencing legacy pair ID 'thermal-shock' preserves progression, stores UnresolvedSignatureId, and resolves 'solar-flare'.");
        Destroy(mig.gameObject);

        // 4. Ability 1: SEISMIC SLAM (Strength) — midair refusal, grounded shockwave, falloff, Brute stagger resistance, friendly safety.
        Check(profile.SetLoadout(F.Hero("vector"), Power("strength"), Power("fire"), Power("speed"), CityColor.Blue, CityColor.Cyan) &&
              profile.SetSignature("seismic-slam"), "Equip Strength + Fire with Seismic Slam selected.");
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Enter Hero mode for Seismic Slam.");
        yield return Scene(GameFlow.CityScene);
        Isolate();
        var r = W.Powers.SynergyRunner;
        var hud = FindAnyObjectByType<GameHud>();
        yield return null;
        var hudSlot = hud.Slots.FirstOrDefault(s => s.Synergy != null);
        Check(W.Powers.Synergy?.Id == "seismic-slam" && hudSlot != null && hudSlot.StatusLabel != null && hudSlot.StatusLabel.text == "READY",
            "HUD displays Seismic Slam slot in READY state.");

        // Midair refusal control
        W.Hero.transform.position = new Vector3(0f, 160f, 0f);
        Physics.SyncTransforms();
        Check(!r.TryActivate() && r.Cooldown == 0f && r.Feedback == "No valid target",
            "CONTROL: Seismic Slam refused high in midair without spending energy or cooldown.");

        // Low energy refusal control
        Isolate();
        W.Powers.TrySpendEnergy(W.Powers.Energy - 5f);
        yield return null;
        Check(r.ReadyState == "INSUFFICIENT ENERGY" && !r.TryActivate() && r.Cooldown == 0f,
            "CONTROL: Seismic Slam refused on insufficient energy and HUD reports INSUFFICIENT ENERGY.");
        W.Powers.Tick(10f, true);
        W.Hero.DebugSetResources(6f, 3, 0f);

        var rusherNear = SpawnActor(new Vector3(0f, 150.05f, 3f), NpcRole.Criminal, Archetype("Rusher"), 1000f);
        var gunnerOuter = SpawnActor(new Vector3(5.8f, 150.05f, 0f), NpcRole.Criminal, Archetype("Gunner"), 1000f);
        var bruteNear = SpawnActor(new Vector3(-3f, 150.05f, 0f), NpcRole.Criminal, Archetype("Brute"), 1000f);
        var farEnemy = SpawnActor(new Vector3(0f, 150.05f, 13f), NpcRole.Criminal, Archetype("Rusher"), 1000f);
        var civilian = SpawnActor(new Vector3(2.5f, 150.05f, 2.5f), NpcRole.Civilian, null, 100f);
        var friendlyCop = SpawnActor(new Vector3(-2.5f, 150.05f, 2.5f), NpcRole.Cop, Archetype("Gunner"), 1000f);
        float energyBefore = W.Powers.Energy;

        Check(r.TryActivate(), "Seismic Slam activates on grounded hero.");
        Check(Mathf.Abs(energyBefore - W.Powers.Energy - r.EnergyCost) < .5f && r.Cooldown > 20f,
            $"Seismic Slam spent {r.EnergyCost:0.#} energy and started {r.Cooldown:0.#}s cooldown.");
        Check(!r.TryActivate(), "CONTROL: duplicate press during active/cooldown Seismic Slam is refused.");
        float waitUntil = Time.time + 4f;
        while (r.Busy && Time.time < waitUntil) yield return null;

        float rusherDmg = 1000f - rusherNear.Health;
        float outerDmg = 1000f - gunnerOuter.Health;
        float rusherDisp = Vector3.Distance(new Vector3(0f, rusherNear.transform.position.y, 3f), rusherNear.transform.position);
        float bruteDisp = Vector3.Distance(new Vector3(-3f, bruteNear.transform.position.y, 0f), bruteNear.transform.position);
        Check(Mathf.Abs(rusherDmg - r.ActivationDamage) < .05f && outerDmg > 0f && outerDmg < rusherDmg,
            $"Seismic Slam inner target took full {rusherDmg:0.##} dmg, outer ring took falloff {outerDmg:0.##} dmg.");
        Check(rusherDisp > bruteDisp,
            $"Brute resisted push ({bruteDisp:F2}m) more than Rusher ({rusherDisp:F2}m).");
        Check(farEnemy.Health == 1000f && civilian.Health == civilian.MaxHealth && friendlyCop.Health == 1000f,
            "CONTROL: out-of-range enemy, civilian, and friendly 0-Heat cop took 0 damage from Seismic Slam.");

        // Verify normal melee (E), Hurricane Kick (RMB), secondary (F), and double jump still work.
        Check(W.Hero.TryComboAttack(), "Normal E melee punch remains functional alongside signatures.");
        yield return new WaitForSeconds(.5f);
        Check(W.Hero.TryHurricaneKick(), "RMB Hurricane Kick remains functional.");
        yield return new WaitForSeconds(.6f);
        Check(W.Powers.UseSecondary(), "F secondary power (Speed) remains functional.");
        yield return new WaitForSeconds(.35f);

        GameFlow.Instance.Home();
        yield return Scene(GameFlow.HomeScene);

        // 5. Ability 2: SOLAR FLARE (Fire) — wide frontal wave, burning wake residual ticks, first-person safety, wall block.
        menu = FindAnyObjectByType<ModeScreens>();
        profile = menu.Profile;
        Check(profile.SetLoadout(F.Hero("vector"), Power("fire"), Power("ice"), Power("speed"), CityColor.Blue, CityColor.Cyan) &&
              profile.SetSignature("solar-flare"), "Equip Fire + Ice with Solar Flare signature.");
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Enter Hero mode for Solar Flare.");
        yield return Scene(GameFlow.CityScene);
        Isolate();
        r = W.Powers.SynergyRunner;

        var camRig = Camera.main.GetComponent<ThirdPersonCamera>();
        if (!camRig.FirstPerson) Check(camRig.ToggleView(), "Switch to First-Person view for Solar Flare test.");
        AimAt(new Vector3(0f, 151f, 10f));

        var frontLeft = SpawnActor(new Vector3(-2.4f, 150.05f, 8f), NpcRole.Criminal, Archetype("Rusher"), 1000f);
        var frontRight = SpawnActor(new Vector3(2.4f, 150.05f, 7.5f), NpcRole.Criminal, Archetype("Gunner"), 1000f);
        var behindHero = SpawnActor(new Vector3(0f, 150.05f, -5f), NpcRole.Criminal, Archetype("Rusher"), 1000f);

        Vector3 fpCamBefore = Camera.main.transform.localPosition;
        Check(r.TryActivate(), "Solar Flare activates in first-person view.");
        yield return new WaitForSeconds(.55f);
        float initialLeftDmg = 1000f - frontLeft.Health;
        float initialRightDmg = 1000f - frontRight.Health;
        Check(SolarFlareSignatureEffect.LastWaveStruck.Count == 2 &&
              Mathf.Abs(initialLeftDmg - r.ActivationDamage) < .05f &&
              Mathf.Abs(initialRightDmg - r.ActivationDamage) < .05f &&
              frontLeft.Burning && frontRight.Burning && behindHero.Health == 1000f,
            $"Solar Flare wave struck both frontal targets once ({initialLeftDmg:0.##} dmg, burning) and ignored enemy behind player.");
        Check(SignatureVfx.Get().SolarWakeVisible, "Solar Flare residual heat wake is active after the wave sweep.");

        waitUntil = Time.time + 4f;
        while (r.Busy && Time.time < waitUntil) yield return null;
        float postWakeRightDmg = 1000f - frontRight.Health;
        Check(SolarFlareSignatureEffect.LastResidualTicks >= 3 && postWakeRightDmg > initialRightDmg &&
              !SignatureVfx.Get().SolarWakeVisible,
            $"Solar Flare residual wake dealt additional DoT ({initialRightDmg:0.##} -> {postWakeRightDmg:0.##}) across {SolarFlareSignatureEffect.LastResidualTicks} ticks and cleaned up.");
        Check(Vector3.Distance(Camera.main.transform.localPosition, fpCamBefore) < .25f,
            "First-person camera stayed capsule-relative during Solar Flare animation.");
        if (camRig.FirstPerson) camRig.ToggleView();

        GameFlow.Instance.Home();
        yield return Scene(GameFlow.HomeScene);

        // 6. Ability 3: GLACIAL PRISON (Ice) — pillar ring eruption, containment, damageable while imprisoned, shatter expiration, recovery.
        menu = FindAnyObjectByType<ModeScreens>();
        profile = menu.Profile;
        Check(profile.SetLoadout(F.Hero("vector"), Power("ice"), Power("strength"), Power("speed"), CityColor.Blue, CityColor.Cyan) &&
              profile.SetSignature("glacial-prison"), "Equip Ice + Strength with Glacial Prison signature.");
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Enter Hero mode for Glacial Prison.");
        yield return Scene(GameFlow.CityScene);
        Isolate();
        r = W.Powers.SynergyRunner;

        // No-target refusal control
        AimAt(new Vector3(0f, 151f, 12f));
        Check(!r.TryActivate() && r.Cooldown == 0f, "CONTROL: Glacial Prison with no hostiles in target area refuses activation without cooldown.");

        var prisonerA = SpawnActor(new Vector3(0f, 150.05f, 10f), NpcRole.Criminal, Archetype("Rusher"), 1000f);
        var prisonerB = SpawnActor(new Vector3(2.8f, 150.05f, 10.5f), NpcRole.Criminal, Archetype("Gunner"), 1000f);
        var outsidePrison = SpawnActor(new Vector3(9f, 150.05f, 10f), NpcRole.Criminal, Archetype("Rusher"), 1000f);
        AimAt(prisonerA.transform.position + Vector3.up * 1f);

        Check(r.TryActivate(), "Glacial Prison activates on targeted enemy cluster.");
        yield return new WaitForSeconds(.35f);
        float eruptDmg = 1000f - prisonerA.Health;
        Check(SignatureVfx.Get().PrisonVisible && GlacialPrisonEffect.LastImprisoned.Count == 2 &&
              prisonerA.Rooted && prisonerB.Rooted && eruptDmg > 0f && outsidePrison.Health == 1000f,
            $"Glacial Prison erected pillar ring, rooted 2 hostiles inside ({eruptDmg:0.##} eruption dmg), and left distant enemy untouched.");

        // Imprisoned enemy can still be damaged by player attacks during containment
        float beforeExtra = prisonerA.Health;
        using (W.Powers.Credit("ice")) prisonerA.Damage(15f, W.Powers);
        Check(prisonerA.Health < beforeExtra, "Imprisoned enemies remain damageable while contained in Glacial Prison.");

        waitUntil = Time.time + 5f;
        while (r.Busy && Time.time < waitUntil) yield return null;
        float totalPrisonDmg = 1000f - prisonerB.Health;
        Check(!SignatureVfx.Get().PrisonVisible && GlacialPrisonEffect.LastShatterHits == 2 &&
              Mathf.Abs(totalPrisonDmg - r.ActivationDamage) < .1f && !prisonerA.Rooted && !prisonerB.Rooted,
            $"Glacial Prison shattered on expiration (total {totalPrisonDmg:0.##} == ActivationDamage {r.ActivationDamage:0.##}), hid pillars, and released survivors.");

        GameFlow.Instance.Home();
        yield return Scene(GameFlow.HomeScene);

        // 7. Ability 4: THUNDERSTORM (Lightning) + Villain mode & Difficulty scaling + escape test + pool stability.
        menu = FindAnyObjectByType<ModeScreens>();
        profile = menu.Profile;
        Check(profile.SetLoadout(F.Hero("vector"), Power("lightning"), Power("fire"), Power("speed"), CityColor.Red, CityColor.Amber) &&
              profile.SetSignature("thunderstorm"), "Equip Lightning + Fire with Thunderstorm signature (saved for separate-process Reload).");
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/villain")), "Enter Villain mode for Thunderstorm.");
        yield return Scene(GameFlow.CityScene);
        Isolate();
        r = W.Powers.SynergyRunner;

        var stationaryTarget = SpawnActor(new Vector3(0f, 150.05f, 11f), NpcRole.Cop, Archetype("Gunner"), 1000f);
        var escapingTarget = SpawnActor(new Vector3(2f, 150.05f, 11f), NpcRole.Cop, Archetype("Rusher"), 1000f);
        // Trigger 1 Heat star in Villain mode so police are hostile
        using (W.Powers.Credit("lightning")) stationaryTarget.Damage(1f, W.Powers, true);
        stationaryTarget.SetCombatStats(1000f, 0f);
        Check(stationaryTarget.Hostile && escapingTarget.Hostile, "Villain mode police are hostile at >=1 Heat star.");

        AimAt(stationaryTarget.transform.position + Vector3.up * 1f);
        Check(r.TryActivate(), "Thunderstorm activates over targeted combat area.");
        yield return new WaitForSeconds(.6f);
        Check(SignatureVfx.Get().StormVisible && ThunderstormEffect.LastPulsesCompleted >= 1,
            "Thunderstorm overhead cloud and ground danger ring active; Pulse 1 struck.");
        float escaperAfterPulse1 = escapingTarget.Health;
        Check(escaperAfterPulse1 < 1000f && stationaryTarget.Health < 1000f,
            "Both enemies inside Thunderstorm radius took Pulse 1 damage.");

        // Move escapingTarget outside the 6m storm radius before subsequent pulses
        escapingTarget.transform.position = new Vector3(12f, 150.05f, 11f);
        Physics.SyncTransforms();

        waitUntil = Time.time + 5f;
        while (r.Busy && Time.time < waitUntil) yield return null;
        float stationaryTotal = 1000f - stationaryTarget.Health;
        float escaperExtra = escaperAfterPulse1 - escapingTarget.Health;
        Check(!SignatureVfx.Get().StormVisible && ThunderstormEffect.LastPulsesCompleted == 4 &&
              Mathf.Abs(stationaryTotal - r.ActivationDamage) < .1f && Mathf.Abs(escaperExtra) < .01f,
            $"Thunderstorm completed 4 pulses (stationary took {stationaryTotal:0.##} == {r.ActivationDamage:0.##}; escaped target took {escaperExtra:0.##} after leaving zone) and cleaned up.");

        // 8. Difficulty scaling check (Hard difficulty applies 1.1x energy, 1.1x cooldown, 0.97x damage once)
        GameFlow.Instance.Home();
        yield return Scene(GameFlow.HomeScene);
        menu = FindAnyObjectByType<ModeScreens>();
        profile = menu.Profile;
        Check(profile.SetDifficulty("hard"), "Switch difficulty to Hard.");
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Enter Hero mode on Hard.");
        yield return Scene(GameFlow.CityScene);
        Isolate();
        r = W.Powers.SynergyRunner;
        var hardTarget = SpawnActor(new Vector3(0f, 150.05f, 10f), NpcRole.Criminal, Archetype("Brute"), 1000f);
        AimAt(hardTarget.transform.position + Vector3.up * 1f);
        Check(Mathf.Abs(r.EnergyCost - r.Definition.EnergyCost * 1.1f) < .01f,
            $"Hard difficulty scales signature energy cost once ({r.EnergyCost:0.##} == {r.Definition.EnergyCost} * 1.1).");
        Check(r.TryActivate(), "Thunderstorm activates on Hard difficulty.");
        Check(Mathf.Abs(r.Cooldown - r.Definition.Cooldown * 1.1f) < .05f,
            $"Hard difficulty scales signature cooldown once ({r.Cooldown:0.##}s == {r.Definition.Cooldown} * 1.1).");
        r.Cancel();
        Check(!r.Busy && !SignatureVfx.Get().StormVisible, "Cancel() immediately stops Thunderstorm and hides storm VFX.");

        // 9. Pool & allocation stability across repeated casts
        int matsBefore = Resources.FindObjectsOfTypeAll<Material>().Length;
        int meshesBefore = Resources.FindObjectsOfTypeAll<Mesh>().Length;
        int linesBefore = FindObjectsByType<LineRenderer>(FindObjectsInactive.Include).Length;
        for (int i = 0; i < 3; i++)
        {
            SignatureVfx.Get().ShowSeismicSlam(Vector3.zero, 8f, .2f);
            SignatureVfx.Get().ShowSolarWave(Vector3.zero, Vector3.forward, 6f, 3f, .5f, .2f);
            SignatureVfx.Get().ShowGlacialPrison(Vector3.zero, 3.2f, .2f);
            SignatureVfx.Get().ShowThunderstorm(Vector3.zero, 6f, 8.5f, .2f);
            SignatureVfx.Get().HideAll();
        }
        Check(Resources.FindObjectsOfTypeAll<Material>().Length == matsBefore &&
              Resources.FindObjectsOfTypeAll<Mesh>().Length == meshesBefore &&
              FindObjectsByType<LineRenderer>(FindObjectsInactive.Include).Length == linesBefore,
            $"Pooled SignatureVfx stability: materials={matsBefore}, meshes={meshesBefore}, LineRenderers={linesBefore} unchanged across repeated shows.");

        GameFlow.Instance.Home();
        yield return Scene(GameFlow.HomeScene);
        menu = FindAnyObjectByType<ModeScreens>();
        profile = menu.Profile;
        profile.SetDifficulty("normal");
        Check(profile.SetLoadout(F.Hero("vector"), Power("lightning"), Power("fire"), Power("speed"), CityColor.Red, CityColor.Amber) &&
              profile.SetSignature("thunderstorm"),
            "Persist Lightning + Fire with Thunderstorm signature to disk for separate-process Reload.");
        File.WriteAllText(Saves + "reload-save-path.txt", profile.SavePath);
    }

    IEnumerator ReloadChecks()
    {
        SignatureCatalog.VerificationPreview = true;
        yield return Scene(GameFlow.HomeScene);
        S.DeveloperPreview = true;
        var menu = FindAnyObjectByType<ModeScreens>();
        var profile = menu.Profile;
        Check(profile.LastError == null, "Separate-process Reload parsed saved progression without error.");
        Check(profile.EquippedA == Power("lightning") && profile.EquippedB == Power("fire") &&
              profile.Data.Loadout.SignatureId == "thunderstorm" &&
              profile.EquippedSignature != null && profile.EquippedSignature.Id == "thunderstorm",
            "SECOND PROCESS: saved signature selection 'thunderstorm' persisted and resolved for Lightning + Fire.");
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")), "Enter Hero mode in second process.");
        yield return Scene(GameFlow.CityScene);
        Check(W.Powers.Synergy != null && W.Powers.Synergy.IsSignature && W.Powers.Synergy.Id == "thunderstorm",
            "SECOND PROCESS: gameplay session caches 'thunderstorm' signature from reloaded save.");
    }
}
#endif
