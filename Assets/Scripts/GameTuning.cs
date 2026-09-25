using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Overpowered/Game Tuning")]
public sealed class GameTuning : ScriptableObject
{
    public MovementSettings Movement = new MovementSettings();
    public CitySettings City = new CitySettings();
    public NpcSettings Npcs = new NpcSettings();
    public ProgressionSettings Progression = new ProgressionSettings();
    public HeatSettings Heat = new HeatSettings();
    public CrimeSettings Crimes = new CrimeSettings();
    public PropSettings Props = new PropSettings();
    public CameraSettings Camera = new CameraSettings();
    /// Game feel (hit pause, camera impulse, FOV kick, impact particles). Every feel value lives here; Camera above stays
    /// the camera authority (offset / look height / field of view).
    public FeelSettings Feel = new FeelSettings();
}
[Serializable] public sealed class MovementSettings
{
    public float WalkSpeed = PrototypeTuning.WalkSpeed, RunSpeed = PrototypeTuning.RunSpeed;
    public float JumpSpeed = PrototypeTuning.JumpSpeed, Gravity = PrototypeTuning.Gravity;
    public float FlightLift = PrototypeTuning.FlightLift, FlightForwardBoost = PrototypeTuning.FlightForwardBoost;
    public float TurnResponse = 14f, GroundStickSpeed = 2f, FlightResponse = 3f;
    public float Health = 100f, RespawnDelay = 3f, Energy = 100f, EnergyRecharge = 12f;
    public float KillPlane = -30f, Height = 1.8f, Radius = .38f;
}
[Serializable] public sealed class CitySettings
{
    public int Seed = 2409, Blocks = 3;
    public float BlockSize = 30f, StreetWidth = 10f, SidewalkWidth = 3f, SidewalkHeight = .12f;
    public Vector2 BuildingHeight = new Vector2(6f, 17f);
    public float LandmarkHeight = 28f, BuildingWidth = 10f, BuildingDepth = 10f, FloorThickness = 1f;
    public int Civilians = 24, RooftopPickups = 5;
    public float RooftopPickupRadius = 2f, RooftopPickupSize = .8f, RoofMarkerHeight = 1f;
    public Color RoadColor = new Color(.09f,.12f,.16f), SidewalkColor = new Color(.38f,.4f,.43f);
    public Color BuildingColor = new Color(.26f,.37f,.48f), RoofColor = new Color(.55f,.65f,.68f);
}
[Serializable] public sealed class NpcSettings
{
    public float CivilianSpeed = 1.8f, FleeSpeed = 5f, CopSpeed = 5.5f, HeroSpeed = 8f;
    public float CivilianHealth = 25f, CopHealth = 65f, HeroHealth = 200f, HealthPerStar = 18f;
    public float AttackDamage = 8f, HeroDamage = 18f, DamagePerStar = 2f, AttackRange = 2f, AttackCooldown = 1f;
    public float RepathSeconds = .35f, WanderSeconds = 3f, FleeSeconds = 7f, FleeDistance = 16f, AlarmRadius = 28f;
    public float Height = 1.8f, Radius = .35f, Acceleration = 20f, AngularSpeed = 360f, NavSampleRadius = 5f;
    public float DestroyDelay = 1.5f, MinimumWanderDistance = 2f, DetectionRange = 100f;
    public Color CivilianColor = new Color(.9f,.75f,.32f), CopColor = new Color(.08f,.2f,.65f), HeroColor = new Color(.85f,.15f,.15f);
}
[Serializable] public sealed class ProgressionSettings
{
    public int BaseLevelXp = 100, PointsPerLevel = 1, EnemyXp = 35, CivilianXp = 10, DestructionXp = 12, CrimeXp = 60, RooftopXp = 25;
    public float LevelXpGrowth = 1.35f, SideSwitchCooldown = 10f;
    public string SaveFilename = "overpowered-progression.json";
}
[Serializable] public sealed class HeatSettings
{
    public int MaximumStars = 5, FriendlyPatrolCount = 2, CopsPerStar = 2, HeroThreshold = 4;
    public float DestructionHeat = .35f, AssaultHeat = .25f, DefeatHeat = .5f, CrimeHeat = 1f;
    public float CrimeReduction = 1f, DecayDelay = 10f, DecayPerSecond = .07f, ResponseInterval = 2f, SpawnDistance = 25f;
}
[Serializable] public sealed class CrimeSettings
{
    public int MaximumActive = 3;
    public float SpawnInterval = 12f, ResolveRadius = 3f, MarkerHeight = 2.5f, MarkerSize = 1.2f;
    public float ChaosTarget = 4f, FireResolutionSeconds = 2f;
    public Color CrimeColor = new Color(1f,.3f,.04f);
}
[Serializable] public sealed class PropSettings
{
    public int HitsToBreak = 3, ShardCount = 5;
    public float Health = 80f, CrateMass = 45f, BarrelMass = 65f, CarMass = 400f, FurnitureMass = 80f;
    public float ShardMass = .35f, ShardSize = .25f, ShardSpread = .35f, ShardLifetime = 7f;
    public float ShardImpulseFraction = .01f, ShardRadius = 2.5f, ShardLift = .2f;
    public float CleanupBelow = -40f;
    public Vector3 CrateSize = Vector3.one * 1.1f, BarrelSize = new Vector3(.85f,.75f,.85f);
    public Vector3 CarSize = new Vector3(2f,1.3f,4f), BenchSize = new Vector3(2f,.6f,.7f);
    public Vector3 TrashSize = new Vector3(.6f,.5f,.6f), LampSize = new Vector3(.18f,2f,.18f);
}
[Serializable] public sealed class CameraSettings
{
    public float Sensitivity = 3.5f, Pitch = 16f, MinimumPitch = -10f, MaximumPitch = 65f, LookHeight = 1.05f;
    public Vector3 Offset = new Vector3(0,2.6f,-6.5f);
    public float CollisionRadius = .2f, CollisionInset = .1f, FieldOfView = 65f;
}
[Serializable] public sealed class FeelSettings
{
    [Header("Heavy hit definition")]
    [Tooltip("An outgoing CombatImpact.Blast that touches at least one body/NPC is HEAVY at or above this impulse (N·s). Super Strength punch 1350, kick 2025, synergies 1500-3400 = heavy; Forge basic melee 160 and Fire Blast 450 = light.")]
    public float HeavyImpulse = 1000f;
    [Tooltip("Damage landing on the player at or above this is HEAVY (Brute slam 18; Rusher 6 and Gunner 8 are light).")]
    public float HeavyIncomingDamage = 15f;
    [Header("Hit pause (real time)")]
    [Tooltip("Real seconds of the freeze on a heavy hit (target band 0.05-0.08).")]
    public float HitPauseSeconds = .06f;
    [Tooltip("timeScale held during the freeze.")]
    public float HitPauseTimeScale = 0f;
    [Tooltip("Minimum real seconds between hit-pause starts, so a crowd cannot chain freezes.")]
    public float HitPauseMinInterval = .4f;
    [Header("Camera impulse (additive, unscaled time)")]
    public float ImpulseAmplitude = .16f;
    public float ImpulseSeconds = .22f;
    public float ImpulseFrequency = 18f;
    [Tooltip("Incoming heavy hits shake at this fraction of ImpulseAmplitude.")]
    public float IncomingImpulseScale = .8f;
    [Header("FOV kick (unscaled time; does not stack with a synergy's own kick)")]
    public float FovKickDegrees = 2.5f;
    public float FovKickSeconds = .2f;
    [Header("Hard landing")]
    [Tooltip("SuperHeroController.Landed speed (m/s) at or above which a landing kicks the camera. A normal jump lands at ~8.5.")]
    public float HardLandingSpeed = 14f;
    [Tooltip("Hard landings shake/kick at this fraction of the hit values.")]
    public float LandingScale = .7f;
    [Header("Impact particles (fixed pool, shared cube mesh, shared palette materials)")]
    public int ParticlePoolSize = 8;
    public int LightHitParticles = 6, HeavyHitParticles = 14, BreakParticles = 12, LandingParticles = 8;
    public float ParticleSize = .13f, ParticleSizeJitter = .5f, ParticleLifetime = .55f, ParticleSpeed = 5f, ParticleGravity = 1.6f;
    public CityColor HitDebris = CityColor.Pavement, HeavyDebris = CityColor.Amber, BreakDebris = CityColor.Wood, LandingDebris = CityColor.Pavement;
}
