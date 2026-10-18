using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class ModeScreens
{
    public readonly Dictionary<string,Button> DifficultyButtons=new Dictionary<string,Button>();
    public Label DifficultyDetails {get;private set;}
    void BuildDifficulty()
    {
        Root.styleSheets.Add(Resources.Load<StyleSheet>("DifficultyMenu"));
        var panel=new VisualElement { name="difficulty-selection" };panel.AddToClassList("difficulty-panel");Root.Add(panel);
        var row=new VisualElement();row.AddToClassList("difficulty-row");panel.Add(row);
        foreach(string id in new[]{"normal","hard","extreme"})
        {
            var d=DifficultyDefinition.Resolve(id);
            var button=new Button(()=>SelectDifficulty(d.Id)){name="difficulty-"+d.Id};
            button.AddToClassList("difficulty-button");button.AddToClassList("difficulty-"+d.Id);
            var title=new Label(d.DisplayName);title.AddToClassList("difficulty-title");button.Add(title);
            var subtitle=new Label(d.Subtitle);subtitle.AddToClassList("difficulty-subtitle");button.Add(subtitle);
            button.RegisterCallback<PointerEnterEvent>(_=>ShowDifficulty(d));
            button.RegisterCallback<FocusInEvent>(_=>ShowDifficulty(d));
            button.RegisterCallback<PointerLeaveEvent>(_=>ShowDifficulty(Profile.SelectedDifficulty));
            button.RegisterCallback<FocusOutEvent>(_=>ShowDifficulty(Profile.SelectedDifficulty));
            row.Add(button);DifficultyButtons.Add(d.Id,button);
        }
        DifficultyDetails=new Label();DifficultyDetails.AddToClassList("difficulty-details");panel.Add(DifficultyDetails);
        RefreshDifficulty();
    }
    public bool SelectDifficulty(string id)
    { if(!Profile.SetDifficulty(id))return false;RefreshDifficulty();return true; }
    void RefreshDifficulty()
    {
        foreach(var pair in DifficultyButtons)
        { bool selected=pair.Key==Profile.SelectedDifficulty.Id;pair.Value.EnableInClassList("difficulty-selected",selected);pair.Value.tooltip=(selected?"SELECTED · ":"")+new SessionDifficulty(DifficultyDefinition.Resolve(pair.Key)).Details; }
        ShowDifficulty(Profile.SelectedDifficulty);
    }
    void ShowDifficulty(DifficultyDefinition definition)
    { if(DifficultyDetails!=null)DifficultyDetails.text=definition.DisplayName+" · "+new SessionDifficulty(definition).Details; }
}
