#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public sealed partial class PowerExpansionVerificationRunner : SessionVerificationRunner
{
    public string Phase;
    protected override string Folder=>"Verification/PowerExpansion2/";
    protected override string ResultFile=>"phase-"+Phase+".txt";
    PowerExpansionRuntime Expansion=>W.Powers.Expansion;
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        if(Phase=="b")yield return Ranged();
        if(Phase=="c")yield return MeleeChecks();
        if(Phase=="d")yield return SecondaryChecks();
        if(Phase=="e")yield return SignatureChecks();
        if(Phase=="final")yield return FinalChecks();
        if(Phase=="reload")yield return ReloadChecks();
    }
    IEnumerator EnterExpansion(string primary)
    {
        yield return Home();var p=Power(primary);p.Selectable=true;
        foreach(var hero in F.Heroes)if(!hero.AvailablePowers.Contains(p))hero.AvailablePowers=hero.AvailablePowers.Concat(new[]{p}).ToArray();
        Check(Profile.SetLoadout(F.Heroes[0],p,Power("ice"),Profile.EquippedSecondary,F.Heroes[0].Primary,F.Heroes[0].Secondary),"Isolated in-memory staged "+primary+" enters through actual equip gates");
        Check(GameFlow.Instance.OpenCombatLab(),"Enter real existing Combat Lab for native collision/damage controls");yield return Scene(CombatLab.SceneName);
        W.Rolls.FixedUnit=.5f;var view=Camera.main.GetComponent<ThirdPersonCamera>();if(!view.FirstPerson)view.ToggleView();view.SetLook(0,0);yield return null;
        Check(W.Powers.Select(W.Powers.PowerForSlot(1)),primary+": actual primary slot selection");
    }
    CityNpc Npc(string archetype,Vector3 at,NpcRole role=NpcRole.Criminal,float hp=10000)
    {
        Check(NavMesh.SamplePosition(at,out var hit,1,NavMesh.AllAreas),archetype+": fixture lies on actual lab NavMesh");
        var n=CityNpc.Spawn(W,hit.position,role,Resources.Load<EnemyArchetype>("Enemies/"+archetype),hp,0);
        Check(n!=null,archetype+": actual shared NPC spawned");
        if(archetype=="Pulsebreaker")PulsebreakerBoss.AttachForVerification(n,5);
        n.enabled=false;n.Agent.isStopped=true;n.transform.forward=Vector3.back;n.AlwaysAggro=true;return n;
    }
    IEnumerator ClearActors(){foreach(var n in W.Npcs.ToArray())if(n!=null)Destroy(n.gameObject);yield return null;yield return null;}
    void Ready(){var p=W.Powers.Selected;p.Cooldown=0;p.Charges=3;W.Powers.RestorePracticeEnergy();}
    IEnumerator Ranged()
    {
        var registry=Resources.Load<SignatureCatalog>("SignatureCatalog");
        Check(registry.Complete&&registry.Enabled&&registry.ExpectedPrimaries==8,"Staged definitions do not disable the current eight shipping signatures");
        Check(!Power("aerokinesis").Selectable&&!Power("sonic-manipulation").Selectable,"Both new powers stay hidden until implementation and full roster acceptance");
        var art=Resources.Load<PowerExpansionArt>("PowerExpansionArt");
        Check(art.WindArc!=null&&art.SoundRing!=null&&art.WindArc!=art.SoundRing&&art.EffectMaterial.shader.isSupported,"Actual distinct native arc/ring meshes and Built-in translucent shader import successfully");
        yield return EnterExpansion("aerokinesis");
        foreach(string kind in new[]{"Rusher","Gunner","Brute","Skirmisher","Bulwark","Pulsebreaker"})
        {
            yield return ClearActors();Ready();var n=Npc(kind,new Vector3(0,.1f,6));yield return null;
            float hp=n.CurrentHealth;Vector3 before=n.transform.position;int events=0;PowerUser credited=null;string source=null;
            Action<NpcDamageResult> damage=r=>{if(r.Target==n){events++;credited=r.Attacker;source=r.SourceId;}};CityNpc.DamageApplied+=damage;
            int charges=W.Powers.Selected.Charges;float energy=W.Powers.Energy;
            Check(W.Powers.Use(W.Powers.Selected),kind+": Wind Blast activates through existing payment path");
            Check(!W.Powers.Use(W.Powers.Selected),"CONTROL immediate wind recast refused by cooldown");
            Check(events==1&&credited==W.Powers&&source=="aerokinesis"&&n.CurrentHealth<hp,kind+": exactly one authoritative damage event with correct ownership/source");
            Check(W.Powers.Selected.Charges==charges-1&&Mathf.Abs(W.Powers.Energy-(energy-W.Powers.EnergyCost(W.Powers.Selected)))<.01f,"One charge and exact centralized energy payment");
            yield return new WaitForSeconds(.4f);
            float moved=Vector3.Distance(before,n.transform.position),limit=Expansion.Tuning.WindDistance*n.PowerDisplacementFactor;
            Check(moved>.03f&&moved<=limit+.2f,kind+": finite grounded displacement "+moved.ToString("F3")+"m <= resisted budget "+limit.ToString("F3"));
            Check(Mathf.Abs(n.transform.position.y-before.y)<.3f&&n.Agent.enabled&&n.Agent.isOnNavMesh,kind+": no airborne/permanent physics handoff");
            CityNpc.DamageApplied-=damage;
        }
        yield return ClearActors();Ready();var target=Npc("Rusher",new Vector3(0,.1f,6));var wall=Wall(new Vector3(0,1.5f,3),new Vector3(4,3,.4f));yield return null;
        float original=target.CurrentHealth;Check(W.Powers.Use(W.Powers.Selected),"Wind can be cast into a blocked lane");Check(target.CurrentHealth==original,"CONTROL static wall shields NPC from wind damage");Destroy(wall);yield return null;
        Ready();wall=Wall(new Vector3(0,1.5f,7),new Vector3(4,3,.4f));yield return null;W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.4f);
        Check(target.transform.position.z<6.6f,"CONTROL full NPC capsule cannot be pushed through a solid wall");Destroy(wall);yield return null;
        yield return ClearActors();Ready();target=Npc("Rusher",new Vector3(5,.1f,1));original=target.CurrentHealth;W.Powers.Use(W.Powers.Selected);Check(target.CurrentHealth==original,"CONTROL directional wind is not a full-sphere attack");
        yield return ClearActors();Ready();target=Npc("Rusher",new Vector3(0,.1f,5),NpcRole.Cop);original=target.CurrentHealth;W.Powers.Use(W.Powers.Selected);Check(target.CurrentHealth==original,"CONTROL friendly unprovoked patrol is not a hostile target");
        yield return ClearActors();Ready();target=Npc("Rusher",new Vector3(0,.1f,5),NpcRole.Criminal,1);bool killed=false;W.Powers.Hit+=r=>{if(r.Npc==target&&r.Killed&&r.Credit=="aerokinesis")killed=true;};W.Powers.Use(W.Powers.Selected);Check(killed&&target.Dead,"Lethal wind credits the player-owned kill through existing hit boundary");
        yield return EnterExpansion("sonic-manipulation");yield return ClearActors();Ready();target=Npc("Rusher",new Vector3(0,.1f,1.7f));target.enabled=true;
        for(float end=Time.time+4;target.Phase!=AttackPhase.Windup&&Time.time<end;)yield return null;
        Check(target.Phase==AttackPhase.Windup,"Actual enemy AI committed a readable windup before Sonic control");int cancels=target.Cancels;
        W.Powers.Use(W.Powers.Selected);Check(target.Cancels==cancels+1&&target.Rooted,"Sonic interrupts actual windup and applies short root");
        int interrupts=target.PowerInterrupts;
        for(int i=0;i<5;i++){Ready();W.Powers.Use(W.Powers.Selected);yield return null;}
        Check(target.PowerInterrupts==interrupts,"CONTROL immediate repeated Sonic cannot refresh interruption through refractory window");yield return new WaitForSeconds(.55f);
        Check(!target.Rooted,"Sonic control expires and ordinary AI resumes");
        yield return ClearActors();Ready();target=Npc("Pulsebreaker",new Vector3(0,.1f,6));W.Powers.Use(W.Powers.Selected);interrupts=target.PowerInterrupts;
        for(int i=0;i<8;i++){Ready();W.Powers.Use(W.Powers.Selected);yield return null;}
        Check(target.PowerInterrupts==interrupts,"Boss interruption has a long refractory window, not permanent stun");yield return new WaitForSeconds(.55f);Check(!target.Rooted,"Boss returns from finite resisted Sonic control");
        Ready();int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length;
        for(int i=0;i<40;i++){Ready();W.Powers.Use(W.Powers.Selected);}
        Check(Expansion.Vfx.LiveCount<=ExpansionVfx.Capacity,"Forty rapid effects remain inside fixed 32-slot mesh pool");
        Check(Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes&&Resources.FindObjectsOfTypeAll<Material>().Length==materials,"Repeated effects allocate no runtime meshes/materials");
        yield return new WaitForSeconds(1);Check(Expansion.Vfx.LiveCount==0,"All wind/sonic active and impact visuals expire");
        Ready();W.Powers.TrySpendEnergy(W.Powers.Energy);Check(!W.Powers.Use(W.Powers.Selected),"CONTROL insufficient energy refuses Sonic");
        Log("LIMIT: automated native damage/control/VFX checks, not human balance/readability or standalone performance approval.");
    }
}
#endif
