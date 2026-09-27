#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Uses only pre-feature APIs, so the exact same harness compiles/runs on the parent commit for an honest A/B.
public sealed class PowerPayoffBenchmarkRunner : MonoBehaviour
{
    public Action<int> Finished;
    readonly List<string> lines=new List<string>();
    WorldSession W=>WorldSession.Instance;
    int freezes,punches,grabs,throws;
    void Log(string s){lines.Add(s);UnityEngine.Debug.Log(s);File.WriteAllLines("Verification/Payoff/benchmark.txt",lines);}
    IEnumerator Start()
    {
        Directory.CreateDirectory("Verification/Payoff");var steps=new Stack<IEnumerator>();steps.Push(Run());
        while(steps.Count>0)
        {
            bool more=false;object next=null;
            try{more=steps.Peek().MoveNext();if(more)next=steps.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Finished(1);yield break;}
            if(!more){steps.Pop();continue;}if(next is IEnumerator child)steps.Push(child);else yield return next;
        }
        Finished(0);
    }
    IEnumerator Run()
    {
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||FindAnyObjectByType<ModeScreens>()==null)yield return null;
        var profile=FindAnyObjectByType<ModeScreens>().Profile;
        profile.SetLoadout(Resources.Load<ForgeCatalog>("ForgeCatalog").Heroes[0],Resources.Load<PowerDefinition>("Powers/ice"),Resources.Load<PowerDefinition>("Powers/telekinesis"),CityColor.Blue,CityColor.Cyan);
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));
        while(GameFlow.Instance.Loading||W==null||SceneManager.GetActiveScene().name!=GameFlow.CityScene)yield return null;
        yield return new WaitForSeconds(1);
        var user=W.Powers;user.Hero.enabled=false;
        var cam=Camera.main;cam.GetComponent<ThirdPersonCamera>().enabled=false;
        Vector3 at=user.transform.position;cam.transform.position=at+new Vector3(0,2.8f,-7.6f);cam.transform.LookAt(at+Vector3.forward*15+Vector3.up);
        var rt=new RenderTexture(1280,720,24);rt.Create();cam.targetTexture=rt;
        W.AddHeat(3);
        // Live population/AI remains running. Only player health is restored to hold an identical sustained-fight fixture.
        var health=typeof(WorldSession).GetField("<Health>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        yield return new WaitForSeconds(2);
        Log($"POPULATION civilians={W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Civilian)}, cops={W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Cop)}; 1280x720 single camera, AI active; test-only health restore.");
        for(int sample=0;sample<3;sample++)
        {
            var watch=Stopwatch.StartNew();int frames=0;long drawSum=0;float next=Time.time;
            while(watch.Elapsed.TotalSeconds<10)
            {
                health.SetValue(W,100f);
                if(Time.time>=next){StartCoroutine(Combo(at));next=Time.time+2;}
                frames++;drawSum+=UnityStats.drawCalls;yield return null;
            }
            Log($"MEASURED fight sample={sample} FPS={frames/watch.Elapsed.TotalSeconds:F2} draws={(double)drawSum/frames:F1} frames={frames} seconds={watch.Elapsed.TotalSeconds:F3} freezes={freezes} punches={punches} grabs={grabs} throws={throws} particles={FeelDirector.Instance.Particles.ParticlesEmitted}");
        }
        if(freezes<6||punches<6||grabs<6||throws<6)throw new Exception("Not enough successful real ability cycles for combat benchmark");
        Log("PASS repeated real Ice / melee / Telekinesis use in populated city; no manual camera double render. Editor throughput, not standalone or human feel.");
    }
    IEnumerator Combo(Vector3 at)
    {
        var user=W.Powers;user.Tick(100,true);
        // Fixed nearby target for each combo, with ambient NPCs still fighting in the same streets.
        var npc=CityNpc.Spawn(W,W.City.NearestSidewalk(at),NpcRole.Criminal);npc.enabled=false;npc.Agent.enabled=false;npc.transform.position=at+Vector3.forward*2.7f;npc.SetCombatStats(1000,0);
        var ice=user.Powers.Find(p=>p.Definition.Id=="ice");var tk=user.Powers.Find(p=>p.Definition.Id=="telekinesis");
        var cam=Camera.main;Quaternion view=cam.transform.rotation;cam.transform.LookAt(npc.transform.position+Vector3.up);Physics.SyncTransforms();
        user.Select(ice);if(user.Use(ice))freezes++;if(user.Hero.TryPunch())punches++;
        yield return new WaitForSeconds(.3f);
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Benchmark throw prop";go.transform.position=at+new Vector3(1,1.5f,3);go.transform.localScale=Vector3.one*.5f;go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);
        var rb=go.AddComponent<Rigidbody>();rb.mass=45;rb.useGravity=false;
        cam.transform.LookAt(go.transform.position);Physics.SyncTransforms();user.Select(tk);if(user.Use(tk))grabs++;
        yield return new WaitForSeconds(.2f);
        cam.transform.LookAt(at+Vector3.forward*16+Vector3.up);if(user.HeldBody!=null&&user.Use(tk))throws++;
        cam.transform.rotation=view;
        Destroy(go,1.2f);Destroy(npc.gameObject,1.2f);
    }
}
#endif
