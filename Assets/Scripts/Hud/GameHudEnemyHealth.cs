using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// Screen-projected, pooled enemy feedback. The only health/damage input is CityNpc and its damage result.
public sealed partial class GameHud
{
    public const int EnemyBarPoolSize = 20, DamageNumberPoolSize = 32;
    public const float EnemyBarRange = 38f, EnemyBarHoldSeconds = 3.5f, EnemyDeathHoldSeconds = .25f;
    public const float DamageNumberSeconds = .8f, RapidDamageWindow = .2f, EnemySightInterval = .12f;
    public sealed class EnemyBarDisplay
    {
        public CityNpc Target;
        public VisualElement Element, Fill;
        public Label Name, Value;
        public float ShowUntil, DeathUntil, NextSight;
        public bool Occluded, Visible;
    }
    public sealed class DamageNumberDisplay
    {
        public CityNpc Target;
        public string SourceId;
        public Label Element;
        public Vector3 Anchor;
        public float Amount, StartedAt, LastHitAt;
        public int Hits;
        public bool Active, Visible;
    }

    public readonly List<EnemyBarDisplay> EnemyBars = new List<EnemyBarDisplay>(EnemyBarPoolSize);
    public readonly List<DamageNumberDisplay> DamageNumbers = new List<DamageNumberDisplay>(DamageNumberPoolSize);
    /// Bounded verification/debug telemetry; each entry is the real CityNpc damage result, not a UI estimate.
    public readonly List<NpcDamageResult> EnemyDamageHistory = new List<NpcDamageResult>(128);
    public int EnemyDamageEvents { get; private set; }
    public int DamageNumbersStarted { get; private set; }
    public int DamageNumbersMerged { get; private set; }
    VisualElement enemyBarLayer, enemyNumberLayer;
    float nextEnemyAim;
    static readonly RaycastHit[] enemySightHits = new RaycastHit[16];

    static string CombatValue(float value)
    {
        float rounded = Mathf.Round(value);
        return Mathf.Abs(value-rounded)<.005f ? rounded.ToString("0",CultureInfo.InvariantCulture) :
            value.ToString("0.##",CultureInfo.InvariantCulture);
    }
    void BuildEnemyFeedback()
    {
        enemyBarLayer=Box("enemy-health-bars"); Absolute(enemyBarLayer);
        enemyBarLayer.style.left=enemyBarLayer.style.top=enemyBarLayer.style.right=enemyBarLayer.style.bottom=0;
        OverlayRoot.Add(enemyBarLayer);
        enemyNumberLayer=Box("enemy-damage-numbers"); Absolute(enemyNumberLayer);
        enemyNumberLayer.style.left=enemyNumberLayer.style.top=enemyNumberLayer.style.right=enemyNumberLayer.style.bottom=0;
        OverlayRoot.Add(enemyNumberLayer);
        var ink=C(CityColor.UiInk);var navy=C(CityColor.UiNavy);var cyan=C(CityColor.Cyan);
        for(int i=0;i<EnemyBarPoolSize;i++)
        {
            var display=new EnemyBarDisplay();
            var box=Box("enemy-health"); Absolute(box); box.style.width=140; box.style.alignItems=Align.Center;
            box.style.backgroundColor=Alpha(navy,.88f); Round(box,6); Border(box,Alpha(cyan,.65f),1); Pad(box,4,7);
            display.Name=Text("",11,cyan); display.Name.style.unityTextAlign=TextAnchor.MiddleCenter; box.Add(display.Name);
            var track=Box("enemy-health-track"); track.style.width=124; track.style.height=6; track.style.marginTop=2;
            track.style.backgroundColor=Alpha(C(CityColor.UiMuted),.45f); Round(track,3); track.style.overflow=Overflow.Hidden; box.Add(track);
            display.Fill=Box("enemy-health-fill"); display.Fill.style.height=Length.Percent(100); display.Fill.style.backgroundColor=cyan;
            Round(display.Fill,3); track.Add(display.Fill);
            display.Value=Text("",11,ink); display.Value.style.marginTop=1; display.Value.style.unityTextAlign=TextAnchor.MiddleCenter; box.Add(display.Value);
            display.Element=box; Show(box,false); enemyBarLayer.Add(box); EnemyBars.Add(display);
        }
        for(int i=0;i<DamageNumberPoolSize;i++)
        {
            var display=new DamageNumberDisplay();
            var label=Text("",20,ink); Absolute(label); label.style.unityTextAlign=TextAnchor.MiddleCenter;
            label.style.width=76; label.style.height=28; label.style.backgroundColor=Alpha(navy,.82f);
            Round(label,5); Border(label,Alpha(cyan,.6f),1); Show(label,false);
            display.Element=label; enemyNumberLayer.Add(label); DamageNumbers.Add(display);
        }
        CityNpc.DamageApplied+=OnNpcDamageApplied;
    }
    void OnNpcDamageApplied(NpcDamageResult result)
    {
        var world=WorldSession.Instance;
        if(world==null || result.Attacker!=world.Powers || result.Target==null || !result.Target.Hostile ||
            result.Target.Role==NpcRole.Civilian || result.HealthLost<=0f) return;
        EnemyDamageEvents++;
        if(EnemyDamageHistory.Count==EnemyDamageHistory.Capacity)EnemyDamageHistory.RemoveAt(0);
        EnemyDamageHistory.Add(result);
        var bar=ShowEnemyBar(result.Target,Time.unscaledTime+EnemyBarHoldSeconds);
        if(bar!=null && result.HealthAfter<=0f)bar.DeathUntil=Time.unscaledTime+EnemyDeathHoldSeconds;
        bool rapid=result.SourceId=="laser-eyes" || result.SourceId=="poison";
        float now=Time.unscaledTime;
        if(rapid)
            foreach(var number in DamageNumbers)
                if(number.Active && number.Target==result.Target && number.SourceId==result.SourceId && now-number.LastHitAt<=RapidDamageWindow)
                {
                    number.Amount+=result.ResolvedDamage; number.Hits++; number.LastHitAt=now;
                    number.StartedAt=now; number.Anchor=result.WorldPosition;
                    number.Element.text=CombatValue(number.Amount); DamageNumbersMerged++; return;
                }
        DamageNumberDisplay free=null;
        foreach(var number in DamageNumbers)
            if(!number.Active || now-number.StartedAt>=DamageNumberSeconds){free=number;break;}
        if(free==null) return; // bounded visual capacity; the authoritative event/history still records the hit.
        free.Active=true;free.Visible=false;free.Target=result.Target;free.SourceId=result.SourceId;
        free.Anchor=result.WorldPosition;free.Amount=result.ResolvedDamage;free.Hits=1;
        free.StartedAt=free.LastHitAt=now;free.Element.text=CombatValue(free.Amount);
        DamageNumbersStarted++;
    }
    EnemyBarDisplay ShowEnemyBar(CityNpc target,float until)
    {
        if(target==null || target.Role==NpcRole.Civilian || !target.Hostile)return null;
        foreach(var bar in EnemyBars)if(bar.Target==target){bar.ShowUntil=Mathf.Max(bar.ShowUntil,until);return bar;}
        EnemyBarDisplay free=null;
        foreach(var bar in EnemyBars)if(bar.Target==null || Time.unscaledTime>bar.ShowUntil && Time.unscaledTime>bar.DeathUntil){free=bar;break;}
        if(free==null)return null;
        free.Target=target;free.ShowUntil=until;free.DeathUntil=0;free.NextSight=0;free.Occluded=true;free.Visible=false;
        free.Name.text=(target.Archetype!=null?target.Archetype.DisplayName:target.Role.ToString()).ToUpperInvariant();
        return free;
    }
    static Vector3 EnemyHead(CityNpc target)
    {
        float height=target.Agent!=null ? target.Agent.height : 1.8f;
        return target.transform.position+Vector3.up*(height+.23f);
    }
    bool EnemyClear(Camera camera,CityNpc target,Vector3 head)
    {
        Vector3 delta=head-camera.transform.position;float distance=delta.magnitude;
        if(distance<.01f)return true;
        int count=Physics.RaycastNonAlloc(camera.transform.position,delta/distance,enemySightHits,distance,~0,QueryTriggerInteraction.Ignore);
        if(count>=enemySightHits.Length)return false; // fail closed if a crowded ray exhausted the buffer
        for(int i=0;i<count;i++)
        {
            var hit=enemySightHits[i].collider;
            if(hit==null || hit.transform.root==target.transform || hit.transform.root==WorldSession.Instance.Hero.transform ||
                hit.GetComponentInParent<CityNpc>()!=null || hit.attachedRigidbody!=null)continue;
            return false;
        }
        return true;
    }
    void TickEnemyFeedback()
    {
        var world=WorldSession.Instance;var camera=Camera.main;
        bool running=world!=null && world.Powers!=null && (world.Mode==null || !world.Mode.Ended) &&
            camera!=null && OverlayRoot!=null && OverlayRoot.layout.width>1f && OverlayRoot.style.display!=DisplayStyle.None;
        if(!running)
        {
            foreach(var bar in EnemyBars){bar.Visible=false;Show(bar.Element,false);}
            foreach(var number in DamageNumbers){number.Visible=false;Show(number.Element,false);}
            return;
        }
        float now=Time.unscaledTime,width=OverlayRoot.layout.width,height=OverlayRoot.layout.height;
        if(now>=nextEnemyAim)
        {
            nextEnemyAim=now+.12f;
            if(Physics.Raycast(camera.ViewportPointToRay(new Vector3(.5f,.5f,0)),out var aimed,EnemyBarRange,~0,QueryTriggerInteraction.Ignore))
            {
                var target=aimed.collider.GetComponentInParent<CityNpc>();
                if(target!=null && !target.Dead)ShowEnemyBar(target,now+.25f);
            }
            foreach(var npc in world.Npcs)
                if(npc!=null && !npc.Dead && npc.Hostile && npc.Role!=NpcRole.Civilian &&
                    (npc.Phase==AttackPhase.Engage || npc.Phase==AttackPhase.Windup) &&
                    (npc.transform.position-camera.transform.position).sqrMagnitude<EnemyBarRange*EnemyBarRange)
                    ShowEnemyBar(npc,now+.3f);
        }
        foreach(var bar in EnemyBars)
        {
            var target=bar.Target;
            if(target==null || !target.Hostile || target.Role==NpcRole.Civilian ||
                now>bar.ShowUntil && (!target.Dead || now>bar.DeathUntil))
            {
                bar.Target=null;bar.Visible=false;Show(bar.Element,false);continue;
            }
            Vector3 head=EnemyHead(target);
            Vector3 view=camera.WorldToViewportPoint(head);
            bool inRange=(head-camera.transform.position).sqrMagnitude<=EnemyBarRange*EnemyBarRange;
            bool onScreen=view.z>0f && view.x>.02f && view.x<.98f && view.y>.04f && view.y<.96f;
            if(inRange && onScreen && now>=bar.NextSight)
            {
                bar.Occluded=!EnemyClear(camera,target,head);bar.NextSight=now+EnemySightInterval;
            }
            bar.Visible=inRange && onScreen && !bar.Occluded;
            Show(bar.Element,bar.Visible);
            if(!bar.Visible)continue;
            bar.Element.style.left=view.x*width-70f;bar.Element.style.top=(1f-view.y)*height-50f;
            bar.Fill.style.width=Length.Percent(target.HealthFraction*100f);
            string hp=CombatValue(target.CurrentHealth)+" / "+CombatValue(target.MaxHealth);
            if(bar.Value.text!=hp)bar.Value.text=hp;
        }
        foreach(var number in DamageNumbers)
        {
            if(!number.Active)continue;
            float age=now-number.StartedAt;
            if(age>=DamageNumberSeconds){number.Active=number.Visible=false;Show(number.Element,false);continue;}
            Vector3 view=camera.WorldToViewportPoint(number.Anchor+Vector3.up*.45f);
            bool inRange=(number.Anchor-camera.transform.position).sqrMagnitude<=EnemyBarRange*EnemyBarRange;
            number.Visible=view.z>0f && inRange && view.x>.02f && view.x<.98f && view.y>.04f && view.y<.96f &&
                (number.Target==null || EnemyClear(camera,number.Target,number.Anchor));
            Show(number.Element,number.Visible);
            if(!number.Visible)continue;
            float x=view.x*width-38f,y=(1f-view.y)*height-20f-age/DamageNumberSeconds*32f;
            if(Mathf.Abs(x+38f-width*.5f)<45f && Mathf.Abs(y+14f-height*.5f)<45f)x+=x<width*.5f?-72f:72f;
            number.Element.style.left=x;number.Element.style.top=y;
            number.Element.style.opacity=Mathf.Clamp01((DamageNumberSeconds-age)/.25f);
        }
    }
}
