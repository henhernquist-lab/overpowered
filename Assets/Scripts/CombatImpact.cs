using System.Collections.Generic;
using UnityEngine;

public static class CombatImpact
{
    public static int Blast(PowerUser source, Vector3 origin, float radius, float impulse, float damage, float lift,float burnSeconds=0,bool displaceNpcs=false,bool melee=false)
    {
        var bodies = new HashSet<Rigidbody>(); var npcs = new HashSet<CityNpc>(); var missions = new HashSet<MissionTarget>();
        foreach (var hit in Physics.OverlapSphere(origin, radius))
        {
            if (hit.transform.root == source.transform) continue;
            // Buildings shield targets; only solid static geometry blocks this short area hit.
            bool blocked = false;
            Vector3 delta = hit.bounds.center - origin;
            foreach (var barrier in Physics.RaycastAll(origin, delta.normalized, delta.magnitude))
                if (barrier.collider != hit && barrier.rigidbody == null && barrier.collider.GetComponentInParent<CityNpc>() == null && barrier.transform.root != source.transform && !barrier.collider.isTrigger)
                    blocked = true;
            if (blocked) continue;
            var mission = hit.GetComponentInParent<MissionTarget>();   // mission objects (fire spot, vault) read area hits
            if (mission != null && missions.Add(mission)) mission.Hit(damage, impulse, source);
            var npc = hit.GetComponentInParent<CityNpc>();
            if (npc != null && npcs.Add(npc))
            {
                if(displaceNpcs&&!npc.Dead)
                {
                    var suspension=npc.GetComponent<SynergySuspension>()??npc.gameObject.AddComponent<SynergySuspension>();
                    var displaced=suspension.Begin(npc);displaced.useGravity=true;suspension.Release();
                }
                float bonus=melee?IceEffect.Shatter(source,npc,origin,radius):0;
                if(burnSeconds>0)npc.MarkBurn(burnSeconds);npc.Damage(damage+bonus, source);
                if(bonus>0)PreserveDeathLaunch(npc);
            }
            Rigidbody body = hit.attachedRigidbody;
            if (body == null || body.isKinematic || !bodies.Add(body)) continue;
            float propBonus=melee&&npc==null?IceEffect.Shatter(source,body,origin,radius):0;
            body.AddExplosionForce(impulse, origin, radius, lift, ForceMode.Impulse);
            body.GetComponent<BreakableProp>()?.TakeDamage(damage+propBonus, source);
        }
        WorldSession.Instance?.Alarm(origin);
        FeelDirector.Impact(origin, impulse, damage, bodies.Count + npcs.Count);   // feel only (particles / heavy-hit pause + camera)
        return bodies.Count;
    }
    // A lethal hit still gets its physical payoff during the existing death-presentation lifetime.
    // The normal suspension recovery is for living navigation agents; it would otherwise stop the corpse next frame.
    public static void PreserveDeathLaunch(CityNpc npc)
    {
        if(!npc.Dead)return;
        var suspension=npc.GetComponent<SynergySuspension>();
        if(suspension!=null)suspension.enabled=false;
        npc.GetComponent<Collider>().enabled=true;
    }
}
