using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Converters;

public sealed class TargetMachineStateToBrushConverter : IValueConverter
{
    public static bool IsDarkTheme { get; set; }

    // Pinceis Tema Claro (Light Mode)
    private static readonly SolidColorBrush LightPendingBackground = CreateBrush("#FFF1F5F9");
    private static readonly SolidColorBrush LightPendingBorder = CreateBrush("#FFCBD5E1");
    private static readonly SolidColorBrush LightPendingForeground = CreateBrush("#FF475569");

    private static readonly SolidColorBrush LightActiveBackground = CreateBrush("#FFE5F2FF");
    private static readonly SolidColorBrush LightActiveBorder = CreateBrush("#FF7AB6F0");
    private static readonly SolidColorBrush LightActiveForeground = CreateBrush("#FF0C4A8A");

    private static readonly SolidColorBrush LightSuccessBackground = CreateBrush("#FFE7F6EC");
    private static readonly SolidColorBrush LightSuccessBorder = CreateBrush("#FF87C79B");
    private static readonly SolidColorBrush LightSuccessForeground = CreateBrush("#FF1F6B35");

    private static readonly SolidColorBrush LightWarningBackground = CreateBrush("#FFFFF4E5");
    private static readonly SolidColorBrush LightWarningBorder = CreateBrush("#FFE2BD7A");
    private static readonly SolidColorBrush LightWarningForeground = CreateBrush("#FF8A5A00");

    private static readonly SolidColorBrush LightErrorBackground = CreateBrush("#FFFCE8E8");
    private static readonly SolidColorBrush LightErrorBorder = CreateBrush("#FFE59A9A");
    private static readonly SolidColorBrush LightErrorForeground = CreateBrush("#FF8C1D1D");

    private static readonly SolidColorBrush LightRolledBackBackground = CreateBrush("#FFE0F2F1");
    private static readonly SolidColorBrush LightRolledBackBorder = CreateBrush("#FF4DB6AC");
    private static readonly SolidColorBrush LightRolledBackForeground = CreateBrush("#FF004D40");

    // Pinceis Tema Escuro (Dark Mode - Alto Contraste WCAG AAA e Elevação Zinc)
    private static readonly SolidColorBrush DarkPendingBackground = CreateBrush("#FF1C1C20");
    private static readonly SolidColorBrush DarkPendingBorder = CreateBrush("#FF323238");
    private static readonly SolidColorBrush DarkPendingForeground = CreateBrush("#FFA1A1AA");

    private static readonly SolidColorBrush DarkActiveBackground = CreateBrush("#FF0F2137");
    private static readonly SolidColorBrush DarkActiveBorder = CreateBrush("#FF1D4ED8");
    private static readonly SolidColorBrush DarkActiveForeground = CreateBrush("#FF60A5FA");

    private static readonly SolidColorBrush DarkSuccessBackground = CreateBrush("#FF0D2818");
    private static readonly SolidColorBrush DarkSuccessBorder = CreateBrush("#FF166534");
    private static readonly SolidColorBrush DarkSuccessForeground = CreateBrush("#FF4ADE80");

    private static readonly SolidColorBrush DarkWarningBackground = CreateBrush("#FF291C0E");
    private static readonly SolidColorBrush DarkWarningBorder = CreateBrush("#FF92400E");
    private static readonly SolidColorBrush DarkWarningForeground = CreateBrush("#FFFBBF24");

    private static readonly SolidColorBrush DarkErrorBackground = CreateBrush("#FF2D1214");
    private static readonly SolidColorBrush DarkErrorBorder = CreateBrush("#FF991B1B");
    private static readonly SolidColorBrush DarkErrorForeground = CreateBrush("#FFF87171");

    private static readonly SolidColorBrush DarkRolledBackBackground = CreateBrush("#FF0E2524");
    private static readonly SolidColorBrush DarkRolledBackBorder = CreateBrush("#FF115E59");
    private static readonly SolidColorBrush DarkRolledBackForeground = CreateBrush("#FF2DD4BF");

    private static bool ResolveIsDarkTheme()
    {
        try
        {
            return IsDarkTheme || Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;
        }
        catch
        {
            return IsDarkTheme;
        }
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is TargetMachineState s ? s : TargetMachineState.Pending;
        var role = parameter?.ToString();
        var isDark = ResolveIsDarkTheme();

        return role switch
        {
            "Border" => GetBorderBrush(state, isDark),
            "Foreground" => GetForegroundBrush(state, isDark),
            _ => GetBackgroundBrush(state, isDark)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush GetBackgroundBrush(TargetMachineState state, bool isDark)
    {
        if (isDark)
        {
            return state switch
            {
                TargetMachineState.CompletedSuccess => DarkSuccessBackground,
                TargetMachineState.SkippedAlreadyExists => DarkWarningBackground,
                TargetMachineState.AbortedDriverMissing => DarkErrorBackground,
                TargetMachineState.Error => DarkErrorBackground,
                TargetMachineState.ContactingRemote => DarkActiveBackground,
                TargetMachineState.ValidatingDriver => DarkActiveBackground,
                TargetMachineState.InstallingDriver => DarkActiveBackground,
                TargetMachineState.DriverInstalledReconfirming => DarkActiveBackground,
                TargetMachineState.Configuring => DarkActiveBackground,
                TargetMachineState.DeployCancelled => DarkWarningBackground,
                TargetMachineState.RollbackRemovingQueue => DarkActiveBackground,
                TargetMachineState.RollbackRemovingPort => DarkActiveBackground,
                TargetMachineState.RolledBack => DarkRolledBackBackground,
                _ => DarkPendingBackground
            };
        }

        return state switch
        {
            TargetMachineState.CompletedSuccess => LightSuccessBackground,
            TargetMachineState.SkippedAlreadyExists => LightWarningBackground,
            TargetMachineState.AbortedDriverMissing => LightErrorBackground,
            TargetMachineState.Error => LightErrorBackground,
            TargetMachineState.ContactingRemote => LightActiveBackground,
            TargetMachineState.ValidatingDriver => LightActiveBackground,
            TargetMachineState.InstallingDriver => LightActiveBackground,
            TargetMachineState.DriverInstalledReconfirming => LightActiveBackground,
            TargetMachineState.Configuring => LightActiveBackground,
            TargetMachineState.DeployCancelled => LightWarningBackground,
            TargetMachineState.RollbackRemovingQueue => LightActiveBackground,
            TargetMachineState.RollbackRemovingPort => LightActiveBackground,
            TargetMachineState.RolledBack => LightRolledBackBackground,
            _ => LightPendingBackground
        };
    }

    private static Brush GetBorderBrush(TargetMachineState state, bool isDark)
    {
        if (isDark)
        {
            return state switch
            {
                TargetMachineState.CompletedSuccess => DarkSuccessBorder,
                TargetMachineState.SkippedAlreadyExists => DarkWarningBorder,
                TargetMachineState.AbortedDriverMissing => DarkErrorBorder,
                TargetMachineState.Error => DarkErrorBorder,
                TargetMachineState.ContactingRemote => DarkActiveBorder,
                TargetMachineState.ValidatingDriver => DarkActiveBorder,
                TargetMachineState.InstallingDriver => DarkActiveBorder,
                TargetMachineState.DriverInstalledReconfirming => DarkActiveBorder,
                TargetMachineState.Configuring => DarkActiveBorder,
                TargetMachineState.DeployCancelled => DarkWarningBorder,
                TargetMachineState.RollbackRemovingQueue => DarkActiveBorder,
                TargetMachineState.RollbackRemovingPort => DarkActiveBorder,
                TargetMachineState.RolledBack => DarkRolledBackBorder,
                _ => DarkPendingBorder
            };
        }

        return state switch
        {
            TargetMachineState.CompletedSuccess => LightSuccessBorder,
            TargetMachineState.SkippedAlreadyExists => LightWarningBorder,
            TargetMachineState.AbortedDriverMissing => LightErrorBorder,
            TargetMachineState.Error => LightErrorBorder,
            TargetMachineState.ContactingRemote => LightActiveBorder,
            TargetMachineState.ValidatingDriver => LightActiveBorder,
            TargetMachineState.InstallingDriver => LightActiveBorder,
            TargetMachineState.DriverInstalledReconfirming => LightActiveBorder,
            TargetMachineState.Configuring => LightActiveBorder,
            TargetMachineState.DeployCancelled => LightWarningBorder,
            TargetMachineState.RollbackRemovingQueue => LightActiveBorder,
            TargetMachineState.RollbackRemovingPort => LightActiveBorder,
            TargetMachineState.RolledBack => LightRolledBackBorder,
            _ => LightPendingBorder
        };
    }

    private static Brush GetForegroundBrush(TargetMachineState state, bool isDark)
    {
        if (isDark)
        {
            return state switch
            {
                TargetMachineState.CompletedSuccess => DarkSuccessForeground,
                TargetMachineState.SkippedAlreadyExists => DarkWarningForeground,
                TargetMachineState.AbortedDriverMissing => DarkErrorForeground,
                TargetMachineState.Error => DarkErrorForeground,
                TargetMachineState.ContactingRemote => DarkActiveForeground,
                TargetMachineState.ValidatingDriver => DarkActiveForeground,
                TargetMachineState.InstallingDriver => DarkActiveForeground,
                TargetMachineState.DriverInstalledReconfirming => DarkActiveForeground,
                TargetMachineState.Configuring => DarkActiveForeground,
                TargetMachineState.DeployCancelled => DarkWarningForeground,
                TargetMachineState.RollbackRemovingQueue => DarkActiveForeground,
                TargetMachineState.RollbackRemovingPort => DarkActiveForeground,
                TargetMachineState.RolledBack => DarkRolledBackForeground,
                _ => DarkPendingForeground
            };
        }

        return state switch
        {
            TargetMachineState.CompletedSuccess => LightSuccessForeground,
            TargetMachineState.SkippedAlreadyExists => LightWarningForeground,
            TargetMachineState.AbortedDriverMissing => LightErrorForeground,
            TargetMachineState.Error => LightErrorForeground,
            TargetMachineState.ContactingRemote => LightActiveForeground,
            TargetMachineState.ValidatingDriver => LightActiveForeground,
            TargetMachineState.InstallingDriver => LightActiveForeground,
            TargetMachineState.DriverInstalledReconfirming => LightActiveForeground,
            TargetMachineState.Configuring => LightActiveForeground,
            TargetMachineState.DeployCancelled => LightWarningForeground,
            TargetMachineState.RollbackRemovingQueue => LightActiveForeground,
            TargetMachineState.RollbackRemovingPort => LightActiveForeground,
            TargetMachineState.RolledBack => LightRolledBackForeground,
            _ => LightPendingForeground
        };
    }

    private static SolidColorBrush CreateBrush(string colorHex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        brush.Freeze();
        return brush;
    }
}
