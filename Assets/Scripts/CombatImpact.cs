using System.Collections.Generic;
using UnityEngine;

public static class CombatImpact
{
    public static int Blast(PowerUser source, Vector3 origin, float radius, float impulse, float damage, float lift,float burnSeconds=0,bool displaceNpcs=false)
    {
        var bodies = new HashSet<Rigidbody>(); var npcs = new HashSet<CityNpc>();
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
            var npc = hit.GetComponentInParent<CityNpc>();
            if (npc != null && npcs.Add(npc))
            {
                if(displaceNpcs&&!npc.Dead)
                {
                    var suspension=npc.GetComponent<SynergySuspension>()??npc.gameObject.AddComponent<SynergySuspension>();
                    var displaced=suspension.Begin(npc);displaced.useGravity=true;suspension.Release();
                }
                if(burnSeconds>0)npc.MarkBurn(burnSeconds);npc.Damage(damage, source);
            }
            Rigidbody body = hit.attachedRigidbody;
            if (body == null || body.isKinematic || !bodies.Add(body)) continue;
            body.AddExplosionForce(impulse, origin, radius, lift, ForceMode.Impulse);
            body.GetComponent<BreakableProp>()?.TakeDamage(damage, source);
        }
        WorldSession.Instance?.Alarm(origin);
        FeelDirector.Impact(origin, impulse, damage, bodies.Count + npcs.Count);   // feel only (particles / heavy-hit pause + camera)
        return bodies.Count;
    }
}
