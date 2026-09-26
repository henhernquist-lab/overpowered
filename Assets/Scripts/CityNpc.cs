using UnityEngine;
using UnityEngine.AI;

public enum NpcRole { Civilian, Cop, PursuingHero, Criminal }
/// Hostile attack cycle: Approach (hold the ring/band, ask for a token) -> Engage (token held, closing in; melee/slam only)
/// -> Windup (committed: stopped, shape locked, telegraph shown) -> Release (re-check the committed shape) -> Recover (cooldown).
public enum AttackPhase { Approach, Engage, Windup, Recover }
public sealed class CityNpc : MonoBehaviour
{
    public NpcRole Role { get; private set; }
    /// Behaviour data (null for civilians). Role decides hostility/colour; the archetype decides movement and attack.
    public EnemyArchetype Archetype { get; private set; }
    public float Health { get; private set; }
    public float MaxHealth { get; private set; }
    /// Chase the player at any distance (ignores DetectionRange). Set by Endless waves.
    public bool AlwaysAggro;
    float explicitDamage = -1f;
    /// Damage of one attack (dealt at release, only if the committed shape still contains the player): the explicit
    /// per-spawn value when set, otherwise the Heat-star formula (standard enemy damage x archetype DamageMultiplier).
    public float ContactDamage => explicitDamage >= 0f ? explicitDamage : PoliceDamageScale * (Archetype != null ?
        (world.Tuning.Npcs.AttackDamage+world.Stars*world.Tuning.Npcs.DamagePerStar)*Archetype.DamageMultiplier :
        (Role==NpcRole.PursuingHero ? world.Tuning.Npcs.HeroDamage : world.Tuning.Npcs.AttackDamage)+world.Stars*world.Tuning.Npcs.DamagePerStar);
    /// Per-side police damage (GameTuning Heat.HeroPolice / VillainPolice); 1 for criminals and explicit (Endless) stats.
    float PoliceDamageScale => Role==NpcRole.Cop || Role==NpcRole.PursuingHero ? world.Tuning.Heat.Police(world.Progression.Data.Side).DamageMultiplier : 1f;
    /// Explicit combat stats for this NPC, replacing the Heat-star spawn formula (health) and attack formula (damage).
    public void SetCombatStats(float health, float damage) { Health = MaxHealth = Mathf.Max(1f, health); explicitDamage = Mathf.Max(0f, damage); }
    public bool Dead => Health <= 0f;
    public bool Frozen=>Time.time<frozenUntil;
    public bool Burning=>Time.time<burningUntil;
    float burningUntil;
    public void MarkBurn(float duration){burningUntil=Mathf.Max(burningUntil,Time.time+duration);}
    public bool Fleeing => Time.time < fleeUntil;
    public bool Hostile => Role == NpcRole.Criminal ? world.Progression.Data.Side == PlayerSide.Hero :
        (Role == NpcRole.Cop || Role == NpcRole.PursuingHero) && world.PoliceHostileTo(this);
    public NavMeshAgent Agent { get; private set; }
    public event System.Action<bool> Damaged;
    /// Fired at RELEASE, whether the committed shape hits or misses (audio/presentation stay in sync with the strike).
    public event System.Action Attacked;
    /// Fired when the NPC commits: stopped, facing and shape locked, telegraph shown.
    public event System.Action AttackWindupStarted;
    /// Fired when a pending windup is dropped without a release (death, freeze, pause, session end, side switch).
    public event System.Action AttackCanceled;
    /// Raised once a new NPC and its presentation are in the world, so listeners (audio) can track it immediately.
    public static event System.Action<CityNpc> Spawned;
    public AttackPhase Phase { get; private set; }
    public float WindupSeconds => Archetype != null ? Archetype.WindupSeconds : 0f;
    public float WindupStartTime { get; private set; } = -1f;
    public int WindupStartFrame { get; private set; } = -1;
    public float LastReleaseTime { get; private set; } = -1f;
    public int LastReleaseFrame { get; private set; } = -1;
    /// Release time - windup-start time of the last completed attack.
    public float LastMeasuredWindup { get; private set; } = -1f;
    public bool LastReleaseHit { get; private set; }
    /// Player's distance OUTSIDE the committed shape at the last release (<= 0: inside). Disc: horizontal distance to the
    /// disc edge minus the player radius; line: capsule-surface distance to the locked line minus the half-width.
    public float LastReleaseMargin { get; private set; }
    public int Windups { get; private set; }
    public int Releases { get; private set; }
    public int Hits { get; private set; }
    public int Cancels { get; private set; }
    /// Committed shape: disc centre on the ground (Melee/Slam) or muzzle point (Ranged), and the locked aim/facing.
    public Vector3 CommittedPoint { get; private set; }
    public Vector3 CommittedDirection { get; private set; }
    public AttackTelegraph Telegraph { get; private set; }
    HumanoidAnimationTuning animationTuning;
    public CrimeEvent Crime;
    public CrimeEncounter Encounter;
    WorldSession world; float nextPath, nextAttack, fleeUntil, frozenUntil, releaseAt, engageUntil, nextSight, combatSpeed; Vector3 alarm; int waypoint, circleSign=1; bool queued, sight;
    static readonly RaycastHit[] rayHits = new RaycastHit[16];
    EnemyRoster Roster => EnemyRoster.Current;
    public static CityNpc Spawn(WorldSession world, Vector3 position, NpcRole role) =>
        Spawn(world, position, role, role == NpcRole.Civilian || EnemyRoster.Current == null ? null : EnemyRoster.Current.For(role));
    public static CityNpc Spawn(WorldSession world, Vector3 position, NpcRole role, EnemyArchetype archetype)
    {
        var c = world.Tuning.Npcs;
        if (!NavMesh.SamplePosition(position, out var hit, c.NavSampleRadius, NavMesh.AllAreas)) return null;
        var root = new GameObject(role.ToString()); root.transform.position = hit.position;
        float scale = archetype != null ? archetype.VisualScale : 1f;
        var capsule = root.AddComponent<CapsuleCollider>(); capsule.height=c.Height*scale; capsule.radius=c.Radius*scale; capsule.center=Vector3.up*c.Height*scale*.5f;
        var npc = root.AddComponent<CityNpc>(); npc.world=world; npc.Role=role; npc.Archetype=archetype;
        float standard = c.CopHealth + world.Stars*c.HealthPerStar;
        npc.Health = role==NpcRole.Civilian ? c.CivilianHealth : archetype!=null ? standard*archetype.HealthMultiplier : role==NpcRole.PursuingHero ? c.HeroHealth : standard;
        npc.MaxHealth = npc.Health;
        npc.Agent=root.AddComponent<NavMeshAgent>(); npc.Agent.height=c.Height*scale; npc.Agent.radius=c.Radius*scale; npc.Agent.acceleration=c.Acceleration; npc.Agent.angularSpeed=c.AngularSpeed;
        npc.Agent.stoppingDistance=c.AttackRange*.5f;
        npc.circleSign=(npc.GetEntityId().GetHashCode()&1)==0?1:-1; npc.combatSpeed=archetype!=null?archetype.MoveSpeed:c.CopSpeed;
        npc.animationTuning=HumanoidPresentation.Create(root,c.Height,null,npc).Tuning;
        world.Npcs.Add(npc); Spawned?.Invoke(npc); return npc;
    }
    public void Alarm(Vector3 position) { alarm=position; fleeUntil=Time.time+world.Tuning.Npcs.FleeSeconds; nextPath=0; }
    public void Freeze(float duration) { frozenUntil=Mathf.Max(frozenUntil,Time.time+duration); if(duration>0f) CancelAttack(); }
    public void Damage(float amount, PowerUser source)
    {
        if (Dead || amount <= 0f) return;
        Health = Mathf.Max(0f, Health-amount);
        if (Dead) CancelAttack(); // a killed NPC mid-windup never deals its damage
        Damaged?.Invoke(Dead);
        if(source!=null) world.OnAssault(this);
        if (!Dead) return;
        Agent.enabled=false; GetComponent<Collider>().enabled=false;
        if(source!=null) world.OnDefeat(this); Crime?.CriminalDefeated();
        Destroy(gameObject,Mathf.Max(world.Tuning.Npcs.DestroyDelay,animationTuning.Death.length/animationTuning.ActionPlayback));
    }
    void Update()
    {
        if (world==null || Dead) return;
        if (!Agent.isOnNavMesh || (world.Mode!=null&&(world.Mode.Ended||world.Mode.Paused))) { CancelAttack(); return; }
        var c=world.Tuning.Npcs;
        Agent.isStopped=Time.time<frozenUntil || world.PlayerDead;
        if (Agent.isStopped) { CancelAttack(); return; }
        bool combatant=Archetype!=null && Hostile;
        if (!combatant) CancelAttack();
        if (Phase==AttackPhase.Windup) { TickWindup(); return; }
        Agent.speed=combatant ? combatSpeed : Role==NpcRole.Civilian ? (Fleeing ? c.FleeSpeed : c.CivilianSpeed) : Role==NpcRole.PursuingHero ? c.HeroSpeed : c.CopSpeed;
        float distance=Vector3.Distance(transform.position,world.Hero.transform.position);
        bool aggro=combatant && (AlwaysAggro || distance<c.DetectionRange);
        if (aggro) { if (TickAttack(distance, Encounter!=null)) return; }
        else Withdraw();
        if(Encounter!=null&&Encounter.Drive(this)) return;
        if (Time.time<nextPath) return;
        nextPath=Time.time+(Hostile || Fleeing ? c.RepathSeconds : c.WanderSeconds);
        if (aggro) { CombatMove(); return; }
        Vector3 destination;
        if (Hostile && (AlwaysAggro || distance<c.DetectionRange)) destination=world.Hero.transform.position;
        else if (Fleeing) destination=PanicDestination(transform.position+(transform.position-alarm).normalized*c.FleeDistance);
        else
        {
            if (!Agent.hasPath || Agent.remainingDistance<c.MinimumWanderDistance) waypoint=Random.Range(0,world.City.Sidewalks.Count);
            destination=world.City.Sidewalks[waypoint];
        }
        if (NavMesh.SamplePosition(destination,out var hit,c.NavSampleRadius,NavMesh.AllAreas)) Agent.SetDestination(hit.position);
    }
    /// Returns true when the attack cycle owns this frame's movement (engaging, or a windup just started).
    bool TickAttack(float distance, bool driven)
    {
        var a=Archetype; var tokens=world.AttackTokens;
        if (Phase==AttackPhase.Engage)
        {
            if (distance<=a.TriggerDistance) { StartWindup(); return true; }
            if (Time.time>engageUntil) { tokens.Release(this); Phase=AttackPhase.Approach; nextAttack=Time.time+Roster.RetrySeconds; return false; }
            Agent.speed=combatSpeed=a.MoveSpeed;
            if (Time.time>=nextPath) { nextPath=Time.time+world.Tuning.Npcs.RepathSeconds; Go(world.Hero.transform.position); }
            return true;
        }
        if (Time.time<nextAttack) { Withdraw(); return false; }
        Phase=AttackPhase.Approach;
        bool ready=a.Kind==AttackKind.Ranged ? distance<=a.Range && LineOfSight() :
            driven ? distance<=a.TriggerDistance : distance<=Mathf.Max(a.TriggerDistance, Roster.WaitRingRadius+Roster.RingSlack);
        if (!ready) { Withdraw(); return false; }
        queued=true;
        if (!tokens.TryAcquire(this)) return false;
        queued=false;
        if (a.Kind==AttackKind.Ranged || distance<=a.TriggerDistance) { StartWindup(); return true; }
        Phase=AttackPhase.Engage; engageUntil=Time.time+Roster.EngageTimeoutSeconds; nextPath=Time.time+world.Tuning.Npcs.RepathSeconds;
        Agent.speed=combatSpeed=a.MoveSpeed; Go(world.Hero.transform.position);
        return true;
    }
    void StartWindup()
    {
        var a=Archetype; Vector3 player=world.Hero.transform.position;
        Vector3 facing=player-transform.position; facing.y=0f;
        facing=facing.sqrMagnitude>.0001f ? facing.normalized : transform.forward;
        Phase=AttackPhase.Windup; Windups++;
        WindupStartTime=Time.time; WindupStartFrame=Time.frameCount; releaseAt=Time.time+a.WindupSeconds;
        Agent.isStopped=true; Agent.velocity=Vector3.zero;
        transform.rotation=Quaternion.LookRotation(facing); // facing is set once and then held; never animated
        if (Telegraph==null) Telegraph=new AttackTelegraph(transform, a.Kind==AttackKind.Ranged);
        if (a.Kind==AttackKind.Ranged)
        {
            CommittedPoint=Muzzle;
            Vector3 aim=Chest(player)-CommittedPoint;
            CommittedDirection=aim.sqrMagnitude>.0001f ? aim.normalized : facing;
            Telegraph.ShowLine(CommittedPoint, CommittedDirection, a.Range, a.TelegraphWidth);
        }
        else
        {
            CommittedPoint=transform.position+facing*a.Reach; CommittedDirection=facing;
            Telegraph.ShowDisc(CommittedPoint, a.Radius, a.TelegraphStartFraction);
        }
        AttackWindupStarted?.Invoke();
    }
    void TickWindup()
    {
        Agent.isStopped=true;
        if (Time.time<releaseAt) { Telegraph.Tick(Archetype.WindupSeconds>0f ? 1f-(releaseAt-Time.time)/Archetype.WindupSeconds : 1f); return; }
        Release();
    }
    void Release()
    {
        var a=Archetype;
        Phase=AttackPhase.Recover; nextAttack=Time.time+a.CooldownSeconds;
        world.AttackTokens.Release(this); queued=false; Telegraph.Hide();
        LastReleaseTime=Time.time; LastReleaseFrame=Time.frameCount; LastMeasuredWindup=Time.time-WindupStartTime; Releases++;
        LastReleaseMargin=ShapeMargin(); LastReleaseHit=LastReleaseMargin<=0f;
        Attacked?.Invoke();
        if (LastReleaseHit)
        {
            Hits++;
            float before=world.Health; world.DamagePlayer(ContactDamage);
            if (world.Health<before) FeelDirector.PlayerHit(before-world.Health, transform.position);   // feel only
            if (a.Knockback>0f && world.Health<before && !world.PlayerDead) StartCoroutine(Knockback(CommittedDirection, a.Knockback, a.KnockbackSeconds));
        }
        Agent.isStopped=false;
    }
    /// Distance of the player's CURRENT body outside the COMMITTED shape (<= 0 means hit).
    float ShapeMargin()
    {
        var a=Archetype; var hero=world.Hero.transform; var body=world.Hero.GetComponent<CharacterController>();
        float radius=body!=null ? body.radius : world.Tuning.Movement.Radius;
        if (a.Kind!=AttackKind.Ranged)
        {
            Vector3 flat=hero.position-CommittedPoint; float height=flat.y; flat.y=0f;
            if (height>a.MaxHitHeight || height<-a.MaxHitHeight) return Mathf.Max(flat.magnitude-a.Radius-radius, Mathf.Abs(height)-a.MaxHitHeight);
            return flat.magnitude-a.Radius-radius;
        }
        float half=body!=null ? Mathf.Max(0f, body.height*.5f-radius) : 0f;
        Vector3 centre=body!=null ? hero.TransformPoint(body.center) : hero.position+Vector3.up*world.Tuning.Movement.Height*.5f;
        Vector3 end=CommittedPoint+CommittedDirection*a.Range;
        float along=SegmentDistance(CommittedPoint, end, centre-Vector3.up*half, centre+Vector3.up*half, out float s);
        float margin=along-radius-a.Radius;
        if (margin<=0f && Blocked(CommittedPoint, CommittedDirection, s*a.Range)) return float.PositiveInfinity; // line of sight
        return margin;
    }
    System.Collections.IEnumerator Knockback(Vector3 direction, float distance, float seconds)
    {
        direction.y=0f; if (direction.sqrMagnitude<.0001f) yield break; direction.Normalize();
        var body=world.Hero.GetComponent<CharacterController>(); float moved=0f, speed=distance/seconds;
        while (moved<distance && body!=null && body.enabled && !world.PlayerDead)
        {
            // Small downward component keeps CharacterController ground contact (isGrounded), so a shoved player can still jump/backflip.
            float step=Mathf.Min(distance-moved, speed*Time.deltaTime); if (Time.deltaTime>0f) body.Move(direction*step+Vector3.down*world.Tuning.Movement.GroundStickSpeed*Time.deltaTime); moved+=step;   // frozen frame (hit pause): no zero Move, ground contact kept
            yield return null;
        }
    }
    void CancelAttack()
    {
        if (Phase!=AttackPhase.Windup && Phase!=AttackPhase.Engage) { Withdraw(); return; }
        bool windup=Phase==AttackPhase.Windup;
        Phase=AttackPhase.Approach; nextAttack=Mathf.Max(nextAttack, Time.time);
        if (world!=null) world.AttackTokens.Release(this); queued=false;
        Telegraph?.Hide();
        if (windup) { Cancels++; AttackCanceled?.Invoke(); }
    }
    void Withdraw() { if (queued) { world.AttackTokens.Withdraw(this); queued=false; } }
    /// Ring (melee/slam) or preferred band (ranged) around the player: approach or back off along the current bearing,
    /// otherwise circle slowly; neighbours on the same ring push each other apart by angle so they never stack.
    void CombatMove()
    {
        var a=Archetype; var r=Roster; Vector3 player=world.Hero.transform.position;
        Vector3 away=transform.position-player; away.y=0f; float d=away.magnitude;
        Vector3 bearing=d>.01f ? away/d : -transform.forward;
        bool ranged=a.Kind==AttackKind.Ranged;
        float ring=ranged ? a.PreferredDistance : r.WaitRingRadius, slack=ranged ? a.PreferredBand : r.RingSlack;
        float angle=Separation(bearing, ring, ranged, player);
        bool holding=d>=ring-slack && d<=ring+slack;
        if (holding) angle+=circleSign*r.CircleLeadDegrees;
        Agent.speed=combatSpeed=holding ? Mathf.Min(r.CircleSpeed, a.MoveSpeed) : a.MoveSpeed;
        Go(player+Quaternion.AngleAxis(angle, Vector3.up)*bearing*ring);
    }
    float Separation(Vector3 bearing, float ring, bool ranged, Vector3 player)
    {
        float minimum=Roster.RingSpacing/Mathf.Max(.1f, ring)*Mathf.Rad2Deg, push=0f;
        foreach (var other in world.Npcs)
        {
            if (other==this || other==null || other.Dead || other.Archetype==null || (other.Archetype.Kind==AttackKind.Ranged)!=ranged) continue;
            if (other.Phase==AttackPhase.Windup || other.Phase==AttackPhase.Engage || !other.Hostile) continue;
            Vector3 theirs=other.transform.position-player; theirs.y=0f; if (theirs.sqrMagnitude<.01f) continue;
            float delta=Vector3.SignedAngle(theirs, bearing, Vector3.up);
            if (Mathf.Abs(delta)<minimum) push+=(delta>=0f ? 1f : -1f)*(minimum-Mathf.Abs(delta));
        }
        return Mathf.Clamp(push, -60f, 60f);
    }
    void Go(Vector3 destination)
    {
        if (NavMesh.SamplePosition(destination,out var hit,world.Tuning.Npcs.NavSampleRadius,NavMesh.AllAreas)) Agent.SetDestination(hit.position);
    }
    Vector3 Muzzle => transform.position+Vector3.up*Archetype.MuzzleHeight*Archetype.VisualScale;
    Vector3 Chest(Vector3 feet) => feet+Vector3.up*world.Tuning.Movement.Height*.7f;
    bool LineOfSight()
    {
        if (Time.time<nextSight) return sight;
        nextSight=Time.time+world.Tuning.Npcs.RepathSeconds;
        Vector3 from=Muzzle, to=Chest(world.Hero.transform.position)-from;
        sight=!Blocked(from, to, to.magnitude);
        return sight;
    }
    /// Anything solid except NPC capsules and the hero blocks a shot (buildings, props, cars).
    bool Blocked(Vector3 from, Vector3 direction, float distance)
    {
        if (distance<=.01f || direction.sqrMagnitude<.0001f) return false;
        int count=Physics.RaycastNonAlloc(from, direction.normalized, rayHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        var hero=world.Hero.transform;
        for (int i=0;i<count;i++)
        {
            var hit=rayHits[i].collider;
            if (hit.TryGetComponent<CityNpc>(out _) || hit.transform==hero || hit.transform.IsChildOf(hero)) continue;
            return true;
        }
        return false;
    }
    /// Closest distance between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9); s is the parameter on p1-q1.
    static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s)
    {
        Vector3 d1=q1-p1, d2=q2-p2, r=p1-p2; float a=Vector3.Dot(d1,d1), e=Vector3.Dot(d2,d2), f=Vector3.Dot(d2,r), t;
        if (a<=1e-6f && e<=1e-6f) { s=0f; return r.magnitude; }
        if (a<=1e-6f) { s=0f; t=Mathf.Clamp01(f/e); }
        else
        {
            float c=Vector3.Dot(d1,r);
            if (e<=1e-6f) { t=0f; s=Mathf.Clamp01(-c/a); }
            else
            {
                float b=Vector3.Dot(d1,d2), denom=a*e-b*b;
                s=denom>1e-6f ? Mathf.Clamp01((b*f-c*e)/denom) : 0f;
                t=(b*s+f)/e;
                if (t<0f) { t=0f; s=Mathf.Clamp01(-c/a); }
                else if (t>1f) { t=1f; s=Mathf.Clamp01((b-c)/a); }
            }
        }
        return ((p1+d1*s)-(p2+d2*t)).magnitude;
    }
    void OnDisable() { CancelAttack(); }
    void OnDestroy() { if (world!=null) { world.AttackTokens.Release(this); world.Npcs.Remove(this); } }
    public void DirectTo(Vector3 destination,float speed)
    {
        if(Role==NpcRole.Civilian&&Fleeing)destination=PanicDestination(destination);
        Agent.speed=speed;
        if(Time.time<nextPath) return;
        nextPath=Time.time+world.Tuning.Npcs.RepathSeconds;
        if(NavMesh.SamplePosition(destination,out var hit,world.Tuning.Npcs.NavSampleRadius,NavMesh.AllAreas)) Agent.SetDestination(hit.position);
    }
    public Vector3 PanicDestination(Vector3 destination)
    {
        Vector3 side=Vector3.Cross(Vector3.up,(destination-transform.position).normalized);
        return destination+side*(Mathf.Sin((Time.time*animationTuning.PanicPathFrequency+(GetEntityId().GetHashCode()%1000)*.013f)*Mathf.PI*2)*animationTuning.PanicPathDeviation);
    }
}
