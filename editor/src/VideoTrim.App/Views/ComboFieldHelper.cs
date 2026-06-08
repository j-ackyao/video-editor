using Microsoft.UI.Xaml.Controls;
using VideoTrim.Core.ViewModels;

namespace VideoTrim.App.Views;

/// <summary>
/// Helper for the editable "Same as source" combo fields. When the user picks the "Same as source"
/// item, an editable <see cref="ComboBox"/> would otherwise display the literal sentinel string;
/// this replaces it with the numeric source value and clears the selection so the box stays numeric.
/// </summary>
internal static class ComboFieldHelper
{
    public static void ResolveSameAsSourceSelection(object sender)
    {
        if (sender is not ComboBox combo || combo.Tag is not ComboFieldViewModel field)
            return;
        if (field.SameAsSourceLabel is null)
            return;

        if (combo.SelectedItem is string selected
            && string.Equals(selected, field.SameAsSourceLabel, StringComparison.OrdinalIgnoreCase)
            && field.SourceValueText is { } sourceText)
        {
            // Clear the selection so the ComboBox doesn't re-apply the sentinel text, then show the
            // numeric source value (which also flows back to the bound view-model Text).
            combo.SelectedItem = null;
            combo.Text = sourceText;
        }
    }
}
