using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// BUILDING FIRE — fire spots burn in a ring around civilians trapped at the site. Each spot has heat 0..1 and slowly regrows.
/// Put them out with powers: an Ice cast on a spot removes IceDouse; any CombatImpact.Blast of at least ImpactDouseImpulse
/// (Strength punch/kick, ground pound, Sonic Slam...) removes ImpactDouse from every spot it reaches; holding R next to a spot
/// works too, but slowly (an extinguisher). A civilian is trapped while any lit spot is within BlockRadius of it; once free,
/// walk up to it and it follows you — lead it to the safe point. Trapped civilians start burning after CivilianBurnAfter.
[CreateAssetMenu(menuName = "Overpowered/Missions/Building fire")]
public sealed class FireScenario : EncounterScenario
{
    public int FireSpots = 4, Trapped = 2;
    public float SpotRing = 3.5f, CivilianRing = 1f;
    [Header("Putting it out")]
    public float IceDouse = .55f, ImpactDouse = .35f, ImpactDouseImpulse = 600f, HoldDousePerSecond = .2f, RegrowPerSecond = .03f;
    public float HoldRadius = 3f;
    [Header("Civilians")]
    public float BlockRadius = 4.5f, CivilianBurnAfter = 25f, CivilianBurnPerSecond = 1.2f;
    public float FollowRadius = 3.5f, FollowGap = 1.8f, EscortSpeed = 4.5f, SafeDistance = 16f, SafeRadius = 3f;
    [Header("Presentation (fixed per spot, shared palette material)")]
    public float FlameRate = 18f, FlameSize = .22f, FlameLifetime = .7f, FlameSpeed = 2.2f;
    public override ScenarioState Begin(CrimeEncounter encounter)
    {
        var state = encounter.gameObject.AddComponent<FireState>(); state.Setup(encounter, this); return state;
    }
}
public sealed class FireState : ScenarioState
{
    FireScenario d;
    public readonly List<FireSpot> Spots = new List<FireSpot>();
    public readonly HashSet<EncounterActor> Following = new HashSet<EncounterActor>();
    public Vector3 SafePoint { get; private set; }
    public int SpotsOut { get { int n = 0; foreach (var s in Spots) if (s.Out) n++; return n; } }
    public void Setup(CrimeEncounter e, FireScenario scenario)
    {
        Attach(e, scenario); d = scenario;
        SafePoint = e.World.City.NearestSidewalk(e.Site + Vector3.back * d.SafeDistance);
        e.SpawnNode("Safe point", SafePoint, CityColor.Leaf);
        for (int i = 0; i < d.Trapped; i++)
        {
            var npc = e.SpawnActor(e.Site + Quaternion.Euler(0, i * 360f / Mathf.Max(1, d.Trapped), 0) * Vector3.forward * d.CivilianRing, NpcRole.Civilian);
            npc.GetComponentInChildren<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Cyan);
            e.Civilians.Add(new EncounterActor { Npc = npc, Exit = SafePoint });
        }
        for (int i = 0; i < d.FireSpots; i++)
        {
            Vector3 at = e.Site + Quaternion.Euler(0, 45f + i * 360f / Mathf.Max(1, d.FireSpots), 0) * Vector3.forward * d.SpotRing;
            var go = new GameObject("Mission fire " + i); go.transform.SetParent(e.transform, false); go.transform.position = at;
            var spot = go.AddComponent<FireSpot>(); spot.Setup(d); Spots.Add(spot);
        }
    }
    public bool Trapped(EncounterActor a)
    {
        if (a.Npc == null) return false;
        foreach (var s in Spots) if (!s.Out && Vector3.Distance(s.transform.position, a.Npc.transform.position) < d.BlockRadius) return true;
        return false;
    }
    public override bool Drive(CityNpc npc)
    {
        EncounterActor a = null; foreach (var c in Encounter.Civilians) if (c.Npc == npc) { a = c; break; }   // every NPC frame: no closure
        if (a == null) return false;
        if (a.Saved) { npc.Agent.isStopped = false; npc.DirectTo(SafePoint + (SafePoint - Encounter.Site).normalized * 6f, d.EscortSpeed); return true; }
        if (Trapped(a)) { npc.Agent.isStopped = true; Following.Remove(a); return true; }
        Vector3 hero = World.Hero.transform.position;
        if (!Following.Contains(a) && Vector3.Distance(hero, npc.transform.position) < d.FollowRadius) Following.Add(a);
        if (!Following.Contains(a)) { npc.Agent.isStopped = true; return true; }
        npc.Agent.isStopped = false;
        Vector3 behind = hero - (hero - npc.transform.position).normalized * d.FollowGap;
        npc.DirectTo(behind, d.EscortSpeed);
        if (Vector3.Distance(npc.transform.position, SafePoint) < d.SafeRadius)
        { a.Saved = true; Following.Remove(a); World.Civilians?.Rescued(a.Npc); World.Civilians?.Escorted(a.Npc); World.Mode.RecordRescue(); World.Mode.AddScore(World.Mode.Definition.RescueScore); Encounter.TryComplete(); }
        return true;
    }
    public override void Tick(float dt)
    {
        if (Encounter.Elapsed < d.CivilianBurnAfter) return;
        using (HarmContext.Environment()) foreach (var a in Encounter.Civilians) if (!a.Saved && a.Npc != null && !a.Npc.Dead && Trapped(a)) a.Npc.Damage(d.CivilianBurnPerSecond * dt, null);
    }
    public override bool Complete() => Spots.Count > 0 && SpotsOut == Spots.Count && Encounter.Civilians.Count > 0 && Encounter.Rescued == Encounter.Civilians.Count;
    public override bool Failed(out string reason)
    {
        reason = Encounter.LostCivilians > 0 ? "A civilian was lost in the fire." : null;
        return reason != null;
    }
    public override bool Progress(ObjectiveTask task, out int done, out int total)
    {
        done = total = 0;
        if (task == ObjectiveTask.Fires) { total = Spots.Count; done = SpotsOut; return true; }
        if (task == ObjectiveTask.Carry) { total = Encounter.Civilians.Count; done = Encounter.Rescued; return true; }
        return false;
    }
    public override void Targets(ObjectiveTask task, List<Vector3> into)
    {
        if (task == ObjectiveTask.Fires) foreach (var s in Spots) { if (!s.Out) into.Add(s.transform.position); }
        if (task == ObjectiveTask.Carry)
            foreach (var a in Encounter.Civilians)
            {
                if (a.Saved || a.Npc == null || a.Npc.Dead) continue;
                into.Add(Following.Contains(a) ? SafePoint : a.Npc.transform.position);
            }
    }
    FireSpot Nearest(Vector3 at)
    {
        FireSpot best = null; float nearest = d.HoldRadius;
        foreach (var s in Spots) { if (s.Out) continue; float dist = Vector3.Distance(at, s.transform.position); if (dist < nearest) { nearest = dist; best = s; } }
        return best;
    }
    public override bool InteractableNear(Vector3 point) => Nearest(point) != null;
    /// Holding R beside a lit spot sprays it (the slow extinguisher route; powers are much faster).
    public override bool Interact(float dt)
    {
        if (dt <= 0f) return false;
        var spot = Nearest(World.Hero.transform.position); if (spot == null) return false;
        bool wasOut = spot.Out; spot.Douse(d.HoldDousePerSecond * dt);
        Encounter.InteractionHint = spot.Out ? "Fire out." : $"Spraying… {1f - spot.Heat:P0}";
        if (!wasOut && spot.Out) { Encounter.TryComplete(); return true; }
        return false;
    }
}
/// One burning spot — GAMEPLAY ONLY: heat 0..1 (regrows while lit) and a solid sphere so Ice / the beam can target it and it
/// blocks walking through flames. Its look is rented from the session FireVisualPool and returned when it goes out or is
/// destroyed; with the pool exhausted (or no pool) the spot still burns, douses and blocks exactly the same.
public sealed class FireSpot : MissionTarget
{
    FireScenario d; int visual = -1;
    public float Heat { get; private set; } = 1f;
    public bool Out => Heat <= 0f;
    public int IceHits { get; private set; }
    public int ImpactHits { get; private set; }
    public bool HasVisual => visual >= 0;
    public void Setup(FireScenario scenario)
    {
        d = scenario;
        var col = gameObject.AddComponent<SphereCollider>(); col.radius = .8f; col.center = Vector3.up;
        visual = FireVisualPool.Get().Rent(transform.position, d);
        Apply();
    }
    public void Douse(float amount)
    {
        if (Out || amount <= 0f) return;
        Heat = Mathf.Max(0f, Heat - amount); Apply();
        FeelDirector.Instance?.Particles.Burst(transform.position + Vector3.up, Out ? CityColor.Slate : CityColor.Cyan, Out ? 12 : 5);
    }
    public override bool Freeze(PowerUser source, PowerStats stats) { if (Out) return false; IceHits++; Douse(d.IceDouse); return true; }
    public override void Hit(float damage, float impulse, PowerUser source) { if (Out || impulse < d.ImpactDouseImpulse) return; ImpactHits++; Douse(d.ImpactDouse); }
    void Update()
    {
        if (Out || Time.deltaTime <= 0f) return;
        if (Heat < 1f) { Heat = Mathf.Min(1f, Heat + d.RegrowPerSecond * Time.deltaTime); Apply(); }
    }
    void Apply()
    {
        if (visual >= 0) FireVisualPool.Instance?.SetHeat(visual, Heat);
        if (!Out) return;
        var col = GetComponent<Collider>(); if (col != null) col.enabled = false;
        ReturnVisual();
    }
    void ReturnVisual() { if (visual >= 0) FireVisualPool.Instance?.Release(visual); visual = -1; }
    void OnDestroy() { ReturnVisual(); }
}
/// Session-level fixed pool of fire looks (a looping flame ParticleSystem + an ember block per slot), parented to the
/// WorldSession and built once. Shared palette materials and the built-in cube mesh; no per-spot GameObject, Material or
/// ParticleSystem is created. Placeholder presentation — LOCAL owns the final fire look (e.g. pooled Cartoon FX).
public sealed class FireVisualPool : MonoBehaviour
{
    public const int Capacity = 12;
    sealed class Slot { public GameObject Root; public ParticleSystem Flames; public Transform Ember; public Renderer EmberRenderer; public FireScenario Settings; public bool Busy; }
    Slot[] slots;
    public static FireVisualPool Instance { get; private set; }
    public int InUse { get { int n = 0; if (slots != null) foreach (var s in slots) if (s.Busy) n++; return n; } }
    public static FireVisualPool Get()
    {
        if (Instance != null) return Instance;
        var root = new GameObject("Pooled fire visuals"); root.transform.SetParent(WorldSession.Instance != null ? WorldSession.Instance.transform : null, false);
        Instance = root.AddComponent<FireVisualPool>(); Instance.Build(); return Instance;
    }
    void Build()
    {
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube); var mesh = template.GetComponent<MeshFilter>().sharedMesh; Destroy(template.GetComponent<Collider>());
        slots = new Slot[Capacity];
        for (int i = 0; i < Capacity; i++)
        {
            var go = new GameObject("Pooled fire " + i); go.transform.SetParent(transform, false);
            var ember = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(ember.GetComponent<Collider>());
            ember.name = "Ember"; ember.transform.SetParent(go.transform, false); ember.transform.localPosition = Vector3.up * .3f;
            var er = ember.GetComponent<Renderer>(); er.sharedMaterial = CityMaterials.Get(CityColor.Fire);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.gravityModifier = -.2f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 64;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .6f; shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = mesh;
            r.sharedMaterial = CityMaterials.Get(CityColor.Fire); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            go.SetActive(false);
            slots[i] = new Slot { Root = go, Flames = ps, Ember = ember.transform, EmberRenderer = er };
        }
        Destroy(template);
    }
    /// A slot index, or -1 when every slot is in use (the spot then simply has no look).
    public int Rent(Vector3 position, FireScenario settings)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i]; if (s.Busy) continue;
            s.Busy = true; s.Settings = settings; s.Root.transform.position = position; s.Root.SetActive(true);
            var main = s.Flames.main; main.startLifetime = settings.FlameLifetime; main.startSize = settings.FlameSize; main.startSpeed = settings.FlameSpeed;
            s.EmberRenderer.sharedMaterial = CityMaterials.Get(CityColor.Fire);
            s.Flames.Clear(); s.Flames.Play(); return i;
        }
        return -1;
    }
    public void SetHeat(int index, float heat)
    {
        if (slots == null || index < 0 || index >= slots.Length || !slots[index].Busy) return;
        var s = slots[index]; var emission = s.Flames.emission; emission.rateOverTime = s.Settings.FlameRate * heat;
        s.Ember.localScale = Vector3.one * Mathf.Lerp(.15f, .9f, heat);
    }
    public void Release(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length || !slots[index].Busy) return;
        var s = slots[index]; s.Busy = false; s.Flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); s.Root.SetActive(false);
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
}
