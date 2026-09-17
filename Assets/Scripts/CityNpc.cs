using UnityEngine;
using UnityEngine.AI;

public enum NpcRole { Civilian, Cop, PursuingHero, Criminal }
public sealed class CityNpc : MonoBehaviour
{
    public NpcRole Role { get; private set; }
    public float Health { get; private set; }
    public bool Dead => Health <= 0f;
    public bool Fleeing => Time.time < fleeUntil;
    public bool Hostile => Role == NpcRole.Criminal ? world.Progression.Data.Side == PlayerSide.Hero :
        (Role == NpcRole.Cop || Role == NpcRole.PursuingHero) && world.Progression.Data.Side == PlayerSide.Villain;
    public NavMeshAgent Agent { get; private set; }
    public CrimeEvent Crime;
    WorldSession world; float nextPath, nextAttack, fleeUntil, frozenUntil; Vector3 alarm; int waypoint;
    public static CityNpc Spawn(WorldSession world, Vector3 position, NpcRole role)
    {
        var c = world.Tuning.Npcs;
        if (!NavMesh.SamplePosition(position, out var hit, c.NavSampleRadius, NavMesh.AllAreas)) return null;
        var root = new GameObject(role.ToString()); root.transform.position = hit.position;
        var capsule = root.AddComponent<CapsuleCollider>(); capsule.height=c.Height; capsule.radius=c.Radius; capsule.center=Vector3.up*c.Height*.5f;
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.transform.SetParent(root.transform,false);
        body.transform.localPosition=Vector3.up*c.Height*.5f; body.transform.localScale=new Vector3(c.Radius*2,c.Height*.5f,c.Radius*2);
        body.GetComponent<Collider>().enabled=false; Destroy(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().material.color= role==NpcRole.Civilian ? c.CivilianColor : role==NpcRole.Cop ? c.CopColor : c.HeroColor;
        var npc = root.AddComponent<CityNpc>(); npc.world=world; npc.Role=role;
        npc.Health = role==NpcRole.Civilian ? c.CivilianHealth : role==NpcRole.PursuingHero ? c.HeroHealth : c.CopHealth + world.Stars*c.HealthPerStar;
        npc.Agent=root.AddComponent<NavMeshAgent>(); npc.Agent.height=c.Height; npc.Agent.radius=c.Radius; npc.Agent.acceleration=c.Acceleration; npc.Agent.angularSpeed=c.AngularSpeed;
        npc.Agent.stoppingDistance=c.AttackRange*.5f;
        world.Npcs.Add(npc); return npc;
    }
    public void Alarm(Vector3 position) { alarm=position; fleeUntil=Time.time+world.Tuning.Npcs.FleeSeconds; nextPath=0; }
    public void Freeze(float duration) { frozenUntil=Mathf.Max(frozenUntil,Time.time+duration); }
    public void Damage(float amount, PowerUser source)
    {
        if (Dead || amount <= 0f) return;
        Health = Mathf.Max(0f, Health-amount);
        world.OnAssault(this);
        if (!Dead) return;
        Agent.enabled=false; GetComponent<Collider>().enabled=false;
        world.OnDefeat(this); Crime?.CriminalDefeated();
        transform.rotation=Quaternion.Euler(0,0,90);
        Destroy(gameObject,world.Tuning.Npcs.DestroyDelay);
    }
    void Update()
    {
        if (world==null || Dead || !Agent.isOnNavMesh) return;
        var c=world.Tuning.Npcs;
        Agent.isStopped=Time.time<frozenUntil || world.PlayerDead;
        if (Agent.isStopped) return;
        Agent.speed=Role==NpcRole.Civilian ? (Fleeing ? c.FleeSpeed : c.CivilianSpeed) : Role==NpcRole.PursuingHero ? c.HeroSpeed : c.CopSpeed;
        float distance=Vector3.Distance(transform.position,world.Hero.transform.position);
        if (Hostile && distance<c.AttackRange && Time.time>=nextAttack)
        {
            nextAttack=Time.time+c.AttackCooldown;
            world.DamagePlayer((Role==NpcRole.PursuingHero ? c.HeroDamage : c.AttackDamage)+world.Stars*c.DamagePerStar);
        }
        if (Time.time<nextPath) return;
        nextPath=Time.time+(Hostile || Fleeing ? c.RepathSeconds : c.WanderSeconds);
        Vector3 destination;
        if (Hostile && distance<c.DetectionRange) destination=world.Hero.transform.position;
        else if (Fleeing) destination=transform.position+(transform.position-alarm).normalized*c.FleeDistance;
        else
        {
            if (!Agent.hasPath || Agent.remainingDistance<c.MinimumWanderDistance) waypoint=Random.Range(0,world.City.Sidewalks.Count);
            destination=world.City.Sidewalks[waypoint];
        }
        if (NavMesh.SamplePosition(destination,out var hit,c.NavSampleRadius,NavMesh.AllAreas)) Agent.SetDestination(hit.position);
    }
    void OnDestroy() { if (world!=null) world.Npcs.Remove(this); }
}
