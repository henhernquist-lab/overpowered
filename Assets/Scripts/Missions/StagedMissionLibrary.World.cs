/// District-authored and cross-district staged missions (world depth). Same rules as the Core recipes: code-defined data
/// turned into assets by StagedMissionSetup, dormant until a rotation is switched on. Each has its own stage structure
/// (StagedMissionVerification enforces unique stage sequences). "Districts" / "Category" / "Tags" only feed the rotation
/// options (data); gameplay never branches on them. Placeholder actors and props only.
public static partial class StagedMissionLibrary
{
    static readonly Entry[] World =
    {
        // ---- Downtown
        new Entry { Id = "highrise-panic", Title = "High-rise panic", Side = PlayerSide.Hero, Kind = CrimeKind.Fire, Deadline = 260f, Build = HighrisePanic, Districts = new[] { "Downtown" }, Category = "emergency", Tags = new[] { "highrise" } },
        new Entry { Id = "corporate-raid", Title = "Corporate raid", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 480f, Build = CorporateRaid, Districts = new[] { "Downtown" }, Category = "heist", Tags = new[] { "corporate" }, CrossDistrict = true },
        // ---- Docks
        new Entry { Id = "smuggler-intercept", Title = "Smuggler intercept", Side = PlayerSide.Hero, Kind = CrimeKind.Robbery, Deadline = 240f, Build = SmugglerIntercept, Districts = new[] { "Docks" }, Category = "vehicle", Tags = new[] { "cargo" } },
        new Entry { Id = "dockyard-score", Title = "Dockyard score", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 240f, Build = DockyardScore, Districts = new[] { "Docks" }, Category = "heist", Tags = new[] { "cargo" } },
        // ---- Park
        new Entry { Id = "public-event-attack", Title = "Public event attack", Side = PlayerSide.Hero, Kind = CrimeKind.Mugging, Deadline = 260f, Build = PublicEventAttack, Districts = new[] { "Park" }, Category = "defence", Tags = new[] { "event" } },
        new Entry { Id = "park-chaos", Title = "Chaos in the park", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 300f, Build = ParkChaos, Districts = new[] { "Park" }, Category = "chaos", Tags = new[] { "open" } },
        // ---- Residential
        new Entry { Id = "neighbourhood-siege", Title = "Neighbourhood siege", Side = PlayerSide.Hero, Kind = CrimeKind.Mugging, Deadline = 260f, Build = NeighbourhoodSiege, Districts = new[] { "Residential" }, Category = "defence", Tags = new[] { "homes" } },
        new Entry { Id = "safehouse-break-in", Title = "Safehouse break-in", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 300f, Build = SafehouseBreakIn, Districts = new[] { "Residential" }, Category = "break-in", Tags = new[] { "homes" } },
        // ---- Cross-district
        new Entry { Id = "citywide-pursuit", Title = "Citywide pursuit", Side = PlayerSide.Hero, Kind = CrimeKind.Robbery, Deadline = 480f, Build = CitywidePursuit, Districts = new[] { "Downtown" }, Category = "chase", CrossDistrict = true },
        new Entry { Id = "emergency-relay", Title = "Emergency relay", Side = PlayerSide.Hero, Kind = CrimeKind.Mugging, Deadline = 540f, Build = EmergencyRelay, Category = "emergency", CrossDistrict = true },
        new Entry { Id = "multi-point-heist", Title = "Multi-point heist", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 600f, Build = MultiPointHeist, Category = "heist", MinBand = 1, CrossDistrict = true },
        new Entry { Id = "cross-town-getaway", Title = "Cross-town getaway", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 540f, Build = CrossTownGetaway, Category = "chase", CrossDistrict = true },
    };
    static MissionPoint Far(string id, string notIn = null, string tag = null) => new MissionPoint { Id = id, Placement = PointPlacement.OtherDistrict, NotInDistrictsOf = notIn, DistrictTag = tag };
    // ================================================================ DOWNTOWN
    /// Street-level emergency at a tower: break open the blocked exits, hold the lobby while attackers come from two
    /// streets, stop their leader, then walk the survivors to triage.
    static void HighrisePanic(StagedScenario s)
    {
        s.Hint = "Panic at the tower. Smash the rubble off the blocked exits, hold the lobby while attackers come from both streets, drop their leader, then lead the survivors to triage.";
        s.Points = new[] { P("lobby", 0, 0), P("exit-n", 20, 14), P("exit-s", 200, 14), P("approach-e", 90, 30), P("approach-w", 270, 30), P("triage", 150, 38) };
        s.Targets = new[] { new TargetGroupSpec { Id = "rubble", Kind = TargetKind.Hardpoint, Count = 2, AtPoint = "exit-n,exit-s", Health = 90f } };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "evacuees", Role = NpcRole.Civilian, Count = 4, AtPoint = "lobby", Ring = 3f, HealthMultiplier = 3f, Speed = 3.5f, Behavior = ActorBehavior.Idle, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "attackers", Role = NpcRole.Criminal, Count = 2, AtPoint = "approach-e,approach-w", Ring = 2f, Speed = 4.5f, Behavior = ActorBehavior.Harass, BehaviorArgument = "evacuees", SpawnAtStart = false },
            new ActorGroupSpec { Id = "leader", Role = NpcRole.Criminal, Archetype = Enemy("Brute"), Count = 1, AtPoint = "approach-e", Ring = 3f, HealthMultiplier = 2f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false, ScaleWithDifficulty = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "REACH THE TOWER", point: "lobby").With(x => x.Radius = 14f),
            Stage(StageKind.DestroyTargets, "CLEAR THE BLOCKED EXITS", "rubble", timeout: 60f).Start(Hint("The exits are blocked - smash the rubble.")),
            Stage(StageKind.ProtectActors, "HOLD THE LOBBY", "evacuees").Start(Spawn("attackers", 2), Hint("Attackers from both streets - keep the evacuees alive."))
                .With(x => { x.Seconds = 35f; x.AllowedLosses = 1; x.RepeatGroup = "attackers"; x.RepeatSeconds = 6f; x.RepeatMaxAlive = 4; }),
            Stage(StageKind.DefeatTargets, "STOP THE ATTACK LEADER", "leader").Start(Spawn("leader", 1)),
            Stage(StageKind.EscortActors, "LEAD SURVIVORS TO TRIAGE", "evacuees", "triage").Start(Behave("evacuees", ActorBehavior.Follow), Hint("Walk up to the survivors; they follow you to triage."))
                .With(x => { x.Radius = 5f; x.AllowedLosses = 1; }),
        };
        s.Bonuses = new[] { Bonus("NOBODY LOST", BonusKind.NoLosses, 0f, 50, "evacuees") };
    }
    /// Take out the cameras, download at the terminal, grab the prototype, exfiltrate through ANOTHER district and shake the
    /// response that follows you.
    static void CorporateRaid(StagedScenario s)
    {
        s.Hint = "Knock out both security cameras, download the prototype at the server terminal (hold R), grab it, exfiltrate through another district and lose the pursuit.";
        s.Points = new[] { P("lobby-cam", 40, 16), P("roof-cam", 220, 16), P("server", 130, 10), Far("exfil") };
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "cameras", Kind = TargetKind.Hardpoint, Count = 2, AtPoint = "lobby-cam,roof-cam", Health = 80f },
            new TargetGroupSpec { Id = "terminal", Kind = TargetKind.Hardpoint, Count = 1, AtPoint = "server", Health = 999f },
            new TargetGroupSpec { Id = "prototype", Kind = TargetKind.Pickup, Count = 1, SpawnAtStart = false },
        };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "security", Role = NpcRole.Cop, Count = 3, AtPoint = "server", Ring = 4f, Behavior = ActorBehavior.HoldPost },
            new ActorGroupSpec { Id = "response", Role = NpcRole.Cop, Count = 2, Ring = 16f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.DestroyTargets, "KNOCK OUT SECURITY", "cameras", timeout: 70f),
            Stage(StageKind.InteractTargets, "DOWNLOAD THE PROTOTYPE", "terminal", timeout: 60f).Then(Drop("prototype", "terminal"), Heat(1f), Spawn("response", 2, "$player")),
            Stage(StageKind.CollectItems, "TAKE THE PROTOTYPE", "prototype", timeout: 30f),
            Stage(StageKind.ReachArea, "EXFILTRATE THROUGH ANOTHER DISTRICT", point: "exfil", timeout: 200f).With(x => x.Radius = 8f).Start(Hint("Exfiltrate - the extraction point is in another district.")),
            Stage(StageKind.LosePursuit, "LOSE THE PURSUIT", timeout: 150f).With(x => x.Seconds = 3f),
        };
        s.Bonuses = new[] { Bonus("UNTOUCHED", BonusKind.NoDamageTaken, 0f, 40) };
    }
    // ================================================================ DOCKS
    /// Arriving smuggler trucks: stop them, drop the guards that pour out, then secure three cargo containers (hold R) while
    /// an extraction team keeps undoing your work.
    static void SmugglerIntercept(StagedScenario s)
    {
        s.Hint = "Smuggler trucks are arriving at the quay. Stop them, drop the guards, then secure the three cargo containers (hold R) before the extraction team undoes it.";
        s.VehicleSpeed = 8f; s.VehicleEscapeDistance = 90f;
        s.Points = new[] { P("berth", 60, 24) };
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "trucks", Kind = TargetKind.Vehicle, Count = 2, Ring = 5f },
            new TargetGroupSpec { Id = "cargo", Kind = TargetKind.Hardpoint, Count = 3, AtPoint = "berth", Ring = 4f, Health = 999f, SpawnAtStart = false },
        };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "guards", Role = NpcRole.Criminal, Count = 3, Ring = 3f, HealthMultiplier = 1.2f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
            new ActorGroupSpec { Id = "extractors", Role = NpcRole.Criminal, Count = 1, AtPoint = "berth", Ring = 10f, Speed = 4.5f, Behavior = ActorBehavior.Harass, BehaviorArgument = "cargo", SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "GET TO THE QUAY", "trucks").With(x => x.Radius = 30f),
            Stage(StageKind.StopVehicles, "STOP THE SMUGGLER TRUCKS", "trucks").Then(Spawn("guards", 3, "trucks")),
            Stage(StageKind.DefeatTargets, "DROP THE GUARDS", "guards").Then(Drop("cargo", "berth")),
            Stage(StageKind.InteractTargets, "SECURE THE CARGO BEFORE EXTRACTION", "cargo", timeout: 60f).Start(Hint("Extraction team inbound - secure every container."))
                .With(x => { x.RepeatGroup = "extractors"; x.RepeatSeconds = 7f; x.RepeatMaxAlive = 3; }),
        };
        s.Bonuses = new[] { Bonus("UNDER 120 S", BonusKind.UnderSeconds, 120f, 40) };
    }
    /// Cut three alarm points, steal four crates from two yards while dock guards hold their posts and a response arrives,
    /// then reach the boat.
    static void DockyardScore(StagedScenario s)
    {
        s.Hint = "Cut the three yard alarms, steal four crates from the two yards, then get to the boat at the quay.";
        s.Points = new[] { P("alarm-a", 30, 20), P("alarm-b", 150, 22), P("alarm-c", 270, 20), P("yard-a", 80, 14), P("yard-b", 250, 14), P("boat", 0, 40) };
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "alarms", Kind = TargetKind.Hardpoint, Count = 3, AtPoint = "alarm-a,alarm-b,alarm-c", Health = 70f },
            new TargetGroupSpec { Id = "crates", Kind = TargetKind.Pickup, Count = 4, AtPoint = "yard-a,yard-b", Ring = 3f },
        };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "dock-guards", Role = NpcRole.Cop, Count = 2, AtPoint = "yard-a,yard-b", Ring = 4f, Behavior = ActorBehavior.HoldPost },
            new ActorGroupSpec { Id = "response", Role = NpcRole.Cop, Count = 1, AtPoint = "boat", Ring = 12f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.DestroyTargets, "CUT THE ALARMS", "alarms", timeout: 70f),
            Stage(StageKind.CollectItems, "STEAL THE CARGO", "crates", timeout: 60f).Start(Hint("Alarms down - grab the crates."))
                .With(x => { x.RepeatGroup = "response"; x.RepeatSeconds = 8f; x.RepeatMaxAlive = 3; }),
            Stage(StageKind.ReachArea, "GET TO THE BOAT", point: "boat", timeout: 60f).With(x => x.Radius = 5f).Start(Heat(1f)),
        };
        s.Bonuses = new[] { Bonus("QUIET (MAX 1 STAR)", BonusKind.MaxHeatStars, 1f, 50) };
    }
    // ================================================================ PARK
    /// Three separated crowds, attackers from three gates, then the ringleader runs for a gate.
    static void PublicEventAttack(StagedScenario s)
    {
        s.Hint = "An attack on the park festival. Protect the three crowds while attackers come through three gates, drop them, then catch the ringleader before he reaches a gate.";
        s.Points = new[] { P("stage", 0, 12), P("picnic", 120, 16), P("fountain", 240, 16), P("gate-n", 30, 45), P("gate-e", 150, 45), P("gate-w", 270, 45) };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "crowd-a", Role = NpcRole.Civilian, Count = 2, AtPoint = "stage", Ring = 2f, HealthMultiplier = 3f, Behavior = ActorBehavior.Idle, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "crowd-b", Role = NpcRole.Civilian, Count = 2, AtPoint = "picnic", Ring = 2f, HealthMultiplier = 3f, Behavior = ActorBehavior.Idle, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "crowd-c", Role = NpcRole.Civilian, Count = 2, AtPoint = "fountain", Ring = 2f, HealthMultiplier = 3f, Behavior = ActorBehavior.Idle, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "attackers", Role = NpcRole.Criminal, Count = 3, AtPoint = "gate-n,gate-e,gate-w", Ring = 2f, Speed = 4.5f, Behavior = ActorBehavior.Harass, BehaviorArgument = "crowd-a,crowd-b,crowd-c", SpawnAtStart = false },
            new ActorGroupSpec { Id = "ringleader", Role = NpcRole.Criminal, Count = 1, AtPoint = "stage", Ring = 3f, HealthMultiplier = 1.5f, Speed = 4.8f, Behavior = ActorBehavior.Flee, BehaviorArgument = "gate-n,gate-e,gate-w", SpawnAtStart = false, ScaleWithDifficulty = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "GET TO THE FESTIVAL", point: "stage").With(x => x.Radius = 25f),
            Stage(StageKind.ProtectActors, "PROTECT THE CROWDS", "crowd-a,crowd-b,crowd-c").Start(Spawn("attackers", 3), Hint("Attackers at three gates - keep all three crowds alive."))
                .With(x => { x.Seconds = 40f; x.AllowedLosses = 1; x.RepeatGroup = "attackers"; x.RepeatSeconds = 5f; x.RepeatMaxAlive = 5; }),
            Stage(StageKind.DefeatTargets, "DROP THE ATTACKERS", "attackers"),
            Stage(StageKind.ChaseExit, "CATCH THE RINGLEADER", "ringleader", timeout: 70f).Start(Spawn("ringleader", 1), Hint("The ringleader is running for a gate!")),
        };
        s.Bonuses = new[] { Bonus("NOBODY LOST", BonusKind.NoLosses, 0f, 50, "crowd-a,crowd-b,crowd-c") };
    }
    /// Wreck three separated fixtures, push Heat to three stars on purpose, drop the courier's escort, crack the case and
    /// lose the pursuit you created.
    static void ParkChaos(StagedScenario s)
    {
        s.Hint = "Wreck the kiosk, bandstand and fountain, push the Heat to three stars so the courier's escort shows, drop them, crack the case (hold R) and lose the pursuit.";
        s.Points = new[] { P("kiosk", 40, 20), P("bandstand", 160, 22), P("fountain", 280, 20), P("courier", 0, 6) };
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "fixtures", Kind = TargetKind.Hardpoint, Count = 3, AtPoint = "kiosk,bandstand,fountain", Health = 60f },
            new TargetGroupSpec { Id = "case", Kind = TargetKind.Hardpoint, Count = 1, AtPoint = "courier", Health = 999f },
        };
        s.Actors = new[] { new ActorGroupSpec { Id = "escort", Role = NpcRole.Cop, Count = 3, AtPoint = "courier", Ring = 3f, Behavior = ActorBehavior.HoldPost, SpawnAtStart = false } };
        s.Stages = new[]
        {
            Stage(StageKind.DestroyTargets, "WRECK THE PARK", "fixtures", timeout: 90f),
            Stage(StageKind.RaiseHeat, "DRAW THE HEAT", timeout: 60f).With(x => x.Count = 3).Then(Spawn("escort", 3)),
            Stage(StageKind.DefeatTargets, "DROP THE COURIER'S ESCORT", "escort"),
            Stage(StageKind.InteractTargets, "CRACK THE CASE", "case", timeout: 40f),
            Stage(StageKind.LosePursuit, "LOSE THE PURSUIT", timeout: 150f).With(x => x.Seconds = 3f),
        };
        s.Bonuses = new[] { Bonus("UNDER 150 S", BonusKind.UnderSeconds, 150f, 40) };
    }
    // ================================================================ RESIDENTIAL
    /// Raiders besiege three houses; one may fall. Then the enforcer; then walk the families to shelter (none may be lost).
    static void NeighbourhoodSiege(StagedScenario s)
    {
        s.Hint = "Raiders are breaking into the houses. Keep them standing (one may fall), beat the enforcer, then walk the families to the shelter - every one of them.";
        s.Points = new[] { P("street", 0, 0), P("house-a", 60, 14), P("house-b", 180, 14), P("house-c", 300, 14), P("shelter", 120, 40) };
        s.Targets = new[] { new TargetGroupSpec { Id = "houses", Kind = TargetKind.Hardpoint, Count = 3, AtPoint = "house-a,house-b,house-c", Health = 120f, Besiegeable = true } };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "families", Role = NpcRole.Civilian, Count = 3, Ring = 2.5f, HealthMultiplier = 3f, Speed = 3.5f, Behavior = ActorBehavior.Idle, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "raiders", Role = NpcRole.Criminal, Count = 3, AtPoint = "house-a,house-b,house-c", Ring = 6f, Speed = 4.5f, Behavior = ActorBehavior.Harass, BehaviorArgument = "houses", SpawnAtStart = false },
            new ActorGroupSpec { Id = "enforcer", Role = NpcRole.Criminal, Archetype = Enemy("Brute"), Count = 1, AtPoint = "house-b", Ring = 4f, HealthMultiplier = 2.5f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false, ScaleWithDifficulty = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "REACH THE STREET", point: "street").With(x => x.Radius = 30f),
            Stage(StageKind.DefendTargets, "SAVE THE HOUSES", "houses").Start(Spawn("raiders", 3), Hint("They're breaking in - keep the houses standing."))
                .With(x => { x.Seconds = 40f; x.AllowedLosses = 1; x.RepeatGroup = "raiders"; x.RepeatSeconds = 6f; x.RepeatMaxAlive = 4; }),
            Stage(StageKind.DefeatTargets, "BEAT THE ENFORCER", "enforcer").Start(Spawn("enforcer", 1)),
            Stage(StageKind.EscortActors, "WALK THE FAMILIES TO SHELTER", "families", "shelter", timeout: 150f).Start(Behave("families", ActorBehavior.Follow)).With(x => { x.Radius = 5f; x.AllowedLosses = 0; }),
        };
        s.Bonuses = new[] { Bonus("UNTOUCHED", BonusKind.NoDamageTaken, 0f, 40) };
    }
    /// Four look-alike houses, one real safehouse (seeded): every wrong door raises Heat and wakes a defender. The real one
    /// brings out its guards; take the ledger and slip away.
    static void SafehouseBreakIn(StagedScenario s)
    {
        s.Hint = "One of these four houses is the gang's safehouse. Try the doors (hold R) - each wrong one wakes a defender - then clear the guards, take the ledger and slip away.";
        s.Points = new[] { P("h1", 30, 18), P("h2", 110, 18), P("h3", 200, 18), P("h4", 290, 18) };
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "doors", Kind = TargetKind.Hardpoint, Count = 4, AtPoint = "h1,h2,h3,h4", Health = 999f },
            new TargetGroupSpec { Id = "ledger", Kind = TargetKind.Pickup, Count = 1, SpawnAtStart = false },
        };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "defenders", Role = NpcRole.Cop, Count = 1, Ring = 2.5f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
            new ActorGroupSpec { Id = "guards", Role = NpcRole.Cop, Count = 2, Ring = 4f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.SearchTargets, "FIND THE SAFEHOUSE", "doors", timeout: 150f)
                .With(x => x.OnDecoy = new[] { Spawn("defenders", 1, "$here"), Heat(.5f), Hint("Wrong house - and now they know you're here.") })
                .Then(Spawn("guards", 2, "$player"), Drop("ledger", "$player")),
            Stage(StageKind.DefeatTargets, "CLEAR THE GUARDS", "guards"),
            Stage(StageKind.CollectItems, "TAKE THE LEDGER", "ledger", timeout: 40f),
            Stage(StageKind.LosePursuit, "SLIP AWAY", timeout: 150f).Start(Heat(1f)).With(x => x.Seconds = 3f),
        };
        s.Bonuses = new[] { Bonus("FIRST DOOR", BonusKind.MaxHeatStars, 1f, 40) };
    }
    // ================================================================ CROSS-DISTRICT
    /// The chase starts at a crash here and the driver runs for a hideout in ANOTHER district; catch him, then raid it.
    static void CitywidePursuit(StagedScenario s)
    {
        s.Hint = "A getaway driver crashed and is running for the gang's hideout across town. Catch him before he gets there, then raid the hideout.";
        s.Points = new[] { Far("hideout") };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "driver", Role = NpcRole.Criminal, Count = 1, Ring = 2f, HealthMultiplier = .8f, Speed = 5.5f, Behavior = ActorBehavior.HoldPost, BehaviorArgument = "hideout", ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "crew", Role = NpcRole.Criminal, Count = 3, AtPoint = "hideout", Ring = 3f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "GET TO THE CRASH", "driver").With(x => x.Radius = 20f).Then(Behave("driver", ActorBehavior.Flee), Hint("He's running across town - catch him!")),
            Stage(StageKind.ChaseExit, "CATCH THE DRIVER BEFORE THE HIDEOUT", "driver", timeout: 180f),
            Stage(StageKind.ReachArea, "RAID THE HIDEOUT", point: "hideout", timeout: 200f).With(x => x.Radius = 10f),
            Stage(StageKind.DefeatTargets, "TAKE DOWN THE CREW", "crew").Start(Spawn("crew", 3)),
        };
    }
    /// Three calls in three districts, in order: a mugging here, a gas leak elsewhere, a trapped family in a third.
    static void EmergencyRelay(StagedScenario s)
    {
        s.Hint = "Three emergencies in three districts. Stop the mugging here, then shut the gas valve across town, then protect a trapped family in a third district.";
        s.Points = new[] { Far("leak"), Far("family", "leak") };
        s.Targets = new[] { new TargetGroupSpec { Id = "valve", Kind = TargetKind.Hardpoint, Count = 1, AtPoint = "leak", Health = 999f } };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "muggers", Role = NpcRole.Criminal, Count = 2, Ring = 3f, Behavior = ActorBehavior.Pursue },
            new ActorGroupSpec { Id = "trapped", Role = NpcRole.Civilian, Count = 2, AtPoint = "family", Ring = 2f, HealthMultiplier = 3f, Behavior = ActorBehavior.Idle, SpawnAtStart = false, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "looters", Role = NpcRole.Criminal, Count = 2, AtPoint = "family", Ring = 10f, Behavior = ActorBehavior.Harass, BehaviorArgument = "trapped", SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.DefeatTargets, "STOP THE MUGGING", "muggers", timeout: 90f),
            Stage(StageKind.ReachArea, "RESPOND: GAS LEAK", point: "leak", timeout: 200f).With(x => x.Radius = 8f).Start(Hint("Next call: a gas leak across town.")),
            Stage(StageKind.InteractTargets, "SHUT THE VALVE", "valve", timeout: 40f),
            Stage(StageKind.ReachArea, "RESPOND: TRAPPED FAMILY", point: "family", timeout: 200f).With(x => x.Radius = 10f).Start(Spawn("trapped", 2), Hint("Last call: a family trapped in another district.")),
            Stage(StageKind.ProtectActors, "KEEP THE FAMILY SAFE", "trapped").Start(Spawn("looters", 2)).With(x => { x.Seconds = 20f; x.AllowedLosses = 0; }),
        };
        s.Bonuses = new[] { Bonus("UNDER 4 MIN", BonusKind.UnderSeconds, 240f, 60) };
    }
    /// Two distractions in two other districts, then the real job back here, then lose the pursuit.
    static void MultiPointHeist(StagedScenario s)
    {
        s.Hint = "Stage two distractions in two different districts, then hit the real vault back here while the police are spread out, and lose the pursuit.";
        s.Points = new[] { Far("decoy-1"), Far("decoy-2", "decoy-1"), P("vault", 0, 10) };
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "charge-1", Kind = TargetKind.Hardpoint, Count = 1, AtPoint = "decoy-1", Health = 60f },
            new TargetGroupSpec { Id = "charge-2", Kind = TargetKind.Hardpoint, Count = 1, AtPoint = "decoy-2", Health = 60f },
            new TargetGroupSpec { Id = "vault", Kind = TargetKind.Hardpoint, Count = 1, AtPoint = "vault", Health = 300f },
        };
        s.Actors = new[] { new ActorGroupSpec { Id = "vault-guards", Role = NpcRole.Cop, Count = 2, AtPoint = "vault", Ring = 4f, Behavior = ActorBehavior.HoldPost, SpawnAtStart = false } };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "DISTRACTION ONE", point: "decoy-1", timeout: 200f).With(x => x.Radius = 10f),
            Stage(StageKind.DestroyTargets, "BLOW THE FIRST DECOY", "charge-1", timeout: 40f).Then(Heat(1f)),
            Stage(StageKind.ReachArea, "DISTRACTION TWO", point: "decoy-2", timeout: 200f).With(x => x.Radius = 10f),
            Stage(StageKind.DestroyTargets, "BLOW THE SECOND DECOY", "charge-2", timeout: 40f).Then(Heat(1f)),
            Stage(StageKind.ReachArea, "HIT THE REAL TARGET", point: "vault", timeout: 220f).With(x => x.Radius = 10f).Start(Spawn("vault-guards", 2)),
            Stage(StageKind.DestroyTargets, "CRACK THE VAULT", "vault", timeout: 60f),
            Stage(StageKind.LosePursuit, "LOSE THE PURSUIT", timeout: 150f).With(x => x.Seconds = 3f),
        };
        s.Bonuses = new[] { Bonus("LOW PROFILE (MAX 3 STARS)", BonusKind.MaxHeatStars, 3f, 60) };
    }
    /// Checkpoints across two other districts with a roadblock at the first and pursuit that grows at each, then back to
    /// the garage and lose the last tail.
    static void CrossTownGetaway(StagedScenario s)
    {
        s.Hint = "Run the getaway route: a checkpoint in another district (smash the roadblock), a second in a third district, back to the garage, then lose the last tail. The response grows at every checkpoint.";
        s.Points = new[] { Far("checkpoint-1"), Far("checkpoint-2", "checkpoint-1"), P("garage", 90, 20) };
        s.Targets = new[] { new TargetGroupSpec { Id = "roadblock", Kind = TargetKind.Hardpoint, Count = 2, AtPoint = "checkpoint-1", Ring = 3f, Health = 80f } };
        s.Actors = new[] { new ActorGroupSpec { Id = "pursuers", Role = NpcRole.Cop, Count = 2, Ring = 18f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false } };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "CHECKPOINT ONE", point: "checkpoint-1", timeout: 200f).With(x => x.Radius = 10f).Start(Heat(1f), Spawn("pursuers", 2, "$player")),
            Stage(StageKind.DestroyTargets, "SMASH THE ROADBLOCK", "roadblock", timeout: 40f),
            Stage(StageKind.ReachArea, "CHECKPOINT TWO", point: "checkpoint-2", timeout: 200f).With(x => x.Radius = 10f).Start(Heat(1f), Spawn("pursuers", 2, "$player")),
            Stage(StageKind.ReachArea, "BACK TO THE GARAGE", point: "garage", timeout: 220f).With(x => x.Radius = 8f).Start(Heat(1f), Spawn("pursuers", 3, "$player")),
            Stage(StageKind.LosePursuit, "LOSE THE LAST TAIL", timeout: 150f).With(x => x.Seconds = 5f),
        };
        s.Bonuses = new[] { Bonus("UNTOUCHED", BonusKind.NoDamageTaken, 0f, 60) };
    }
}
