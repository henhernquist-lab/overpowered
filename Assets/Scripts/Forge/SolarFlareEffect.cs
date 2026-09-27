using System.Collections;
using UnityEngine;

/// SOLAR FLARE (Fire Blast + Laser Eyes): a short focusing beam from the eyes to the crosshair point, then that point
/// detonates — the shared SynergyRunner.Impact blast (Damage / Radius / Force / BurnSeconds from the synergy asset).
/// Same shape as Thermal Shock: aimed CanBegin, cast presentation, delayed Impact. Beam and bursts are pooled.
[CreateAssetMenu(menuName = "Overpowered/Forge/Effects/Solar flare")]
public sealed class SolarFlareEffect : SynergyEffect
{
    public float BeamWidth = .12f, BurstInterval = .1f;
    public override bool CanBegin(SynergyRunner r)
    {
        if (!r.User.FindTarget(r.Definition.Range, out var hit)) return false;
        r.Target = hit.point; return true;
    }
    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition; var vfx = PowerVfx.Get();
        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        float elapsed = 0, nextBurst = 0;
        while (elapsed < d.LiftSeconds)
        {
            vfx.ShowBeam(r.User.AimOrigin + (r.Target - r.User.AimOrigin).normalized * .3f, r.Target, d.Primary, BeamWidth);
            if (elapsed >= nextBurst) { r.Vfx.Burst(r.Target, d); nextBurst = elapsed + BurstInterval; }
            elapsed += Time.deltaTime; yield return null;
        }
        vfx.HideBeam();
        r.Impact(r.Target);
    }
}
