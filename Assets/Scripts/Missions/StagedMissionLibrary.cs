using System;
using UnityEngine;

/// The staged missions as CODE-DEFINED DATA: StagedMissionSetup turns each entry into a StagedScenario asset plus an
/// EncounterDefinition under Resources (menu / batch, never by hand), and StagedMissionVerification reads the assets back.
/// After creation the assets are the source of truth for tuning; this file is the recipe. Rooftop Rescue is not here: NPCs
/// only stand on the ground NavMesh (rooftops have none), so a rooftop cast cannot exist without a NavMesh change (STATUS).
public static class StagedMissionLibrary
{
    public sealed class Entry
    {
        public string Id, Title; public PlayerSide Side; public CrimeKind Kind; public float Deadline; public Action<StagedScenario> Build;
        public string Asset => "staged-" + Id;
    }
    public static readonly Entry[] All =
    {
        new Entry { Id = "pursuit", Title = "Street pursuit", Side = PlayerSide.Hero, Kind = CrimeKind.Robbery, Deadline = 200f, Build = Pursuit },
        new Entry { Id = "convoy-intercept", Title = "Convoy intercept", Side = PlayerSide.Hero, Kind = CrimeKind.Robbery, Deadline = 200f, Build = ConvoyIntercept },
        new Entry { Id = "blackout", Title = "Blackout response", Side = PlayerSide.Hero, Kind = CrimeKind.Mugging, Deadline = 200f, Build = Blackout },
        new Entry { Id = "hold-the-block", Title = "Hold the block", Side = PlayerSide.Hero, Kind = CrimeKind.Mugging, Deadline = 220f, Build = HoldTheBlock },
        new Entry { Id = "armored-heist", Title = "Armoured strongroom", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 240f, Build = ArmoredHeist },
        new Entry { Id = "sabotage-run", Title = "Sabotage run", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 200f, Build = SabotageRun },
        new Entry { Id = "convoy-robbery", Title = "Armoured car robbery", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 240f, Build = ConvoyRobbery },
        new Entry { Id = "distraction", Title = "The distraction job", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 220f, Build = Distraction },
        new Entry { Id = "getaway", Title = "Getaway", Side = PlayerSide.Villain, Kind = CrimeKind.Robbery, Deadline = 200f, Build = Getaway },
    };
    public static Entry Find(string id) => Array.Find(All, e => e.Id == id);
    public static StagedScenario Create(Entry entry)
    {
        var s = ScriptableObject.CreateInstance<StagedScenario>(); s.name = entry.Asset;
        // Every shipped mission varies per spawn: layout rotated / mirrored, +1 hostile per 2 bands (max 3), timers 5% tighter
        // per band (floor 70%); band = successes / 2 in the session.
        s.RandomYaw = true; s.RandomMirror = true; s.DifficultyStep = 2; s.ExtraActorsPerBand = .5f; s.MaxExtraActors = 3; s.TimeoutScalePerBand = .95f; s.MinTimeoutScale = .7f;
        entry.Build(s);
        if (s.Bonuses.Length > 0) s.Hint += " Bonus: " + string.Join(", ", Array.ConvertAll(s.Bonuses, b => b.Label.ToLowerInvariant())) + ".";
        return s;
    }
    // ---------------------------------------------------------------- builders
    static MissionPoint P(string id, float angle, float distance) => new MissionPoint { Id = id, Angle = angle, Distance = distance };
    static StageAction Spawn(string group, int count, string anchor = null) => new StageAction { Kind = StageActionKind.SpawnActors, Group = group, Amount = count, Text = anchor };
    static StageAction Drop(string group, string anchor) => new StageAction { Kind = StageActionKind.SpawnTargets, Group = group, Text = anchor };
    static StageAction Behave(string group, ActorBehavior behavior) => new StageAction { Kind = StageActionKind.SetBehavior, Group = group, Behavior = behavior };
    static StageAction Heat(float stars) => new StageAction { Kind = StageActionKind.AddHeat, Amount = stars };
    static StageAction Hint(string text) => new StageAction { Kind = StageActionKind.Hint, Text = text };
    static MissionStageSpec Stage(StageKind kind, string label, string group = null, string point = null, float timeout = 0f)
        => new MissionStageSpec { Kind = kind, Label = label, Group = group, Point = point, Timeout = timeout };
    static MissionStageSpec Start(this MissionStageSpec s, params StageAction[] onStart) { s.OnStart = onStart; return s; }
    static BonusObjective Bonus(string label, BonusKind kind, float value = 0f, int xp = 30, string group = null) => new BonusObjective { Label = label, Kind = kind, Value = value, RewardXp = xp, Group = group };
    static MissionStageSpec Then(this MissionStageSpec s, params StageAction[] onComplete) { s.OnComplete = onComplete; return s; }
    static MissionStageSpec With(this MissionStageSpec s, Action<MissionStageSpec> set) { set(s); return s; }
    static EnemyArchetype Enemy(string name) => Resources.Load<EnemyArchetype>("Enemies/" + name);
    // ---------------------------------------------------------------- HERO
    /// Chase: the crew scatters to different exits once you arrive and re-routes when cut off; the bag drops where the last
    /// runner falls; return it. Speed, Ice / Poison roots and positioning matter; nothing here is a hold-R loop.
    static void Pursuit(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("UNDER 70 S", BonusKind.UnderSeconds, 70f, 40), Bonus("UNTOUCHED", BonusKind.NoDamageTaken, 0f, 30) };
        s.Hint = "A getaway crew crashed. Get close and they scatter for the exits; take each runner down (or freeze / root one and hold R to cuff). Cut one off and it changes route.";
        s.Points = new[] { P("exit-n", 0, 60), P("exit-e", 100, 60), P("exit-s", 190, 60), P("exit-w", 270, 60), P("bank", 0, 0) };
        s.Actors = new[] { new ActorGroupSpec { Id = "runners", Role = NpcRole.Criminal, Count = 3, Ring = 2.5f, HealthMultiplier = .6f, Speed = 4.6f, Behavior = ActorBehavior.HoldPost, BehaviorArgument = "exit-n,exit-e,exit-s,exit-w" } };
        s.Targets = new[] { new TargetGroupSpec { Id = "bag", Kind = TargetKind.Pickup, SpawnAtStart = false } };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "GET TO THE CRASH", "runners").With(x => x.Radius = 22f).Then(Behave("runners", ActorBehavior.Flee), Hint("They're running! Different exits - stop every one.")),
            Stage(StageKind.ChaseExit, "CATCH THE RUNNERS", "runners", timeout: 90f).Then(Drop("bag", "runners"), Hint("The loot bag fell where the last runner went down.")),
            Stage(StageKind.CollectItems, "RECOVER THE BAG", "bag", timeout: 45f),
            Stage(StageKind.ReachArea, "RETURN IT TO THE BANK", point: "bank", timeout: 60f).With(x => x.Radius = 5f),
        };
    }
    /// Vehicles: two hijacked trucks leave when you close in; stop both (Ice / heavy hit / wreck) before either escapes,
    /// then the gunmen pour out and the weapon crates only become breakable once the gunmen are down.
    static void ConvoyIntercept(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("UNDER 80 S", BonusKind.UnderSeconds, 80f, 40), Bonus("UNTOUCHED", BonusKind.NoDamageTaken, 0f, 30) };
        s.Hint = "Two hijacked trucks are about to leave. Stop both before either gets away: Ice freezes one, a heavy hit knocks it out, or wreck it. Then drop the gunmen and smash the weapon crates.";
        s.VehicleSpeed = 9f; s.VehicleEscapeDistance = 90f;
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "trucks", Kind = TargetKind.Vehicle, Count = 2, Ring = 5f },
            new TargetGroupSpec { Id = "crates", Kind = TargetKind.Hardpoint, Count = 2, Ring = 3f, Health = 80f, SpawnAtStart = false },
        };
        s.Actors = new[] { new ActorGroupSpec { Id = "gunmen", Role = NpcRole.Criminal, Count = 3, Ring = 3f, HealthMultiplier = 1.2f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false } };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "INTERCEPT THE CONVOY", "trucks").With(x => x.Radius = 30f),
            Stage(StageKind.StopVehicles, "STOP THE TRUCKS", "trucks").Start(Hint("They're rolling - stop both trucks!")).Then(Spawn("gunmen", 3, "trucks"), Drop("crates", "trucks")),
            Stage(StageKind.DefeatTargets, "DROP THE GUNMEN", "gunmen"),
            Stage(StageKind.DestroyTargets, "SMASH THE WEAPON CRATES", "crates", timeout: 45f),
        };
    }
    /// Repair under harassment: restore three spread-out relays (hold R) while saboteurs undo restored ones; deal with them
    /// or race them. Lights on, looters are caught in the open.
    static void Blackout(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("UNDER 90 S", BonusKind.UnderSeconds, 90f, 30), Bonus("UNTOUCHED", BonusKind.NoDamageTaken, 0f, 30) };
        s.Hint = "The block's relays are down. Hold R at each one to restore it - a saboteur left standing next to a restored relay knocks it out again. Then clear the looters.";
        s.Targets = new[] { new TargetGroupSpec { Id = "relays", Kind = TargetKind.Hardpoint, Count = 3, Ring = 13f, Health = 999f } };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "saboteurs", Role = NpcRole.Criminal, Count = 2, Ring = 6f, Speed = 4f, Behavior = ActorBehavior.Harass, BehaviorArgument = "relays" },
            new ActorGroupSpec { Id = "looters", Role = NpcRole.Criminal, Count = 3, Ring = 9f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.InteractTargets, "RESTORE THE RELAYS", "relays", timeout: 120f),
            Stage(StageKind.DefeatTargets, "CLEAR THE LOOTERS", "looters").Start(Spawn("looters", 3), Hint("Lights are back - the looters are caught in the open.")),
        };
    }
    /// Defence then escort: once you arrive, raiders keep coming to hurt the residents (one loss allowed across the mission); beat the
    /// enforcer; the residents follow you once you walk up to them, and the raiders are still around.
    static void HoldTheBlock(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("NOBODY LOST", BonusKind.NoLosses, 0f, 50, "residents") };
        s.Hint = "Raiders are hitting the residents. Keep them alive until the patrol arrives, beat the gang's enforcer, then walk up to the residents and lead them to the shelter.";
        s.Points = new[] { P("shelter", 135, 34) };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "residents", Role = NpcRole.Civilian, Count = 3, Ring = 3f, Speed = 3.5f, HealthMultiplier = 3f, Behavior = ActorBehavior.Idle, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "raiders", Role = NpcRole.Criminal, Count = 2, Ring = 11f, Speed = 4.5f, Behavior = ActorBehavior.HoldPost, BehaviorArgument = "residents" },
            new ActorGroupSpec { Id = "enforcer", Role = NpcRole.Criminal, Archetype = Enemy("Brute"), Count = 1, Ring = 8f, HealthMultiplier = 2.5f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false, ScaleWithDifficulty = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "REACH THE BLOCK", "residents").With(x => x.Radius = 25f).Then(Behave("raiders", ActorBehavior.Harass), Hint("Here they come - keep the residents alive.")),
            Stage(StageKind.ProtectActors, "HOLD THE BLOCK", "residents").With(x => { x.Seconds = 40f; x.AllowedLosses = 1; x.RepeatGroup = "raiders"; x.RepeatSeconds = 5f; x.RepeatMaxAlive = 4; }),
            Stage(StageKind.DefeatTargets, "BEAT THE ENFORCER", "enforcer").Start(Spawn("enforcer", 1), Hint("Their enforcer is here. Put him down.")),
            Stage(StageKind.EscortActors, "LEAD THEM TO THE SHELTER", "residents", "shelter").Start(Behave("residents", ActorBehavior.Follow), Hint("Walk up to the residents; they follow you to the shelter."))
                .With(x => { x.Radius = 5f; x.AllowedLosses = 1; }),
        };
    }
    // ---------------------------------------------------------------- VILLAIN
    /// Gate + clock: the strongroom door only takes damage once its guards are down; breaching it raises Heat and brings a
    /// response team that keeps arriving while you grab the cash on a timer; then reach the pickup.
    static void ArmoredHeist(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("LOW PROFILE (MAX 2 STARS)", BonusKind.MaxHeatStars, 2f, 50), Bonus("UNDER 120 S", BonusKind.UnderSeconds, 120f, 30) };
        s.Hint = "An armoured strongroom. Its guards must go down before the door can be worked; breaching it brings a response team, so grab the cash fast and reach the pickup.";
        s.Points = new[] { P("street", 90, 30), P("pickup", 200, 55) };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "guards", Role = NpcRole.Cop, Count = 3, Ring = 4f, Behavior = ActorBehavior.HoldPost },
            new ActorGroupSpec { Id = "response", Role = NpcRole.Cop, Count = 2, AtPoint = "street", Ring = 3f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
        };
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "door", Kind = TargetKind.Hardpoint, Health = 300f },
            new TargetGroupSpec { Id = "cash", Kind = TargetKind.Pickup, Count = 3, Ring = 3f, SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.DefeatTargets, "TAKE OUT THE GUARDS", "guards"),
            Stage(StageKind.DestroyTargets, "BREACH THE STRONGROOM", "door", timeout: 60f).Start(Hint("The door is exposed - smash or burn through it.")).Then(Heat(1f), Spawn("response", 2), Drop("cash", "door")),
            Stage(StageKind.CollectItems, "GRAB THE CASH", "cash", timeout: 35f).Start(Hint("Alarm! Grab the cash before the response team boxes you in."))
                .With(x => { x.RepeatGroup = "response"; x.RepeatSeconds = 6f; x.RepeatMaxAlive = 4; }),
            Stage(StageKind.ReachArea, "GET TO THE PICKUP", point: "pickup", timeout: 60f).With(x => x.Radius = 5f),
        };
    }
    /// Route under per-stage timers: three nodes across the district, each exposed only while it is the current target
    /// (hits on the others are wasted), guarded posts, then get clear of the last one.
    static void SabotageRun(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("UNDER 90 S", BonusKind.UnderSeconds, 90f, 40) };
        s.Hint = "Knock out the three security nodes IN ORDER before each lockdown timer runs out - only the current node can be damaged - then get clear.";
        s.Points = new[] { P("node-a", 30, 24), P("node-b", 150, 28), P("node-c", 270, 26) };
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "node-a", Kind = TargetKind.Hardpoint, AtPoint = "node-a", Health = 120f },
            new TargetGroupSpec { Id = "node-b", Kind = TargetKind.Hardpoint, AtPoint = "node-b", Health = 120f },
            new TargetGroupSpec { Id = "node-c", Kind = TargetKind.Hardpoint, AtPoint = "node-c", Health = 160f },
        };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "techs-b", Role = NpcRole.Cop, Count = 2, AtPoint = "node-b", Ring = 3f, Behavior = ActorBehavior.HoldPost },
            new ActorGroupSpec { Id = "techs-c", Role = NpcRole.Cop, Count = 2, AtPoint = "node-c", Ring = 3f, Behavior = ActorBehavior.HoldPost },
        };
        s.Stages = new[]
        {
            Stage(StageKind.DestroyTargets, "HIT NODE A", "node-a", timeout: 45f),
            Stage(StageKind.DestroyTargets, "HIT NODE B", "node-b", timeout: 45f).Start(Hint("Node A down. Node B is exposed - move!")),
            Stage(StageKind.DestroyTargets, "HIT NODE C", "node-c", timeout: 45f).Start(Hint("Last node. The lockdown is closing.")).Then(Heat(2f)),
            Stage(StageKind.EscapeRadius, "GET CLEAR", point: "node-c", timeout: 45f).With(x => x.Radius = 60f),
        };
    }
    /// The vehicle must SURVIVE: stop the armoured car without wrecking it (a wreck loses the cargo and fails the crack
    /// stage), drop the escort that climbs out, hold R at the stopped car, take the bags and get away.
    static void ConvoyRobbery(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("UNTOUCHED", BonusKind.NoDamageTaken, 0f, 40) };
        s.Hint = "An armoured car is moving the city's cash. Stop it WITHOUT wrecking it (a wreck burns the cargo): Ice or one heavy hit. Drop the escort, hold R at the car to crack it, take the bags and get away.";
        s.VehicleSpeed = 9f; s.VehicleEscapeDistance = 90f;
        s.Targets = new[]
        {
            new TargetGroupSpec { Id = "car", Kind = TargetKind.Vehicle, Count = 1 },
            new TargetGroupSpec { Id = "bags", Kind = TargetKind.Pickup, Count = 2, Ring = 2.5f, SpawnAtStart = false },
        };
        s.Actors = new[] { new ActorGroupSpec { Id = "escort", Role = NpcRole.Cop, Count = 3, Ring = 3f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false } };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "FIND THE ARMOURED CAR", "car").With(x => x.Radius = 30f),
            Stage(StageKind.StopVehicles, "STOP THE ARMOURED CAR", "car").Start(Hint("It's moving - stop it, don't wreck it.")).Then(Spawn("escort", 3, "car")),
            Stage(StageKind.DefeatTargets, "DROP THE ESCORT", "escort"),
            Stage(StageKind.InteractTargets, "CRACK THE CARGO DOOR", "car", timeout: 40f).Then(Drop("bags", "car")),
            Stage(StageKind.CollectItems, "TAKE THE CASH", "bags", timeout: 30f),
            Stage(StageKind.EscapeRadius, "GET AWAY", timeout: 50f).Start(Heat(1f)).With(x => x.Radius = 55f),
        };
    }
    /// Heat as a tool: raise it on purpose, slip the cordon that answers, cross town to the real target and crack it.
    static void Distraction(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("UNDER 120 S", BonusKind.UnderSeconds, 120f, 40) };
        s.Hint = "Make a loud scene (reach 2 Heat stars) so the police converge here, slip out of their cordon, then crack the real target across town while they're busy.";
        s.Points = new[] { P("target", 200, 70) };
        s.Targets = new[] { new TargetGroupSpec { Id = "safe", Kind = TargetKind.Hardpoint, AtPoint = "target", Health = 999f } };
        s.Actors = new[] { new ActorGroupSpec { Id = "cordon", Role = NpcRole.Cop, Count = 3, Ring = 8f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false } };
        s.Stages = new[]
        {
            Stage(StageKind.RaiseHeat, "MAKE A SCENE", timeout: 60f).With(x => x.Count = 2).Then(Spawn("cordon", 3)),
            Stage(StageKind.EscapeRadius, "SLIP THE CORDON", timeout: 40f).Start(Hint("They took the bait. Get out of the cordon.")).With(x => x.Radius = 45f),
            Stage(StageKind.ReachArea, "GET TO THE REAL TARGET", point: "target").With(x => x.Radius = 5f),
            Stage(StageKind.InteractTargets, "CRACK THE SAFE", "safe", timeout: 30f),
        };
    }
    /// Escort under attack: walk up to the crew so they follow; hunters go for THEM (not you) and keep coming; then shake
    /// the pursuit that meets you at the van.
    static void Getaway(StagedScenario s)
    {
        s.Bonuses = new[] { Bonus("NOBODY LEFT BEHIND", BonusKind.NoLosses, 0f, 50, "crew"), Bonus("UNTOUCHED", BonusKind.NoDamageTaken, 0f, 30) };
        s.Hint = "Your crew is pinned with the take. Walk up to them so they follow you, protect them from the hunters on the way to the van, then shake the pursuit.";
        s.Points = new[] { P("van", 160, 40) };
        s.Actors = new[]
        {
            new ActorGroupSpec { Id = "crew", Role = NpcRole.Criminal, Count = 2, Ring = 2f, Speed = 4.2f, Behavior = ActorBehavior.Follow, ScaleWithDifficulty = false },
            new ActorGroupSpec { Id = "hunters", Role = NpcRole.Cop, Count = 2, Ring = 14f, Speed = 5f, Behavior = ActorBehavior.HoldPost, BehaviorArgument = "crew" },
            new ActorGroupSpec { Id = "pursuers", Role = NpcRole.Cop, Count = 2, AtPoint = "van", Ring = 12f, Behavior = ActorBehavior.Pursue, SpawnAtStart = false },
        };
        s.Stages = new[]
        {
            Stage(StageKind.ReachArea, "REACH YOUR CREW", "crew").With(x => x.Radius = 4f).Then(Behave("hunters", ActorBehavior.Harass), Hint("Hunters are going for your crew - get them to the van.")),
            Stage(StageKind.EscortActors, "GET THE CREW TO THE VAN", "crew", "van", timeout: 120f)
                .With(x => { x.Radius = 5f; x.AllowedLosses = 1; x.RepeatGroup = "hunters"; x.RepeatSeconds = 7f; x.RepeatMaxAlive = 3; }),
            Stage(StageKind.EscapeRadius, "SHAKE THE PURSUIT", point: "van", timeout: 45f).Start(Spawn("pursuers", 2), Heat(1f)).With(x => x.Radius = 60f),
        };
    }
}
