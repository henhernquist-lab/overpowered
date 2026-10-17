#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class CombatBalanceVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/CombatBalance/";
    protected override string ResultFile => "results.txt";
    GameHud Hud => FindAnyObjectByType<GameHud>();
    readonly List<float> samples = new List<float>();
    readonly List<NpcDamageResult> hits = new List<NpcDamageResult>();
    CombatRolls expected;
    void Observe(NpcDamageResult hit) { if(hit.Attacker==W.Powers) hits.Add(hit); }
    void Seed(int seed=101)
    { W.Rolls.FixedUnit=null; W.Rolls.Seed(seed); expected=new CombatRolls(seed); samples.Clear(); hits.Clear(); }
    float Expected(float mean) => expected.Damage(mean,W.Tuning.CombatRanges);
    void Range(float value,float mean,string label)
    {
        float v=W.Tuning.CombatRanges.PlayerDamageVariance;
        Check(value>=mean*(1-v)-.003f&&value<=mean*(1+v)+.003f,label+" in range: "+value.ToString("F5"));
        samples.Add(value);
    }
    void Distribution(float mean,string label)
    {
        float average=samples.Average();
        Check(samples.Distinct().Count()>samples.Count/2 && Mathf.Abs(average/mean-1)<.035f,
            $"{label}: n={samples.Count}, mean={average:F5}, intended={mean:F5}, min={samples.Min():F5}, max={samples.Max():F5}; diversity and midpoint within 3.5%.");
    }
    void Feedback(CityNpc target)
    {
        var hit=hits.Last(h=>h.Target==target);
        var numbers=Hud.DamageNumbers.Where(n=>n.Active&&n.Target==target).ToArray();
        Check(numbers.Length>0 && numbers.All(n=>n.Element.text==n.Amount.ToString("0.#")) &&
            numbers.Any(n=>Mathf.Abs(n.Amount-(hit.SourceId=="laser-eyes"?hits.Where(h=>h.Target==target).Sum(h=>h.ResolvedDamage):hit.ResolvedDamage))<.003f),"Floating label uses actual resolved/aggregated amount and one-decimal formatting.");
    }
    IEnumerator WaitHit(CityNpc target,int count=1,float seconds=2)
    {
        float end=Time.time+seconds;
        while(hits.Count(h=>h.Target==target)<count && Time.time<end)yield return null;
        Check(hits.Count(h=>h.Target==target)>=count,"Real gameplay damage arrived before timeout.");
        Feedback(target);
    }
    void Ready(string id)
    { W.Powers.Tick(100,true); var p=Runtime(id);if(p!=W.Powers.Strength)Check(W.Powers.Select(p),"Select equipped "+id); }
    IEnumerator Setup(string a,string b)
    {
        yield return Enter(F.Heroes[0],a,b,"free-play"); Isolate();
        W.enabled=false; // isolate seeded sample order from unrelated timed police reinforcements
        foreach(var n in W.Npcs.ToArray())if(n!=null)Destroy(n.gameObject);
        yield return null; Hud.DismissBriefing("combat range verification");
    }
    protected override IEnumerator Checks()
    {
        CityNpc.DamageApplied+=Observe;
        try
        {
            yield return Setup("fire","strength");
            yield return HealthSamples();
            Check(Mathf.Abs(Power("fire").Damage-45f*.6f)<.001f && Power("strength").Damage==35f &&
                Mathf.Abs(W.Tuning.Melee.FinisherDamageMultiplier*35f-83.3f*.5f)<.001f &&
                Mathf.Abs(W.Tuning.Melee.HurricaneDamageMultiplier*35f-59.5f*.2f)<.001f,"Audited Fire/punch/independent kick mean relationships.");
            yield return Instant("fire",27);
            foreach(string move in new[]{"punch","combo","hurricane"})yield return Melee(move);
            yield return Setup("ice","telekinesis");
            Check(Power("ice").Damage==10 && ((IceEffect)Power("ice").Effect).ShatterDamage==30 &&
                ((IceEffect)Power("ice").Effect).ShatterImpulse==1800 && ((IceEffect)Power("ice").Effect).ShatterParticles==28,"Ice initial doubled; shatter damage/force/particles unchanged.");
            yield return Instant("ice",10);
            foreach(float mass in new[]{45f,400f})yield return Throws(mass);
            yield return Setup("darkness","lightning");
            Check(Power("darkness").Damage==4*3 && Mathf.Abs(Power("lightning").Damage-24*1.7f)<.001f &&
                ((LightningEffect)Power("lightning").Effect).Falloff==.85f,"Darkness/Lightning mean multipliers and chain falloff unchanged.");
            yield return Instant("darkness",12);yield return Instant("lightning",40.8f);
            yield return Setup("laser-eyes","poison");
            Check(Power("laser-eyes").Damage==42 && Power("laser-eyes").DrainPerSecond==64 && Power("poison").Damage==12.5f,"Laser mean unchanged, drain doubled, Poison mean +25%.");
            yield return Channels(); yield return Poison();
            long start=GC.GetAllocatedBytesForCurrentThread();float sum=0;
            for(int i=0;i<10000;i++)sum+=W.Powers.RollDamage(35);
            long bytes=GC.GetAllocatedBytesForCurrentThread()-start;
            Check(sum>0&&bytes==0,$"10000 warmed damage resolutions allocate {bytes} managed bytes; no helper objects.");
        }
        finally { CityNpc.DamageApplied-=Observe; }
    }
    IEnumerator HealthSamples()
    {
        foreach(string id in new[]{"Rusher","Gunner","Brute"})
        {
            var archetype=Resources.Load<EnemyArchetype>("Enemies/"+id);float mean=65*archetype.HealthMultiplier;
            float[] first=new float[32];
            for(int pass=0;pass<3;pass++)
            {
                Seed(pass==2?102:101);var values=new List<float>();
                for(int i=0;i<32;i++)
                {
                    var npc=CityNpc.Spawn(W,W.City.Sidewalks[0],NpcRole.Criminal,archetype);
                    Check(npc!=null && npc.MaxHealth>=mean*.9f && npc.MaxHealth<=mean*1.1f && npc.Health==npc.MaxHealth,
                        $"{id} spawn {pass}/{i}: actual HP {npc?.Health:F5}, mean {mean}; current=max and bounds.");
                    values.Add(npc.MaxHealth);
                    if(pass==0)first[i]=npc.MaxHealth;
                    if(pass==1)Check(npc.MaxHealth==first[i],"Same seed reproduces actual spawn HP.");
                    npc.enabled=false;npc.Agent.enabled=false;npc.transform.position=new Vector3(0,150.05f,8);Physics.SyncTransforms();
                    if(i==0&&pass==0)
                    {
                        Aim(Chest(npc));npc.Damage(1,W.Powers);yield return null;yield return null;
                        var bar=Hud.EnemyBars.FirstOrDefault(b=>b.Target==npc);
                        Check(bar!=null&&bar.Value.text==npc.Health.ToString("0.#")+" / "+npc.MaxHealth.ToString("0.#")&&bar.ShownMaxHealth==npc.MaxHealth,
                            "Health bar reads this spawned rolled maximum, not archetype mean.");
                    }
                    Destroy(npc.gameObject);yield return null;
                }
                Check(Mathf.Abs(values.Average()/mean-1)<.035f&&values.Distinct().Count()>24,"Spawn sample diverse and mean within 3.5% of authored midpoint.");
                if(pass==2)Check(values.Where((v,i)=>v!=first[i]).Count()>24,"Different seed changes actual spawn sequence.");
            }
            Seed();var scaled=CityNpc.Spawn(W,W.City.Sidewalks[0],NpcRole.Criminal,archetype,mean*2,17);
            float hp=scaled.MaxHealth;int draws=W.Rolls.HealthSamples;yield return null;yield return null;
            Check(hp>=mean*1.8f&&hp<=mean*2.2f&&scaled.MaxHealth==hp&&W.Rolls.HealthSamples==draws&&scaled.ContactDamage==17,
                "Explicit scaled spawn rolls once in scaled range; updates do not reroll or randomize outgoing damage.");
            Destroy(scaled.gameObject);yield return null;
        }
    }
    IEnumerator Instant(string id,float mean)
    {
        Seed();
        for(int i=0;i<20;i++)
        {
            Ready(id);var target=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);
            CityNpc second=null;
            if(id=="fire"||id=="lightning")second=Actor(new Vector3(id=="fire"?1.2f:4,150.05f,10),NpcRole.Criminal,1000);
            hits.Clear();Aim(Chest(target));float expectedDamage=Expected(mean);int draws=W.Rolls.DamageSamples;
            Check(W.Powers.Use(Runtime(id)),"Accepted real "+id+" cast.");yield return WaitHit(target);
            float value=hits.First(h=>h.Target==target).ResolvedDamage;
            Check(Mathf.Abs(value-expectedDamage)<.003f&&W.Rolls.DamageSamples==draws+1,"One seeded damage roll at accepted attack boundary.");
            Range(value,mean,id);
            if(second!=null)
            {
                float factor=id=="fire"?1:.85f;
                Check(hits.Any(h=>h.Target==second&&Mathf.Abs(h.ResolvedDamage-value*factor)<.003f),"Blast/chain victims derive from shared cast roll.");
                Destroy(second.gameObject);
            }
            Destroy(target.gameObject);yield return null;
        }
        Distribution(mean,id);
    }
    IEnumerator Melee(string move)
    {
        Seed();float mean=move=="punch"?35:move=="combo"?41.65f:11.9f;
        for(int i=0;i<20;i++)
        {
            Ready("strength");var target=Actor(new Vector3(0,150.05f,2.7f),NpcRole.Criminal,1000);hits.Clear();
            if(move=="combo")
            {
                // Standalone RMB resets a pending E string, without changing the combo clock.
                Check(W.Hero.TryHurricaneKick(),"Reset pending combo via real RMB action.");yield return WaitHit(target);Expected(11.9f);
                W.Powers.Tick(100,true);Check(W.Hero.TryComboAttack(),"Start E combo.");yield return WaitHit(target,2);Expected(35);
                W.Powers.Tick(100,true);
            }
            float valueExpected=Expected(mean);int before=hits.Count;
            Check(move=="punch"?W.Hero.TryPunch():move=="combo"?W.Hero.TryComboAttack():W.Hero.TryHurricaneKick(),"Paid "+move+" accepted.");
            yield return WaitHit(target,before+1);float value=hits.Last().ResolvedDamage;
            Check(Mathf.Abs(value-valueExpected)<.003f,"Melee impact matches seeded roll after windup.");Range(value,mean,move);
            Destroy(target.gameObject);yield return null;
        }
        Distribution(mean,move);
    }
    IEnumerator Throws(float mass)
    {
        Seed();float mean=35*Mathf.Clamp(Mathf.Sqrt(mass/45),.5f,3)*2/7;
        for(int i=0;i<20;i++)
        {
            Ready("telekinesis");var crate=GameObject.CreatePrimitive(PrimitiveType.Cube);crate.transform.position=new Vector3(1,151.5f,4);
            crate.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);
            var body=crate.AddComponent<Rigidbody>();body.mass=mass;body.useGravity=true;Aim(body.position);Physics.SyncTransforms();
            Check(W.Powers.Use(Runtime("telekinesis"))&&W.Powers.HeldBody==body,"Paid real TK grab "+mass+"kg.");yield return new WaitForSeconds(.25f);
            var target=Actor(new Vector3(2,150.05f,18),NpcRole.Criminal,1000);Aim(target.transform.position+Vector3.up*.9f);hits.Clear();
            float expectedDamage=Expected(mean);int draws=W.Rolls.DamageSamples;
            Check(W.Powers.Use(Runtime("telekinesis")),"Real TK release.");
            Check(W.Rolls.DamageSamples==draws,"Throw launch does not roll before impact.");yield return WaitHit(target);
            var impact=body.GetComponent<ThrownProp>();float value=hits.Last().ResolvedDamage;
            Check(impact.Spent&&impact.LastImpulse>0&&Mathf.Abs(value-expectedDamage)<.003f&&W.Rolls.DamageSamples==draws+1,"Mass-derived nerf then one seeded physical impact roll; momentum retained.");
            Range(value,mean,"TK "+mass);Destroy(crate);Destroy(target.gameObject);yield return null;
        }
        Distribution(mean,"TK "+mass);
    }
    IEnumerator Channels()
    {
        Seed();
        for(int i=0;i<20;i++)
        {
            Ready("laser-eyes");var target=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);Aim(Chest(target));hits.Clear();
            float rate=Expected(42);Check(W.Powers.Use(Runtime("laser-eyes")),"Channel begins.");int draws=W.Rolls.DamageSamples;
            for(int j=0;j<4;j++){W.Powers.Channel(true,.1f);yield return null;}
            var state=BeamState.For(W.Powers);Check(hits.Count==4&&hits.All(h=>Mathf.Abs(h.ResolvedDamage-rate*.1f)<.003f)&&
                W.Rolls.DamageSamples==draws&&Mathf.Abs(state.DamagePerSecond-rate)<.003f,"Four channel ticks retain the single seeded DPS roll.");
            Feedback(target);Range(state.DamagePerSecond,42,"Laser DPS");W.Powers.Channel(false,0);Destroy(target.gameObject);yield return null;
        }
        Distribution(42,"Laser DPS");
        Ready("laser-eyes");var full=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);Aim(Chest(full));
        Check(W.Powers.Use(Runtime("laser-eyes")),"Full-energy uptime channel starts.");
        float started=Time.time;
        while(W.Powers.Channeling!=null && Time.time-started<4){yield return null;W.Powers.Channel(true,Time.deltaTime);}
        float uptime=Time.time-started, predicted=W.Powers.MaxEnergy/(64-W.Powers.EnergyRegen);
        Check(W.Powers.Channeling==null&&Mathf.Abs(uptime-predicted)<.12f,
            $"Full-energy Laser uptime {uptime:F4}s vs {predicted:F4}s from 64 drain minus real regen; depleted normally.");
        Destroy(full.gameObject);yield return null;
    }
    IEnumerator Poison()
    {
        Seed();
        for(int i=0;i<20;i++)
        {
            Ready("poison");var target=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);Aim(Chest(target));hits.Clear();
            float rate=Expected(12.5f);Check(W.Powers.Use(Runtime("poison")),"Poison application accepted.");int draws=W.Rolls.DamageSamples;
            yield return WaitHit(target);var status=target.GetComponent<Poisoned>();
            Check(Mathf.Abs(status.DamagePerSecond-rate)<.003f&&Mathf.Abs(status.DamagePerTick-rate*.5f)<.003f,"Application caches its seeded DPS and tick value.");
            if(i<3)
            {
                float end=Time.time+7;while(status.Active&&Time.time<end)yield return null;
                Check(hits.Count==12&&hits.All(h=>Mathf.Abs(h.ResolvedDamage-rate*.5f)<.003f)&&Mathf.Abs(hits.Sum(h=>h.ResolvedDamage)-rate*6)<.003f&&
                    W.Rolls.DamageSamples==draws,"Twelve stable ticks total exactly rolled DPS × six seconds, with no rerolls.");
            }
            Range(status.DamagePerSecond,12.5f,"Poison DPS");Destroy(target.gameObject);yield return null;
        }
        Distribution(12.5f,"Poison DPS");
        Seed();Ready("poison");
        var origin=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);
        var recipient=Actor(new Vector3(3,150.05f,10),NpcRole.Criminal,1000);
        var outside=Actor(new Vector3(9,150.05f,10),NpcRole.Criminal,1000);
        Aim(Chest(origin));Check(W.Powers.Use(Runtime("poison")),"Seeded poison spread source applied.");Expected(12.5f);
        float next=Expected(12.5f);origin.Damage(1000,W.Powers);
        var spread=recipient.GetComponent<Poisoned>();
        Check(spread!=null&&spread.Generation==1&&Mathf.Abs(spread.DamagePerSecond-next)<.003f&&outside.GetComponent<Poisoned>()==null,
            "Spread independently rolls original intended DPS, never compounds parent variance; outside control untouched.");
        Destroy(origin.gameObject);Destroy(recipient.gameObject);Destroy(outside.gameObject);yield return null;
    }
}
#endif
