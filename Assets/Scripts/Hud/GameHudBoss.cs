using UnityEngine;
using UnityEngine.UIElements;

/// PULSEBREAKER boss bar: one top-centre panel, built once per HUD and driven directly from the authoritative CityNpc
/// health (never a predicted estimate). Uses the existing CityNpc.Damaged event and observes active-boss lifecycle.
public sealed partial class GameHud
{
    public const float BossBarWidth = 560f, BossBarTop = 104f, BossBarRange = 90f;

    VisualElement bossRoot, bossFill;
    CityNpc bossHealthSource;
    float shownBossHealth = float.NaN, shownBossMax = float.NaN;
    Label bossName, bossTag, bossPhase, bossValue;
    public bool BossBarVisible { get; private set; }
    public string BossNameShown { get; private set; }
    public string BossPhaseShown { get; private set; }
    public float BossFraction { get; private set; } = -1f;
    public int BossBarUpdates { get; private set; }
    public VisualElement BossRoot => bossRoot;

    void BuildBossBar()
    {
        if (bossRoot != null) return;
        var ink = C(CityColor.UiInk); var muted = C(CityColor.UiMuted); var accent = C(CityColor.Red);
        bossRoot = Box("hud-boss");
        Absolute(bossRoot);
        bossRoot.style.left = Length.Percent(50f);
        bossRoot.style.top = BossBarTop;
        bossRoot.style.translate = new Translate(Length.Percent(-50f), 0f);
        bossRoot.style.width = BossBarWidth; bossRoot.style.maxWidth = Length.Percent(80f);
        bossRoot.style.backgroundColor = Alpha(C(CityColor.UiNavy), .92f);
        Round(bossRoot, 10); Border(bossRoot, Alpha(accent, .85f), 2); Pad(bossRoot, 9, 14);
        bossRoot.style.borderBottomWidth = 3;
        var row = Box("boss-row", FlexDirection.Row);
        row.style.justifyContent = Justify.SpaceBetween; row.style.alignItems = Align.Center;
        bossRoot.Add(row);
        var nameColumn = Box("boss-name-column");
        bossName = Text("PULSEBREAKER", 17, ink); bossName.name = "boss-name"; nameColumn.Add(bossName);
        bossTag = Text("ELITE THREAT", 10, accent); bossTag.name = "boss-tag"; bossTag.style.marginTop = 1; nameColumn.Add(bossTag);
        row.Add(nameColumn);
        bossPhase = Text("PHASE 1", 12, accent); bossPhase.name = "boss-phase";
        bossPhase.style.backgroundColor = Alpha(accent, .16f); Round(bossPhase, 5); Border(bossPhase, Alpha(accent, .6f), 1); Pad(bossPhase, 2, 8);
        row.Add(bossPhase);
        var track = Box("boss-track");
        track.style.height = 12; track.style.marginTop = 7;
        track.style.backgroundColor = Alpha(C(CityColor.UiNavy), .95f); Round(track, 6); track.style.overflow = Overflow.Hidden;
        bossFill = Box("boss-fill"); Absolute(bossFill);
        bossFill.style.left = 0; bossFill.style.top = 0; bossFill.style.bottom = 0;
        bossFill.style.width = Length.Percent(100f);
        bossFill.style.backgroundColor = accent; Round(bossFill, 6);
        track.Add(bossFill);
        bossRoot.Add(track);
        bossValue = Text("", 11, muted, false); bossValue.name = "boss-value";
        bossValue.style.marginTop = 4; bossValue.style.unityTextAlign = TextAnchor.MiddleCenter;
        bossRoot.Add(bossValue);
        Show(bossRoot, false);
        OverlayRoot.Add(bossRoot);
    }

    /// Called once per frame from GameHud.LateUpdate. Every write is guarded by a real change.
    void TickBossBar()
    {
        if (bossRoot == null) BuildBossBar();
        var boss = PulsebreakerBoss.Active;
        BindBossHealth(boss != null ? boss.Npc : null);
        var world = WorldSession.Instance;
        bool live = boss != null && boss.Npc != null && !boss.Npc.Dead && world != null && world.Hero != null &&
            !(world.Mode != null && (world.Mode.Ended || world.Mode.Paused)) && !world.MenuOpen &&
            world.Npcs.Contains(boss.Npc);
        if (!live)
        {
            if (BossBarVisible) { BossBarVisible = false; Show(bossRoot, false); }
            return;
        }
        if (!BossBarVisible)
        {
            BossBarVisible = true; BossFraction = -1f;
            Show(bossRoot, true);
        }
        UpdateBossHealth();
        string shownName = boss.Npc.Archetype != null ? boss.Npc.Archetype.DisplayName.ToUpperInvariant() : "PULSEBREAKER";
        if (BossNameShown != shownName) { BossNameShown = shownName; bossName.text = shownName; }
        string phase = boss.Phase2 ? "PHASE 2" : "PHASE 1";
        if (BossPhaseShown != phase)
        {
            BossPhaseShown = phase;
            bossPhase.text = boss.Phase2 ? "OVERDRIVE · PHASE 2" : phase;
            var accent = C(boss.Phase2 ? CityColor.Fire : CityColor.Red);
            bossPhase.style.color = accent;
            bossPhase.style.borderTopColor = bossPhase.style.borderBottomColor = bossPhase.style.borderLeftColor = bossPhase.style.borderRightColor = Alpha(accent, .6f);
            bossPhase.style.backgroundColor = Alpha(accent, .16f);
            bossFill.style.backgroundColor = accent;
            Border(bossRoot, Alpha(accent, .85f), 2);
            bossTag.style.color = accent;
        }
        // Keep the bar inside the existing popup avoidance lists (no overlap with banners / briefings).
        if (bossRoot.parent == null) OverlayRoot.Add(bossRoot);
    }
    void BindBossHealth(CityNpc npc)
    {
        if (ReferenceEquals(bossHealthSource, npc)) return;
        if (bossHealthSource != null) bossHealthSource.Damaged -= BossDamaged;
        bossHealthSource = npc; shownBossHealth = shownBossMax = float.NaN;
        if (bossHealthSource != null) bossHealthSource.Damaged += BossDamaged;
    }
    void BossDamaged(bool dead)
    {
        UpdateBossHealth();
        if (dead && BossBarVisible) { BossBarVisible = false; Show(bossRoot, false); }
    }
    void UpdateBossHealth()
    {
        if (bossHealthSource == null || bossRoot == null) return;
        float hp = bossHealthSource.CurrentHealth, max = bossHealthSource.MaxHealth;
        if (hp == shownBossHealth && max == shownBossMax && BossFraction >= 0f) return;
        shownBossHealth = hp; shownBossMax = max;
        BossFraction = Mathf.Clamp01(hp / Mathf.Max(1f, max));
        bossFill.style.width = Length.Percent(BossFraction * 100f);
        bossValue.text = CombatValue(hp) + " / " + CombatValue(max);
        BossBarUpdates++;
    }

}
