#if UNITY_EDITOR
using System.Collections;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class SecondaryVerificationRunner : SessionVerificationRunner
{
    public bool Reload;
    protected override string Folder=>"Verification/Secondary/";
    protected override string ResultFile=>Reload?"reload.txt":"results.txt";
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var hero=F.Heroes[0];var profile=Profile;
        var combat=new[]{"strength","fire","ice","telekinesis","darkness","laser-eyes","lightning","poison"};
        var secondary=new[]{"phase-shift","speed","force-field","teleport"};
        Check(combat.All(id=>Power(id)!=null&&Power(id).Role==PowerRole.Combat)&&
            secondary.All(id=>Power(id)!=null&&Power(id).Role==PowerRole.Secondary),"Exactly the requested eight combat and four secondary powers have their assigned roles.");
        Check(hero.AvailablePowers.Contains(Power("teleport"))&&F.Heroes.All(h=>h.AvailablePowers.Contains(Power("teleport"))),
            "Teleport is available in every hero roster.");
        if(Reload)
        {
            Check(profile.EquippedA==Power("fire")&&profile.EquippedB==Power("ice")&&profile.EquippedSecondary==Power("teleport"),
                "Separate process reload retains both attacks and Teleport secondary.");
            yield break;
        }
        Check(profile.EquippedA.Role==PowerRole.Combat&&profile.EquippedB.Role==PowerRole.Combat&&profile.EquippedSecondary.Role==PowerRole.Secondary,
            "Fresh or migrated profile has two attacks and one secondary.");
        var old=JsonUtility.FromJson<ProgressSave>(JsonUtility.ToJson(profile.Data));
        old.Loadout=new HeroLoadout{HeroId=hero.Id,PowerA="flight",PowerB="strength",Primary=hero.Primary,Secondary=hero.Secondary};
        string oldPath=Path.GetFullPath(Folder+"saves/old-pair-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(oldPath,JsonUtility.ToJson(old));
        var migrated=new GameObject("Old loadout migration control").AddComponent<PlayerProgression>();
        migrated.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),oldPath);
        Check(migrated.EquippedA.Role==PowerRole.Combat&&migrated.EquippedB.Role==PowerRole.Combat&&
            migrated.EquippedA!=migrated.EquippedB&&migrated.EquippedSecondary==Power("phase-shift")&&migrated.Owns(Power("strength")),
            "Old Flight + Strength save migrates to two combat attacks and Phase Shift secondary without losing ownership.");
        Destroy(migrated.gameObject);
        Check(!profile.SetLoadout(hero,Power("flight"),Power("fire"),Power("speed"),hero.Primary,hero.Secondary)&&
            !profile.SetLoadout(hero,Power("fire"),Power("ice"),Power("fire"),hero.Primary,hero.Secondary),
            "CONTROL category crossing is refused in both directions.");
        Check(F.Resolve(Power("strength"),Power("fire"),Power("flight"))==F.Resolve(Power("strength"),Power("flight")),
            "Legacy Strength + Flight synergy remains available across attack and secondary slots.");
        var screens=FindAnyObjectByType<ModeScreens>();
        var forge=screens.gameObject.AddComponent<HeroForgeScreen>();forge.Initialize(screens);
        Check(forge.SecondarySlot!=null,"Hero Forge displays a third secondary-power choice.");
        Check(forge.SelectPower(2,Power("force-field"))&&profile.EquippedSecondary==Power("force-field"),
            "Hero Forge saves the independently selected secondary.");
        forge.Close();
        foreach(var id in secondary)
        {
            yield return Home();
            Check(Profile.SetLoadout(hero,Power("fire"),Power("ice"),Power(id),hero.Primary,hero.Secondary),"Save Fire + Ice + "+id);
            GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));yield return Scene(GameFlow.CityScene);
            var user=W.Powers;var ability=user.SecondaryRuntime;
            Check(user.EquippedA==Power("fire")&&user.EquippedB==Power("ice")&&user.EquippedSecondary==Power(id)&&ability!=null,
                "Session receives two attacks plus "+id);
            Check(user.Synergy==F.Resolve(Power("fire"),Power("ice"))&&user.PowerForSlot(1).Definition==Power("fire")&&
                user.PowerForSlot(2).Definition==Power("ice")&&user.SlotNumber(ability)==0,
                "Fire + Ice synergy takes priority; attacks use keys 1/2 and secondary uses F.");
            var hud=FindAnyObjectByType<GameHud>();
            Check(hud!=null&&hud.Slots.Count(slot=>slot.Power!=null)==3&&hud.Slots.Any(slot=>slot.Power==ability&&slot.KeyText=="F"),
                "Gameplay HUD shows two attack powers and the separate F secondary slot.");
            Check(!user.Select(ability)&&user.Selected.Definition.Role==PowerRole.Combat,
                "CONTROL secondary cannot replace the selected LMB combat power.");
            Check(secondary.Where(other=>other!=id).All(other=>!user.IsEquipped(Power(other))),
                "CONTROL other secondary powers are unequipped.");
            Isolate();PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.2f);
            if(id=="phase-shift")
            {
                int before=ability.Charges;W.Hero.ScriptedSecondaryPress=true;yield return null;
                Check(user.Phase.Active&&ability.Charges==before-1,"F Phase Shift activates and spends one charge.");
                Check(!user.ConsumeFlight(.1f),"Retained Flight cannot spend fuel when removed from the loadout.");
            }
            else if(id=="speed")
            {
                int before=ability.Charges;
                W.Hero.ScriptedSecondaryPress=true;yield return null;
                Check(W.Hero.Dashing&&ability.Charges==before-1,
                    "F Speed starts its existing paid dash.");
                yield return new WaitForSeconds(.3f);
                Check(!W.Hero.Dashing,"Speed dash finishes through CharacterController movement.");
            }
            else if(id=="force-field")
            {
                int before=ability.Charges;
                W.MenuOpen=true;W.Hero.ScriptedSecondaryPress=true;yield return null;W.MenuOpen=false;
                Check(ability.Charges==before,"CONTROL secondary F input is refused while the menu is open.");
                W.Hero.ScriptedSecondaryPress=true;yield return null;
                Check(user.Shield!=null&&user.Shield.Up&&ability.Charges==before-1,
                    "F Force Field raises the existing paid shield.");
                Check(!user.UseSecondary()&&ability.Charges==before-1,"CONTROL raised shield refuses duplicate activation.");
            }
            else
            {
                Cam.transform.position=new Vector3(12,155,-9);Cam.transform.LookAt(new Vector3(0,151,4));
                var before=W.Hero.transform.position;int charges=ability.Charges;
                int visualBefore=PowerVfx.Get().Draws;
                W.Hero.ScriptedSecondaryPress=true;yield return null;
                Check(Vector3.Distance(before,W.Hero.transform.position)>7f&&ability.Charges==charges-1,
                    "F Teleport moves to clear supported ground and pays one charge.");
                Check(PowerVfx.Get().Draws==visualBefore+2,"Teleport draws one bounded departure pop and one arrival pop, without a connecting trail.");
                Capture("teleport-departure-arrival.png");
                PlaceHero(new Vector3(0,150.05f,0),true);yield return new WaitForSeconds(.2f);
                Vector3 direction=Vector3.ProjectOnPlane(Cam.transform.forward,Vector3.up).normalized;
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position=W.Hero.transform.position+direction*4+Vector3.up;
                wall.transform.localScale=new Vector3(4,2,1);wall.transform.rotation=Quaternion.LookRotation(direction);
                Physics.SyncTransforms();ability.Cooldown=0;charges=ability.Charges;before=W.Hero.transform.position;
                visualBefore=PowerVfx.Get().Draws;
                Check(!user.UseSecondary()&&ability.Charges==charges&&Vector3.Distance(before,W.Hero.transform.position)<.01f,
                    "CONTROL Teleport cannot pass a wall or spend a charge on blocked travel.");
                Check(PowerVfx.Get().Draws==visualBefore,"Blocked Teleport shows no misleading departure or arrival pop.");
                Destroy(wall);
            }
        }
    }
}
#endif
