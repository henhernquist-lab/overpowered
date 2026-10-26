#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;
public sealed partial class PowerExpansionVerificationRunner
{
    IEnumerator EnterSecondary(string id)
    {
        yield return Home();var p=Power(id);p.Selectable=true;
        foreach(var h in F.Heroes)if(!h.AvailablePowers.Contains(p))h.AvailablePowers=h.AvailablePowers.Concat(new[]{p}).ToArray();
        Check(Profile.SetLoadout(F.Heroes[0],Power("fire"),Power("ice"),p,F.Heroes[0].Primary,F.Heroes[0].Secondary),"Actual staged secondary equip gates: "+id);
        Check(GameFlow.Instance.OpenCombatLab(),"Enter Combat Lab with staged secondary");yield return Scene(CombatLab.SceneName);
        var view=Camera.main.GetComponent<ThirdPersonCamera>();if(!view.FirstPerson)view.ToggleView();view.SetLook(0,0);yield return null;yield return ClearActors();
    }
    void ReadySecondary(){W.Powers.SecondaryRuntime.Cooldown=0;W.Powers.SecondaryRuntime.Charges=3;W.Powers.RestorePracticeEnergy();}
    IEnumerator SecondaryChecks()
    {
        Check(!Power("grapple-line").Selectable&&!Power("mirage-decoy").Selectable,"Traversal/distraction remain hidden until full acceptance");
        yield return EnterSecondary("grapple-line");ReadySecondary();
        Check(!W.Powers.UseSecondary(),"CONTROL empty aiming lane refuses invalid grapple without payment");
        var wall=Wall(new Vector3(0,3,14),new Vector3(8,8,.5f));yield return null;
        Vector3 before=W.Hero.transform.position;float energy=W.Powers.Energy;
        Check(W.Powers.UseSecondary(),"First-person actual grapple activation on static world collider");Check(Expansion.Grappling&&W.Hero.AbilityDriving,"Grapple owns movement through existing controller contract");
        Check(Mathf.Abs(W.Powers.Energy-(energy-W.Powers.EnergyCost(W.Powers.SecondaryRuntime)))<.01f,"Grapple pays established secondary energy once");
        yield return new WaitForSeconds(.5f);
        Check(W.Hero.transform.position.z>before.z+1&&W.Hero.transform.position.z<13.5f,"Grapple traverses actual space with capsule collision, no teleport");
        Check(Expansion.GrappleVelocity.magnitude<=22.01f,"Grapple speed remains bounded");Expansion.ReleaseGrapple();yield return null;Check(!Expansion.Grappling&&!Expansion.DrivesMotion,"Intentional release restores ordinary control");
        ReadySecondary();Check(W.Powers.UseSecondary(),"Grapple can reanchor");Destroy(wall);yield return null;yield return null;Check(!Expansion.Grappling&&!Expansion.DrivesMotion,"Destroyed anchor cannot leave stuck traversal");
        yield return EnterSecondary("grapple-line");ReadySecondary();var view=Camera.main.GetComponent<ThirdPersonCamera>();view.ToggleView();view.SetLook(0,0);yield return null;
        wall=Wall(new Vector3(0,3,14),new Vector3(8,8,.5f));yield return null;Check(W.Powers.UseSecondary(),"Third-person crosshair grapple also anchors");
        var blocker=Wall(new Vector3(0,2,4),new Vector3(8,5,.4f));yield return new WaitForSeconds(.2f);Check(!Expansion.Grappling,"CONTROL new intervening wall releases tether");Destroy(wall);Destroy(blocker);
        yield return EnterSecondary("mirage-decoy");ReadySecondary();Check(W.Powers.UseSecondary(),"Mirage summoned through real secondary payment path");yield return null;
        var decoy=Expansion.Decoy;Check(decoy!=null&&decoy.Alive,"One finite hologram active");
        Check(decoy.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0&&decoy.GetComponentsInChildren<Animator>().Length==0&&decoy.GetComponent<CityNpc>()==null&&decoy.GetComponent<PowerUser>()==null,"Actual hero shared skinned meshes without Animator/player/NPC/progression clone");
        Check(decoy.GetComponentsInChildren<SkinnedMeshRenderer>().All(x=>x.sharedMaterials.All(m=>m==Expansion.Art.Hologram)),"All hologram surfaces use authored shared translucent shimmer material");
        var npc=Npc("Rusher",decoy.transform.position+Vector3.back*1.6f);npc.SetCombatStats(10000,10);npc.enabled=true;
        float health=W.Health;int xp=W.Progression.Data.Xp;
        for(float end=Time.time+5;npc.Releases==0&&Time.time<end;)yield return null;
        Check(npc.TargetingDecoy&&npc.Releases>0&&decoy.ReceivedHits>0,"Real eligible AI redirects windup and actual attack release to decoy");
        Check(W.Health==health&&W.Progression.Data.Xp==xp,"Decoy attacks do not damage/heal player or grant XP");
        var boss=Npc("Pulsebreaker",decoy.transform.position+Vector3.forward*3);Check(!boss.TargetingDecoy,"Pulsebreaker remains resistant to illusion target substitution");
        ReadySecondary();Check(W.Powers.UseSecondary(),"Replacing decoy is finite");yield return null;Check(Object.FindObjectsByType<MirageDecoy>(FindObjectsSortMode.None).Length==1,"At most one hologram after replacement cleanup");
        decoy=Expansion.Decoy;decoy.Hit(100);yield return null;Check(!npc.TargetingDecoy&&MirageDecoy.Current==null,"Destroyed decoy returns valid hero acquisition");
        ReadySecondary();W.Powers.UseSecondary();decoy=Expansion.Decoy;yield return new WaitForSeconds(6.2f);Check(decoy==null&&MirageDecoy.Current==null,"Natural expiry removes hologram and aggro lease");
        Check(W.Progression.Data.Xp==xp,"Hologram death/expiry grants no progression");
        ReadySecondary();W.Powers.UseSecondary();W.MenuOpen=true;yield return null;yield return null;Check(Expansion.Decoy==null,"Menu cancellation cleans finite decoy");W.MenuOpen=false;
        Log("LIMIT: native target, traversal and lifecycle checks; human swing feel and visual approval remain unverified.");
    }
}
#endif
