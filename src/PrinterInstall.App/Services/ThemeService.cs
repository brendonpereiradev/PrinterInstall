using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PrinterInstall.App.Services;

/// <summary>
/// Implementação do serviço de controle de temas visuais (Claro / Escuro) da aplicação.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private enum WindowThemeAttributeType
    {
        WTA_NONCLIENT = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTA_OPTIONS
    {
        public uint Flags;
        public uint Mask;
    }

    private const uint WTNCA_NODRAWCAPTION = 0x00000001;
    private const uint WTNCA_NODRAWICON = 0x00000002;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint FrameRefreshFlags = SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged;

    [DllImport("uxtheme.dll", PreserveSig = true)]
    private static extern int SetWindowThemeAttribute(
        IntPtr hWnd,
        WindowThemeAttributeType wtype,
        ref WTA_OPTIONS attributes,
        uint size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly Color DefaultAccentColor = Color.FromRgb(37, 99, 235);

    private readonly IAppSettingsStore _appSettingsStore;
    private ApplicationTheme _currentTheme;

    public ThemeService(IAppSettingsStore appSettingsStore)
    {
        _appSettingsStore = appSettingsStore;
        var savedTheme = _appSettingsStore.Load().Theme;
        _currentTheme = string.Equals(savedTheme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;
    }

    public ApplicationTheme CurrentTheme => _currentTheme;

    public bool IsDarkMode => _currentTheme == ApplicationTheme.Dark;

    public event EventHandler<ApplicationTheme>? ThemeChanged;

    /// <summary>
    /// Inicializa e aplica o tema salvo na inicialização do aplicativo.
    /// </summary>
    public void Initialize()
    {
        ApplyThemeToApplication(_currentTheme);
    }

    public void SetTheme(ApplicationTheme theme, bool persist = true)
    {
        _currentTheme = theme;
        ApplyThemeToApplication(theme);

        if (persist)
        {
            try
            {
                var settings = _appSettingsStore.Load();
                var updated = settings with { Theme = theme == ApplicationTheme.Dark ? "Dark" : "Light" };
                _appSettingsStore.Save(updated);
            }
            catch
            {
                // Falha de persistência não interrompe a alteração visual
            }
        }

        ThemeChanged?.Invoke(this, theme);
    }

    public void ToggleTheme()
    {
        var next = IsDarkMode ? ApplicationTheme.Light : ApplicationTheme.Dark;
        SetTheme(next, persist: true);
    }

    private static void ApplyThemeToApplication(ApplicationTheme theme)
    {
        Converters.TargetMachineStateToBrushConverter.IsDarkTheme = (theme == ApplicationTheme.Dark);

        try
        {
            if (Application.Current is null)
                return;

            void Action()
            {
                ApplicationThemeManager.Apply(theme, WindowBackdropType.None);

                var accent = theme == ApplicationTheme.Dark
                    ? Color.FromRgb(59, 130, 246)
                    : Color.FromRgb(37, 99, 235);

                ApplicationAccentColorManager.Apply(accent, theme);
                UpdateThemeTokens(theme);
                RefreshWindowTitlesAndFrames(theme);
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                Action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(Action);
            }
        }
        catch
        {
            // Em testes unitários sem runtime gráfico completo, ignora falha de aplicação de recurso de UI
        }
    }

    private static void UpdateThemeTokens(ApplicationTheme theme)
    {
        try
        {
            var targetSourceName = theme == ApplicationTheme.Dark
                ? "ThemeTokens.Dark.xaml"
                : "ThemeTokens.Light.xaml";

            var merged = Application.Current.Resources.MergedDictionaries;
            var existing = merged.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("ThemeTokens."));

            var newUri = new Uri($"pack://application:,,,/PrinterInstall.App;component/Themes/{targetSourceName}", UriKind.RelativeOrAbsolute);
            var newDict = new ResourceDictionary { Source = newUri };

            if (existing != null)
            {
                merged.Remove(existing);
            }

            // Garante que os tokens semânticos fiquem sempre no topo da cadeia de precedência
            merged.Add(newDict);
        }
        catch
        {
            // Fallback seguro
        }
    }

    private static void RefreshWindowTitlesAndFrames(ApplicationTheme theme)
    {
        try
        {
            void RefreshCore()
            {
                if (Application.Current?.Windows == null)
                    return;

                foreach (Window window in Application.Current.Windows)
                {
                    ApplyNativeWindowTheme(window, theme);
                    // Restaura explicitamente o pincel de canvas da janela, impedindo que o Wpf.Ui sobrescreva com cinza padrão
                    window.SetResourceReference(Control.BackgroundProperty, "AppCanvasBackgroundBrush");
                }
            }

            // Executa imediatamente e agenda um reforço em ApplicationIdle para quando o DWM terminar a transição
            RefreshCore();
            Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(RefreshCore));
        }
        catch
        {
            // Ignora falhas de P/Invoke de UI em ambientes sem desktop
        }
    }

    private static void ApplyNativeWindowTheme(Window window, ApplicationTheme theme)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
                return;

            // 1. Aplica o Immersive Dark Mode nativo do DWM (ou remove no modo claro)
            int isDark = theme == ApplicationTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref isDark, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref isDark, sizeof(int));

            // 2. Reverte a flag WTNCA_NODRAWCAPTION inserida pelo WPF-UI para restaurar a legenda/título da janela nativa
            var options = new WTA_OPTIONS
            {
                Flags = 0,
                Mask = WTNCA_NODRAWCAPTION | WTNCA_NODRAWICON
            };
            SetWindowThemeAttribute(hwnd, WindowThemeAttributeType.WTA_NONCLIENT, ref options, (uint)Marshal.SizeOf<WTA_OPTIONS>());

            // 3. Garante o título no HWND Win32
            var title = window.Title;
            if (!string.IsNullOrEmpty(title))
            {
                SetWindowText(hwnd, title);
            }

            // 4. Força o DWM a recalcular e repintar imediatamente a moldura não-cliente
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, FrameRefreshFlags);
        }
        catch
        {
            // Ignora falhas pontuais de P/Invoke
        }
    }
}
