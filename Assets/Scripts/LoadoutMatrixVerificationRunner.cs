#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// See LoadoutMatrixVerification. All C(11,2) = 55 loadouts, each saved through PlayerProgression.SetLoadout and played in a
/// real Hero session: both powers equipped, the synergy resolved is exactly the capped one for that pair (5 pairs) or none,
/// every power's generic LIFECYCLE the first time it is equipped (fresh charges / fuel, pay on use, cooldown block, cooldown
/// and charge recovery, channel start / drain / release, flight fuel spend and refill), a use of both powers in every later
/// pair, the pair's synergy attempted, and a teardown check that no power object survives the return Home. Each pair's
/// save is snapshotted; Reload (a SECOND Unity process) re-reads all 55 snapshots in one batch.
public sealed class LoadoutMatrixVerificationRunner : SessionVerificationRunner
{
    public bool Reload;
    protected override string Folder => "Verification/LoadoutMatrix/";
    protected override string ResultFile => Reload ? "reload.txt" : "results.txt";
    string Manifest => Folder + "manifest.txt";
    string Snapshots => Path.GetFullPath(Folder + "saves/");
    static readonly string[] Shipping = { "sonic-slam", "thermal-shock", "solar-flare", "void-grasp", "eclipse-beam" };
    readonly HashSet<string> lifecycle = new HashSet<string>();
    CityNpc dummy; Rigidbody crate;
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        if (Reload) { ReadBack(); yield break; }
        var powers = Resources.LoadAll<PowerDefinition>("Powers").OrderBy(p => p.Id).ToArray();
        var hero = F.Heroes[0];
        Check(powers.Length == 11 && powers.All(p => hero.AvailablePowers.Contains(p)), $"11 powers, all available to {hero.DisplayName}: {string.Join(", ", powers.Select(p => p.Id))}.");
        Directory.CreateDirectory(Snapshots); var manifest = new List<string>(); int synergies = 0, pairIndex = 0;
        for (int i = 0; i < powers.Length; i++)
            for (int j = i + 1; j < powers.Length; j++)
            {
                var a = powers[i]; var b = powers[j]; pairIndex++;
                Log($"---- PAIR {pairIndex}/55: {a.Id} + {b.Id}");
                yield return Enter(hero, a.Id, b.Id, "hero");
                var expected = F.Resolve(a, b);
                Check(W.Powers.EquippedA == a && W.Powers.EquippedB == b && W.Powers.Synergy == expected && (expected == null || Shipping.Contains(expected.Id)),
                    $"Equipped {a.Id} + {b.Id}; synergy {(expected != null ? expected.Id : "none")}.");
                if (expected != null) synergies++;
                Isolate(); Props();
                foreach (var p in new[] { a, b })
                {
                    if (lifecycle.Add(p.Id)) yield return Lifecycle(p);
                    else yield return QuickUse(p);
                }
                if (expected != null) yield return TrySynergy();
                W.Progression.Save(); Check(W.Progression.LastError == null, "Saved.");
                string snapshot = Snapshots + a.Id + "+" + b.Id + ".json"; File.Copy(W.Progression.SavePath, snapshot, true);
                manifest.Add(string.Join("|", snapshot, hero.Id, a.Id, b.Id, expected != null ? expected.Id : "-"));
            }
        yield return Home();
        Check(FindObjectsByType<PowerProjectile>(FindObjectsSortMode.None).Length == 0 && FindObjectsByType<ThrownProp>(FindObjectsSortMode.None).Length == 0 &&
              FindObjectsByType<Poisoned>(FindObjectsSortMode.None).Length == 0 && FindObjectsByType<SynergySuspension>(FindObjectsSortMode.None).Length == 0 &&
              FindObjectsByType<PowerVfx>(FindObjectsSortMode.None).Length == 0 && FindObjectsByType<CityNpc>(FindObjectsSortMode.None).Length == 0,
            "Teardown: back Home, no projectile, thrown prop, poison, suspension, line pool or NPC survived the city.");
        Check(synergies == 5 && lifecycle.Count == 11, $"55 pairs played: exactly {synergies} resolved a synergy; lifecycle run for all {lifecycle.Count} powers.");
        File.WriteAllLines(Manifest, manifest);
        Log($"Manifest of {manifest.Count} save snapshots written for the batched reload.");
    }
    void Props()
    {
        dummy = Actor(new Vector3(0, 150, 7), NpcRole.Criminal, 100000f);
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = "Matrix crate"; box.transform.position = new Vector3(3, 150.6f, 6); box.transform.localScale = Vector3.one;
        box.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Wood); crate = box.AddComponent<Rigidbody>(); crate.mass = 40f; Physics.SyncTransforms();
    }
    void AimFor(PowerDefinition p)
    {
        PlaceHero(new Vector3(0, 150.05f, 0));
        if (p.Effect is TelekinesisEffect && crate != null) Aim(crate.worldCenterOfMass);
        else if (dummy != null) Aim(Chest(dummy));
    }
    IEnumerator Lifecycle(PowerDefinition p)
    {
        var rt = Runtime(p.Id); var stats = W.Powers.Stats(rt);
        Check(rt.Charges == stats.Charges && rt.Cooldown == 0f, $"LIFECYCLE {p.Id}: fresh session, {rt.Charges}/{stats.Charges} charges, no cooldown.");
        if (p.Effect.IsFlight)
        {
            Check(Mathf.Abs(rt.Fuel - stats.Duration) < .01f, $"Flight fuel full ({rt.Fuel:F2}).");
            float fuel = rt.Fuel; Check(W.Powers.ConsumeFlight(.5f) && Mathf.Abs(fuel - rt.Fuel - .5f * p.ResourceCost) < .01f, $"Flying 0.5 s spends {.5f * p.ResourceCost:F2} fuel.");
            fuel = rt.Fuel; yield return new WaitForSeconds(1f);
            Check(rt.Fuel > fuel, $"Grounded, fuel refills ({fuel:F2} -> {rt.Fuel:F2}).");
            yield break;
        }
        Check(W.Powers.Select(rt), "Selected.");
        yield return new WaitForSeconds(.1f);
        AimFor(p); float energy = W.Powers.Energy; int charges = rt.Charges;
        if (p.Activation == PowerActivation.Channeled)
        {
            Check(W.Powers.Use(rt) && W.Powers.Channeling == rt && rt.Charges == charges, "Channel starts without spending a charge.");
            float until = Time.time + .5f; while (Time.time < until) { W.Powers.Channel(true, Time.deltaTime); yield return null; }
            Check(W.Powers.Energy < energy, $"Held 0.5 s: energy {energy:F1} -> {W.Powers.Energy:F1}.");
            W.Powers.Channel(false, Time.deltaTime);
            Check(W.Powers.Channeling == null && rt.Cooldown > 0f, $"Released: channel ended, cooldown {rt.Cooldown:F2}.");
        }
        else
        {
            bool used = W.Powers.Use(rt);
            Check(used && rt.Charges == charges - 1 && Mathf.Abs(rt.Cooldown - stats.Cooldown) < .05f && W.Powers.Energy <= energy - p.ResourceCost + 1f,
                $"Use pays: charges {charges} -> {rt.Charges}, cooldown {rt.Cooldown:F2} (data {stats.Cooldown:F2}), energy {energy:F1} -> {W.Powers.Energy:F1} (cost {p.ResourceCost}). {W.Powers.Message}");
            if (W.Powers.HeldBody != null) W.Powers.Release(false);
            if (stats.Cooldown > .1f) { int c = rt.Charges; Check(!W.Powers.Use(rt) && rt.Charges == c && W.Powers.Message == "Blocked: cooldown", "CONTROL: a second use inside the cooldown is refused and spends nothing."); }
        }
        float wait = Time.time + stats.Cooldown + .5f; while (rt.Cooldown > 0f && Time.time < wait) yield return null;
        Check(rt.Cooldown == 0f, "Cooldown runs out.");
        if (rt.Charges < stats.Charges)
        {
            wait = Time.time + p.ChargeRecharge * (stats.Charges - rt.Charges) + .5f; while (rt.Charges < stats.Charges && Time.time < wait) yield return null;
            Check(rt.Charges == stats.Charges, $"Charges recover to {stats.Charges} within ChargeRecharge {p.ChargeRecharge} s each.");
        }
        yield return Settle();
    }
    IEnumerator QuickUse(PowerDefinition p)
    {
        var rt = Runtime(p.Id);
        if (p.Effect.IsFlight) { Check(W.Powers.ConsumeFlight(.1f), "Flight usable."); yield break; }
        Check(W.Powers.Select(rt), $"{p.Id} selected."); yield return null;
        AimFor(p); rt.Cooldown = 0f; rt.Charges = Mathf.Max(1, rt.Charges);
        bool used = W.Powers.Use(rt);
        Check(used, $"{p.Id} used ({W.Powers.Message}).");
        if (W.Powers.Channeling == rt) { yield return null; W.Powers.Channel(false, Time.deltaTime); }
        if (W.Powers.HeldBody != null) W.Powers.Release(false);
        yield return Settle();
    }
    IEnumerator TrySynergy()
    {
        var runner = W.Powers.SynergyRunner; PlaceHero(new Vector3(0, 150.05f, 0)); Aim(Chest(dummy));
        bool started = runner.TryActivate();
        float until = Time.time + 10f; while (runner.Busy && Time.time < until) yield return null;
        Check(!runner.Busy, $"Synergy {W.Powers.Synergy.Id}: {(started ? "performed and finished" : "not started (" + runner.Feedback + ")")}, runner idle afterwards.");
        yield return Settle();
    }
    /// Let dashes / projectiles finish and put the hero back.
    IEnumerator Settle() { yield return new WaitForSeconds(.4f); PlaceHero(new Vector3(0, 150.05f, 0)); }
    void ReadBack()
    {
        Log("---- BATCHED SEPARATE-PROCESS RELOAD");
        var lines = File.ReadAllLines(Manifest); int ok = 0;
        var tuning = Resources.Load<GameTuning>("GameTuning").Progression; var powers = Resources.LoadAll<PowerDefinition>("Powers");
        foreach (var line in lines)
        {
            var f = line.Split('|');
            var probe = new GameObject("Matrix probe").AddComponent<PlayerProgression>(); probe.Initialize(tuning, powers, f[0]);
            var synergy = F.Resolve(probe.EquippedA, probe.EquippedB);
            bool match = probe.LastError == null && probe.Data.Loadout != null && probe.Data.Loadout.HeroId == f[1] && probe.Data.Loadout.PowerA == f[2] && probe.Data.Loadout.PowerB == f[3] && (synergy != null ? synergy.Id : "-") == f[4];
            if (!match) Log($"MISMATCH {Path.GetFileName(f[0])}: {probe.Data.Loadout?.HeroId} {probe.Data.Loadout?.PowerA} {probe.Data.Loadout?.PowerB} synergy {(synergy != null ? synergy.Id : "-")} error {probe.LastError}");
            else ok++;
            Destroy(probe.gameObject);
        }
        Check(lines.Length == 55 && ok == 55, $"All {ok}/{lines.Length} snapshots reload with the same hero, powers and synergy in a separate process.");
    }
}
#endif
