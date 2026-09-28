#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// See PursuitVerification. The pursuit state machine on the isolated floor with a real (AI-off) police NPC: crime ->
/// Alerted, contact -> Pursued and held while engaged (also at 30 m), a wall -> Searching, reacquired -> Pursued, time +
/// distance -> Escaped -> Alerted, session end -> Clear; and a Hero session with Heat and a nearby cop never leaves Clear.
public sealed class PursuitVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/Pursuit/";
    protected override string ResultFile => "results.txt";
    PursuitTracker P => W.Pursuit;
    readonly List<string> changes = new List<string>();
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return HeroControl();
        yield return Villain();
    }
    IEnumerator HoldFor(float seconds) { float until = Time.time + seconds; while (Time.time < until) { P.Sample(); yield return null; } }
    IEnumerator HeroControl()
    {
        Log("---- HERO: never pursued");
        yield return Enter(F.Heroes[0], "ice", "strength", "hero");
        Isolate(); W.AddHeat(3f);
        var cop = Actor(W.Hero.transform.position + new Vector3(0, 0, 8), NpcRole.Cop, 1000f);
        yield return HoldFor(1f);
        Check(!P.SideCanBePursued && !cop.Hostile && P.State == PursuitState.Clear && P.Contacts == 0, $"Hero with {W.Stars} Heat stars and a cop 8 m away in view: state {P.State}, police hostile {cop.Hostile}, side can be pursued {P.SideCanBePursued}.");
        Check(W.Tuning.Heat.Police(PlayerSide.Hero).HostileFromStars > W.Tuning.Heat.MaximumStars && !W.Tuning.Heat.Police(PlayerSide.Hero).RespondersHostile, "Hero police settings unchanged (never hostile).");
    }
    IEnumerator Villain()
    {
        Log("---- VILLAIN");
        yield return Enter(F.Heroes[0], "fire", "ice", "villain");
        Isolate(); P.StateChanged += (a, b) => changes.Add($"{a}->{b}");
        W.AddHeat(-W.Heat); P.Sample();
        Check(P.State == PursuitState.Clear && P.SideCanBePursued, "Villain at 0 Heat: Clear.");
        W.OnDestruction(W.Hero.transform.position); P.Sample();
        Check(W.Heat > 0f && P.State == PursuitState.Alerted, $"A crime (destruction, Heat {W.Heat:F2}) with no police in view: Alerted.");
        Vector3 hero = W.Hero.transform.position;
        var cop = Actor(hero + new Vector3(0, 0, 10), NpcRole.Cop, 1000f); yield return null; P.Sample();
        Check(cop.Hostile && P.State == PursuitState.Pursued && P.ContactCount == 1, "A hostile cop 10 m away with a clear line: Pursued.");
        yield return HoldFor(4f);
        Check(P.State == PursuitState.Pursued, "Staying engaged 4 s keeps Pursued.");
        cop.transform.position = hero + new Vector3(0, 0, 30); Physics.SyncTransforms();
        yield return HoldFor(4f);
        Check(P.State == PursuitState.Pursued, "CONTROL: 30 m away with the cop still in view is NOT an escape (still Pursued).");
        var wall = Wall(hero + new Vector3(0, 1.5f, 15), new Vector3(12, 4, 1));
        yield return HoldFor(P.Settings.LoseContactSeconds + .5f);
        Check(P.State == PursuitState.Searching && P.ContactCount == 0, $"A wall breaks the line: Searching after {P.Settings.LoseContactSeconds} s.");
        Destroy(wall); yield return null; Physics.SyncTransforms(); P.Sample();
        Check(P.State == PursuitState.Pursued && P.Reacquired >= 1, "Wall gone: reacquired -> Pursued.");
        cop.gameObject.SetActive(false);
        PlaceHero(hero + new Vector3(0, 0, -P.Settings.EscapeDistance - 5f));
        yield return HoldFor(P.Settings.LoseContactSeconds + .3f);
        Check(P.State == PursuitState.Searching, "Out of sight and far away: Searching (not yet escaped).");
        float until = Time.time + P.Settings.SearchSeconds + 1f; while (P.State != PursuitState.Escaped && Time.time < until) { P.Sample(); yield return null; }
        Check(P.State == PursuitState.Escaped && P.Escapes == 1, $"{P.Settings.SearchSeconds} s without contact and {P.Settings.EscapeDistance}+ m from the lost contact: Escaped.");
        yield return HoldFor(P.Settings.EscapedHoldSeconds + .3f);
        Check(P.State == (W.Heat > 0f ? PursuitState.Alerted : PursuitState.Clear), $"After the hold: {P.State} (Heat {W.Heat:F2}).");
        cop.gameObject.SetActive(true); cop.transform.position = W.Hero.transform.position + new Vector3(0, 0, 8); Physics.SyncTransforms(); P.Sample();
        Check(P.State == PursuitState.Pursued, "Seen again: Pursued.");
        Log("Transitions: " + string.Join(", ", changes));
        W.Mode.EndToResults();
        Check(P.State == PursuitState.Clear, "Session end clears the pursuit state.");
    }
}
#endif
