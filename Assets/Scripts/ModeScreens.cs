using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// Presentation only: mode selection belongs to GameFlow; purchases belong to PlayerProgression.
public sealed class ModeScreens : MonoBehaviour
{
    public PanelSettings Panel {get;private set;}
    public VisualElement Root {get;private set;}
    public PlayerProgression Profile {get;private set;}
    public readonly Dictionary<string,Button> ModeButtons=new Dictionary<string,Button>();
    public readonly List<Button> UpgradeButtons=new List<Button>();
    public Button ReplayButton {get;private set;}
    public Button HomeButton {get;private set;}
    public Label Headline {get;private set;}
    public float DisplayedXpFraction {get;private set;}
    public int DisplayedLevel {get;private set;}
    public bool RewardVisible {get;private set;}
    public bool MotionEnabled=true;
    public MenuMotes Motes {get;private set;}
    public int StatCount {get;private set;}
    CityPalette palette;
    MenuPresentationTuning tuning;
    GameTuning game;
    GameModeDefinition[] modes;
    PowerDefinition[] powers;
    SessionResult result;
    VisualElement xpFill,reward,choices;
    Label level,xpLabel,rewardLabel;
    float opened,popAt=-100;
    int lastLevel;
    bool rewardBuilt;
    sealed class CardMotion {public Button Button;public Color Accent;public float Weight;public bool Hover;}
    readonly List<CardMotion> cards=new List<CardMotion>();
    Color C(CityColor role)=>palette.Colors[(int)role];
    static Color Alpha(Color color,float alpha){color.a=alpha;return color;}
    public Color ColorFor(PlayerSide side)=>C(side==PlayerSide.Hero?CityColor.HeroAccent:CityColor.VillainAccent);

    void Awake()
    {
        palette=Resources.Load<CityPalette>("CityPalette");tuning=Resources.Load<MenuPresentationTuning>("MenuPresentationTuning");game=Resources.Load<GameTuning>("GameTuning");
        if(tuning==null)throw new InvalidOperationException("Run Overpowered/Create menu presentation data first.");
        modes=Resources.LoadAll<GameModeDefinition>("Modes").OrderBy(m=>m.MenuOrder).ToArray();powers=Resources.LoadAll<PowerDefinition>("Powers").OrderBy(p=>p.Id).ToArray();
        Profile=gameObject.AddComponent<PlayerProgression>();Profile.Initialize(game.Progression,powers,WorldSession.VerificationSavePath);
        result=SceneManager.GetActiveScene().name==GameFlow.ResultsScene?GameFlow.Instance.Result:null;
        Panel=ScriptableObject.CreateInstance<PanelSettings>();Panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("MenuTheme");Panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;Panel.referenceResolution=new Vector2Int(1440,900);Panel.screenMatchMode=PanelScreenMatchMode.MatchWidthOrHeight;Panel.match=0;
        var document=gameObject.AddComponent<UIDocument>();document.panelSettings=Panel;document.sortingOrder=10;
        Root=document.rootVisualElement;Root.AddToClassList("overpowered-menu");Root.style.flexGrow=1;Root.style.backgroundColor=C(CityColor.UiNavy);Root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");Root.style.color=C(CityColor.UiInk);
        var backdrop=GameFlow.Instance.GetComponent<MenuSkyline>()??GameFlow.Instance.gameObject.AddComponent<MenuSkyline>();
        var background=new VisualElement();Full(background);background.style.backgroundImage=Background.FromRenderTexture(backdrop.Get(tuning));background.style.backgroundSize=new BackgroundSize(BackgroundSizeType.Cover);Root.Add(background);
        var wash=new VisualElement();Full(wash);wash.style.backgroundColor=Alpha(C(CityColor.UiNavy),tuning.SkylineTint);Root.Add(wash);
        var shade=new VisualElement();Place(shade,0,0,100,23);shade.style.backgroundColor=Alpha(C(CityColor.UiNavy),.58f);Root.Add(shade);
        Motes=new MenuMotes(palette,tuning);Full(Motes);Root.Add(Motes);
        if(result==null)BuildHome();else BuildResults();opened=Time.unscaledTime;
    }
    Label Text(string text,int size,Color color,bool bold=false)
    {
        var label=new Label(text){pickingMode=PickingMode.Ignore};label.style.fontSize=size;label.style.color=color;label.style.unityFontStyleAndWeight=bold?FontStyle.Bold:FontStyle.Normal;label.style.marginTop=label.style.marginBottom=0;label.style.whiteSpace=WhiteSpace.Normal;return label;
    }
    static VisualElement Row(){var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;return row;}
    static void Full(VisualElement v){v.style.position=Position.Absolute;v.style.left=v.style.right=v.style.top=v.style.bottom=0;}
    static void Place(VisualElement v,float x,float y,float w,float h){v.style.position=Position.Absolute;v.style.left=Length.Percent(x);v.style.top=Length.Percent(y);v.style.width=Length.Percent(w);v.style.height=Length.Percent(h);}
    static void Round(VisualElement v,float r){v.style.borderTopLeftRadius=v.style.borderTopRightRadius=v.style.borderBottomLeftRadius=v.style.borderBottomRightRadius=r;}
    static void Border(VisualElement v,Color c,float width){v.style.borderTopColor=v.style.borderBottomColor=v.style.borderLeftColor=v.style.borderRightColor=c;v.style.borderTopWidth=v.style.borderBottomWidth=v.style.borderLeftWidth=v.style.borderRightWidth=width;}
    static void ResetButton(Button b){b.text="";b.style.marginLeft=b.style.marginRight=b.style.marginTop=b.style.marginBottom=0;b.style.paddingLeft=b.style.paddingRight=12;b.style.paddingTop=b.style.paddingBottom=0;b.style.justifyContent=Justify.Center;b.style.alignItems=Align.Center;b.focusable=true;}
    void PanelBox(VisualElement v){v.style.backgroundColor=Alpha(C(CityColor.UiPanel),.96f);Round(v,12);}
    Button ActionButton(string title,Action action,Color accent)
    {
        var button=new Button(action);ResetButton(button);button.Add(Text(title,16,C(CityColor.UiInk),true));PanelBox(button);Border(button,Alpha(accent,.8f),1);button.style.height=50;return button;
    }
    void BuildHome()
    {
        var eyebrow=Text("YOUR CITY. YOUR RULES.",13,C(CityColor.HeroAccent),true);Place(eyebrow,6,5,88,4);Root.Add(eyebrow);
        Headline=Text("OVERPOWERED",76,C(CityColor.UiInk),true);Headline.style.unityFontStyleAndWeight=FontStyle.BoldAndItalic;Place(Headline,5.6f,8,89,12);Root.Add(Headline);
        var choose=Text("CHOOSE YOUR SIDE",14,C(CityColor.UiMuted),true);Place(choose,6,21,88,4);Root.Add(choose);
        var row=Row();Place(row,6,27,88,45);Root.Add(row);
        var primary=new[]{modes.FirstOrDefault(m=>m.Playable&&m.Side==PlayerSide.Hero),modes.FirstOrDefault(m=>m.Playable&&m.Side==PlayerSide.Villain)}.Where(m=>m!=null).ToArray();
        foreach(var mode in primary){var card=ModeCard(mode);if(row.childCount>0)card.style.marginLeft=26;row.Add(card);}
        // Additional data-defined playable modes retain a route without changing the menu code.
        var extras=Row();Place(extras,6,74,88,4);Root.Add(extras);
        foreach(var mode in modes.Where(m=>m.Playable&&!primary.Contains(m))){var button=ActionButton(mode.DisplayName.ToUpperInvariant(),()=>Launch(mode),ColorFor(mode.Side));button.style.height=30;button.style.marginRight=12;extras.Add(button);ModeButtons.Add(mode.Id,button);}
        var strip=Row();Place(strip,6,81,88,14);PanelBox(strip);strip.style.alignItems=Align.Center;strip.style.paddingLeft=26;strip.style.paddingRight=20;Root.Add(strip);
        var levelBlock=new VisualElement();levelBlock.style.width=130;levelBlock.Add(Text("LEVEL",11,C(CityColor.UiMuted),true));levelBlock.Add(Text(Profile.Data.Level.ToString("00"),36,C(CityColor.UiInk),true));strip.Add(levelBlock);
        var powerBlock=new VisualElement();powerBlock.style.flexGrow=1;powerBlock.Add(Text("YOUR POWERS",11,C(CityColor.UiMuted),true));var icons=Row();icons.style.marginTop=8;powerBlock.Add(icons);strip.Add(powerBlock);
        foreach(var power in powers.Where(Profile.Owns)){var icon=new MenuIcon(power.MenuIcon,C(power.PaletteColor));icon.style.width=37;icon.style.height=37;icon.style.marginRight=13;icon.tooltip=power.DisplayName+" · tier "+Profile.Tier(power);icons.Add(icon);}
        foreach(var mode in modes.Where(m=>!m.Playable))
        {
            var button=new Button(()=>Launch(mode)){name="mode-"+mode.Id};ResetButton(button);button.style.width=172;button.style.height=64;button.style.marginLeft=12;button.style.backgroundColor=Alpha(C(CityColor.UiPurple),.55f);Round(button,9);
            button.Add(Text(mode.DisplayName.ToUpperInvariant(),12,C(CityColor.UiMuted),true));button.Add(Text("COMING SOON",10,C(CityColor.UiMuted)));button.SetEnabled(false);strip.Add(button);ModeButtons.Add(mode.Id,button);
        }
    }
    Button ModeCard(GameModeDefinition mode)
    {
        var accent=ColorFor(mode.Side);var glyph=mode.Side==PlayerSide.Hero?MenuGlyph.Shield:MenuGlyph.Flame;
        var button=new Button(()=>Launch(mode)){name="mode-"+mode.Id};ResetButton(button);button.style.flexGrow=1;button.style.flexBasis=0;button.style.height=Length.Percent(100);button.style.alignItems=Align.FlexStart;button.style.overflow=Overflow.Hidden;Round(button,18);Border(button,Alpha(accent,.65f),2);button.style.backgroundColor=Alpha(Color.Lerp(C(CityColor.UiPanel),accent,.13f),.96f);
        var ghost=new MenuIcon(glyph,Alpha(accent,.075f));ghost.style.position=Position.Absolute;ghost.style.width=360;ghost.style.height=360;ghost.style.right=-46;ghost.style.top=-38;button.Add(ghost);
        var tag=Text(mode.Side==PlayerSide.Hero?"01 / PROTECT":"02 / DISRUPT",12,accent,true);tag.style.position=Position.Absolute;tag.style.left=30;tag.style.top=23;button.Add(tag);
        var icon=new MenuIcon(glyph,accent);icon.style.position=Position.Absolute;icon.style.width=142;icon.style.height=142;icon.style.left=23;icon.style.top=62;button.Add(icon);
        var title=Text(mode.DisplayName.ToUpperInvariant(),36,C(CityColor.UiInk),true);title.style.position=Position.Absolute;title.style.left=32;title.style.bottom=65;button.Add(title);
        var tagline=Text(mode.Side==PlayerSide.Hero?"STOP CRIMES. SAVE CIVILIANS.":"CAUSE CHAOS. ESCAPE THE HEAT.",13,accent,true);tagline.style.position=Position.Absolute;tagline.style.left=34;tagline.style.bottom=37;button.Add(tagline);
        var play=Text("PLAY",12,accent,true);play.style.position=Position.Absolute;play.style.right=47;play.style.top=24;button.Add(play);
        var arrow=new MenuIcon(MenuGlyph.Chevron,accent);arrow.style.position=Position.Absolute;arrow.style.width=22;arrow.style.height=22;arrow.style.right=23;arrow.style.top=20;button.Add(arrow);
        var motion=new CardMotion{Button=button,Accent=accent};cards.Add(motion);button.RegisterCallback<PointerEnterEvent>(_=>motion.Hover=true);button.RegisterCallback<PointerLeaveEvent>(_=>motion.Hover=false);button.RegisterCallback<FocusInEvent>(_=>motion.Hover=true);button.RegisterCallback<FocusOutEvent>(_=>motion.Hover=false);ModeButtons.Add(mode.Id,button);return button;
    }
    void Launch(GameModeDefinition mode){GameFlow.Instance.Select(mode);}
    void BuildResults()
    {
        var accent=ColorFor(result.Side);bool hero=result.Side==PlayerSide.Hero,won=result.Outcome==SessionOutcome.Won;
        var kicker=Text(result.ModeName.ToUpperInvariant()+" / "+(won?"COMPLETE":"SESSION ENDED"),12,C(CityColor.UiMuted),true);Place(kicker,7,7,86,4);Root.Add(kicker);
        var icon=new MenuIcon(hero?MenuGlyph.Shield:MenuGlyph.Flame,accent);Place(icon,6,12,9,15);Root.Add(icon);
        Headline=Text(hero?(won?"CITY SAVED":"MISSION FAILED"):(won?"ESCAPED THE HEAT":"CAUGHT"),58,accent,true);Place(Headline,17,13,77,13);Root.Add(Headline);
        var stats=Row();Place(stats,7,31,86,11);Root.Add(stats);
        if(hero){Stat(stats,result.Successes.ToString(),"CRIMES STOPPED",accent);Stat(stats,result.Rescues.ToString(),"CIVILIANS SAVED",accent);Stat(stats,"+"+result.Xp,"XP EARNED",accent);Stat(stats,TimeSpan.FromSeconds(Mathf.Max(0,result.TimeLimit-result.Seconds)).ToString(@"mm\:ss"),"TIME LEFT",accent);}
        else{Stat(stats,result.Successes.ToString(),"HEISTS COMPLETED",accent);Stat(stats,result.Score.ToString(),"CHAOS SCORE",accent);Stat(stats,result.PeakHeat.ToString("0.0")+" / 5","PEAK HEAT",accent);Stat(stats,"+"+result.Xp,"XP EARNED",accent);}
        var xpPanel=new VisualElement();Place(xpPanel,7,47,86,11);PanelBox(xpPanel);Root.Add(xpPanel);
        level=Text("",20,C(CityColor.UiInk),true);level.style.position=Position.Absolute;level.style.left=22;level.style.top=13;xpPanel.Add(level);
        xpLabel=Text("",12,C(CityColor.UiMuted));xpLabel.style.position=Position.Absolute;xpLabel.style.right=22;xpLabel.style.top=18;xpPanel.Add(xpLabel);
        var track=new VisualElement();track.style.position=Position.Absolute;track.style.left=track.style.right=22;track.style.bottom=17;track.style.height=9;track.style.backgroundColor=C(CityColor.UiPurple);Round(track,5);xpPanel.Add(track);
        xpFill=new VisualElement(){name="earned-xp-fill"};xpFill.style.height=Length.Percent(100);xpFill.style.backgroundColor=accent;Round(xpFill,5);track.Add(xpFill);
        reward=new VisualElement();Place(reward,7,62,86,21);Root.Add(reward);rewardLabel=Text("",16,accent,true);reward.Add(rewardLabel);choices=Row();choices.style.flexGrow=1;choices.style.marginTop=12;reward.Add(choices);
        var actions=Row();Place(actions,7,88,86,7);actions.style.justifyContent=Justify.FlexEnd;Root.Add(actions);
        HomeButton=ActionButton("HOME",()=>GameFlow.Instance.Home(),C(CityColor.UiMuted));HomeButton.style.width=190;actions.Add(HomeButton);
        var definition=modes.FirstOrDefault(m=>m.Id==result.ModeId);ReplayButton=ActionButton("PLAY AGAIN",()=>GameFlow.Instance.Select(definition),accent);ReplayButton.style.width=245;ReplayButton.style.marginLeft=16;ReplayButton.SetEnabled(definition!=null&&definition.Playable);actions.Add(ReplayButton);
    }
    void Stat(VisualElement row,string value,string title,Color accent){var box=new VisualElement();box.style.flexGrow=1;box.style.flexBasis=0;box.style.marginRight=15;box.Add(Text(value,34,accent,true));box.Add(Text(title,11,C(CityColor.UiMuted),true));row.Add(box);StatCount++;}
    void ShowRewards()
    {
        rewardBuilt=true;choices.Clear();UpgradeButtons.Clear();RewardVisible=result.EndLevel>result.StartLevel&&Profile.Data.Points>0;
        if(!RewardVisible){rewardLabel.text=result.EndLevel>result.StartLevel?"POWER UPGRADED. MAKE IT COUNT.":"YOUR PROGRESS IS SAVED.";return;}
        rewardLabel.text="LEVEL UP / CHOOSE YOUR NEXT EDGE     "+Profile.Data.Points+" POINT(S)";
        var candidates=powers.Where(p=>Profile.Tier(p)<p.Upgrades.Length).OrderByDescending(Profile.Owns).ThenBy(p=>p.Id).Take(3).ToArray();
        if(candidates.Length==0){rewardLabel.text="ALL POWERS MAXED / POINTS SAVED";return;}
        var accent=ColorFor(result.Side);
        foreach(var power in candidates)
        {
            int tier=Profile.Tier(power),cost=tier<0?power.UnlockCost:power.Upgrades[tier].PointCost;
            var button=new Button(()=>ChooseUpgrade(power)){name="upgrade-"+power.Id};ResetButton(button);button.style.flexGrow=1;button.style.flexBasis=0;button.style.marginRight=16;PanelBox(button);Border(button,Alpha(accent,.65f),1);button.style.alignItems=Align.FlexStart;button.style.paddingLeft=18;button.style.paddingRight=62;
            var icon=new MenuIcon(power.MenuIcon,accent);icon.style.width=36;icon.style.height=36;icon.style.position=Position.Absolute;icon.style.right=14;icon.style.top=14;button.Add(icon);
            button.Add(Text((tier<0?"UNLOCK ":"UPGRADE ")+power.DisplayName.ToUpperInvariant(),13,C(CityColor.UiInk),true));var detail=Text(UpgradeLine(power,tier),11,C(CityColor.UiMuted));detail.style.marginTop=8;button.Add(detail);var price=Text(cost+" POINT"+(cost==1?"":"S"),10,accent,true);price.style.marginTop=9;button.Add(price);button.SetEnabled(Profile.Data.Points>=cost);choices.Add(button);UpgradeButtons.Add(button);
        }
    }
    string UpgradeLine(PowerDefinition power,int tier)
    {
        if(tier<0)return "ADD TO YOUR POWERS";var from=power.GetStats(tier);var to=power.GetStats(tier+1);
        if(power.Effect.IsFlight)return $"{from.Duration:0.#}s FLIGHT > {to.Duration:0.#}s";
        if(to.Force>from.Force)return $"{from.Force:0} FORCE > {to.Force:0}";
        return $"{from.Charges} CHARGES > {to.Charges}";
    }
    public bool ChooseUpgrade(PowerDefinition power)
    {
        if(result==null||result.EndLevel<=result.StartLevel||!RewardVisible||GameFlow.Instance.Loading)return false;
        bool bought=Profile.Buy(power);if(bought)ShowRewards();return bought;
    }
    int RequiredXp(int atLevel)=>Mathf.Max(1,Mathf.RoundToInt(game.Progression.BaseLevelXp*Mathf.Pow(game.Progression.LevelXpGrowth,atLevel-1)));
    void Update()
    {
        foreach(var card in cards)
        {
            card.Button.SetEnabled(!GameFlow.Instance.Loading);
            if(!MotionEnabled)continue;
            card.Weight=Mathf.Lerp(card.Weight,card.Hover?1:0,1-Mathf.Exp(-tuning.HoverResponse*Time.unscaledDeltaTime));card.Button.style.translate=new Translate(0,-card.Weight*tuning.HoverLift,0);card.Button.style.backgroundColor=Alpha(Color.Lerp(C(CityColor.UiPanel),card.Accent,.13f+.13f*card.Weight),.97f);Border(card.Button,Alpha(card.Accent,.65f+.35f*card.Weight),2);
        }
        if(MotionEnabled){Motes.Clock=Time.unscaledTime;Motes.MarkDirtyRepaint();}
        if(result==null)return;
        float t=Mathf.Clamp01((Time.unscaledTime-opened)/Mathf.Max(.01f,tuning.XpFillSeconds));float shownXp=result.StartXp+result.Xp*(1-Mathf.Pow(1-t,3));int shownLevel=Mathf.Max(1,result.StartLevel);
        while(shownXp>=RequiredXp(shownLevel)){shownXp-=RequiredXp(shownLevel);shownLevel++;}
        if(shownLevel!=lastLevel){if(lastLevel!=0)popAt=Time.unscaledTime;lastLevel=shownLevel;}
        DisplayedLevel=shownLevel;DisplayedXpFraction=shownXp/RequiredXp(shownLevel);xpFill.style.width=Length.Percent(DisplayedXpFraction*100);level.text="LEVEL "+shownLevel.ToString("00");xpLabel.text=$"{Mathf.FloorToInt(shownXp)} / {RequiredXp(shownLevel)} XP";
        float pop=Mathf.Sin(Mathf.Clamp01((Time.unscaledTime-popAt)/Mathf.Max(.01f,tuning.LevelPopSeconds))*Mathf.PI)*tuning.LevelPopScale;level.style.scale=new Scale(Vector3.one*(1+pop));
        if(t>=1&&!rewardBuilt)ShowRewards();HomeButton.SetEnabled(!GameFlow.Instance.Loading);ReplayButton.SetEnabled(!GameFlow.Instance.Loading&&modes.Any(m=>m.Id==result.ModeId&&m.Playable));
    }
    void OnDestroy(){if(Panel!=null)Destroy(Panel);}
}
