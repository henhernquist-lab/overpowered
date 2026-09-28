using System.Collections.Generic;
using UnityEngine;

/// VAULT HEIST (villain) — a steel vault stands at the site. Crack it with raw damage: any CombatImpact.Blast (punches, kicks,
/// Fire Blast, pounds, synergies) or a held Laser Eyes beam. The first hit trips the alarm: after ResponseDelay a police
/// response squad arrives on top of the posted guards. Cracked, it spills LootBags; run over a bag to grab it. Then reach
/// the getaway van (marked, VanDistance away) with the loot. Win: vault cracked, all loot, at the van. The mode's deadline
/// and defeat limit remain the ways to lose.
[CreateAssetMenu(menuName = "Overpowered/Missions/Vault heist")]
public sealed class HeistScenario : EncounterScenario
{
    public float VaultHealth = 350f;
    public Vector3 VaultSize = new Vector3(2.4f, 2.6f, 1.4f);
    public int LootBags = 3;
    public float PickupRadius = 1.8f, SpillRadius = 2.5f;
    public float VanDistance = 30f, VanRadius = 3.5f;
    [Tooltip("Police sent to the site after the vault is first hit (on top of EncounterDefinition.RespondingCops).")] public int ResponseCops = 3;
    public float ResponseDelay = 4f, ResponseRing = 12f, CrackHeat = 1f;
    public override ScenarioState Begin(CrimeEncounter encounter)
    {
        var state = encounter.gameObject.AddComponent<HeistState>(); state.Setup(encounter, this); return state;
    }
}
public sealed class HeistState : ScenarioState
{
    HeistScenario d;
    public VaultTarget Vault { get; private set; }
    public Vector3 Van { get; private set; }
    public bool ResponseSent { get; private set; }
    public int ResponseSpawned { get; private set; }
    float responseAt = -1f; bool spilled;
    public void Setup(CrimeEncounter e, HeistScenario scenario)
    {
        Attach(e, scenario); d = scenario;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Mission vault"; go.transform.SetParent(e.transform, false);
        go.transform.position = e.Site + Vector3.up * d.VaultSize.y * .5f; go.transform.localScale = d.VaultSize;
        go.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Metal);
        Vault = go.AddComponent<VaultTarget>(); Vault.Setup(d.VaultHealth);
        Vector3 away = e.Site - e.World.City.Spawn; away.y = 0f; if (away.sqrMagnitude < .01f) away = Vector3.forward;
        Van = e.World.City.NearestSidewalk(e.Site + away.normalized * d.VanDistance);
        var van = e.SpawnNode("Getaway van", Van, CityColor.Blue); van.Visual.transform.localScale = new Vector3(2f, 1.6f, 4f);
    }
    public override void Tick(float dt)
    {
        if (Vault.Hits > 0 && responseAt < 0f) { responseAt = Time.time + d.ResponseDelay; Encounter.InteractionHint = "Alarm tripped: police are on the way."; }
        if (!ResponseSent && responseAt > 0f && Time.time >= responseAt)
        {
            ResponseSent = true;
            for (int i = 0; i < d.ResponseCops; i++)
            {
                var cop = Encounter.SpawnActor(Encounter.World.City.NearestSidewalk(Encounter.Site + Quaternion.Euler(0, i * 360f / Mathf.Max(1, d.ResponseCops), 0) * Vector3.forward * d.ResponseRing), NpcRole.Cop);
                if (cop != null) { Encounter.Responders.Add(cop); ResponseSpawned++; }
            }
        }
        if (Vault.Cracked && !spilled)
        {
            spilled = true; World.AddHeat(d.CrackHeat); Encounter.InteractionHint = "Vault open: grab the loot, then get to the van.";
            for (int i = 0; i < d.LootBags; i++)
                Encounter.Loot.Add(Encounter.SpawnNode("Loot bag", Encounter.Site + Quaternion.Euler(0, i * 360f / Mathf.Max(1, d.LootBags), 0) * Vector3.back * d.SpillRadius, CityColor.Amber));
        }
        if (!spilled) return;
        Vector3 hero = World.Hero.transform.position;
        foreach (var bag in Encounter.Loot)
            if (!bag.Done && bag.Visual != null && Vector3.Distance(hero, bag.Visual.transform.position) < d.PickupRadius) { bag.Done = true; bag.Visual.SetActive(false); }
    }
    public bool AtVan => Vector3.Distance(World.Hero.transform.position, Van) < d.VanRadius;
    public override bool Complete() => Vault.Cracked && Encounter.Loot.Count == d.LootBags && Encounter.LootTaken == d.LootBags && AtVan;
    public override bool Failed(out string reason) { reason = null; return false; }   // deadline and the mode's defeat limit
    public override bool Progress(ObjectiveTask task, out int done, out int total)
    {
        done = total = 0;
        if (task == ObjectiveTask.Vault) { total = 100; done = Mathf.FloorToInt(100f * Vault.Progress); return true; }
        if (task == ObjectiveTask.Extract)
        {
            total = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(Encounter.Site, Van)));
            float left = Vector3.Distance(World.Hero.transform.position, Van);
            // Only counts once the loot is in hand; done when inside the van radius.
            done = !Vault.Cracked || Encounter.LootTaken < d.LootBags ? 0 : AtVan ? total : Mathf.Clamp(total - Mathf.CeilToInt(left), 0, total - 1);
            return true;
        }
        return false;
    }
    public override void Targets(ObjectiveTask task, List<Vector3> into)
    {
        if (task == ObjectiveTask.Vault && !Vault.Cracked) into.Add(Vault.transform.position);
        if (task == ObjectiveTask.Extract) into.Add(Van);
    }
}
/// The heist vault: a static block that soaks damage from blasts and beams until cracked. It ignores Ice.
public sealed class VaultTarget : MissionTarget
{
    public float Health { get; private set; }
    public float MaxHealth { get; private set; }
    public int Hits { get; private set; }
    public bool Cracked => Health <= 0f;
    public float Progress => MaxHealth > 0f ? 1f - Mathf.Clamp01(Health / MaxHealth) : 1f;
    public void Setup(float health) { Health = MaxHealth = Mathf.Max(1f, health); }
    public override void Hit(float damage, float impulse, PowerUser source)
    {
        if (Cracked || damage <= 0f) return;
        Health = Mathf.Max(0f, Health - damage); Hits++;
        FeelDirector.Instance?.Particles.Burst(transform.position + Vector3.up * .5f, CityColor.Metal, Cracked ? 14 : 4);
        if (Cracked) GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Amber);
    }
}
