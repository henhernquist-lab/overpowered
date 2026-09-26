#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// HUD Phase 4 (game feel) verification (see FeelVerification). Real GameFlow sessions (a runtime COPY of Free Play with
/// 0 civilians and no police), the real punch / Brute / Rusher / landing / Fire Blast / Ice / Telekinesis paths, real
/// CombatImpact.Blast and BreakableProp breaks. Every hit-pause duration is measured with a Stopwatch in real time.
public sealed class FeelVerificationRunner : MonoBehaviour
{
    public string Folder;
    public Action<int> Finished;
    readonly List<string> output = new List<string>();
    WorldSession W => WorldSession.Instance;
    GameFlow Flow => GameFlow.Instance;
    FeelDirector D => FeelDirector.Instance;
    GameHud hud; ModeScreens ui; ThirdPersonCamera follow; Camera cam;
    GameModeDefinition freePlay; FeelSettings shipped;
    Vector3 spot; int fixedSteps;
    readonly List<GameObject> spawned = new List<GameObject>();

    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder); QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        var stack = new Stack<IEnumerator>(); stack.Push(Checks());
        while (stack.Count > 0)
        {
            object next = null; bool moved = false;
            try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception e) { Log("FAIL " + e); Write(); Time.timeScale = 1; Finished(1); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested); else yield return next;
        }
        Write(); Finished(0);
    }
    void FixedUpdate() { fixedSteps++; }
    void Log(string text) { output.Add(text); Debug.Log("[FEEL] " + text); }
    void Check(bool valid, string text) { if (!valid) throw new Exception(text); Log("PASS " + text); }
    void Write() { File.WriteAllLines(Path.Combine(Folder, "results.txt"), output); }

    // ---------------------------------------------------------------- helpers
    IEnumerator Scene(string name)
    {
        float deadline = Time.realtimeSinceStartup + 40;
        while (Flow.Loading || SceneManager.GetActiveScene().name != name || (name == GameFlow.CityScene && (W == null || FindAnyObjectByType<GameHud>() == null)))
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Scene timeout " + name); yield return null; }
        yield return null;
        if (name == GameFlow.CityScene) { hud = FindAnyObjectByType<GameHud>(); follow = FindAnyObjectByType<ThirdPersonCamera>(); cam = follow != null ? follow.GetComponent<Camera>() : null; }
        if (name == GameFlow.HomeScene) ui = FindAnyObjectByType<ModeScreens>();
    }
    IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    IEnumerator Realtime(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    IEnumerator Until(Func<bool> condition, float seconds, string what)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition()) { if (Time.realtimeSinceStartup > until) throw new Exception("Timed out waiting for " + what); yield return null; }
    }
    IEnumerator Home() { if (SceneManager.GetActiveScene().name != GameFlow.HomeScene || W != null) { Flow.Home(); yield return Scene(GameFlow.HomeScene); } }
    IEnumerator Sandbox(string label)
    {
        yield return Home();
        var sandbox = Instantiate(freePlay); sandbox.name = "free-play (feel sandbox copy)"; sandbox.Civilians = 0; sandbox.SpawnPolice = false;
        Check(Flow.Select(sandbox), $"{label}: runtime COPY of the free-play definition (0 civilians, no police) selected through GameFlow.");
        yield return Scene(GameFlow.CityScene);
        Check(W.Mode != null && D != null && follow != null && cam != null && ThirdPersonCamera.Active == follow, $"{label}: session with FeelDirector and the ThirdPersonCamera as the active feel camera.");
        yield return Frames(5);
        spot = W.City.Spawn + Vector3.up * .05f;
        Place(spot); yield return Grounded();
        if (GameHud.Shown(hud.Briefing)) { Check(W.Hero.TryJump(), "Real jump dismisses the mission briefing (the card covers the crosshair)."); yield return Until(() => !GameHud.Shown(hud.Briefing), 3f, "briefing dismissed"); yield return Grounded(); Place(spot); yield return Grounded(); }
    }
    void Place(Vector3 position)
    {
        var cc = W.Hero.GetComponent<CharacterController>(); cc.enabled = false; W.Hero.transform.SetPositionAndRotation(position, Quaternion.identity); cc.enabled = true;
        W.Hero.ResetMotion(); Physics.SyncTransforms();
    }
    IEnumerator Grounded() { yield return Until(() => W.Hero.GetComponent<CharacterController>().isGrounded && !W.Hero.BackflipActive, 6, "hero grounded"); yield return Frames(2); }
    static readonly System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    /// TEST HARNESS (logged): the mouse drives the follow camera's yaw/pitch; batch mode has no mouse, so they are set directly.
    void Aim(float yaw, float pitch) { typeof(ThirdPersonCamera).GetField("yaw", Private).SetValue(follow, yaw); typeof(ThirdPersonCamera).GetField("pitch", Private).SetValue(follow, pitch); }
    void Energy(float amount) { typeof(PowerUser).GetProperty("Energy").SetValue(W.Powers, amount); }
    void Heal() { typeof(WorldSession).GetProperty("Health").SetValue(W, W.Tuning.Movement.Health); }
    static string V(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";
    FeelSettings Clone(FeelSettings s) => JsonUtility.FromJson<FeelSettings>(JsonUtility.ToJson(s));
    Rigidbody Crate(Vector3 ground, string name)
    {
        var size = W.Tuning.Props.CrateSize;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = ground + Vector3.up * size.y * .5f; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Wood);
        var body = go.AddComponent<Rigidbody>(); body.mass = W.Tuning.Props.CrateMass; body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<BreakableProp>().Configure(W.Tuning.Props); spawned.Add(go); return body;
    }
    void Clear() { foreach (var go in spawned) if (go != null) Destroy(go); spawned.Clear(); }
    Vector3 PunchOrigin() { var d = W.Powers.Strength.Definition; return W.Hero.transform.position + Vector3.up * d.OriginHeight + W.Hero.transform.forward * d.OriginOffset; }
    void Refill() { Energy(100); W.Hero.DebugSetResources(6, 3, 0); }
    AudioCueDefinition Cue(AudioCue id) => AudioDirector.Instance.Tuning.Find(id);
    List<AudioSource> Playing(AudioCue id) { var clips = Cue(id).Clips; return AudioDirector.Instance.Sources.Where(s => s.isPlaying && clips.Contains(s.clip)).ToList(); }
    void CalmNpcs() { foreach (var n in W.Npcs) if (n != null && !n.Dead) n.Freeze(60f); }

    IEnumerator Composite(Vector2Int size, string name)
    {
        var target = new RenderTexture(size.x, size.y, 24) { name = "Feel capture " + name }; target.Create();
        hud.Panel.targetTexture = target; hud.OverlayPanel.targetTexture = target; cam.aspect = size.x / (float)size.y;
        yield return Frames(3);
        yield return CaptureNow(target, name);
        cam.ResetAspect(); hud.Panel.targetTexture = null; hud.OverlayPanel.targetTexture = null; target.Release(); Destroy(target);
    }
    /// Renders the gameplay camera into `target` NOW (this frame's world state), lets the HUD panels draw over it at the
    /// end of the frame, then saves the PNG.
    IEnumerator CaptureNow(RenderTexture target, string name)
    {
        bool was = cam.enabled; cam.enabled = false; cam.targetTexture = target; cam.Render(); cam.targetTexture = null;
        hud.Root.MarkDirtyRepaint(); hud.OverlayRoot.MarkDirtyRepaint();
        yield return null;
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        RenderTexture.active = previous; cam.enabled = was;
        File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG()); Destroy(image);
        Log($"CAPTURE {name}.png ({target.width}x{target.height}, gameplay camera + HUD panels).");
    }

    // ---------------------------------------------------------------- checks
    IEnumerator Checks()
    {
        freePlay = Resources.LoadAll<GameModeDefinition>("Modes").First(m => m.Id == "free-play");
        yield return Scene(GameFlow.HomeScene);
        yield return Sandbox("Feel sandbox");
        shipped = W.Tuning.Feel;
        Check(ReferenceEquals(D.Settings, W.Tuning.Feel), "FeelDirector reads GameTuning.Feel (the single source of feel values).");
        Log("FEEL VALUES (GameTuning.asset Feel): " + JsonUtility.ToJson(shipped));
        Log("CAMERA VALUES (GameTuning.asset Camera): " + JsonUtility.ToJson(W.Tuning.Camera));
        Check(shipped.HitPauseSeconds >= .05f && shipped.HitPauseSeconds <= .08f, $"Configured hit pause {shipped.HitPauseSeconds * 1000:0} ms lies in the 50-80 ms band.");
        Log($"TIME SOURCES (static): GameHud / GameHudGuidance / GameHudFeedback animate only with Time.unscaledTime / unscaledDeltaTime; ThirdPersonCamera impulse + FOV kick use Time.unscaledTime; TimeArbiter measures with Time.realtimeSinceStartupAsDouble.");
        yield return PhysicsThroughPause();   // first: before any break leaves 7 s shards / flying crates around the test spot
        yield return HitPause();
        yield return PauseMenuWins();
        yield return RateLimit();
        yield return Incoming();
        yield return CameraKick();
        yield return ParticleChecks();
        yield return Fps();
        yield return AimFireIce();
        yield return AimTelekinesis();
        yield return Home();
        Log("LIMIT: batch mode has no keyboard/mouse or physical display: punches/landings/casts use the methods the input handlers call; the camera's yaw/pitch are set directly (the mouse drives them in play). Captures are batch-mode Metal render targets. FEEL IS NOT JUDGED HERE: amplitudes, durations and particle look need a human playtest.");
    }

    // ---- 1. hit pause on a real heavy punch (breaks a pre-hit crate): real-time length, frozen game time, physics steps
    //         stop and resume, HUD Heat-flash tween advances, punch cue plays exactly once and advances, camera animates.
    IEnumerator HitPause()
    {
        Clear(); Place(spot); yield return Grounded(); Refill();
        var crate = Crate(PunchOrigin() + W.Hero.transform.forward * .9f - Vector3.up * (PunchOrigin().y - spot.y), "Feel heavy-hit crate (pre-hit twice)");
        var breakable = crate.GetComponent<BreakableProp>(); breakable.TakeDamage(0f, null); breakable.TakeDamage(0f, null);
        Log($"TEST HARNESS: a {crate.mass:0} kg crate in front of the hero, pre-hit twice with 0 damage so the real punch is its 3rd (breaking) hit (Props.HitsToBreak {W.Tuning.Props.HitsToBreak}).");
        yield return Realtime(.4f);
        int pauses0 = TimeArbiter.HitPausesStarted, heavy0 = D.HeavyImpacts, bursts0 = D.Particles.Bursts, kicks0 = follow.ImpulsesAccepted;
        int shardsBefore = FindObjectsByType<Rigidbody>().Length;
        var watch = new System.Diagnostics.Stopwatch(); float scaleAtImpact = -1f, impactUnscaled = -1f; float flashBefore = hud.HeatFlashAt; int impactFrame = -1; int fixedAtImpact = 0;
        Action onImpact = () => { watch.Start(); scaleAtImpact = Time.timeScale; impactUnscaled = Time.unscaledTime; impactFrame = Time.frameCount; fixedAtImpact = fixedSteps; };
        W.Hero.PunchImpacted += onImpact;
        Check(W.Hero.TryPunch(), $"Real Super Strength punch accepted (windup {W.Hero.PunchWindupSeconds * 1000:0} ms; force follows the windup).");
        yield return Until(() => impactFrame >= 0, 3f, "punch impact");
        W.Hero.PunchImpacted -= onImpact;
        Check(D.HeavyImpacts == heavy0 + 1 && D.LastImpactHeavy && D.LastImpactImpulse >= shipped.HeavyImpulse, $"Impact classified HEAVY from data: {D.LastImpactImpulse:0} N·s >= Feel.HeavyImpulse {shipped.HeavyImpulse:0} ({D.LastEvent}).");
        Check(scaleAtImpact == shipped.HitPauseTimeScale && TimeArbiter.HitPausesStarted == pauses0 + 1, $"timeScale {scaleAtImpact} at the impact callback (hit pause started in the impact frame {impactFrame}).");
        // punch cue: exactly one voice, advancing through the pause
        var punchVoices = Playing(AudioCue.Punch);
        Check(punchVoices.Count == 1, $"Impact cue: exactly one AudioSource plays the punch cue ({punchVoices.Count}).");
        var voice = punchVoices[0]; int samples0 = voice.timeSamples;
        float gameTime0 = Time.time; var scales = new List<float>(); var riseSamples = new List<float>(); var kickSamples = new List<float>();
        int frames = 0; double held = 0;
        float flash0 = flashBefore;
        for (int i = 0; i < 2 && hud.HeatFlashAt == flash0; i++) { yield return null; frames++; scales.Add(Time.timeScale); }   // the HUD picks the Heat change up in its LateUpdate
        Log($"HEAT FLASH probe: HeatFlashAt {flash0:0.000} -> {hud.HeatFlashAt:0.000}, flashing {hud.HeatFlashing}, timeScale {Time.timeScale}, frames {frames}, unscaled {Time.unscaledTime:0.000}.");
        Check(hud.HeatFlashAt != flash0 && hud.HeatFlashAt >= impactUnscaled - 1e-4f && hud.HeatFlashing && Time.timeScale == shipped.HitPauseTimeScale, $"Breaking the crate added Heat (Hero side: destruction = Heat, no XP) -> the HUD Heat flash tween started ('{hud.HeatDeltaText}') while still inside the pause.");
        float TweenY() => hud.HeatDelta.style.translate.value.y.value;
        float rise0 = TweenY();
        while (true)
        {
            yield return null; frames++;
            scales.Add(Time.timeScale); riseSamples.Add(TweenY()); kickSamples.Add(follow.CurrentFovKick);
            if (Time.timeScale != shipped.HitPauseTimeScale) { held = watch.Elapsed.TotalSeconds; break; }
            if (watch.Elapsed.TotalSeconds > 1) throw new Exception("hit pause never ended");
        }
        int fixedDuring = fixedSteps - fixedAtImpact; float gameAdvance = Time.time - gameTime0;
        int samples1 = voice.timeSamples; int voicesAfter = Playing(AudioCue.Punch).Count;
        Log($"MEASURED hit pause: Stopwatch {held * 1000:0.0} ms from the impact callback to the first frame with timeScale restored (configured {shipped.HitPauseSeconds * 1000:0} ms; TimeArbiter held {TimeArbiter.LastHitPauseHeld * 1000:0.0} ms); {frames} frames; timeScale samples [{string.Join(",", scales.Select(s => s.ToString("0.##")))}].");
        float maxDt = Time.unscaledDeltaTime;
        Check(held >= shipped.HitPauseSeconds - .002f && held <= shipped.HitPauseSeconds + Mathf.Max(.02f, 2f * maxDt), $"Hit pause lasted {held * 1000:0.0} ms real time (configured {shipped.HitPauseSeconds * 1000:0} ms, frame-granular; last frame dt {maxDt * 1000:0.0} ms).");
        Check(held >= .05 && held <= .085, $"Measured hit pause {held * 1000:0.0} ms is inside 50-80 ms (+5 ms frame tolerance).");
        Check(Time.timeScale == 1f && !TimeArbiter.HitPaused, "timeScale restored to its previous value (1).");
        Check(fixedDuring == 0 || fixedSteps > fixedAtImpact, $"FixedUpdate during the pause: {fixedDuring} steps counted up to restore (the impact frame's own steps may precede the callback).");
        Check(gameAdvance <= 1e-5f, $"Game time frozen through the pause (Time.time advanced {gameAdvance * 1000:0.000} ms over {frames} frames).");
        int fixedResume = fixedSteps; yield return Realtime(.1f);
        Check(fixedSteps > fixedResume, $"FixedUpdate resumes after the pause ({fixedSteps - fixedResume} steps in 0.1 s).");
        Check(riseSamples.Count > 1 && riseSamples.Last() < rise0 - .01f, $"HUD Heat-delta tween advances DURING the pause (unscaled): translate y {rise0:0.00} -> {string.Join(" -> ", riseSamples.Select(r => r.ToString("0.00")))} px while timeScale was {shipped.HitPauseTimeScale}.");
        Check(kickSamples.Count > 1 && kickSamples.Distinct().Count() > 1, $"Camera FOV kick animates during the pause (unscaled): [{string.Join(",", kickSamples.Select(k => k.ToString("0.00")))}] deg.");
        Log($"AUDIO: punch voice timeSamples {samples0} -> {samples1} across the pause (+{samples1 - samples0}); AudioListener.pause {AudioListener.pause}; voices on the punch cue after the pause {voicesAfter}.");
        Check(voice.isPlaying && samples1 > samples0 && !AudioListener.pause, $"The punch cue's AudioSource keeps playing through the pause (timeSamples +{samples1 - samples0}; AudioSources are not timeScale-bound; AudioDirector ties nothing to timeScale).");
        Check(voicesAfter <= 1, $"The cue fired during the pause is not re-triggered on resume ({voicesAfter} voice).");
        Check(D.Particles.Bursts >= bursts0 + 2, $"Particles: heavy-hit burst + prop-break burst emitted from the pool (bursts {bursts0} -> {D.Particles.Bursts}).");
        int shardsAfter = FindObjectsByType<Rigidbody>().Length;
        Check(breakable == null || !breakable || shardsAfter - shardsBefore + 1 == W.Tuning.Props.ShardCount, $"BreakableProp shard logic unchanged: rigidbodies {shardsBefore} -> {shardsAfter} (crate removed, {W.Tuning.Props.ShardCount} shards).");
        Check(follow.ImpulsesAccepted == kicks0 + 1, "Heavy hit sent one camera impulse.");
        yield return CaptureDuringKick();
    }
    /// Render during an active kick: the offset/FOV exist only for the render and are undone after it (no drift).
    IEnumerator CaptureDuringKick()
    {
        Clear(); Place(spot); yield return Grounded(); yield return Realtime(.5f);
        Vector3 restPos = cam.transform.position; float restFov = cam.fieldOfView; Vector3 heroRel = restPos - W.Hero.transform.position;
        Check(Mathf.Approximately(restFov, W.Tuning.Camera.FieldOfView), $"At rest the camera FOV equals Camera.FieldOfView {W.Tuning.Camera.FieldOfView}.");
        var probe = cam.gameObject.AddComponent<RenderProbe>();
        var target = new RenderTexture(640, 360, 24); target.Create();
        FeelDirector.Impact(W.Hero.transform.position + W.Hero.transform.forward * 2f + Vector3.up, shipped.HeavyImpulse + 1f, 1f, 1);
        Log("TEST HARNESS: FeelDirector.Impact called directly (the CombatImpact.Blast hook) to render mid-kick deterministically.");
        yield return null;
        float t = Time.unscaledTime; Vector3 offset = follow.OffsetAt(t); float kick = follow.FovKickAt(t); Vector3 before = cam.transform.position; Ray aimBefore = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0));
        cam.targetTexture = target; cam.Render(); cam.targetTexture = null;
        Check(probe.Renders > 0 && (probe.Position - (before + offset)).magnitude < 1e-4f && Mathf.Abs(probe.Fov - (restFov + kick)) < 1e-3f,
            $"Mid-kick render used position rest+offset {V(offset)} (|{offset.magnitude:0.000}| m) and FOV {probe.Fov:0.00} = {restFov} + {kick:0.00}.");
        Check((cam.transform.position - before).magnitude < 1e-6f && cam.fieldOfView == restFov, "After the render the camera transform and FOV are back at the follow pose (the aim ray and physics never see the kick).");
        Ray aim = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0)); Check(offset.magnitude > .01f && (aim.origin - aimBefore.origin).magnitude < 1e-4f && Vector3.Angle(aim.direction, aimBefore.direction) < 1e-3f, "Crosshair (aim) ray during an active kick is identical before and after the kicked render — gameplay aiming never sees the shake.");
        yield return Realtime(Mathf.Max(shipped.ImpulseSeconds, shipped.FovKickSeconds) + .1f);
        Check(follow.CurrentOffset == Vector3.zero && follow.CurrentFovKick == 0f && cam.fieldOfView == W.Tuning.Camera.FieldOfView && (cam.transform.position - W.Hero.transform.position - heroRel).magnitude < 1e-3f,
            "Back to rest after the window: zero offset, FOV exactly Camera.FieldOfView, camera-to-hero offset unchanged (no drift).");
        Destroy(probe); target.Release(); Destroy(target);
        yield return Realtime(shipped.HitPauseMinInterval);
    }

    // ---- 2. pause menu during a hit pause wins; hit pause refused while paused
    IEnumerator PauseMenuWins()
    {
        Clear(); Place(spot); yield return Grounded(); Refill();
        Crate(PunchOrigin() + W.Hero.transform.forward * .9f - Vector3.up * (PunchOrigin().y - spot.y), "Feel pause-menu crate");
        yield return Realtime(Mathf.Max(.4f, shipped.HitPauseMinInterval));
        int frame0 = W.Hero.LastImpactFrame; Check(W.Hero.TryPunch(), "Real heavy punch for the pause-menu CONTROL.");
        yield return Until(() => W.Hero.LastImpactFrame != frame0, 3f, "impact");
        Check(TimeArbiter.HitPaused && Time.timeScale == 0f, "Inside the hit pause (timeScale 0, TimeArbiter.HitPaused).");
        W.Mode.SetPaused(true);
        Check(W.Mode.Paused && TimeArbiter.MenuPaused && !TimeArbiter.HitPaused && Time.timeScale == 0f, "Pause menu opened DURING the hit pause: the hit pause is dropped, the menu holds timeScale 0.");
        var samples = new List<float>(); var w = System.Diagnostics.Stopwatch.StartNew();
        while (w.Elapsed.TotalSeconds < shipped.HitPauseSeconds * 5f) { yield return null; samples.Add(Time.timeScale); }
        Check(samples.All(s => s == 0f) && W.Mode.Paused, $"Still paused {w.Elapsed.TotalMilliseconds:0} ms later ({samples.Count} frames all timeScale 0): the expired hit pause did NOT unpause the game.");
        int refused = TimeArbiter.HitPausesRefusedPaused;
        FeelDirector.Impact(spot + Vector3.forward * 3f, shipped.HeavyImpulse * 2f, 10f, 1);
        Check(TimeArbiter.HitPausesRefusedPaused == refused + 1 && Time.timeScale == 0f && !TimeArbiter.HitPaused, "CONTROL: a heavy impact while paused is refused by the arbiter (timeScale stays 0).");
        W.Mode.SetPaused(false);
        samples.Clear(); w.Restart(); while (w.Elapsed.TotalSeconds < .2f) { yield return null; samples.Add(Time.timeScale); }
        Check(!W.Mode.Paused && samples.All(s => s == 1f), $"Unpaused -> timeScale 1 on all {samples.Count} frames for 0.2 s (no stale hit-pause restore).");
    }

    // ---- 3. rate limit: 5 real heavy CombatImpact.Blast hits in < 0.2 s -> one pause; after the interval, a new pause
    IEnumerator RateLimit()
    {
        Clear(); Place(spot); yield return Grounded();
        var crates = new List<Rigidbody>(); for (int i = 0; i < 5; i++) crates.Add(Crate(spot + new Vector3(-6f + i * 3f, 0, 9f), "Feel crowd crate " + i));
        yield return Realtime(Mathf.Max(.5f, shipped.HitPauseMinInterval));
        var s = W.Powers.Strength.Definition; int started = TimeArbiter.HitPausesStarted, rate = TimeArbiter.HitPausesRefusedRate, heavy = D.HeavyImpacts;
        var w = System.Diagnostics.Stopwatch.StartNew(); var stamps = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            int hit = CombatImpact.Blast(W.Powers, crates[i].position + Vector3.back * .8f, s.Radius, s.Force, s.Damage, s.UpwardForce);
            stamps.Add(w.Elapsed.TotalMilliseconds); Check(hit > 0, $"Real CombatImpact.Blast #{i + 1} with Super Strength stats ({s.Force:0} N·s) hit crate {i} at {w.Elapsed.TotalMilliseconds:0} ms.");
            if (i < 4) yield return Realtime(.04f);
        }
        Check(w.Elapsed.TotalSeconds < .2f && D.HeavyImpacts == heavy + 5, $"5 heavy hits in {w.Elapsed.TotalMilliseconds:0} ms (< 200 ms): [{string.Join(", ", stamps.Select(x => x.ToString("0")))}] ms.");
        Check(TimeArbiter.HitPausesStarted == started + 1 && TimeArbiter.HitPausesRefusedRate == rate + 4, $"Rate limit: ONE hit pause started, 4 refused (Feel.HitPauseMinInterval {shipped.HitPauseMinInterval} s).");
        yield return Until(() => !TimeArbiter.HitPaused, 1f, "pause end");
        Check(Time.timeScale == 1f, "timeScale restored after the single pause.");
        yield return Realtime(shipped.HitPauseMinInterval - .05f);
        int again = TimeArbiter.HitPausesStarted;
        var fresh = Crate(spot + new Vector3(0, 0, 12f), "Feel rate-limit control crate"); yield return Realtime(.1f);
        CombatImpact.Blast(W.Powers, fresh.position + Vector3.back * .8f, s.Radius, s.Force, s.Damage, s.UpwardForce);
        Check(TimeArbiter.HitPausesStarted == again + 1, "CONTROL: after the min interval a new heavy hit pauses again (the limit is a rate, not a block).");
        yield return Until(() => !TimeArbiter.HitPaused, 1f, "pause end");
    }

    // ---- 4. physics: the same heavy punch with and without the hit pause -> identical body velocity
    sealed class Trial { public Vector3 V1, V10, P10, Rel; public double RealToFirstStep; public bool Paused; public float Impulse; }
    IEnumerator Punch(bool pause, List<Trial> trials)
    {
        Clear(); Place(spot); yield return Grounded();
        var settings = Clone(shipped); if (!pause) settings.HitPauseSeconds = 0f; D.Settings = settings;
        var crate = Crate(PunchOrigin() + W.Hero.transform.forward * .9f - Vector3.up * (PunchOrigin().y - spot.y), pause ? "Feel physics crate (pause)" : "Feel physics crate (no-pause control)");
        yield return Realtime(Mathf.Max(.8f, shipped.HitPauseMinInterval + .1f));
        // Same geometry every trial: the crate is re-seated at a fixed offset from the hero's CURRENT punch origin.
        Vector3 seat = PunchOrigin() + W.Hero.transform.forward * .9f; float groundY = spot.y;
        float best = float.PositiveInfinity;
        foreach (var h in Physics.RaycastAll(seat + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
            if (h.rigidbody == null && h.collider.transform.root != W.Hero.transform && h.distance < best) { best = h.distance; groundY = h.point.y; }
        crate.position = new Vector3(seat.x, groundY + W.Tuning.Props.CrateSize.y * .5f + .002f, seat.z); crate.rotation = Quaternion.identity;
        crate.linearVelocity = Vector3.zero; crate.angularVelocity = Vector3.zero; Physics.SyncTransforms();
        for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
        var others = FindObjectsByType<Rigidbody>().Where(b => b != crate && !b.isKinematic && (b.position - crate.position).magnitude < 8f).Select(b => $"{b.name}{V(b.position)} v{b.linearVelocity.magnitude:0.00}").ToList();
        Log($"PHYSICS SETUP ({(pause ? "pause" : "control")}): other dynamic bodies within 8 m of the crate: {(others.Count == 0 ? "none" : string.Join("; ", others))}.");
        Refill(); var t = new Trial { Paused = pause }; int frame0 = W.Hero.LastImpactFrame; int started = TimeArbiter.HitPausesStarted;
        Vector3 start = crate.position;
        Check(crate.linearVelocity.magnitude < .02f, $"Crate at rest before the punch ({crate.linearVelocity.magnitude:0.000} m/s).");
        var watch = new System.Diagnostics.Stopwatch(); Action started0 = () => { watch.Start(); t.Rel = crate.position - D.LastImpactOrigin; }; W.Hero.PunchImpacted += started0;   // timed from the impact callback itself
        Check(W.Hero.TryPunch(), "Real punch (" + (pause ? "hit pause ON" : "CONTROL: hit pause 0 s on a CLONE of Feel") + ").");
        yield return Until(() => W.Hero.LastImpactFrame != frame0, 3f, "impact");
        W.Hero.PunchImpacted -= started0; t.Impulse = D.LastImpactImpulse;
        Check(TimeArbiter.HitPausesStarted == started + (pause ? 1 : 0), pause ? "Hit pause started at the impact." : "CONTROL: no hit pause (HitPauseSeconds 0 on the clone).");
        yield return new WaitForFixedUpdate(); t.RealToFirstStep = watch.Elapsed.TotalSeconds; t.V1 = crate.linearVelocity;
        for (int i = 0; i < 9; i++) yield return new WaitForFixedUpdate();
        t.V10 = crate.linearVelocity; t.P10 = crate.position - start;
        Log($"MEASURED {(pause ? "PAUSE  " : "CONTROL")}: crate - blast origin at impact {t.Rel.x:0.0000},{t.Rel.y:0.0000},{t.Rel.z:0.0000}; impulse {t.Impulse:0} N·s; first physics step {t.RealToFirstStep * 1000:0.0} ms (real) after the impact; v1 {V(t.V1)} |{t.V1.magnitude:0.000}| m/s; v10 {V(t.V10)} |{t.V10.magnitude:0.000}|; moved {V(t.P10)} after 10 steps.");
        trials.Add(t); D.Settings = shipped;
    }
    IEnumerator PhysicsThroughPause()
    {
        var trials = new List<Trial>();
        for (int i = 0; i < 4; i++) { yield return Punch(true, trials); yield return Punch(false, trials); }
        var p = trials.Where(t => t.Paused).ToList(); var c = trials.Where(t => !t.Paused).ToList();
        Check(p.All(t => t.RealToFirstStep >= shipped.HitPauseSeconds - .003f) && c.All(t => t.RealToFirstStep < shipped.HitPauseSeconds - .01f),
            $"The first physics step after the impact waited out the pause ({string.Join(", ", p.Select(t => (t.RealToFirstStep * 1000).ToString("0.0")))} ms) vs the control ({string.Join(", ", c.Select(t => (t.RealToFirstStep * 1000).ToString("0.0")))} ms).");
        // Compare only trials with the SAME impact geometry (crate relative to the blast origin within 2 mm): the punch
        // origin follows the idle hero's exact pose, so geometry can differ between trials; a pause/no-pause pair with
        // identical geometry isolates the hit pause.
        int pairs = 0;
        foreach (var pt in p)
        {
            var ct = c.FirstOrDefault(x => (x.Rel - pt.Rel).magnitude < .002f); if (ct == null) { Log($"NOTE pause trial geometry {V(pt.Rel)} has no control twin; not compared."); continue; }
            pairs++; float d1 = (pt.V1 - ct.V1).magnitude, d10 = (pt.V10 - ct.V10).magnitude, ratio = pt.V1.magnitude / Mathf.Max(1e-4f, ct.V1.magnitude);
            Check(ct.V1.magnitude > .5f && d1 <= .002f * ct.V1.magnitude + .001f && d10 <= .002f * ct.V10.magnitude + .001f,
                $"Pair {pairs} (same geometry, |Δrel| {(pt.Rel - ct.Rel).magnitude * 1000:0.00} mm): velocity after the pause == no-pause control (v1 |Δ| {d1:0.0000} m/s of {ct.V1.magnitude:0.000}; v10 |Δ| {d10:0.0000}; ratio {ratio:0.0000} -> impulse applied once, not lost, not doubled).");
        }
        Check(pairs >= 2, $"{pairs} pause/control pairs with identical geometry compared (of {p.Count} pause trials).");
        Check(trials.All(t => t.V1.magnitude > .5f), "Every trial's crate was really launched (impulse applied).");
    }

    // ---- 5. incoming hits: Rusher (light) -> no pause; Brute (heavy by data) -> pause + camera
    IEnumerator Incoming()
    {
        Clear(); Place(spot); yield return Grounded(); Heal();
        var roster = Resources.Load<EnemyRoster>("Enemies/EnemyRoster");
        foreach (var (archetype, heavy) in new[] { (Resources.Load<EnemyArchetype>("Enemies/Rusher"), false), (Resources.Load<EnemyArchetype>("Enemies/Brute"), true) })
        {
            yield return Realtime(shipped.HitPauseMinInterval + .1f); Heal();
            var npc = CityNpc.Spawn(W, spot + Vector3.forward * 3f, NpcRole.Criminal, archetype); npc.AlwaysAggro = true; spawned.Add(npc.gameObject);
            Check((npc.ContactDamage >= shipped.HeavyIncomingDamage) == heavy, $"{archetype.name} contact damage {npc.ContactDamage:0.#} {(heavy ? ">=" : "<")} Feel.HeavyIncomingDamage {shipped.HeavyIncomingDamage} -> {(heavy ? "HEAVY" : "light")} by data.");
            int started = TimeArbiter.HitPausesStarted, kicks = follow.ImpulsesAccepted, incoming = D.HeavyIncoming; float hp = W.Health;
            var scales = new List<float>();
            yield return Until(() => { scales.Add(Time.timeScale); return W.Health < hp; }, 15f, archetype.name + " hit landing on the player");
            yield return null; scales.Add(Time.timeScale);
            Log($"{archetype.name} hit landed: health {hp:0} -> {W.Health:0}.");
            if (heavy)
            {
                Check(TimeArbiter.HitPausesStarted == started + 1 && D.HeavyIncoming == incoming + 1 && follow.ImpulsesAccepted == kicks + 1, $"Brute slam landing on the player: hit pause + camera impulse ({D.LastEvent}).");
                var cc = W.Hero.GetComponent<CharacterController>(); var grounded = new List<bool>();
                for (int i = 0; i < 3 && TimeArbiter.HitPaused; i++) { yield return null; grounded.Add(cc.isGrounded); }
                bool stillPaused = TimeArbiter.HitPaused;
                Check(stillPaused && grounded.Count > 0 && grounded.All(g => g) && W.Hero.TryBackflip(), $"Inside the hit pause (knockback + idle frames frozen) the hero keeps ground contact [{string.Join(",", grounded)}] and a backflip input is ACCEPTED ({W.Hero.LastBackflipResult}) — the combat contract 'a shoved player can still backflip' holds through the freeze.");
                yield return Until(() => !TimeArbiter.HitPaused, 1f, "pause end"); yield return new WaitForSeconds(HeroAbilityTuning.BackflipSeconds);
            }
            else Check(TimeArbiter.HitPausesStarted == started && follow.ImpulsesAccepted == kicks && scales.All(s => s == 1f), $"CONTROL light hit (Rusher): no hit pause, no camera impulse; timeScale 1 on all {scales.Count} sampled frames.");
            npc.Damage(100000, null); yield return Until(() => !TimeArbiter.HitPaused, 1f, "pause end");
            Place(spot); yield return Grounded();
        }
        CalmNpcs(); Heal();
    }

    // ---- 6. camera impulse + FOV kick: bounded, decays to rest within the window; hard vs soft landing CONTROL
    IEnumerator CameraKick()
    {
        Clear(); Place(spot); yield return Grounded(); yield return Realtime(.3f);
        float landedSpeed = -1f; Action<float> onLanded = v => landedSpeed = v; W.Hero.Landed += onLanded;
        // soft landing CONTROL: a real jump lands well under the threshold
        int kicks = follow.ImpulsesAccepted, hard = D.HardLandings, soft = D.SoftLandings;
        Check(W.Hero.TryJump(), "Real jump (soft landing CONTROL).");
        yield return Until(() => landedSpeed >= 0f, 4f, "jump landing");
        Check(landedSpeed < shipped.HardLandingSpeed && D.SoftLandings > soft && D.HardLandings == hard && follow.ImpulsesAccepted == kicks && follow.CurrentOffset == Vector3.zero,
            $"CONTROL soft landing {landedSpeed:0.0} m/s < Feel.HardLandingSpeed {shipped.HardLandingSpeed}: no impulse, no FOV kick.");
        yield return Grounded();
        // hard landing: dropped from 12 m
        landedSpeed = -1f; Vector3 drop = spot + Vector3.up * 12f; Place(drop); Log("TEST HARNESS: hero placed 12 m above the street and released (a real fall; landing reported by SuperHeroController.Landed).");
        yield return Until(() => landedSpeed >= 0f, 6f, "hard landing");
        var offsets = new List<float>(); var fovs = new List<float>(); var watch = System.Diagnostics.Stopwatch.StartNew(); float end = Mathf.Max(shipped.ImpulseSeconds, shipped.FovKickSeconds);
        float kickedAt = Time.unscaledTime; offsets.Add(follow.CurrentOffset.magnitude); fovs.Add(follow.CurrentFovKick);
        while (Time.unscaledTime - kickedAt < end + .12f) { yield return null; offsets.Add(follow.CurrentOffset.magnitude); fovs.Add(follow.CurrentFovKick); }
        W.Hero.Landed -= onLanded;
        Log($"MEASURED hard landing {landedSpeed:0.0} m/s: |offset| samples [{string.Join(",", offsets.Select(o => o.ToString("0.000")))}] m; FOV kick samples [{string.Join(",", fovs.Select(o => o.ToString("0.00")))}] deg over {watch.Elapsed.TotalMilliseconds:0} ms.");
        float amp = shipped.ImpulseAmplitude * shipped.LandingScale, deg = shipped.FovKickDegrees * shipped.LandingScale;
        Check(landedSpeed >= shipped.HardLandingSpeed && D.HardLandings == hard + 1 && follow.ImpulsesAccepted == kicks + 1, $"Hard landing {landedSpeed:0.0} m/s >= {shipped.HardLandingSpeed}: one impulse + FOV kick (scaled x{shipped.LandingScale}).");
        Check(offsets.Max() > 0f && offsets.Max() <= amp + 1e-4f && fovs.Max() > 0f && fovs.Max() <= deg + 1e-4f, $"Kick present and bounded: max offset {offsets.Max():0.000} m (<= {amp:0.000}), max FOV kick {fovs.Max():0.00} deg (<= {deg:0.00}).");
        Check(follow.CurrentOffset == Vector3.zero && follow.CurrentFovKick == 0f && follow.ImpulseEndsAt <= Time.unscaledTime && follow.FovKickEndsAt <= Time.unscaledTime, $"Back to rest within {end * 1000:0} ms (impulse {shipped.ImpulseSeconds * 1000:0} ms, FOV {shipped.FovKickSeconds * 1000:0} ms).");
        Check(cam.fieldOfView == W.Tuning.Camera.FieldOfView, "Rest FOV exactly Camera.FieldOfView after the kick.");
    }

    // ---- 7. particles: fixed pool, shared cube mesh, CityMaterials palette instances, no new Material, no new systems
    IEnumerator ParticleChecks()
    {
        Clear(); Place(spot); yield return Grounded();
        var crates = new List<Rigidbody>(); for (int i = 0; i < 6; i++) crates.Add(Crate(spot + new Vector3(-7.5f + i * 3f, 0, 10f), "Feel particle crate " + i));
        yield return Realtime(.5f);
        var pool = D.Particles; int poolCount = pool.PoolCount; var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); Mesh builtin = cube.GetComponent<MeshFilter>().sharedMesh; Destroy(cube);
        int materials0 = Resources.FindObjectsOfTypeAll<Material>().Length, systems0 = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length;
        int bursts0 = pool.Bursts, emitted0 = pool.ParticlesEmitted; var s = W.Powers.Strength.Definition;
        for (int i = 0; i < 6; i++)
        {
            CombatImpact.Blast(W.Powers, crates[i].position + Vector3.back * .8f, s.Radius, s.Force * (i % 2 == 0 ? 1f : .2f), s.Damage, s.UpwardForce);   // heavy + light hits
            yield return Realtime(.05f);
        }
        foreach (var c in crates) if (c != null) c.GetComponent<BreakableProp>().TakeDamage(100000f, W.Powers);   // real breaks
        yield return Frames(3);
        int materials1 = Resources.FindObjectsOfTypeAll<Material>().Length, systems1 = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length;
        Log($"MEASURED particles: bursts {bursts0} -> {pool.Bursts}, particles emitted {emitted0} -> {pool.ParticlesEmitted}; pool {poolCount} -> {pool.PoolCount}; ParticleSystems in scene {systems0} -> {systems1}; Material objects {materials0} -> {materials1}.");
        Check(pool.Bursts - bursts0 == 12, $"12 bursts from real hits (6) + real breaks (6) ({pool.Bursts - bursts0}).");
        Check(pool.PoolCount == poolCount && poolCount == shipped.ParticlePoolSize && systems1 == systems0, $"Pool size constant ({poolCount} = Feel.ParticlePoolSize) and no ParticleSystem instantiated per hit ({systems0} -> {systems1}).");
        Check(materials1 == materials0, $"0 new Material objects created by the hits/breaks/particles ({materials0} -> {materials1}).");
        var palette = new HashSet<Material>(CityMaterials.Current.All);
        var roles = new[] { shipped.HitDebris, shipped.HeavyDebris, shipped.BreakDebris, shipped.LandingDebris }.Select(CityMaterials.Get).ToList();
        for (int i = 0; i < pool.PoolCount; i++)
        {
            var r = pool.SlotRenderer(i);
            Check(r.renderMode == ParticleSystemRenderMode.Mesh && ReferenceEquals(r.mesh, builtin) && ReferenceEquals(r.mesh, pool.SharedMesh) && roles.Any(m => ReferenceEquals(m, r.sharedMaterial)) && palette.Contains(r.sharedMaterial),
                $"Slot {i}: Mesh mode, SHARED built-in cube mesh (reference-equal), material '{r.sharedMaterial.name}' is a CityMaterials palette instance (reference-equal).");
        }
        int alive = Enumerable.Range(0, pool.PoolCount).Sum(i => pool.Slot(i).particleCount);
        Check(alive > 0, $"Debris particles alive right after the bursts: {alive}.");
        yield return Composite(new Vector2Int(1920, 1080), "particles-hits-and-breaks-1920x1080");
        Clear();
    }

    // ---- 8. FPS: heavy-hit stress with particles ON vs OFF, interleaved A/B/A/B, gameplay camera single render
    IEnumerator Fps()
    {
        Clear(); Place(spot); yield return Grounded(); yield return Realtime(.3f);
        var target = new RenderTexture(1920, 1080, 24) { name = "Feel FPS" }; target.Create();
        bool was = cam.enabled; cam.targetTexture = target; cam.enabled = false; hud.Panel.targetTexture = target; hud.OverlayPanel.targetTexture = target;
        var off = Clone(shipped); off.LightHitParticles = off.HeavyHitParticles = off.BreakParticles = off.LandingParticles = 0;
        var rows = new List<(string label, double fps, double mean, double p95, int maxAlive)>();
        Vector3 at = spot + Vector3.forward * 6f + Vector3.up * .8f;
        foreach (bool particles in new[] { true, false, true, false })
        {
            D.Settings = particles ? shipped : off;
            var warm = System.Diagnostics.Stopwatch.StartNew(); while (warm.Elapsed.TotalSeconds < 1) { cam.Render(); yield return null; }
            var watch = System.Diagnostics.Stopwatch.StartNew(); var frames = new List<double>(); double last = 0, nextHit = 0; int maxAlive = 0;
            while (watch.Elapsed.TotalSeconds < 4)
            {
                if (watch.Elapsed.TotalSeconds >= nextHit) { nextHit += .1f; FeelDirector.Impact(at + new Vector3(UnityEngine.Random.Range(-2f, 2f), 0, UnityEngine.Random.Range(-1f, 1f)), shipped.HeavyImpulse + 350f, 35f, 1); }
                cam.Render(); yield return null; double now = watch.Elapsed.TotalSeconds; frames.Add((now - last) * 1000); last = now;
                maxAlive = Mathf.Max(maxAlive, Enumerable.Range(0, D.Particles.PoolCount).Sum(i => D.Particles.Slot(i).particleCount));
            }
            frames.Sort(); string label = particles ? "A particles ON " : "B particles OFF";
            rows.Add((label, frames.Count / watch.Elapsed.TotalSeconds, frames.Average(), frames[(int)(frames.Count * .95)], maxAlive));
            var row = rows.Last();
            Log($"MEASURED {row.label}: frames={frames.Count}, FPS={row.fps:F1}, mean={row.mean:F2}ms, p95={row.p95:F2}ms, peak live debris={row.maxAlive} (heavy impact every 100 ms via the CombatImpact hook; hit pauses rate-limited; gameplay camera single render 1920x1080 + HUD).");
        }
        D.Settings = shipped;
        cam.targetTexture = null; cam.enabled = was; hud.Panel.targetTexture = null; hud.OverlayPanel.targetTexture = null; target.Release(); Destroy(target);
        double a = (rows[0].fps + rows[2].fps) * .5, b = (rows[1].fps + rows[3].fps) * .5;
        Log($"MEASURED particles A/B summary: ON mean {a:F1} FPS, OFF mean {b:F1} FPS ({(a - b) / b:+0.0%;-0.0%}); frame-time delta {1000 / a - 1000 / b:+0.00;-0.00} ms. Editor batch-mode throughput, not a player build.");
        Check(rows[0].maxAlive > 0 && rows[1].maxAlive == 0, "FPS A/B really toggled the debris (ON had live particles, OFF had none).");
        yield return Realtime(shipped.HitPauseMinInterval);
    }

    // ---- 9. aim accuracy with the new camera: Fire Blast at pillars under the crosshair; Ice/Telekinesis selection
    sealed class ShotProbe : MonoBehaviour
    {
        public bool Hit; public Vector3 Point; public Collider Other; public string OtherName;
        void OnCollisionEnter(Collision c) { if (Hit) return; Hit = true; Point = c.GetContact(0).point; Other = c.collider; OtherName = c.collider.name; }
    }
    sealed class RenderProbe : MonoBehaviour
    {
        public int Renders; public Vector3 Position; public float Fov;
        void OnPreRender() { Renders++; Position = transform.position; Fov = GetComponent<Camera>().fieldOfView; }
    }
    IEnumerator Loadout(string a, string b)
    {
        yield return Home();
        var profile = ui.Profile; PowerDefinition Power(string id) => Resources.Load<PowerDefinition>("Powers/" + id);
        var pa = Power(a); var pb = Power(b);
        profile.AddXp(3000); foreach (var p in new[] { pa, pb }) if (!profile.Owns(p)) Check(profile.Buy(p), "Existing progression unlock " + p.Id);
        var l = profile.Data.Loadout;
        Check(profile.SetLoadout(profile.SelectedHero, pa, pb, l.Primary, l.Secondary), $"PlayerProgression.SetLoadout -> {a} + {b} before the session.");
        yield return Sandbox($"{a}+{b} sandbox");
        Check(W.Powers.EquippedA == pa && W.Powers.EquippedB == pb, $"Session equips {a} + {b}.");
    }
    /// A tall static pillar `metres` ahead along the camera's flat forward; pitch is lowered until the crosshair ray hits
    /// the pillar between 0.6 and 2.4 m above the street (as a player would aim).
    IEnumerator Pillar(float metres, float yaw, List<GameObject> made, Action<Vector3, Collider> done)
    {
        Aim(yaw, W.Tuning.Camera.Pitch); yield return Frames(2);
        Vector3 flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube); pillar.name = $"Aim pillar {metres} m yaw {yaw}"; pillar.transform.localScale = new Vector3(1.2f, 6f, 1.2f);
        pillar.transform.position = W.Hero.transform.position + flat * metres + Vector3.up * 3f; pillar.transform.rotation = Quaternion.LookRotation(flat);
        pillar.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Red); spawned.Add(pillar); made.Add(pillar);
        Physics.SyncTransforms();
        var col = pillar.GetComponent<Collider>(); Vector3 point = default; bool ok = false;
        for (float pitch = W.Tuning.Camera.Pitch; pitch >= W.Tuning.Camera.MinimumPitch; pitch -= .5f)
        {
            Aim(yaw, pitch); yield return null;
            var ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            if (Crosshair(ray, out var hit) && hit.collider == col && hit.point.y - W.Hero.transform.position.y > .6f && hit.point.y - W.Hero.transform.position.y < 2.4f) { point = hit.point; ok = true; break; }
        }
        done(ok ? point : Vector3.positiveInfinity, col);
    }
    bool Crosshair(Ray ray, out RaycastHit best)
    {
        best = default; float d = float.PositiveInfinity;
        foreach (var h in Physics.RaycastAll(ray, 200f)) if (h.collider.transform.root != W.Hero.transform && h.distance < d) { d = h.distance; best = h; }
        return d < float.PositiveInfinity;
    }
    IEnumerator FireAt(string label, Vector3 crosshairPoint, Collider intended, List<string> table, bool capture)
    {
        var fire = W.Powers.Powers.First(p => p.Definition.Id == "fire");
        Energy(100); fire.Charges = W.Powers.Stats(fire).Charges; fire.Cooldown = 0f;
        Check(W.Powers.Select(fire), "Fire Blast selected.");
        Vector3 aimDir = W.Powers.AimDirection, origin = W.Powers.AimOrigin;
        string line = Physics.Linecast(origin, crosshairPoint, out var blocker) && blocker.collider != intended && blocker.collider.transform.root != W.Hero.transform ? $"shoulder line BLOCKED by '{blocker.collider.name}' (parallax)" : "shoulder line clear";
        float camDist = (crosshairPoint - cam.transform.position).magnitude, range = W.Powers.Stats(fire).Range;
        if (camDist > range) line += $"; crosshair point is {camDist:0.0} m from the CAMERA > Fire Range {range:0} (AimDirection clamps its crosshair raycast at Range from the camera, so it aims at the ray point {range:0} m out instead)";
        var before = new HashSet<PowerProjectile>(FindObjectsByType<PowerProjectile>());
        Check(W.Powers.Use(fire), $"{label}: real Fire Blast cast (PowerUser.Use): {W.Powers.Message}");
        var shot = FindObjectsByType<PowerProjectile>().First(p => !before.Contains(p));
        var probe = shot.gameObject.AddComponent<ShotProbe>(); var body = shot.GetComponent<Rigidbody>();
        RenderTexture target = null;
        if (capture) { target = new RenderTexture(1920, 1080, 24); target.Create(); hud.Panel.targetTexture = target; hud.OverlayPanel.targetTexture = target; cam.aspect = 16f / 9f; }
        bool captured = !capture; float until = Time.realtimeSinceStartup + 4f;
        while (!probe.Hit && shot != null && Time.realtimeSinceStartup < until)
        {
            if (!captured && shot != null && (shot.transform.position - crosshairPoint).magnitude < 3.2f) { captured = true; yield return CaptureNow(target, "aim-" + label + "-in-flight"); continue; }
            yield return new WaitForFixedUpdate();
        }
        if (capture) { yield return Frames(2); yield return CaptureNow(target, "aim-" + label + "-impact"); cam.ResetAspect(); hud.Panel.targetTexture = null; hud.OverlayPanel.targetTexture = null; target.Release(); Destroy(target); }
        float miss = probe.Hit ? (probe.Point - crosshairPoint).magnitude : float.PositiveInfinity;
        Vector3 toPoint = crosshairPoint - cam.transform.position; float lateral = probe.Hit ? Vector3.Cross(toPoint.normalized, probe.Point - cam.transform.position).magnitude : float.PositiveInfinity;
        string hitName = probe.Hit ? probe.OtherName : "nothing";
        string row = $"{label}: crosshair hit {V(crosshairPoint)} on '{intended.name}'; shoulder {V(origin)} dir {V(aimDir)}; projectile contact {(probe.Hit ? V(probe.Point) : "none")} on '{hitName}'; MISS {miss:0.000} m (off the crosshair ray by {lateral:0.000} m); {line}";
        table.Add(row); Log("AIM " + row);
        if (probe != null) Destroy(probe);
        yield return Realtime(.3f);
    }
    IEnumerator AimFireIce()
    {
        yield return Loadout("fire", "ice");
        CalmNpcs(); Place(spot); yield return Grounded(); Aim(0f, W.Tuning.Camera.Pitch); yield return Frames(3);
        yield return Composite(new Vector2Int(1920, 1080), "camera-framing-rest-1920x1080");
        Vector3 heroScreen = cam.WorldToViewportPoint(W.Hero.transform.position + Vector3.up * W.Tuning.Movement.Height);
        Log($"FRAMING: camera {V(cam.transform.position)} for hero {V(W.Hero.transform.position)} (height {cam.transform.position.y - W.Hero.transform.position.y:0.00} m, {Vector3.ProjectOnPlane(W.Hero.transform.position - cam.transform.position, Vector3.up).magnitude:0.00} m behind); look-down {Vector3.Angle(Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up), cam.transform.forward):0.0} deg; hero head at viewport y {heroScreen.y:0.000} (crosshair 0.5).");
        Check(heroScreen.y < .5f, $"Crosshair no longer on the hero: the top of the hero's head projects at viewport y {heroScreen.y:0.000}, below the crosshair (0.5).");
        var ray0 = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0));
        if (Crosshair(ray0, out var ground0)) Log($"FRAMING: at rest the crosshair ray meets '{ground0.collider.name}' {Vector3.ProjectOnPlane(ground0.point - W.Hero.transform.position, Vector3.up).magnitude:0.0} m ahead of the hero.");
        var table = new List<string>();
        foreach (var (metres, yaw, capture) in new[] { (5f, 0f, true), (15f, 0f, true), (30f, 0f, true), (15f, 30f, true), (15f, -30f, false), (15f, 20f, false), (8f, 20f, false), (22f, 0f, false) })
        {
            Clear(); Place(spot); yield return Grounded();
            var made = new List<GameObject>(); Vector3 point = Vector3.positiveInfinity; Collider col = null;
            yield return Pillar(metres, yaw, made, (p, c) => { point = p; col = c; });
            if (float.IsInfinity(point.x)) { Log($"AIM {metres} m yaw {yaw}: could not bring the crosshair onto the pillar (blocked by city geometry) — skipped."); continue; }
            yield return FireAt($"{metres:0}m-yaw{yaw:0}", point, col, table, capture);
        }
        Log("AIM TABLE (Fire Blast, projectile contact vs crosshair raycast hit):\n  " + string.Join("\n  ", table));
        // Parallax: an obstacle between the shoulder and the crosshair point that the camera ray passes over.
        {
            Clear(); Place(spot); yield return Grounded();
            var made = new List<GameObject>(); Vector3 point = Vector3.positiveInfinity; Collider col = null;
            yield return Pillar(15f, 0f, made, (p, c) => { point = p; col = c; });
            Check(!float.IsInfinity(point.x), "Parallax setup: crosshair on a pillar 15 m ahead.");
            Vector3 shoulder = W.Hero.transform.position + Vector3.up * W.Powers.Powers.First(p => p.Definition.Id == "fire").Definition.OriginHeight;
            Vector3 mid = Vector3.Lerp(shoulder, point, .35f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Parallax obstacle"; wall.transform.localScale = new Vector3(2.5f, 1f, .4f);
            wall.transform.position = new Vector3(mid.x, mid.y, mid.z); wall.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(CityColor.Metal); spawned.Add(wall); Physics.SyncTransforms();
            var ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0)); Crosshair(ray, out var still);
            bool shoulderBlocked = Physics.Linecast(shoulder, point, out var block) && block.collider.gameObject == wall;
            Log($"PARALLAX: obstacle {V(wall.transform.position)} ({wall.transform.localScale.y} m tall box) between the shoulder {V(shoulder)} and the crosshair point {V(point)}; camera ray still hits '{still.collider.name}'; shoulder line blocked by it: {shoulderBlocked}.");
            Check(still.collider == col && shoulderBlocked, "Parallax case built: the crosshair shows the pillar, the shoulder's line is blocked.");
            yield return FireAt("parallax-15m", point, col, table, true);
            Log("PARALLAX RESULT: " + table.Last());
            var ice = W.Powers.Powers.First(p => p.Definition.Id == "ice");
            bool found = W.Powers.FindTarget(W.Powers.Stats(ice).Range, out var iceHit);
            Log($"PARALLAX Ice/Telekinesis targeting (PowerUser.FindTarget, shoulder ray): {(found ? "'" + iceHit.collider.name + "' at " + V(iceHit.point) : "nothing")} — the crosshair shows '{col.name}'.");
        }
        // Ice selects the crate under the crosshair, not the decoy beside it.
        {
            Clear(); Place(spot); yield return Grounded(); Aim(0f, W.Tuning.Camera.Pitch); yield return Frames(2);
            var ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0)); Check(Crosshair(ray, out var g), "Crosshair meets the street.");
            Vector3 ground = new Vector3(g.point.x, spot.y, g.point.z);
            var target = Crate(ground, "Ice target crate (under crosshair)"); var decoy = Crate(ground + cam.transform.right * 2.6f, "Ice decoy crate");
            yield return Realtime(.5f);
            ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0)); Crosshair(ray, out var onTarget);
            Check(onTarget.rigidbody == target, $"Crosshair now on '{onTarget.collider.name}'.");
            var ice = W.Powers.Powers.First(p => p.Definition.Id == "ice"); Energy(100); ice.Cooldown = 0; ice.Charges = W.Powers.Stats(ice).Charges;
            Check(W.Powers.Select(ice) && W.Powers.Use(ice), "Real Ice cast: " + W.Powers.Message);
            Check(target.GetComponent<FrozenBody>() != null && decoy.GetComponent<FrozenBody>() == null, "Ice froze the crate under the crosshair and not the decoy 2.6 m beside it.");
            yield return Composite(new Vector2Int(1920, 1080), "aim-ice-crosshair-target-1920x1080");
        }
    }
    /// TEST HARNESS: turn the follow camera (yaw/pitch, as the mouse would) until the crosshair ray meets `body`.
    IEnumerator AimAtBody(Rigidbody body, Action<bool> done)
    {
        for (float pitch = W.Tuning.Camera.Pitch; pitch >= W.Tuning.Camera.MinimumPitch; pitch -= 1f)
            for (float yaw = -30f; yaw <= 30f; yaw += .5f)
            {
                Aim(yaw, pitch); yield return null;
                if (Crosshair(cam.ViewportPointToRay(new Vector3(.5f, .5f, 0)), out var h) && h.rigidbody == body) { Log($"TEST HARNESS: camera yaw {yaw:0.0} pitch {pitch:0.0} puts the crosshair on '{body.name}'."); done(true); yield break; }
            }
        done(false);
    }
    IEnumerator AimTelekinesis()
    {
        yield return Loadout("telekinesis", "fire");
        CalmNpcs(); Clear(); Place(spot); yield return Grounded();
        Vector3 ahead = spot + Vector3.forward * 9f;
        var a = Crate(ahead - Vector3.right * 1.5f, "TK crate A"); var b = Crate(ahead + Vector3.right * 1.5f, "TK crate B");
        yield return Realtime(.6f);
        foreach (var (want, other) in new[] { (a, b), (b, a) })
        {
            bool on = false; yield return AimAtBody(want, v => on = v);
            Check(on, $"Crosshair brought onto '{want.name}' (3 m from '{other.name}').");
            var tk = W.Powers.Powers.First(p => p.Definition.Id == "telekinesis"); Energy(100); tk.Cooldown = 0; tk.Charges = W.Powers.Stats(tk).Charges;
            Check(W.Powers.Select(tk) && W.Powers.Use(tk), "Real Telekinesis cast: " + W.Powers.Message);
            Check(W.Powers.HeldBody == want, $"Telekinesis grabbed '{W.Powers.HeldBody?.name}' — the crate under the crosshair, not '{other.name}'{(want == b ? " (CONTROL: aim moved to the other crate, selection followed)" : "")}.");
            if (want == a) yield return Composite(new Vector2Int(1920, 1080), "aim-telekinesis-crosshair-target-1920x1080");
            W.Powers.Release(false); yield return Realtime(.3f);
            var body = want; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; yield return Realtime(.3f);
        }
    }
}
#endif
