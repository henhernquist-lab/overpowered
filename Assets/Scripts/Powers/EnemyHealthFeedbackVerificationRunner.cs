#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class EnemyHealthFeedbackVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/EnemyHealthFeedback/";
    protected override string ResultFile => "results.txt";
    GameHud Hud => FindAnyObjectByType<GameHud>();
    EnemyArchetype Rusher => Resources.Load<EnemyArchetype>("Enemies/Rusher");
    EnemyArchetype Gunner => Resources.Load<EnemyArchetype>("Enemies/Gunner");
    EnemyArchetype Brute => Resources.Load<EnemyArchetype>("Enemies/Brute");
    GameHud.EnemyBarDisplay Bar(CityNpc npc) => Hud.EnemyBars.FirstOrDefault(b=>b.Target==npc);
    NpcDamageResult[] Hits(CityNpc npc,string source=null) => Hud.EnemyDamageHistory.Where(h=>h.Target==npc && (source==null || h.SourceId==source)).ToArray();
    CityNpc Actor(EnemyArchetype archetype,Vector3 feet,NpcRole role=NpcRole.Criminal,float health=0f)
    {
        var npc=CityNpc.Spawn(W,W.City.Sidewalks[0],role,archetype);
        if(npc==null)throw new Exception("Spawn failed: "+archetype.name);
        npc.enabled=false;npc.Agent.enabled=false;npc.transform.position=feet;
        if(health>0f)npc.SetCombatStats(health,0f);
        Physics.SyncTransforms();return npc;
    }
    IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
    IEnumerator Until(Func<bool> ready,float seconds,string label)
    {
        float stop=Time.realtimeSinceStartup+seconds;
        while(!ready()){if(Time.realtimeSinceStartup>stop)throw new Exception("Timed out: "+label);yield return null;}
    }
    IEnumerator Screenshot(string name)
    {
        Hud.DismissBriefing("verification capture");
        var hud=Hud;var composite=new RenderTexture(1280,720,24){name="Enemy feedback "+name};composite.Create();
        hud.Panel.targetTexture=composite;hud.OverlayPanel.targetTexture=composite;
        yield return Frames(4);
        var camera=Cam;bool enabled=camera.enabled;camera.enabled=false;camera.targetTexture=composite;camera.Render();camera.targetTexture=null;
        hud.Root.MarkDirtyRepaint();hud.OverlayRoot.MarkDirtyRepaint();yield return null;
        var prior=RenderTexture.active;RenderTexture.active=composite;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        File.WriteAllBytes(Folder+name+".png",image.EncodeToPNG());RenderTexture.active=prior;
        Destroy(image);camera.enabled=enabled;hud.Panel.targetTexture=null;hud.OverlayPanel.targetTexture=null;
        composite.Release();Destroy(composite);Log("IMAGE "+Folder+name+".png");
    }
    IEnumerator ClearActors()
    {
        foreach(var npc in W.Npcs.ToArray())if(npc!=null)Destroy(npc.gameObject);
        yield return new WaitForSecondsRealtime(GameHud.DamageNumberSeconds+.05f);
    }
    IEnumerator Sample(string label,int frames)
    {
        var target=new RenderTexture(1280,720,24){name="Enemy feedback profile"};target.Create();
        var prior=Cam.targetTexture;Cam.targetTexture=target;
        var clock=System.Diagnostics.Stopwatch.StartNew();long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<frames;i++){Cam.Render();yield return null;}
        clock.Stop();Cam.targetTexture=prior;target.Release();Destroy(target);
        Log($"MEASURED {label}: {frames} frames, {frames/clock.Elapsed.TotalSeconds:0.00} FPS, " +
            $"{clock.Elapsed.TotalMilliseconds/frames:0.000} ms/frame, " +
            $"{(GC.GetAllocatedBytesForCurrentThread()-bytes)/(double)frames:0} managed B/frame, " +
            $"GameObjects {FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length}, " +
            $"UI bars {Hud.EnemyBars.Count}, number labels {Hud.DamageNumbers.Count}, " +
            $"materials {Resources.FindObjectsOfTypeAll<Material>().Length}, active bars {Hud.EnemyBars.Count(x=>x.Visible)}.");
    }
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Enter("fire","strength");Isolate();Hud.DismissBriefing("verification setup");yield return new WaitForSecondsRealtime(.4f);
        Check(Hud.EnemyBars.Count==GameHud.EnemyBarPoolSize && Hud.DamageNumbers.Count==GameHud.DamageNumberPoolSize,
            "HUD prebuilds exactly 20 reusable bars and 96 reusable number labels before any hit.");
        var r=Actor(Rusher,new Vector3(0,150,9));var g=Actor(Gunner,new Vector3(4,150,9));
        var b=Actor(Brute,new Vector3(-4,150,9));var pursuer=Actor(Brute,new Vector3(7,150,9),NpcRole.PursuingHero);
        Check(W.Stars==0 && Mathf.Abs(r.MaxHealth-39f)<.001f && Mathf.Abs(g.MaxHealth-65f)<.001f &&
            Mathf.Abs(b.MaxHealth-195f)<.001f && Mathf.Abs(pursuer.MaxHealth-195f)<.001f,
            $"Actual spawned 0-star HP: Rusher {r.MaxHealth}, Gunner {g.MaxHealth}, Brute {b.MaxHealth}, pursuing-hero Brute {pursuer.MaxHealth}.");
        var waves=Resources.Load<EndlessWaveDirector>("ModeDirectors/EndlessWaves");
        var wave1=waves.Compose(1,0,PlayerSide.Hero);
        Check(wave1.Archetype==Rusher && Mathf.Abs(wave1.Health-39f)<.001f,
            $"Endless wave-1 composed Rusher HP {wave1.Health}; explicit spawn stats use this value (Heat excluded).");
        Check(Hud.EnemyBars.All(x=>x.Target==null) && Hud.EnemyDamageEvents==0,"Undamaged unrelated enemies have no combat bars or damage records.");
        Aim(Chest(r));yield return new WaitForSecondsRealtime(.2f);
        Check(Bar(r)!=null && Bar(r).Visible && r.CurrentHealth==r.MaxHealth,"Clear aim reveals undamaged hostile with full authoritative HP.");
        Aim(new Vector3(0,155,-10));yield return new WaitForSecondsRealtime(.4f);
        Check(Bar(r)==null,"Aim-only bar expires after the player looks away.");
        foreach(var pair in new[]{(r,"rusher"),(g,"gunner"),(b,"brute")})
        {
            Aim(Chest(pair.Item1));using(W.Powers.Credit("verification"))pair.Item1.Damage(5,W.Powers);
            yield return Frames(2);
            var bar=Bar(pair.Item1);
            Check(bar!=null && bar.Visible && bar.Value.text==pair.Item1.Health.ToString("0")+" / "+pair.Item1.MaxHealth.ToString("0") &&
                Mathf.Abs(bar.Fill.resolvedStyle.width/124f-pair.Item1.HealthFraction)<.03f,
                $"{pair.Item2} bar reads actual CityNpc current/max and fill ({pair.Item1.Health}/{pair.Item1.MaxHealth}).");
            yield return Screenshot(pair.Item2+"-health");
        }
        Check(pursuer.Health==pursuer.MaxHealth && Bar(pursuer)==null,"Neutral pursuing-hero actor gets no player-hostile bar in Hero mode.");
        yield return ClearActors();

        // Physical Fire projectile and real CombatImpact.Blast: two victims, one outside the radius.
        var fire=Runtime("fire");var near=Actor(Rusher,new Vector3(0,150,10),health:1000);
        var splash=Actor(Gunner,new Vector3(1.8f,150,10),health:1000);
        var far=Actor(Brute,new Vector3(6f,150,10),health:1000);
        for(int i=0;i<9;i++)Actor(i%2==0?Gunner:Brute,new Vector3(-20+i*4,150,25),health:1000);
        Check(W.Npcs.Count(n=>n!=null && !n.Dead)>=12,"Real Fire AoE fixture retains twelve live enemies under the legal Fire/Strength loadout.");
        Aim(Chest(near));Check(W.Powers.Use(fire),"Fire Blast activates through the equipped PowerUser gate.");
        yield return Until(()=>Hits(near,"fire").Length>0,3,"real Fire impact");yield return Frames(2);
        Check(Hits(near,"fire").Length==1 && Hits(splash,"fire").Length==1 && Hits(far).Length==0 && far.Health==1000,
            "Fire AoE emits one real resolved result per damaged target; outside-radius control gets none.");
        Check(Mathf.Abs(Hits(near,"fire")[0].ResolvedDamage-(1000-near.Health))<.01f &&
            Hud.DamageNumbers.Any(n=>n.Active&&n.Target==near&&Mathf.Abs(n.Amount-Hits(near,"fire")[0].ResolvedDamage)<.01f),
            "Fire number belongs to the impact victim and equals its authoritative resolved damage.");
        yield return Screenshot("fire-damage");
        yield return ClearActors();

        var melee=Actor(Rusher,new Vector3(0,150,2.2f),health:1000);W.Hero.DebugSetResources(6,3,0);
        Check(W.Hero.TryComboAttack(),"Equipped Strength starts the real first E punch.");
        yield return Until(()=>Hits(melee).Length>0,2,"punch impact");
        var punch=Hits(melee)[0];
        Check(punch.HealthLost==punch.ResolvedDamage && Hud.DamageNumbers.Any(n=>n.Active&&n.Target==melee&&Mathf.Abs(n.Amount-punch.ResolvedDamage)<.01f),
            $"Punch number equals authoritative {punch.ResolvedDamage:0.##} damage after configured windup.");
        W.Hero.DebugSetResources(6,3,0);
        Check(W.Hero.TryComboAttack()&&W.Hero.LastComboStage==2,"Second E in the combo window starts Martelo.");
        yield return Until(()=>Hits(melee).Length>1,2,"Martelo impact");
        Check(Hits(melee)[1].ResolvedDamage>punch.ResolvedDamage && Hud.DamageNumbers.Any(n=>n.Active&&n.Target==melee&&Mathf.Abs(n.Amount-Hits(melee)[1].ResolvedDamage)<.01f),
            "Martelo number uses its separate real impact damage, with no rapid-source merge.");
        yield return ClearActors();

        yield return Enter("laser-eyes","poison");Isolate();Hud.DismissBriefing("verification setup");yield return new WaitForSecondsRealtime(.4f);
        var laser=Actor(Rusher,new Vector3(0,150,10),health:1000);var laserControl=Actor(Gunner,new Vector3(4,150,10),health:1000);
        var beam=Runtime("laser-eyes");Aim(Chest(laser));Check(W.Powers.Use(beam),"Laser channel starts through the equipped gate.");
        float stop=Time.time+.55f;
        while(Time.time<stop){yield return null;W.Powers.Channel(true,Time.deltaTime);}
        W.Powers.Channel(false,Time.deltaTime);
        var ticks=Hits(laser,"laser-eyes");
        float resolved=ticks.Sum(x=>x.ResolvedDamage),shown=Hud.DamageNumbers.Where(n=>n.Active&&n.Target==laser&&n.SourceId=="laser-eyes").Sum(n=>n.Amount);
        Check(ticks.Length>=3 && Hud.DamageNumbersMerged>0 && Mathf.Abs(resolved-shown)<.02f &&
            Mathf.Abs(1000-laser.Health-ticks.Sum(x=>x.HealthLost))<.02f && Hits(laserControl).Length==0,
            $"Laser {ticks.Length} real ticks aggregate into truthful displayed total {shown:0.##} (resolved {resolved:0.##}); adjacent control untouched.");
        yield return Screenshot("laser-aggregated");

        var poison=Actor(Gunner,new Vector3(-4,150,10),health:1000);var poisonPower=Runtime("poison");
        var poisonSettings=poisonPower.Definition.Effect as PoisonEffect;float originalTick=poisonSettings.TickSeconds;
        try
        {
            poisonSettings.TickSeconds=.1f; // verifier-only rapid-tick stress; restored before leaving this method
            W.Powers.Select(poisonPower);Aim(Chest(poison));
            Check(W.Powers.Use(poisonPower),"Poison applies through its real equipped gate; damage begins on a later tick.");
            Check(Hits(poison,"poison").Length==0,"Poison application invents no immediate damage number.");
            yield return new WaitForSeconds(.48f);
            var poisonHits=Hits(poison,"poison");
            float actual=poisonHits.Sum(x=>x.ResolvedDamage);
            float visible=Hud.DamageNumbers.Where(n=>n.Active&&n.Target==poison&&n.SourceId=="poison").Sum(n=>n.Amount);
            Check(poisonHits.Length>=3 && Mathf.Abs(actual-visible)<.02f && Mathf.Abs(1000-poison.Health-poisonHits.Sum(x=>x.HealthLost))<.02f,
                $"Rapid Poison real ticks aggregate exactly: {poisonHits.Length} ticks, resolved {actual:0.##}, shown {visible:0.##}.");
        }
        finally { poisonSettings.TickSeconds=originalTick; }
        // Twelve real NPCs, legal Laser + Poison loadout and always-available melee. The two other ranged
        // powers are exercised in their own legal loadout fixtures rather than bypassing session equip gates.
        var crowd=new CityNpc[12];crowd[0]=laser;crowd[1]=laserControl;crowd[2]=poison;
        for(int i=3;i<crowd.Length;i++)crowd[i]=Actor(i%3==0?Brute:i%3==1?Gunner:Rusher,
            new Vector3(i==3?0f:(i-6)*1.6f,150,i==3?2.2f:13f),health:1000);
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        yield return new WaitForSecondsRealtime(GameHud.EnemyBarHoldSeconds+.2f);
        yield return Sample("12-enemy idle before feedback",120);
        for(int i=0;i<crowd.Length;i++)using(W.Powers.Credit("verification-stress"))crowd[i].Damage(1,W.Powers,false);
        yield return Frames(2);
        Check(Hud.EnemyBars.Count(x=>x.Target!=null)>=12 && Hud.EnemyBars.Count==GameHud.EnemyBarPoolSize &&
            Hud.DamageNumbers.Count==GameHud.DamageNumberPoolSize,"Twelve damaged enemies use prebuilt bounded bar/number pools.");
        yield return Until(()=>beam.Cooldown<=0f,3,"Laser cooldown for stress");
        W.Powers.Select(beam);Aim(Chest(laser));Check(W.Powers.Use(beam),"Stress Laser channel starts with legal equip.");
        stop=Time.time+.5f;while(Time.time<stop){yield return null;W.Powers.Channel(true,Time.deltaTime);}
        W.Powers.Channel(false,Time.deltaTime);
        W.Hero.DebugSetResources(6,3,0);Check(W.Hero.TryComboAttack(),"Stress melee starts with the real controller.");
        yield return Sample("12-enemy active feedback",120);
        int gameObjects=FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length;
        int materialWarm=Resources.FindObjectsOfTypeAll<Material>().Length;
        for(int i=0;i<6;i++)
        {
            using(W.Powers.Credit("verification-stress"))foreach(var npc in crowd)npc.Damage(1,W.Powers,false);
            yield return new WaitForSecondsRealtime(.15f);
        }
        yield return Frames(2);
        Log($"MEASURED pool after stress: objects {gameObjects}->{FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length}, materials {materialWarm}->{Resources.FindObjectsOfTypeAll<Material>().Length}, drops {Hud.DamageNumbersDropped}.");
        Check(Hud.EnemyBars.Count==GameHud.EnemyBarPoolSize && Hud.DamageNumbers.Count==GameHud.DamageNumberPoolSize &&
            FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length==gameObjects &&
            Resources.FindObjectsOfTypeAll<Material>().Length==materialWarm && Hud.DamageNumbersDropped==0,
            "Sustained 12-target hits reuse the same UI slots without GameObject or material growth after warmup.");
        yield return ClearActors();

        yield return Enter("lightning","poison");Isolate();Hud.DismissBriefing("verification setup");yield return new WaitForSecondsRealtime(.4f);
        var first=Actor(Rusher,new Vector3(0,150,10),health:1000);
        var second=Actor(Gunner,new Vector3(3,150,10),health:1000);
        var outside=Actor(Brute,new Vector3(22,150,10),health:1000);
        for(int i=0;i<9;i++)Actor(i%2==0?Rusher:Brute,new Vector3(-20+i*4,150,25),health:1000);
        Check(W.Npcs.Count(n=>n!=null && !n.Dead)>=12,"Real Lightning chain fixture retains twelve live enemies under the legal Lightning/Poison loadout.");
        var lightning=Runtime("lightning");Aim(Chest(first));
        Check(W.Powers.Use(lightning),"Lightning casts through its real equipped gate.");yield return Frames(2);
        Check(LightningEffect.LastChain.Contains(first)&&LightningEffect.LastChain.Contains(second)&&
            !LightningEffect.LastChain.Contains(outside)&&Hits(first,"lightning").Length==1&&Hits(second,"lightning").Length==1&&Hits(outside).Length==0,
            "Lightning shows one result per actual chained target and none for the unchained control.");
        yield return Screenshot("lightning-chain");
        var victim=Actor(Rusher,new Vector3(0,150,5),health:5);
        yield return Until(()=>lightning.Cooldown<=0f,10,"Lightning cooldown for real overkill");
        Aim(Chest(victim));Check(W.Powers.Use(lightning),"Real equipped Lightning supplies the overkill attack.");
        var lethal=Hits(victim,"lightning")[0];yield return Frames(2);
        Check(Mathf.Abs(lethal.ResolvedDamage-W.Powers.Stats(lightning).Damage)<.001f && lethal.ResolvedDamage>5 && lethal.HealthLost==5 && lethal.HealthBefore==5 && lethal.HealthAfter==0 &&
            Hud.DamageNumbers.Any(n=>n.Active&&n.Target==victim&&Mathf.Abs(n.Amount-lethal.ResolvedDamage)<.001f) && Bar(victim)!=null && Bar(victim).Value.text=="0 / 5",
            $"Overkill shows full resolved {lethal.ResolvedDamage}, records only 5 health lost, and empties the real bar before cleanup.");
        yield return new WaitForSecondsRealtime(GameHud.EnemyDeathHoldSeconds+.15f);
        Check(Bar(victim)==null,"Defeated NPC bar is released after the brief empty-bar presentation.");
        var friendly=Actor(Gunner,new Vector3(5,150,5),NpcRole.Cop,100);
        int prior=Hud.EnemyDamageEvents;friendly.Damage(5,W.Powers);yield return Frames(2);
        Check(!friendly.Hostile && Hud.EnemyDamageEvents==prior && Bar(friendly)==null,
            "Hero-side friendly cop receives no ordinary enemy feedback even when harmed.");
        yield return ClearActors();

        // Still use the real side relation; Villain patrol police become hostile at 1 Heat star.
        yield return Enter(F.Heroes[0],"lightning","poison","villain");Isolate();Hud.DismissBriefing("verification setup");yield return new WaitForSecondsRealtime(.4f);
        var hostileCop=Actor(Gunner,new Vector3(0,150,10),NpcRole.Cop,100);
        Check(!hostileCop.Hostile && W.Stars==0,"Villain patrol starts neutral before the first assault.");
        Aim(Chest(hostileCop));using(W.Powers.Credit("verification"))hostileCop.Damage(5,W.Powers);yield return Frames(2);
        Check(hostileCop.Hostile && Bar(hostileCop)!=null && Hits(hostileCop).Length==1 &&
            Hud.DamageNumbers.Any(n=>n.Active && n.Target==hostileCop && n.Amount==5f),"First Villain assault makes the patrol hostile and displays that same hit, without losing its damage number.");
        var wall=Wall(new Vector3(0,151,5),new Vector3(4,4,.5f));
        yield return new WaitForSecondsRealtime(GameHud.EnemySightInterval+.1f);
        Check(Bar(hostileCop)!=null && !Bar(hostileCop).Visible && Bar(hostileCop).Occluded,
            "Solid wall occludes the active hostile bar.");
        Destroy(wall);yield return new WaitForSecondsRealtime(GameHud.EnemySightInterval+.1f);
        Check(Bar(hostileCop)!=null && Bar(hostileCop).Visible,"Bar returns when the wall is removed.");
        var distant=Actor(Rusher,new Vector3(0,150,45),NpcRole.Cop,100);
        using(W.Powers.Credit("verification"))distant.Damage(5,W.Powers);yield return Frames(2);
        Check(Bar(distant)!=null && !Bar(distant).Visible && distant.Health==95,
            "Beyond 38 m, damage stays real but the hostile bar is hidden.");
        var civilian=Actor(Rusher,new Vector3(5,150,10),NpcRole.Civilian,100);
        int civilianBefore=Hud.EnemyDamageEvents;civilian.Damage(5,W.Powers);yield return Frames(2);
        Check(Bar(civilian)==null && Hud.EnemyDamageEvents==civilianBefore,"Civilian damage produces no ordinary enemy bar or number.");
        var rig=Cam.GetComponent<ThirdPersonCamera>();rig.enabled=true;
        Check(rig.FirstPerson || rig.ToggleView(),"Existing B-view camera enters first person.");rig.SetLook(0,0);
        yield return new WaitForSecondsRealtime(.3f);
        Check(rig.FirstPerson && Bar(hostileCop)!=null && Bar(hostileCop).Visible,"Hostile health feedback remains visible above the actor in first person.");
        Check(rig.ToggleView() && !rig.FirstPerson,"Existing camera returns to third person.");rig.enabled=false;
        W.AddHeat(4f-W.Heat);W.ReconcilePolice();
        var actualPursuer=W.Npcs.FirstOrDefault(n=>n!=null && n.Role==NpcRole.PursuingHero);
        Check(actualPursuer!=null && actualPursuer.MaxHealth==411f && actualPursuer.Hostile,
            $"Actual four-star pursuing hero spawned by ReconcilePolice: {actualPursuer?.MaxHealth} HP, hostile to Villain.");

        yield return Enter("poison","fire");Isolate();Hud.DismissBriefing("verification setup");yield return new WaitForSecondsRealtime(.4f);
        var dot=Actor(Rusher,new Vector3(0,150,10),health:1000);
        var dotControl=Actor(Gunner,new Vector3(12,150,10),health:1000);
        var defaultPoison=Runtime("poison");var stats=W.Powers.Stats(defaultPoison);
        Aim(Chest(dot));Check(W.Powers.Use(defaultPoison),"Default Poison applies through real equip with unmodified tick settings.");
        var status=dot.GetComponent<Poisoned>();
        yield return Until(()=>!status.Active,stats.Duration+2f,"default Poison duration");
        var dotHits=Hits(dot,"poison");
        Check(dotHits.Length==status.Ticks && Mathf.Abs(dotHits.Sum(h=>h.ResolvedDamage)-stats.Damage*stats.Duration)<.02f &&
            Mathf.Abs(1000-dot.Health-stats.Damage*stats.Duration)<.02f && Hits(dotControl).Length==0,
            $"Default Poison completes {status.Ticks} real ticks / {dotHits.Sum(h=>h.ResolvedDamage):0.##} damage; unrelated target unchanged.");
        yield return ClearActors();
        var seed=Actor(Rusher,new Vector3(0,150,10),health:5);
        var recipient=Actor(Gunner,new Vector3(3,150,10),health:1000);
        var noSpread=Actor(Brute,new Vector3(12,150,10),health:1000);
        Aim(Chest(seed));Check(W.Powers.Use(defaultPoison),"Default Poison primes a real lethal tick for spread.");
        yield return Until(()=>Hits(recipient,"poison").Length>0,3f,"real Poison death spread tick");
        Check(seed.Dead && recipient.GetComponent<Poisoned>().Generation==1 && Hits(noSpread).Length==0 &&
            Hud.DamageNumbers.Any(n=>n.Active && n.Target==recipient && n.SourceId=="poison" && Mathf.Abs(n.Amount-Hits(recipient,"poison").Sum(h=>h.ResolvedDamage))<.02f),
            "Poison death spread produces later truthful target-owned numbers; outside-spread control receives none.");

        yield return Enter(F.Heroes[0],"fire","strength","endless-fight");
        var endless=W.Mode.Director as EndlessWaveState;
        yield return Until(()=>endless!=null && endless.Alive.Count>=3,12f,"actual Endless wave-1 spawns");
        Check(endless.Wave==1 && endless.Alive.All(n=>n.Archetype==Rusher && n.MaxHealth==39f && n.Hostile),
            "Actual Endless wave-1 director spawns three hostile Rusher actors at 39 HP each.");
        Log("LIMIT: visual size/clutter and moment-to-moment aiming still need human Editor play. Fire/Lightning and Laser/Poison require separate legal two-attack loadouts.");
    }
}
#endif
