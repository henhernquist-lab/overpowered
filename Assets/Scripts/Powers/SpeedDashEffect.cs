using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// SPEED — a short combat burst dash, distinct from Flight (sustained, fuel, vertical traversal). Range = dash distance,
/// Duration = dash time, both through SuperHeroController.Dash (CharacterController only). Direction: the movement input,
/// or the flattened aim when standing still. NPC capsules are passed through; each HOSTILE NPC the dash passes takes the
/// power's Damage once. Several charges with a short cooldown make it a tempo tool rather than a travel tool.
[CreateAssetMenu(menuName = "Overpowered/Effects/Burst dash")]
public sealed class SpeedDashEffect : PowerEffect
{
    [Header("Dash")]
    public float PassRadius = 1.1f;
    public int StepParticles = 2, PassParticles = 8;
    public float TrailSeconds = .25f, TrailWidth = .35f;
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        var hero = user.Hero;
        if (hero == null || hero.Dashing) return false;
        var stats = user.Stats(power); var color = power.Definition.PaletteColor;
        Vector3 direction = hero.MoveInput.sqrMagnitude > .01f ? hero.MoveInput : Vector3.Scale(user.AimDirection, new Vector3(1, 0, 1));
        if (direction.sqrMagnitude < .0001f) direction = hero.transform.forward;
        var trail = DashTrail.For(hero, color, TrailSeconds, TrailWidth);
        // Per-hero reusable state and a cached step delegate: no collection or closure is allocated per dash (a dash cannot
        // start while one is running, so the set is never shared between two live dashes).
        trail.Begin(user, this, user.RollDamage(stats.Damage), color);
        trail.Emitting = true;
        bool started = hero.Dash(direction, stats.Range, stats.Duration, trail.Step);
        if (!started) { trail.Emitting = false; return false; }
        LastPassed = trail.Passed;
        return true;
    }
    /// Hostile NPCs hit by the most recent dash (filled while that dash runs).
    public static HashSet<CityNpc> LastPassed { get; private set; } = new HashSet<CityNpc>();
}
/// Fixed short side-streaks sampled from actual CharacterController displacement. Speed stays visibly continuous,
/// including in first person, without a wide camera-filling TrailRenderer ribbon or a fake blink.
public sealed class DashTrail : MonoBehaviour
{
    const int PairCount=6;
    readonly LineRenderer[] streaks=new LineRenderer[PairCount*2];
    readonly float[] expires=new float[PairCount*2];
    SuperHeroController hero;
    PowerUser user; SpeedDashEffect settings; float damage; CityColor color; string credit;
    float streakSeconds,streakWidth,nextEmit;
    int cursor;
    bool emitting;
    Vector3 previous;
    readonly Collider[] nearby = new Collider[32];
    /// Hostile NPCs clipped by the current (or last) dash.
    public readonly HashSet<CityNpc> Passed = new HashSet<CityNpc>();
    /// Frozen enemies shattered by dashes through this trail (verification).
    public int LastShatters { get; private set; }
    public int StreakCount { get; private set; }
    public int RendererCount=>streaks[0]==null?0:streaks.Length;
    public Vector3 LastVisualDirection {get;private set;}
    public System.Action Step { get; private set; }
    public void Begin(PowerUser owner, SpeedDashEffect effect, float hitDamage, CityColor tint)
    {
        user = owner; settings = effect; damage = hitDamage; color = tint;
        credit = owner != null ? owner.Crediting : null; Passed.Clear();
        previous=hero.transform.position;nextEmit=0;LastVisualDirection=Vector3.zero;
        if (Step == null) Step = OnStep;
    }
    void OnStep()
    {
        if (hero == null || settings == null) return;
        VisualStep();
        Vector3 centre = hero.transform.position + Vector3.up * .9f;
        int count = Physics.OverlapSphereNonAlloc(centre, settings.PassRadius, nearby, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var npc = nearby[i].GetComponentInParent<CityNpc>();
            if (npc == null || npc.Dead || !npc.Hostile || !Passed.Add(npc)) continue;
            // Base interaction (same rule as melee): a physical hit consumes an existing freeze and shatters it for Ice's
            // ShatterDamage. Not a synergy: any freeze source counts, and no pair has to be equipped.
            float shatter = user != null ? IceEffect.Shatter(user, npc, centre, settings.PassRadius * 2f) : 0f;
            using (user != null ? user.Credit(credit) : default) npc.Damage(damage + shatter, user);
            if (shatter > 0f) { LastShatters++; CombatImpact.PreserveDeathLaunch(npc); }
            FeelDirector.Instance?.Particles.Burst(npc.transform.position + Vector3.up, color, settings.PassParticles);
        }
    }
    void VisualStep()
    {
        Vector3 now=hero.transform.position;
        Vector3 delta=Vector3.ProjectOnPlane(now-previous,Vector3.up);
        if(emitting&&delta.sqrMagnitude>.0001f&&Time.time>=nextEmit)
        {
            Vector3 direction=delta.normalized;
            Vector3 right=Vector3.Cross(Vector3.up,direction).normalized;
            float intensity=Mathf.Clamp(delta.magnitude/Mathf.Max(.001f,Time.deltaTime*45f),.35f,1.15f);
            float width=streakWidth*.22f*intensity;
            int index=cursor*2;
            for(int side=0;side<2;side++)
            {
                float sign=side==0?-1f:1f;
                Vector3 offset=right*(.34f*sign)+Vector3.up*(side==0?.62f:1.18f);
                var line=streaks[index+side];
                // Length follows measured displacement, with a short capped extension so the dash reads at
                // ordinary camera distance without turning into Teleport's endpoint-to-endpoint streak.
                line.SetPosition(0,now-direction*Mathf.Clamp(delta.magnitude*2.5f,.6f,1.6f)+offset);
                line.SetPosition(1,now+offset);
                line.widthMultiplier=width;line.enabled=true;
                expires[index+side]=Time.time+streakSeconds;
            }
            cursor=(cursor+1)%PairCount;StreakCount++;LastVisualDirection=direction;
            nextEmit=Time.time+.025f;
            FeelDirector.Instance?.Particles.Burst(now+Vector3.up*.25f,color,settings.StepParticles);
        }
        previous=now;
    }
    public bool Emitting
    {
        get=>emitting;
        set
        {
            if(emitting==value)return;
            emitting=value;
            if(hero!=null)FeelDirector.Instance?.Particles.Burst(hero.transform.position+Vector3.up*.35f,color,value?6:4);
        }
    }
    public static DashTrail For(SuperHeroController hero, CityColor color, float seconds, float width)
    {
        var t = hero.GetComponentInChildren<DashTrail>();
        if (t == null)
        {
            var go = new GameObject("Speed displacement streak pool"); go.transform.SetParent(hero.transform, false);
            t = go.AddComponent<DashTrail>(); t.hero = hero;
            for(int i=0;i<t.streaks.Length;i++)
            {
                var child=new GameObject("Speed streak "+i);child.transform.SetParent(go.transform,false);
                var line=child.AddComponent<LineRenderer>();line.useWorldSpace=true;line.positionCount=2;
                line.numCapVertices=2;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
                line.enabled=false;t.streaks[i]=line;
            }
        }
        var material=CityMaterials.GetVfx(color);
        for(int i=0;i<t.streaks.Length;i++)t.streaks[i].sharedMaterial=material;
        t.streakSeconds=seconds;t.streakWidth=width;
        return t;
    }
    void LateUpdate()
    {
        if(emitting&&hero!=null&&!hero.Dashing)Emitting=false;
        for(int i=0;i<streaks.Length;i++)if(streaks[i]!=null&&streaks[i].enabled)
        {
            float remaining=expires[i]-Time.time;
            if(remaining<=0)streaks[i].enabled=false;
            else streaks[i].widthMultiplier=streakWidth*.22f*Mathf.Clamp01(remaining/Mathf.Max(.01f,streakSeconds));
        }
    }
    void OnDisable(){emitting=false;for(int i=0;i<streaks.Length;i++)if(streaks[i]!=null)streaks[i].enabled=false;}
}
