using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Run with -batchmode -executeMethod ProceduralAnimationVerification.Run (without -quit).
/// Enters real Play Mode; samples presentation deterministically, then observes a real fall.
[InitializeOnLoad]
public static class ProceduralAnimationVerification
{
    const string SessionKey = "Overpowered.AnimationVerification";
    static readonly List<string> results = new List<string>();
    static SuperHeroController hero;
    static ProceduralHeroAnimation animation;
    static Transform visuals, shoulder;
    static ProceduralAnimationTuning tuning;
    static float dropStarted;
    static double deadline;
    static int stage, landingEvents;
    static float impactSpeed, minimumScale = 1f;
    static string outputDirectory;

    static ProceduralAnimationVerification() { EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity");
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (stage == 0)
            {
                hero = UnityEngine.Object.FindAnyObjectByType<SuperHeroController>();
                if (hero == null) return;
                stage = 1;
                deadline = EditorApplication.timeSinceStartup + 30;
                outputDirectory = Path.Combine(Application.dataPath, "../Verification/Animation");
                Directory.CreateDirectory(outputDirectory);
                animation = hero.GetComponent<ProceduralHeroAnimation>();
                visuals = hero.transform.Find("Hero Visuals (presentation only)");
                shoulder = visuals.Find("Punch Shoulder");
                tuning = Resources.Load<ProceduralAnimationTuning>("ProceduralAnimationTuning");
                Check(tuning != null && animation != null, "Serialized tuning asset and visual component loaded.");
                hero.enabled = false;
                TestPosesAndPunch();
                // Place the actual controller over the arena and let Update/CharacterController.Move land it.
                animation.enabled = true;
                CharacterController cc = hero.GetComponent<CharacterController>();
                cc.enabled = false;
                hero.transform.position = new Vector3(12f, 5f, -10f);
                cc.enabled = true;
                hero.Landed += OnLanding;
                hero.enabled = true;
                dropStarted = Time.time;
                return;
            }
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Real fall did not finish within 30 seconds.");
            minimumScale = Mathf.Min(minimumScale, visuals.localScale.y);
            if (Time.time - dropStarted < 2f || landingEvents == 0) return;
            Check(landingEvents == 1, $"Real 5m fall: landing event count={landingEvents}; impact={impactSpeed:F3} m/s.");
            Check(minimumScale < .95f, $"Actual landing squash observed: minimum visual Y scale={minimumScale:F3}.");
            Check(Mathf.Abs(visuals.localScale.y - 1f) < .001f, "Actual landing recovered to scale 1.000.");
            Check(hero.GetComponent<CharacterController>().height == 1.8f, "Controller collider height remains 1.800m.");
            Finish(0);
        }
        catch (Exception e)
        {
            results.Add("FAIL: " + e);
            Debug.LogException(e);
            Finish(1);
        }
    }
    static void OnLanding(float speed) { landingEvents++; impactSpeed = speed; }
    static void TestPosesAndPunch()
    {
        Vector3 rootPosition = hero.transform.position;
        Quaternion rootRotation = hero.transform.rotation;
        var idle = new HeroPresentationState(Vector3.zero, true, false);
        int punchEvents = 0;
        hero.PunchStarted += () => punchEvents++;
        Capture("01-idle");
        Check(hero.TryPunch(), "Normal charged punch accepted.");
        animation.Advance(idle, tuning.PunchAttackSeconds);
        Check(Quaternion.Angle(shoulder.localRotation, Quaternion.Euler(tuning.PunchArmDegrees, 0f, 0f)) < .01f,
            $"Punch at {tuning.PunchAttackSeconds:F3}s: arm rotation from rest={Quaternion.Angle(Quaternion.identity, shoulder.localRotation):F3} degrees (configured X={tuning.PunchArmDegrees:F3}).");
        Capture("02-punch");
        Check(!hero.TryPunch() && punchEvents == 1, "Cooldown rejection emits no new animation event.");
        animation.Advance(idle, tuning.PunchRecoverSeconds + .01f);
        Check(Quaternion.Angle(shoulder.localRotation, Quaternion.identity) < .01f, "Punch eased back to rest.");
        hero.DebugSetResources(hero.FlightFuel, 0);
        Check(!hero.TryPunch() && punchEvents == 1, "Zero-charge rejection emits no new animation event.");

        animation.Advance(new HeroPresentationState(Vector3.forward * 12, false, true), 1f);
        float flightPitch = Mathf.DeltaAngle(0, visuals.localEulerAngles.x);
        Check(Mathf.Abs(flightPitch - tuning.FlightPitchDegrees) < .01f, $"Moving flight: body pitch={flightPitch:F3} degrees.");
        Capture("03-flight");
        animation.Advance(new HeroPresentationState(Vector3.up * 5, false, true), 1f);
        Check(Quaternion.Angle(visuals.localRotation, Quaternion.identity) < .01f, "Hover / vertical ascent levels body to 0 degrees.");
        Capture("04-hover");
        var run = new HeroPresentationState(new Vector3(6.364f, 0, 6.364f), true, false);
        float minBob = float.MaxValue, maxBob = float.MinValue;
        for (int i = 0; i < 120; i++)
        {
            animation.Advance(run, 1f / 120f);
            minBob = Mathf.Min(minBob, visuals.localPosition.y);
            maxBob = Mathf.Max(maxBob, visuals.localPosition.y);
        }
        Check(visuals.localEulerAngles.x > 1f && Mathf.DeltaAngle(0, visuals.localEulerAngles.z) < -1f,
            $"Diagonal run: pitch={Mathf.DeltaAngle(0, visuals.localEulerAngles.x):F3}, roll={Mathf.DeltaAngle(0, visuals.localEulerAngles.z):F3} degrees.");
        Check(maxBob - minBob > .02f && maxBob <= tuning.BobHeight + .001f, $"Run bob sampled over 120 steps: {minBob:F4}–{maxBob:F4} m.");
        Capture("05-run");
        animation.Advance(idle, 1f);
        Check(visuals.localPosition.y < .001f, "Idle control: bob fades to zero.");
        animation.PlayLanding(tuning.LandMinimumImpactSpeed * .5f);
        animation.Advance(idle, tuning.LandCompressSeconds);
        Check(visuals.localScale == Vector3.one, "Small contact control: no landing squash.");
        animation.PlayLanding(tuning.LandFullImpactSpeed);
        animation.Advance(idle, tuning.LandCompressSeconds);
        Check(Mathf.Abs(visuals.localScale.y - (1f - tuning.LandSquash)) < .001f,
            $"Full impact: scale={visuals.localScale:F3}; backward tilt={Mathf.DeltaAngle(0, visuals.localEulerAngles.x):F3} degrees.");
        Capture("06-landing");
        animation.Advance(idle, tuning.LandRecoverSeconds + .01f);
        Check(visuals.localScale == Vector3.one, "Landing fully recovered.");
        Check(hero.transform.position == rootPosition && hero.transform.rotation == rootRotation && hero.transform.localScale == Vector3.one,
            "All sampled poses left the physics root position/rotation/scale untouched.");
        animation.PlayPunch();
        animation.Advance(idle, tuning.PunchAttackSeconds);
        animation.enabled = false;
        Check(visuals.localScale == Vector3.one && visuals.localRotation == Quaternion.identity && shoulder.localRotation == Quaternion.identity,
            "Disabling presentation restores bind pose for replacement by an Animator adapter.");
    }
    static void Capture(string name)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var cameraObject = new GameObject("Verification camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = hero.transform.position + new Vector3(3f, 2f, 4.5f);
        camera.transform.LookAt(hero.transform.position + Vector3.up);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.16f, .18f, .22f);
        camera.fieldOfView = 32f;
        var lightObject = new GameObject("Verification light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.5f;
        light.transform.rotation = Quaternion.Euler(35f, -140f, 0f);
        var target = new RenderTexture(512, 512, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(512, 512, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(outputDirectory, name + ".png"), texture.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        target.Release();
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);
        UnityEngine.Object.DestroyImmediate(lightObject);
    }
    static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(message);
        results.Add("PASS: " + message);
        Debug.Log("[ANIMATION VERIFY] " + message);
    }
    static void Finish(int code)
    {
        SessionState.SetBool(SessionKey, false);
        if (outputDirectory != null) File.WriteAllLines(Path.Combine(outputDirectory, "results.txt"), results);
        EditorApplication.Exit(code);
    }
}
