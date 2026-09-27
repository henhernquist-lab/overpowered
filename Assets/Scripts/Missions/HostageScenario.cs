using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// HOSTAGE RESCUE — hostages kneel at the site, each pinned by a heavy piece of debris, guarded by armed gunmen (ranged
/// Gunner archetype) who hold their posts. Once the player comes within AlertRadius the gunmen are alerted; if any gunman is
/// still up after AlertGraceSeconds, the hostages start getting hurt. Clear the threats, then free each hostage by moving its
/// debris (punch, Telekinesis, blasts); a freed hostage runs to the safe point. Careful: area attacks hurt hostages too.
[CreateAssetMenu(menuName = "Overpowered/Missions/Hostage rescue")]
public sealed class HostageScenario : EncounterScenario
{
    public int Hostages = 3, Gunmen = 3;
    [Tooltip("Hostage ring radius around the site, and the gunmen's guard ring radius.")] public float HostageRing = 2f, GuardRing = 5f;
    [Tooltip("Heavy debris pinning each hostage: moved this far = freed.")] public float DebrisMass = 250f, DebrisMoveDistance = 2f;
    public Vector3 DebrisSize = new Vector3(1.4f, 1f, 1.4f);
    public float AlertRadius = 18f, AlertGraceSeconds = 15f, HostageDamagePerSecond = 2.5f;
    public float SafeDistance = 16f, SafeRadius = 3f, FleeSpeed = 5f;
    public override ScenarioState Begin(CrimeEncounter encounter)
    {
        var state = encounter.gameObject.AddComponent<HostageState>(); state.Setup(encounter, this); return state;
    }
}
public sealed class HostageState : ScenarioState
{
    HostageScenario d;
    public readonly List<CityNpc> Gunmen = new List<CityNpc>();
    readonly Dictionary<CityNpc, Vector3> posts = new Dictionary<CityNpc, Vector3>();
    public Vector3 SafePoint { get; private set; }
    public bool Alerted { get; private set; }
    public float AlertedFor { get; private set; }
    public bool HostagesInDanger => Alerted && AlertedFor >= d.AlertGraceSeconds && GunmenLeft > 0;
    public int GunmenLeft { get { int n = 0; foreach (var g in Gunmen) if (g != null && !g.Dead) n++; return n; } }
    public void Setup(CrimeEncounter e, HostageScenario scenario)
    {
        Attach(e, scenario); d = scenario;
        SafePoint = e.World.City.NearestSidewalk(e.Site + Vector3.back * d.SafeDistance);
        e.SpawnNode("Safe point", SafePoint, CityColor.Leaf);
        var roster = EnemyRoster.Current;
        for (int i = 0; i < d.Hostages; i++)
        {
            Vector3 dir = Quaternion.Euler(0, i * 360f / Mathf.Max(1, d.Hostages), 0) * Vector3.forward;
            var npc = e.SpawnActor(e.Site + dir * d.HostageRing, NpcRole.Civilian);
            var debris = e.SpawnProp("Hostage debris", npc.transform.position + dir * 1.1f, d.DebrisSize, d.DebrisMass);
            npc.GetComponentInChildren<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Cyan);
            e.Civilians.Add(new EncounterActor { Npc = npc, Exit = SafePoint, Blockade = debris, BlockadeStart = debris.position });
        }
        for (int i = 0; i < d.Gunmen; i++)
        {
            Vector3 dir = Quaternion.Euler(0, 30f + i * 360f / Mathf.Max(1, d.Gunmen), 0) * Vector3.forward;
            var gunman = e.SpawnActor(e.Site + dir * d.GuardRing, NpcRole.Criminal, roster != null ? roster.Cop : null);
            Gunmen.Add(gunman); posts[gunman] = gunman.transform.position;
        }
    }
    public bool Freed(EncounterActor hostage) => Encounter.BarrierCleared(hostage);
    public override bool Drive(CityNpc npc)
    {
        EncounterActor hostage = null; foreach (var c in Encounter.Civilians) if (c.Npc == npc) { hostage = c; break; }   // every NPC frame: no closure
        if (hostage != null)
        {
            if (hostage.Saved || !Freed(hostage)) { npc.Agent.isStopped = true; return true; }
            npc.Agent.isStopped = false; npc.DirectTo(SafePoint, d.FleeSpeed);
            if (Vector3.Distance(npc.transform.position, SafePoint) < d.SafeRadius)
            { hostage.Saved = true; World.Civilians?.Rescued(hostage.Npc); World.Mode.RecordRescue(); World.Mode.AddScore(World.Mode.Definition.RescueScore); Encounter.TryComplete(); }
            return true;
        }
        // Gunmen hold their posts by the hostages (they still shoot through their normal attack cycle when in range).
        if (posts.TryGetValue(npc, out var post)) { npc.DirectTo(post, World.Tuning.Npcs.CopSpeed); return true; }
        return false;
    }
    public override void Tick(float dt)
    {
        if (!Alerted && Vector3.Distance(World.Hero.transform.position, Encounter.Site) < d.AlertRadius) Alerted = true;
        foreach (var g in Gunmen) if (!Alerted && g != null && g.Health < g.MaxHealth) Alerted = true;   // attacked from range
        if (Alerted && GunmenLeft > 0) AlertedFor += dt;
        if (!HostagesInDanger) return;
        using (HarmContext.Hostile()) foreach (var h in Encounter.Civilians) if (!h.Saved && h.Npc != null && !h.Npc.Dead) h.Npc.Damage(d.HostageDamagePerSecond * dt, null);
    }
    public override bool Complete() => GunmenLeft == 0 && Encounter.Civilians.Count > 0 && Encounter.Rescued == Encounter.Civilians.Count;
    public override bool Failed(out string reason)
    {
        reason = Encounter.LostCivilians > 0 ? "A hostage was lost." : null;
        return reason != null;
    }
    public override bool Progress(ObjectiveTask task, out int done, out int total)
    {
        done = total = 0;
        if (task == ObjectiveTask.Threats) { total = Gunmen.Count; done = total - GunmenLeft; return true; }
        if (task == ObjectiveTask.Hostages) { total = Encounter.Civilians.Count; done = Encounter.Rescued; return true; }
        return false;
    }
    public override void Targets(ObjectiveTask task, List<Vector3> into)
    {
        if (task == ObjectiveTask.Threats) foreach (var g in Gunmen) { if (g != null && !g.Dead) into.Add(g.transform.position); }
        if (task == ObjectiveTask.Hostages)
            foreach (var h in Encounter.Civilians)
            {
                if (h.Saved || h.Npc == null || h.Npc.Dead) continue;
                into.Add(Freed(h) ? SafePoint : h.Blockade != null ? h.Blockade.position : h.Npc.transform.position);
            }
    }
}
