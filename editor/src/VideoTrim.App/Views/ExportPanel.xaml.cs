using Microsoft.UI.Xaml.Controls;

namespace VideoTrim.App.Views;

public sealed partial class ExportPanel : UserControl
{
    public ExportPanel()
    {
        InitializeComponent();
    }

    private void OnSameAsSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
        => ComboFieldHelper.ResolveSameAsSourceSelection(sender);
}
