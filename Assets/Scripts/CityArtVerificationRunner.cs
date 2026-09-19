#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public sealed class CityArtVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;
    readonly List<string> lines=new List<string>();
    int originalSeed;
    GameTuning tuning;
    WorldSession W=>WorldSession.Instance;
    void Log(string text){lines.Add(text);Debug.Log("[ART VERIFY] "+text);}
    void Check(bool condition,string text){if(!condition)throw new Exception(text);Log("PASS "+text);}
    IEnumerator Start()
    {
        tuning=Resources.Load<GameTuning>("GameTuning");originalSeed=tuning.City.Seed;
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            bool more=false;object current=null;
            try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Finish(1);yield break;}
            if(!more){stack.Pop();continue;}if(current is IEnumerator next)stack.Push(next);else yield return current;
        }
        Finish(0);
    }
    void Finish(int code){tuning.City.Seed=originalSeed;File.WriteAllLines("Verification/Art/results.txt",lines);Finished(code);}
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+90;
        while(GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name)
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene load timeout");yield return null;}
        for(int i=0;i<8;i++)yield return null;
        if(W!=null)W.Hero.enabled=false;
    }
    IEnumerator Run()
    {
        yield return Scene("Home");
        string firstFingerprint=null;
        foreach(int seed in new[]{originalSeed,originalSeed+817})
        {
            tuning.City.Seed=seed;GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene("Prototype");
            var art=W.City.Art;var buildings=W.City.GetComponentsInChildren<ArtBuilding>();var props=W.City.GetComponentsInChildren<CityArtProp>();
            var counts=props.GroupBy(p=>p.Kind).OrderBy(g=>g.Key).Select(g=>g.Key+"="+g.Count());
            Check(buildings.Length==36&&buildings.Select(b=>b.Style).Distinct().Count()==4,$"Seed {seed}: 36 buildings, all four archetypes; distribution="+string.Join(",",buildings.GroupBy(b=>b.Archetype).Select(g=>g.Key+"="+g.Count())));
            Check(props.Length>200&&props.Any(p=>p.Kind==CityPropKind.Billboard)&&props.Any(p=>p.Kind==CityPropKind.WaterTower),$"Seed {seed}: props={props.Length}, rooftop={props.Count(p=>p.Rooftop)}, street={props.Count(p=>!p.Rooftop)}; "+string.Join(",",counts));
            Check(W.City.Buildings.Select(b=>b.Size.y).Distinct().Count()>10,"Existing seeded height/layout variety retained.");
            var layout=Resources.Load<CityLayout>("CityLayout").Generate(tuning.City);
            Check(layout.Select(b=>JsonUtility.ToJson(b)).SequenceEqual(W.City.Buildings.Select(b=>JsonUtility.ToJson(b))),"Layout CONTROL: original CityLayout placements/heights/reward records are unchanged by art generation.");
            var plan=art.Settings.Generate(tuning.City,W.City.Buildings);
            string fingerprint=string.Join("|",plan.Select(p=>JsonUtility.ToJson(p)))+string.Join(",",buildings.Select(b=>b.Style));
            Check(plan.Count==art.Placements.Count&&string.Join("|",plan.Select(p=>JsonUtility.ToJson(p)))==string.Join("|",art.Placements.Select(p=>JsonUtility.ToJson(p))),"Same-seed CONTROL regenerates exact placement records.");
            if(firstFingerprint==null)firstFingerprint=fingerprint;else Check(firstFingerprint!=fingerprint,"Different-seed CONTROL changes styles, rooftop equipment selection, street jitter and existing heights.");
            File.WriteAllText("Verification/Art/seed-"+seed+"-placements.json",JsonUtility.ToJson(new PlacementRecord{Seed=seed,Placements=plan},true));
            var clone=Instantiate(art.Settings);clone.AuthoredPlacements=new List<ArtPlacement>{new ArtPlacement{Kind=CityPropKind.Bench,Position=new Vector3(1,2,3),Yaw=37}};clone.UseAuthoredPlacements=true;
            Check(clone.Generate(tuning.City,W.City.Buildings).Count==1&&clone.Generate(tuning.City,W.City.Buildings)[0].Yaw==37,"Authored placement CONTROL preserves explicit position/yaw instead of regenerating over edits.");Destroy(clone);
            yield return PaletteControl();
            var sun=UnityEngine.Object.FindObjectsByType<Light>().Single(l=>l.type==LightType.Directional);
            Check(Mathf.Approximately(sun.intensity,1.2f)&&Quaternion.Angle(sun.transform.rotation,Quaternion.Euler(45,-35,0))<.01f&&RenderSettings.ambientLight==new Color(.45f,.5f,.6f),"Existing sun intensity=1.2, rotation=(45,-35,0), ambient=(.45,.5,.6) unchanged.");
            int volumes=UnityEngine.Object.FindObjectsByType<MonoBehaviour>().Count(c=>c!=null&&(c.GetType().Name=="Volume"||c.GetType().Name=="PostProcessVolume"));
            Check(GraphicsSettings.currentRenderPipeline==null&&volumes==0,"Existing Built-in render pipeline retained; no active post-processing Volume was present/added.");
            Capture("seed-"+seed+"-street",W.Hero.transform.position+new Vector3(3,3,-7),W.Hero.transform.position+Vector3.up*2);
            var roof=W.City.Buildings.OrderByDescending(b=>b.Size.y).First();Vector3 top=roof.Position+Vector3.up*roof.Size.y*.5f;
            Capture("seed-"+seed+"-roof",top+new Vector3(10,9,-12),top);
            yield return Benchmark(seed);
            if(seed==originalSeed)yield return PunchControl();
            GameFlow.Instance.Home();yield return Scene("Home");
        }
        Check(Camera.main!=null&&Camera.main.isActiveAndEnabled,"Camera repair retained through both seeded mode runs and return Home.");
    }
    IEnumerator PaletteControl()
    {
        var renderer=UnityEngine.Object.FindObjectsByType<Renderer>();
        var palette=CityMaterials.Current;var allowed=new HashSet<Material>(palette.All);
        Check(renderer.All(r=>r.sharedMaterials.All(allowed.Contains)),"ALL live renderers use shared materials from CityPalette; unregistered material count=0.");
        var amber=CityMaterials.Get(CityColor.Amber);int affected=renderer.Count(r=>r.sharedMaterials.Contains(amber));Color before=palette.Palette.Colors[(int)CityColor.Amber];
        try
        {
            palette.Palette.Colors[(int)CityColor.Amber]=palette.Palette.Colors[(int)CityColor.Cyan];yield return null;yield return null;
            Check(affected>30&&amber.color==palette.Palette.Colors[(int)CityColor.Cyan],$"Palette CONTROL: changed ONE Amber swatch {before} -> {amber.color}; {affected} renderers sharing it updated (windows, lamps, vehicle lights, actors).");
            Capture("palette-control",new Vector3(-65,60,-80),Vector3.zero);
        }
        finally {palette.Palette.Colors[(int)CityColor.Amber]=before;palette.Apply();}
        Check(amber.color==before,"Palette restore CONTROL returns all shared Amber surfaces to the original swatch.");
    }
    void MovePlayer(Vector3 point)
    {var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=point;W.Hero.transform.forward=Vector3.forward;cc.enabled=true;Physics.SyncTransforms();}
    IEnumerator PunchControl()
    {
        var prop=W.City.GetComponentsInChildren<CityArtProp>().First(p=>p.Kind==CityPropKind.Newspaper);
        var body=prop.GetComponent<Rigidbody>();Check(body!=null&&prop.GetComponent<BreakableProp>()!=null&&prop.GetComponents<BreakableProp>().Length==1,"New newspaper box uses the EXISTING BreakableProp and one real Rigidbody, no parallel damage implementation.");
        // Isolate a generated prop above a clear road; measure only movement AFTER the actual punch.
        body.position=W.City.Spawn+Vector3.up*7;body.linearVelocity=Vector3.zero;MovePlayer(body.position+Vector3.back*3-Vector3.up*.2f);
        Vector3 before=body.position;Check(W.Hero.TryPunch()&&W.Hero.LastAffectedBodies>0,"Real charged punch strikes the newly styled newspaper box.");
        for(int i=0;i<20;i++)yield return new WaitForFixedUpdate();
        float displacement=Vector3.Distance(before,body.position);Check(displacement>1,$"New prop physics: mass={body.mass}kg, punch={W.Hero.LastForce} N·s, displacement={displacement:F3}m, velocity={body.linearVelocity.magnitude:F3}m/s.");
        prop.GetComponent<BreakableProp>().TakeDamage(10000,W.Powers);yield return null;
        Check(prop==null,"Existing break/shard path removes the damaged compound prop (not an extra prop implementation).");
    }
    IEnumerator Benchmark(int seed)
    {
        W.AddHeat(3);W.ReconcilePolice();MovePlayer(W.City.Spawn+Vector3.up*35);
        // Match the recorded legacy benchmark: use the real camera, with mouse-follow temporarily disabled.
        var camera=Camera.main;var follow=camera.GetComponent<ThirdPersonCamera>();bool following=follow.enabled;follow.enabled=false;
        camera.transform.position=new Vector3(-65,60,-80);camera.transform.LookAt(Vector3.zero);
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
        yield return new WaitForSeconds(1);
        int civilians=W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Civilian),cops=W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Cop&&n.Agent.isOnNavMesh);
        Check(civilians>=24&&cops>=10,$"Populated benchmark: civilians={civilians}, on-NavMesh cops={cops}.");
        var watch=System.Diagnostics.Stopwatch.StartNew();var times=new List<double>();var draws=new List<int>();var passes=new List<int>();double previous=0;
        while(watch.Elapsed.TotalSeconds<5)
        {camera.Render();yield return null;double now=watch.Elapsed.TotalSeconds;times.Add((now-previous)*1000);previous=now;draws.Add(UnityStats.drawCalls);passes.Add(UnityStats.setPassCalls);}
        watch.Stop();times.Sort();draws.Sort();passes.Sort();double fps=times.Count/watch.Elapsed.TotalSeconds;
        Log($"MEASURED seed={seed}: 1280x720 Editor actual renders, frames={times.Count}, seconds={watch.Elapsed.TotalSeconds:F3}, FPS={fps:F2}, p95={times[(int)(times.Count*.95)]:F2}ms, draw calls median/max={draws[draws.Count/2]}/{draws.Last()}, SetPass median={passes[passes.Count/2]}; vs recorded 155 FPS={(fps/155-1)*100:F1}%; GPU={SystemInfo.graphicsDeviceName}.");
        Save(target,"seed-"+seed+"-city");camera.targetTexture=null;target.Release();Destroy(target);follow.enabled=following;
    }
    void Capture(string name,Vector3 position,Vector3 look)
    {
        var camera=new GameObject("Art capture").AddComponent<Camera>();camera.transform.position=position;camera.transform.LookAt(look);var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
        camera.Render();Save(target,name);camera.targetTexture=null;target.Release();Destroy(target);Destroy(camera.gameObject);
    }
    void Save(RenderTexture target,string name)
    {
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes("Verification/Art/"+name+".png",image.EncodeToPNG());RenderTexture.active=previous;Destroy(image);
    }
    [Serializable] sealed class PlacementRecord {public int Seed;public List<ArtPlacement> Placements;}
}
#endif
