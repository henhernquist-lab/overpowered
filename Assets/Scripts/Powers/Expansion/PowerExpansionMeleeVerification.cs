#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;
public sealed partial class PowerExpansionVerificationRunner
{
    IEnumerator MeleeChecks()
    {
        Check(!Power("combat-morphing").Selectable&&!Power("plasma-blades").Selectable,"Melee replacements remain staged");
        yield return EnterExpansion("combat-morphing");yield return ClearActors();
        var capsule=W.Hero.GetComponent<CharacterController>();float height=capsule.height,radius=capsule.radius;
        var npc=Npc("Brute",new Vector3(0,.1f,2.1f));yield return null;
        string[] forms={"BladeArms","HammerFists","ExtendedClaws"};float[] loss=new float[3];
        for(int i=0;i<3;i++)
        {
            Ready();npc.Agent.Warp(new Vector3(0,0,2.1f));float hp=npc.CurrentHealth;int impacts=Expansion.MeleeImpacts;
            Check(Expansion.Form.ToString()==forms[i],"Intentional form "+forms[i]);
            Check(W.Powers.Use(W.Powers.Selected),forms[i]+": real activation accepted");
            Check(npc.CurrentHealth==hp,"CONTROL no damage before configured windup");
            Check(!W.Powers.Use(W.Powers.Selected),"CONTROL immediate melee repeat refused");
            yield return new WaitForSeconds(.52f);loss[i]=hp-npc.CurrentHealth;
            Check(loss[i]>0&&Expansion.MeleeImpacts==impacts+1,forms[i]+": exactly one deferred authoritative impact");
            Check(!Expansion.MeleePending&&!Expansion.DrivesMotion,"Finite strike/motion completes");
            Expansion.CycleForm();
        }
        Check(loss[1]>loss[0]&&loss[0]>loss[2],"Hammer, blade, claw have distinct damage budgets");
        Check(capsule.height==height&&capsule.radius==radius,"Morph never changes physics capsule");
        Check(W.Hero.GetComponentsInChildren<Animator>().Length==1,"Weapons add no duplicate Animator");
        Ready();float hpBefore=npc.CurrentHealth;W.Powers.Use(W.Powers.Selected);W.Powers.Select(W.Powers.PowerForSlot(2));yield return new WaitForSeconds(.5f);
        Check(npc.CurrentHealth==hpBefore,"CONTROL switching cancels pending morph damage");
        Check(!W.Hero.GetComponentsInChildren<MeshRenderer>().Any(x=>x.name.Contains("power weapon")&&x.enabled),"Switching away hides both weapons");
        yield return EnterExpansion("plasma-blades");yield return ClearActors();npc=Npc("Rusher",new Vector3(0,.1f,2));yield return null;
        var art=Expansion.Art;Check(art.MorphBlade!=art.MorphHammer&&art.MorphHammer!=art.MorphClaws&&art.PlasmaBlade!=null,"Four distinct real shared weapon meshes");
        for(int i=1;i<=3;i++){Ready();float hp=npc.CurrentHealth;Check(W.Powers.Use(W.Powers.Selected),"Plasma strike "+i+" accepted");yield return new WaitForSeconds(.32f);Check(Expansion.PlasmaStage==i&&npc.CurrentHealth<hp,"Plasma stage "+i+" impacts after finite windup");}
        Ready();float energy=W.Powers.Energy;float before=npc.CurrentHealth;Check(Expansion.ChargedStrike(),"Charged plasma accepted");Check(npc.CurrentHealth==before,"Charged strike requires commitment windup");
        Check(Mathf.Abs(W.Powers.Energy-(energy-W.Powers.EnergyCost(W.Powers.Selected)-8))<.01f,"Charged strike pays base plus extra energy exactly once");yield return new WaitForSeconds(.5f);
        Check(Mathf.Abs((before-npc.CurrentHealth)-W.Powers.Stats(W.Powers.Selected).Damage*2.2f*W.Difficulty.PlayerDamage)<.02f,"Charged plasma resolves exact 2.2x damage through centralized difficulty once");
        var view=Camera.main.GetComponent<ThirdPersonCamera>();view.ToggleView();yield return null;yield return null;
        var weapons=W.Hero.GetComponentsInChildren<MeshRenderer>().Where(x=>x.name.Contains("power weapon")).ToArray();
        Check(weapons.Length==2&&weapons.All(x=>x.enabled&&x.sharedMaterial==art.EffectMaterial&&x.bounds.size.magnitude>.2f&&x.bounds.size.magnitude<3),"Two fitted world-scale shared-material weapons visible in third person");
        view.ToggleView();yield return null;Check(weapons.All(x=>x.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),"First-person hides only weapon renderers, preserving skeleton");
        Ready();before=npc.CurrentHealth;W.MenuOpen=true;Check(!W.Powers.Use(W.Powers.Selected),"CONTROL menu blocks primary attack");W.MenuOpen=false;
        Ready();var wall=Wall(new Vector3(0,1,1),new Vector3(3,3,.25f));yield return null;W.Powers.Use(W.Powers.Selected);yield return new WaitForSeconds(.5f);Check(npc.CurrentHealth==before,"CONTROL solid wall blocks plasma melee");Destroy(wall);
        Log("LIMIT: original bounded low-poly weapon geometry and existing punch animation. Bespoke slash/transformation animation and human feel approval remain unverified.");
    }
}
#endif
