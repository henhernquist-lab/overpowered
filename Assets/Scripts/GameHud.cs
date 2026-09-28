using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// Player-facing in-game HUD, built in UI Toolkit in the menus' visual language (palette colours, MenuIcon glyphs,
/// rounded ~.9-alpha panels, bold uppercase captions).
/// Layout: EDGES ONLY. The centre 40% x 40% of the screen holds nothing but the crosshair, which sits at the exact
/// screen centre because every aim ray is Camera.ViewportPointToRay(.5,.5) (PowerUser, SynergyRunner).
/// Data: which groups show is read every frame from the session's GameModeDefinition (Hud flags, SessionSeconds,
/// Director); the power bar is built from the equipped Hero Forge loadout and rebuilt whenever that loadout changes.
/// Scaling: ScaleWithScreenSize + Expand around 1600x900, so the whole reference area fits at every aspect ratio.
/// Every animation uses unscaled time (hit-pause via timeScale must not freeze the HUD).
[DefaultExecutionOrder(1000)]
public sealed partial class GameHud : MonoBehaviour
{
    public static readonly Vector2Int ReferenceResolution = new Vector2Int(1600, 900);
    public const float SafeMargin = 28f, CentreZone = .4f;
    public const float StarPopSeconds = .38f, StarPopScale = .55f, MessageSeconds = 4.5f, MessageFade = .6f;

    public PanelSettings Panel { get; private set; }
    public UIDocument Document { get; private set; }
    public VisualElement Root { get; private set; }
    public VisualElement LevelGroup { get; private set; }
    public VisualElement HeatGroup { get; private set; }
    public VisualElement StarsRow { get; private set; }
    public VisualElement TimerRow { get; private set; }
    public VisualElement TopCentre { get; private set; }
    public VisualElement DirectorGroup { get; private set; }
    public VisualElement ObjectiveGroup { get; private set; }
    public VisualElement VitalsGroup { get; private set; }
    public VisualElement BottomStack { get; private set; }
    public VisualElement PowerBar { get; private set; }
    public VisualElement MeleeHint { get; private set; }
    public Label MessageLine { get; private set; }
    public Label ErrorLine { get; private set; }
    public VisualElement Crosshair { get; private set; }
    public Label LevelValue { get; private set; }
    public Label TimerValue { get; private set; }
    public Label ObjectiveBody { get; private set; }
    public VisualElement HealthFill { get; private set; }
    public VisualElement EnergyFill { get; private set; }
    public VisualElement XpFill { get; private set; }
    public readonly List<HudSlot> Slots = new List<HudSlot>();
    public readonly List<Label> DirectorValues = new List<Label>(), DirectorLabels = new List<Label>();
    /// Loadout signature the power bar was built from, and how many times it has been (re)built.
    public string BuiltSignature { get; private set; }
    public int Rebuilds { get; private set; }
    public int ShownStars { get; private set; } = -1;
    public float LastHeatChange { get; private set; } = -100f;
    /// Top-level groups (the verification no-clip / centre-clear / no-overlap checks walk these).
    public IEnumerable<VisualElement> Groups { get { yield return LevelGroup; yield return HeatGroup; yield return TopCentre; yield return VitalsGroup; yield return BottomStack; } }

    CityPalette palette; ForgeCatalog catalog;
    readonly List<HudStar> stars = new List<HudStar>();
    readonly List<float> starPopAt = new List<float>();
    readonly List<DirectorHudStat> stats = new List<DirectorHudStat>(), shownStats = new List<DirectorHudStat>();
    Label levelCaption, xpLabel, pointsLabel, healthValue, energyValue, directorCaption, objectiveHeader, objectiveMeta;
    VisualElement healthGhost, levelBadge, directorStats, starsBox;
    readonly List<VisualElement> accentBorders = new List<VisualElement>();
    PlayerSide? shownSide; float ghost = 1f, messageAt = -100f, nextObjective, nextCaption;
    string lastMessage, lastError; int shownLevel = -1, shownXp = -1, shownPoints = -1, shownSeconds = -1, shownHealth = -1, shownEnergy = -1;
    WorldSession boundWorld;

    Color C(CityColor role) => palette.Colors[(int)role];
    static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }
    public Color Accent(PlayerSide side) => C(side == PlayerSide.Hero ? CityColor.HeroAccent : CityColor.VillainAccent);

    void Awake()
    {
        palette = Resources.Load<CityPalette>("CityPalette"); catalog = Resources.Load<ForgeCatalog>("ForgeCatalog");
        Panel = ScriptableObject.CreateInstance<PanelSettings>(); Panel.name = "Game HUD panel";
        Panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("MenuTheme");
        Panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; Panel.referenceResolution = ReferenceResolution;
        Panel.screenMatchMode = PanelScreenMatchMode.Expand; Panel.clearColor = false;
        Document = gameObject.AddComponent<UIDocument>(); Document.panelSettings = Panel; Document.sortingOrder = 0;
        Root = Document.rootVisualElement; Root.name = "game-hud"; Root.pickingMode = PickingMode.Ignore;
        Root.style.position = Position.Absolute; Root.style.left = Root.style.right = Root.style.top = Root.style.bottom = 0;
        Root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); Root.style.color = C(CityColor.UiInk);
        Build(); BuildOverlay(); BuildFeedback();
    }

    /// Shows or hides the whole HUD (the FPS A/B uses this to measure the legacy IMGUI-only frame).
    public void SetShown(bool shown) { enabled = shown; Root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None; OverlayRoot.style.display = Root.style.display; }

    // ------------------------------------------------------------------ construction helpers (ModeScreens style)
    Label Text(string text, int size, Color color, bool bold = true)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.style.fontSize = size; label.style.color = color;
        label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
        label.style.marginTop = label.style.marginBottom = label.style.marginLeft = label.style.marginRight = 0;
        label.style.paddingTop = label.style.paddingBottom = label.style.paddingLeft = label.style.paddingRight = 0;
        label.style.whiteSpace = WhiteSpace.NoWrap; return label;
    }
    static VisualElement Box(string name, FlexDirection direction = FlexDirection.Column)
    { var v = new VisualElement { name = name, pickingMode = PickingMode.Ignore }; v.style.flexDirection = direction; return v; }
    static void Round(VisualElement v, float r) { v.style.borderTopLeftRadius = v.style.borderTopRightRadius = v.style.borderBottomLeftRadius = v.style.borderBottomRightRadius = r; }
    static void Border(VisualElement v, Color c, float width)
    {
        v.style.borderTopColor = v.style.borderBottomColor = v.style.borderLeftColor = v.style.borderRightColor = c;
        v.style.borderTopWidth = v.style.borderBottomWidth = v.style.borderLeftWidth = v.style.borderRightWidth = width;
    }
    static void Pad(VisualElement v, float vertical, float horizontal) { v.style.paddingTop = v.style.paddingBottom = vertical; v.style.paddingLeft = v.style.paddingRight = horizontal; }
    void PanelBox(VisualElement v) { v.style.backgroundColor = Alpha(C(CityColor.UiPanel), .9f); Round(v, 12); Border(v, Alpha(C(CityColor.UiMuted), .18f), 1); }
    static void Absolute(VisualElement v) { v.style.position = Position.Absolute; }
    Label Keycap(string text)
    {
        var key = Text(text, 11, C(CityColor.UiInk)); key.name = "keycap"; key.style.backgroundColor = Alpha(C(CityColor.UiNavy), .92f);
        Round(key, 4); Border(key, Alpha(C(CityColor.UiMuted), .55f), 1); Pad(key, 1, 5); key.style.unityTextAlign = TextAnchor.MiddleCenter; return key;
    }
    VisualElement Track(float height, Color fill, out VisualElement bar, out VisualElement lag)
    {
        var track = Box("track"); track.style.height = height; track.style.backgroundColor = Alpha(C(CityColor.UiNavy), .95f); Round(track, height * .5f); track.style.overflow = Overflow.Hidden;
        lag = Box("ghost"); Absolute(lag); lag.style.left = 0; lag.style.top = 0; lag.style.bottom = 0; lag.style.backgroundColor = Alpha(C(CityColor.UiInk), .55f); Round(lag, height * .5f); track.Add(lag);
        bar = Box("fill"); Absolute(bar); bar.style.left = 0; bar.style.top = 0; bar.style.bottom = 0; bar.style.backgroundColor = fill; Round(bar, height * .5f); track.Add(bar);
        return track;
    }

    void Build()
    {
        var ink = C(CityColor.UiInk); var muted = C(CityColor.UiMuted);
        // ---- top-left: level badge + thin XP bar
        LevelGroup = Box("hud-level", FlexDirection.Row); Absolute(LevelGroup); LevelGroup.style.left = SafeMargin; LevelGroup.style.top = SafeMargin;
        PanelBox(LevelGroup); Pad(LevelGroup, 10, 14); LevelGroup.style.alignItems = Align.Center; Root.Add(LevelGroup);
        levelBadge = Box("level-badge"); levelBadge.style.width = 54; levelBadge.style.height = 54; Round(levelBadge, 11); levelBadge.style.alignItems = Align.Center; levelBadge.style.justifyContent = Justify.Center;
        accentBorders.Add(levelBadge); LevelGroup.Add(levelBadge);
        levelCaption = Text("LV", 11, muted); levelBadge.Add(levelCaption);
        LevelValue = Text("1", 24, ink); LevelValue.name = "level-value"; LevelValue.style.marginTop = -3; levelBadge.Add(LevelValue);
        var xpColumn = Box("xp"); xpColumn.style.marginLeft = 12; xpColumn.style.width = 188; LevelGroup.Add(xpColumn);
        var xpRow = Box("xp-row", FlexDirection.Row); xpRow.style.justifyContent = Justify.SpaceBetween; xpColumn.Add(xpRow);
        xpRow.Add(Text("LEVEL", 12, muted)); xpLabel = Text("0 / 0 XP", 12, muted, false); xpRow.Add(xpLabel);
        var xpTrack = Track(7, Accent(PlayerSide.Hero), out var xpFill, out var xpGhost); xpGhost.style.display = DisplayStyle.None; xpTrack.style.marginTop = 6; xpTrack.style.backgroundColor = C(CityColor.UiPurple); XpFill = xpFill; XpFill.name = "xp-fill"; xpColumn.Add(xpTrack);
        pointsLabel = Text("", 12, Accent(PlayerSide.Hero)); pointsLabel.style.marginTop = 6; xpColumn.Add(pointsLabel);

        // ---- top-right: Heat stars + session timer
        HeatGroup = Box("hud-heat"); Absolute(HeatGroup); HeatGroup.style.right = SafeMargin; HeatGroup.style.top = SafeMargin; HeatGroup.style.alignItems = Align.FlexEnd;
        PanelBox(HeatGroup); Pad(HeatGroup, 10, 14); Root.Add(HeatGroup);
        StarsRow = Box("heat-row", FlexDirection.Row); StarsRow.style.alignItems = Align.Center; HeatGroup.Add(StarsRow);
        StarsRow.Add(Text("HEAT", 12, muted)); starsBox = Box("stars", FlexDirection.Row); starsBox.style.marginLeft = 8; StarsRow.Add(starsBox);
        TimerRow = Box("timer-row", FlexDirection.Row); TimerRow.style.alignItems = Align.Center; TimerRow.style.marginTop = 4; HeatGroup.Add(TimerRow);
        TimerRow.Add(Text("TIME LEFT", 12, muted)); TimerValue = Text("00:00", 26, ink); TimerValue.name = "timer-value"; TimerValue.style.marginLeft = 10; TimerRow.Add(TimerValue);

        // ---- top-centre: director stats (e.g. Endless waves) and the objective line (Phase 2: Hud/GameHudGuidance.cs)
        TopCentre = Box("hud-top-centre"); Absolute(TopCentre); TopCentre.style.left = Length.Percent(50); TopCentre.style.top = SafeMargin;
        TopCentre.style.translate = new Translate(Length.Percent(-50), 0); TopCentre.style.alignItems = Align.Center; Root.Add(TopCentre);
        DirectorGroup = Box("hud-director"); PanelBox(DirectorGroup); Pad(DirectorGroup, 8, 12); DirectorGroup.style.alignItems = Align.Center; TopCentre.Add(DirectorGroup);
        directorStats = Box("director-stats", FlexDirection.Row); DirectorGroup.Add(directorStats);
        directorCaption = Text("", 12, Accent(PlayerSide.Hero)); directorCaption.style.marginTop = 2; DirectorGroup.Add(directorCaption);
        // Phase 2: a short objective LINE ("STOP THE ROBBERS 2/3") under the encounter name, with a "+N MORE TASKS" hint.
        ObjectiveGroup = Box("hud-objective"); PanelBox(ObjectiveGroup); Pad(ObjectiveGroup, 8, 20); ObjectiveGroup.style.minWidth = 300; ObjectiveGroup.style.maxWidth = 600; ObjectiveGroup.style.marginTop = 8;
        ObjectiveGroup.style.alignItems = Align.Center; ObjectiveGroup.style.borderBottomWidth = 3; accentBorders.Add(ObjectiveGroup); TopCentre.Add(ObjectiveGroup);
        objectiveHeader = Text("", 12, Accent(PlayerSide.Hero)); objectiveHeader.name = "objective-header"; ObjectiveGroup.Add(objectiveHeader);
        ObjectiveBody = Text("", 22, ink); ObjectiveBody.name = "objective-body"; ObjectiveBody.style.whiteSpace = WhiteSpace.Normal; ObjectiveBody.style.maxWidth = 560; ObjectiveBody.style.unityTextAlign = TextAnchor.MiddleCenter; ObjectiveBody.style.marginTop = 2; ObjectiveGroup.Add(ObjectiveBody);
        objectiveMeta = Text("", 12, muted, false); objectiveMeta.name = "objective-meta"; objectiveMeta.style.marginTop = 2; ObjectiveGroup.Add(objectiveMeta);

        // ---- bottom-left: health + energy
        VitalsGroup = Box("hud-vitals"); Absolute(VitalsGroup); VitalsGroup.style.left = SafeMargin; VitalsGroup.style.bottom = SafeMargin; VitalsGroup.style.width = 330;
        PanelBox(VitalsGroup); Pad(VitalsGroup, 12, 16); Root.Add(VitalsGroup);
        var healthRow = Box("health-row", FlexDirection.Row); healthRow.style.justifyContent = Justify.SpaceBetween; healthRow.style.alignItems = Align.FlexEnd; VitalsGroup.Add(healthRow);
        healthRow.Add(Text("HEALTH", 12, muted)); healthValue = Text("100", 16, ink); healthRow.Add(healthValue);
        var healthTrack = Track(13, C(CityColor.Red), out var hf, out healthGhost); healthTrack.style.marginTop = 4; HealthFill = hf; HealthFill.name = "health-fill"; VitalsGroup.Add(healthTrack);
        var energyRow = Box("energy-row", FlexDirection.Row); energyRow.style.justifyContent = Justify.SpaceBetween; energyRow.style.alignItems = Align.FlexEnd; energyRow.style.marginTop = 9; VitalsGroup.Add(energyRow);
        energyRow.Add(Text("ENERGY", 12, muted)); energyValue = Text("100", 13, ink); energyRow.Add(energyValue);
        var energyTrack = Track(8, C(CityColor.Cyan), out var ef, out var eg); eg.style.display = DisplayStyle.None; energyTrack.style.marginTop = 4; EnergyFill = ef; EnergyFill.name = "energy-fill"; VitalsGroup.Add(energyTrack);

        // ---- bottom-centre: save error, message line, power bar
        BottomStack = Box("hud-bottom-centre"); Absolute(BottomStack); BottomStack.style.left = Length.Percent(50); BottomStack.style.bottom = SafeMargin;
        BottomStack.style.translate = new Translate(Length.Percent(-50), 0); BottomStack.style.alignItems = Align.Center; Root.Add(BottomStack);
        ErrorLine = Text("", 13, ink); ErrorLine.name = "hud-error"; ErrorLine.style.backgroundColor = Alpha(C(CityColor.Red), .92f); Round(ErrorLine, 8); Pad(ErrorLine, 5, 14);
        ErrorLine.style.whiteSpace = WhiteSpace.Normal; ErrorLine.style.maxWidth = 620; ErrorLine.style.marginBottom = 8; ErrorLine.style.display = DisplayStyle.None; BottomStack.Add(ErrorLine);
        MessageLine = Text("", 14, ink); MessageLine.name = "hud-message"; MessageLine.style.backgroundColor = Alpha(C(CityColor.UiNavy), .82f); Round(MessageLine, 8); Pad(MessageLine, 6, 14);
        MessageLine.style.whiteSpace = WhiteSpace.Normal; MessageLine.style.maxWidth = 620; MessageLine.style.unityTextAlign = TextAnchor.MiddleCenter; MessageLine.style.marginBottom = 10; MessageLine.style.display = DisplayStyle.None; BottomStack.Add(MessageLine);
        PowerBar = Box("hud-powers", FlexDirection.Row); PowerBar.style.alignItems = Align.FlexStart; PanelBox(PowerBar); Pad(PowerBar, 10, 6); BottomStack.Add(PowerBar);

        // ---- the only centre element
        Crosshair = new HudCrosshair(Alpha(ink, .95f), Alpha(C(CityColor.UiNavy), .7f)) { name = "hud-crosshair" }; Absolute(Crosshair);
        Crosshair.style.width = 26; Crosshair.style.height = 26; Crosshair.style.left = Length.Percent(50); Crosshair.style.top = Length.Percent(50);
        Crosshair.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50)); Root.Add(Crosshair);
    }

    // ------------------------------------------------------------------ power bar (built from the loadout)
    PowerDefinition builtA, builtB; PowerSynergyDefinition builtSynergy; int builtCount; bool builtStrength, built; KeyCode builtKey;
    static bool StrengthEquipped(PowerUser user) => user.Strength != null && user.IsEquipped(user.Strength.Definition);
    /// Allocation-free per-frame check that the loadout still matches what the bar was built from.
    bool LoadoutChanged(PowerUser user) => !built || user.EquippedA != builtA || user.EquippedB != builtB || user.Synergy != builtSynergy || user.Powers.Count != builtCount ||
        StrengthEquipped(user) != builtStrength || (catalog != null ? catalog.SynergyKey : KeyCode.None) != builtKey;
    static string Signature(PowerUser user, ForgeCatalog forge)
    {
        if (user == null) return "";
        return $"{user.EquippedA?.Id}|{user.EquippedB?.Id}|{user.Synergy?.Id}|{user.Powers.Count}|{forge?.SynergyKey}|{(user.Strength != null && user.IsEquipped(user.Strength.Definition))}";
    }
    /// The slots, in loadout order: every equipped power the player owns, then the synergy.
    /// Without a Forge catalog every owned power is "equipped" (PowerUser.IsEquipped), so they all become slots.
    public static List<PowerRuntime> EquippedPowers(PowerUser user)
    {
        var list = new List<PowerRuntime>();
        if (user.Forge != null)
        {
            foreach (var definition in new[] { user.EquippedA, user.EquippedB })
            { var runtime = user.Powers.Find(p => p.Definition == definition); if (runtime != null && !list.Contains(runtime)) list.Add(runtime); }
        }
        else foreach (var runtime in user.Powers) if (user.Progression.Owns(runtime.Definition)) list.Add(runtime);
        return list;
    }
    void RebuildPowers(PowerUser user)
    {
        PowerBar.Clear(); Slots.Clear();
        bool strengthEquipped = user.Strength != null && user.IsEquipped(user.Strength.Definition);
        MeleeHint = Box("melee-hint"); MeleeHint.style.alignItems = Align.Center; MeleeHint.style.marginRight = 10; MeleeHint.style.marginTop = 22;
        MeleeHint.Add(Keycap(HudBindings.BasicMelee)); var meleeLabel = Text("MELEE", 11, C(CityColor.UiMuted)); meleeLabel.style.marginTop = 5; MeleeHint.Add(meleeLabel);
        MeleeHint.style.display = strengthEquipped || user.Forge == null ? DisplayStyle.None : DisplayStyle.Flex; PowerBar.Add(MeleeHint);
        foreach (var runtime in EquippedPowers(user))
        {
            var d = runtime.Definition;
            PowerBar.Add(Slot(new HudSlot { Id = d.Id, Power = runtime, Glyph = d.MenuIcon, Color = C(d.PaletteColor), Name = d.DisplayName, KeyText = HudBindings.PowerKey(user, runtime), Flight = d.Effect != null && d.Effect.IsFlight }));
        }
        if (user.Synergy != null && user.SynergyRunner != null)
        {
            var divider = Box("synergy-divider"); divider.style.width = 2; divider.style.height = 50; divider.style.marginTop = 11; divider.style.marginLeft = 4; divider.style.marginRight = 4;
            divider.style.backgroundColor = Alpha(C(CityColor.UiMuted), .35f); PowerBar.Add(divider);
            var s = user.Synergy;
            PowerBar.Add(Slot(new HudSlot { Id = s.Id, Synergy = s, Glyph = s.Icon, Color = C(s.Primary), Name = s.DisplayName, KeyText = catalog != null ? HudBindings.KeyName(catalog.SynergyKey) : "" }));
        }
        BuiltSignature = Signature(user, catalog); Rebuilds++;
        builtA = user.EquippedA; builtB = user.EquippedB; builtSynergy = user.Synergy; builtCount = user.Powers.Count; builtStrength = StrengthEquipped(user); builtKey = catalog != null ? catalog.SynergyKey : KeyCode.None; built = true;
    }
    VisualElement Slot(HudSlot slot)
    {
        var container = Box("slot-" + slot.Id); container.style.width = 118; container.style.alignItems = Align.Center; slot.Container = container;
        var tile = Box("tile"); tile.style.width = 74; tile.style.height = 74; Round(tile, 14); tile.style.overflow = Overflow.Hidden;
        tile.style.backgroundColor = Alpha(Color.Lerp(C(CityColor.UiPanel), slot.Color, .16f), .92f); Border(tile, Alpha(slot.Color, .55f), 2); slot.Tile = tile; container.Add(tile);
        slot.Icon = new MenuIcon(slot.Glyph, slot.Color) { name = "icon" }; Absolute(slot.Icon); slot.Icon.style.left = slot.Icon.style.right = 13; slot.Icon.style.top = 9; slot.Icon.style.bottom = 17; tile.Add(slot.Icon);
        slot.Radial = new HudRadial(Alpha(C(CityColor.UiNavy), .74f), Alpha(C(CityColor.UiInk), .9f)) { name = "radial" }; Absolute(slot.Radial);
        slot.Radial.style.left = slot.Radial.style.right = slot.Radial.style.top = slot.Radial.style.bottom = 0; tile.Add(slot.Radial);
        if (slot.Flight)
        {
            var track = Box("fuel-track"); Absolute(track); track.style.left = 13; track.style.right = 13; track.style.bottom = 8; track.style.height = 5; Round(track, 2.5f); track.style.backgroundColor = Alpha(C(CityColor.UiMuted), .25f); tile.Add(track);
            slot.FuelFill = Box("fuel-fill"); slot.FuelFill.style.height = Length.Percent(100); slot.FuelFill.style.backgroundColor = slot.Color; Round(slot.FuelFill, 2.5f); track.Add(slot.FuelFill);
        }
        else if (slot.Power != null)
        {
            slot.PipRow = Box("pips", FlexDirection.Row); Absolute(slot.PipRow); slot.PipRow.style.left = 0; slot.PipRow.style.right = 0; slot.PipRow.style.bottom = 7; slot.PipRow.style.justifyContent = Justify.Center; tile.Add(slot.PipRow);
        }
        slot.Key = Keycap(slot.KeyText); Absolute(slot.Key); slot.Key.style.left = 4; slot.Key.style.top = 4; tile.Add(slot.Key);
        slot.SelectedTag = Keycap(HudBindings.FireSelected); Absolute(slot.SelectedTag); slot.SelectedTag.style.right = 4; slot.SelectedTag.style.top = 4;
        slot.SelectedTag.style.color = C(CityColor.UiNavy); slot.SelectedTag.style.backgroundColor = slot.Color; slot.SelectedTag.style.display = DisplayStyle.None; tile.Add(slot.SelectedTag);
        var name = Text(slot.Name.ToUpperInvariant(), 11, C(CityColor.UiMuted)); name.name = "slot-name"; name.style.marginTop = 5; name.style.maxWidth = 118;
        name.style.overflow = Overflow.Hidden; name.style.textOverflow = TextOverflow.Ellipsis; name.style.unityTextAlign = TextAnchor.MiddleCenter; slot.NameLabel = name; container.Add(name);
        Slots.Add(slot); return container;
    }
    void SetPips(HudSlot slot, int count)
    {
        if (slot.PipRow == null || count == slot.Pips.Count) return;
        slot.PipRow.Clear(); slot.Pips.Clear(); slot.LitPips = -1;
        for (int i = 0; i < count; i++)
        { var pip = Box("pip"); pip.style.width = count > 4 ? 7 : 10; pip.style.height = 4; Round(pip, 2); pip.style.marginLeft = pip.style.marginRight = 2; slot.PipRow.Add(pip); slot.Pips.Add(pip); }
    }
    void UpdateSlot(HudSlot slot, PowerUser user)
    {
        float fraction = 0f; bool dim = false;
        if (slot.Power != null)
        {
            var p = slot.Power; var s = user.Stats(p);
            if (slot.Flight)
            {
                slot.FuelFraction = Mathf.Clamp01(p.Fuel / Mathf.Max(.001f, s.Duration)); slot.FuelFill.style.width = Length.Percent(slot.FuelFraction * 100f); dim = p.Fuel <= 0f;
            }
            else
            {
                SetPips(slot, Mathf.Max(0, s.Charges));
                int lit = Mathf.Clamp(p.Charges, 0, slot.Pips.Count);
                if (lit != slot.LitPips)
                {
                    slot.LitPips = lit;
                    for (int i = 0; i < slot.Pips.Count; i++) slot.Pips[i].style.backgroundColor = i < lit ? slot.Color : Alpha(C(CityColor.UiMuted), .25f);
                }
                if (p.Cooldown > 0f) fraction = p.Cooldown / Mathf.Max(.001f, s.Cooldown);
                else if (p.Charges <= 0) fraction = 1f - p.ChargeTimer / Mathf.Max(.001f, p.Definition.ChargeRecharge);
                dim = p.Charges <= 0 || user.Energy < p.Definition.ResourceCost;
            }
            bool selected = !slot.Flight && user.Selected == p;
            slot.SelectedTag.style.display = selected && slot.KeyText != HudBindings.StrengthKey ? DisplayStyle.Flex : DisplayStyle.None;
            if (selected != slot.Selected) { slot.Selected = selected; Border(slot.Tile, selected ? slot.Color : Alpha(slot.Color, .55f), 2); }
        }
        else if (slot.Synergy != null)
        {
            var runner = user.SynergyRunner;
            fraction = runner.Cooldown / Mathf.Max(.001f, slot.Synergy.Cooldown);
            bool busy = runner.Busy; slot.Busy = busy;
            float pulse = busy ? .55f + .45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 9f)) : .55f;
            Border(slot.Tile, Alpha(busy ? C(CityColor.UiInk) : slot.Color, pulse), 2);
        }
        slot.Radial.Set(fraction); slot.CooldownFraction = slot.Radial.Fraction;
        if (dim != slot.Dim) { slot.Dim = dim; slot.Icon.Ink = dim ? Alpha(slot.Color, .35f) : slot.Color; slot.Icon.MarkDirtyRepaint(); }
    }

    // ------------------------------------------------------------------ per-frame update
    static void Show(VisualElement v, bool shown) { var d = shown ? DisplayStyle.Flex : DisplayStyle.None; if (v.style.display != d) v.style.display = d; }
    public static bool Shown(VisualElement v) { for (var e = v; e != null; e = e.parent) if (e.resolvedStyle.display == DisplayStyle.None || !e.visible) return false; return true; }

    void Bind(WorldSession w)
    {
        BindFeedback(w);   // progression / session / director can appear after the world (cheap reference checks)
        if (boundWorld == w) { if (w != null) BindHero(w.Hero); return; }
        if (boundWorld != null) boundWorld.PlayerRespawned -= Respawned;
        BindGuidance(boundWorld, w); BindHeat(boundWorld, w);
        boundWorld = w; if (w != null) w.PlayerRespawned += Respawned;
    }
    void Respawned() => ShowMessage("RESPAWNED  ·  PROGRESSION KEPT");
    public void ShowMessage(string text) { if (string.IsNullOrEmpty(text)) return; MessageLine.text = text; messageAt = Time.unscaledTime; }

    /// CPU milliseconds of the most recent HUD update (measurement only).
    public double LastUpdateMs { get; private set; }
    void LateUpdate()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Refresh();
        LastUpdateMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }
    void Refresh()
    {
        var w = WorldSession.Instance; Bind(w);
        if (w == null || w.Tuning == null || w.Powers == null || (w.Mode != null && w.Mode.Ended)) { Show(Root, false); HideGuidance(); if (w != null && w.Mode != null && w.Mode.Ended) DismissBriefing("session ended"); return; }
        Show(Root, true);
        var definition = w.Mode != null ? w.Mode.Definition : null;
        var hud = definition != null ? definition.Hud : ModeHud.All;
        var progress = w.Progression; var user = w.Powers; float now = Time.unscaledTime;
        var side = progress.Data.Side;
        if (shownSide != side)
        {
            shownSide = side; var accent = Accent(side);
            foreach (var v in accentBorders) { if (v == ObjectiveGroup) v.style.borderBottomColor = accent; else v.style.borderLeftColor = accent; if (v == levelBadge) { Border(v, accent, 2); v.style.backgroundColor = Alpha(accent, .16f); } }
            XpFill.style.backgroundColor = accent; pointsLabel.style.color = accent; directorCaption.style.color = accent; objectiveHeader.style.color = accent;
        }

        // Level + XP (Progression)
        Show(LevelGroup, (hud & ModeHud.Progression) != 0);
        if (progress.Data.Level != shownLevel || progress.Data.Xp != shownXp || progress.Data.Points != shownPoints)
        {
            shownLevel = progress.Data.Level; shownXp = progress.Data.Xp; shownPoints = progress.Data.Points;
            LevelValue.text = shownLevel.ToString(); xpLabel.text = $"{shownXp} / {progress.RequiredXp} XP";
            XpFill.style.width = Length.Percent(100f * Mathf.Clamp01(shownXp / (float)progress.RequiredXp));
            pointsLabel.text = shownPoints > 0 ? $"{shownPoints} UPGRADE POINT{(shownPoints == 1 ? "" : "S")}  ·  {HudBindings.KeyName(HudBindings.MenuKey)}" : "";
            Show(pointsLabel, shownPoints > 0);
        }
        UpdateLevelBurst(now, Accent(side));

        // Heat stars (Heat) + timer (SessionSeconds > 0)
        bool heat = (hud & ModeHud.Heat) != 0, timer = definition != null && definition.SessionSeconds > 0;
        Show(StarsRow, heat); Show(TimerRow, timer); Show(HeatGroup, heat || timer);
        TimerRow.style.marginTop = heat ? 4 : 0;
        UpdateStars(w, now); UpdateHeatFlash(now, heat);
        if (timer)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, definition.SessionSeconds - w.Mode.Elapsed));
            if (seconds != shownSeconds)
            {
                shownSeconds = seconds; TimerValue.text = $"{seconds / 60:00}:{seconds % 60:00}";
                TimerValue.style.color = seconds <= 60 ? C(CityColor.VillainAccent) : C(CityColor.UiInk);
            }
        }

        // Director stats (Director) and objective slot (Objectives)
        var director = w.Mode != null ? w.Mode.Director : null;
        Show(DirectorGroup, director != null);
        if (director != null) UpdateDirector(director);
        bool objectives = definition != null && (hud & ModeHud.Objectives) != 0;
        Show(ObjectiveGroup, objectives);
        ObjectiveGroup.style.marginTop = director != null ? 8 : 0;
        Show(TopCentre, director != null || objectives);
        if (objectives) UpdateObjectiveState(w, now);
        if (objectives && now >= nextObjective) { nextObjective = now + .25f; UpdateObjectiveLine(w, now); }
        if (objectives) PulseObjective(now);

        // Vitals (Health)
        Show(VitalsGroup, (hud & ModeHud.Health) != 0);
        float health = Mathf.Clamp01(w.Health / Mathf.Max(1f, w.MaxHealth)), energy = Mathf.Clamp01(user.Energy / Mathf.Max(1f, user.MaxEnergy));   // hero archetype maxima
        ghost = health >= ghost ? health : Mathf.MoveTowards(ghost, health, Time.unscaledDeltaTime * .5f);
        HealthFill.style.width = Length.Percent(health * 100f); healthGhost.style.width = Length.Percent(ghost * 100f); EnergyFill.style.width = Length.Percent(energy * 100f);
        int hv = Mathf.CeilToInt(w.Health), ev = Mathf.FloorToInt(user.Energy);
        if (hv != shownHealth) { shownHealth = hv; healthValue.text = hv.ToString(); }
        if (ev != shownEnergy) { shownEnergy = ev; energyValue.text = ev.ToString(); }
        HealthFill.style.opacity = health < .3f ? .65f + .35f * Mathf.Abs(Mathf.Sin(now * 6f)) : 1f;

        // Power bar (Powers)
        bool powers = (hud & ModeHud.Powers) != 0;
        Show(PowerBar, powers);
        if (LoadoutChanged(user)) RebuildPowers(user);
        if (powers) foreach (var slot in Slots) UpdateSlot(slot, user);

        // Message line: player-relevant WorldSession messages (side switch, waves, encounter results), respawn, save errors.
        // The session-start message is the mode Description; when the mode has a briefing card, the card replaces it.
        // Phase 3: an encounter result or a director milestone already has its own banner, so its message line is not repeated.
        if (w.Message != lastMessage) { lastMessage = w.Message; if (!(definition != null && definition.HasBriefing && w.Message == definition.Description)) { if (BannerCovers(w)) messageAt = -100f; else ShowMessage(w.Message); } }
        float age = now - messageAt;
        bool message = age < MessageSeconds + MessageFade && !string.IsNullOrEmpty(MessageLine.text);
        Show(MessageLine, message);
        MessageLine.style.opacity = Mathf.Clamp01(1f - (age - MessageSeconds) / MessageFade);
        if (progress.LastError != lastError) { lastError = progress.LastError; ErrorLine.text = lastError == null ? "" : "SAVE ERROR: " + lastError; }
        Show(ErrorLine, lastError != null);
        // An empty bottom-centre stack is hidden (e.g. Powers flag off and no message, now that a briefing card
        // replaces the session-start Description message).
        Show(BottomStack, powers || message || lastError != null);

        Show(Crosshair, !w.PlayerDead && !w.MenuOpen);
        UpdateGuidance(w, definition, objectives, now);
    }

    void UpdateStars(WorldSession w, float now)
    {
        int maximum = Mathf.Max(0, w.Tuning.Heat.MaximumStars);
        if (stars.Count != maximum)
        {
            starsBox.Clear(); stars.Clear(); starPopAt.Clear();
            for (int i = 0; i < maximum; i++) { var star = new HudStar { name = "star-" + i }; star.style.width = 24; star.style.height = 24; star.style.marginLeft = 2; starsBox.Add(star); stars.Add(star); starPopAt.Add(-100f); }
            ShownStars = -1;
        }
        int lit = Mathf.Clamp(w.Stars, 0, maximum);
        if (lit != ShownStars)
        {
            if (ShownStars >= 0) { for (int i = Mathf.Min(lit, ShownStars); i < Mathf.Max(lit, ShownStars); i++) starPopAt[i] = now; LastHeatChange = now; StarsChanged(ShownStars, lit, now); }
            ShownStars = lit;
            for (int i = 0; i < maximum; i++) stars[i].Set(i < lit ? C(CityColor.Amber) : Alpha(C(CityColor.UiNavy), .7f), i < lit ? Alpha(C(CityColor.UiInk), .9f) : Alpha(C(CityColor.UiMuted), .45f));
        }
        for (int i = 0; i < maximum; i++)
        {
            float scale = StarScaleAt(i, now);
            if (!Mathf.Approximately(stars[i].resolvedStyle.scale.value.x, scale)) stars[i].style.scale = new Scale(new Vector3(scale, scale, 1f));
        }
    }
    float StarScaleAt(int i, float now)
    {
        float t = (now - starPopAt[i]) / StarPopSeconds;
        return t < 0f || t >= 1f ? 1f : 1f + StarPopScale * Mathf.Sin(t * Mathf.PI);
    }
    /// The scale the HUD is applying to star i right now (verification samples it).
    public float StarScale(int i) => i >= 0 && i < stars.Count ? StarScaleAt(i, Time.unscaledTime) : 1f;
    public int StarCount => stars.Count;

    void UpdateDirector(ModeDirectorState director)
    {
        stats.Clear(); director.HudStats(stats);
        if (stats.Count != DirectorValues.Count)
        {
            directorStats.Clear(); DirectorValues.Clear(); DirectorLabels.Clear(); shownStats.Clear();
            foreach (var stat in stats)
            {
                var column = Box("stat"); column.style.alignItems = Align.Center; column.style.marginLeft = column.style.marginRight = 14; column.style.minWidth = 52; directorStats.Add(column);
                var value = Text("", 26, C(CityColor.UiInk)); column.Add(value); DirectorValues.Add(value);
                var label = Text("", 11, C(CityColor.UiMuted)); column.Add(label); DirectorLabels.Add(label); shownStats.Add(new DirectorHudStat(null, int.MinValue));
            }
        }
        for (int i = 0; i < stats.Count; i++)
        {
            // Strings are only built when a value or label actually changes (no per-frame allocation).
            if (shownStats[i].Value != stats[i].Value) DirectorValues[i].text = stats[i].Value.ToString();
            if (!ReferenceEquals(shownStats[i].Label, stats[i].Label)) DirectorLabels[i].text = stats[i].Label.ToUpperInvariant();
            shownStats[i] = stats[i];
        }
        if (Time.unscaledTime >= nextCaption) { nextCaption = Time.unscaledTime + .2f; string caption = director.HudCaption ?? ""; if (directorCaption.text != caption) directorCaption.text = caption; }
        string shown = directorCaption.text;
        Show(directorCaption, shown.Length > 0);
    }

    void OnDestroy() { Bind(null); BindHero(null); if (Panel != null) Destroy(Panel); if (OverlayPanel != null) Destroy(OverlayPanel); if (overlayHost != null) Destroy(overlayHost); }
}

/// One power-bar slot and the values it is currently drawing (verification reads these).
public sealed class HudSlot
{
    public string Id, Name, KeyText; public PowerRuntime Power; public PowerSynergyDefinition Synergy;
    public MenuGlyph Glyph; public Color Color; public bool Flight;
    public VisualElement Container, Tile, PipRow, FuelFill; public MenuIcon Icon; public HudRadial Radial; public Label Key, SelectedTag, NameLabel;
    public readonly List<VisualElement> Pips = new List<VisualElement>();
    public int LitPips = -1; public float CooldownFraction, FuelFraction; public bool Selected, Dim, Busy;
}
