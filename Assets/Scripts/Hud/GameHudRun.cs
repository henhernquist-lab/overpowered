using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// RUN PROGRESSION HUD: the three-card level-up modal for Endless Fight.
/// Built once (three reusable cards), shown only while RunProgress.UpgradeOpen is true. The session is paused through
/// GameModeSession.SetPaused while it is open, so no enemy can act during a choice. Mouse and keys 1/2/3 both work; the
/// modal consumes those keys itself (number keys are otherwise power-slot shortcuts on SuperHeroController).
public sealed partial class GameHud
{
    public const float RunModalMaxWidth = 1040f;

    VisualElement runModal, runCardRow, runModalPanel;
    Label runModalLevel, runModalTitle, runModalHint;
    readonly List<VisualElement> runCards = new List<VisualElement>(3);
    readonly List<Label> runCardRarity = new List<Label>(3), runCardName = new List<Label>(3), runCardTarget = new List<Label>(3),
        runCardDesc = new List<Label>(3), runCardStacks = new List<Label>(3), runCardKey = new List<Label>(3);
    readonly List<VisualElement> runCardAccents = new List<VisualElement>(3);
    int runOfferStamp = -1;
    RunProgress boundRun;

    /// Verification/diagnostics: modal shown, how many cards the current offer has, and each card's texts.
    public bool RunModalVisible { get; private set; }
    public int RunOfferCount { get; private set; }
    public string RunOfferName(int index) => index >= 0 && index < runCardName.Count ? runCardName[index].text : null;
    public string RunOfferRarity(int index) => index >= 0 && index < runCardRarity.Count ? runCardRarity[index].text : null;
    public int RunModalLevelShown { get; private set; }

    static readonly string[] RunKeyLabels = { "1", "2", "3" };

    void BuildRunModal()
    {
        if (runModal != null) return;
        var ink = C(CityColor.UiInk); var muted = C(CityColor.UiMuted); var navy = C(CityColor.UiNavy); var accent = C(CityColor.Cyan);

        runModal = Box("run-modal"); Absolute(runModal);
        runModal.style.left = runModal.style.right = runModal.style.top = runModal.style.bottom = 0;
        runModal.style.backgroundColor = Alpha(navy, .74f);
        runModal.style.alignItems = Align.Center; runModal.style.justifyContent = Justify.Center;
        runModal.pickingMode = PickingMode.Position;   // the scrim swallows clicks so nothing behind it can be pressed
        Show(runModal, false);

        runModalPanel = Box("run-modal-panel");
        runModalPanel.style.width = Length.Percent(92f); runModalPanel.style.maxWidth = RunModalMaxWidth;
        runModalPanel.style.backgroundColor = Alpha(C(CityColor.UiPanel), .98f);
        Round(runModalPanel, 16); Border(runModalPanel, Alpha(accent, .65f), 2); Pad(runModalPanel, 22, 26);
        runModalPanel.style.alignItems = Align.Center;
        runModal.Add(runModalPanel);

        runModalLevel = Text("", 12, accent); runModalLevel.name = "run-modal-level";
        runModalPanel.Add(runModalLevel);
        runModalTitle = Text("CHOOSE YOUR EVOLUTION", 30, ink); runModalTitle.name = "run-modal-title"; runModalTitle.style.marginTop = 4;
        runModalPanel.Add(runModalTitle);
        runModalHint = Text("THE FIGHT IS PAUSED · ONE PICK ONLY", 11, muted); runModalHint.style.marginTop = 4; runModalHint.style.marginBottom = 18;
        runModalPanel.Add(runModalHint);

        runCardRow = Box("run-card-row", FlexDirection.Row); runCardRow.style.alignSelf = Align.Stretch;
        runModalPanel.Add(runCardRow);
        for (int i = 0; i < 3; i++) runCardRow.Add(BuildRunCard(i));

        var footer = Text("CLICK A CARD OR PRESS 1 / 2 / 3", 11, muted); footer.style.marginTop = 16;
        runModalPanel.Add(footer);
        OverlayRoot.Add(runModal);
    }

    VisualElement BuildRunCard(int index)
    {
        var card = Box("run-card-" + index);
        card.style.flexGrow = 1; card.style.flexBasis = 0; card.style.marginRight = index < 2 ? 16 : 0;
        card.style.backgroundColor = Alpha(C(CityColor.UiNavy), .9f); Round(card, 12);
        card.style.paddingTop = card.style.paddingBottom = 16; card.style.paddingLeft = card.style.paddingRight = 18;
        card.style.minHeight = 230; card.style.borderLeftWidth = 4;
        var button = new Button(() => ChooseRunCard(index)) { name = "run-card-button-" + index };
        button.style.position = Position.Absolute;
        button.style.left = button.style.right = button.style.top = button.style.bottom = 0;
        button.style.backgroundColor = new Color(0, 0, 0, 0); button.style.borderTopWidth = button.style.borderBottomWidth = 0;
        button.style.borderLeftWidth = button.style.borderRightWidth = 0; button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.text = "";
        card.Add(button);

        var rarity = Text("COMMON", 11, C(CityColor.UiMuted)); card.Add(rarity);
        var name = Text("", 19, C(CityColor.UiInk)); name.style.marginTop = 8; name.style.whiteSpace = WhiteSpace.Normal; card.Add(name);
        var target = Text("", 11, C(CityColor.Cyan)); target.style.marginTop = 3; card.Add(target);
        var desc = Text("", 13, C(CityColor.UiMuted), false); desc.style.marginTop = 12; desc.style.whiteSpace = WhiteSpace.Normal;
        desc.style.flexGrow = 1; card.Add(desc);
        var stacks = Text("", 11, C(CityColor.UiMuted)); stacks.style.marginTop = 12; card.Add(stacks);
        // The button under the text owns the click: labels are added on top, so without this a click on a title/description
        // would hit the label and never reach the card's Button.
        rarity.pickingMode = name.pickingMode = target.pickingMode = desc.pickingMode = stacks.pickingMode = PickingMode.Ignore;
        var key = Text(RunKeyLabels[index], 12, C(CityColor.UiInk)); key.style.marginTop = 8;
        key.style.backgroundColor = Alpha(C(CityColor.UiNavy), .95f); key.style.borderTopLeftRadius = key.style.borderTopRightRadius =
            key.style.borderBottomLeftRadius = key.style.borderBottomRightRadius = 6;
        key.style.paddingTop = 1; key.style.paddingBottom = 1; key.style.paddingLeft = 8; key.style.paddingRight = 8;
        key.style.alignSelf = Align.FlexStart; key.pickingMode = PickingMode.Ignore;
        card.Add(key);

        runCards.Add(card); runCardRarity.Add(rarity); runCardName.Add(name); runCardTarget.Add(target);
        runCardDesc.Add(desc); runCardStacks.Add(stacks); runCardKey.Add(key); runCardAccents.Add(card);
        return card;
    }

    /// The HUD is the only place that reads the level-up keys: it owns the modal, and while it is open the session is
    /// paused, so SuperHeroController's 1..9 power-slot shortcuts are already gated off.
    void Update() { UpdateRunModalInput(); }

    void ChooseRunCard(int index)
    {
        var run = WorldSession.Instance != null ? WorldSession.Instance.Run : null;
        if (run == null || !run.UpgradeOpen) return;
        run.Choose(index);
        runOfferStamp = -1;
        RefreshRunModal();
    }

    /// Called once per frame from GameHud.Update (HUD execution order is late, after input-driven gameplay code).
    void UpdateRunModalInput()
    {
        var run = WorldSession.Instance != null ? WorldSession.Instance.Run : null;
        BindRun(run);
        RefreshRunModal();
        if (run == null || !run.UpgradeOpen) return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) ChooseRunCard(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) ChooseRunCard(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) ChooseRunCard(2);
    }

    /// Shows/hides and repopulates the modal from the authoritative RunProgress offer. Cheap: only rebuilds labels when
    /// the offer stamp changes, so a paused frame does no allocations.
    void RefreshRunModal()
    {
        var run = WorldSession.Instance != null ? WorldSession.Instance.Run : null;
        bool open = run != null && run.UpgradeOpen && run.Offer.Count > 0;
        if (open != RunModalVisible)
        {
            RunModalVisible = open;
            Show(runModal, open);
            // RunProgress pauses through GameModeSession.SetPaused, which already unlocks the cursor for the choice.
            if (open) runOfferStamp = -1;
        }
        if (!open) { RunOfferCount = 0; return; }

        RunOfferCount = run.Offer.Count;
        RunModalLevelShown = run.Level;
        int stamp = run.Level * 100 + run.UpgradePicks;
        if (stamp == runOfferStamp) return;
        runOfferStamp = stamp;

        runModalLevel.text = $"RUN LEVEL {run.Level:00}   ·   {run.Xp} / {run.RequiredXp} XP";
        var ink = C(CityColor.UiInk); var muted = C(CityColor.UiMuted);
        for (int i = 0; i < runCards.Count; i++)
        {
            bool used = i < run.Offer.Count;
            Show(runCards[i], used);
            if (!used) continue;
            var def = run.Offer[i];
            Color accent = def.Rarity == RunRarity.Epic ? C(CityColor.Amber) : def.Rarity == RunRarity.Rare ? C(CityColor.Cyan) : C(CityColor.UiInk);
            runCardAccents[i].style.borderLeftColor = accent;
            runCardRarity[i].text = def.Rarity.ToString().ToUpperInvariant();
            runCardRarity[i].style.color = accent;
            runCardName[i].text = def.Name;
            runCardName[i].style.color = ink;
            runCardTarget[i].text = RunUpgradeTarget(def);
            runCardTarget[i].style.color = accent;
            runCardDesc[i].text = def.Description;
            runCardDesc[i].style.color = muted;
            int have = run.StacksOf(def.Id);
            runCardStacks[i].text = def.MaxStacks > 1 ? $"STACK {have} / {def.MaxStacks}" : "ONE PICK";
            runCardStacks[i].style.color = muted;
        }
    }

    /// Run XP is shown through the existing floating-popup pool (the same elements, merge rules and pooling as persistent
    /// XP), so no new UI object is created per kill and a multi-kill merges into one number.
    void BindRun(RunProgress run)
    {
        if (run == boundRun) return;
        if (boundRun != null) boundRun.XpGained -= OnRunXp;
        boundRun = run;
        if (boundRun != null) boundRun.XpGained += OnRunXp;
    }
    void UnbindRun() { if (boundRun != null) boundRun.XpGained -= OnRunXp; boundRun = null; }
    void OnRunXp(int amount, Vector3 where) { if (amount > 0) OnXpGranted(new XpGrant(amount, true, where, "run")); }

    /// "FIRE BLAST" etc. Resolved from the real power assets so a card can never mislabel its target.
    string RunUpgradeTarget(RunUpgradeDefinition def)
    {
        if (def.IsHero) return "SUPERHERO";
        if (def.IsSignature) return "SIGNATURE";
        var power = RunPowerName(def.PowerId);
        return power != null ? power.ToUpperInvariant() : def.PowerId.ToUpperInvariant();
    }

    static readonly Dictionary<string, string> runPowerNames = new Dictionary<string, string>();
    string RunPowerName(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (runPowerNames.Count == 0)
            foreach (var def in Resources.LoadAll<PowerDefinition>("Powers")) runPowerNames[def.Id] = def.DisplayName;
        return runPowerNames.TryGetValue(id, out var name) ? name : null;
    }
}
