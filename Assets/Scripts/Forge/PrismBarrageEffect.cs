using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[CreateAssetMenu(menuName = "Overpowered/Forge/Signature Effects/Prism Barrage")]
public sealed class PrismBarrageEffect : SynergyEffect
{
    public float ChargeSeconds = .24f, BranchInterval = .065f, Falloff = .78f, AfterglowSeconds = .28f;
    public static readonly List<CityNpc> LastTargets = new List<CityNpc>(8);
    public override bool CanBegin(SynergyRunner r)
    {
        if (!SignatureTargeting.Aim(r, out var hit)) return false;
        r.Captive = hit.collider.GetComponentInParent<CityNpc>();
        r.Target = hit.point;
        return r.Captive != null && !r.Captive.Dead && r.Captive.Hostile;
    }
    public override IEnumerator Execute(SynergyRunner r)
    {
        var first = r.Captive; var d = r.Definition; var fx = SignatureVfx.Get();
        var targets = ListPool<CityNpc>.Get();
        LastTargets.Clear();
        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        FeelDirector.Instance?.Particles.Burst(r.Target, CityColor.Cream, 8);
        try
        {
            yield return new WaitForSeconds(Mathf.Clamp(ChargeSeconds, .05f, 1f));
            if (first == null || first.Dead || !first.Hostile) yield break;
            Vector3 hub = first.transform.position + Vector3.up * 1.1f;
            if (Vector3.Distance(r.User.AimOrigin, hub) > d.Range || !SignatureTargeting.Clear(r.User.AimOrigin, hub, r.transform, true)) yield break;
            // Optical refraction is a fan anchored at the first impact, not an electrical chain.
            SignatureTargeting.Gather(r, first.transform.position, d.Radius, targets, 16);
            targets.Remove(first); targets.Insert(0, first);
            int limit = Mathf.Min(Mathf.Clamp(d.MaxTargets, 1, 8), targets.Count);
            for (int i = 0; i < limit; i++)
            {
                var npc = targets[i];
                if (npc == null || npc.Dead || !npc.Hostile) continue;
                Vector3 chest = npc.transform.position + Vector3.up * 1.1f;
                if (i > 0 && (Vector3.Distance(chest, hub) > d.Radius || !SignatureTargeting.Clear(hub, chest, r.transform))) continue;
                Vector3 from = i == 0 ? r.User.AimOrigin : hub;
                if (i == 0)
                {
                    // Keep all near-eye geometry beyond the camera near plane, even at point blank range.
                    var camera = Camera.main;
                    float inset = camera != null ? Mathf.Max(.35f, camera.nearClipPlane + .15f) : .35f;
                    from += (chest - from).normalized * Mathf.Min(inset, Vector3.Distance(from, chest) * .5f);
                }
                fx.ShowPrism(i, from, chest, i == 0, AfterglowSeconds + BranchInterval * (limit - i));
                LastTargets.Add(npc);
                npc.Damage(r.ActivationDamage * Mathf.Pow(Mathf.Clamp01(Falloff), i), r.User, true, d.Force);
                FeelDirector.Instance?.Particles.Burst(chest, i == 0 ? CityColor.Cream : CityColor.Cyan, 8);
                if (i == 0) r.KickCamera();
                yield return new WaitForSeconds(Mathf.Clamp(BranchInterval, .02f, .2f));
            }
            yield return new WaitForSeconds(Mathf.Clamp(AfterglowSeconds, .05f, 1f));
        }
        finally { fx.HidePrism(); ListPool<CityNpc>.Release(targets); }
    }
}
