using System.Collections.Generic;

public enum RunRarity { Common, Rare, Epic }

/// What a card multiplies or adds. Stat cards need no per-effect code: every power already resolves through
/// PowerUser.Stats, signatures through SynergyRunner, and health/energy through WorldSession/PowerUser.
public enum RunStat
{
    None,
    PowerDamage, PowerCooldown, PowerRadius, PowerRange, PowerDuration, ExtraCharges, PowerEnergyCost,
    MeleeDamage, AllPowerDamage,
    SignatureDamage, SignatureCooldown, SignatureEnergyCost, SignatureRadius,
    MaxHealth, HealOnKill, MaxEnergy, EnergyRegen
}

/// Mechanical evolutions (★). Each one is read by exactly one effect, guarded so a run without the stack behaves
/// exactly as today. They are the reason the catalog is not just percentages.
public enum RunMechanic
{
    None,
    FireDoubleBlast,   // every 3rd Fire blast detonates twice
    IceShatter,        // +% shatter damage on frozen targets
    DarknessRoot,      // longer initial root, stronger slow
    LightningArc,      // +N chain targets
    PoisonSpread,      // +N spread targets on death
    TelekinesisMass,   // thrown damage scales with prop mass
    TelekinesisBlast,  // thrown impacts release a blast
    StrengthShockwave, // melee finisher/shockwave radius + damage
    LaserPierce,       // beam pierces to a second target
    LaserOverload      // beam damage ramps while held
}

/// One run-only upgrade. Plain C# data (not a ScriptableObject) so the run layer can never mutate a shared asset and a
/// balance pass is a single table edit. Persisted nowhere: the catalog is static, the stacks live in RunProgress.
public sealed class RunUpgradeDefinition
{
    public string Id;
    public string Name;
    public string Description;
    /// "" = hero/global, "signature" = the equipped signature, else a PowerDefinition.Id.
    public string PowerId = "";
    public RunRarity Rarity = RunRarity.Common;
    public RunStat Stat = RunStat.None;
    /// Multiplier per stack is (1 + PerStack) for multipliers; absolute amount for ExtraCharges.
    public float PerStack;
    public RunMechanic Mechanic = RunMechanic.None;
    public int MechanicPerStack;
    public int MaxStacks = 1;
    public int MinLevel = 1;
    public string RequiresId;

    public bool IsSignature => PowerId == "signature";
    public bool IsHero => string.IsNullOrEmpty(PowerId);
    public string TargetLabel => IsHero ? "Hero" : IsSignature ? "Signature" : string.Empty;
}

/// The shipped catalog. Every power here exists in Assets/Resources/Powers. The power-expansion roster is covered by
/// stat cards built on the same PowerUser.Stats path, so a run that equips those powers always has legal offers.
public static class RunUpgradeLibrary
{
    public static readonly RunUpgradeDefinition[] All = Build();
    static readonly Dictionary<string, RunUpgradeDefinition> ById = Index();

    public static RunUpgradeDefinition Find(string id) => id != null && ById.TryGetValue(id, out var d) ? d : null;

    static Dictionary<string, RunUpgradeDefinition> Index()
    {
        var map = new Dictionary<string, RunUpgradeDefinition>(All.Length);
        foreach (var d in All) map[d.Id] = d;
        return map;
    }

    static RunUpgradeDefinition[] Build()
    {
        var list = new List<RunUpgradeDefinition>(96);
        void Add(string id, string name, string power, RunRarity rarity, RunStat stat, float per, int max, string description,
                 int minLevel = 1, string requires = null, RunMechanic mechanic = RunMechanic.None, int mechanicPer = 0)
        {
            list.Add(new RunUpgradeDefinition
            {
                Id = id, Name = name, PowerId = power, Rarity = rarity, Stat = stat, PerStack = per, MaxStacks = max,
                Description = description, MinLevel = minLevel, RequiresId = requires, Mechanic = mechanic, MechanicPerStack = mechanicPer
            });
        }

        // ---------------------------------------------------------------- Fire Blast
        Add("fire-core", "KINDLED CORE", "fire", RunRarity.Common, RunStat.PowerDamage, .14f, 4, "Fire Blast damage +14%");
        Add("fire-radius", "WIDER BLAST", "fire", RunRarity.Common, RunStat.PowerRadius, .18f, 4, "Blast radius +18%");
        Add("fire-burn", "LINGERING HEAT", "fire", RunRarity.Rare, RunStat.PowerDuration, .25f, 3, "Burn lasts +25% longer");
        Add("fire-twin", "TWIN DETONATION", "fire", RunRarity.Epic, RunStat.None, 0f, 1,
            "Every 3rd blast detonates twice", 4, "fire-core", RunMechanic.FireDoubleBlast, 1);

        // ---------------------------------------------------------------- Ice
        Add("ice-core", "SHATTERING COLD", "ice", RunRarity.Common, RunStat.PowerDamage, .14f, 4, "Ice damage +14%");
        Add("ice-freeze", "DEEP CHILL", "ice", RunRarity.Common, RunStat.PowerDuration, .12f, 4, "Freeze lasts +12%");
        Add("ice-shatter", "GLACIAL RUPTURE", "ice", RunRarity.Rare, RunStat.None, 0f, 3,
            "Shattering a frozen enemy deals +30% damage", 1, null, RunMechanic.IceShatter, 30);
        Add("ice-anchor", "ABSOLUTE ZERO", "ice", RunRarity.Epic, RunStat.None, 0f, 1,
            "Shatter damage doubled and its burst reaches 2 m further", 5, "ice-shatter", RunMechanic.IceShatter, 100);

        // ---------------------------------------------------------------- Darkness
        Add("dark-core", "DEEPER CURSE", "darkness", RunRarity.Common, RunStat.PowerDamage, .15f, 4, "Curse damage +15%");
        Add("dark-radius", "WIDER AURA", "darkness", RunRarity.Common, RunStat.PowerRadius, .12f, 3, "Aura radius +12%");
        Add("dark-grip", "UMBRAL GRIP", "darkness", RunRarity.Rare, RunStat.None, 0f, 2,
            "Initial root +0.5s and a stronger slow", 2, null, RunMechanic.DarknessRoot, 1);
        Add("dark-eclipse", "ECLIPSE", "darkness", RunRarity.Epic, RunStat.PowerDuration, .35f, 1, "Curse lasts +35% longer");

        // ---------------------------------------------------------------- Lightning
        Add("bolt-core", "OVERCHARGE", "lightning", RunRarity.Common, RunStat.PowerDamage, .14f, 4, "Lightning damage +14%");
        Add("bolt-radius", "STATIC FIELD", "lightning", RunRarity.Common, RunStat.PowerRadius, .10f, 3, "Arc search radius +10%");
        Add("bolt-arc", "CHAIN REACH", "lightning", RunRarity.Rare, RunStat.None, 0f, 2,
            "Chain to +1 target (hard cap 7)", 2, null, RunMechanic.LightningArc, 1);
        Add("bolt-storm", "CHAIN STORM", "lightning", RunRarity.Epic, RunStat.None, 0f, 1,
            "Chain to +2 more targets", 6, "bolt-arc", RunMechanic.LightningArc, 2);

        // ---------------------------------------------------------------- Poison
        Add("venom-core", "VIRULENCE", "poison", RunRarity.Common, RunStat.PowerDamage, .15f, 4, "Poison damage +15%");
        Add("venom-duration", "LINGERING VENOM", "poison", RunRarity.Common, RunStat.PowerDuration, .20f, 3, "Poison lasts +20%");
        Add("venom-spread", "CONTAGION", "poison", RunRarity.Rare, RunStat.None, 0f, 2,
            "A dying poisoned enemy spreads to +1 target", 1, null, RunMechanic.PoisonSpread, 1);
        Add("venom-plague", "PLAGUE BLOOM", "poison", RunRarity.Epic, RunStat.None, 0f, 1,
            "Spread reaches +2 more targets and ignores one wall bounce", 6, "venom-spread", RunMechanic.PoisonSpread, 2);

        // ---------------------------------------------------------------- Telekinesis
        Add("tk-core", "HEAVY HURL", "telekinesis", RunRarity.Common, RunStat.PowerDamage, .15f, 4, "Thrown damage +15%");
        Add("tk-duration", "LONGER GRIP", "telekinesis", RunRarity.Common, RunStat.PowerDuration, .15f, 3, "Hold lasts +15%");
        Add("tk-mass", "MASS DRIVER", "telekinesis", RunRarity.Rare, RunStat.None, 0f, 2,
            "Thrown damage scales with the prop's mass", 2, null, RunMechanic.TelekinesisMass, 1);
        Add("tk-wave", "IMPACT WAVE", "telekinesis", RunRarity.Epic, RunStat.None, 0f, 1,
            "Thrown impacts release a 3 m blast", 5, "tk-mass", RunMechanic.TelekinesisBlast, 1);

        // ---------------------------------------------------------------- Super Strength
        Add("str-core", "HARDER HITS", "strength", RunRarity.Common, RunStat.MeleeDamage, .12f, 4, "Punch and kick damage +12%");
        Add("str-cooldown", "FASTER FISTS", "strength", RunRarity.Common, RunStat.PowerCooldown, .12f, 3, "Melee cooldown -12%");
        Add("str-quake", "SHOCKFIST", "strength", RunRarity.Rare, RunStat.None, 0f, 2,
            "Landing a finisher releases a shockwave", 3, null, RunMechanic.StrengthShockwave, 1);
        Add("str-heavy", "TITAN GRIP", "strength", RunRarity.Epic, RunStat.MeleeDamage, .30f, 1, "Melee damage +30%");

        // ---------------------------------------------------------------- Laser Eyes
        Add("eye-core", "FOCUSED BEAM", "laser-eyes", RunRarity.Common, RunStat.PowerDamage, .14f, 4, "Beam damage +14%");
        Add("eye-drain", "EFFICIENT EMITTER", "laser-eyes", RunRarity.Common, RunStat.PowerEnergyCost, .15f, 3, "Beam drain -15%");
        Add("eye-pierce", "PIERCING GAZE", "laser-eyes", RunRarity.Rare, RunStat.None, 0f, 1,
            "The beam pierces to a second target", 3, null, RunMechanic.LaserPierce, 1);
        Add("eye-overload", "OVERLOAD", "laser-eyes", RunRarity.Epic, RunStat.None, 0f, 1,
            "Beam damage ramps up to +40% while held", 5, null, RunMechanic.LaserOverload, 1);

        // ---------------------------------------------------------------- Aerokinesis
        Add("aero-core", "GALE FORCE", "aerokinesis", RunRarity.Common, RunStat.PowerDamage, .15f, 4, "Aerokinesis damage +15%");
        Add("aero-range", "LONG REACH", "aerokinesis", RunRarity.Common, RunStat.PowerRange, .15f, 3, "Gust reach +15%");
        Add("aero-cooldown", "FASTER CURRENT", "aerokinesis", RunRarity.Rare, RunStat.PowerCooldown, .12f, 3, "Aerokinesis cooldown -12%");
        Add("aero-charge", "EYE OF THE STORM", "aerokinesis", RunRarity.Epic, RunStat.ExtraCharges, 1f, 1, "+1 gust charge", 5);

        // ---------------------------------------------------------------- Sonic Manipulation
        Add("sonic-core", "AMPLIFIED", "sonic-manipulation", RunRarity.Common, RunStat.PowerDamage, .14f, 4, "Sonic damage +14%");
        Add("sonic-range", "CARRYING NOTE", "sonic-manipulation", RunRarity.Common, RunStat.PowerRange, .15f, 3, "Sonic reach +15%");
        Add("sonic-cooldown", "QUICKENED PULSE", "sonic-manipulation", RunRarity.Rare, RunStat.PowerCooldown, .12f, 3, "Sonic cooldown -12%");
        Add("sonic-charge", "ENCORE", "sonic-manipulation", RunRarity.Epic, RunStat.ExtraCharges, 1f, 1, "+1 sonic charge", 5);

        // ---------------------------------------------------------------- Combat Morphing
        Add("morph-core", "SHARPENED FORMS", "combat-morphing", RunRarity.Common, RunStat.PowerDamage, .14f, 4, "Morph form damage +14%");
        Add("morph-range", "LONGER REACH", "combat-morphing", RunRarity.Common, RunStat.PowerRange, .12f, 3, "Morph reach +12%");
        Add("morph-cooldown", "FLUID SHIFT", "combat-morphing", RunRarity.Rare, RunStat.PowerCooldown, .10f, 3, "Morph cooldown -10%");
        Add("morph-charge", "APEX ENGINE", "combat-morphing", RunRarity.Epic, RunStat.ExtraCharges, 1f, 1, "+1 morph charge", 5);

        // ---------------------------------------------------------------- Plasma Blades
        Add("blade-core", "SUPERHEATED EDGE", "plasma-blades", RunRarity.Common, RunStat.PowerDamage, .14f, 4, "Plasma damage +14%");
        Add("blade-range", "EXTENDED ARC", "plasma-blades", RunRarity.Common, RunStat.PowerRange, .12f, 3, "Blade reach +12%");
        Add("blade-cooldown", "FLICKER COMBO", "plasma-blades", RunRarity.Rare, RunStat.PowerCooldown, .10f, 3, "Blade cooldown -10%");
        Add("blade-charge", "OVERCHARGED CELL", "plasma-blades", RunRarity.Epic, RunStat.ExtraCharges, 1f, 1, "+1 blade charge", 5);

        // ---------------------------------------------------------------- Grapple Line
        Add("grapple-range", "LONGER LINE", "grapple-line", RunRarity.Common, RunStat.PowerRange, .15f, 3, "Grapple reach +15%");
        Add("grapple-cooldown", "QUICK REEL", "grapple-line", RunRarity.Common, RunStat.PowerCooldown, .15f, 3, "Grapple cooldown -15%");
        Add("grapple-charge", "SECOND ANCHOR", "grapple-line", RunRarity.Rare, RunStat.ExtraCharges, 1f, 1, "+1 grapple charge", 4);

        // ---------------------------------------------------------------- Mirage Decoy
        Add("mirage-duration", "LASTING ECHO", "mirage-decoy", RunRarity.Common, RunStat.PowerDuration, .15f, 3, "Decoy lasts +15%");
        Add("mirage-cooldown", "FASTER ILLUSION", "mirage-decoy", RunRarity.Common, RunStat.PowerCooldown, .12f, 3, "Decoy cooldown -12%");
        Add("mirage-charge", "CROWD OF ONE", "mirage-decoy", RunRarity.Rare, RunStat.ExtraCharges, 1f, 1, "+1 decoy charge", 4);

        // ---------------------------------------------------------------- Secondaries
        Add("spd-core", "QUICKER DASH", "speed", RunRarity.Common, RunStat.PowerDamage, .20f, 3, "Dash damage +20%");
        Add("spd-charge", "SECOND WIND", "speed", RunRarity.Rare, RunStat.ExtraCharges, 1f, 1, "+1 dash charge", 4);
        Add("tp-range", "FAR STEP", "teleport", RunRarity.Common, RunStat.PowerRange, .15f, 3, "Teleport range +15%");
        Add("tp-cooldown", "QUICK RETURN", "teleport", RunRarity.Rare, RunStat.PowerCooldown, .15f, 2, "Recharge -15%");
        Add("ff-duration", "LINGERING WARD", "force-field", RunRarity.Common, RunStat.PowerDuration, .15f, 3, "Field lasts +15%");
        Add("ff-cost", "CHEAPER WARD", "force-field", RunRarity.Rare, RunStat.PowerEnergyCost, .15f, 2, "Field costs -15% energy");
        Add("ps-duration", "LONGER PHASE", "phase-shift", RunRarity.Common, RunStat.PowerDuration, .12f, 3, "Phase lasts +12%");
        Add("ps-cooldown", "SLIPSTREAM", "phase-shift", RunRarity.Rare, RunStat.PowerCooldown, .12f, 2, "Phase recharge -12%");
        Add("fly-fuel", "AEROBATIC", "flight", RunRarity.Common, RunStat.PowerDuration, .15f, 3, "Flight fuel +15%");
        Add("fly-cost", "AFTERBURNER", "flight", RunRarity.Rare, RunStat.PowerEnergyCost, .12f, 2, "Flight fuel drain -12%");

        // ---------------------------------------------------------------- Signature (the equipped one only)
        Add("sig-damage", "HEIGHTENED RITE", "signature", RunRarity.Common, RunStat.SignatureDamage, .12f, 3, "Signature damage +12%");
        Add("sig-cooldown", "CHEAPER CALL", "signature", RunRarity.Common, RunStat.SignatureCooldown, .12f, 3, "Signature cooldown -12%");
        Add("sig-energy", "FOCUSED RITE", "signature", RunRarity.Common, RunStat.SignatureEnergyCost, .12f, 3, "Signature energy cost -12%");
        Add("sig-radius", "WIDER RITE", "signature", RunRarity.Rare, RunStat.SignatureRadius, .12f, 2, "Signature radius +12%");
        Add("sig-second", "SECOND COMING", "signature", RunRarity.Epic, RunStat.SignatureCooldown, .25f, 1,
            "Signature cooldown -25% (never below 12s)", 8, "sig-cooldown");

        // ---------------------------------------------------------------- Hero / defence (always eligible)
        Add("hero-hp", "SECOND WIND", "", RunRarity.Common, RunStat.MaxHealth, .10f, 3, "Max health +10%");
        Add("hero-heal", "SCAVENGER", "", RunRarity.Common, RunStat.HealOnKill, .20f, 3, "Health on kill +20%");
        Add("hero-energy", "DEEP RESERVES", "", RunRarity.Common, RunStat.MaxEnergy, .12f, 3, "Max energy +12%");
        Add("hero-regen", "FASTER BREATH", "", RunRarity.Common, RunStat.EnergyRegen, .12f, 3, "Energy regen +12%");
        Add("hero-fury", "RELENTLESS", "", RunRarity.Common, RunStat.AllPowerDamage, .02f, 999, "All power damage +2%");

        return list.ToArray();
    }
}
