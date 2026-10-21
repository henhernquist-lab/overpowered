using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Overpowered/Forge/Signature Effects/Abyssal Grasp")]
public sealed class AbyssalGraspEffect : SynergyEffect
{
    public float PullSeconds = 2f, PullSpeed = 4.5f, HoldRadius = 1.25f, BrutePullScale = .4f, BruteHoldSeconds = .5f;
    public static readonly List<CityNpc> LastTargets = new List<CityNpc>(8);
    public override bool CanBegin(SynergyRunner r)
    {
        if (!SignatureTargeting.Ground(r)) return false;
        return SignatureTargeting.Gather(r, r.Target, r.Definition.Radius, LastTargets, r.Definition.MaxTargets) > 0;
    }
    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition; Vector3 center = r.Target; var fx = SignatureVfx.Get();
        SignatureTargeting.Gather(r, center, d.Radius, LastTargets, Mathf.Min(d.MaxTargets, 8));
        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        float duration = Mathf.Clamp(d.Duration, .5f, 6f), pull = Mathf.Clamp(PullSeconds, .25f, duration), elapsed = 0, nextParticle = 0;
        try
        {
            while (elapsed < duration)
            {
                fx.ShowAbyss(center, d.Radius, elapsed / duration);
                for (int i = 0; i < LastTargets.Count; i++)
                {
                    var npc = LastTargets[i];
                    if (npc == null || npc.Dead || !npc.Hostile || Vector3.Distance(npc.transform.position, center) > d.Radius + 1f ||
                        !SignatureTargeting.Clear(center + Vector3.up, npc.transform.position + Vector3.up, r.transform)) { fx.HideTendril(i); continue; }
                    bool brute = SignatureTargeting.Brute(npc);
                    if (elapsed < pull)
                    {
                        float angle = i * Mathf.PI * 2 / LastTargets.Count;
                        Vector3 slot = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Mathf.Clamp(HoldRadius, .75f, 2f);
                        Vector3 desired = Vector3.MoveTowards(npc.transform.position, slot, Mathf.Clamp(PullSpeed, .5f, 8f) * (brute ? Mathf.Clamp01(BrutePullScale) : 1f) * Mathf.Min(Time.deltaTime, .05f));
                        SignatureTargeting.MoveOnGround(npc, desired, r.transform);
                    }
                    // Short leases expire even on interruption/death/session changes, without clearing another power's root.
                    if (elapsed < pull + (brute ? Mathf.Clamp(BruteHoldSeconds, 0, 1f) : duration)) npc.Freeze(.18f, false);
                    fx.ShowTendril(i, center, npc.transform.position + Vector3.up * .6f, elapsed);
                }
                if (elapsed >= nextParticle)
                {
                    FeelDirector.Instance?.Particles.Burst(center + Vector3.up * .3f, CityColor.UiPurple, 5); nextParticle += .3f;
                }
                elapsed += Time.deltaTime; yield return null;
            }
            foreach (var npc in LastTargets)
                if (npc != null && !npc.Dead && npc.Hostile && Vector3.Distance(npc.transform.position, center) <= d.Radius &&
                    SignatureTargeting.Clear(center + Vector3.up, npc.transform.position + Vector3.up, r.transform))
                    npc.Damage(r.ActivationDamage, r.User, true, d.Force);
            r.Vfx.Burst(center + Vector3.up * .5f, d); r.KickCamera();
            FeelDirector.Instance?.Particles.Burst(center + Vector3.up * .7f, CityColor.UiPurple, 24);
            yield return new WaitForSeconds(.15f);
        }
        finally { fx.HideAbyss(); }
    }
}
