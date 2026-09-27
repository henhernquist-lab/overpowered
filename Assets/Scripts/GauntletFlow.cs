#if UNITY_EDITOR
// GAUNTLET FLOW HELPERS — drives the REAL GameFlow scene transitions from gauntlet verifiers.
// Never changes shipping flow code; it only calls the same public entry points the menus use.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GauntletFlow
{
    /// Loads Home through GameFlow and waits until ModeScreens exists (the menu finished building).
    public static IEnumerator OpenHome(float timeoutSeconds = 40f)
    {
        GameFlow.Instance.Home();
        float until = Time.realtimeSinceStartup + timeoutSeconds;
        while (GameFlow.Instance == null || GameFlow.Instance.Loading
            || SceneManager.GetActiveScene().name != GameFlow.HomeScene
            || UnityEngine.Object.FindAnyObjectByType<ModeScreens>() == null)
        {
            if (Time.realtimeSinceStartup > until) throw new TimeoutException("OpenHome timed out: loading=" + (GameFlow.Instance != null && GameFlow.Instance.Loading) + " scene=" + SceneManager.GetActiveScene().name);
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.3f);
    }

    /// Waits for the city scene and a live WorldSession (after GameFlow.Instance.Select(...) already returned true).
    public static IEnumerator AwaitCity(float timeoutSeconds = 40f)
    {
        float until = Time.realtimeSinceStartup + timeoutSeconds;
        while (GameFlow.Instance == null || GameFlow.Instance.Loading
            || SceneManager.GetActiveScene().name != GameFlow.CityScene
            || WorldSession.Instance == null)
        {
            if (Time.realtimeSinceStartup > until) throw new TimeoutException("AwaitCity timed out: loading=" + (GameFlow.Instance != null && GameFlow.Instance.Loading));
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.5f);
    }

    /// Waits until the Results scene is active and its screen built (a session ended via the shipping path).
    public static IEnumerator AwaitResults(float timeoutSeconds = 40f)
    {
        float until = Time.realtimeSinceStartup + timeoutSeconds;
        while (GameFlow.Instance == null || GameFlow.Instance.Loading
            || SceneManager.GetActiveScene().name != GameFlow.ResultsScene
            || UnityEngine.Object.FindAnyObjectByType<ModeScreens>() == null)
        {
            if (Time.realtimeSinceStartup > until) throw new TimeoutException("AwaitResults timed out: loading=" + (GameFlow.Instance != null && GameFlow.Instance.Loading));
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.3f);
    }
}
#endif
