using System;
using System.Collections.Generic;

/// Weighted draw of three distinct upgrades for one level-up. Pure given (catalog, loadout, stacks, level, rng), so a
/// seeded test can assert an exact offer set. Rules:
///  - only the equipped primaries, the equipped secondary and the equipped signature are eligible (never a power the
///    player does not own or has not equipped);
///  - cards already at their stack cap, below their prerequisites or above the run level are skipped;
///  - the draw is weighted by rarity AND by what the card targets (a primary outweighs a secondary);
///  - at least one card is always for one of the two primaries when such a card exists;
///  - the offer is never empty (the repeatable hero card backfills).
public static class RunUpgradePicker
{
    public static void Fill(List<RunUpgradeDefinition> into, RunProgress run, bool epicOnly, Random rng)
    {
        into.Clear();
        if (run == null) return;
        var world = run.World;
        var user = world != null ? world.Powers : null;
        int level = run.Level;
        RunTuning.RarityWeights(level, out float common, out float rare, out float epic);
        if (epicOnly) { common = 0f; rare = 0f; epic = 1f; }

        var pool = new List<RunUpgradeDefinition>(24);
        var weights = new List<float>(24);
        for (int i = 0; i < RunUpgradeLibrary.All.Length; i++)
        {
            var def = RunUpgradeLibrary.All[i];
            if (!Eligible(def, run, user, level)) continue;
            float rarity = def.Rarity == RunRarity.Epic ? epic : def.Rarity == RunRarity.Rare ? rare : common;
            if (rarity <= 0f) continue;
            pool.Add(def);
            weights.Add(rarity * TargetWeight(def, user));
        }

        for (int pick = 0; pick < 3 && pool.Count > 0; pick++)
        {
            int index = Draw(weights, rng);
            if (index < 0) break;
            into.Add(pool[index]);
            pool.RemoveAt(index); weights.RemoveAt(index);
        }

        // A boss milestone is Epic ONLY. Never pad it with a common card: a loadout with two eligible evolutions gets a
        // two-card choice, and a loadout with none gets an ordinary offer rather than an empty modal.
        if (epicOnly)
        {
            if (into.Count == 0) Fill(into, run, false, rng);
            return;
        }

        // Guarantee a choice that touches the equipped primaries (the cards the player actually plays with).
        if (!HasPrimary(into, user))
        {
            var primary = PickPrimary(run, user, level, rng, into);
            if (primary != null)
            {
                if (into.Count >= 3) into[2] = primary; else into.Add(primary);
            }
        }
        // Backfill so the modal always has three cards.
        for (int i = 0; into.Count < 3 && i < 12; i++)
        {
            var fury = RunUpgradeLibrary.Find("hero-fury");
            if (fury == null) break;
            if (!into.Contains(fury)) into.Add(fury);
            else
            {
                var any = RunUpgradeLibrary.Find(i % 2 == 0 ? "hero-hp" : "hero-energy");
                if (any != null && !into.Contains(any) && Eligible(any, run, user, level)) into.Add(any);
                else break;
            }
        }
    }

    public static bool Eligible(RunUpgradeDefinition def, RunProgress run, PowerUser user, int level)
    {
        if (def == null || string.IsNullOrEmpty(def.Id)) return false;
        if (level < def.MinLevel) return false;
        if (run != null && run.StacksOf(def.Id) >= def.MaxStacks) return false;
        if (!string.IsNullOrEmpty(def.RequiresId) && (run == null || run.StacksOf(def.RequiresId) <= 0)) return false;
        if (def.IsHero) return true;
        if (user == null) return false;
        if (def.IsSignature)
        {
            return level >= RunTuning.SignatureUnlockLevel && user.Synergy != null && user.Synergy.IsSignature;
        }
        return def.PowerId == EquippedId(user, 0) || def.PowerId == EquippedId(user, 1) || def.PowerId == EquippedId(user, 2);
    }

    static string EquippedId(PowerUser user, int slot)
    {
        var def = slot == 0 ? user.EquippedA : slot == 1 ? user.EquippedB : user.EquippedSecondary;
        return def != null ? def.Id : null;
    }

    static float TargetWeight(RunUpgradeDefinition def, PowerUser user)
    {
        if (def.IsHero) return RunTuning.HeroWeight;
        if (def.IsSignature) return RunTuning.SignatureWeight;
        if (user != null && (def.PowerId == EquippedId(user, 0) || def.PowerId == EquippedId(user, 1))) return RunTuning.PrimaryWeight;
        return RunTuning.SecondaryWeight;
    }

    static bool HasPrimary(List<RunUpgradeDefinition> offer, PowerUser user)
    {
        if (user == null) return true;
        string a = EquippedId(user, 0), b = EquippedId(user, 1);
        foreach (var def in offer) if (def.PowerId == a || def.PowerId == b) return true;
        return false;
    }

    static RunUpgradeDefinition PickPrimary(RunProgress run, PowerUser user, int level, Random rng, List<RunUpgradeDefinition> exclude)
    {
        if (user == null) return null;
        var pool = new List<RunUpgradeDefinition>(8); var weights = new List<float>(8);
        for (int i = 0; i < RunUpgradeLibrary.All.Length; i++)
        {
            var def = RunUpgradeLibrary.All[i];
            if (!Eligible(def, run, user, level) || exclude.Contains(def)) continue;
            if (def.PowerId != EquippedId(user, 0) && def.PowerId != EquippedId(user, 1)) continue;
            pool.Add(def); weights.Add(1f);
        }
        int index = Draw(weights, rng);
        return index >= 0 ? pool[index] : null;
    }

    static int Draw(List<float> weights, Random rng)
    {
        float total = 0f;
        for (int i = 0; i < weights.Count; i++) total += weights[i];
        if (total <= 0f || weights.Count == 0) return -1;
        double roll = (rng != null ? rng.NextDouble() : UnityEngine.Random.value) * total;
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0) return i;
        }
        return weights.Count - 1;
    }
}
