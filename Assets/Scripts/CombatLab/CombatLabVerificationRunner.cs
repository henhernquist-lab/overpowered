#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed partial class CombatLabVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;public bool Matrix,Reload;string runtimeError;
    void OnEnable(){Application.logMessageReceived+=Logged;}
    void OnDisable(){Application.logMessageReceived-=Logged;}
    void Logged(string text,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error)runtimeError=text+"\n"+trace;}
    readonly List<string> lines=new List<string>();int failures;
    CombatLab Lab=>CombatLab.Active;
    WorldSession W=>Lab.World;
    void Check(bool ok,string label){string line=(ok?"PASS ":"FAIL ")+label;lines.Add(line);Debug.Log(line);if(!ok)failures++;}
    IEnumerator Start()
    {
        yield return null;yield return null;
        var stack=new Stack<IEnumerator>();stack.Push(Matrix?PowerMatrix():Reload?ReloadChecks():Run());
        while(stack.Count>0)
        {
            bool next=false;object yielded=null;
            try{if(runtimeError!=null)throw new Exception(runtimeError);next=stack.Peek().MoveNext();if(next)yielded=stack.Peek().Current;}
            catch(Exception e){Check(false,e.ToString());break;}
            if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(yielded is IEnumerator nested)stack.Push(nested);else yield return yielded;
        }
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        Directory.CreateDirectory("Verification/CombatLab");File.WriteAllLines("Verification/CombatLab/"+(Matrix?"power-matrix.txt":Reload?"reload.txt":"combat-lab.txt"),lines);
        Debug.Log($"Combat Lab verification: {lines.Count-failures} PASS, {failures} FAIL");Finished?.Invoke(failures==0?0:1);
    }
    IEnumerator ReloadChecks()
    {
        string source=WorldSession.VerificationSavePath;string before=File.ReadAllText(source);
        var saved=JsonUtility.FromJson<ProgressSave>(before);
        Check(W.IsPractice&&W.Progression.SavePath==null,"Separate-process reload remains transient");
        Check(W.Progression.Data.Level==saved.Level&&W.Progression.Data.Xp==saved.Xp&&!W.Progression.HintSeen("lab-only")&&W.Progression.Data.FirstPerson==saved.FirstPerson,"Practice XP/hints/view do not survive process reload");
        W.Progression.AddXp(500);W.Progression.Save();
        Check(File.ReadAllText(source)==before,"Separate-process practice actions still cannot write source");
        Check(W.Mode==null&&W.Npcs.Count==0&&W.Heat==0,"Reload starts clean without mode, automatic spawns or Heat");
        yield return null;
    }
    IEnumerator Run()
    {
        string source=WorldSession.VerificationSavePath;string before=File.ReadAllText(source);
        Check(Lab!=null&&W.IsPractice&&W.Mode==null,"Dedicated practice session has no mode/director");
        Check(W.Progression.SavePath==null,"Practice profile has no filesystem save target");
        Check(W.City.Plan==null&&W.City.Buildings.Count==0&&W.Npcs.Count==0,"No procedural city or automatic enemies");
        Check(W.Powers.EquippedA==W.Progression.EquippedA&&W.Powers.EquippedB==W.Progression.EquippedB,"Lab keeps current equipped pair");
        Check(W.Powers.Synergy==W.Progression.EquippedSignature,"Lab keeps current signature");
        W.Rolls.FixedUnit=.5f;
        for(int i=0;i<Lab.Settings.Archetypes.Length;i++)
        {
            Check(Lab.Select(i),"Select "+Lab.Settings.Archetypes[i].DisplayName);
            var npc=Lab.SpawnOne();Check(npc!=null,"Spawn "+Lab.Settings.Archetypes[i].DisplayName);
            if(npc==null)continue;
            Check(npc.Hostile&&npc.Agent.enabled&&npc.Agent.isOnNavMesh,"Faction and valid navigation: "+npc.Archetype.DisplayName);
            Check(Mathf.Approximately(npc.MaxHealth,W.Tuning.Npcs.CopHealth*npc.Archetype.HealthMultiplier*W.Difficulty.EnemyHealth),"Central health + difficulty applied once: "+npc.Archetype.DisplayName);
            Check(npc.GetComponent<HumanoidPresentation>().Animator.runtimeAnimatorController==Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning").Controller,"Existing rigged Animator: "+npc.Archetype.DisplayName);
            int count=0;NpcDamageResult result=default;Action<NpcDamageResult> handler=r=>{if(r.Target==npc){count++;result=r;}};
            CityNpc.DamageApplied+=handler;
            float hp=npc.Health;npc.Damage(4,W.Powers);Check(count==1&&Mathf.Approximately(result.HealthLost,hp-npc.Health),"One resolved damage result: "+npc.Archetype.DisplayName);
            npc.Damage(100000,W.Powers);npc.Damage(100000,W.Powers);Check(npc.Dead&&count==2,"Death is terminal without duplicate events: "+npc.Archetype.DisplayName);
            CityNpc.DamageApplied-=handler;
        }
        Lab.Clear();yield return null;
        Check(Lab.ActiveCount==0,"Clear all lab-owned enemies");
        var outsider=CityNpc.Spawn(W,new Vector3(20,0,0),NpcRole.Criminal,Resources.Load<EnemyArchetype>("Enemies/Rusher"));
        Lab.SpawnGroup();Lab.Clear();yield return null;
        Check(outsider!=null&&!outsider.Dead,"Clear leaves non-owned actors alone");if(outsider!=null)Destroy(outsider.gameObject);
        yield return null;
        for(int i=0;i<30;i++)Lab.SpawnOne();Check(Lab.ActiveCount==Mathf.Clamp(Lab.Settings.MaxEnemies,1,24),"Spawn count remains capped");
        Lab.Clear();yield return null;
        W.DamagePlayer(10,true);Check(W.RestorePracticeHealth()&&Mathf.Approximately(W.Health,W.MaxHealth),"Restore health");
        W.Powers.TrySpendEnergy(10);Check(W.Powers.RestorePracticeEnergy()&&Mathf.Approximately(W.Powers.Energy,W.Powers.MaxEnergy),"Restore energy");
        W.AddHeat(5);W.Progression.AddXp(100);W.Progression.MarkHintSeen("lab-only");W.Progression.SetFirstPerson(true);W.Progression.Save();
        Check(W.Heat==0&&File.ReadAllText(source)==before,"Practice actions cannot write snapshot source or Heat");
        VerifyWaveData();
        yield return VerifySkirmisher();
        Lab.Clear();yield return null;
        yield return VerifyBulwark();
        Lab.Clear();yield return null;
        Lab.ResetArena();yield return null;yield return null;yield return null;
        Check(Lab!=null&&W.Npcs.Count==0&&W.Progression.Data.Xp==0,"Reset rebuilds scene and drops practice rewards");
        Check(File.ReadAllText(source)==before,"Reset leaves read-only snapshot source byte-identical");
    }
    void PlaceHero(Vector3 position)
    {
        var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=position;cc.enabled=true;W.Hero.ResetMotion();
    }
    CityNpc At(string archetype,Vector3 position,float hp=1000,float damage=0)
    {
        var npc=CityNpc.Spawn(W,position,NpcRole.Criminal,Resources.Load<EnemyArchetype>("Enemies/"+archetype));
        if(npc!=null){npc.SetCombatStats(hp,damage);npc.AlwaysAggro=true;Lab.Owned.Add(npc);}return npc;
    }
    IEnumerator VerifySkirmisher()
    {
        PlaceHero(Vector3.zero);var npc=At("Skirmisher",new Vector3(0,0,6),1000,5);float playerBefore=W.Health;
        Check(npc!=null&&npc.Evasive,"Skirmisher registered with evasive behavior");if(npc==null)yield break;
        float end=Time.time+18;bool hitWhileMoving=false,recovered=false;int maximum=0;float hp=npc.Health;
        while(Time.time<end&&npc!=null&&!npc.Dead)
        {
            maximum=Mathf.Max(maximum,npc.Dodges+npc.Dashes);
            if(npc.EvasionState==EvasionPhase.Travel&&!hitWhileMoving){npc.Damage(1,W.Powers);hitWhileMoving=npc.Health<hp;}
            if(npc.EvasionState==EvasionPhase.Recover)recovered=true;
            yield return null;
        }
        Check(maximum>0,"Skirmisher performs a dodge/dash");Check(maximum<=5,"Evasion cooldown bounds frequency");
        Check(hitWhileMoving,"Skirmisher remains hittable during travel");Check(recovered,"Evasion exposes finite recovery");
        Check(npc.Releases>=2&&npc.Retreats>0,"Rapid melee releases and retreat occur");
        Check(npc.Hits>0&&W.Health<playerBefore,"Skirmisher burst removes real player health through the shared damage path");
        npc.Root(.5f);Vector3 at=npc.transform.position;yield return new WaitForSeconds(.25f);
        Check(Vector3.Distance(at,npc.transform.position)<.15f&&npc.EvasionState==EvasionPhase.None,"Darkness-style root prevents evasion");
        npc.Freeze(.4f);yield return null;Check(npc.Frozen&&npc.EvasionState==EvasionPhase.None,"Ice interrupts evasion/burst");
        yield return new WaitForSeconds(.65f);Check(!npc.Frozen&&!npc.Rooted&&npc.Agent.enabled&&npc.Agent.isOnNavMesh,"Control expires without navigation corruption");
        npc.Damage(100000,W.Powers);Check(npc.Dead,"Skirmisher can die normally");W.RestorePracticeHealth();
    }
}
#endif
