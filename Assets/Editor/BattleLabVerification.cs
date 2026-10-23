#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// Home-route verification for the Combat Lab ("BATTLE LAB").
///   Unity -batchmode -projectPath <copy> -executeMethod BattleLabVerification.Run
/// Positive controls: the Home chip exists, is labelled, shares the chip row, a real UI click routes through GameFlow,
/// the lab builds a practice session with an arena (no generated city), spawns configured archetypes, honours its cap,
/// clears, restores health/energy, and returning Home destroys it.
/// Negative controls: a lab defeat pays no XP and raises no Heat, the practice profile has no save path, and the personal
/// save file's bytes are unchanged.
public static class BattleLabVerification
{
    const string Key = "Overpowered.BattleLabVerification";
    static double deadline;
    static BattleLabVerification()
    {
        EditorApplication.update += () =>
        {
            if (deadline > 0 && EditorApplication.timeSinceStartup > deadline)
            {
                Debug.LogError("Battle lab verification timeout");
                Finish(1);
            }
        };
    }
    public static void Run() => Begin();
    static void Begin()
    {
        Directory.CreateDirectory("Verification/BattleLab/saves");
        SessionState.SetString(Key, "run");
        EditorSceneManager.OpenScene("Assets/Scenes/Home.unity");
        EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (SessionState.GetString(Key, "") == "") return;
        EditorApplication.LockReloadAssemblies();
        deadline = EditorApplication.timeSinceStartup + 600;
        WorldSession.VerificationSavePath = Path.GetFullPath("Verification/BattleLab/saves/practice-" + Guid.NewGuid().ToString("N") + ".json");
        GameFlow.VerificationSandbox = false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (SessionState.GetString(Key, "") == "") return;
        var go = new GameObject("Battle lab verifier");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<BattleLabVerificationRunner>().Finished = Finish;
    }
    static void Finish(int code)
    {
        SessionState.EraseString(Key);
        deadline = 0;
        EditorApplication.UnlockReloadAssemblies();
        EditorApplication.Exit(code);
    }
}

public sealed class BattleLabVerificationRunner : SessionVerificationRunner
{
    protected override string Folder => "Verification/BattleLab/";
    protected override string ResultFile => "results.txt";

    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var home = FindAnyObjectByType<ModeScreens>();
        Check(home != null, "Home menu is present.");
        Check(home.ModeButtons.ContainsKey("battle-lab"), "Home exposes a BATTLE LAB route beside the data-defined mode chips.");

        var chip = home.ModeButtons["battle-lab"];
        var label = chip.Q<Label>();
        Check(label != null && label.text == "BATTLE LAB", $"Chip is labelled '{label?.text}'.");
        Check(chip.enabledSelf, "BATTLE LAB chip is enabled (not a COMING SOON placeholder).");
        Check(home.ModeButtons.ContainsKey("free-play") && Mathf.Abs(chip.layout.height - home.ModeButtons["free-play"].layout.height) < .5f,
            $"BATTLE LAB uses the chip row height ({chip.layout.height:0.#} px) like FREE PLAY ({home.ModeButtons["free-play"].layout.height:0.#} px).");
        var heroCard = home.ModeButtons.ContainsKey("hero") ? home.ModeButtons["hero"] : null;
        if (heroCard != null) Check(!chip.worldBound.Overlaps(heroCard.worldBound), "BATTLE LAB chip never overlaps the HERO MODE card.");

        // Personal save is the control surface: practice must never write it.
        string personalSave = Path.Combine(Application.persistentDataPath, Resources.Load<GameTuning>("GameTuning").Progression.SaveFilename);
        bool existedBefore = File.Exists(personalSave); string hashBefore = existedBefore ? Hash(personalSave) : null;
        Log($"personal save: {personalSave} exists={existedBefore}");

        // Snapshot what Home reads from the saved profile: the lab scene has no Home menu to ask afterwards.
        int homeLevel = home.Profile.Data.Level, homePoints = home.Profile.Data.Points;
        var homeSide = home.Profile.Data.Side;

        // The chip's own click handler, then the scene assertion below proves the route it runs.
        yield return Click(chip);
        float until = Time.realtimeSinceStartup + 60f;
        while (CombatLab.Active == null && Time.realtimeSinceStartup < until) yield return null;
        Check(CombatLab.Active != null, "Clicking BATTLE LAB loads the Combat Lab through GameFlow.");
        Check(SceneManager.GetActiveScene().name == CombatLab.SceneName, $"Active scene is {SceneManager.GetActiveScene().name}.");

        var lab = CombatLab.Active; var w = lab.World;
        Check(w != null && w.IsPractice, "Lab builds a practice WorldSession.");
        Check(WorldSession.Instance == w, "Practice session is the active WorldSession.");
        Check(w.Mode == null, "CONTROL: no game mode session is created in the lab.");
        Check(w.City != null && w.City.Sidewalks.Count == 1, $"CONTROL: the arena holds 1 metadata sidewalk, not a generated city ({w.City?.Sidewalks.Count}).");
        Check(w.Districts == null, "CONTROL: no district context (Heat / police / crimes) exists in the lab.");
        Check(w.Hero != null && w.Hero.GetComponent<CharacterController>() != null, "Practice hero has a CharacterController.");
        Check(lab.Settings != null && lab.Settings.Archetypes.Length == 5, $"Home route loads the full five-archetype lab ({lab.Settings?.Archetypes.Length}).");
        foreach (string name in new[] { "Skirmisher", "Bulwark" })
        {
            int index = Array.FindIndex(lab.Settings.Archetypes, a => a != null && a.name == name);
            Check(index >= 0, "Home Battle Lab registers " + name);
            lab.Select(index); var enemy = lab.SpawnOne();
            Check(enemy != null && enemy.Archetype.name == name && !enemy.Dead, "Home Battle Lab spawns live " + name);
            enemy.Damage(1000000f, w.Powers);
            Check(enemy.Dead, "Home Battle Lab defeats " + name + " through shared damage");
            lab.Clear(); yield return null;
        }
        lab.Select(0);
        yield return new WaitForSeconds(.2f);

        var first = lab.SpawnOne();
        Check(first != null && lab.Owned.Count == 1 && !first.Dead, $"Spawn one creates an owned lab enemy ({first?.Archetype?.DisplayName}).");
        Check(first.AlwaysAggro && first.Archetype != null && Array.IndexOf(lab.Settings.Archetypes, first.Archetype) >= 0,
            "Spawned enemy uses a configured archetype and always aggroes.");

        lab.SpawnGroup();
        int cap = Mathf.Clamp(lab.Settings.MaxEnemies, 1, 24);
        Check(lab.Owned.Count <= cap, $"Group spawn respects the cap ({lab.Owned.Count}/{cap}).");
        // Positive control for the cap under pressure: keep asking, never exceed it.
        for (int i = 0; i < cap + 3; i++) lab.SpawnOne();
        Check(lab.ActiveCount <= cap && lab.Owned.Count <= cap, $"Repeated spawns never exceed the cap ({lab.ActiveCount} alive / {lab.Owned.Count} owned, cap {cap}).");

        int xpBefore = w.Progression.Data.Xp; float heatBefore = w.Heat;
        foreach (var npc in new List<CityNpc>(lab.Owned)) if (npc != null && !npc.Dead) npc.Damage(1000000f, w.Powers);
        yield return null;
        Check(lab.ActiveCount == 0, "Every lab enemy can be defeated through the shared damage path.");
        Check(w.Progression.Data.Xp == xpBefore && w.Heat == heatBefore && w.Heat == 0f,
            $"CONTROL: lab defeats pay no XP ({xpBefore} -> {w.Progression.Data.Xp}) and raise no Heat ({w.Heat}).");

        lab.Clear();
        Check(lab.Owned.Count == 0, "Clear removes every lab-owned enemy.");

        Check(w.Progression.SavePath == null, "CONTROL: the practice profile has no save path, so progress cannot persist.");
        Check(w.Progression.Data.Side == homeSide && w.Progression.Data.Level == homeLevel && w.Progression.Data.Points == homePoints,
            $"Practice profile copies the saved side/level/points read-only ({w.Progression.Data.Side}, level {w.Progression.Data.Level}, {w.Progression.Data.Points} points).");
        Check(w.RestorePracticeHealth() && w.Health == w.MaxHealth, "Restore health refills practice health.");
        Check(w.Powers.RestorePracticeEnergy(), "Restore energy refills practice energy.");

        bool existedAfter = File.Exists(personalSave); string hashAfter = existedAfter ? Hash(personalSave) : null;
        Check(existedAfter == existedBefore && hashBefore == hashAfter,
            $"CONTROL: personal save file is byte-identical after the lab ({personalSave}).");

        lab.Exit();
        float homeBy = Time.realtimeSinceStartup + 60f;
        while ((SceneManager.GetActiveScene().name != GameFlow.HomeScene || FindAnyObjectByType<ModeScreens>() == null) && Time.realtimeSinceStartup < homeBy) yield return null;
        Check(SceneManager.GetActiveScene().name == GameFlow.HomeScene, "Leaving the lab returns Home.");
        Check(CombatLab.Active == null, "Returning Home destroys the lab instance.");
        var back = FindAnyObjectByType<ModeScreens>();
        Check(back != null && back.ModeButtons.ContainsKey("battle-lab"), "BATTLE LAB route is still on Home after returning.");
    }

    /// Presses the chip the way a click does: UI Toolkit routes a click through the button's Clickable, whose delegate is the
    /// callback ModeScreens registered. Pooled pointer events cannot be posed from outside the package (their setters are
    /// internal), so the registered callback is invoked directly; the scene assertion that follows still proves the route
    /// the player's click runs, and a missing handler fails the suite instead of passing.
    IEnumerator Click(Button button)
    {
        var clickable = typeof(Button)
            .GetProperty("clickable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(button);
        Check(clickable != null, "Chip has a Clickable (it is a real button, not a label).");
        var handler = clickable.GetType()
            .GetField("clicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(clickable) as Action;
        Check(handler != null, "Chip Clickable has a click handler registered by the Home menu.");
        handler();
        Log("chip click callback invoked at " + Time.realtimeSinceStartup.ToString("F2"));
        yield return null;
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            return Convert.ToBase64String(sha.ComputeHash(stream));
    }
}
#endif
