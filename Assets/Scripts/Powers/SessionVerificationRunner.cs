#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Shared harness for the cloud-branch suites (Roster, Melee, HeroStats): the nested-coroutine runner with console-error
/// capture used by every existing verifier, plus session entry through the saved Hero Forge loadout and the isolated
/// 150 m floor fixtures of HeroForgeVerification (AI-off actors, shoulder camera aimed through the crosshair).
public abstract class SessionVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;
    protected abstract string Folder { get; }
    protected abstract string ResultFile { get; }
    protected abstract IEnumerator Checks();
    readonly List<string> output = new List<string>(); string runtimeFailure;
    protected WorldSession W => WorldSession.Instance;
    protected ForgeCatalog F => Resources.Load<ForgeCatalog>("ForgeCatalog");
    protected Camera Cam => Camera.main;
    protected PowerDefinition Power(string id) => Resources.Load<PowerDefinition>("Powers/" + id);
    protected PowerRuntime Runtime(string id) => W.Powers.Powers.Find(p => p.Definition.Id == id);
    /// Power clock for isolated fixtures. The only per-frame caller of PowerUser.Tick in play is SuperHeroController.Update;
    /// PlaceHero disables that controller so fixtures stay put, which would freeze every cooldown, charge recharge and energy
    /// regen. While the controller is disabled the harness makes exactly that call itself, once per frame, so waits pass
    /// real game time for the powers. Frames the controller runs (keepEnabled) are not ticked twice.
    public int PowerClockTicks { get; private set; }
    void Update()
    {
        var w = WorldSession.Instance;
        if (w == null || w.Hero == null || w.Powers == null || w.Hero.enabled || Time.deltaTime <= 0f) return;
        w.Powers.Tick(Time.deltaTime, true); PowerClockTicks++;
    }
    void Awake() { Application.logMessageReceived += ObserveLog; }
    void OnDestroy() { Application.logMessageReceived -= ObserveLog; }
    void ObserveLog(string message, string trace, LogType type)
    { if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && trace.Contains("Assets/Scripts/")) runtimeFailure = message; }
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder); var stack = new Stack<IEnumerator>(); stack.Push(Checks());
        while (stack.Count > 0)
        {
            object next = null; bool moved = false;
            try { if (runtimeFailure != null) throw new Exception("Gameplay Console error: " + runtimeFailure); moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception e) { Log("FAIL " + e); Write(); Finished(1); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested); else yield return next;
        }
        Write(); Finished(0);
    }
    protected void Log(string line) { output.Add(line); Debug.Log(line); }
    protected void Check(bool ok, string line) { if (!ok) throw new Exception(line); Log("PASS " + line); }
    void Write() { File.WriteAllLines(Folder + ResultFile, output); }
    protected IEnumerator Scene(string name)
    {
        float until = Time.realtimeSinceStartup + 60;
        while (GameFlow.Instance == null || GameFlow.Instance.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && W == null))
        { if (Time.realtimeSinceStartup > until) throw new Exception("Scene timeout: " + name); yield return null; }
        yield return new WaitForSecondsRealtime(.5f);
    }
    protected IEnumerator Home()
    {
        if (SceneManager.GetActiveScene().name != GameFlow.HomeScene) { GameFlow.Instance.Home(); yield return Scene(GameFlow.HomeScene); }
    }
    protected PlayerProgression Profile => FindAnyObjectByType<ModeScreens>().Profile;
    /// Save hero + pair through PlayerProgression.SetLoadout (the Forge save path), then start a real Hero session.
    protected IEnumerator Enter(HeroDefinition hero, string a, string b, string mode = "hero")
    {
        yield return Home();
        Check(Profile.SetLoadout(hero, Power(a), Power(b), hero.Primary, hero.Secondary), $"Save {hero.DisplayName} with {a} + {b} through the loadout API.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/" + mode)); yield return Scene(GameFlow.CityScene);
        Check(W.Powers.IsEquipped(Power(a)) && W.Powers.IsEquipped(Power(b)), $"Session equips {a} + {b}.");
        var rt = Runtime(a);
        if (rt != null && !rt.Definition.Effect.IsFlight && rt != W.Powers.Strength) Check(W.Powers.Select(rt) && W.Powers.Selected == rt, $"{a} selected for LMB (key {W.Powers.SlotNumber(rt)}).");
    }
    protected IEnumerator Enter(string a, string b) { yield return Enter(F.Heroes[0], a, b); }
    protected void Isolate()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Isolated verification floor";
        floor.transform.position = new Vector3(0, 149.5f, 0); floor.transform.localScale = new Vector3(120, 1, 120);
        floor.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Road);
        PlaceHero(new Vector3(0, 150.05f, 0));
        Cam.GetComponent<ThirdPersonCamera>().enabled = false;
    }
    /// Teleport the hero. `keepEnabled` leaves SuperHeroController running (gravity, movement-driven abilities).
    protected void PlaceHero(Vector3 at, bool keepEnabled = false)
    {
        W.Hero.enabled = keepEnabled; var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false;
        W.Hero.transform.position = at; W.Hero.transform.forward = Vector3.forward; cc.enabled = true; W.Hero.ResetMotion(); Physics.SyncTransforms();
    }
    /// Shoulder camera 1 m behind the hero's head, looking at the point: the viewport-centre ray (the crosshair) passes through it.
    protected void Aim(Vector3 point)
    {
        Vector3 head = W.Hero.transform.position + Vector3.up * 1.7f, dir = (point - head).normalized;
        Cam.transform.position = head - dir * 1f + Vector3.up * .2f; Cam.transform.LookAt(point); Physics.SyncTransforms();
    }
    protected CityNpc Actor(Vector3 feet, NpcRole role, float health)
    {
        var npc = CityNpc.Spawn(W, W.City.Sidewalks[0], role);
        if (npc == null) throw new Exception("Actor spawn failed");
        npc.enabled = false; npc.Agent.enabled = false; npc.transform.position = feet; npc.SetCombatStats(health, 0); Physics.SyncTransforms();
        return npc;
    }
    protected GameObject Wall(Vector3 centre, Vector3 size)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Verification wall CONTROL"; wall.transform.position = centre; wall.transform.localScale = size;
        wall.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Brick); Physics.SyncTransforms(); return wall;
    }
    protected static Vector3 Chest(CityNpc npc) => npc.transform.position + Vector3.up * 1.2f;
    protected void Capture(string file)
    {
        var target = new RenderTexture(1280, 720, 24); var previous = Cam.targetTexture; Cam.targetTexture = target; Cam.Render(); Cam.targetTexture = previous;
        var active = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Folder + file, image.EncodeToPNG()); RenderTexture.active = active; Destroy(image); target.Release(); Destroy(target);
        Log("IMAGE " + Folder + file);
    }
}
#endif
