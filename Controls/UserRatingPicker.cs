using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// Three buttons for the user's own rating of a card: G, R-18, R-18G.
/// </summary>
/// <remarks>
/// Shared by the rating page and the detail page so both offer the same three
/// choices in the same order. The checked state only ever mirrors
/// <see cref="Rating"/>; a click reports the choice and leaves it to the page
/// to store it and set <see cref="Rating"/> back.
/// </remarks>
internal sealed partial class UserRatingPicker : Grid
{
    private static readonly string[] Labels = ["G", "R-18", "R-18G"];
    private readonly ToggleButton[] _buttons;
    private int? _rating;

    public UserRatingPicker(bool large)
    {
        ColumnSpacing = large ? 12 : 8;
        _buttons = new ToggleButton[Labels.Length];
        for (var i = 0; i < Labels.Length; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var value = i;
            var button = new ToggleButton
            {
                Content = Labels[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = large ? 56 : 32,
                FontSize = large ? 20 : 14,
            };
            AutomationProperties.SetName(button, UiText.Format("UserRating_ButtonName", Labels[i]));
            AutomationProperties.SetAutomationId(button, $"UserRating{i}");
            button.Click += (_, _) =>
            {
                Apply();
                RatingChosen?.Invoke(value);
            };
            SetColumn(button, i);
            Children.Add(button);
            _buttons[i] = button;
        }
    }

    /// <summary>Raised with 0 (G), 1 (R-18) or 2 (R-18G) when a button is clicked.</summary>
    public event Action<int>? RatingChosen;

    public int? Rating
    {
        get => _rating;
        set
        {
            _rating = value;
            Apply();
        }
    }

    /// <summary>Grid is not a Control, so enabling goes through the buttons.</summary>
    public void SetEnabled(bool enabled)
    {
        foreach (var button in _buttons) button.IsEnabled = enabled;
    }

    /// <summary>Adds the 1/2/3 keys to the buttons, for rating many cards in a row.</summary>
    public void AddNumberKeys()
    {
        for (var i = 0; i < _buttons.Length; i++)
        {
            _buttons[i].KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.Number1 + i,
            });
        }
    }

    private void Apply()
    {
        for (var i = 0; i < _buttons.Length; i++)
            _buttons[i].IsChecked = _rating == i;
    }
}
