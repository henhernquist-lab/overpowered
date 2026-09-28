#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEditor;

/// Real Play Mode proof that a synergy is usable as soon as its pair is equipped (no purchase), that its long cooldown is
/// the only gate, and that ownership/equip gates and tier purchases are intact. Evidence: Verification/Synergy/.
public sealed class SynergyAvailabilityVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;public bool Reload;
    const string Folder="Verification/Synergy/";const string Saves=Folder+"saves/";
    readonly List<string> output=new List<string>();string runtimeFailure;
    PowerSynergyDefinition[] shippingSynergies;
    WorldSession W=>WorldSession.Instance;
    ForgeCatalog F=>Resources.Load<ForgeCatalog>("ForgeCatalog");
    PowerDefinition Power(string id)=>Resources.Load<PowerDefinition>("Powers/"+id);
    void Awake(){Application.logMessageReceived+=ObserveLog;}
    void OnDestroy(){Application.logMessageReceived-=ObserveLog;RestoreCatalog();}
    void ObserveLog(string message,string trace,LogType type)
    {if((type==LogType.Exception||type==LogType.Error||type==LogType.Assert)&&(trace.Contains("Assets/Scripts/")||trace.Contains("Synergy")))runtimeFailure=message;}
    IEnumerator Start()
    {
        Directory.CreateDirectory(Saves);var stack=new Stack<IEnumerator>();stack.Push(Reload?ReloadChecks():Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{if(runtimeFailure!=null)throw new Exception("Gameplay Console error: "+runtimeFailure);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);RestoreCatalog();Write();Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        RestoreCatalog();Write();Finished(0);
    }
    void Log(string line){output.Add(line);Debug.Log(line);}
    void Check(bool ok,string line){if(!ok)throw new Exception(line);Log("PASS "+line);}
    void Write(){File.WriteAllLines(Folder+(Reload?"reload.txt":"results.txt"),output);}
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+45;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout");yield return null;}
        yield return new WaitForSecondsRealtime(.5f);
    }
    void Submit(Button button){using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    static readonly string[] Shipping={"sonic-slam","thermal-shock","solar-flare","void-grasp","eclipse-beam"};
    void RestoreCatalog(){if(shippingSynergies!=null){F.Synergies=shippingSynergies;shippingSynergies=null;}}

    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);var menu=FindAnyObjectByType<ModeScreens>();var profile=menu.Profile;
        var thermal=F.Synergies.Single(s=>s.Id=="thermal-shock");
        // ---- Data: every synergy has its own long cooldown; no cost field exists at all.
        foreach(var s in F.Synergies.OrderBy(s=>s.Cooldown).ThenBy(s=>s.Id))
            Log($"DATA synergy={s.Id} pair={s.PowerA.Id}+{s.PowerB.Id} cooldown={s.Cooldown:0.#}s damage={s.Damage:0.#} radius={s.Radius:0.#}m force={s.Force:0} duration={s.Duration:0.#}s freeze={s.FreezeSeconds:0.#}s burn={s.BurnSeconds:0.#}s");
        var ids=F.Synergies.Select(s=>s.Id).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        Check(ids.SequenceEqual(Shipping.OrderBy(s=>s,StringComparer.Ordinal)),"Shipping catalog is EXACTLY the five capped synergies: "+string.Join(", ",ids));
        Check(F.Synergies.All(s=>s.Cooldown>=25&&s.Cooldown<=45),"All five synergy cooldowns are long (25-45 s): "+string.Join(", ",F.Synergies.Select(s=>s.Id+"="+s.Cooldown)));
        Check(F.Synergies.Select(s=>s.Cooldown).Distinct().Count()>1,"Cooldowns are tuned per synergy, not one flat value.");
        // The 40x rule compares synergies with ORDINARY INSTANT OFFENSIVE powers. Excluded, because their cooldown is not their
        // gate: Flight (traversal, gated by fuel), Force Field (defensive; gated by its lifetime and 12 s charge recharge) and
        // any Channeled power, i.e. Laser Eyes (gated by continuous energy drain; its cooldown only starts on release).
        var all=Resources.LoadAll<PowerDefinition>("Powers").Where(p=>!p.Id.StartsWith("verification-")).ToArray();
        var ordinary=all.Where(p=>p.Activation==PowerActivation.Instant&&!(p.Effect is ForceFieldEffect)&&!(p.Effect!=null&&p.Effect.IsFlight)).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        var excluded=all.Except(ordinary).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        Log("RULE 40x compares: "+string.Join(", ",ordinary.Select(p=>p.Id+"="+p.Cooldown+"s"))+" | excluded: "+string.Join(", ",excluded.Select(p=>p.Id+" ("+(p.Activation==PowerActivation.Channeled?"channeled: drain-gated":p.Effect is ForceFieldEffect?"defensive: lifetime + charge recharge":"traversal: fuel")+")")));
        Check(excluded.Select(p=>p.Id).OrderBy(s=>s,StringComparer.Ordinal).SequenceEqual(new[]{"flight","force-field","laser-eyes"}),"Excluded from the ratio rule: exactly flight, force-field, laser-eyes.");
        float normal=ordinary.Max(p=>p.Cooldown);
        Check(F.Synergies.Min(s=>s.Cooldown)>=normal*40,$"Shortest synergy cooldown {F.Synergies.Min(s=>s.Cooldown)} s is >= 40x the longest instant offensive power cooldown {normal} s ({ordinary.First(p=>p.Cooldown==normal).Id}).");
        // ---- Fresh profile: nothing to buy.
        Check(profile.Data.Level==1&&profile.Data.Points==0,"Fresh profile: level 1, 0 points.");
        Check(Resources.LoadAll<PowerDefinition>("Powers").All(p=>p.InitiallyUnlocked&&profile.Owns(p)&&profile.Tier(p)==0),"Every power asset is InitiallyUnlocked and owned at tier 0 with 0 points.");
        Submit(menu.ForgeButton);yield return null;var screen=menu.ForgeScreen;
        Check(screen!=null&&screen.Root.resolvedStyle.display!=DisplayStyle.None,"Forge opens from Home.");
        Check(!screen.Root.Query<Button>().ToList().Any(b=>b.text.ToUpperInvariant().Contains("UNLOCK")),"Forge shows no UNLOCK / point-cost buttons.");
        Check(screen.SelectHero(F.Hero("vector"))&&screen.SelectPower(0,Power("fire"))&&screen.SelectPower(1,Power("ice")),"VECTOR equips Fire + Ice (previously point-gated) straight from the Forge dropdowns.");
        Check(profile.Data.Points==0&&profile.EquippedA==Power("fire")&&profile.EquippedB==Power("ice"),"Fire + Ice saved with 0 points spent.");
        string status=screen.SynergyStatus.text;
        Check(screen.SynergyLabel.text=="SYNERGY / Thermal Shock"&&status.Contains("READY WHEN EQUIPPED")&&status.Contains(thermal.Cooldown.ToString("0")+" S COOLDOWN"),$"Forge shows the synergy as available with its cooldown: '{screen.SynergyLabel.text}' / '{status}'.");
        yield return CaptureMenu(menu,"forge-fire-ice-available.png");
        Check(screen.SelectPower(1,Power("laser-eyes"))&&screen.SynergyLabel.text=="SYNERGY / Solar Flare"&&screen.SynergyStatus.text.Contains(F.Resolve(Power("fire"),Power("laser-eyes")).Cooldown.ToString("0")+" S"),"Changing the pair updates the synergy line and its own cooldown: "+screen.SynergyStatus.text);
        Check(screen.SelectPower(1,Power("strength"))&&screen.SynergyLabel.text=="SYNERGY / No synergy"&&screen.SynergyStatus.text=="","CONTROL: Fire + Strength (a removed legacy pair) shows no synergy and no status line.");
        Check(screen.SelectPower(1,Power("ice")),"Back to Fire + Ice.");
        screen.Close();

        // ---- Play: fires immediately, no purchase.
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")),"Enter Hero mode.");yield return Scene(GameFlow.CityScene);
        var r=W.Powers.SynergyRunner;
        Check(W.Powers.Synergy==thermal&&W.Progression.Data.Points==0,"Session resolves Thermal Shock from the equipped pair; still 0 points.");
        var victim=Arena(out var control);
        var hud=FindAnyObjectByType<GameHud>();yield return null;yield return null;
        var slot=hud.Slots.FirstOrDefault(s=>s.Synergy!=null);
        Check(slot!=null&&slot.Synergy==thermal&&slot.CooldownFraction==0f,"HUD synergy slot shows Thermal Shock ready (radial 0).");
        float hp=victim.Health,controlHp=control.Health;
        Check(r.TryActivate(),"Thermal Shock fires immediately on a 0-point profile (no unlock step).");
        float activated=Time.time;
        Check(Mathf.Abs(r.Cooldown-thermal.Cooldown)<.001f,$"Activation starts the configured cooldown: {r.Cooldown:0.00}s of {thermal.Cooldown}s.");
        yield return null;yield return null;
        Check(slot.CooldownFraction>.95f,$"HUD radial reads the new cooldown: {slot.CooldownFraction:0.000} (runner {r.Cooldown:0.00}/{thermal.Cooldown}s).");
        float until=Time.time+8;while(r.Busy&&Time.time<until)yield return null;
        float dealt=hp-victim.Health;
        Check(!r.Busy&&Mathf.Abs(dealt-thermal.Damage)<.01f&&control.Health==controlHp,$"Real effect: aimed target took {dealt:0.##} damage (= Damage {thermal.Damage}); out-of-radius CONTROL untouched.");
        // ---- Mid-cooldown use is REJECTED: no effect, no damage, cooldown not reset.
        float cd=r.Cooldown,hp2=victim.Health;int impacts=r.Impacts,emissions=r.Vfx.Emissions;
        Check(!r.TryActivate()&&r.Feedback=="Synergy cooling down",$"Mid-cooldown activation refused ({cd:0.00}s left): \"{r.Feedback}\".");
        yield return new WaitForSeconds(.5f);
        Check(!r.Busy&&victim.Health==hp2&&r.Impacts==impacts&&r.Vfx.Emissions==emissions,"Refused use caused no effect: health, impacts and VFX emissions unchanged.");
        Check(r.Cooldown<cd&&r.Cooldown>cd-1f,$"Refused use did not reset or extend the cooldown: {cd:0.00}s -> {r.Cooldown:0.00}s after 0.5s.");
        until=Time.time+thermal.Cooldown;while(r.Cooldown>thermal.Cooldown*.5f&&Time.time<until)yield return null;yield return null;
        Check(Mathf.Abs(slot.CooldownFraction-r.Cooldown/thermal.Cooldown)<.02f&&slot.CooldownFraction>.4f&&slot.CooldownFraction<.55f,$"HUD radial halfway: {slot.CooldownFraction:0.000} for {r.Cooldown:0.00}/{thermal.Cooldown}s.");
        Check(!r.TryActivate(),"Still refused at half cooldown.");
        until=Time.time+thermal.Cooldown+2;while(r.Cooldown>0&&Time.time<until)yield return null;
        float waited=Time.time-activated;
        Log($"MEASURED Thermal Shock became ready again {waited:0.00}s (game time) after activation; configured {thermal.Cooldown}s.");
        Check(r.Cooldown==0&&Mathf.Abs(waited-thermal.Cooldown)<.25f,"Cooldown elapses on schedule.");
        hp=victim.Health;
        Check(r.TryActivate(),"CONTROL: after the cooldown the synergy fires again.");
        until=Time.time+8;while(r.Busy&&Time.time<until)yield return null;
        Check(Mathf.Abs(hp-victim.Health-thermal.Damage)<.01f,$"Second activation dealt {hp-victim.Health:0.##} damage.");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);

        // ---- Refusal CONTROLs.
        // (a) Thermal Shock removed from the loaded catalog IN MEMORY ONLY: the same Fire + Ice session then has no synergy.
        shippingSynergies=F.Synergies;F.Synergies=shippingSynergies.Where(s=>s!=thermal).ToArray();
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        r=W.Powers.SynergyRunner;victim=Arena(out control);hp=victim.Health;yield return null;yield return null;
        Check(W.Powers.Synergy==null&&!r.TryActivate()&&r.Feedback=="No synergy for this pair"&&r.Cooldown==0&&r.Impacts==0,$"CONTROL: Fire + Ice with no synergy defined -> refused (\"{r.Feedback}\"), no cooldown, no impact.");
        yield return new WaitForSeconds(.4f);
        Check(victim.Health==hp&&FindAnyObjectByType<GameHud>().Slots.All(s=>s.Synergy==null),"No-synergy CONTROL: target untouched and the HUD has no synergy slot.");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
        RestoreCatalog();
        Check(F.Synergies.Contains(thermal)&&F.Synergies.Select(s=>s.Id).OrderBy(s=>s,StringComparer.Ordinal).SequenceEqual(Shipping.OrderBy(s=>s,StringComparer.Ordinal))&&!EditorUtility.IsDirty(F),"Catalog restored to its exact five shipping synergies; never dirtied or saved.");
        // (b) Unequipped power: Ice is owned but not in the Fire + Strength loadout -> Thermal Shock is unreachable.
        menu=FindAnyObjectByType<ModeScreens>();profile=menu.Profile;
        Check(profile.SetLoadout(F.Hero("vector"),Power("fire"),Power("strength"),CityColor.Blue,CityColor.Cyan),"Equip Fire + Strength.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        var ice=W.Powers.Powers.Find(p=>p.Definition.Id=="ice");
        Check(W.Powers.Synergy==null&&!W.Powers.IsEquipped(ice.Definition)&&!W.Powers.Use(ice)&&W.Powers.Message=="Power not equipped"&&!W.Powers.SynergyRunner.TryActivate(),
            "CONTROL: with Ice owned but unequipped, Fire + Strength has no synergy (not Thermal Shock; Meteor Punch was removed), C is refused and Ice itself is refused by the equip gate.");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);

        // ---- Points still buy upgrade TIERS.
        menu=FindAnyObjectByType<ModeScreens>();profile=menu.Profile;
        Check(profile.Data.Points==0&&!profile.Buy(Power("fire"))&&profile.Tier(Power("fire"))==0,"Zero-point CONTROL: tier purchase refused, Fire stays tier 0.");
        profile.AddXp(profile.RequiredXp);
        Check(profile.Data.Points==1&&profile.Buy(Power("fire"))&&profile.Tier(Power("fire"))==1&&profile.Data.Points==0,"Earned point buys Fire tier 0 -> 1.");
        Check(!profile.Buy(Power("ice"))&&profile.Tier(Power("ice"))==0,"Zero-point CONTROL after spending: Ice tier purchase refused.");

        // ---- Old save for the separate-process migration test: Fire/Ice/Telekinesis NOT owned, no Loadout (pre-Forge).
        string old=Path.GetFullPath(Saves+"old-save-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(old,"{\"Version\":1,\"Level\":3,\"Xp\":17,\"Points\":2,\"Side\":0,\"Powers\":[{\"Id\":\"flight\",\"Tier\":1},{\"Id\":\"strength\",\"Tier\":0}],\"Rooftops\":[\"roof-a\"],\"SessionsPlayed\":4,\"SessionsWon\":1}");
        File.WriteAllText(Saves+"old-save-path.txt",old);File.Copy(old,Folder+"old-save-as-written.json",true);
        Check(!File.ReadAllText(old).Contains("fire")&&!File.ReadAllText(old).Contains("ice"),"Old save written with Fire/Ice not owned (no entry in Powers): "+Path.GetFileName(old));
        Log("LIMIT: activation is driven through SynergyRunner.TryActivate (the method the C key calls); no hardware key injection. Cooldown lengths are a design judgment from the effect numbers, not human-playtested.");
    }
    IEnumerator ReloadChecks()
    {
        yield return Scene(GameFlow.HomeScene);var menu=FindAnyObjectByType<ModeScreens>();var profile=menu.Profile;
        Log("SECOND PROCESS loaded "+profile.SavePath);
        Check(profile.LastError==null,"Old save parsed without error.");
        Check(profile.Owns(Power("fire"))&&profile.Owns(Power("ice"))&&profile.Owns(Power("telekinesis"))&&profile.Tier(Power("fire"))==0,"SECOND PROCESS: old save without Fire/Ice/Telekinesis now owns them at tier 0 (loader grants InitiallyUnlocked).");
        Check(profile.Data.Level==3&&profile.Data.Xp==17&&profile.Data.Points==2&&profile.Tier(Power("flight"))==1&&profile.Data.Rooftops.Contains("roof-a")&&profile.Data.SessionsPlayed==4,"Old progression preserved exactly: level 3, 17 XP, 2 unspent points, Flight tier 1, rooftop, sessions.");
        Check(profile.SetLoadout(F.Hero("nova"),Power("fire"),Power("ice"),CityColor.Red,CityColor.Cyan)&&profile.Data.Points==2,"Migrated Fire + Ice equippable with no points spent.");
        Check(File.ReadAllText(profile.SavePath).Contains("\"fire\""),"Migrated ownership written back to the same save file.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        var r=W.Powers.SynergyRunner;var victim=Arena(out _);float hp=victim.Health;
        Check(W.Powers.EquippedA==Power("fire")&&W.Powers.EquippedB==Power("ice")&&W.Powers.Synergy!=null&&W.Powers.Synergy.Id=="thermal-shock","Migrated session equips Fire + Ice and resolves Thermal Shock.");
        Check(r.TryActivate(),"Migrated profile fires Thermal Shock immediately.");
        float until=Time.time+8;while(r.Busy&&Time.time<until)yield return null;
        Check(hp-victim.Health>0,$"Migrated Thermal Shock dealt {hp-victim.Health:0.##} damage.");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
        var fresh=new GameObject("Fresh control").AddComponent<PlayerProgression>();
        fresh.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),Path.GetFullPath(Saves+"fresh-"+Guid.NewGuid().ToString("N")+".json"));
        Check(fresh.Data.Points==0&&Resources.LoadAll<PowerDefinition>("Powers").All(fresh.Owns),"Fresh-save CONTROL: 0 points and every power owned.");
    }
    /// Isolated platform in the sky, camera aimed straight down +Z at a heavily-armoured target actor; CONTROL actor 12 m aside.
    CityNpc Arena(out CityNpc control)
    {
        W.Hero.enabled=false;var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=new Vector3(0,151,0);cc.enabled=true;W.Hero.ResetMotion();W.Hero.transform.forward=Vector3.forward;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Synergy test platform";floor.transform.position=new Vector3(0,149.5f,0);floor.transform.localScale=new Vector3(60,1,60);
        floor.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Road);
        var camera=Camera.main;camera.GetComponent<ThirdPersonCamera>().enabled=false;camera.transform.position=new Vector3(0,152,-8);camera.transform.forward=Vector3.forward;
        var victim=Actor(new Vector3(0,151,6));control=Actor(new Vector3(12,151,6));
        victim.SetCombatStats(1000,0);control.SetCombatStats(1000,0);Physics.SyncTransforms();return victim;
    }
    CityNpc Actor(Vector3 point){var npc=CityNpc.Spawn(W,W.City.Sidewalks[0],NpcRole.Criminal);npc.enabled=false;npc.Agent.enabled=false;npc.transform.position=point;return npc;}
    IEnumerator CaptureMenu(ModeScreens menu,string file)
    {
        var target=new RenderTexture(1440,900,24);target.Create();menu.Panel.targetTexture=target;
        yield return null;yield return null;
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1440,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();
        File.WriteAllBytes(Folder+file,image.EncodeToPNG());Destroy(image);RenderTexture.active=previous;menu.Panel.targetTexture=null;target.Release();Destroy(target);
    }
}
#endif
