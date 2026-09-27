using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Creates the expanded roster's DATA through the editor (no hand-written .asset files): effect assets under
/// Resources/Effects, PowerDefinitions under Resources/Powers, each new power added to every Forge hero's AvailablePowers, and
/// the capped synergy set. Idempotent: existing assets are never overwritten, so tuned inspector values survive a re-run.
/// Menu: Overpowered/Roster/Create missing roster assets. Batch: -executeMethod RosterSetup.Batch
public static class RosterSetup
{
    [MenuItem("Overpowered/Roster/Create missing roster assets")]
    public static void Create()
    {
        ProjectDataSetup.Create();
        Directory.CreateDirectory("Assets/Resources/Effects"); Directory.CreateDirectory("Assets/Resources/Powers"); AssetDatabase.Refresh();
        Define("darkness", "Darkness", Effect<DarknessEffect>("Darkness"), d =>
        {
            d.Description = "Shadow Tendrils: root the aimed enemy in place. It cannot move, but can still attack what is in reach.";
            d.Charges = 2; d.ChargeRecharge = 5f; d.Cooldown = .6f; d.ResourceCost = 15f;
            d.Damage = 4f; d.Force = 0f; d.Duration = 3.5f; d.Range = 22f;
            d.PaletteColor = CityColor.UiPurple; d.MenuIcon = MenuGlyph.Orbit; d.CastingPresentation = true;
        });
        // Channeled: no charges are spent (Charges 1 only keeps the shared HUD slot's single pip full); ResourceCost is the energy
        // needed to START; DrainPerSecond is paid while held (net ~20/s against the 12/s regen, ~5 s from full); Damage is per second.
        Define("laser-eyes", "Laser Eyes", Effect<LaserEyesEffect>("LaserEyes"), d =>
        {
            d.Description = "Hold to fire a continuous beam at the crosshair. Drains energy while held.";
            d.Activation = PowerActivation.Channeled; d.DrainPerSecond = 32f; d.ResourceCost = 10f;
            d.Charges = 1; d.ChargeRecharge = 1f; d.Cooldown = .8f;
            d.Damage = 38f; d.Force = 60f; d.Range = 26f; d.Duration = 0f; d.OriginHeight = 1.62f;
            d.PaletteColor = CityColor.Red; d.MenuIcon = MenuGlyph.Star; d.CastingPresentation = false;
        });
        Define("lightning", "Lightning", Effect<LightningEffect>("Lightning"), d =>
        {
            d.Description = "Chain bolt: strikes the aimed enemy, then arcs to up to five nearby enemies (weaker each jump).";
            d.Charges = 2; d.ChargeRecharge = 2.5f; d.Cooldown = .8f; d.ResourceCost = 18f;
            d.Damage = 24f; d.Force = 300f; d.Range = 24f;
            d.PaletteColor = CityColor.Cream; d.MenuIcon = MenuGlyph.Chevron; d.CastingPresentation = true;
        });
        // Defensive: Damage 0 (never used); Duration is the field's life; the absorb capacity lives on the effect asset.
        Define("force-field", "Force Field", Effect<ForceFieldEffect>("ForceField"), d =>
        {
            d.Description = "Raise a shield that absorbs up to 60 damage for 6 s. Deals no damage.";
            d.Charges = 1; d.ChargeRecharge = 12f; d.Cooldown = 1f; d.ResourceCost = 25f;
            d.Damage = 0f; d.Force = 0f; d.Duration = 6f; d.Range = 0f;
            d.PaletteColor = CityColor.Blue; d.MenuIcon = MenuGlyph.Shield; d.CastingPresentation = true;
        });
        // Range = dash metres, Duration = dash seconds (9 m in 0.18 s); Damage hits each hostile NPC the dash passes, once.
        Define("speed", "Speed", Effect<SpeedDashEffect>("SpeedDash"), d =>
        {
            d.Description = "Burst dash through the crowd toward your movement (or aim). Clips enemies you pass.";
            d.Charges = 3; d.ChargeRecharge = 2.5f; d.Cooldown = .35f; d.ResourceCost = 8f;
            d.Damage = 8f; d.Force = 0f; d.Range = 9f; d.Duration = .18f;
            d.PaletteColor = CityColor.Amber; d.MenuIcon = MenuGlyph.Wing; d.CastingPresentation = false;
        });
        // Damage is damage PER SECOND for Duration (10 x 6 s = 60: kills a 39 HP Endless rusher on its own, not a 65 HP cop).
        Define("poison", "Poison", Effect<PoisonEffect>("Poison"), d =>
        {
            d.Description = "Poison the aimed enemy (damage over time). If it dies while poisoned, the poison jumps to nearby enemies.";
            d.Charges = 2; d.ChargeRecharge = 3f; d.Cooldown = .6f; d.ResourceCost = 12f;
            d.Damage = 10f; d.Force = 0f; d.Duration = 6f; d.Range = 20f;
            d.PaletteColor = CityColor.Leaf; d.MenuIcon = MenuGlyph.Crystal; d.CastingPresentation = true;
        });
        AddToHeroes();
        AddSynergies();
        HeroArchetypeSetup.Apply();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }
    public static void Batch() { Create(); EditorApplication.Exit(0); }
    static T Effect<T>(string name, Action<T> configure = null) where T : ScriptableObject
    {
        string path = "Assets/Resources/Effects/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path); if (existing != null) return existing;
        var asset = ScriptableObject.CreateInstance<T>(); configure?.Invoke(asset); AssetDatabase.CreateAsset(asset, path); return asset;
    }
    static void Define(string id, string title, PowerEffect effect, Action<PowerDefinition> configure)
    {
        string path = "Assets/Resources/Powers/" + id + ".asset";
        if (AssetDatabase.LoadAssetAtPath<PowerDefinition>(path) != null) return;
        var d = ScriptableObject.CreateInstance<PowerDefinition>();
        d.Id = id; d.DisplayName = title; d.Effect = effect; d.InitiallyUnlocked = true; d.Color = Color.white;
        configure(d);
        AssetDatabase.CreateAsset(d, path);
    }
    /// The synergy set is CAPPED (Sonic Slam, Thermal Shock, Solar Flare, Void Grasp, Eclipse Beam are the five in scope).
    /// Only the three new capped pairs are created here; the eight other legacy synergies are left untouched (reported in
    /// STATUS, not deleted). No other pair is ever created.
    static void AddSynergies()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ForgeCatalog>("Assets/Resources/ForgeCatalog.asset");
        if (catalog == null) { HeroForgeSetup.Create(); catalog = AssetDatabase.LoadAssetAtPath<ForgeCatalog>("Assets/Resources/ForgeCatalog.asset"); }
        Directory.CreateDirectory("Assets/Resources/Forge/Synergies"); Directory.CreateDirectory("Assets/Resources/Forge/Effects");
        var entries = catalog.Synergies.Where(s => s != null).ToList();
        T NewEffect<T>(string id) where T : SynergyEffect
        {
            string path = "Assets/Resources/Forge/Effects/" + id + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path); if (existing != null) return existing;
            var effect = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(effect, path); return effect;
        }
        void Add(string id, string title, string a, string b, SynergyEffect effect, string description, Action<PowerSynergyDefinition> configure)
        {
            if (entries.Any(s => s.Id == id)) return;
            string path = "Assets/Resources/Forge/Synergies/" + id + ".asset";
            var d = AssetDatabase.LoadAssetAtPath<PowerSynergyDefinition>(path);
            if (d == null)
            {
                d = ScriptableObject.CreateInstance<PowerSynergyDefinition>(); d.Id = id; d.DisplayName = title; d.Description = description;
                d.PowerA = AssetDatabase.LoadAssetAtPath<PowerDefinition>("Assets/Resources/Powers/" + a + ".asset");
                d.PowerB = AssetDatabase.LoadAssetAtPath<PowerDefinition>("Assets/Resources/Powers/" + b + ".asset");
                d.Effect = effect; configure(d); AssetDatabase.CreateAsset(d, path);
            }
            entries.Add(d);
        }
        Add("solar-flare", "Solar Flare", "fire", "laser-eyes", NewEffect<SolarFlareEffect>("solar-flare"),
            "Focus the beam, then the target point erupts and burns.",
            d => { d.Cooldown = 40; d.Range = 30; d.LiftSeconds = .5f; d.Damage = 60; d.Radius = 6; d.Force = 2600; d.BurnSeconds = 4; d.Primary = CityColor.Fire; d.Secondary = CityColor.Red; });
        Add("void-grasp", "Void Grasp", "darkness", "telekinesis", NewEffect<VoidGraspEffect>("void-grasp"),
            "Open a singularity: pull nearby enemies in, crush them together, leave them rooted.",
            d => { d.Cooldown = 40; d.Range = 26; d.Radius = 9; d.MaxTargets = 6; d.Duration = 1.4f; d.Spring = 30; d.Damping = 6; d.Damage = 45; d.Force = 2200; d.FreezeSeconds = 2.5f; d.Primary = CityColor.UiPurple; d.Secondary = CityColor.UiNavy; });
        Add("eclipse-beam", "Eclipse Beam", "darkness", "laser-eyes", NewEffect<EclipseBeamEffect>("eclipse-beam"),
            "Wrap one enemy in darkness, then burn straight through it. Single target, enormous damage.",
            d => { d.Cooldown = 45; d.Range = 30; d.LiftSeconds = .6f; d.Duration = .5f; d.Damage = 160; d.Force = 3000; d.Radius = 1.5f; d.Primary = CityColor.Red; d.Secondary = CityColor.UiPurple; });
        catalog.Synergies = entries.ToArray(); EditorUtility.SetDirty(catalog);
    }
    /// Every shipping hero may equip every shipping power (the existing heroes were created with the whole list).
    static void AddToHeroes()
    {
        AssetDatabase.SaveAssets();
        var powers = AssetDatabase.FindAssets("t:PowerDefinition", new[] { "Assets/Resources/Powers" })
            .Select(g => AssetDatabase.LoadAssetAtPath<PowerDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null && !p.Id.StartsWith("verification-")).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        var catalog = AssetDatabase.LoadAssetAtPath<ForgeCatalog>("Assets/Resources/ForgeCatalog.asset");
        if (catalog == null) return;
        foreach (var hero in catalog.Heroes)
        {
            if (hero == null) continue;
            var missing = powers.Where(p => Array.IndexOf(hero.AvailablePowers, p) < 0).ToArray();
            if (missing.Length == 0) continue;
            hero.AvailablePowers = hero.AvailablePowers.Concat(missing).ToArray();
            EditorUtility.SetDirty(hero);
        }
    }
}
