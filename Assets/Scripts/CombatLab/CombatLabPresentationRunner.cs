#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using Unity.Profiling;

public sealed class CombatLabPresentationRunner : MonoBehaviour
{
    public Action<int> Finished;readonly List<string> lines=new List<string>();string runtimeError;int failures;
    CombatLab Lab=>CombatLab.Active;WorldSession W=>Lab.World;VisualElement UI=>Lab.GetComponent<UIDocument>().rootVisualElement;
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)runtimeError=message+"\n"+trace;}
    void Check(bool ok,string message){string line=(ok?"PASS ":"FAIL ")+message;lines.Add(line);Debug.Log(line);if(!ok)failures++;}
    void Record(string line){lines.Add(line);Debug.Log(line);}
    void Click(string id){using(var e=NavigationSubmitEvent.GetPooled()){var button=UI.Q<Button>(id);e.target=button;button.SendEvent(e);}}
    IEnumerator Start()
    {
        yield return null;yield return null;var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            bool next=false;object item=null;
            try{if(runtimeError!=null)throw new Exception(runtimeError);next=stack.Peek().MoveNext();if(next)item=stack.Peek().Current;}
            catch(Exception e){Check(false,e.ToString());break;}
            if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}if(item is IEnumerator nested)stack.Push(nested);else yield return item;
        }
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        Directory.CreateDirectory("Verification/CombatLab");File.WriteAllLines("Verification/CombatLab/presentation.txt",lines);Finished?.Invoke(failures==0?0:1);
    }
    IEnumerator Frames(int n){for(int i=0;i<n;i++)yield return null;}
    IEnumerator Checks()
    {
        string before=File.ReadAllText(WorldSession.VerificationSavePath);
        Check(UI.Q<DropdownField>("lab-enemy").choices.Count==5,"All five archetypes appear in the native dropdown");
        Click("lab-toggle");Check(W.MenuOpen&&TimeArbiter.MenuPaused&&Time.timeScale==0,"Actual panel button pauses practice");
        yield return Capture("controls-1080",1920,1080,true);yield return Capture("controls-720",1280,720,true);
        UI.Q<DropdownField>("lab-enemy").index=3;Click("lab-one");Check(Lab.ActiveCount==1&&Lab.Owned[0].Evasive,"Actual dropdown/spawn-one button creates Skirmisher");
        Click("lab-group");Check(Lab.ActiveCount==4,"Actual group button adds three");
        Click("lab-clear");yield return null;Check(Lab.ActiveCount==0,"Actual clear button removes owned actors");
        W.DamagePlayer(10,true);Click("lab-health");Check(Mathf.Approximately(W.Health,W.MaxHealth),"Actual restore-health button works");
        W.Powers.TrySpendEnergy(10);Click("lab-energy");Check(Mathf.Approximately(W.Powers.Energy,W.Powers.MaxEnergy),"Actual restore-energy button works");
        foreach(string difficulty in new[]{"hard","extreme","normal"})
        {
            UI.Q<DropdownField>("lab-difficulty").value=difficulty;yield return Frames(3);
            Check(W.Difficulty.Id==difficulty&&Lab.ActiveCount==0,"Actual difficulty control rebuilds session: "+difficulty);
            Check(File.ReadAllText(WorldSession.VerificationSavePath)==before,"Difficulty UI never writes source snapshot");
            W.Rolls.FixedUnit=.5f;Lab.Select(4);var guard=Lab.SpawnOne();
            Check(guard!=null&&Mathf.Abs(guard.MaxHealth-W.Tuning.Npcs.CopHealth*guard.Archetype.HealthMultiplier*W.Difficulty.EnemyHealth)<.01f,"Live lab enemy HP scales once: "+difficulty);
            if(guard!=null)
            {
                guard.transform.forward=Vector3.back;float hp=guard.Health;
                guard.Damage(10,W.Powers,true,0,guard.GetComponent<Collider>().bounds.center+Vector3.back*5);
                Check(Mathf.Abs(hp-guard.Health-3.5f*W.Difficulty.PlayerDamage)<.01f,"Active guard damage scales once: "+difficulty);
            }
            Lab.Clear();yield return null;
        }
        var camera=Camera.main;camera.GetComponent<ThirdPersonCamera>().enabled=false;camera.transform.position=new Vector3(6,3,-5);camera.transform.LookAt(new Vector3(0,1,4));
        foreach(var pair in new[]{new KeyValuePair<string,float>("Skirmisher",2),new KeyValuePair<string,float>("Bulwark",-2)})
        {
            var npc=CityNpc.Spawn(W,new Vector3(pair.Value,0,6),NpcRole.Criminal,Resources.Load<EnemyArchetype>("Enemies/"+pair.Key));npc.SetCombatStats(1000,0);npc.AlwaysAggro=true;npc.transform.forward=Vector3.back;Lab.Owned.Add(npc);
            npc.Damage(10,W.Powers);
        }
        yield return new WaitForSeconds(.2f);TimeArbiter.SetMenuPaused(true);yield return Frames(3);yield return Capture("enemy-silhouettes",1920,1080,false);
        var bulwark=Lab.Owned.First(n=>n.HasGuard);Check(bulwark.GuardRaised,"Captured Bulwark is actually guarding");
        bulwark.Damage(5,W.Powers,true,bulwark.Archetype.Guard.BreakImpulse);Check(bulwark.GuardBroken&&!bulwark.GuardRaised,"Captured shield break matches gameplay state");
        yield return Capture("guard-break",1920,1080,false);TimeArbiter.SetMenuPaused(false);
        Lab.Clear();yield return Frames(3);
        // Warm the SAME models/materials before comparing idle and a bounded live mixed crowd.
        SpawnTwelve();yield return Frames(20);var cubes=Lab.Owned.SelectMany(n=>n.GetComponentsInChildren<MeshFilter>()).Select(m=>m.sharedMesh).Distinct().Count();
        Check(cubes==1,"All new armor/visor/shield attachments share one cube mesh");Lab.Clear();yield return Frames(3);
        yield return Measure("idle");SpawnTwelve();yield return Frames(45);yield return Measure("12 mixed enemies");
        Check(Lab.ActiveCount==12&&W.Npcs.Count(n=>n!=null&&!n.Dead)==12,"Performance sample retains exactly twelve live enemies");
        int dataCount=Resources.FindObjectsOfTypeAll<NavMeshData>().Length;Lab.Clear();yield return Frames(3);
        for(int i=0;i<10;i++)
        {
            if(!W.MenuOpen)Click("lab-toggle");Click("lab-reset");yield return Frames(3);
            Check(W.Npcs.Count==0&&FindObjectsByType<WorldSession>().Length==1&&FindObjectsByType<SuperHeroController>().Length==1,"Reset "+(i+1)+" leaves one clean session/hero");
            Check(NavMesh.SamplePosition(Vector3.zero,out _,1,NavMesh.AllAreas),"Reset "+(i+1)+" has valid arena navigation");
        }
        Check(Resources.FindObjectsOfTypeAll<NavMeshData>().Length<=dataCount,"Repeated resets do not retain old arena NavMeshData");
        Check(File.ReadAllText(WorldSession.VerificationSavePath)==before,"Controls, combat and ten resets preserve source snapshot bytes");
    }
    void SpawnTwelve()
    {
        for(int i=0;i<12;i++){Lab.Select(i%5);var npc=Lab.SpawnOne();if(npc!=null)npc.SetCombatStats(npc.MaxHealth,0);}
    }
    IEnumerator Measure(string name)
    {
        var camera=Camera.main;bool wasEnabled=camera.enabled;camera.enabled=false;
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
        var cpu=new List<double>();var frame=new List<float>();var gc=new List<long>();
        using(var allocated=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1))
        {
            for(int i=0;i<180;i++)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();camera.Render();watch.Stop();cpu.Add(watch.Elapsed.TotalMilliseconds);
                frame.Add(Time.unscaledDeltaTime*1000);if(allocated.Valid)gc.Add(allocated.LastValue);yield return null;
            }
        }
        camera.targetTexture=null;camera.enabled=wasEnabled;target.Release();Destroy(target);cpu.Sort();frame.Sort();gc.Sort();
        Record($"MEASURED lab {name}: one explicit 1280x720 camera render per frame, 180 frames; render CPU median/p95 {cpu[90]:F3}/{cpu[171]:F3} ms; whole-Editor frame median/p95 {frame[90]:F3}/{frame[171]:F3} ms; whole-frame GC median {(gc.Count>0?gc[gc.Count/2]:-1)} B; live NPCs {Lab.ActiveCount}. Not standalone FPS or a sustained combat benchmark.");
    }
    IEnumerator Capture(string name,int width,int height,bool controlsOpen)
    {
        var camera=Camera.main;var hud=FindAnyObjectByType<GameHud>();var doc=Lab.GetComponent<UIDocument>();
        var target=new RenderTexture(width,height,24);target.Create();camera.aspect=width/(float)height;
        hud.Panel.targetTexture=target;hud.OverlayPanel.targetTexture=target;doc.panelSettings.targetTexture=target;yield return Frames(3);
        var controls=UI.Q("lab-controls");Check((controls.resolvedStyle.display!=DisplayStyle.None)==controlsOpen,name+": expected control visibility");
        if(controlsOpen)
        {
            var bounds=UI.worldBound;Check(bounds.width>=1200&&bounds.height>=700,name+": native root has a valid rendered layout");
            foreach(var button in controls.Query<Button>().ToList())Check(bounds.Contains(button.worldBound.min)&&bounds.Contains(button.worldBound.max),name+": button fits native panel: "+button.name);
        }
        bool enabled=camera.enabled;camera.enabled=false;camera.targetTexture=target;camera.Render();camera.targetTexture=null;
        hud.Root.MarkDirtyRepaint();hud.OverlayRoot.MarkDirtyRepaint();UI.MarkDirtyRepaint();yield return null;
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();RenderTexture.active=previous;
        Directory.CreateDirectory("Verification/CombatLab/captures");File.WriteAllBytes("Verification/CombatLab/captures/"+name+".png",image.EncodeToPNG());Destroy(image);
        camera.enabled=enabled;camera.ResetAspect();hud.Panel.targetTexture=null;hud.OverlayPanel.targetTexture=null;doc.panelSettings.targetTexture=null;target.Release();Destroy(target);yield return null;
    }
}
#endif
