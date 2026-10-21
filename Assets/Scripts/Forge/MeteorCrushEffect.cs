using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[CreateAssetMenu(menuName = "Overpowered/Forge/Signature Effects/Meteor Crush")]
public sealed class MeteorCrushEffect : SynergyEffect
{
    public float Height = 8f, AssembleSeconds = 1.05f, FallSeconds = .22f, DustSeconds = .65f;
    public float DirectRadius = 2.6f, ShockDamageFraction = .28f, StaggerSeconds = .75f, BruteStaggerSeconds = .3f;
    public static readonly List<CityNpc> LastTargets = new List<CityNpc>(16);
    public override bool CanBegin(SynergyRunner r)
    {
        if (!SignatureTargeting.Ground(r)) return false;
        // The entire visual mass needs overhead clearance. Reject ceilings instead of spawning underground/inside roofs.
        return SignatureTargeting.Clear(r.Target + Vector3.up * .15f, r.Target + Vector3.up * (Mathf.Clamp(Height, 4f, 15f) + 2f), r.transform, true) &&
            !Physics.CheckBox(r.Target + Vector3.up * Mathf.Clamp(Height, 4f, 15f), Vector3.one * 2.2f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
    }
    public override IEnumerator Execute(SynergyRunner r)
    {
        var d = r.Definition; Vector3 center = r.Target; var fx = SignatureVfx.Get();
        LastTargets.Clear(); r.User.GetComponent<HumanoidPresentation>()?.Cast();
        float assemble = Mathf.Clamp(AssembleSeconds, .3f, 2f), fall = Mathf.Clamp(FallSeconds, .1f, .6f);
        try
        {
            float elapsed = 0;
            while (elapsed < assemble + fall)
            {
                float progress = elapsed < assemble ? 0 : Mathf.Clamp01((elapsed - assemble) / fall);
                fx.ShowMeteor(center, Mathf.Clamp(Height, 4f, 15f), Mathf.Clamp01(elapsed / assemble), progress, -1, d.Radius);
                elapsed += Time.deltaTime; yield return null;
            }
            // One authoritative impact pass, one hit per NPC even with overlapping colliders. No meteor collider exists.
            SignatureTargeting.Gather(r, center, d.Radius, LastTargets, d.MaxTargets);
            foreach (var npc in LastTargets)
            {
                float distance = Vector2.Distance(new Vector2(center.x, center.z), new Vector2(npc.transform.position.x, npc.transform.position.z));
                bool brute = SignatureTargeting.Brute(npc);
                npc.Freeze(brute ? BruteStaggerSeconds : StaggerSeconds, false);
                npc.Damage(r.ActivationDamage * (distance <= DirectRadius ? 1f : Mathf.Clamp01(ShockDamageFraction)), r.User, true, d.Force * (brute ? .5f : 1f));
            }
            ImpactProps(r, center);
            r.Vfx.Burst(center + Vector3.up * .2f, d); r.KickCamera();
            FeelDirector.Instance?.Particles.Burst(center + Vector3.up * .5f, CityColor.Sand, 32);
            AudioDirector.Instance?.Play(AudioCue.Destruction, center);
            elapsed = 0; float dust = Mathf.Clamp(DustSeconds, .2f, 1.5f);
            while (elapsed < dust)
            {
                fx.ShowMeteor(center, Height, 1, 1, elapsed / dust, d.Radius);
                elapsed += Time.deltaTime; yield return null;
            }
        }
        finally { fx.HideMeteor(); }
    }
    void ImpactProps(SynergyRunner r, Vector3 center)
    {
        var seen = HashSetPool<Rigidbody>.Get();
        try
        {
            int count = Physics.OverlapSphereNonAlloc(center + Vector3.up, r.Definition.Radius, r.Hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count && seen.Count < 32; i++)
            {
                var col = r.Hits[i]; var body = col.attachedRigidbody;
                if (body == null || col.transform.root == r.transform || col.GetComponentInParent<CityNpc>() != null || !seen.Add(body) ||
                    !SignatureTargeting.Clear(center + Vector3.up, col.bounds.center, r.transform)) continue;
                if (!body.isKinematic) body.AddExplosionForce(Mathf.Min(r.Definition.Force, 1400), center, r.Definition.Radius, .5f, ForceMode.Impulse);
                body.GetComponent<BreakableProp>()?.TakeDamage(r.ActivationDamage, r.User);
            }
        }
        finally { HashSetPool<Rigidbody>.Release(seen); }
    }
}
