using System;

/// RUN PROGRESSION — pure maths, no Unity state, trivially testable.
///   RequiredXp(L) = max(1, round(120 * 1.20^(L-1)))
/// A geometric curve against a wave whose XP grows roughly with the round number keeps the level pace slowly decaying:
/// one level every ~1.3 rounds at round 5, ~2.5 rounds by round 30. That is the roguelite shape (early picks define the
/// build, later picks polish it) and it needs no hard cap.
public static class RunMath
{
    public static int RequiredXp(int level)
    {
        if (level < 1) level = 1;
        if (level >= RunTuning.MaxLevel) return int.MaxValue;
        double raw = RunTuning.BaseXp * Math.Pow(RunTuning.XpGrowth, level - 1);
        if (double.IsNaN(raw) || double.IsInfinity(raw) || raw >= int.MaxValue) return int.MaxValue;
        return Math.Max(1, (int)Math.Round(raw));
    }

    /// Total XP a fresh run needs to reach `level` (level 1 = 0). Used by tests and the results summary.
    public static long TotalXpTo(int level)
    {
        long total = 0;
        for (int l = 1; l < level && l < RunTuning.MaxLevel; l++) total += RequiredXp(l);
        return total;
    }

    /// The level a fresh run reaches with `xp` total XP. Mirrors RunProgress.GrantXp without touching state.
    public static int LevelFor(long xp)
    {
        int level = 1; long left = Math.Max(0, xp);
        while (level < RunTuning.MaxLevel)
        {
            int need = RequiredXp(level);
            if (left < need) break;
            left -= need; level++;
        }
        return level;
    }
}
