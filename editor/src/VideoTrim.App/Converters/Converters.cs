using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using VideoTrim.Core.ViewModels;

namespace VideoTrim.App.Converters;

/// <summary>true → Visible, false → Collapsed. Pass parameter "invert" to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Inverts a boolean.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not bool b || !b;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not bool b || !b;
}

/// <summary>Maps the UI-agnostic <see cref="InfoSeverity"/> to WinUI's <see cref="InfoBarSeverity"/>.</summary>
public sealed class InfoSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        InfoSeverity.Success => InfoBarSeverity.Success,
        InfoSeverity.Warning => InfoBarSeverity.Warning,
        InfoSeverity.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Binds an enum value to a RadioButton/ToggleButton via its name in ConverterParameter.</summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not null && parameter is string name &&
           value.ToString()!.Equals(name, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b && parameter is string name)
            return Enum.Parse(targetType, name);
        return DependencyProperty.UnsetValue;
    }
}
