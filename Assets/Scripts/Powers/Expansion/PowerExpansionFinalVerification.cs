#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
public sealed partial class PowerExpansionVerificationRunner
{
    const string SaveDir="Verification/PowerExpansion2/saves/";
    string ProtectedSave(ProgressSave original){var copy=JsonUtility.FromJson<ProgressSave>(JsonUtility.ToJson(original));copy.Loadout=null;copy.Powers=null;return JsonUtility.ToJson(copy);}
    IEnumerator FinalChecks()
    {
        var registry=Resources.Load<SignatureCatalog>("SignatureCatalog");var all=Resources.LoadAll<PowerDefinition>("Powers");
        var primaries=all.Where(p=>p.Selectable&&p.Role==PowerRole.Combat).OrderBy(p=>p.Id).ToArray();var secondaries=all.Where(p=>p.Selectable&&p.Role==PowerRole.Secondary).OrderBy(p=>p.Id).ToArray();
        Check(primaries.Length==10&&secondaries.Length==6&&registry.Abilities.Length==10&&registry.ExpectedPrimaries==10&&registry.Complete&&registry.Enabled,"Actual shipping resources contain exactly 10 primaries, 6 secondaries and 10 complete signatures");
        Check(new[]{"strength","laser-eyes","flight"}.All(id=>Power(id)!=null&&!Power(id).Selectable),"Legacy resources retained while retired from normal selection");
        Check(registry.Abilities.All(a=>a.Effect!=null&&a.OwnerPrimary.Selectable)&&registry.Abilities.All(a=>a.Id!="seismic-slam"&&a.Id!="prism-barrage"),"Every current primary has implemented signature; retired signatures unexposed");
        foreach(var hero in F.Heroes)
        {
            Check(hero.AvailablePowers.Count(p=>p.Selectable&&p.Role==PowerRole.Combat)==10&&hero.AvailablePowers.Count(p=>p.Selectable&&p.Role==PowerRole.Secondary)==6,hero.Id+": same complete selectable roster");
            for(int i=0;i<10;i++)for(int j=i+1;j<10;j++)
            {
                var choices=registry.Choices(primaries[i],primaries[j]);
                bool valid=Profile.SetLoadout(hero,primaries[i],primaries[j],secondaries[(i+j)%6],hero.Primary,hero.Secondary)&&choices.Count==2&&choices[0]!=choices[1]&&choices.Select(c=>c.OwnerPrimary).Contains(primaries[i])&&choices.Select(c=>c.OwnerPrimary).Contains(primaries[j]);
                foreach(var choice in choices)valid&=Profile.SetSignature(choice.Id)&&Profile.EquippedSignature==choice;
                Check(valid,hero.Id+": unordered pair "+primaries[i].Id+" + "+primaries[j].Id+" equips and provides exactly two selectable owner signatures");
            }
        }
        Check(!Profile.SetLoadout(F.Heroes[0],Power("strength"),Power("fire"),Power("speed"),CityColor.Blue,CityColor.Cyan)&&!Profile.SetLoadout(F.Heroes[0],Power("fire"),Power("fire"),Power("speed"),CityColor.Blue,CityColor.Cyan),"CONTROL retired primaries and duplicate slots rejected by actual save API");
        var menu=FindAnyObjectByType<ModeScreens>();using(var e=NavigationSubmitEvent.GetPooled()){e.target=menu.ForgeButton;menu.ForgeButton.SendEvent(e);}yield return null;
        var screen=menu.ForgeScreen;Check(screen!=null&&screen.SlotA!=null&&screen.SlotB!=null&&screen.SecondarySlot!=null,"Existing live Hero Forge builds all three power selectors");
        Check(screen.SlotA.choices.Count==10&&screen.SlotB.choices.Count==9&&screen.SecondarySlot.choices.Count==6,"Actual UI choices expose ten primaries, exclude duplicate slot, and expose six secondaries");
        foreach(var hero in F.Heroes)
        {
            Check(screen.SelectHero(hero),hero.Id+": existing Forge hero selection");
            var appearance=Profile.Appearance(hero);string before=JsonUtility.ToJson(appearance);
            Check(screen.SelectPower(0,Power("combat-morphing"))&&screen.SelectPower(1,Power("plasma-blades"))&&screen.SelectPower(2,Power("mirage-decoy")),hero.Id+": new primary and secondary choices work through actual Forge UI handlers");
            Check(JsonUtility.ToJson(Profile.Appearance(hero))==before,hero.Id+": changing powers preserves complete appearance selection");
            Check(screen.Root.Query<Button>().ToList().Count(x=>x.name!=null&&x.name.StartsWith("signature-")&&x.text.StartsWith("SELECTED"))==1,hero.Id+": existing Forge displays exactly one equipped signature");
            Check(screen.Root.Q<Label>("forge-power-info-combat-morphing")!=null&&screen.Root.Q<Label>("forge-power-info-plasma-blades")!=null&&screen.Root.Q<Label>("forge-power-info-mirage-decoy")!=null,hero.Id+": descriptions and real base costs/cooldowns appear for all equipped powers");
            Check(!screen.SelectPower(0,Power("laser-eyes"))&&!screen.SelectPower(0,Power("strength")),"CONTROL existing UI refuses both retired primaries");
        }
        screen.Close();
        Check(menu.Root.Query<MenuIcon>().ToList().Count(x=>x.name!=null&&x.name.StartsWith("owned-"))==16&&!menu.Root.Query<MenuIcon>().ToList().Any(x=>x.name=="owned-strength"||x.name=="owned-laser-eyes"||x.name=="owned-flight"),"Home owned-power strip exposes exactly the current sixteen powers, preserving legacy records only in the save");
        foreach(var current in all.Where(x=>x.Selectable))Profile.Data.Powers.Find(x=>x.Id==current.Id).Tier=current.Upgrades.Length;
        Profile.Data.Powers.Find(x=>x.Id=="fire").Tier=0;Profile.Data.Points=9;Profile.Save();
        GameFlow.Instance.Results(new SessionResult{ModeId="endless-fight",ModeName="ENDLESS FIGHT",StartLevel=1,EndLevel=2,Xp=250,Layout=ResultsLayout.Survival,Side=PlayerSide.Hero});yield return Scene(GameFlow.ResultsScene);
        menu=FindAnyObjectByType<ModeScreens>();for(float end=Time.unscaledTime+5;!menu.RewardVisible&&Time.unscaledTime<end;)yield return null;
        Check(menu.RewardVisible&&menu.UpgradeButtons.Count==1&&menu.UpgradeButtons[0].name=="upgrade-fire","Results offers the one remaining current upgrade and no retired resources, even when all current alternatives are maxed");
        int points=menu.Profile.Data.Points;
        Check(!menu.ChooseUpgrade(Power("strength"))&&!menu.ChooseUpgrade(Power("laser-eyes"))&&menu.Profile.Data.Points==points,"CONTROL results rejects retired upgrade choices without spending progression points");
        Check(menu.ChooseUpgrade(Power("fire"))&&menu.Profile.Tier(Power("fire"))==1&&menu.Profile.Data.Points==points-Power("fire").Upgrades[0].PointCost,"Current power upgrade still uses the established payment and ownership path");
        yield return Home();
        foreach(string legacy in new[]{"strength","laser-eyes","duplicate","unknown"})
        {
            var data=JsonUtility.FromJson<ProgressSave>(JsonUtility.ToJson(Profile.Data));
            data.Level=7;data.Xp=31;data.Points=9;data.DifficultyId="hard";data.FirstPerson=true;data.IntroSeen=true;
            data.ModeRecords.Add(new ModeRecord{Id="future-record",BestScore=1234,Runs=3});data.PowerStats.Add(new PowerUsage{Id="strength",Uses=13,Hits=9,Kills=4,Damage=219});
            data.ParkourCourses.Add(new ParkourCourseRecord{Id="future-course",Completed=true,RewardClaimed=true,Completions=2,BestSeconds=27.25f});
            data.HeroAppearances.Add(new HeroAppearanceSave{HeroId="future-hero",SuitId="future-suit",MaskId="future-mask",EmblemId="future-emblem",Primary=CityColor.Red,Secondary=CityColor.Cyan,Accent=CityColor.Amber});
            data.Powers.RemoveAll(p=>p.Id=="combat-morphing"||p.Id=="plasma-blades"||p.Id=="strength"||p.Id=="laser-eyes");
            data.Powers.Add(new PowerOwnership{Id="strength",Tier=2});data.Powers.Add(new PowerOwnership{Id="laser-eyes",Tier=1});data.Powers.Add(new PowerOwnership{Id="future-power",Tier=17});
            data.Loadout=new HeroLoadout{HeroId="titan",PowerA=legacy=="laser-eyes"?"laser-eyes":legacy=="unknown"?"unknown-primary":"strength",PowerB=legacy=="duplicate"?"combat-morphing":"ice",SecondaryPower="grapple-line",SignatureId=legacy=="laser-eyes"?"prism-barrage":"seismic-slam",Primary=CityColor.Teal,Secondary=CityColor.Amber};
            string protectedBefore=ProtectedSave(data),path=Path.GetFullPath(SaveDir+legacy+".json");File.WriteAllText(path,JsonUtility.ToJson(data));
            var go=new GameObject("Legacy save control");var profile=go.AddComponent<PlayerProgression>();profile.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,all,path);
            Check(profile.Data.Version==1&&profile.SelectedHero.Id=="titan"&&profile.Data.Loadout.Primary==CityColor.Teal&&profile.Data.Loadout.Secondary==CityColor.Amber&&profile.EquippedSecondary.Id=="grapple-line",legacy+": outer version, hero, palette colors and secondary survive actual legacy load");
            Check(ProtectedSave(profile.Data)==protectedBefore,legacy+": exact XP/level/points/difficulty/identity/cosmetics/stats/records/parkour/unknown records preserved");
            Check(profile.Tier(Power("combat-morphing"))==2&&profile.Tier(Power("plasma-blades"))==1&&profile.Data.Powers.Any(p=>p.Id=="strength"&&p.Tier==2)&&profile.Data.Powers.Any(p=>p.Id=="laser-eyes"&&p.Tier==1)&&profile.Data.Powers.Any(p=>p.Id=="future-power"&&p.Tier==17),legacy+": mapped upgrade tiers plus original and unknown ownership records retained");
            Check(profile.EquippedA!=profile.EquippedB&&profile.EquippedA.Selectable&&profile.EquippedB.Selectable&&profile.EquippedSignature!=null&&registry.Choices(profile.EquippedA,profile.EquippedB).Contains(profile.EquippedSignature),legacy+": valid distinct deterministic replacement build and equipped signature");
            if(legacy=="strength"||legacy=="laser-eyes")Check(profile.EquippedA.Id==(legacy=="strength"?"combat-morphing":"plasma-blades")&&profile.EquippedB.Id=="ice"&&profile.EquippedSignature.Id==(legacy=="strength"?"apex-mutation":"blade-cyclone"),legacy+": preferred primary/signature alias and unaffected primary preserved");
            string normalized=JsonUtility.ToJson(profile.Data);profile.Load();Check(JsonUtility.ToJson(profile.Data)==normalized,legacy+": durable migration is idempotent");Destroy(go);
        }
        Check(Profile.SetLoadout(F.Hero("nova"),Power("combat-morphing"),Power("plasma-blades"),Power("grapple-line"),CityColor.Red,CityColor.Cyan)&&Profile.SetSignature("blade-cyclone"),"Save final complete new build for a separate native process");
        Profile.SetFirstPerson(true);Profile.Save();File.WriteAllText(SaveDir+"reload-path.txt",Profile.SavePath);File.WriteAllText(SaveDir+"reload-expected.json",JsonUtility.ToJson(Profile.Data));
        Log("READY separate-process reload pointer and exact expected save written; no personal profile used.");
    }
    IEnumerator ReloadChecks()
    {
        Check(JsonUtility.ToJson(Profile.Data)==File.ReadAllText(SaveDir+"reload-expected.json"),"SECOND PROCESS exact complete Version 1 save survived including all progression and appearance fields");
        Check(Profile.SelectedHero.Id=="nova"&&Profile.EquippedA.Id=="combat-morphing"&&Profile.EquippedB.Id=="plasma-blades"&&Profile.EquippedSecondary.Id=="grapple-line"&&Profile.EquippedSignature.Id=="blade-cyclone","SECOND PROCESS all four equipped stable IDs reconstruct correctly");
        foreach(string kind in new[]{"strength","laser-eyes","duplicate","unknown"})
        {
            string path=Path.GetFullPath(SaveDir+kind+".json");var written=JsonUtility.FromJson<ProgressSave>(File.ReadAllText(path));
            var go=new GameObject("SECOND PROCESS migrated save");var restored=go.AddComponent<PlayerProgression>();restored.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),path);
            Check(JsonUtility.ToJson(restored.Data)==JsonUtility.ToJson(written)&&restored.Data.Level==7&&restored.Data.Xp==31&&restored.SelectedHero.Id=="titan"&&restored.EquippedA.Selectable&&restored.EquippedB.Selectable&&restored.EquippedA!=restored.EquippedB&&restored.EquippedSignature!=null,"SECOND PROCESS migrated "+kind+": exact nonzero progression/cosmetics/unknown records and valid equipment survive without another repair");Destroy(go);
        }
        Check(GameFlow.Instance.OpenCombatLab(),"SECOND PROCESS enter gameplay with reconstructed appearance and loadout");yield return Scene(CombatLab.SceneName);
        Check(W.Powers.EquippedA.Id=="combat-morphing"&&W.Powers.EquippedB.Id=="plasma-blades"&&W.Powers.Synergy.Id=="blade-cyclone"&&W.Hero.GetComponentsInChildren<Animator>().Length==1,"SECOND PROCESS live session caches new primary pair/signature and existing Animator");
        Check(Camera.main.GetComponent<ThirdPersonCamera>().FirstPerson,"SECOND PROCESS preserved first-person preference");
    }
}
#endif
