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
        if (!npc.Dead) RootedLook.Show(npc, this, color);
        FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up * .15f, color, CastParticles);
        return true;
    }
}
/// Visible root: tendrils from the ground to the NPC's waist, redrawn from the PowerVfx pool while CityNpc.Rooted holds.
public sealed class RootedLook : MonoBehaviour
{
    CityNpc npc; DarknessEffect settings; CityColor color; float nextDraw;
    public int Draws { get; private set; }
    public bool Showing => enabled && npc != null && !npc.Dead && npc.Rooted;
    public static RootedLook Show(CityNpc target, DarknessEffect settings, CityColor color)
    {
        var look = target.GetComponent<RootedLook>(); if (look == null) look = target.gameObject.AddComponent<RootedLook>();
        look.npc = target; look.settings = settings; look.color = color; look.nextDraw = 0f; look.enabled = true; return look;
    }
    void Update()
    {
        if (npc == null || npc.Dead || !npc.Rooted) { enabled = false; return; }
        if (Time.time < nextDraw) return;
        nextDraw = Time.time + settings.RedrawSeconds;
        var vfx = PowerVfx.Get(); Vector3 feet = npc.transform.position;
        for (int i = 0; i < settings.Tendrils; i++)
        {
            float a = (i + Random.value * .5f) * Mathf.PI * 2f / Mathf.Max(1, settings.Tendrils);
            Vector3 root = feet + new Vector3(Mathf.Cos(a), .02f, Mathf.Sin(a)) * settings.TendrilSpread;
            Vector3 grip = feet + Vector3.up * settings.TendrilHeight * (.6f + .4f * Random.value);
            vfx.Arc(root, grip, 4, settings.TendrilJitter, color, settings.TendrilWidth, settings.RedrawSeconds + .05f);
        }
        Draws++;
    }
}
