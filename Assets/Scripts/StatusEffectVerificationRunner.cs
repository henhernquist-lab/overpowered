#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

/// See StatusEffectVerification. Audit of the NPC statuses (Freeze, Root, Poison; Lightning is instant, Burning a marker;
/// Force Field is the PLAYER's shield and deliberately not a status): expiry and overlap, cleanup when a status holder dies
/// or is despawned, pause, status stacking under a Lightning hit, and the one base interaction added (a dash consumes and
/// shatters an existing freeze, like melee), each with a control.
public sealed class StatusEffectVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/StatusEffects/";
    protected override string ResultFile => "results.txt";
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Enter(F.Heroes[0], "ice", "darkness", "hero");
        Isolate();
        yield return Expiry();
        yield return DeathCleanup();
        yield return Despawn();
        yield return Pause();
        yield return Enter(F.Heroes[0], "speed", "lightning", "hero");
        Isolate();
        yield return DashShatter();
        yield return LightningStacking();
        Log("LIMIT: the ended-session poison guard (Poisoned stops ticking once GameModeSession.Ended) is code-reviewed, not driven: the results scene load removes the NPCs within the same few frames.");
    }
    IEnumerator Expiry()
    {
        Log("---- EXPIRY AND OVERLAP");
        var a = Actor(new Vector3(-4, 150, 10), NpcRole.Criminal, 1000f);
        a.Freeze(1f); a.Root(1f);
        Check(a.Frozen && a.Rooted, "Freeze and root applied.");
        yield return new WaitForSeconds(1.15f);
        Check(!a.Frozen && !a.Rooted, "Both expire after their 1 s duration (timestamps, nothing to clean up).");
        a.Freeze(.5f); a.Root(1.5f);
        yield return new WaitForSeconds(.75f);
        Check(!a.Frozen && a.Rooted, "Overlap: the shorter freeze ends, the longer root still holds.");
        a.Freeze(.3f); a.Freeze(1.2f);
        yield return new WaitForSeconds(.5f);
        Check(a.Frozen, "CONTROL: a second, longer freeze extends (never shortens) the first.");
    }
    IEnumerator DeathCleanup()
    {
        Log("---- DEATH CLEANUP");
        var ice = Runtime("ice"); var frozen = Actor(new Vector3(0, 150, 9), NpcRole.Criminal, 1000f);
        yield return new WaitForSeconds(.2f);
        Aim(Chest(frozen)); Check(W.Powers.Use(ice) && frozen.Frozen, "Real Ice cast freezes a criminal.");
        var look = frozen.GetComponent<FrozenLook>(); yield return null;
        Check(look != null && look.Shown, "Frozen look shown.");
        frozen.Damage(99999f, W.Powers); yield return null; yield return null;
        Check(frozen.Dead && !look.Shown, "Killed while frozen: the frozen look restores the original materials.");
        var darkness = Runtime("darkness"); var rooted = Actor(new Vector3(3, 150, 9), NpcRole.Criminal, 1000f);
        W.Powers.Select(darkness); yield return new WaitForSeconds(.2f);
        Aim(Chest(rooted)); Check(W.Powers.Use(darkness) && rooted.Rooted, "Real Darkness cast roots a criminal.");
        var tendrils = rooted.GetComponent<RootedLook>(); yield return null;
        Check(tendrils != null && tendrils.Showing, "Root tendrils showing.");
        rooted.Damage(99999f, W.Powers); yield return null;
        Check(!tendrils.Showing && !tendrils.enabled, "Killed while rooted: the tendril look switches itself off.");
        var poison = (PoisonEffect)Power("poison").Effect;
        var sick = Actor(new Vector3(-8, 150, 14), NpcRole.Criminal, 1000f); var near = Actor(new Vector3(-7, 150, 14), NpcRole.Criminal, 1000f);
        int spreads = 0; Action<CityNpc, CityNpc> count = (from, to) => { if (from == sick) spreads++; }; Poisoned.Spreading += count;
        var p = Poisoned.Apply(sick, W.Powers, poison, 5f, 4f, 0, CityColor.Leaf);
        yield return new WaitForSeconds(poison.TickSeconds + .1f);
        Check(p.Active && p.Ticks >= 1, "Poison ticking.");
        sick.Damage(99999f, W.Powers); yield return null; int first = spreads;
        sick.Damage(5f, W.Powers); yield return null;
        Check(!p.Active && !p.enabled && first >= 1 && spreads == first, $"Killed while poisoned: poison stops and spreads once ({first} neighbour(s)); a hit on the corpse spreads nothing more.");
        Poisoned.Spreading -= count;
        var spreadTo = near.GetComponent<Poisoned>();
        Check(spreadTo != null && spreadTo.Active && spreadTo.Generation == 1, "The neighbour received a generation-1 poison.");
    }
    IEnumerator Despawn()
    {
        Log("---- DESPAWN");
        var poison = (PoisonEffect)Power("poison").Effect;
        var gone = Actor(new Vector3(8, 150, 14), NpcRole.Criminal, 1000f); var near = Actor(new Vector3(9, 150, 14), NpcRole.Criminal, 1000f);
        int spreads = 0; Action<CityNpc, CityNpc> count = (from, to) => spreads++; Poisoned.Spreading += count;
        Poisoned.Apply(gone, W.Powers, poison, 5f, 4f, 0, CityColor.Leaf); gone.Freeze(4f); gone.Root(4f);
        Destroy(gone.gameObject);
        yield return new WaitForSeconds(poison.TickSeconds * 2f + .1f);
        Poisoned.Spreading -= count;
        Check(gone == null && spreads == 0 && near.GetComponent<Poisoned>() == null, "A poisoned + frozen + rooted NPC despawned (destroyed without dying): no spread, no errors, nothing left behind.");
    }
    IEnumerator Pause()
    {
        Log("---- PAUSE");
        var poison = (PoisonEffect)Power("poison").Effect;
        var npc = Actor(new Vector3(12, 150, 18), NpcRole.Criminal, 1000f);
        var p = Poisoned.Apply(npc, W.Powers, poison, 5f, 6f, 0, CityColor.Leaf); npc.Freeze(2f);
        yield return new WaitForSeconds(poison.TickSeconds + .1f);
        W.Mode.SetPaused(true); int ticks = p.Ticks; float health = npc.Health;
        yield return new WaitForSecondsRealtime(1.5f);
        Check(p.Ticks == ticks && npc.Health == health && npc.Frozen, $"Paused 1.5 s (real time): no poison ticks ({ticks}), no damage, the freeze does not run out.");
        W.Mode.SetPaused(false);
        yield return new WaitForSeconds(poison.TickSeconds * 2f + .1f);
        Check(p.Ticks > ticks, "CONTROL: resumed, the poison ticks again.");
    }
    IEnumerator DashShatter()
    {
        Log("---- BASE INTERACTION: A DASH SHATTERS A FREEZE");
        var speed = Runtime("speed"); var stats = W.Powers.Stats(speed); var ice = (IceEffect)Power("ice").Effect;
        W.Powers.Select(speed);
        var frozen = Actor(new Vector3(0, 150, 2.5f), NpcRole.Criminal, 1000f); var plain = Actor(new Vector3(.2f, 150, 4.2f), NpcRole.Criminal, 1000f);
        yield return new WaitForSeconds(.3f);
        frozen.Freeze(5f); yield return null;
        Aim(W.Hero.transform.position + new Vector3(0, 1.5f, 20));
        var trail = W.Hero.GetComponentInChildren<DashTrail>(); int before = trail != null ? trail.LastShatters : 0;
        Check(W.Powers.Use(speed), "Dash through a frozen and an unfrozen criminal.");
        yield return new WaitForSeconds(stats.Duration + .3f);
        trail = W.Hero.GetComponentInChildren<DashTrail>();
        float frozenTook = 1000f - frozen.Health, plainTook = 1000f - plain.Health;
        Log($"MEASURED dash: frozen took {frozenTook:F1}, unfrozen took {plainTook:F1} (dash {stats.Damage:F1}, shatter {ice.ShatterDamage}).");
        Check(Mathf.Abs(frozenTook - (stats.Damage + ice.ShatterDamage)) < .01f && !frozen.Frozen && trail.LastShatters - before == 1, "The frozen criminal takes dash + shatter damage and is thawed (freeze consumed).");
        Check(Mathf.Abs(plainTook - stats.Damage) < .01f, "CONTROL: the unfrozen criminal takes the plain dash damage.");
    }
    IEnumerator LightningStacking()
    {
        Log("---- STACKING UNDER A LIGHTNING HIT");
        var lightning = Runtime("lightning"); W.Powers.Select(lightning);
        var poison = (PoisonEffect)Power("poison").Effect;
        var npc = Actor(new Vector3(-3, 150, 9), NpcRole.Criminal, 5000f);
        yield return new WaitForSeconds(.2f);
        npc.Freeze(3f); npc.Root(3f); var p = Poisoned.Apply(npc, W.Powers, poison, 5f, 3f, 0, CityColor.Leaf);
        float health = npc.Health;
        Aim(Chest(npc)); Check(W.Powers.Use(lightning), "Lightning at a frozen, rooted, poisoned criminal.");
        Check(npc.Frozen && npc.Rooted && p.Active && Mathf.Abs(health - npc.Health - LightningEffect.LastDamages[0]) < .01f, "Lightning deals its first-arc damage and leaves freeze, root and poison in place (statuses stack independently).");
    }
}
#endif
