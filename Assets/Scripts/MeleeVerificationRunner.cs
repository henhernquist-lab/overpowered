#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

/// See MeleeVerification. Uses TryComboAttack / TryHeavyAttack / TryGroundPound, the methods the E key routes to, on the
/// isolated floor with AI-off criminals of 1000 HP (damage is measured as health lost). Strength-equipped (VECTOR, speed +
/// strength) and basic-melee (fire + ice) sessions.
public sealed class MeleeVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/Melee/";
    protected override string ResultFile => "results.txt";
    MeleeSettings M => W.Tuning.Melee;
    IEnumerator EnterMelee(string attackA,string attackB,string secondary)
    {
        yield return Home();var hero=F.Heroes[0];Profile.Data.Points=100;
        foreach(var id in new[]{attackA,attackB,secondary})if(!Profile.Owns(Power(id)))
            Check(Profile.Buy(Power(id)),"Unlock melee-test power through progression: "+id);
        Check(Profile.SetLoadout(hero,Power(attackA),Power(attackB),Power(secondary),hero.Primary,hero.Secondary),
            $"Save two melee-test attacks ({attackA}, {attackB}) and {secondary} secondary through progression.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        Check(W.Powers.EquippedA==Power(attackA)&&W.Powers.EquippedB==Power(attackB)&&
            W.Powers.EquippedSecondary==Power(secondary),"Melee session retains exact two-attack/one-secondary loadout.");
    }
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return EnterMelee("strength", "fire", "speed");
        var m = M; var s = W.Powers.Stats(W.Powers.Strength); var hero = W.Hero;
        Log($"DATA melee: combo {m.ComboLength} hits / {m.ComboWindow} s window, finisher x{m.FinisherForceMultiplier} force x{m.FinisherDamageMultiplier} damage + {m.FinisherHitPauseSeconds} s pause; heavy tap<{m.HeavyTapThreshold} s, cap {m.HeavyMaxChargeSeconds} s -> x{m.HeavyMaxForceMultiplier}/x{m.HeavyMaxDamageMultiplier}; pound >= {m.PoundMinHeight} m, radius {m.PoundRadius}, x{m.PoundForceMultiplier}/x{m.PoundDamageMultiplier}. Strength: {s.Force} N.s, {s.Damage} dmg.");
        Isolate();
        var target = Actor(new Vector3(0, 150, 2.2f), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        // ---------------------------------------------------------------- combo string
        Log("---- COMBO (Strength equipped)");
        hero.DebugSetResources(6, 3, 0); float hp = target.Health;
        var pose=hero.GetComponent<HumanoidPresentation>();
        Check(m.ComboLength==2&&Mathf.Approximately(m.ComboWindow,.9f),"CONFIG: E combo is punch -> Martelo kick in the existing 0.9 s window.");
        Check(hero.TryComboAttack() && hero.LastComboStage == 1 && pose.State=="Punch", "Tap 1 = combo stage 1 (punch), never Martelo.");
        yield return new WaitForSeconds(.55f);
        Check(Mathf.Abs(hp - target.Health - s.Damage) < .01f && Mathf.Approximately(hero.LastForce, s.Force), $"Stage 1 punch: {hp - target.Health:F2} dmg @ {hero.LastForce} N.s (plain punch).");
        hp = target.Health; int pauses = TimeArbiter.HitPausesStarted;
        float priorKick=hero.LastComboKickImpactTime, priorHurricane=hero.LastKickImpactTime;
        Check(hero.TryComboAttack() && hero.LastComboStage == 2 && pose.State=="Martelo Kick" &&
            pose.Tuning.HurricaneKick!=pose.Tuning.MarteloKick, "Tap 2 inside window = distinct Martelo clip, not Hurricane.");
        yield return new WaitForSeconds(.8f);
        Check(hero.LastComboKickImpactTime>priorKick&&hero.LastKickImpactTime==priorHurricane,
            "Second tap caused Martelo impact after windup and no Hurricane gameplay impact.");
        float finisherForce = s.Force * HeroAbilityTuning.KickForceMultiplier * m.FinisherForceMultiplier, finisherDamage = s.Damage * m.FinisherDamageMultiplier;
        Check(Mathf.Abs(hero.LastComboKickForce - finisherForce) < .5f && Mathf.Abs(hp - target.Health - finisherDamage) < .05f,
            $"Finisher: {hero.LastComboKickForce:F0} N.s (kick x{m.FinisherForceMultiplier}), {hp - target.Health:F2} dmg (expected {finisherDamage:F2}).");
        Check(TimeArbiter.HitPausesStarted > pauses, "Finisher impact produced a hit pause.");
        hero.DebugSetResources(6, 3, 0);
        Check(hero.TryComboAttack() && hero.LastComboStage == 1 && pose.State=="Punch", "After the finisher the next tap restarts at a punch.");
        yield return new WaitForSeconds(m.ComboWindow + .3f); hero.DebugSetResources(6, 3, 0);
        Check(hero.TryComboAttack() && hero.LastComboStage == 1 && pose.State=="Punch", $"CONTROL: a tap after the {m.ComboWindow} s window restarts at punch (not Martelo).");
        yield return new WaitForSeconds(.55f); hero.DebugSetResources(6, 0, 0); int stage = hero.LastComboStage; hp = target.Health;
        Check(!hero.TryComboAttack() && hero.LastComboStage == stage, "CONTROL: a tap with 0 Strength charges is refused and does not advance the string.");
        yield return new WaitForSeconds(.6f);
        Check(target.Health == hp, "CONTROL: the refused tap dealt nothing.");
        hero.DebugSetResources(6,3,0);yield return new WaitForSeconds(.55f);
        float priorCombo=hero.LastComboKickImpactTime;priorHurricane=hero.LastKickImpactTime;
        Check(hero.TryHurricaneKick()&&pose.State=="Hurricane Kick"&&pose.Tuning.HurricaneKick.name.Contains("Hurricane"),
            "RMB path starts original spinning Hurricane clip without a prior punch.");
        yield return new WaitForSeconds(.6f);
        Check(hero.LastKickImpactTime>priorHurricane&&hero.LastComboKickImpactTime==priorCombo,
            "RMB produces Hurricane gameplay impact without a Martelo/basic-combo impact.");
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
        yield return EnterMelee("fire", "ice", "speed");
        Isolate(); hero = W.Hero; var basic = Actor(new Vector3(0, 150, 2.2f), NpcRole.Criminal, 1000);
        yield return new WaitForSeconds(.3f);
        float bd = (m.BasicPunchDamage.x+m.BasicPunchDamage.y)*.5f, bf = F.BasicForce;
        hp = basic.Health; pauses = TimeArbiter.HitPausesStarted;
        Check(hero.TryComboAttack() && hero.LastComboStage == 1, "Basic tap 1 = punch without Strength.");
        yield return new WaitForSeconds(.75f);
        Check(Mathf.Abs(hp - basic.Health - bd) < .01f && TimeArbiter.HitPausesStarted == pauses, $"Basic punch: {hp - basic.Health:F2} dmg, no hit pause (light {bf} N.s, CONTROL).");
        hp = basic.Health; pauses = TimeArbiter.HitPausesStarted; int finisherPauses = hero.FinisherPauses;
        Check(hero.TryComboAttack() && hero.LastComboStage == 2, "Basic tap 2 = Martelo finisher.");
        yield return new WaitForSeconds(.9f);
        float basicFinisher = (m.BasicComboDamage.x+m.BasicComboDamage.y)*.5f;
        Check(Mathf.Abs(hp - basic.Health - basicFinisher) < .05f && hero.FinisherPauses == finisherPauses + 1 && TimeArbiter.HitPausesStarted == pauses + 1,
            $"Basic finisher: {hp - basic.Health:F2} dmg (expected {basicFinisher:F2}) and its own hit pause although the kick is light ({hero.LastComboKickForce:F0} N.s).");
        Check(hero.LastComboKickForce < s.Force, "CONTROL: basic-melee finisher stays weaker than a plain Strength punch.");
        Log("LIMIT: the E key's hold/tap timing itself (press -> release) needs hardware input; tests call the methods the key routes to. Feel of combo timing, charge curve and pound is not human-tested.");
    }
}
#endif
