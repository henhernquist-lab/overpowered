#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class CombatLabVerificationRunner
{
    PowerDefinition Definition(string id)=>Resources.Load<PowerDefinition>("Powers/"+id);
    IEnumerator LoadProfile(string primary,string secondary,string difficulty,string signature)
    {
        // Uses real ownership, Forge loadout and signature gates on a temporary, non-session profile.
        var go=new GameObject("Matrix profile");var profile=go.AddComponent<PlayerProgression>();
        var definitions=Resources.LoadAll<PowerDefinition>("Powers");
        profile.InitializeTransient(W.Tuning.Progression,definitions,new ProgressSave{Points=10000});
        foreach(var definition in definitions)if(!profile.Owns(definition))Check(profile.Buy(definition),"Fixture buys "+definition.Id);
        Check(profile.SetLoadout(Resources.Load<ForgeCatalog>("ForgeCatalog").Hero("vector"),Definition(primary),Definition(primary=="fire"?"ice":"fire"),Definition(secondary),CityColor.Blue,CityColor.Cyan),"Real Forge gates equip "+primary+" / "+secondary);
        if(signature!=null)Check(profile.SetSignature(signature),"Real signature gate selects "+signature);
        Check(profile.SetDifficulty(difficulty),"Select fixture difficulty "+difficulty);
        File.WriteAllText(WorldSession.VerificationSavePath,JsonUtility.ToJson(profile.Data));Destroy(go);
        SceneManager.LoadScene(CombatLab.SceneName);yield return null;yield return null;yield return new WaitForSeconds(.2f);
        Check(W.Difficulty.Id==difficulty&&W.Powers.EquippedA.Id==primary&&W.Progression.SavePath==null,"Fresh immutable lab snapshot "+difficulty+" / "+primary);
        W.Rolls.FixedUnit=.5f;PlaceHero(Vector3.zero);
        Camera.main.GetComponent<ThirdPersonCamera>().enabled=false;
    }
    void AimAt(Vector3 point)
    {
        Camera.main.transform.position=W.Hero.transform.position+Vector3.up*1.7f-Vector3.forward;
        Camera.main.transform.LookAt(point);W.Hero.transform.forward=Vector3.forward;Physics.SyncTransforms();
    }
    CityNpc Fixture(string kind,float distance)
    {
        var npc=At(kind,new Vector3(0,0,distance),10000,0);
        if(npc==null)throw new Exception("Fixture spawn "+kind);
        // Stationary vulnerability fixture; movement/guarding are exercised independently by Run.
        npc.enabled=false;npc.Agent.ResetPath();npc.transform.forward=Vector3.back;
        AimAt(npc.GetComponent<Collider>().bounds.center);return npc;
    }
    IEnumerator PowerMatrix()
    {
        var signatures=Resources.Load<SignatureCatalog>("SignatureCatalog");
        foreach(string difficulty in new[]{"normal","hard","extreme"})
        foreach(string kind in new[]{"Skirmisher","Bulwark"})
        foreach(var signature in signatures.Abilities)
        {
            string primary=signature.OwnerPrimary.Id;
            yield return LoadProfile(primary,"speed",difficulty,signature.Id);
            var runtime=W.Powers.PowerForSlot(1);var npc=Fixture(kind,primary=="strength"?1.3f:6f);float before=npc.Health;
            int received=0;NpcDamageResult last=default;GameObject thrownFixture=null;
            Action<NpcDamageResult> handler=r=>{if(r.Target==npc){received++;last=r;}};
            CityNpc.DamageApplied+=handler;
            try
            {
                Check(W.Powers.Select(runtime),"Equipped primary selected "+primary);
                if(primary=="telekinesis")
                {
                    var crate=GameObject.CreatePrimitive(PrimitiveType.Cube);thrownFixture=crate;crate.name="Matrix shared throwable";crate.transform.position=new Vector3(0,.7f,3);crate.transform.localScale=Vector3.one*.8f;
                    crate.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);crate.AddComponent<Rigidbody>().mass=20;
                    AimAt(crate.transform.position);Check(W.Powers.Use(runtime),"Telekinesis grabs through ownership gates");
                    yield return new WaitForFixedUpdate();AimAt(npc.GetComponent<Collider>().bounds.center);W.Powers.Release(true);
                }
                else Check(W.Powers.Use(runtime),"Primary activation "+primary+" vs "+kind+" / "+difficulty);
                if(primary=="laser-eyes")W.Powers.ScriptedHold=true;
                yield return new WaitForSeconds(primary=="poison"?1.3f:1f);
                W.Powers.ScriptedHold=false;W.Powers.EndChannel();
                if(primary=="ice")Check(npc.Frozen,"Ice controls "+kind);
                else Check(npc.Health<before&&received>0,"Primary damage reaches authoritative health: "+primary+" / "+kind+" / "+difficulty);
                if(received>0)Check(Mathf.Abs(last.HealthLost-(last.HealthBefore-last.HealthAfter))<.01f&&last.ResolvedDamage<=last.DamageBeforeGuard+.01f,"Resolved damage metadata stays consistent");
                if(primary=="darkness")Check(npc.Rooted,"Darkness controls "+kind);
                var hud=FindAnyObjectByType<GameHud>();
                if(received>0)
                {
                    Check(hud.EnemyDamageHistory.Any(r=>r.Target==npc),"Existing floating-number path receives damage");
                    Check(hud.EnemyBars.Any(b=>b.Target==npc&&Mathf.Abs(b.ShownHealth-npc.Health)<.1f),"Existing health bar reads real HP");
                }
            }
            finally{if(thrownFixture!=null)Destroy(thrownFixture);CityNpc.DamageApplied-=handler;W.Powers.ScriptedHold=false;W.Powers.EndChannel();}
            Lab.Clear();yield return null;PlaceHero(Vector3.zero);W.Powers.RestorePracticeEnergy();
            npc=Fixture(kind,primary=="strength"?2f:6f);before=npc.Health;
            var runner=W.Powers.SynergyRunner;
            Check(runner.Definition==signature&&runner.TryActivate(),"Signature activation "+signature.Id+" / "+kind+" / "+difficulty);
            // Glacial Prison's supported repeat action performs its follow-up shatter.
            if(signature.Effect is GlacialPrisonEffect){yield return new WaitForSeconds(1.25f);runner.TryActivate();}
            float end=Time.time+14;while(runner.Busy&&Time.time<end)yield return null;
            Check(!runner.Busy,"Signature lifetime bounded: "+signature.Id);
            Check(npc.Health<before,"Signature damage reaches "+kind+": "+signature.Id+" / "+difficulty);
            runner.Cancel();yield return new WaitForSeconds(.25f);
            Check(npc.Dead||npc.Agent.enabled&&npc.Agent.isOnNavMesh,"Signature leaves valid navigation: "+signature.Id);
            var pool=SignatureVfx.Existing;
            Check(pool==null||pool.ActiveBlooms==0&&!pool.PrisonVisible&&!pool.StormVisible&&!pool.SolarWakeVisible,"Signature cancellation releases persistent presentation");
        }
        var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");var hero=forge.Hero("vector");
        var utilities=Resources.LoadAll<PowerDefinition>("Powers").Where(d=>forge.AllowedSecondary(hero,d)).ToArray();
        Check(utilities.Length==4,"Only the four current Forge secondary choices are tested; legacy Flight remains unavailable");
        Check(!forge.AllowedSecondary(hero,Definition("flight")),"Legacy Flight is refused by the same equipment gate in practice");
        foreach(var utility in utilities)
        {
            yield return LoadProfile("fire",utility.Id,"normal",null);
            var runtime=W.Powers.SecondaryRuntime;var npc=Fixture("Bulwark",6);
            // Teleport needs a clear grounded route. The old forward fixture intentionally blocked that route.
            yield return new WaitForSeconds(.15f);
            if(utility.Effect.IsFlight)
            {
                PlaceHero(Vector3.up*5);float fuel=runtime.Fuel;W.Hero.ScriptedFlight=true;yield return new WaitForSeconds(.35f);W.Hero.ScriptedFlight=false;
                Check(runtime.Fuel<fuel,"Equipped Flight works in lab");
            }
            else
            {
                AimAt(new Vector3(12,1.3f,0));Check(W.Powers.UseSecondary(),"Equipped secondary activates: "+utility.Id+" ("+W.Powers.Message+")");
                yield return new WaitForSeconds(.25f);
                if(utility.Effect is PhaseShiftEffect){Check(W.Powers.Phase.Active,"Phase remains active");W.Powers.Phase.End();}
                if(utility.Effect is ForceFieldEffect){float hp=W.Health;W.DamagePlayer(5);Check(W.Health==hp,"Force Field absorbs through authoritative health path");}
            }
        }
        Lab.Clear();Lab.ResetArena();yield return null;yield return null;
        Check(W.Npcs.Count==0&&SignatureVfx.Existing==null,"Scene reset removes test NPCs and signature pool");
    }
}
#endif
