#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

/// A = mannequin NPCs, B = Sidekick NpcLooks (HumanoidAnimationTuning.SidekickNpcs). Sessions alternate A/B/A/B...; each is a
/// fresh Free Play session (same seeded city), Heat topped to 3 stars, hero parked on the sidewalk of the densest crossing of the
/// whole island, the REAL ThirdPersonCamera placement rendered once per frame (WorldProfile's "gameplay-camera single-render").
/// Samples wait until no other batch-mode Unity runs and are re-taken when one started meanwhile.
public sealed class SidekickNpcProfileRunner : MonoBehaviour
{
    public Action<int> Finished;
    const string Dir="Verification/Sidekick/npc-fps";
    readonly List<string> lines=new List<string>();WorldSession W=>WorldSession.Instance;
    HumanoidAnimationTuning tuning;bool original;int rounds=3;float seconds=5;Camera cam;RenderTexture target;string variants="AB";
    HeroDefinition hero;GameObject heroPrefab;SidekickSuit heroSuit;
    readonly Dictionary<char,List<double>> fps=new Dictionary<char,List<double>>();
    static string Name(char v)=>v=='A'?"A mannequin NPCs":v=='B'?"B sidekick NPCs":v=='C'?"C mannequin hero":v=='D'?"D sidekick hero on the other suit shader (diagnostic)":
        v=='E'?"E hero SkinQuality.Bone2":v=='F'?"F hero blend-shape weights zeroed (diagnostic, changes body shape)":v=='G'?"G hero updateWhenOffscreen=false":"H hero on the unoptimized prefab mesh (no suit Body)";
    Mesh suitBody;
    Material diagnostic;
    void Log(string text){lines.Add(text);Debug.Log("[NPC PROFILE] "+text);Directory.CreateDirectory(Dir);File.WriteAllLines(Dir+"/results.txt",lines);}
    void Check(bool ok,string text){if(!ok)throw new Exception(text);Log("PASS "+text);}
    static string Arg(string name){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,name);return i>=0&&i+1<a.Length?a[i+1]:null;}
    IEnumerator Start()
    {
        if(int.TryParse(Arg("-npcRounds"),out int r)&&r>0)rounds=r;if(float.TryParse(Arg("-npcSeconds"),out float s)&&s>0)seconds=s;if(Arg("-npcVariants") is string v)variants=v;
        foreach(char c in variants)fps[c]=new List<double>();
        tuning=Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning");original=tuning.SidekickNpcs;
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            bool more=false;object current=null;
            try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception e){Log("FAIL "+e);Done(1);yield break;}
            if(!more){stack.Pop();continue;}if(current is IEnumerator next)stack.Push(next);else yield return current;
        }
        Done(0);
    }
    void Done(int code)
    {
        tuning.SidekickNpcs=original;if(hero!=null){hero.CharacterPrefab=heroPrefab;hero.Suit=heroSuit;if(heroSuit!=null)heroSuit.Body=suitBody;}
        Log($"RESTORED HumanoidAnimationTuning.SidekickNpcs={original}{(hero!=null?$", {hero.Id} CharacterPrefab={hero.CharacterPrefab?.name} Suit={hero.Suit?.name}":"")}");Finished(code);
    }
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+180;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null)){if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout "+name);yield return null;}
        for(int i=0;i<8;i++)yield return null;
    }
    static string Shell(string file,string args)
    {
        try{var p=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file,args){RedirectStandardOutput=true,UseShellExecute=false});string t=p.StandardOutput.ReadToEnd().Trim();p.WaitForExit();return t;}
        catch(Exception e){return "unavailable ("+e.Message+")";}
    }
    // Case-insensitive: Unity's asset-import workers are launched with "-batchMode".
    static int BatchUnities(){string s=Shell("/usr/bin/pgrep","-if \"^/Applications/Unity.*/MacOS/Unity .*-batchmode\"");return s.StartsWith("unavailable")?-1:s.Split('\n').Count(l=>l.Trim().Length>0);}
    /// %CPU (ps' decaying average, 100 = one core) of every process that is not this Unity or one of its children: other
    /// agents' tools, imports, compilers. FPS samples are only kept while this stays low.
    static double OtherCpu()
    {
        int self=System.Diagnostics.Process.GetCurrentProcess().Id;double sum=0;
        foreach(var line in Shell("/bin/ps","-Ao pid=,ppid=,pcpu=").Split('\n'))
        {
            var f=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);if(f.Length<3)continue;
            if(int.TryParse(f[0],out int pid)&&int.TryParse(f[1],out int ppid)&&pid!=self&&ppid!=self&&double.TryParse(f[2],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double cpu))sum+=cpu;
        }
        return sum;
    }
    const double QuietCpu=60;
    static readonly string[] Recorders={"PlayerLoop","PostLateUpdate.UpdateAllSkinnedMeshes","PreLateUpdate.DirectorUpdateAnimationBegin","PreLateUpdate.DirectorUpdateAnimationEnd","Camera.Render","Render.OpaqueGeometry","Shadows.RenderShadowMap","Culling","Gfx.WaitForPresentOnGfxThread","Gfx.WaitForGfxCommandsFromMainThread","Gfx.PresentFrame"};
    void Populate(){if(W.Heat<3)W.AddHeat(3-W.Heat);W.ReconcilePolice();}
    IEnumerator Run()
    {
        yield return Scene(GameFlow.HomeScene);var mode=Resources.Load<GameModeDefinition>("Modes/free-play");
        Check(tuning.NpcLooks!=null&&tuning.NpcLooks.Length>0&&tuning.NpcLooks.All(l=>l!=null&&l.Prefab!=null),$"NpcLooks: {string.Join(", ",tuning.NpcLooks.Select(l=>$"{l.Prefab.name} ({l.Prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.vertexCount} verts)"))}; mannequin {tuning.Model.name} ({tuning.Model.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(x=>x.sharedMesh.vertexCount)} verts in {tuning.Model.GetComponentsInChildren<SkinnedMeshRenderer>().Length} skinned meshes).");
        Log($"ENV GPU={SystemInfo.graphicsDeviceName} CPU={SystemInfo.processorType} quality='{QualitySettings.names[QualitySettings.GetQualityLevel()]}' load={Shell("/usr/sbin/sysctl","-n vm.loadavg")} batchUnities={BatchUnities()} rounds={rounds} seconds/sample={seconds}");
        hero=FindAnyObjectByType<ModeScreens>().Profile.SelectedHero;heroPrefab=hero.CharacterPrefab;heroSuit=hero.Suit;suitBody=heroSuit!=null?heroSuit.Body:null;
        Log($"SCENARIO {mode.Id}: {mode.Civilians} civilians, Heat topped to 3 stars; sessions cycle {variants} each round: A = mannequin NPCs + Sidekick hero {hero.DisplayName}={heroPrefab.name}; B = Sidekick NPCs + same hero; C = mannequin NPCs + the mannequin as hero (hero definition's prefab/suit cleared for that session only, restored at exit).");
        target=new RenderTexture(1280,720,24){name="NPC profile 1280x720"};target.Create();
        for(int r=1;r<=rounds;r++)foreach(char variant in variants)
        {
            bool sidekick=variant=='B';tuning.SidekickNpcs=sidekick;string label=Name(variant);
            hero.CharacterPrefab=variant=='C'?null:heroPrefab;hero.Suit=variant=='C'?null:heroSuit;if(heroSuit!=null)heroSuit.Body=variant=='H'?null:suitBody;
            Check(GameFlow.Instance.Select(mode),$"{label} r{r}: enter Free Play.");yield return Scene(GameFlow.CityScene);
            yield return Stage();Populate();yield return new WaitForSeconds(2f);Populate();
            var npcs=W.Npcs.Where(n=>n!=null&&!n.Dead).ToList();
            var skins=npcs.SelectMany(n=>n.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToList();
            var mats=skins.SelectMany(x=>x.sharedMaterials).Distinct().ToList();
            Log($"{label} r{r} STRUCTURE npcs={npcs.Count} (civilians {npcs.Count(n=>n.Role==NpcRole.Civilian)}, cops {npcs.Count(n=>n.Role==NpcRole.Cop)}) npcSkinnedRenderers={skins.Count} npcSkinnedVertices={skins.Sum(x=>(long)x.sharedMesh.vertexCount)} distinctNpcMaterials={mats.Count} suitMaterials={CityMaterials.Current.SuitCount} animators={FindObjectsByType<Animator>().Length}");
            if(r==1&&variant!='C')yield return Controls(sidekick,npcs,skins,mats);
            if(variant=='D')
            {
                var body=W.Hero.GetComponent<HumanoidPresentation>().VisualRoot.GetComponentInChildren<SkinnedMeshRenderer>();
                // D = the other shader than the suit ships with (authored ShaderGraph <-> Standard), same colour map.
                var suit=hero.Suit;var map=(Texture2D)SidekickSuit.MapOf(body.sharedMaterial);suit.AuthoredShader=!suit.AuthoredShader;
                diagnostic=suit.CreateMaterial(map,"Diagnostic other-shader suit",Resources.Load<CityPalette>("CityPalette").Smoothness);suit.AuthoredShader=!suit.AuthoredShader;
                body.sharedMaterial=diagnostic;Log($"D: hero body material -> {diagnostic.name} ({diagnostic.shader.name}) with the suit colour map.");
            }
            {
                var body=W.Hero.GetComponent<HumanoidPresentation>().VisualRoot.GetComponentInChildren<SkinnedMeshRenderer>();var m=body.sharedMesh;
                int nonzero=Enumerable.Range(0,m.blendShapeCount).Count(i=>body.GetBlendShapeWeight(i)!=0);
                if(variant=='E')body.quality=SkinQuality.Bone2;
                if(variant=='F')for(int i=0;i<m.blendShapeCount;i++)body.SetBlendShapeWeight(i,0);
                if(variant=='G')body.updateWhenOffscreen=false;
                Log($"{label} HERO BODY {m.name} (suit Body {(heroSuit!=null&&heroSuit.Body!=null?heroSuit.Body.name:"none")}): {m.vertexCount} verts, {body.bones.Length} bones, {m.blendShapeCount} blend shapes ({nonzero} non-zero before, {Enumerable.Range(0,m.blendShapeCount).Count(i=>body.GetBlendShapeWeight(i)!=0)} now), quality={body.quality}, updateWhenOffscreen={body.updateWhenOffscreen}, skinnedMotionVectors={body.skinnedMotionVectors}");
            }
            if(variant=='C')Check(W.Hero.GetComponent<HumanoidPresentation>().VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>().Length==2,"C: hero body is the two-mesh mannequin.");
            cam=Camera.main;cam.enabled=false;cam.targetTexture=target;
            var warm=System.Diagnostics.Stopwatch.StartNew();while(warm.Elapsed.TotalSeconds<2){cam.Render();yield return null;}
            yield return Measure(label,r,variant);
            if(r==1)Save(target,"street-"+variant);
            cam.targetTexture=null;cam.enabled=true;
            GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
            if(diagnostic!=null){Destroy(diagnostic);diagnostic=null;}
        }
        double Median(List<double> v){var c=v.OrderBy(x=>x).ToList();return c.Count==0?0:c.Count%2==1?c[c.Count/2]:(c[c.Count/2-1]+c[c.Count/2])/2;}
        foreach(var pair in fps)Log($"SUMMARY {Name(pair.Key)}: FPS per session [{string.Join(", ",pair.Value.Select(x=>x.ToString("F2")))}] mean={(pair.Value.Count>0?pair.Value.Average():0):F2} ({(pair.Value.Count>0?1000/pair.Value.Average():0):F2} ms) median={Median(pair.Value):F2}");
        if(fps.ContainsKey('A')&&fps['A'].Count>0)foreach(var pair in fps)if(pair.Key!='A'&&pair.Value.Count>0)Log($"SUMMARY {Name(pair.Key)} vs A (medians): {(Median(pair.Value)/Median(fps['A'])-1)*100:+0.0;-0.0}% FPS, {1000/Median(pair.Value)-1000/Median(fps['A']):+0.00;-0.00} ms/frame");
        if(fps.ContainsKey('A')&&fps['A'].Count>0)foreach(var pair in fps)if(pair.Key!='A'&&pair.Value.Count>0)Log($"SUMMARY {Name(pair.Key)} vs A: {(pair.Value.Average()/fps['A'].Average()-1)*100:+0.0;-0.0}% FPS, {1000/pair.Value.Average()-1000/fps['A'].Average():+0.00;-0.00} ms/frame");
        Log("LIMIT: Editor Play Mode batch throughput at 1280x720, hero parked, idle input; not a standalone-player figure.");
    }
    /// Structure + NpcLod controls for this variant (B must keep LOD working exactly like A).
    IEnumerator Controls(bool sidekick,List<CityNpc> npcs,List<SkinnedMeshRenderer> skins,List<Material> mats)
    {
        if(sidekick)
        {
            var prefabs=tuning.NpcLooks.Select(l=>l.Prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh).ToList();
            Check(skins.All(x=>prefabs.Contains(x.sharedMesh))&&npcs.All(n=>n.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==1),"B: every NPC body is one NpcLooks skinned mesh.");
            Check(mats.All(m=>m.name.StartsWith("Suit/"))&&mats.Count==CityMaterials.Current.SuitCount-1&&mats.Count<npcs.Count,$"B: {mats.Count} shared suit materials for {npcs.Count} NPCs = the cache's distinct (look, role colour, accent) entries (+1 hero suit), never per NPC: {string.Join(", ",mats.Select(m=>m.name))}.");
        }
        else Check(skins.All(x=>x.sharedMesh==tuning.Model.GetComponentsInChildren<SkinnedMeshRenderer>().First(y=>y.name==x.name).sharedMesh),"A: every NPC body is the shared mannequin.");
        // NpcLod decides tiers by distance every frame, so check an NPC it put in each tier by itself.
        var lod=NpcLod.Current;var far=npcs.FirstOrDefault(n=>lod.IsFar(n));var near=npcs.FirstOrDefault(n=>!lod.IsFar(n));
        Check(far!=null&&near!=null,$"{(sidekick?"B":"A")}: NpcLod has NPCs in both tiers (near {lod.NearCount}, far {lod.FarCount}).");
        var fp=far.GetComponent<HumanoidPresentation>();var fb=far.GetComponentsInChildren<SkinnedMeshRenderer>(true);int steps=lod.AnimatorSteps(far);yield return new WaitForSeconds(1f);
        Check(far==null||!lod.IsFar(far)||(!fp.Animator.enabled&&!fp.enabled&&fb.All(x=>!x.updateWhenOffscreen)&&lod.AnimatorSteps(far)>steps),$"{(sidekick?"B":"A")}: far NPC ({far.Role}, {Vector3.Distance(far.transform.position,W.Hero.transform.position):F0} m): Animator disabled and stepped manually ({lod.AnimatorSteps(far)-steps} steps in 1 s), presentation off, skinning only when visible.");
        var np=near.GetComponent<HumanoidPresentation>();var nb=near.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check(lod.IsFar(near)||(np.Animator.enabled&&np.enabled&&nb.All(x=>x.updateWhenOffscreen)),$"{(sidekick?"B":"A")}: near NPC ({near.Role}, {Vector3.Distance(near.transform.position,W.Hero.transform.position):F0} m): Animator, presentation and skinning fully on.");
        Log($"{(sidekick?"B":"A")} NpcLod now near(full)={lod.NearCount} far(cheap)={lod.FarCount}");
    }
    IEnumerator Stage()
    {
        W.Hero.enabled=false;var plan=W.City.Plan;
        float Density(CityPlan.Crossing x)=>W.City.Buildings.Where(b=>(new Vector2(b.Position.x,b.Position.z)-new Vector2(x.Center.x,x.Center.z)).magnitude<60).Sum(b=>b.Size.y);
        var best=plan.Crossings.OrderByDescending(Density).First();
        var at=new Vector3(best.Center.x-best.Street*.5f-1.5f,W.Tuning.City.SidewalkHeight,best.Center.z-best.Pitch*.5f);
        var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=at;cc.enabled=true;
        var follow=Camera.main.GetComponent<ThirdPersonCamera>();follow.enabled=true;follow.SetLook(0,W.Tuning.Camera.Pitch);
        yield return new WaitForSeconds(1.5f);
        Log($"VIEW densest crossing of the island: {plan.Districts[best.District].Name} {best.Center} ({Density(best):F0} m of building height within 60 m); hero {at}; camera {Camera.main.transform.position}");
    }
    IEnumerator Measure(string label,int round,char variant)
    {
        for(int attempt=1;attempt<=3;attempt++)
        {
            var wait=System.Diagnostics.Stopwatch.StartNew();
            while((BatchUnities()>1||OtherCpu()>QuietCpu)&&wait.Elapsed.TotalMinutes<30){cam.Render();yield return new WaitForSecondsRealtime(5f);Populate();}
            if(wait.Elapsed.TotalSeconds>1)Log($"HYGIENE waited {wait.Elapsed.TotalSeconds:F0}s for other batch Unity processes / other CPU load before {label} r{round}");
            Populate();yield return new WaitForSecondsRealtime(.4f);int before=BatchUnities();double cpuBefore=OtherCpu();
            var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
            var recs=handles.Select(h=>(h,n:ProfilerRecorderHandle.GetDescription(h).Name)).Where(x=>Recorders.Contains(x.n)).GroupBy(x=>x.n).Select(g=>g.First()).Select(x=>(x.n,rec:new ProfilerRecorder(x.h,1,ProfilerRecorderOptions.Default))).ToList();
            foreach(var x in recs)x.rec.Start();var sums=new double[recs.Count];int frames=0;
            var times=new List<double>();var draws=new List<int>();var watch=System.Diagnostics.Stopwatch.StartNew();double prior=0;
            while(watch.Elapsed.TotalSeconds<seconds){cam.Render();yield return null;double now=watch.Elapsed.TotalSeconds;times.Add((now-prior)*1000);prior=now;draws.Add(UnityStats.drawCalls);for(int i=0;i<recs.Count;i++)sums[i]+=recs[i].rec.LastValue;frames++;}
            watch.Stop();string profile=string.Join(" ",recs.Select((x,i)=>$"{x.n}={sums[i]/Math.Max(1,frames)/1e6:F2}"));foreach(var x in recs)x.rec.Dispose();int after=BatchUnities();double cpuAfter=OtherCpu();times.Sort();draws.Sort();double f=times.Count/watch.Elapsed.TotalSeconds;
            bool dirty=before>1||after>1||cpuBefore>QuietCpu||cpuAfter>QuietCpu;var lod=NpcLod.Current;
            Log($"MEASURED {label} r{round} attempt{attempt}: FPS={f:F2} p95={times[(int)(times.Count*.95f)]:F2}ms frames={times.Count} drawCalls median={draws[draws.Count/2]} npcs={W.Npcs.Count(n=>n!=null&&!n.Dead)} heat={W.Heat:F2} npcLod near/far={lod.NearCount}/{lod.FarCount} visibleNpcBodies={W.Npcs.Where(n=>n!=null).SelectMany(n=>n.GetComponentsInChildren<SkinnedMeshRenderer>()).Count(x=>x.isVisible)} batchUnities before/after={before}/{after} otherCpu before/after={cpuBefore:F0}%/{cpuAfter:F0}%{(dirty?" CONTAMINATED":"")} load={Shell("/usr/sbin/sysctl","-n vm.loadavg")}");
            Log($"   PROFILE ms/frame: {profile}");
            if(!dirty){fps[variant].Add(f);yield break;}
        }
        Log($"FAILED to get an uncontaminated sample for {label} r{round}");
    }
    void Save(RenderTexture rt,string name)
    {
        var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes($"{Dir}/{name}.png",image.EncodeToPNG());RenderTexture.active=previous;Destroy(image);
    }
}
#endif
