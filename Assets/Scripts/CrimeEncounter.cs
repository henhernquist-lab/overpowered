using System.Collections.Generic;
using UnityEngine;

public sealed class EncounterActor
{
    public CityNpc Npc;
    public Vector3 Exit;
    public bool Captured, Escaped, Saved;
    public Rigidbody Blockade;
    public Vector3 BlockadeStart;
}
public sealed class EncounterNode
{
    public GameObject Visual;
    public bool Done;
}
public sealed class CrimeEncounter : MonoBehaviour
{
    public EncounterDefinition Definition { get; private set; }
    public WorldSession World { get; private set; }
    public Vector3 Site { get; private set; }
    public readonly List<EncounterActor> Robbers=new List<EncounterActor>(), Civilians=new List<EncounterActor>();
    public readonly List<CityNpc> Responders=new List<CityNpc>();
    public readonly List<EncounterNode> Loot=new List<EncounterNode>(), Hazards=new List<EncounterNode>();
    public readonly List<Rigidbody> Props=new List<Rigidbody>();
    public int DestroyedProps { get; private set; }
    public int Rescued => Civilians.FindAll(a=>a.Saved).Count;
    public int LostCivilians => Civilians.FindAll(a=>!a.Saved&&(a.Npc==null||a.Npc.Dead)).Count;
    public int EscapedRobbers => Robbers.FindAll(a=>a.Escaped).Count;
    public int StoppedRobbers => Robbers.FindAll(a=>!a.Escaped&&(a.Captured||a.Npc==null||a.Npc.Dead)).Count;
    public int LootTaken => Loot.FindAll(n=>n.Done).Count;
    public int HazardsDone => Hazards.FindAll(n=>n.Done).Count;
    public float Elapsed { get; private set; }
    public bool Finished { get; private set; }
    public string Objective => World.Mode.Definition.Rules.Objective(this);
    public string InteractionHint { get; private set; }="Move barriers with powers, then hold R near people or supplies.";
    CrimeEvent crime;
    object interaction;
    float hold, nextSuppression;

    public void Initialize(WorldSession world,CrimeEvent owner,EncounterDefinition definition,Vector3 site)
    {
        World=world; crime=owner; Definition=definition; Site=site;
        for(int i=0;i<definition.Robbers;i++)
        {
            var npc=Actor(site+Vector3.forward*(i+1)*definition.Spacing,NpcRole.Criminal);
            Robbers.Add(new EncounterActor {Npc=npc,Exit=ExitPoint(i)});
        }
        for(int i=0;i<definition.Civilians;i++)
        {
            var npc=Actor(site+Vector3.back*(i+1)*definition.Spacing,NpcRole.Civilian);
            var barrier=Prop("Rescue blockade",npc.transform.position+Vector3.right*definition.BlockadeOffset,world.Tuning.Props.CrateSize,world.Tuning.Props.CrateMass);
            Civilians.Add(new EncounterActor {Npc=npc,Exit=ExitPoint(i+definition.Robbers),Blockade=barrier,BlockadeStart=barrier.position});
            npc.GetComponentInChildren<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Cyan);
        }
        if(world.Mode.Definition.SpawnPolice) for(int i=0;i<definition.RespondingCops;i++)
            Responders.Add(Actor(site+Vector3.right*(i+1)*definition.Spacing,NpcRole.Cop));
        for(int i=0;i<definition.Cars;i++) Prop("Encounter throwable car",site+Vector3.left*(i+1)*definition.Spacing,world.Tuning.Props.CarSize,world.Tuning.Props.CarMass);
        for(int i=0;i<definition.LooseProps;i++) Prop("Encounter supply crate",site+new Vector3((i%2==0?1:-1)*definition.Spacing,0,(i+1)*definition.Spacing),world.Tuning.Props.CrateSize,world.Tuning.Props.CrateMass);
        for(int i=0;i<definition.Loot;i++) Loot.Add(Node("Loot — hold R",site+new Vector3(-definition.Spacing,0,-(i+1)*definition.Spacing),CityColor.Amber));
        for(int i=0;i<definition.Hazards;i++) Hazards.Add(Node("Fire — hold R",site+new Vector3(definition.Spacing,0,-(i+1)*definition.Spacing),CityColor.Fire));
        World.Alarm(site);
    }
    CityNpc Actor(Vector3 position,NpcRole role)
    {
        var actor=CityNpc.Spawn(World,position,role);
        if(actor==null) actor=CityNpc.Spawn(World,Site,role);
        if(actor==null) throw new System.InvalidOperationException("Encounter has no usable NavMesh at "+Site);
        actor.Encounter=this; actor.transform.SetParent(transform,true); return actor;
    }
    Vector3 ExitPoint(int index)
    {
        Vector3 direction=Quaternion.Euler(0,index*360f/Mathf.Max(1,Definition.Robbers+Definition.Civilians),0)*Vector3.forward;
        Vector3 target=Site+direction*Definition.RunnerEscapeDistance;
        return World.City.NearestSidewalk(target);
    }
    Rigidbody Prop(string name,Vector3 ground,Vector3 size,float mass)
    {
        if(name=="Encounter throwable car")
        {
            var car=World.City.Art.CreateProp(new ArtPlacement{Kind=CityPropKind.Car,Position=ground});
            car.name=name;car.transform.SetParent(transform,true);var rb=car.GetComponent<Rigidbody>();rb.mass=mass;
            car.AddComponent<EncounterProp>().Owner=this;Props.Add(rb);return rb;
        }
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.position=ground+Vector3.up*size.y*.5f;
        go.transform.localScale=size; go.transform.SetParent(transform,true);
        go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);
        var body=go.AddComponent<Rigidbody>(); body.mass=mass; body.interpolation=RigidbodyInterpolation.Interpolate; body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<BreakableProp>().Configure(World.Tuning.Props); go.AddComponent<EncounterProp>().Owner=this; Props.Add(body); return body;
    }
    EncounterNode Node(string name,Vector3 ground,CityColor color)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.position=ground+Vector3.up*Definition.MarkerHeight; go.transform.localScale=Vector3.one*Definition.MarkerSize;
        go.transform.SetParent(transform,true); go.GetComponent<Collider>().enabled=false; go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(color);
        return new EncounterNode {Visual=go};
    }
    void Update()
    {
        if(Finished||World.Mode.Ended||World.Mode.Paused) return;
        Tick(Time.deltaTime);
        if(!World.MenuOpen&&!World.PlayerDead) Interact(Input.GetKey(KeyCode.R)?Time.deltaTime:0f);
    }
    public void Tick(float dt)
    {
        if(Finished||World.Mode.Ended||World.Mode.Paused) return;
        Elapsed+=dt;
        if(Elapsed>Definition.CivilianDangerAfter)
        {
            float exposure=Mathf.Min(dt,Elapsed-Definition.CivilianDangerAfter);
            foreach(var actor in Civilians) if(!actor.Saved&&actor.Npc!=null&&!actor.Npc.Dead) actor.Npc.Damage(Definition.CivilianDamagePerSecond*exposure,null);
        }
        if(World.Mode.Definition.Rules.Failed(this)) { End(false,"A robber escaped or a civilian was lost."); return; }
        if(Elapsed>=Definition.Deadline) { End(false,"Encounter ignored / deadline expired."); return; }
        TryComplete();
    }
    public bool TryComplete()
    {
        if(Finished||World.Mode.Ended||!World.Mode.Definition.Rules.Complete(this)) return false;
        End(true,"All objectives complete."); return true;
    }
    void End(bool success,string reason)
    {
        if(Finished) return; Finished=true; crime.MarkEncounterFinished();
        World.Mode.EncounterEnded(this,success,reason); Destroy(gameObject);
    }
    public bool BarrierCleared(EncounterActor actor) => actor.Blockade==null||Vector3.Distance(actor.Blockade.position,actor.BlockadeStart)>=Definition.PropMoveDistance;
    public bool Interact(float dt)
    {
        if(Finished||World.Mode.Ended||World.Mode.Paused||World.PlayerDead) return false;
        object candidate=null; float best=Definition.InteractRadius;
        Vector3 player=World.Hero.transform.position;
        bool hero=World.Mode.Definition.Side==PlayerSide.Hero;
        InteractionHint=hero?"Use powers to clear rescue blockades. Hold R near civilians, robbers or fire.":"Hold R at gold loot / fire, wreck props, then escape the scene.";
        if(hero)
        {
            foreach(var actor in Civilians) if(!actor.Saved&&actor.Npc!=null&&!actor.Npc.Dead)
            {
                float distance=Vector3.Distance(player,actor.Npc.transform.position);
                if(distance<best)
                {
                    if(!BarrierCleared(actor)) { InteractionHint="Clear the brown blockade with a punch or Telekinesis first."; continue; }
                    best=distance; candidate=actor;
                }
            }
            foreach(var actor in Robbers) if(!actor.Captured&&!actor.Escaped&&actor.Npc!=null&&!actor.Npc.Dead)
            { float distance=Vector3.Distance(player,actor.Npc.transform.position); if(distance<best) {best=distance;candidate=actor;} }
        }
        var nodes=new List<EncounterNode>(Hazards); if(!hero) nodes.AddRange(Loot);
        foreach(var node in nodes) if(!node.Done&&node.Visual!=null)
        {float distance=Vector3.Distance(player,node.Visual.transform.position);if(distance<best){best=distance;candidate=node;}}
        if(dt<=0||candidate==null) {hold=0;interaction=null;return false;}
        if(candidate!=interaction) {hold=0;interaction=candidate;}
        hold+=dt; InteractionHint=$"Hold R… {Mathf.Clamp01(hold/Definition.HoldSeconds):P0}";
        if(hold<Definition.HoldSeconds) return false;
        if(candidate is EncounterActor chosen)
        {
            if(Civilians.Contains(chosen)) { chosen.Saved=true; World.Mode.RecordRescue(); World.Mode.AddScore(World.Mode.Definition.RescueScore); }
            else {chosen.Captured=true;chosen.Npc.gameObject.SetActive(false);}
        }
        else if(candidate is EncounterNode node) {node.Done=true;node.Visual.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Leaf);}
        hold=0; interaction=null; TryComplete(); return true;
    }
    public bool Drive(CityNpc npc)
    {
        if(Finished) return false;
        var robber=Robbers.Find(a=>a.Npc==npc);
        if(robber!=null)
        {
            if(robber.Captured||robber.Escaped) return true;
            Vector3 target=Elapsed<Definition.RunForExitAfter ? Site+(robber.Exit-Site).normalized*Definition.Spacing*2 : robber.Exit;
            npc.DirectTo(target,Definition.RunnerSpeed);
            if(Elapsed>=Definition.RunForExitAfter&&Vector3.Distance(npc.transform.position,robber.Exit)<Definition.EscapeReach)
            {robber.Escaped=true;npc.gameObject.SetActive(false);}
            return true;
        }
        var civilian=Civilians.Find(a=>a.Npc==npc);
        if(civilian!=null)
        {
            npc.Agent.isStopped=!civilian.Saved;
            if(civilian.Saved) npc.DirectTo(civilian.Exit,World.Tuning.Npcs.FleeSpeed);
            return true;
        }
        if(npc.Role==NpcRole.Cop&&World.Mode.Definition.Side==PlayerSide.Hero)
        {
            EncounterActor closest=null; float best=float.MaxValue;
            foreach(var actor in Robbers) if(!actor.Captured&&!actor.Escaped&&actor.Npc!=null&&!actor.Npc.Dead)
            {float d=Vector3.Distance(actor.Npc.transform.position,npc.transform.position);if(d<best){best=d;closest=actor;}}
            if(closest==null) return true;
            npc.DirectTo(closest.Npc.transform.position,World.Tuning.Npcs.CopSpeed);
            if(best<World.Tuning.Npcs.AttackRange&&Time.time>=nextSuppression)
            {nextSuppression=Time.time+World.Tuning.Npcs.AttackCooldown;closest.Npc.Freeze(Definition.CopSuppressSeconds);closest.Npc.Damage(Definition.CopSuppressDamage,null);}
            return true;
        }
        return false;
    }
    public void PropDestroyed()
    {
        if(Finished) return; DestroyedProps++;
        if(World.Mode.Definition.Side==PlayerSide.Villain) World.Mode.AddScore(World.Mode.Definition.PropScore);
    }
}
public sealed class EncounterProp : MonoBehaviour
{
    public CrimeEncounter Owner;
    public void Broken() {if(Owner!=null) Owner.PropDestroyed();}
}
