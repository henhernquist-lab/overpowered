using UnityEngine;

/// DARKNESS — Shadow Tendrils. Roots the aimed NPC for the power's Duration (CityNpc.Root: navigation stopped, attack cycle
/// kept, so it is crowd control, not a stun) and deals the power's small Damage. NPC targets only; limited charges come from
/// the PowerDefinition like every other power.
[CreateAssetMenu(menuName = "Overpowered/Effects/Shadow tendrils")]
public sealed class DarknessEffect : PowerEffect
{
    [Header("Shadow tendrils (presentation)")]
    public int Tendrils = 4;
    public float TendrilHeight = 1.3f, TendrilSpread = .75f, TendrilWidth = .07f, TendrilJitter = .12f;
    [Tooltip("Tendrils are redrawn from the pooled lines this often while the root holds.")]
    public float RedrawSeconds = .3f;
    public int CastParticles = 10;
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var stats = user.Stats(power);
        if (!user.FindTarget(stats.Range, out RaycastHit hit)) return false;
        var npc = hit.collider.GetComponentInParent<CityNpc>();
        if (npc == null || npc.Dead) { user.Message = "Aim at a person to root."; return false; }
        npc.Root(stats.Duration);
        npc.Damage(stats.Damage, user);
        var color = power.Definition.PaletteColor;
        if (!npc.Dead)
        {
            RootedLook.Show(npc, this, color);
            // A brief aimed snap establishes the target; the leased body bands carry the held state.
            var snap=PowerVfx.Get().Arc(user.AimOrigin,npc.transform.position+Vector3.up,
                4,.045f,CityColor.UiMuted,.035f,.12f);
            if(snap!=null)snap.sharedMaterial=CityMaterials.GetVfx(CityColor.UiMuted);
        }
        FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * .15f, color, CastParticles);
        return true;
    }
}
/// Visible root: four closely wrapped, leased tendril bands. The gameplay root is unchanged.
public sealed class RootedLook : MonoBehaviour
{
    CityNpc npc; int slot=-1;
    public int Draws { get; private set; }
    public bool Showing => enabled && npc != null && !npc.Dead && npc.Rooted && slot>=0;
    public static RootedLook Show(CityNpc target, DarknessEffect settings, CityColor color)
    {
        var look=target.GetComponent<RootedLook>();if(look==null)look=target.gameObject.AddComponent<RootedLook>();
        look.npc=target;look.enabled=true;
        if(look.slot<0){look.slot=StatusPowerVfx.Get().AcquireRoot(target);if(look.slot>=0)look.Draws++;}
        return look;
    }
    void Update()
    {
        if(npc==null||npc.Dead||!npc.Rooted){enabled=false;return;}
        if(slot<0){slot=StatusPowerVfx.Get().AcquireRoot(npc);if(slot>=0)Draws++;}
    }
    void OnDisable()
    {
        if(slot>=0&&npc!=null&&StatusPowerVfx.Existing!=null)StatusPowerVfx.Existing.ReleaseRoot(npc,slot);
        slot=-1;
    }
}
