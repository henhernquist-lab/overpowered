using UnityEngine;
using UnityEngine.UIElements;

public sealed class CombatLabPanel : MonoBehaviour
{
    CombatLab lab; PanelSettings panel; VisualElement controls; Label status, feedback;Button toggle; bool open;int shownCount=-1;string shownFeedback;
    public void Initialize(CombatLab owner)
    {
        lab=owner;
        panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("MenuTheme");
        panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1600,900);panel.screenMatchMode=PanelScreenMatchMode.Expand;
        var doc=gameObject.AddComponent<UIDocument>();doc.panelSettings=panel;doc.sortingOrder=30;
        var root=doc.rootVisualElement;Resources.Load<VisualTreeAsset>("CombatLabPanel").CloneTree(root);
        root.style.position=Position.Absolute;root.style.left=root.style.right=root.style.top=root.style.bottom=0;
        root.pickingMode=PickingMode.Ignore;root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        controls=root.Q("lab-controls");status=root.Q<Label>("lab-status");feedback=root.Q<Label>("lab-feedback");
        toggle=root.Q<Button>("lab-toggle");toggle.clicked+=()=>Show(!open);
        var select=root.Q<DropdownField>("lab-enemy");select.choices=new System.Collections.Generic.List<string>();
        foreach(var a in lab.Settings.Archetypes)select.choices.Add(a.DisplayName);
        select.index=0;select.RegisterValueChangedCallback(_=>lab.Select(select.index));
        root.Q<Button>("lab-one").clicked+=()=>lab.SpawnOne();
        var group=root.Q<Button>("lab-group");group.text="Spawn group ("+Mathf.Clamp(lab.Settings.GroupSize,1,6)+")";group.clicked+=lab.SpawnGroup;
        root.Q<Button>("lab-clear").clicked+=lab.Clear;
        root.Q<Button>("lab-health").clicked+=()=>lab.World.RestorePracticeHealth();
        root.Q<Button>("lab-energy").clicked+=()=>lab.World.Powers.RestorePracticeEnergy();
        root.Q<Button>("lab-reset").clicked+=lab.ResetArena;
        root.Q<Button>("lab-exit").clicked+=lab.Exit;
        var difficulty=root.Q<DropdownField>("lab-difficulty");
        difficulty.choices=new System.Collections.Generic.List<string>{"normal","hard","extreme"};difficulty.value=lab.World.Difficulty.Id;
        difficulty.RegisterValueChangedCallback(e=>lab.ChangeDifficulty(e.newValue));
        Show(false);
    }
    void Show(bool visible){open=visible;controls.EnableInClassList("lab-hidden",!visible);lab.SetControls(visible);TimeArbiter.SetMenuPaused(visible);}
    void Update()
    {
        if(lab==null)return;
        if(Input.GetKeyDown(KeyCode.F2)||Input.GetKeyDown(KeyCode.Escape))Show(!open);
        int count=lab.ActiveCount;
        if(count!=shownCount)
        {
            shownCount=count;toggle.text=$"LAB · {lab.World.Difficulty.DisplayName} · {count}/{Mathf.Clamp(lab.Settings.MaxEnemies,1,24)} · F2";
            status.text=$"COMBAT LAB · {lab.World.Difficulty.DisplayName} · {count} enemies";
        }
        if(shownFeedback!=lab.Feedback){shownFeedback=lab.Feedback;feedback.text=shownFeedback;}
    }
    void OnDestroy(){TimeArbiter.Reset();if(panel!=null)Destroy(panel);}
}
