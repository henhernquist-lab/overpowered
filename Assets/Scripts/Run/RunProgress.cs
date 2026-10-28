using System;
using System.Collections.Generic;
using UnityEngine;

/// RUN PROGRESSION — temporary Endless Fight state. Lives on the session object, is destroyed with the city scene, and
/// never writes to the save file. Persistent progression (PlayerProgression) is untouched: a level-40 account still
/// enters a run at level 1.
///
/// Rewards: credited kills only (KillCredit shares the health-on-kill gate), once per enemy life. XP is accumulated in
/// long so a huge grant can never overflow; a single grant may raise several levels, and every level queues one pick.
/// Every Pulsebreaker kill queues an extra Epic pick.
public sealed class RunProgress : MonoBehaviour
{
    public WorldSession World { get; private set; }
    public int Level { get; private set; } = 1;
    public int Xp { get; private set; }
    public int RequiredXp => RunMath.RequiredXp(Level);
    public int Seed { get; private set; }
    public int Kills { get; private set; }
    public int BossKills { get; private set; }
    public int UpgradePicks { get; private set; }
    public bool Closed { get; private set; }
    public bool UpgradeOpen { get; private set; }
    public int PendingPicks => pending.Count;
    public RunModifiers Mods { get; private set; } = RunModifiers.Empty;

    readonly List<RunUpgradeStack> stacks = new List<RunUpgradeStack>(24);
    readonly List<RunUpgradeDefinition> offer = new List<RunUpgradeDefinition>(3);
    readonly Queue<bool> pending = new Queue<bool>();
    System.Random rng;
    bool pausedByUpgrade;

    public IReadOnlyList<RunUpgradeStack> Stacks => stacks;
    public IReadOnlyList<RunUpgradeDefinition> Offer => offer;

    public event Action Changed;
    /// Amount and where it happened (HUD "+N XP" popup).
    public event Action<int, Vector3> XpGained;
    /// Raised once per level reached, with the new level.
    public event Action<int> LevelGained;

    /// Deterministic seed for verification (the shipped game draws a fresh one per run).
    public static int? SeedOverride;

    public void Configure(WorldSession world, int seed)
    {
        World = world;
        if (SeedOverride.HasValue) { seed = SeedOverride.Value; SeedOverride = null; }
        Seed = seed;
        rng = new System.Random(seed);
        Level = 1; Xp = 0; Kills = 0; BossKills = 0; UpgradePicks = 0;
        Closed = false; UpgradeOpen = false; pending.Clear(); stacks.Clear(); offer.Clear();
        Mods = RunModifiers.Empty;
        Changed?.Invoke();
    }

    public int StacksOf(string id)
    {
        for (int i = 0; i < stacks.Count; i++) if (stacks[i].Id == id) return stacks[i].Count;
        return 0;
    }

    /// Called by KillCredit for every player-credited, hostile, non-civilian kill — exactly once per enemy life.
    public void ClaimKill(CityNpc victim)
    {
        if (Closed || victim == null) return;
        Kills++;
        bool boss = victim.IsBoss;
        if (boss) BossKills++;
        int xp = boss ? RunTuning.BossXp : RunTuning.XpFor(victim.Archetype != null ? victim.Archetype.DisplayName : null);
        GrantXp(xp, victim.transform.position);
        // Boss rounds pay a milestone choice as well as their XP; it is Epic-only (see OpenNext).
        if (boss) { pending.Enqueue(true); OpenNext(); }
    }

    public void GrantXp(int amount, Vector3 where)
    {
        if (Closed || amount <= 0) return;
        XpGained?.Invoke(amount, where);
        long total = (long)Xp + amount;
        int reached = 0;
        while (Level < RunTuning.MaxLevel)
        {
            int need = RunMath.RequiredXp(Level);
            if (total < need) break;
            total -= need; Level++; reached++;
            pending.Enqueue(false);
        }
        Xp = (int)Math.Min(total, int.MaxValue);
        if (reached > 0) LevelGained?.Invoke(Level);
        Changed?.Invoke();
        if (pending.Count > 0 && !UpgradeOpen) OpenNext();
    }

    /// Shows the next queued choice. Called internally; public so a test can drive the loop without XP.
    public void OpenNext()
    {
        if (Closed || pending.Count == 0 || World == null) return;
        offer.Clear();
        RunUpgradePicker.Fill(offer, this, pending.Peek(), rng);
        if (offer.Count == 0) { pending.Dequeue(); return; }   // impossible by construction; never stall the run
        UpgradeOpen = true;
        Pause(true);
        Changed?.Invoke();
    }

    /// Picks card `index` of the current offer. Returns false if no choice is open.
    public bool Choose(int index)
    {
        if (!UpgradeOpen || index < 0 || index >= offer.Count) return false;
        var def = offer[index];
        pending.Dequeue();
        AddStack(def);
        UpgradePicks++;
        float healthBefore = World != null ? World.MaxHealth : 0f;
        offer.Clear();
        Mods = RunModifiers.Build(stacks);
        if (World != null)
        {
            // A max-health card raises the ceiling; grant the difference once so the pick is felt immediately.
            float healthAfter = World.MaxHealth;
            if (healthAfter > healthBefore + .01f) World.HealPlayer(healthAfter - healthBefore);
            if (World.Powers != null) World.Powers.RefreshRunStats(def);
        }
        if (pending.Count > 0) OpenNext();
        else { UpgradeOpen = false; Pause(false); }
        Changed?.Invoke();
        return true;
    }

    void AddStack(RunUpgradeDefinition def)
    {
        if (def == null) return;
        for (int i = 0; i < stacks.Count; i++)
        {
            if (stacks[i].Id != def.Id) continue;
            var s = stacks[i];
            s.Count = Mathf.Min(s.Count + 1, def.MaxStacks);
            stacks[i] = s;
            return;
        }
        stacks.Add(new RunUpgradeStack { Id = def.Id, Count = 1 });
    }

    /// Stops all run rewards. Called by GameModeSession.Finish: no XP after the run is over, ever.
    public void Close()
    {
        Closed = true;
        pending.Clear();
        offer.Clear();
        UpgradeOpen = false;
        if (pausedByUpgrade) Pause(false);
    }

    void Pause(bool paused)
    {
        if (World == null || World.Mode == null || World.Mode.Ended) return;
        if (paused)
        {
            if (!World.Mode.Paused) { World.Mode.SetPaused(true); pausedByUpgrade = true; }
        }
        else if (pausedByUpgrade)
        {
            pausedByUpgrade = false;
            if (World.Mode.Paused) World.Mode.SetPaused(false);
        }
    }

    void OnDestroy()
    {
        if (pausedByUpgrade && World != null && World.Mode != null && !World.Mode.Ended) Pause(false);
        if (World != null && World.Run == this) World.Run = null;
    }
}
