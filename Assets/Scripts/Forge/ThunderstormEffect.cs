using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

/// THUNDERSTORM (Primary: Lightning) — sustained localized area punishment. Summons an overhead storm cloud
/// and danger ring over a targeted combat area, then strikes enemies remaining inside the zone in PulseCount
/// distinct lightning pulses. Mechanically distinct from ordinary chain Lightning: location-based area pressure
/// that enemies can escape by leaving the radius.
[CreateAssetMenu(menuName = "Overpowered/Forge/Signature Effects/Thunderstorm")]
public sealed class ThunderstormEffect : SynergyEffect
{
    public int PulseCount = 4;
    public float CloudHeight = 8.5f;
    public float WarningLeadSeconds = .4f;
    public float BriefStaggerSeconds = .22f;

    public static int LastPulsesCompleted { get; private set; }
    public static int LastTotalPulseHits { get; private set; }
    public static readonly List<CityNpc> LastStruckAnyPulse = new List<CityNpc>();
    static readonly RaycastHit[] barriers = new RaycastHit[16];

    public override bool CanBegin(SynergyRunner r)
    {
        if (r.User == null) return false;
        Vector3 point;
        if (r.User.FindTarget(r.Definition.Range, out var hit))
        {
            var npc = hit.collider.GetComponentInParent<CityNpc>();
            point = npc != null ? npc.transform.position : hit.point;
        }
        else if (!r.GroundTarget(true, out point))
        {
            return false;
        }

        if (Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var groundHit, 15f, ~0, QueryTriggerInteraction.Ignore))
            point = groundHit.point;
        if (NavMesh.SamplePosition(point, out var navHit, 4f, NavMesh.AllAreas))
            point = navHit.position;

        r.Target = point;
        return true;
    }

    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition;
        Vector3 center = r.Target;
        Vector3 sky = center + Vector3.up * CloudHeight;
        LastPulsesCompleted = 0;
        LastTotalPulseHits = 0;
        LastStruckAnyPulse.Clear();

        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        int pulses = Mathf.Max(2, PulseCount);
        float totalDuration = Mathf.Max(1.2f, d.Duration);
        float lead = Mathf.Min(WarningLeadSeconds, totalDuration * .25f);
        float interval = (totalDuration - lead) / Mathf.Max(1, pulses - 1);
        float damagePerPulse = r.ActivationDamage / pulses;

        var sigVfx = SignatureVfx.Get();
        var powerVfx = PowerVfx.Get();
        sigVfx.ShowThunderstorm(center, d.Radius, CloudHeight, totalDuration + .3f);

        var assaulted = HashSetPool<CityNpc>.Get();
        try
        {
            float waited = 0f;
            while (waited < lead)
            {
                sigVfx.PulseThunderstorm(center, d.Radius, CloudHeight, false);
                powerVfx.Arc(sky + new Vector3(1.2f, 0f, 0f), sky + new Vector3(-1.2f, -.3f, .8f), 4, .25f, CityColor.Cyan, .07f, .14f);
                float step = Mathf.Min(.12f, lead - waited);
                yield return new WaitForSeconds(step);
                waited += step;
            }

            for (int p = 0; p < pulses; p++)
            {
                StrikePulse(r, center, sky, d.Radius, damagePerPulse, sigVfx, powerVfx, assaulted);
                LastPulsesCompleted++;

                if (p < pulses - 1)
                {
                    float remaining = interval;
                    while (remaining > 0f)
                    {
                        float step = Mathf.Min(.15f, remaining);
                        yield return new WaitForSeconds(step);
                        remaining -= step;
                        sigVfx.PulseThunderstorm(center, d.Radius, CloudHeight, false);
                    }
                }
            }
            yield return new WaitForSeconds(.12f);
        }
        finally
        {
            sigVfx.HideThunderstorm();
            HashSetPool<CityNpc>.Release(assaulted);
        }
    }

    void StrikePulse(SynergyRunner r, Vector3 center, Vector3 sky, float radius, float pulseDamage,
        SignatureVfx sigVfx, PowerVfx powerVfx, HashSet<CityNpc> assaulted)
    {
        var d = r.Definition;
        sigVfx.PulseThunderstorm(center, radius, CloudHeight, true);

        var mainBolt = powerVfx.Arc(sky, center + Vector3.up * .15f, 6, .35f, CityColor.Cream, .22f, .22f);
        if (mainBolt != null) mainBolt.sharedMaterial = CityMaterials.GetVfx(CityColor.Cream);
        for (int b = 0; b < 3; b++)
        {
            float a = (b * Mathf.PI * 2f / 3f) + LastPulsesCompleted * 1.1f;
            Vector3 branchEnd = center + new Vector3(Mathf.Cos(a) * radius * .6f, .1f, Mathf.Sin(a) * radius * .6f);
            var fork = powerVfx.Arc(sky, branchEnd, 5, .28f, CityColor.Cyan, .09f, .18f);
            if (fork != null) fork.sharedMaterial = CityMaterials.GetVfx(CityColor.Cyan);
        }

        FeelDirector.Instance?.Particles.Burst(center + Vector3.up * .25f, CityColor.Cream, 12);
        AudioDirector.Instance?.Play(AudioCue.Destruction, center);
        r.KickCamera();

        int count = Physics.OverlapSphereNonAlloc(center + Vector3.up * .8f, radius, r.Hits, ~0, QueryTriggerInteraction.Ignore);
        var struckThisPulse = HashSetPool<CityNpc>.Get();
        try
        {
            for (int i = 0; i < count; i++)
            {
                var col = r.Hits[i];
                if (col == null || col.transform.root == r.transform) continue;
                var npc = col.GetComponentInParent<CityNpc>();
                if (npc == null || npc.Dead || !npc.Hostile || !struckThisPulse.Add(npc)) continue;

                Vector3 rel = npc.transform.position - center;
                if (Mathf.Abs(rel.y) > 3.5f) continue;
                if (new Vector2(rel.x, rel.z).magnitude > radius + .35f) continue;

                Vector3 chest = npc.transform.position + Vector3.up * 1.1f;
                if (!ClearStrike(sky, chest, center + Vector3.up * 1f, r.transform)) continue;

                var targetBolt = powerVfx.Arc(sky, chest, 5, .24f, CityColor.Cream, .13f, .2f);
                if (targetBolt != null) targetBolt.sharedMaterial = CityMaterials.GetVfx(CityColor.Cream);

                bool firstAssault = assaulted.Add(npc);
                if (BriefStaggerSeconds > 0f) npc.Slow(.5f, BriefStaggerSeconds);
                npc.Damage(pulseDamage, r.User, firstAssault, d.Force);
                FeelDirector.Instance?.Particles.Burst(chest, CityColor.Cyan, 6);

                LastTotalPulseHits++;
                if (!LastStruckAnyPulse.Contains(npc)) LastStruckAnyPulse.Add(npc);
            }
        }
        finally
        {
            HashSetPool<CityNpc>.Release(struckThisPulse);
        }
    }

    static bool ClearStrike(Vector3 sky, Vector3 targetChest, Vector3 groundCenter, Transform ignoreRoot)
    {
        return ClearSegment(sky, targetChest, ignoreRoot) && ClearSegment(groundCenter, targetChest, ignoreRoot);
    }

    static bool ClearSegment(Vector3 from, Vector3 to, Transform ignoreRoot)
    {
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= .05f) return true;
        int hits = Physics.RaycastNonAlloc(from, delta / dist, barriers, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            var c = barriers[i].collider;
            if (c.attachedRigidbody != null || c.GetComponentInParent<CityNpc>() != null || c.transform.root == ignoreRoot)
                continue;
            return false;
        }
        return true;
    }
}
