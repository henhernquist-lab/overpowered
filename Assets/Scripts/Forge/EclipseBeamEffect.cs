using System.Collections;
using UnityEngine;

/// ECLIPSE BEAM (Darkness + Laser Eyes): a single-target finisher. The aimed NPC is wrapped in a darkness field — rooted for the
/// whole cast (CityNpc.Root) with pooled tendril arcs — for LiftSeconds, then the eye beam fires THROUGH the field for Duration
/// and delivers one huge hit of Damage to that target only (no area blast; bystanders are never touched). Needs a live NPC
/// under the crosshair (refused otherwise, with no cooldown).
[CreateAssetMenu(menuName = "Overpowered/Forge/Effects/Eclipse beam")]
public sealed class EclipseBeamEffect : SynergyEffect
{
    public float BeamWidth = .16f, FieldRadius = 1.2f, FieldInterval = .1f;
    public int FieldTendrils = 5;
    /// Damage dealt by the most recent completed cast (tests read it).
    public static float LastDamage { get; private set; }
    public override bool CanBegin(SynergyRunner r)
    {
        if (!r.User.FindTarget(r.Definition.Range, out var hit)) return false;
        var npc = hit.collider.GetComponentInParent<CityNpc>();
        if (npc == null || npc.Dead) return false;
        r.Captive = npc; return true;
    }
    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition; var vfx = PowerVfx.Get(); var target = r.Captive; LastDamage = 0;
        r.User.GetComponent<HumanoidPresentation>()?.Cast();
        target.Root(d.LiftSeconds + d.Duration + .2f);
        float elapsed = 0, nextFx = 0;
        while (elapsed < d.LiftSeconds + d.Duration && target != null && !target.Dead)
        {
            Vector3 feet = target.transform.position, chest = feet + Vector3.up * 1.2f;
            if (elapsed >= nextFx)
            {
                for (int i = 0; i < FieldTendrils; i++)
                {
                    float a = i * Mathf.PI * 2f / FieldTendrils + elapsed * 3f;
                    vfx.Arc(feet + new Vector3(Mathf.Cos(a), .05f, Mathf.Sin(a)) * FieldRadius, chest, 4, .15f, d.Secondary, .06f, FieldInterval + .04f);
                }
                nextFx = elapsed + FieldInterval;
            }
            if (elapsed >= d.LiftSeconds) vfx.ShowBeam(r.User.AimOrigin + (chest - r.User.AimOrigin).normalized * .3f, chest, d.Primary, BeamWidth);
            elapsed += Time.deltaTime; yield return null;
        }
        vfx.HideBeam();
        if (target != null && !target.Dead)
        {
            Vector3 chest = target.transform.position + Vector3.up * 1.2f;
            float before = target.Health; target.Damage(d.Damage, r.User); LastDamage = before - target.Health;
            r.Vfx.Burst(chest, d); r.KickCamera();
            FeelDirector.Impact(chest, d.Force, d.Damage, 1);
            AudioDirector.Instance?.Play(AudioCue.Destruction, chest);
        }
        r.Captive = null;
    }
}
