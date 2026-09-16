using System.Collections;
using UnityEngine;

public sealed class VerificationHarness : MonoBehaviour
{
    SuperHeroController hero;
    void Update() { if (Input.GetKeyDown(KeyCode.V)) StartCoroutine(Run()); }
    IEnumerator Run()
    {
        hero = Object.FindFirstObjectByType<SuperHeroController>();
        Debug.Log("[VERIFY] START — configured flight duration=6.000s, recharge=2.500/s");
        hero.DebugSetResources(6f, 3); Debug.Log($"[VERIFY] Flight sample start: {hero.FlightFuel:0.000}s");
        hero.DebugSimulateFlight(2f); Debug.Log($"[VERIFY] Flight sample after 2.000s held: {hero.FlightFuel:0.000}s");
        hero.DebugSimulateFlight(4f); Debug.Log($"[VERIFY] Flight sample after 6.000s held: {hero.FlightFuel:0.000}s (empty)");
        hero.DebugSimulateGround(1f); Debug.Log($"[VERIFY] Ground recharge after 1.000s: {hero.FlightFuel:0.000}s");
        hero.transform.position = new Vector3(1f, 1.1f, -3f); hero.transform.forward = Vector3.forward;
        hero.DebugSetResources(6f, 3); bool one=hero.TryPunch(); hero.DebugSetResources(6f, 2); bool two=hero.TryPunch(); hero.DebugSetResources(6f, 1); bool three=hero.TryPunch(); hero.DebugSetResources(6f, 0); bool zero=hero.TryPunch();
        Debug.Log($"[VERIFY] Punches: 3 charged fires={one}/{two}/{three}; zero charges fires={zero} ({hero.LastPunchResult})");
        hero.DebugSetResources(6f, 1, .45f); bool cooling=hero.TryPunch(); Debug.Log($"[VERIFY] Cooldown attempt fires={cooling} ({hero.LastPunchResult})");
        hero.DebugSetResources(6f, 1); bool recovered=hero.TryPunch(); Debug.Log($"[VERIFY] Recovered charge fires={recovered}; real force={hero.LastForce:0} N; bodies affected={hero.LastAffectedBodies}");
        yield return null;
    }
}
