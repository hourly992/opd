using System.Windows;

namespace Pereulki.Launcher.Controls;

public static class MotionSettings
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(MotionSettings), new FrameworkPropertyMetadata(
            true, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
}
