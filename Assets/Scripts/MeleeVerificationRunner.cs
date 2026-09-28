#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

/// See MeleeVerification. Uses TryComboAttack / TryHeavyAttack / TryGroundPound, the methods the E key routes to, on the
/// isolated floor with AI-off criminals of 1000 HP (damage is measured as health lost). Strength-equipped (VECTOR, flight +
/// strength) and basic-melee (fire + ice) sessions.
public sealed class MeleeVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/Melee/";
    protected override string ResultFile => "results.txt";
    MeleeSettings M => W.Tuning.Melee;
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Enter("flight", "strength");
        var m = M; var s = W.Powers.Stats(W.Powers.Strength); var hero = W.Hero;
        Log($"DATA melee: combo {m.ComboLength} hits / {m.ComboWindow} s window, finisher x{m.FinisherForceMultiplier} force x{m.FinisherDamageMultiplier} damage + {m.FinisherHitPauseSeconds} s pause; heavy tap<{m.HeavyTapThreshold} s, cap {m.HeavyMaxChargeSeconds} s -> x{m.HeavyMaxForceMultiplier}/x{m.HeavyMaxDamageMultiplier}; pound >= {m.PoundMinHeight} m, radius {m.PoundRadius}, x{m.PoundForceMultiplier}/x{m.PoundDamageMultiplier}. Strength: {s.Force} N.s, {s.Damage} dmg.");
        Isolate();
        var target = Actor(new Vector3(0, 150, 2.2f), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        // ---------------------------------------------------------------- combo string
        Log("---- COMBO (Strength equipped)");
        hero.DebugSetResources(6, 3, 0); float hp = target.Health;
        Check(hero.TryComboAttack() && hero.LastComboStage == 1, "Tap 1 = combo stage 1 (punch).");
        yield return new WaitForSeconds(.55f);
        Check(Mathf.Abs(hp - target.Health - s.Damage) < .01f && Mathf.Approximately(hero.LastForce, s.Force), $"Stage 1 punch: {hp - target.Health:F2} dmg @ {hero.LastForce} N.s (plain punch).");
        hp = target.Health;
        Check(hero.TryComboAttack() && hero.LastComboStage == 2, "Tap 2 inside the window = stage 2 (punch).");
        yield return new WaitForSeconds(.55f);
        Check(Mathf.Abs(hp - target.Health - s.Damage) < .01f, $"Stage 2 punch: {hp - target.Health:F2} dmg.");
        hp = target.Health; int pauses = TimeArbiter.HitPausesStarted;
        Check(hero.TryComboAttack() && hero.LastComboStage == 3, "Tap 3 = the kick finisher.");
        yield return new WaitForSeconds(.8f);
        float finisherForce = s.Force * HeroAbilityTuning.KickForceMultiplier * m.FinisherForceMultiplier, finisherDamage = s.Damage * HeroAbilityTuning.KickDamageMultiplier * m.FinisherDamageMultiplier;
        Check(Mathf.Abs(hero.LastKickForce - finisherForce) < .5f && Mathf.Abs(hp - target.Health - finisherDamage) < .05f,
            $"Finisher: {hero.LastKickForce:F0} N.s (kick x{m.FinisherForceMultiplier}), {hp - target.Health:F2} dmg (expected {finisherDamage:F2}).");
        Check(TimeArbiter.HitPausesStarted > pauses, "Finisher impact produced a hit pause.");
        hero.DebugSetResources(6, 3, 0);
        Check(hero.TryComboAttack() && hero.LastComboStage == 1, "After the finisher the next tap restarts at stage 1.");
        yield return new WaitForSeconds(m.ComboWindow + .3f); hero.DebugSetResources(6, 3, 0);
        Check(hero.TryComboAttack() && hero.LastComboStage == 1, $"CONTROL: a tap after the {m.ComboWindow} s window restarts at stage 1 (not 2).");
        yield return new WaitForSeconds(.55f); hero.DebugSetResources(6, 0, 0); int stage = hero.LastComboStage; hp = target.Health;
        Check(!hero.TryComboAttack() && hero.LastComboStage == stage, "CONTROL: a tap with 0 Strength charges is refused and does not advance the string.");
        yield return new WaitForSeconds(.6f);
        Check(target.Health == hp, "CONTROL: the refused tap dealt nothing.");
        // ---------------------------------------------------------------- charged heavy
        Log("---- CHARGED HEAVY");
        float lastForce = 0;
        foreach (float held in new[] { m.HeavyTapThreshold, (m.HeavyTapThreshold + m.HeavyMaxChargeSeconds) * .5f, m.HeavyMaxChargeSeconds * 2.5f, m.HeavyMaxChargeSeconds * 5f })
        {
            hero.DebugSetResources(6, 3, 0); hp = target.Health;
            float t = Mathf.Clamp01((held - m.HeavyTapThreshold) / (m.HeavyMaxChargeSeconds - m.HeavyTapThreshold));
            float force = s.Force * Mathf.Lerp(1, m.HeavyMaxForceMultiplier, t), damage = s.Damage * Mathf.Lerp(1, m.HeavyMaxDamageMultiplier, t);
            Check(hero.TryHeavyAttack(held), $"Heavy released after {held:F2} s.");
            yield return new WaitForSeconds(.6f);
            Check(Mathf.Abs(hero.LastForce - force) < .5f && Mathf.Abs(hp - target.Health - damage) < .05f,
                $"Heavy {held:F2} s -> charge {t:F2}: {hero.LastForce:F0} N.s (expected {force:F0}), {hp - target.Health:F2} dmg (expected {damage:F2}).");
            if (held > m.HeavyMaxChargeSeconds * 3f) Check(Mathf.Approximately(hero.LastForce, lastForce), "CONTROL: holding longer than the cap adds nothing (capped).");
            lastForce = hero.LastForce;
        }
        hero.DebugSetResources(6, 0, 0);
        Check(!hero.TryHeavyAttack(1f), "CONTROL: a heavy with 0 Strength charges is refused.");
        // ---------------------------------------------------------------- ground pound
        Log("---- GROUND POUND");
        var sonic = F.Synergies.First(x => x != null && x.Id == "sonic-slam");
        Check(s.Force * m.PoundForceMultiplier < sonic.Force && m.PoundRadius < sonic.Radius && s.Damage * m.PoundDamageMultiplier < sonic.Damage,
            $"Pound is smaller than Sonic Slam: {s.Force * m.PoundForceMultiplier} < {sonic.Force} N.s, {m.PoundRadius} < {sonic.Radius} m, {s.Damage * m.PoundDamageMultiplier} < {sonic.Damage} dmg; no synergy cooldown.");
        var near = Actor(new Vector3(2.5f, 150, 0), NpcRole.Criminal, 1000); var far = Actor(new Vector3(7, 150, 0), NpcRole.Criminal, 1000);
        var prop = GameObject.CreatePrimitive(PrimitiveType.Cube); prop.transform.position = new Vector3(-2f, 150.6f, 1f); prop.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Wood);
        var body = prop.AddComponent<Rigidbody>(); body.mass = 45;
        target.transform.position = new Vector3(0, 150, 30); Physics.SyncTransforms();
        yield return new WaitForSeconds(.5f);
        hero.DebugSetResources(6, 3, 0); PlaceHero(new Vector3(0, 156, 0), true); yield return null;
        Check(hero.HeightAboveGround() >= m.PoundMinHeight, $"Hero airborne {hero.HeightAboveGround():F2} m above the floor.");
        Check(hero.TryGroundPound() && hero.GroundPounding && W.Powers.Strength.Charges == 2, "Ground pound starts and spends one Strength charge.");
        float until = Time.time + 3; while (hero.GroundPounding && Time.time < until) yield return null;
        yield return new WaitForFixedUpdate();
        Check(hero.GroundPounds == 1 && Mathf.Abs(hero.LastPoundForce - s.Force * m.PoundForceMultiplier) < .5f, $"Pound lands: {hero.LastPoundForce:F0} N.s.");
        Check(Mathf.Abs(1000 - near.Health - s.Damage * m.PoundDamageMultiplier) < .05f, $"Enemy 2.5 m away takes {1000 - near.Health:F2} (expected {s.Damage * m.PoundDamageMultiplier:F2}).");
        Check(far.Health == 1000, $"CONTROL: enemy 7 m away (radius {m.PoundRadius}) untouched.");
        Check(body.linearVelocity.magnitude > 1f, $"Radial knockback moves a 45 kg prop: {body.linearVelocity.magnitude:F2} m/s.");
        until = Time.time + 2; while (!hero.GetComponent<CharacterController>().isGrounded && Time.time < until) yield return null;
        int charges = W.Powers.Strength.Charges;
        Check(!hero.TryGroundPound() && W.Powers.Strength.Charges == charges, "CONTROL: E on the ground is not a pound; no charge spent.");
        yield return new WaitForSeconds(PrototypeTuning.PunchCooldown + .05f);
        hero.DebugSetResources(6, 2, 0); PlaceHero(new Vector3(0, 156, 0), true); yield return null;
        Check(hero.TryGroundPound(), "No long cooldown: a second pound is accepted right after the punch cooldown.");
        until = Time.time + 3; while (hero.GroundPounding && Time.time < until) yield return null;
        Check(hero.GroundPounds == 2, "Second pound lands.");
        // ---------------------------------------------------------------- basic melee (no Strength)
        Log("---- BASIC MELEE (Strength not equipped)");
        yield return Enter("fire", "ice");
        Isolate(); hero = W.Hero; var basic = Actor(new Vector3(0, 150, 2.2f), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        float bd = F.BasicDamage, bf = F.BasicForce;
        for (int i = 1; i <= 2; i++)
        {
            hp = basic.Health; pauses = TimeArbiter.HitPausesStarted;
            Check(hero.TryComboAttack() && hero.LastComboStage == i, $"Basic tap {i} = stage {i}.");
            yield return new WaitForSeconds(.75f);
            Check(Mathf.Abs(hp - basic.Health - bd) < .01f && TimeArbiter.HitPausesStarted == pauses, $"Basic stage {i}: {hp - basic.Health:F2} dmg, no hit pause (light {bf} N.s, CONTROL).");
        }
        hp = basic.Health; pauses = TimeArbiter.HitPausesStarted; int finisherPauses = hero.FinisherPauses;
        Check(hero.TryComboAttack() && hero.LastComboStage == 3, "Basic tap 3 = finisher.");
        yield return new WaitForSeconds(.9f);
        float basicFinisher = bd * HeroAbilityTuning.KickDamageMultiplier * m.FinisherDamageMultiplier;
        Check(Mathf.Abs(hp - basic.Health - basicFinisher) < .05f && hero.FinisherPauses == finisherPauses + 1 && TimeArbiter.HitPausesStarted == pauses + 1,
            $"Basic finisher: {hp - basic.Health:F2} dmg (expected {basicFinisher:F2}) and its own hit pause although the kick is light ({hero.LastKickForce:F0} N.s).");
        Check(hero.LastKickForce < s.Force, "CONTROL: basic-melee finisher stays weaker than a plain Strength punch.");
        Log("LIMIT: the E key's hold/tap timing itself (press -> release) needs hardware input; tests call the methods the key routes to. Feel of combo timing, charge curve and pound is not human-tested.");
    }
}
#endif
